using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Cryptography;
using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>Persists the portable Settings.json without retaining the caller's mutable settings.</summary>
public sealed class SettingsStore(string path) : ISettingsService
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly object _fileGate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public AppSettings Load()
    {
        lock (_fileGate)
        {
            var settings = ReadSnapshot(out var needsSave);
            if (needsSave) Save(settings);
            return settings;
        }
    }

    private AppSettings ReadSnapshot(out bool needsSave)
    {
        needsSave = false;
        if (!File.Exists(_path)) return new AppSettings();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return DeserializeSnapshot(input, out needsSave);
    }

    private static AppSettings DeserializeSnapshot(Stream input, out bool needsSave)
    {
        using var reader = new StreamReader(input, detectEncodingFromByteOrderMarks: true);
        var settings = JsonSerializer.Deserialize<AppSettings>(reader.ReadToEnd(), Options)
            ?? throw new JsonException("配置文件不能是 null。");
        settings.CentralManagement ??= new();
        settings.Notification ??= new();
        settings.TimeLayout ??= new();
        needsSave = SettingsService.MigrateAccountEndpoint(settings) | SettingsService.MigrateCentralEndpoint(settings) | settings.MigratedWidgetLayout;
        return settings;
    }

    /// <summary>读取启动配置。若同一数据目录已有未完成的首次设置，返回 false 且不写入；成功时调用方须持有首次设置句柄直至退出。已完成的设备不加锁。</summary>
    public bool TryLoadForStartup(out AppSettings settings, out IDisposable? firstRunLease)
    {
        lock (_fileGate)
        {
            firstRunLease = null;
            settings = ReadSnapshot(out var needsSave);
            FileStream? owner = null;
            try
            {
                if (!settings.IsOobeCompleted)
                {
                    var directory = Path.GetDirectoryName(_path)!;
                    Directory.CreateDirectory(directory);
                    try { owner = new FileStream(Path.Combine(directory, ".classnex-first-run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 || !OperatingSystem.IsWindows() && (ex.HResult & 0xffff) is 11 or 35) { }
                    settings = ReadSnapshot(out needsSave);
                    if (!settings.IsOobeCompleted && owner is null) return false;
                    if (settings.IsOobeCompleted) { owner?.Dispose(); owner = null; }
                }
                if (needsSave) Save(settings);
                firstRunLease = owner;
                return true;
            }
            catch { owner?.Dispose(); throw; }
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        lock (_fileGate) AtomicFile.Write(_path, JsonSerializer.Serialize(settings, Options));
    }

    internal (AppSettings Settings, byte[]? Hash) ReadImportBase(AppSettings fallback)
    {
        lock (_fileGate)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(_path); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.SerializeToUtf8Bytes(fallback, Options), Options)!;
                return (copy, null);
            }
            using var input = new MemoryStream(bytes, writable: false);
            var settings = DeserializeSnapshot(input, out _);
            if (settings.IsOobeCompleted != fallback.IsOobeCompleted)
                throw new IOException("本机的首次设置状态已变化。请重新启动 ClassNEX，原有设置未被覆盖。");
            return (settings, SHA256.HashData(bytes));
        }
    }

    private static byte[]? Fingerprint(string path)
    {
        if (!File.Exists(path)) return null;
        using var source = File.OpenRead(path);
        return SHA256.HashData(source);
    }

    internal AppSettings SaveImport(AppSettings settings, byte[]? before)
    {
        lock (_fileGate)
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid().ToString("N");
            var draft = Path.Combine(directory, $".import-{id}.tmp");
            var backup = Path.Combine(directory, $".before-import-{id}.tmp");
            var existed = before is not null;
            var committed = false;
            var verified = false;
            try
            {
                var json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(draft, json);
                var latest = Fingerprint(_path);
                if ((before == null) != (latest == null) || before != null && !CryptographicOperations.FixedTimeEquals(before, latest!))
                    throw new IOException("迁移期间本地设置已更新，请重新尝试，以免覆盖新设置。");
                if (existed) File.Replace(draft, _path, backup); else File.Move(draft, _path);
                committed = true;
                var saved = File.ReadAllText(_path);
                if (saved != json) throw new IOException("设置回读不一致。");
                var readback = JsonSerializer.Deserialize<AppSettings>(saved, Options) ?? throw new IOException("无法验证迁入的设置。");
                verified = true;
                return readback;
            }
            catch
            {
                if (committed) { if (existed) File.Replace(backup, _path, null); else File.Delete(_path); }
                throw;
            }
            finally
            {
                try { File.Delete(draft); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (verified && existed)
                    try { File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
