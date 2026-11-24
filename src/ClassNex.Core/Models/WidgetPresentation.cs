namespace ClassNex.Models;

/// <summary>组件实例的可选外观。未设置的值沿用组件自己的布局与调色板；尺寸单位为未缩放的 DIP。</summary>
/// <param name="Width">固定宽度，优先于最小和最大宽度。</param>
/// <param name="MinWidth">最小宽度。</param>
/// <param name="MaxWidth">最大宽度。</param>
/// <param name="Alignment">内容的水平对齐：left、center、right 或 stretch。</param>
/// <param name="Margin">左、上、右、下四边间距。</param>
/// <param name="Background">本组件的背景颜色（ARGB）。</param>
/// <param name="BackgroundOpacity">仅应用于背景的不透明度。</param>
/// <param name="CornerRadius">背景圆角半径。</param>
/// <param name="Opacity">整个组件的不透明度。</param>
public sealed record WidgetPresentation(double? Width = null, double? MinWidth = null, double? MaxWidth = null,
    string? Alignment = null, double[]? Margin = null, string? Background = null, double? BackgroundOpacity = null,
    double? CornerRadius = null, double Opacity = 1);
