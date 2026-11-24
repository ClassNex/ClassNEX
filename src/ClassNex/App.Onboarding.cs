using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private void ShowSetup(IClassicDesktopStyleApplicationLifetime desktop, OobeMode mode)
    {
        var guide = new OobeWindow(mode);
        _oobeWindow = guide;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        desktop.MainWindow = guide;
        guide.SetupCompleted += () =>
        {
            // The old HWND owns both the first-run tray icon and its menu.
            RemoveTray();
            try
            {
                ApplyTheme(AppServices.Settings.ThemeMode);
                StartClassroom(desktop);
                _oobeWindow = null;
            }
            catch
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.MainWindow = guide;
                StopClassroom();
                _mainWindow?.Close();
                _mainWindow = null;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                InstallTray(guide);
                throw;
            }
        };
        guide.Show();
#if DEBUG
        if (desktop.Args is ["--review-tray"]) Dispatcher.UIThread.Post(() => _ = ReviewTrayAsync(desktop));
        else
#endif
            InstallTray(guide);
    }

    internal static bool IsPreviewInstallation()
    {
#if DEBUG
        try
        {
            var app = new DirectoryInfo(AppContext.BaseDirectory);
            if (app.Name != "app" || app.Parent is not { Parent.Name: "build" } fixture) return false;
            var prefix = $"preview-{AppServices.ProductVersion}-";
            var manual = fixture.Name.StartsWith(prefix, StringComparison.Ordinal) && fixture.Name.Length == prefix.Length + 8 &&
                fixture.Name[prefix.Length..].All(char.IsAsciiHexDigit);
            const string reviewPrefix = "oobe-review-";
            var scripted = fixture.Name.StartsWith(reviewPrefix, StringComparison.Ordinal) && Guid.TryParseExact(fixture.Name[reviewPrefix.Length..], "N", out _);
            if (!manual && !scripted) return false;
            var marker = new FileInfo(Path.Combine(fixture.FullName, ".classnex-review"));
            const string signature = "isolated migration preview";
            if (!marker.Exists || marker.Length != signature.Length || marker.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                File.ReadAllText(marker.FullName) != signature) return false;
            var data = new DirectoryInfo(SettingsService.DataDirectory);
            if (!data.Exists || !string.Equals(data.Parent?.FullName, fixture.FullName, StringComparison.OrdinalIgnoreCase) ||
                data.Attributes.HasFlag(FileAttributes.ReparsePoint) || File.GetAttributes(SettingsService.SettingsPath).HasFlag(FileAttributes.ReparsePoint)) return false;
            for (DirectoryInfo? part = app; part != null; part = part.Parent)
                if (part.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
#else
        return false;
#endif
    }

#if DEBUG
    private static bool IsIsolatedMigrationPreview()
    {
        var s = AppServices.Settings;
        return IsPreviewInstallation() && s.IsOobeCompleted && s.IsMainWindowVisible && !s.IsNexIntelligenceEnabled && !s.IsNexVoiceWakeEnabled && !s.IsNexEdgeBarEnabled &&
            !s.RunAtStartup && !s.RegisterUrlProtocol && !s.CentralManagement.IsEnabled && string.IsNullOrEmpty(s.TimetablePath) &&
            string.IsNullOrEmpty(s.UserName) && string.IsNullOrEmpty(s.Email) && AccountService.Current is null;
    }
#endif
}
