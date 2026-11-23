namespace ClassNex.WinUI;

public static class NativeSettingsKeys
{
    public const string AccountName = "account.name", AccountEmail = "account.email", AccountAvatar = "account.avatar",
        Schedule = "schedule", Version = "version", Voice = "voice";
    public const string Login = "account.login", SwitchIdentity = "account.switch", Logout = "account.logout", Profile = "profile.open", Oobe = "oobe.preview",
        Chat = "nex.chat", Glow = "nex.glow", Speech = "system.speech", Sound = "system.sound",
        Repo = "about.repo", Issues = "about.issues", License = "about.license", Privacy = "about.privacy",
        TestReminder = "notify.test", Prepare = "notify.prepare", ClassOn = "notify.on", ClassOff = "notify.off", AfterSchool = "notify.after-school";
    public const string CentralConnect = "central.connect", Models = "nex.models", TestNex = "nex.test", Mic = "nex.mic";
    public const string Restart = "app.restart", PickTimetable = "profile.pick";
    public const string PluginFolder = "plugins.folder", ReleasePage = "updates.releases", EditOverlay = "overlay.edit";
    public const string PickReminderSound = "notify.sound.pick", ResetReminderSound = "notify.sound.reset", ReminderSoundSource = "notify.sound.source";
    public static string ProfileTab(int tab) => $"profile:{tab}";
}
