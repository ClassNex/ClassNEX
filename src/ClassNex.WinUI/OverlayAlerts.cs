using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

public sealed partial class NativeOverlayWindow
{
    private readonly Border _maskPage = new() { Padding = new Thickness(12, 4, 12, 4) };
    private readonly Border _alertPage = new() { Padding = new Thickness(13, 3, 13, 3) };
    private readonly Grid _maskRow = new() { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _coursePage = new() { ColumnSpacing = 12 };
    private readonly Grid _pages = new();
    private readonly TranslateTransform _courseY = new(), _messageY = new();
    private readonly ScaleTransform _trackSize = new() { ScaleX = 1, ScaleY = 1 };
    private readonly Border _track = new() { Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 2, 0, 0) };
    private readonly Border _trackFill = new() { CornerRadius = new CornerRadius(1), RenderTransformOrigin = new Point(0, .5) };
    private readonly RectangleGeometry _clip = new();
    private readonly UISettings _accessibility = new();
    private readonly SymbolIcon _maskLeft = new(Symbol.ViewAll), _maskRight = new(Symbol.Library);
    private TextBlock _maskText = null!;
    private TextBlock _lead = null!, _subject = null!, _detail = null!, _messagePage = null!;
    private Storyboard? _progressAnim, _turnAnim, _maskAnim;
    private bool _messageVisible, _ignoreContinuation;

