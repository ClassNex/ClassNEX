using ClassNex.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ClassNex.WinUI;

public sealed partial class NativeOverlayWindow
{
    private sealed class SchedulePart(NativeOverlayWindow window, WidgetStyle style)
    {
        internal StackPanel Row { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 5 * style.Spacing };
        internal FrameworkElement? Focus { get; private set; }
        private CourseSlot[] _slots = [];
        private int _mode = -1, _current, _gap, _done;
        private TimeSpan _start, _end;
        private TextBlock? _remaining, _name;
        private ScaleTransform? _fill;

        internal void Update(OverlaySnapshot snapshot)
        {
            var slots = snapshot.Today.Slots;
            var now = snapshot.Now.TimeOfDay;
            var mode = !snapshot.HasTimetable ? 0 : slots.Count == 0 ? 1 : 2;
            var current = -1; var gap = -1; var done = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                if (current < 0 && slots[i].Contains(now)) current = i;
                if (i > 0 && slots[i - 1].End <= now && now < slots[i].Start) gap = i;
                if (slots[i].End <= now) done++;
            }
            if (current >= 0) gap = -1;
            if (mode != _mode || current != _current || gap != _gap || done != _done || _slots.Length != slots.Count || !_slots.SequenceEqual(slots))
            {
                _mode = mode; _current = current; _gap = gap; _done = done;
                _slots = slots.ToArray(); _remaining = null; _name = null; _fill = null; Focus = null;
                Row.Children.Clear();
                if (mode < 2)
                {
                    var text = window.Label(13.5 * style.Scale);
                    text.Text = mode == 0 ? "尚未导入课程 · 前往导入 ›" : "今天没有课程";
                    var hint = window.Chip(text, 10);
                    if (mode == 0) hint.Tapped += (_, _) => window.GestureCommand("profile");
                    Row.Children.Add(hint);
                }
                else
                {
                    FrameworkElement? upcoming = null;
                    for (var i = 0; i < slots.Count; i++)
                    {
                        if (i == gap) Row.Children.Add(Expanded("课间休息", slots[i - 1].End, slots[i].Start));
                        var slot = slots[i];
                        if (i == current) { Row.Children.Add(Expanded(slot.Subject, slot.Start, slot.End)); continue; }
                        var shortName = window.Label(14.5 * style.Scale, window._palette.Custom(style.Color));
                        shortName.Text = slot.DisplayName;
                        var chip = window.Chip(shortName, 7);
                        if (style.Fade && slot.End <= now && !window._palette.HighContrast) shortName.Foreground = window._palette.SecondaryInk;
                        if (upcoming is null && slot.Start > now) upcoming = chip;
                        Row.Children.Add(chip);
                    }
                    Focus ??= upcoming;
                }
                window._layoutDirty = true;
                window.RequestCourseInView();
            }
            if (_remaining is null || _fill is null) return;
            var seconds = Math.Max(0, (int)Math.Ceiling((_end - now).TotalSeconds));
            var left = TimeSpan.FromSeconds(seconds);
            window.SetText(_remaining, seconds >= 3600 ? $"-{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"-{left.Minutes}:{left.Seconds:00}");
            var span = (_end - _start).TotalSeconds;
            var fraction = span <= 0 ? 0 : Math.Clamp((now - _start).TotalSeconds / span, 0, 1);
            if (_fill.ScaleX != fraction) _fill.ScaleX = fraction;
        }

        private Border Expanded(string title, TimeSpan start, TimeSpan end)
        {
            _start = start; _end = end;
            _name = window.Label(16.5 * style.Scale, window._palette.Ink);
            _name.Text = title; _name.TextWrapping = TextWrapping.Wrap;
            _remaining = window.Label(14 * style.Scale, window._palette.Ink);
            _remaining.MinWidth = 43 * style.Scale;
            _remaining.TextAlignment = TextAlignment.Right;
            var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 * style.Scale };
            labels.Children.Add(_name); labels.Children.Add(_remaining);
            _fill = new ScaleTransform { ScaleX = 0, ScaleY = 1 };
            var track = new Grid { Height = 2, Margin = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(1), Background = window._palette.Track };
            track.Children.Add(new Border { Background = window._palette.Accent, CornerRadius = new CornerRadius(1), RenderTransformOrigin = new Point(0, .5), RenderTransform = _fill });
            var contents = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            contents.Children.Add(labels); contents.Children.Add(track);
            var selected = new Border { Child = contents, MinHeight = 35, Padding = new Thickness(9, 2, 9, 2), CornerRadius = new CornerRadius(9),
                Background = window._palette.Selected, BorderBrush = window._palette.SurfaceStroke, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            Focus = selected;
            return selected;
        }

        internal void LimitWidth(double available)
        {
            if (_name is null) return;
            var max = Math.Max(8, available - 24 - 67 * style.Scale);
            if (_name.MaxWidth != max) _name.MaxWidth = max;
        }
    }
}
