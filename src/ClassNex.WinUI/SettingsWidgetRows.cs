using ClassNex.Models;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private void AddWidgetRows(StackPanel master, List<WidgetConfig> sorted, Action<string, int, int?> insert)
    {
        const string instanceFormat = "ClassNEX.WidgetInstance";
        WidgetConfig? dragged = null;
        var source = _bridge.ReadSettings().Widgets;
        var slots = Enumerable.Range(0, source.Count).OrderBy(i => source[i].Row).ThenBy(i => source[i].Order).ToArray();
        var rows = sorted.Select(w => w.Row).Distinct().DefaultIfEmpty(0).ToArray();
        for (var line = 0; line < rows.Length; line++)
        {
            var row = rows[line];
            var surface = Target("WidgetStrip_" + line, $"第 {line + 1} 行", new Grid
            {
                Style = SettingSurface, AllowDrop = true, RowSpacing = 5, Padding = new Thickness(10, 8, 10, 8)
            });
            surface.RowDefinitions.Add(new() { Height = GridLength.Auto });
            surface.RowDefinitions.Add(new() { Height = GridLength.Auto });
            surface.Children.Add(SettingCaption($"第 {line + 1} 行"));
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var scroll = new ScrollViewer { Content = strip, HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MinHeight = 38, IsTabStop = false };
            Grid.SetRow(scroll, 1); surface.Children.Add(scroll); master.Children.Add(surface);
            foreach (var (widget, index) in sorted.Select((widget, index) => (widget, index)).Where(x => x.widget.Row == row))
            {
                var name = WidgetRegistry.DisplayNameOf(widget.Type);
                var caption = new TextBlock { MaxWidth = 136, TextTrimming = TextTrimming.CharacterEllipsis };
                var chip = Target("WidgetChip_" + index, name, new ToggleButton
                {
                    Content = caption, Tag = index, CanDrag = true, IsChecked = !_widgetLibrary && index == _widgetIndex,
                    VerticalAlignment = VerticalAlignment.Center, MinHeight = 32, Padding = new Thickness(10, 3, 10, 3)
                });
                var slot = slots[index]; string? lastText = null; bool? lastEnabled = null;
                _bindings.Add(s =>
                {
                    var current = s.Widgets[slot];
                    if (current.Text == lastText && current.IsEnabled == lastEnabled) return;
                    lastText = current.Text; lastEnabled = current.IsEnabled;
                    var text = name;
                    if (current.Type == "text" && !string.IsNullOrWhiteSpace(current.Text))
                    {
                        var sample = current.Text.AsSpan().Trim(); var end = sample.IndexOfAny('\r', '\n');
                        text = sample[..Math.Min(end < 0 ? sample.Length : end, 96)].ToString();
                    }
                    if (!current.IsEnabled) text += "（隐藏）";
                    if (caption.Text == text) return;
                    caption.Text = text;
                    ToolTipService.SetToolTip(chip, $"{name} · {text}");
                });
                chip.Checked += (_, _) => { if (_widgetLibrary || index != _widgetIndex) SelectWidget(index); };
                chip.Unchecked += (_, _) => { if (chip.IsLoaded && !_widgetLibrary && index == _widgetIndex) chip.IsChecked = true; };
                chip.DragStarting += (_, e) => { dragged = widget; e.AllowedOperations = DataPackageOperation.Move; e.Data.SetData(instanceFormat, "row"); };
                chip.DropCompleted += (_, _) => dragged = null;
                strip.Children.Add(chip);
            }
            var add = ActionButton("WidgetStripAdd_" + line, $"向第 {line + 1} 行添加组件", () =>
            {
                CommitInputs(); _widgetIndex = Math.Max(0, sorted.FindLastIndex(w => w.Row == row)); _widgetLibrary = true; RenderPage();
            });
            add.Content = new FontIcon { Glyph = "\uE710", FontSize = 14 };
            add.MinWidth = 32; add.Padding = new Thickness(6, 3, 6, 3); add.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(add, "在这一行添加组件"); strip.Children.Add(add);
            DataPackageOperation Operation(DragEventArgs e) => e.DataView.Contains(WidgetTypeFormat) ? DataPackageOperation.Copy :
                dragged != null && e.DataView.Contains(instanceFormat) ? DataPackageOperation.Move : DataPackageOperation.None;
            int Insertion(DragEventArgs e)
            {
                foreach (var chip in strip.Children.OfType<ToggleButton>())
                    if (e.GetPosition(chip).X < chip.ActualWidth / 2) return (int)chip.Tag;
                return sorted.FindLastIndex(w => w.Row == row) + 1;
            }
            surface.DragOver += (_, e) =>
            {
                var operation = Operation(e);
                if (operation == DataPackageOperation.None) return;
                e.AcceptedOperation = operation; e.Handled = true;
            };
            surface.Drop += async (_, e) =>
            {
                var operation = Operation(e);
                if (operation == DataPackageOperation.None) return;
                var before = Insertion(e); var moving = dragged;
                e.AcceptedOperation = operation; e.Handled = true;
                if (operation == DataPackageOperation.Move && moving != null)
                {
                    var from = sorted.IndexOf(moving);
                    if (from < 0 || (moving.Row == row && (before == from || before == from + 1))) return;
                    ChangeWidgetLayout(items =>
                    {
                        if (!items.SequenceEqual(sorted)) return;
                        items.RemoveAt(from); _widgetIndex = before - (before > from ? 1 : 0);
                        moving.Row = row; items.Insert(_widgetIndex, moving); _widgetLibrary = false;
                    });
                    return;
                }
                var deferral = e.GetDeferral();
                try { if (await e.DataView.GetDataAsync(WidgetTypeFormat) is string type && !_closed && surface.IsLoaded) insert(type, row, before); }
                finally { deferral.Complete(); }
            };
        }
    }
}
