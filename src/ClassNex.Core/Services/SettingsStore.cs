using System.Text.Encodings.Web;
using System.Text.Json;
using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>Persists the portable Settings.json without retaining the caller's mutable settings.</summary>
public sealed class SettingsStore(string path) : ISettingsService
{
    private readonly string _path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options)
            ?? throw new JsonException("配置文件不能是 null。");
        settings.CentralManagement ??= new();
        settings.Notification ??= new();
        settings.Widgets ??= AppSettings.CreateDefaultWidgets();
        settings.TimeLayout ??= new();
        if (SettingsService.MigrateAccountEndpoint(settings) | SettingsService.MigrateCentralEndpoint(settings))
            Save(settings);
        return settings;
    }

    /// <inheritdoc />
    public void Save(AppSettings settings) => AtomicFile.Write(_path, JsonSerializer.Serialize(settings, Options));
}
