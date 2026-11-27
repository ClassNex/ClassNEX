using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;

internal static class Program
{
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

    [STAThread]
    private static int Main(string[] args)
    {
        var root = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, ".."));
        var parent = root.Parent;
        Check(args.Length == 2 && root.Name == "paired-desktop" && parent?.Name.StartsWith("classnex-central-test-", StringComparison.Ordinal) == true &&
            string.Equals(parent.Parent?.FullName, Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(SettingsService.DataDirectory) && new[] { root, parent, new DirectoryInfo(AppContext.BaseDirectory) }.All(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)), "Requires a new disposable pairing fixture");
        var host = Local(args[0]); var issuer = Local(args[1]);
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        using var stop = new CancellationTokenSource();
        var exit = 1;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Verify(host, issuer); exit = 0; }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        return exit;
    }

    private static Uri Local(string address)
    {
        var uri = new Uri(address);
        Check(uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0, "Loopback HTTP origins only");
        return uri;
    }

    private sealed class FixtureTransport(Uri local) : DelegatingHandler(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var uri = request.RequestUri!;
            Check(uri.GetLeftPart(UriPartial.Authority) + "/Control" == AppSettings.DefaultCentralEndpoint && uri.AbsolutePath.StartsWith("/Control/api/v1/", StringComparison.Ordinal), "Client did not use the built-in control route");
            request.RequestUri = new Uri(local, uri.PathAndQuery["/Control".Length..]);
            return base.SendAsync(request, cancellation);
        }
    }

    private static async Task Verify(Uri host, Uri issuer)
    {
        const string yaml = "version: 1\nsubjects:\n  - name: Physics\nschedules: []\n";
        SettingsService.EnsureDataDirectory();
        File.WriteAllText(SettingsService.TimetableStoragePath, yaml);
        SettingsService.Save(new AppSettings { IsOobeCompleted = true, ThemeMode = "system", FontScale = 1.25, IsNexVoiceWakeEnabled = false, NexApiKey = "local-only-fixture" });
        AppServices.Initialize();
        CentralManagementService Start() { var service = new CentralManagementService(new(new FixtureTransport(host))); service.Start(); return service; }
        static async Task Until(Func<bool> condition, string reason)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!condition() && !limit.IsCancellationRequested) await Task.Delay(70);
            Check(condition(), reason);
        }
        using var www = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = host, Timeout = TimeSpan.FromSeconds(5) };
        using var login = await www.GetAsync("auth/login");
        var authorize = login.Headers.Location!; Check(login.StatusCode == HttpStatusCode.Redirect && authorize.GetLeftPart(UriPartial.Authority) == issuer.GetLeftPart(UriPartial.Authority), "Unexpected fixture issuer");
        using var redirect = await www.GetAsync(authorize); var callback = redirect.Headers.Location!;
        Check(redirect.StatusCode == HttpStatusCode.Redirect && callback.GetLeftPart(UriPartial.Authority) == host.GetLeftPart(UriPartial.Authority) && callback.AbsolutePath == "/Control/auth/callback", "Unexpected fixture callback");
        using var completed = await www.GetAsync("auth/callback" + callback.Query); Check(completed.StatusCode == HttpStatusCode.Redirect, "Browser fixture login failed");
        var secretPath = Path.Combine(SettingsService.DataDirectory, "central-device.bin");
        using (var first = Start())
        {
            await first.RequestPairingAsync(); var cancelled = AppServices.Settings.CentralManagement.PairingCode;
            await first.DisconnectAsync();
            using var unused = await www.PostAsJsonAsync("api/v1/pairing/claim", new { code = cancelled });
            Check(!File.Exists(secretPath) && unused.StatusCode == HttpStatusCode.NotFound, "Cancelling retained a device grant");
            await first.RequestPairingAsync();
        }
        var pair = AppServices.Settings.CentralManagement.PairingCode;
        Check(AccountService.Current == null && pair.Length == 9 && File.Exists(secretPath), "Anonymous client did not receive a code");
        var encrypted = await File.ReadAllBytesAsync(secretPath);
        Check(!Encoding.UTF8.GetString(encrypted).Contains(pair) && !Encoding.Unicode.GetString(encrypted).Contains(pair) && !File.ReadAllText(SettingsService.SettingsPath).Contains(pair), "Code leaked into unprotected settings");
        AppServices.Initialize();
        using var active = Start();
        await Until(() => AppServices.Settings.CentralManagement.PairingCode == pair, "Pending binding was not recovered after reopening");
        using var claim = await www.PostAsJsonAsync("api/v1/pairing/claim", new { code = pair }); claim.EnsureSuccessStatusCode();
        var link = (await claim.Content.ReadFromJsonAsync<JsonObject>())!;
        var classroom = "api/v1/classes/" + (string)link["classId"]!;
        using var emptyForm = await www.PutAsJsonAsync(classroom + "/timetable", new { subjects = Array.Empty<object>(), schedules = Array.Empty<object>() }); emptyForm.EnsureSuccessStatusCode();
        await Until(() => AppServices.Settings.CentralManagement.AppliedRevision > 0, "Bound client did not initialize its class");
        var seeded = await www.GetFromJsonAsync<JsonObject>(classroom);
        Check(seeded!["timetableYaml"]!.ToString().Contains("Physics") && !seeded["settingsJson"]!.ToString().Contains("local-only-fixture"), "Initial sync replaced the local timetable or shared a local key");
        using var remote = await www.PutAsJsonAsync(classroom + "/settings", new { settingsJson = "{\"themeMode\":\"dark\"}" }); remote.EnsureSuccessStatusCode();
        await Until(() => AppServices.Settings.ThemeMode == "dark", "Web settings did not reach the device automatically");
        Check(AppServices.Settings.FontScale == 1.25 && File.ReadAllText(SettingsService.TimetableStoragePath).Contains("Physics"), "A partial web edit reset unrelated preferences");
        AppServices.Settings.ThemeMode = "light"; AppServices.SaveSettings();
        AppServices.Schedule.Profile.Subjects[0].Name = "Chemistry"; AppServices.Schedule.Save();
        var expected = AppServices.Settings.CentralManagement.AppliedRevision;
        await Until(() => AppServices.Settings.CentralManagement.AppliedRevision > expected, "Local edits did not publish automatically");
        var published = (await www.GetFromJsonAsync<JsonObject>(classroom))!;
        Check(JsonNode.Parse(published["settingsJson"]!.ToString())!["themeMode"]!.ToString() == "light" && published["timetableYaml"]!.ToString().Contains("Chemistry"), "Local settings and timetable missing from paired class");
        using var removed = await www.DeleteAsync("api/v1/devices/" + (string)link["clientId"]!); removed.EnsureSuccessStatusCode();
        await Until(() => !AppServices.Settings.CentralManagement.IsEnabled, "Owner revocation did not stop automatic sync");
        AppServices.Settings.ThemeMode = "system"; AppServices.SaveSettings();
        Check(!File.Exists(secretPath) && SettingsService.Load().ThemeMode == "system" && AccountService.Current == null, "Revocation did not preserve local editing");
        Console.WriteLine("PASS: real desktop service, built-in URL and no account, device cancellation, Windows-protected restart, initial timetable, automatic two-way edits, owner revoke and independent local settings");
    }
}
