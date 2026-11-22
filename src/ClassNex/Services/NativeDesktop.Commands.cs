using System.Reflection;
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
            NativeSettingsKeys.Version => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
            Voice => AppServices.NexVoice is { IsRunning: true } voice ? voice.EngineName : "未启用", _ => ""
        };
    }

    private void RunCommand(string key)
    {
        switch (key)
        {
            case "settings": OpenSettings(); break;
            case "widgets": OpenSettings("widgets"); break;
            case NativeSettingsKeys.Profile: case "profile": App.OpenProfileEditor(); break;
            case "toggle-overlay": App.ToggleMainWindow(); break;
            case "exit": App.ExitApp(); break;
            case Restart: App.RestartApplication(); break;
            case Login: App.OpenLoginWindow(); break;
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
            Issues => "https://github.com/ClassNex/ClassNEX/issues", License => "https://www.gnu.org/licenses/gpl-3.0.html", _ => null
        };
        if (uri != null) _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
    }
}
