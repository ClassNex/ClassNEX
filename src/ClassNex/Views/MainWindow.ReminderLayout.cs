using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class MainWindow
{
    private bool _stackedLesson, _shrinkAfterMask, _oversizeSlidePair;

    private static double Across(Thickness margin) => margin.Left + margin.Right;

    private static Size Natural(Control view)
    {
        view.MaxWidth = double.PositiveInfinity;
        if (view is TextBlock text) text.TextWrapping = TextWrapping.NoWrap;
        view.Measure(Size.Infinity);
        return view.DesiredSize;
    }

    private static void FitText(TextBlock text, double available, bool wrap)
    {
        text.MaxWidth = wrap ? Math.Max(1, available - Across(text.Margin)) : double.PositiveInfinity;
        text.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        text.TextAlignment = wrap ? TextAlignment.Center : TextAlignment.Left;
    }

    private void SetLessonRows(bool stacked)
    {
        if (stacked == _stackedLesson) return;
        _stackedLesson = stacked;
        var grid = NotificationOverlayCoursePage;
        grid.ColumnDefinitions[1].Width = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        grid.RowDefinitions = stacked ? new RowDefinitions("Auto,Auto") : new RowDefinitions();
        grid.RowSpacing = stacked ? 3 : 0;
        foreach (var block in new[] { NotificationOverlayLeft, NotificationBreakCaption, NotificationPlainCaption })
            Grid.SetColumnSpan(block, stacked ? 2 : 1);
        Grid.SetColumn(NotificationLesson, stacked ? 0 : 1);
        Grid.SetColumnSpan(NotificationLesson, stacked ? 2 : 1);
        Grid.SetRow(NotificationLesson, stacked ? 1 : 0);
        NotificationLesson.ItemsAlignment = stacked ? WrapPanelItemsAlignment.Center : WrapPanelItemsAlignment.Start;
        NotificationLesson.LineSpacing = stacked ? 2 : 0;
    }

    private void ArrangeNotification()
    {
        if (_overlayExiting) return;
        var limit = Math.Max(1, Math.Floor(AvailableIslandWidth));
        var heightLimit = Math.Max(1, Math.Floor(CardContent.MaxHeight));
        var titleInsets = Across(MaskTitleViewport.Padding) + Across(NotificationMaskIcons.Margin) + 40;
        var title = Natural(NotificationMaskText);
        var maskWidth = Math.Min(limit, Math.Ceiling(title.Width + titleInsets));
        FitText(NotificationMaskText, maskWidth - titleInsets, title.Width > maskWidth - titleInsets);
        NotificationMaskIcons.Measure(Size.Infinity);
        var maskHeight = Math.Max(NotificationHeight, Math.Ceiling(NotificationMaskIcons.DesiredSize.Height + 8));
        MaskTitleViewport.VerticalScrollBarVisibility = maskHeight > heightLimit ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        NotificationMask.IsHitTestVisible = maskHeight > heightLimit;

        var width = Math.Clamp(Math.Ceiling(_normalWidth), 1, limit);
        var bodyHeight = NotificationHeight;
        _oversizeSlidePair = false;
        if (NotificationOverlay.IsVisible)
        {
            if (_hasCoursePage)
            {
                var caption = NotificationOverlayLeft.IsVisible ? NotificationOverlayLeft : NotificationBreakCaption.IsVisible ? NotificationBreakCaption : NotificationPlainCaption;
                var captionWidth = Natural(caption).Width;
                NotificationOverlayTime.IsVisible = !string.IsNullOrWhiteSpace(NotificationOverlayTime.Text);
                NotificationOverlayTime.Margin = new Thickness(NotificationOverlayTeacher.IsVisible ? 2 : 6, 0, 0, 0);
                foreach (var label in NotificationLesson.Children.OfType<TextBlock>()) Natural(label);
                var lessonWidth = Natural(NotificationLesson).Width;
                var messageWidth = Natural(NotificationOverlayMessagePage).Width;
                var inset = Across(NotificationCourseProvider.Margin);
                var courseWidth = captionWidth + lessonWidth;
                width = Math.Min(limit, Math.Max(width, Math.Ceiling(inset + Math.Max(courseWidth, messageWidth))));
                var inner = Math.Max(1, width - inset);
                var stack = courseWidth > inner;
                SetLessonRows(stack);
                NotificationOverlayCoursePage.Width = stack ? inner : double.NaN;
                FitText(caption, inner, stack && captionWidth > inner);
                NotificationLesson.MaxWidth = stack ? Math.Max(1, inner - Across(NotificationLesson.Margin)) : double.PositiveInfinity;
                foreach (var label in NotificationLesson.Children.OfType<TextBlock>())
                    FitText(label, NotificationLesson.MaxWidth, stack);
                FitText(NotificationOverlayMessagePage, inner, messageWidth > inner);
                NotificationOverlayCoursePage.Measure(new Size(inner, double.PositiveInfinity));
                NotificationOverlayMessagePage.Measure(new Size(inner, double.PositiveInfinity));
                var courseHeight = Math.Ceiling(NotificationOverlayCoursePage.DesiredSize.Height + 10);
                var messageHeight = Math.Ceiling(NotificationOverlayMessagePage.DesiredSize.Height + 10);
                var sharedHeight = Math.Max(courseHeight, messageHeight);
                _oversizeSlidePair = sharedHeight > heightLimit;
                var activeHeight = NotificationSlides.SelectedIndex == 1 ? messageHeight : courseHeight;
                bodyHeight = Math.Max(bodyHeight, _oversizeSlidePair ? activeHeight : sharedHeight);
            }
            else
            {
                var natural = Natural(NotificationSimpleMessage);
                width = Math.Min(limit, Math.Max(width, Math.Ceiling(natural.Width)));
                var wrap = natural.Width > width;
                FitText(NotificationSimpleMessage, width, wrap);
                NotificationSimpleMessage.TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis;
                NotificationSimpleMessage.Measure(new Size(width, double.PositiveInfinity));
                bodyHeight = Math.Max(bodyHeight, Math.Ceiling(NotificationSimpleMessage.DesiredSize.Height + 10));
            }
        }
        _shrinkAfterMask = NotificationMask.IsVisible && NotificationOverlay.IsVisible && (maskWidth > width || maskHeight > bodyHeight);
        if (NotificationMask.IsVisible) width = Math.Max(width, maskWidth);
        var height = Math.Min(heightLimit, NotificationMask.IsVisible ? Math.Max(maskHeight, bodyHeight) : bodyHeight);
        NotificationBodyViewport.VerticalScrollBarVisibility = bodyHeight > heightLimit ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        NotificationPages.MinHeight = bodyHeight > heightLimit ? bodyHeight : 0;
        NotificationOverlay.IsHitTestVisible = bodyHeight > heightLimit;
        NotificationDetailLayout.HorizontalAlignment = (AppServices.Settings.ImportedDock?.Anchor % 3) switch
        {
            0 => HorizontalAlignment.Left, 2 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center
        };
        NotificationDetailLayout.Width = width;
        SetIslandWidth(width, reminder: true);
        IslandBackground.Height = height;
        RefreshReminderInput();
    }
}
