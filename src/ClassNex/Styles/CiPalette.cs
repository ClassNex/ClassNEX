using Avalonia.Media;

namespace ClassNex.Styles;

/// <summary>
/// CI（ClassIsland）配色常量 —— 全部取自 CI 本体，不在本地自创。
///
/// 数据来源（本机 CI 2.1.0.1 安装目录）：
///   data/Settings.json                          → PrimaryColor / SecondaryColor / BackgroundColor
///   data/Config/ComponentLayouts/Default.json    → 主界面组件默认外观（黑底 / 50% 透明 / 圆角 8）
///   app-2.1.0.1-0/ClassIsland.dll                → 内嵌色值 #00BFFF #333333 #48000000 #66000000 #F4EF74 #FFFFFF
///   app-2.1.0.1-0/ColorHelper.dll                → HSL 色轮（同饱和度同明度、只变色相）
/// </summary>
public static class CiPalette
{
    // ---------- CI 本体色值 ----------

    /// <summary>CI 主色（Settings.json: PrimaryColor）。</summary>
    public static readonly Color Primary = Color.Parse("#00BFFF");

    /// <summary>CI 副色（Settings.json: SecondaryColor）。</summary>
    public static readonly Color Secondary = Color.Parse("#7FFFD4");

    /// <summary>CI 组件默认前景色（ComponentLayouts: ForegroundColor）。</summary>
    public static readonly Color ComponentForeground = Color.Parse("#1E90FF");

    /// <summary>CI 主界面卡片底色（ComponentLayouts: BackgroundColor）。</summary>
    public static readonly Color CardBackground = Colors.Black;

    /// <summary>CI 中性深色（ClassIsland.dll: #333333）。</summary>
    public static readonly Color NeutralDark = Color.Parse("#333333");

    /// <summary>CI 轻微叠加层（ClassIsland.dll: #48000000，黑 28%）。</summary>
    public static readonly Color OverlaySubtle = Color.Parse("#48000000");

    /// <summary>CI 中等叠加层（ClassIsland.dll: #66000000，黑 40%）。</summary>
    public static readonly Color OverlayMedium = Color.Parse("#66000000");

    /// <summary>CI 高亮 / 提醒色（ClassIsland.dll: #F4EF74）。</summary>
    public static readonly Color Highlight = Color.Parse("#F4EF74");

    // ---------- CI 主界面外观参数 ----------

    /// <summary>CI 主界面卡片不透明度（ComponentLayouts: BackgroundOpacity）。</summary>
    public const double CardOpacity = 0.5;

    /// <summary>CI 主界面圆角（ComponentLayouts: CustomCornerRadius）。</summary>
    public const double CardCornerRadius = 8;

    // ---------- CI 主色的 HSL（由 #00BFFF 算出：H=195° S=100% L=50%）----------

    /// <summary>CI 主色色相（度）。</summary>
    public const double AccentHue = 195.0;

    /// <summary>CI 主色饱和度。</summary>
    public const double AccentSaturation = 1.0;

    /// <summary>CI 主色明度。</summary>
    public const double AccentLightness = 0.5;

    // ---------- 画笔 ----------

    /// <summary>CI 主色画刷。</summary>
    public static readonly IBrush PrimaryBrush = new SolidColorBrush(Primary);

    /// <summary>CI 主界面卡片画刷（黑 @ 50%）。</summary>
    public static readonly IBrush CardBrush =
        new SolidColorBrush(CardBackground, CardOpacity);

    /// <summary>主界面文字画刷（CI 组件默认白色前景）。</summary>
    public static readonly IBrush OnCardBrush = Brushes.White;

    /// <summary>主界面次要文字画刷。</summary>
    public static IBrush OnCardSecondary(double opacity = 0.82) =>
        new SolidColorBrush(Colors.White, opacity);

    // ---------- 科目色 ----------

    /// <summary>
    /// CI 色带下界：CI 副色 #7FFFD4 的色相（≈160°，青绿）。
    /// CI 的 ColorHelper 提供的是受限色轮（BlueGreenColorWheel / BlueVioletColorWheel），
    /// 并非全色环；此处据此把科目色统一收在 CI 的冷色带内。
    /// </summary>
    public const double HueBandStart = 160.0;

    /// <summary>CI 色带上界：由 CI 主色 #00BFFF（H=195°）向靛蓝延伸，仍在同一冷色系。</summary>
    public const double HueBandEnd = 260.0;

    /// <summary>
    /// 科目色：色相限制在 CI 色带内、饱和度与明度固定为 CI 主色的值，
    /// 从而「同色调、可区分」，与 CI 的用色方式一致。
    /// </summary>
    public static Color SubjectColor(string subject)
    {
        var t = HueOf(subject) / 360.0;
        var hue = HueBandStart + t * (HueBandEnd - HueBandStart);
        return FromHsl(hue, AccentSaturation, AccentLightness);
    }

    /// <summary>课表卡片填充：科目色 + CI 卡片不透明度（50%），与主界面观感一致。</summary>
    public static IBrush CourseFill(string subject) =>
        new SolidColorBrush(SubjectColor(subject), CardOpacity);

    /// <summary>课表卡片描边：同色相、稍亮的细边。</summary>
    public static IBrush CourseBorder(string subject) =>
        new SolidColorBrush(SubjectColor(subject), 0.85);

    /// <summary>稳定地由字符串得到色相（0–359）。</summary>
    public static double HueOf(string text)
    {
        if (string.IsNullOrEmpty(text))
            return AccentHue;

        var hash = 0;
        foreach (var ch in text)
            hash = (hash * 31 + ch) & 0x7fffffff;

        return hash % 360;
    }

    /// <summary>HSL → RGB。</summary>
    public static Color FromHsl(double hueDegrees, double saturation, double lightness)
    {
        var h = ((hueDegrees % 360) + 360) % 360 / 360.0;
        var s = Math.Clamp(saturation, 0, 1);
        var l = Math.Clamp(lightness, 0, 1);

        if (s <= 0)
        {
            var v = (byte)Math.Round(l * 255);
            return Color.FromArgb(255, v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return Color.FromArgb(
            255,
            Channel(p, q, h + 1.0 / 3),
            Channel(p, q, h),
            Channel(p, q, h - 1.0 / 3));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;

        double value;
        if (t < 1.0 / 6) value = p + (q - p) * 6 * t;
        else if (t < 1.0 / 2) value = q;
        else if (t < 2.0 / 3) value = p + (q - p) * (2.0 / 3 - t) * 6;
        else value = p;

        return (byte)Math.Round(value * 255);
    }
}
