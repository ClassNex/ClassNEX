using ClassNex.WinUI;
using ClassNex.Services.NexIntelligence;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private static string ReadStatus(string key)
    {
        var account = AccountService.Current is { IsLoggedIn: true } a ? a : null;
        return key switch
        {
            AccountName => account is null ? "" : string.IsNullOrWhiteSpace(account.DisplayName) ? account.Name : account.DisplayName,
            AccountEmail => account?.Email ?? "", AccountAvatar => account is null ? "" : AccountService.AvatarImagePath ?? "",
            Schedule => AppServices.TimetableFileName,
            NativeSettingsKeys.Version => AppServices.ProductVersion,
            Voice => AppServices.NexVoice is { IsRunning: true } voice ? voice.EngineName : "未启用", _ => ""
        };
    }

    private void RunCommand(string key)
    {
        switch (key)
        {
            case EditOverlay:
                if (!_overlay.IsVisible) App.ToggleMainWindow();
                _overlay.EnterEditMode(); break;
            case PluginFolder: _ = OpenPluginsFolderAsync(); break;
            case string route when route.StartsWith("profile:", StringComparison.Ordinal) && int.TryParse(route.AsSpan(8), out var index) && index is >= -1 and <= 3:
                App.OpenProfileEditor(index); break;
            case "settings": OpenSettings(); break;
            case "widgets": OpenSettings("widget-layout"); break;
            case NativeSettingsKeys.Profile: case "profile": App.OpenProfileEditor(); break;
            case "toggle-overlay": App.ToggleMainWindow(); break;
            case "exit": App.ExitApp(); break;
            case Restart: App.RestartApplication(); break;
            case Login: App.OpenLoginWindow(); break;
            case SwitchIdentity: App.OpenAccountSwitch(); break;
            case Logout: AccountService.Logout(); break;
            case Oobe: App.ShowOobePreview(); break;
            case Chat: case "nex": App.OpenNexChat(); break;
            case Glow: AppServices.NexVoice?.ShowGlow(); break;
            case Speech: App.OpenWindowsSpeechSettings(); break;
            case "central.disconnect": AppServices.Management?.Disconnect(); _settings?.RefreshSettings(); break;
            case TestReminder: AppServices.NotificationService?.DebugTriggerTestNotification(); break;
            case Prepare: AppServices.NotificationService?.DebugTriggerClassPreparing(); break;
            case ClassOn: AppServices.NotificationService?.DebugTriggerClassOn(); break;
            case ClassOff: AppServices.NotificationService?.DebugTriggerClassOff(); break;
            default: OpenKnownLink(key); break;
        }
    }

    private async Task<string> ExecuteAsync(string key)
    {
        var s = AppServices.Settings;
        switch (key)
        {
            case PickTimetable:
                await PickTimetableAsync();
                return "";
            case CentralConnect when AppServices.Management is { } manager:
                await manager.ConnectAndSyncAsync();
                return s.CentralManagement.LastStatus;
            case NativeSettingsKeys.Models:
                return string.Join('\n', await NexModelCatalog.FetchAsync(NexConfig.ResolveBaseUrl(s), NexConfig.ResolveApiKey(s)));
            case TestNex:
                return await NexModelCatalog.TestAsync(NexConfig.ResolveBaseUrl(s), NexConfig.ResolveApiKey(s), NexConfig.ResolveModel(s));
            case Mic:
                return AppServices.NexVoice is { } voice ? await voice.TestMicrophoneAsync() : "语音唤醒未启用";
            default: throw new InvalidOperationException("该服务尚未就绪");
        }
    }

    private static void OpenKnownLink(string key)
    {
        var uri = key switch
        {
            Sound => "ms-settings:sound", Repo => "https://github.com/ClassNex/ClassNEX",
            Issues => "https://github.com/ClassNex/ClassNEX/issues", License => "https://github.com/ClassNex/ClassNEX/blob/main/LICENSE",
            ReleasePage => "https://github.com/ClassNex/ClassNEX/releases", _ => null
        };
        if (uri != null) _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }

    private static async Task OpenPluginsFolderAsync()
    {
        var directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory.FullName);
        await Windows.System.Launcher.LaunchFolderAsync(folder);
    }
}