    private void InitAlerts()
    {
        _maskText = Label(20);
        _maskLeft.IsHitTestVisible = _maskRight.IsHitTestVisible = false;
        _maskText.TextAlignment = TextAlignment.Center; _maskText.TextWrapping = TextWrapping.Wrap;
        _maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _maskRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_maskText, 1); Grid.SetColumn(_maskRight, 2);
        _maskRow.Children.Add(_maskLeft); _maskRow.Children.Add(_maskText); _maskRow.Children.Add(_maskRight);
        _maskPage.Child = _maskRow;
        _lead = Label(14); _lead.MaxWidth = 170; _lead.TextWrapping = TextWrapping.Wrap;
        _subject = Label(18); _subject.TextWrapping = TextWrapping.Wrap;
        _detail = Label(11); _detail.TextWrapping = TextWrapping.Wrap;
        var detailColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        detailColumn.Children.Add(_subject); detailColumn.Children.Add(_detail);
        _coursePage.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _coursePage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(detailColumn, 1);
        _coursePage.VerticalAlignment = VerticalAlignment.Center;
        _coursePage.Children.Add(_lead); _coursePage.Children.Add(detailColumn);
        _coursePage.RenderTransform = _courseY;
        _messagePage = Label(16); _messagePage.TextAlignment = TextAlignment.Center;
        _messagePage.TextWrapping = TextWrapping.Wrap; _messagePage.RenderTransform = _messageY;
        _pages.Clip = _clip;
        _pages.SizeChanged += (_, e) => _clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        _pages.Children.Add(_coursePage); _pages.Children.Add(_messagePage);
        _trackFill.RenderTransform = _trackSize;
        _track.Child = _trackFill;
        var arrangement = new Grid();
        arrangement.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        arrangement.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_track, 1);
        arrangement.Children.Add(_pages); arrangement.Children.Add(_track);
        _alertPage.Child = arrangement;
        ApplyAlertPalette();
    }

    private void ApplyAlertPalette()
    {
        _maskLeft.Foreground = _maskRight.Foreground = _palette.MaskSymbol;
        _maskText.Foreground = _palette.MaskInk;
        _lead.Foreground = _subject.Foreground = _messagePage.Foreground = _palette.Ink;
        _detail.Foreground = _palette.SecondaryInk;
        _track.Background = _palette.Track;
        _trackFill.Background = _palette.Accent;
    }

    private void BeginMask(string text, string leftGlyph, string rightGlyph, bool playEffect)
    {
        if (!_visible) return;
        _ignoreContinuation = false;
        StopAlertAnimations();
        SetText(_maskText, text);
        ApplyMaskSymbol(_maskLeft, leftGlyph); ApplyMaskSymbol(_maskRight, rightGlyph);
        SelectSurface(Surface.Mask);
        MeasureAndPlace();
        if (playEffect && _accessibility.AnimationsEnabled)
        {
            _maskAnim = new Storyboard();
            AddMotion(_maskAnim, _maskRow, "Opacity", 0, 1, 180);
            _maskAnim.Begin();
        }
    }

    private static void ApplyMaskSymbol(SymbolIcon icon, string glyph)
    {
        Symbol? symbol = glyph switch { "\uE0FF" => Symbol.ViewAll, "\uE54F" => Symbol.Library, "\uE250" => Symbol.Clock, _ => null };
        if (symbol is { } known) icon.Symbol = known;
        icon.Visibility = symbol.HasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BeginAlert(string left, string subject, string teacher, string time, string prefix, string message, bool verticalPageTurn, TimeSpan trackDuration)
    {
        if (!_visible || _ignoreContinuation) return;
        StopAlertAnimations();
        SetText(_lead, left); SetText(_subject, subject);
        SetText(_detail, string.Join("  ·  ", new[] { prefix.TrimEnd('：', ':'), teacher, time }.Where(x => !string.IsNullOrWhiteSpace(x))));
        SetText(_messagePage, message);
        _detail.Visibility = string.IsNullOrWhiteSpace(_detail.Text) ? Visibility.Collapsed : Visibility.Visible;
        _messageVisible = false;
        _messagePage.Visibility = verticalPageTurn && !string.IsNullOrWhiteSpace(message) ? Visibility.Visible : Visibility.Collapsed;
        _messagePage.Opacity = 0; _coursePage.Opacity = 1;
        _courseY.Y = _messageY.Y = 0;
        _trackSize.ScaleX = trackDuration > TimeSpan.Zero ? 1 : 0;
        SelectSurface(Surface.Alert);
        MeasureAndPlace();
        if (trackDuration > TimeSpan.Zero)
        {
            _progressAnim = new Storyboard();
            AddMotion(_progressAnim, _trackSize, "ScaleX", 1, 0, trackDuration.TotalMilliseconds);
            _progressAnim.Begin();
        }
        if (_messagePage.Visibility == Visibility.Visible && trackDuration > _turn.Interval) _turn.Start();
    }

    private void UpdateAlertCountdown(string text)
    {
        if (!_visible || _surface != Surface.Alert || _lead.Text == text) return;
        SetText(_lead, text);
        MeasureAndPlace();
    }

    private void TurnAlertPage()
    {
        if (!_visible || _surface != Surface.Alert) return;
        _turnAnim?.Stop();
        var old = _messageVisible ? (UIElement)_messagePage : _coursePage;
        var next = _messageVisible ? (UIElement)_coursePage : _messagePage;
        var oldY = _messageVisible ? _messageY : _courseY;
        var nextY = _messageVisible ? _courseY : _messageY;
        _messageVisible = !_messageVisible;
        old.Opacity = 0; next.Opacity = 1;
        oldY.Y = nextY.Y = 0;
        if (!_accessibility.AnimationsEnabled) return;
        _turnAnim = new Storyboard();
        AddMotion(_turnAnim, old, "Opacity", 1, 0, 110);
        AddMotion(_turnAnim, oldY, "Y", 0, -14, 110);
        AddMotion(_turnAnim, next, "Opacity", 0, 1, 180, 110);
        AddMotion(_turnAnim, nextY, "Y", 14, 0, 180, 110, new CubicEase { EasingMode = EasingMode.EaseOut });
        next.Opacity = 0;
        _turnAnim.Begin();
    }

    private void EndAlert()
    {
        if (!_visible || _surface is not (Surface.Mask or Surface.Alert)) return;
        StopAlertAnimations();
        RestoreBaseSurface();
    }

    private void StopAlertAnimations()
    {
        _turn.Stop();
        _turnAnim?.Stop(); _turnAnim = null;
        _progressAnim?.Stop(); _progressAnim = null;
        _maskAnim?.Stop(); _maskAnim = null;
        _maskRow.Opacity = 1;
    }

    private static void AddMotion(Storyboard storyboard, DependencyObject target, string property, double from, double to, double milliseconds,
        double delay = 0, EasingFunctionBase? easing = null)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = easing };
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
