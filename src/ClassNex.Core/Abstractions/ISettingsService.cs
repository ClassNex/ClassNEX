using ClassNex.Models;

namespace ClassNex.Core.Abstractions;

/// <summary>Loads and atomically saves the configuration for one portable installation.</summary>
public interface ISettingsService
{
    /// <summary>Returns defaults when no file exists. Invalid JSON and I/O errors are reported to the caller.</summary>
    AppSettings Load();

    /// <summary>Replaces the settings file only when serialization and writing succeed.</summary>
    void Save(AppSettings settings);
}
