using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;

internal static partial class Program
{
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--legacy-pdb", var pdb]) return DescribeLegacyPdb(pdb);
        var legacy = args is [_, _, "old-settings" or "old-timetable" or "old-settings-boundary" or "old-settings-reopen" or "old-timetable-reopen" or "old-settings-boundary-reopen"] ? args[2] : null;
        var reopen = legacy?.EndsWith("-reopen", StringComparison.Ordinal) == true;
        var root = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, ".."));
        var parent = root.Parent;
        Check((args.Length == 2 || legacy != null) && root.Name == "paired-desktop" && parent?.Name.StartsWith("classnex-central-test-", StringComparison.Ordinal) == true &&
            string.Equals(parent.Parent?.FullName, Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
            new[] { root, parent, new DirectoryInfo(AppContext.BaseDirectory) }.All(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)), "Requires a disposable pairing fixture");
        Check(reopen ? new DirectoryInfo(SettingsService.DataDirectory) is { Exists: true } d && !d.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
            File.ReadAllText(Path.Combine(root.FullName, ".legacy-binary-fixture")) == legacy![..^7] : !Directory.Exists(SettingsService.DataDirectory), "Requires a new fixture or its own verified previous phase");
        var host = Local(args[0]); var issuer = Local(args[1]);
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        using var stop = new CancellationTokenSource();
        var exit = 1;
        Dispatcher.UIThread.Post(async () =>
        {
            try { if (legacy == null) await Verify(host, issuer); else await VerifyLegacyBinary(host, legacy, reopen); exit = 0; }
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

    private sealed class FixtureWrites
    {
        private TaskCompletionSource? _entered, _release;
        public volatile bool Legacy;
        public int UnsafePublishes;

        public Task Pause()
        {
            _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _release, new(TaskCreationOptions.RunContinuationsAsynchronously));
            return _entered.Task.WaitAsync(TimeSpan.FromSeconds(12));
        }

        public async Task EnterAsync(CancellationToken cancellation)
        {
            if (Volatile.Read(ref _release) is not { } pending) return;
            _entered!.TrySetResult();
            await pending.Task.WaitAsync(cancellation);
        }

        public void Resume() => Interlocked.Exchange(ref _release, null)?.TrySetResult();
    }

    private sealed class FixtureTransport(Uri local, FixtureWrites writes) : DelegatingHandler(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var uri = request.RequestUri!;
            Check(uri.GetLeftPart(UriPartial.Authority) + "/Control" == AppSettings.DefaultCentralEndpoint && uri.AbsolutePath.StartsWith("/Control/api/v1/", StringComparison.Ordinal), "Client did not use the built-in control route");
            request.RequestUri = new Uri(local, uri.PathAndQuery["/Control".Length..]);
            if (uri.AbsolutePath.EndsWith("/client/changes", StringComparison.Ordinal)) await writes.EnterAsync(cancellation);
            if (uri.AbsolutePath.EndsWith("/client/publish", StringComparison.Ordinal)) Interlocked.Increment(ref writes.UnsafePublishes);
            var response = await base.SendAsync(request, cancellation);
            if (writes.Legacy && response.StatusCode == HttpStatusCode.OK && uri.AbsolutePath.EndsWith("/client/assignment", StringComparison.Ordinal))
            {
                var config = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellation))!;
                config.Remove("supportsResourceEdits");
                response.Content.Dispose(); response.Content = JsonContent.Create(config);
            }
            return response;
        }
    }

    private static async Task Verify(Uri host, Uri issuer)
    {
        const string yaml = "version: 1\nsubjects:\n  - name: Physics\nschedules: []\n";
        SettingsService.EnsureDataDirectory();
        File.WriteAllText(SettingsService.TimetableStoragePath, yaml);
        SettingsService.Save(new AppSettings { IsOobeCompleted = true, ThemeMode = "system", FontScale = 1.25, IsNexVoiceWakeEnabled = false, NexApiKey = "local-only-fixture" });
        AppServices.Initialize();
        var writes = new FixtureWrites();
        CentralManagementService Start() { var service = new CentralManagementService(new(new FixtureTransport(host, writes))); service.Start(); return service; }
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
        using var completed = await www.GetAsync(callback); Check(completed.StatusCode == HttpStatusCode.Redirect, "Browser fixture login failed");
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
        async Task WebPut(string resource, long baseline, object payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, classroom + "/" + resource) { Content = JsonContent.Create(payload) };
            request.Headers.IfMatch.Add(new($"\"{baseline}\""));
            using var response = await www.SendAsync(request); response.EnsureSuccessStatusCode();
        }

        var localWrite = writes.Pause();
        AppServices.Settings.ThemeMode = "dark"; AppServices.SaveSettings();
        await localWrite;
        await WebPut("timetable", AppServices.Settings.CentralManagement.AppliedRevision,
            new { subjects = new[] { new { name = "Geography" } }, schedules = Array.Empty<object>() });
        writes.Resume();
        await Until(() => AppServices.Schedule.Profile.Subjects.Any(s => s.Name == "Geography"), "A simultaneous classroom setting prevented the web timetable from reaching the desktop");
        var merged = (await www.GetFromJsonAsync<JsonObject>(classroom))!;
        Check(AppServices.Settings.ThemeMode == "dark" && JsonNode.Parse(merged["settingsJson"]!.ToString())!["themeMode"]!.ToString() == "dark" && merged["timetableYaml"]!.ToString().Contains("Geography"), "A cross-resource edit overwrote the other resource");

        localWrite = writes.Pause();
        AppServices.Settings.FontScale = 1.4; AppServices.SaveSettings();
        await localWrite;
        await WebPut("settings", AppServices.Settings.CentralManagement.AppliedRevision, new { settingsJson = "{\"themeMode\":\"light\",\"fontScale\":1.6}" });
        writes.Resume();
        await Until(() => AppServices.Settings.FontScale == 1.6 && AppServices.Settings.CentralManagement.LastConflictDraft != null, "An overlapping edit did not preserve the remote winner and a classroom draft");
        var drafts = Path.Combine(SettingsService.DataDirectory, "CentralDrafts");
        var archived = await File.ReadAllTextAsync(Path.Combine(drafts, AppServices.Settings.CentralManagement.LastConflictDraft!));
        Check((double?)JsonNode.Parse(archived)?["fontScale"] == 1.4 && !archived.Contains("local-only-fixture") && AppServices.Settings.CentralManagement.LastStatus.Contains("CentralDrafts"), "The local conflict draft is incomplete or shares private preferences");

        var binding = AppServices.Settings.CentralManagement;
        var savedName = binding.LastConflictDraft!;
        foreach (var invalid in new[] { "../Settings.json", ((savedName[0] == '0') ? "1" : "0") + savedName[1..] })
        {
            binding.LastConflictDraft = invalid;
            var rejected = false;
            try { await active.RestoreLastConflictAsync(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && AppServices.Settings.FontScale == 1.6, "A draft from outside this device and class was accepted");
        }
        binding.LastConflictDraft = savedName;
        var cloudBeforeRestore = binding.AppliedRevision;
        await active.RestoreLastConflictAsync();
        await Until(() => AppServices.Settings.CentralManagement.AppliedRevision > cloudBeforeRestore && AppServices.Settings.FontScale == 1.4, "Explicit conflict restore did not publish a guarded edit");
        Check(AppServices.Settings.CentralManagement.LastConflictDraft == null && File.Exists(Path.Combine(drafts, savedName)), "The original conflict archive disappeared after restoration");

        writes.Legacy = true;
        AppServices.Settings.FontScale = 1.35; AppServices.SaveSettings();
        await Until(() => AppServices.Settings.CentralManagement.LastStatus.Contains("尚不支持分项同步"), "An older server did not show protected pending edits");
        Check(Directory.GetFiles(drafts, "*.pending.json").Length == 1 && writes.UnsafePublishes == 0, "An older server received an unguarded snapshot or lost the draft");
        using var legacyCourse = await www.PutAsJsonAsync(classroom + "/timetable", new { subjects = new[] { new { name = "Mathematics" } }, schedules = Array.Empty<object>() });
        legacyCourse.EnsureSuccessStatusCode();
        await Until(() => AppServices.Schedule.Profile.Subjects.Any(s => s.Name == "Mathematics"), "A pending setting blocked incoming timetables from older services");
        Check(AppServices.Settings.FontScale == 1.35 && Directory.GetFiles(drafts, "*.pending.json").Length == 1, "A legacy downlink touched the pending classroom setting");
        active.Dispose(); AppServices.Initialize(); writes.Legacy = false;
        using var resumed = Start();
        var beforeResume = AppServices.Settings.CentralManagement.AppliedRevision;
        await Until(() => AppServices.Settings.CentralManagement.AppliedRevision > beforeResume && Directory.GetFiles(drafts, "*.pending.json").Length == 0, "An unsent edit was lost across a service restart");
        var restored = (await www.GetFromJsonAsync<JsonObject>(classroom))!;
        Check((double?)JsonNode.Parse(restored["settingsJson"]!.ToString())?["fontScale"] == 1.35 && AppServices.Settings.FontScale == 1.35 && restored["timetableYaml"]!.ToString().Contains("Mathematics") && writes.UnsafePublishes == 0, "Resumed pending edit did not reach the class");
        writes.Legacy = true;
        var secondProfile = Path.Combine(SettingsService.DataDirectory, "second-class-fixture.yaml");
        await File.WriteAllTextAsync(secondProfile, yaml.Replace("Physics", "Biology", StringComparison.Ordinal));
        AppServices.LoadTimetable(secondProfile);
        await Until(() => AppServices.Settings.CentralManagement.LastStatus.Contains("尚不支持分项同步"), "Older service did not preserve pending timetable");
        using var legacyAppearance = await www.PutAsJsonAsync(classroom + "/settings", new { settingsJson = "{\"themeMode\":\"system\"}" }); legacyAppearance.EnsureSuccessStatusCode();
        await Until(() => AppServices.Settings.ThemeMode == "system", "A pending timetable blocked incoming settings from older services");
        Check(AppServices.Schedule.Profile.Subjects.Any(s => s.Name == "Biology") && Directory.GetFiles(drafts, "*.pending.json").Length == 1 && writes.UnsafePublishes == 0, "Downstream legacy settings erased the pending timetable");
        using var removed = await www.DeleteAsync("api/v1/devices/" + (string)link["clientId"]!); removed.EnsureSuccessStatusCode();
        await Until(() => !AppServices.Settings.CentralManagement.IsEnabled, "Owner revocation did not stop automatic sync");
        AppServices.Settings.ThemeMode = "light"; AppServices.SaveSettings();
        var revokedDraft = AppServices.Settings.CentralManagement.LastConflictDraft;
        Check(!File.Exists(secretPath) && SettingsService.Load().ThemeMode == "light" && AccountService.Current == null && revokedDraft != null &&
            File.ReadAllText(Path.Combine(drafts, revokedDraft)).Contains("Biology"), "Revocation did not preserve local editing and an accessible offline timetable");
        Console.WriteLine("PASS: anonymous desktop pairing, automatic edits, independent concurrent changes, overlapping drafts, safe legacy negotiation, restart recovery, owner revoke and independent local settings");
    }
}
