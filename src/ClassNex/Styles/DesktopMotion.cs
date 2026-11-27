using Avalonia.Animation.Easings;
using Windows.UI.ViewManagement;

namespace ClassNex.Styles;

internal static class DesktopMotion
{
    private static readonly UISettings SystemPreferences = new();
    internal static event Action? Changed;
    static DesktopMotion() => SystemPreferences.AnimationsEnabledChanged += (_, _) => Changed?.Invoke();
    internal static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(167);
    internal static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(125);
    internal static readonly Easing EnterEase = Easing.Parse("0,0,0,1");
    internal static readonly Easing ExitEase = Easing.Parse("0.7,0,1,0.5");

#if DEBUG
    private static readonly bool IsReviewDirectory = File.Exists(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review"));
    private static readonly bool ReducedForReview = IsReviewDirectory && Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_REDUCED_MOTION") == "1";
    private static bool? _reviewEnabled;

    internal static void SetReviewEnabled(bool? enabled)
    {
        if (!IsReviewDirectory) throw new InvalidOperationException("Motion review requires an isolated review directory");
        var previous = IsEnabled;
        _reviewEnabled = enabled;
        if (previous != IsEnabled) Changed?.Invoke();
    }

    internal static bool IsEnabled => _reviewEnabled ?? (!ReducedForReview && SystemPreferences.AnimationsEnabled);
#else
    internal static bool IsEnabled => SystemPreferences.AnimationsEnabled;
#endif
}
