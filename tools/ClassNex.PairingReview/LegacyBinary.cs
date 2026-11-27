using System.Net;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;

internal static partial class Program
{
    private static int DescribeLegacyPdb(string file)
    {
        var project = Path.GetFileNameWithoutExtension(file);
        Check(project is "ClassNex.Central" or "ClassNex.Core" && Path.GetExtension(file) == ".pdb", "Expected the saved Central or Core symbols");
        using var stream = File.OpenRead(file);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader(); var documents = new Dictionary<string, string>();
        var binary = Path.ChangeExtension(file, ".dll");
        using var assembly = File.OpenRead(binary); using var pe = new PEReader(assembly);
        var codeView = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
        var id = new BlobContentId(reader.DebugMetadataHeader!.Id);
        Check(pe.ReadCodeViewDebugDirectoryData(codeView).Guid == id.Guid && codeView.Stamp == id.Stamp, "Symbols do not belong to this binary");
        var marker = "/src/" + project + "/";
        foreach (var handle in reader.Documents)
        {
            var d = reader.GetDocument(handle); var name = reader.GetString(d.Name).Replace('\\','/');
            var at = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || name.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
            Check(reader.GetGuid(d.HashAlgorithm) == new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), "Expected a SHA-256 document checksum");
            documents[name[(at + 1)..]] = Convert.ToHexStringLower(reader.GetBlobBytes(d.Hash));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { productVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(binary).ProductVersion, documents })); return 0;
    }

    private sealed class LegacyTraffic(Uri host) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        public int Assignments, FullPublishes, ConditionalPublishes, CleanReports, InitialPayloads, PendingReports, PendingFullReports, PendingDraftReports;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        {
            var source = message.RequestUri!;
            Check(source.GetLeftPart(UriPartial.Authority) + "/Control" == AppSettings.DefaultCentralEndpoint && source.AbsolutePath.StartsWith("/Control/api/v1/", StringComparison.Ordinal), "Unexpected desktop destination");
            var path = source.AbsolutePath;
            message.RequestUri = new Uri(host, source.PathAndQuery);
            if (path.EndsWith("/client/publish", StringComparison.Ordinal)) Interlocked.Increment(ref FullPublishes);
            if (path.EndsWith("/client/changes", StringComparison.Ordinal)) Interlocked.Increment(ref ConditionalPublishes);
            var report = path.EndsWith("/client/report", StringComparison.Ordinal);
            var pendingReport = report && DraftFiles().Length != 0;
            if (report)
            {
                var body = (await message.Content!.ReadFromJsonAsync<JsonObject>(token))!;
                var settings = (string?)body["settingsJson"];
                var timetable = (string?)body["timetableYaml"];
                Check(settings?.Contains("private-local-fixture") != true, "An outbound heartbeat contains a private setting");
                var hasPayload = !string.IsNullOrEmpty(settings) || !string.IsNullOrEmpty(timetable);
                if (pendingReport && hasPayload)
                {
                    Interlocked.Increment(ref PendingFullReports);
                    if ((!string.IsNullOrEmpty(settings) && (double?)JsonNode.Parse(settings)?["fontScale"] == 1.35) || timetable?.Contains("生物") == true) Interlocked.Increment(ref PendingDraftReports);
                }
                else if (hasPayload)
                {
                    Check(!string.IsNullOrEmpty(settings) && (double?)JsonNode.Parse(settings)?["fontScale"] == 1.25 && timetable?.Contains("数学") == true, "The blank classroom was not seeded with both of its original resources");
                    Interlocked.Increment(ref InitialPayloads);
                }
            }
            var reply = await base.SendAsync(message, token);
            if (report && reply.IsSuccessStatusCode)
            {
                if (pendingReport) Interlocked.Increment(ref PendingReports);
                else Interlocked.Increment(ref CleanReports);
            }
            if (path.EndsWith("/client/assignment", StringComparison.Ordinal) && reply.StatusCode == HttpStatusCode.OK)
            {
                var body = JsonNode.Parse(await reply.Content.ReadAsStringAsync(token))!.AsObject();
                Check(!body.ContainsKey("supportsResourceEdits"), "This binary advertises the new protocol; this is not a legacy run");
                Interlocked.Increment(ref Assignments);
            }
            return reply;
        }
    }

    private static string[] DraftFiles()
    {
        var folder = Path.Combine(SettingsService.DataDirectory, "CentralDrafts");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.pending.json") : [];
    }

    private static async Task VerifyLegacyBinary(Uri host, string mode, bool reopen)
    {
        var localSetting = mode.StartsWith("old-settings", StringComparison.Ordinal);
        static void Stage(object data) { Console.WriteLine(JsonSerializer.Serialize(data)); Console.Out.Flush(); }
        static async Task Until(Func<bool> ok, string why, int seconds = 30)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            while (!ok() && !limit.IsCancellationRequested) await Task.Delay(65);
            Check(ok(), why);
        }

        if (!reopen)
        {
            SettingsService.EnsureDataDirectory();
            File.WriteAllText(SettingsService.TimetableStoragePath, "version: 1\nsubjects:\n  - name: 数学\nschedules: []\n");
            SettingsService.Save(new AppSettings { IsOobeCompleted = true, FontScale = 1.25, ThemeMode = "system", IsNexVoiceWakeEnabled = false, NexApiKey = "private-local-fixture" });
        }
        AppServices.Initialize(); Check(AccountService.Current == null, "Desktop must not use a personal account");
        var transport = new LegacyTraffic(host);
        using var desktop = new CentralManagementService(new(transport));
        var onSyncBoundary = !reopen && mode == "old-settings-boundary";
        var savedAtBoundary = 0;
        if (onSyncBoundary) desktop.StatusChanged += status =>
        {
            if (status.StartsWith("已同步 ·", StringComparison.Ordinal) && AppServices.Settings.CentralManagement.AppliedRevision > 0 && Interlocked.Exchange(ref savedAtBoundary, 1) == 0)
                Dispatcher.UIThread.Invoke(() => { AppServices.Settings.FontScale = 1.35; AppServices.SaveSettings(); });
        };
        desktop.Start();
        if (!reopen)
        {
            await desktop.RequestPairingAsync();
            var code = AppServices.Settings.CentralManagement.PairingCode;
            Check(code.Length == 9, "The old binary did not issue a pairing code");
            Stage(new { stage = "pair", code }); // only the isolated browser receives this through a pipe
            await Until(() => AppServices.Settings.CentralManagement.AppliedRevision > 0, "Old server did not seed the paired class", 55);
            if (onSyncBoundary)
                await Until(() => Volatile.Read(ref savedAtBoundary) == 1, "The boundary edit did not run");
            else
            {
                await Until(() => transport.CleanReports >= 2, "Initial class seed and acknowledgement did not finish");
                if (localSetting) { AppServices.Settings.FontScale = 1.35; AppServices.SaveSettings(); }
                else { AppServices.Schedule.Profile.Subjects[0].Name = "生物"; AppServices.Schedule.Save(); }
            }
            await Until(() => AppServices.Settings.CentralManagement.LastStatus.Contains("尚不支持分项同步") && DraftFiles().Length == 1, "Old service did not preserve the pending edit");
            Stage(new { stage = "pending" });
        }
        await Until(() => transport.Assignments > 0 && AppServices.Settings.CentralManagement.LastStatus.Contains("尚不支持分项同步") &&
            (localSetting ? AppServices.Settings.FontScale == 1.35 && AppServices.Schedule.Profile.Subjects.Any(s => s.Name == "地理") :
            AppServices.Settings.ThemeMode == "dark" && AppServices.Schedule.Profile.Subjects.Any(s => s.Name == "生物")), "The unrelated portal edit or the local draft did not survive");
        var files = DraftFiles(); Check(files.Length == 1, "Wrong number of pending resources");
        var pending = JsonNode.Parse(await File.ReadAllTextAsync(files[0]))!;
        var content = pending["Value"]!.ToString();
        Check((bool?)pending["Timetable"] == !localSetting && (localSetting ? (double?)JsonNode.Parse(content)?["fontScale"] == 1.35 : content.Contains("生物")) &&
            !content.Contains("private-local-fixture"), "The persisted draft is incorrect or contains a local credential");
        await Until(() => transport.PendingReports > 0, "Did not observe a completed presence report while a draft was pending", 42);
        Check(transport.FullPublishes == 0 && transport.ConditionalPublishes == 0 && transport.PendingFullReports == 0,
            $"Unexpected old-protocol write in {mode}: publishes={transport.FullPublishes}, changes={transport.ConditionalPublishes}, fullReportsWhilePending={transport.PendingFullReports}, reportsContainingDraft={transport.PendingDraftReports}");
        Check(transport.InitialPayloads == (reopen ? 0 : 1), "A full report was sent outside the single initial seed");
        Stage(new { stage = "verified", newProcess = reopen, transport.Assignments, transport.CleanReports, transport.InitialPayloads, transport.FullPublishes, transport.ConditionalPublishes, transport.PendingReports, transport.PendingFullReports, transport.PendingDraftReports, pendingResources = files.Length });
    }
}
