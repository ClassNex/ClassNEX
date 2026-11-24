using Avalonia.Animation.Easings;
using Windows.UI.ViewManagement;

namespace ClassNex.Styles;

internal static class DesktopMotion
{
    private static readonly UISettings SystemPreferences = new();
    internal static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(167);
    internal static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(125);
    internal static readonly Easing EnterEase = Easing.Parse("0,0,0,1");
    internal static readonly Easing ExitEase = Easing.Parse("0.7,0,1,0.5");

#if DEBUG
    private static readonly bool ReducedForReview = Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_REDUCED_MOTION") == "1"
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review"));
#endif
    internal static bool IsEnabled =>
#if DEBUG
        !ReducedForReview &&
#endif
        SystemPreferences.AnimationsEnabled;
}
