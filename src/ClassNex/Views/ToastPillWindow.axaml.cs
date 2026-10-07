using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using ClassNex.Services;

namespace ClassNex.Views;

/// <summary>
/// ClassWidgets 的「灵动通知」（对照 <c>E:\ClassWidgets\tip_toast.py</c> 的 tip_toast +
/// <c>view/widget-toast-bar.ui</c>）：
/// 顶部居中胶囊条，盖在悬浮组件行的位置（CW：toast 宽度 = 组件行总宽、位置 = 屏幕居中、顶距 = 边距），
/// 放大出现（750ms OutCirc）+ 淡入（450ms InOutQuad）→ 停留 2000ms → 缩小淡出（500ms InOutQuad）。
/// 颜色对照 CW data/default_config.json：上课 DD986F / 下课 46B878 / 预备 7065D8 / 其它 6EBED2，
/// 渐变 = [原色×1.24, 原色, 原色×0.89]（CW generate_gradient_color：adjust 0 / +0.24 / -0.11）。
/// </summary>
public partial class ToastPillWindow : Window
{
    /// <summary>CW tip_toast 的 state：0=下课 1=上课 2=放学 3=预备铃 4=其它通知。</summary>
    public enum PillState
    {
        FinishClass = 0,
        AttendClass = 1,
        AfterSchool = 2,
        PrepareClass = 3,
        Custom = 4,
    }

    /// <summary>各状态底色（CW default_config.json Color 段；下标 = PillState）。</summary>
    private static readonly string[] StateColors = { "#46B878", "#DD986F", "#46B878", "#7065D8", "#6EBED2" };

    private int _durationMs = 2000;

    public ToastPillWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
    }

    public void ShowPill(PillState state, string lessonName, string? title = null,
        string? subtitle = null, string? content = null, int durationMs = 2000)
    {
        _durationMs = durationMs;

        // 位置/尺寸 = 浮窗岛的屏幕矩形（CW：toast 盖在组件行上，宽度等于组件行总宽）
        if (AppServices.MainWindow?.GetIslandScreenRect() is { } rect)
        {
            Width = Math.Max(1, rect.Width);
            Height = Math.Max(1, rect.Height);
            Position = rect.Position;
        }

        // CW tip_toast 各状态文案
        string titleText, subtitleText, lessonText, glyph;
        switch (state)
        {
            case PillState.AttendClass:
                titleText = "活动开始";
                subtitleText = "当前课程";
                lessonText = lessonName;
                glyph = "\uE8AB";
                break;
            case PillState.FinishClass:
                titleText = "下课";
                subtitleText = lessonName.Length > 0 ? "即将进行" : "";
                lessonText = lessonName;
                glyph = "\uE7C4";
                break;
            case PillState.AfterSchool:
                titleText = "放学";
                subtitleText = "当前课程已结束";
                lessonText = "";
                glyph = "\uE9D9";
                break;
            case PillState.PrepareClass:
                titleText = "即将开始";
                subtitleText = "下一节";
                lessonText = lessonName;
                glyph = "\uE823";
                break;
            default:
                titleText = title ?? "通知";
                subtitleText = subtitle ?? "";
                lessonText = content ?? "";
                glyph = "\uE916";
                break;
        }

        TitleText.Text = titleText;
        SubtitleText.Text = subtitleText;
        SubtitleText.IsVisible = subtitleText.Length > 0;
        LessonText.Text = lessonText;
        LessonText.IsVisible = lessonText.Length > 0;
        PillIcon.Glyph = glyph;
        Root.Background = BuildGradient(StateColors[(int)state]);
        Show();
        ApplyTransparentToolWindow();
        PlayIn();
    }

    private async void PlayIn()
    {
        var visual = ElementComposition.GetElementVisual(Root);
        if (visual is null)
            return;

        var compositor = visual.Compositor;

        // CW：geometry_animation 750ms OutCirc，从中央小矩形放大到完整大小
        visual.CenterPoint = new Vector3D(visual.Size.X / 2, visual.Size.Y / 2, 0);
        visual.Scale = new Vector3D(0.15, 0.15, 0.15);
        var scale = compositor.CreateVector3DKeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3D(1, 1, 1), new CircularEaseOut());
        scale.Duration = TimeSpan.FromMilliseconds(750);
        visual.StartAnimation(nameof(visual.Scale), scale);

        // CW：opacity_animation 450ms InOutQuad
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, new QuadraticEaseInOut());
        fade.Duration = TimeSpan.FromMilliseconds(450);
        visual.StartAnimation(nameof(visual.Opacity), fade);

        await Task.Delay(_durationMs);
        PlayOut();
    }

    private async void PlayOut()
    {
        var visual = ElementComposition.GetElementVisual(Root);
        if (visual is null)
        {
            Hide();
            return;
        }

        var compositor = visual.Compositor;

        // CW close：500ms InOutQuad，缩回中央小矩形
        var scale = compositor.CreateVector3DKeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3D(0.15, 0.15, 0.15), new QuadraticEaseInOut());
        scale.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation(nameof(visual.Scale), scale);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f, new QuadraticEaseInOut());
        fade.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation(nameof(visual.Opacity), fade);

        await Task.Delay(500);
        Hide();
    }

    private void ApplyTransparentToolWindow()
    {
        if (TryGetPlatformHandle() is { } handle)
        {
            var hwnd = handle.Handle;
            WindowsOverlay.SetClickThrough(hwnd, true);
            WindowsOverlay.SetToolWindow(hwnd, true);
        }
    }

    /// <summary>CW generate_gradient_color：亮 = 原色×(1+0.24)，暗 = 原色×(1-0.11)，停靠 0 / 0.5 / 1。</summary>
    private static IBrush BuildGradient(string hex)
    {
        var baseColor = Color.Parse(hex);
        var bright = Scale(baseColor, 0.24);
        var dark = Scale(baseColor, -0.11);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(bright, 0),
                new GradientStop(baseColor, 0.5),
                new GradientStop(dark, 1),
            },
        };
    }

    private static Color Scale(Color c, double factor) => Color.FromRgb(
        (byte)Math.Clamp((int)(c.R * (1 + factor)), 0, 255),
        (byte)Math.Clamp((int)(c.G * (1 + factor)), 0, 255),
        (byte)Math.Clamp((int)(c.B * (1 + factor)), 0, 255));
}
