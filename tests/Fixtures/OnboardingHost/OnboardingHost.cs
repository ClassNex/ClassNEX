using ClassNex.Services;

namespace ClassNex.TestFixtures;

public static class OnboardingHost
{
    public static int Main(string[] args)
    {
        if (args is not [var settingsPath]) return 2;
        var store = new SettingsStore(settingsPath);
        if (!store.TryLoadForStartup(out var settings, out var owner)) { Console.WriteLine("busy"); return 0; }
        using (owner)
        {
            Console.WriteLine(owner is null ? "desktop" : "setup");
            if (Console.ReadLine() == "finish" && owner is not null)
            {
                settings.IsOobeCompleted = true;
                settings.Language = "en-US";
                store.Save(settings);
                Console.WriteLine("saved");
                Console.ReadLine();
            }
        }
        return 0;
    }
}
