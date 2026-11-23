using ClassNex.Models;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private int _widgetIndex;
    private string _widgetShape = "";
    private static string WidgetShape(AppSettings s) => string.Join(';', s.Widgets.Select(w => $"{w.Order}:{w.Type}"));
    private static void Reindex(List<WidgetConfig> list) { for (var i = 0; i < list.Count; ++i) list[i].Order = i; }

    private void WidgetLayoutPage()
    {
        var settings = _bridge.ReadSettings();
        _widgetShape = WidgetShape(settings);
        _widgetIndex = Math.Clamp(_widgetIndex, 0, Math.Max(0, settings.Widgets.Count - 1));
        CommandRow("EditDesktop", "桌面布局", "编辑组件", NativeSettingsKeys.EditOverlay);
        Section("桌面课表布局");
        if (settings.Widgets.Count != 0)
        {
            var index = _widgetIndex;
            var display = Enumerable.Range(0, settings.Widgets.Count).OrderBy(i => settings.Widgets[i].Order).ToArray();
            var choice = Target("WidgetSelection", "要编辑的组件", new ComboBox
            {
                ItemsSource = display.Select((slot, i) => $"{i + 1}. {WidgetRegistry.DisplayNameOf(settings.Widgets[slot].Type)}").ToArray(),
                SelectedIndex = index, MinWidth = 154, MaxWidth = 240
            });
            choice.SelectionChanged += (_, _) =>
            {
                if (_refreshing || choice.SelectedIndex < 0 || choice.SelectedIndex == _widgetIndex) return;
                FlushSlider();
                _widgetIndex = choice.SelectedIndex;
                RenderPage();
            };
            Row("要编辑的组件", "", choice);
            var actions = Target("WidgetActions", "排列和移除当前组件", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 });
            Button MoveButton(int delta)
            {
                var button = ActionButton(delta < 0 ? "WidgetUp" : "WidgetDown", delta < 0 ? "上移" : "下移", () =>
                {
                    FlushSlider();
                    var target = index + delta;
                    Edit(s =>
                    {
                        if (target < 0 || index >= s.Widgets.Count || target >= s.Widgets.Count) return;
                        s.Widgets = s.Widgets.OrderBy(w => w.Order).ToList();
                        (s.Widgets[index], s.Widgets[target]) = (s.Widgets[target], s.Widgets[index]);
                        Reindex(s.Widgets);
                        _widgetIndex = target;
                        _widgetShape = "!";
                    });
                });
                button.IsEnabled = index + delta >= 0 && index + delta < settings.Widgets.Count;
                return button;
            }
            actions.Children.Add(MoveButton(-1));
            actions.Children.Add(MoveButton(1));
            actions.Children.Add(ActionButton("WidgetRemove", "移除", () =>
            {
                FlushSlider();
                Edit(s => { if (index < s.Widgets.Count) { s.Widgets = s.Widgets.OrderBy(w => w.Order).ToList(); s.Widgets.RemoveAt(index); Reindex(s.Widgets); } });
            }));
            Row("排列与移除", "", actions);
            var type = settings.Widgets[display[index]].Type;
            WidgetConfig Get(AppSettings s) => s.Widgets[display[index]];
            Switch("WidgetEnabled", "启用该组件", s => Get(s).IsEnabled, (s, v) => Get(s).IsEnabled = v);
            Scale("WidgetFontScale", "组件字号", .6, 2, .05, s => Get(s).FontScale, (s, v) => Get(s).FontScale = v, v => $"{v:0.00}×");
            switch (type)
            {
                case "text":
                    Field("WidgetText", "文本内容", s => Get(s).Text, (s, v) => Get(s).Text = v, true, "支持文本组件的课程、时间等占位符。");
                    Switch("WidgetColorEnabled", "自定义字体颜色", s => Get(s).UseCustomFontColor, (s, v) => Get(s).UseCustomFontColor = v);
                    string?[] colors = [null, "#FFFFFFFF", "#FFC8C8C8", "#FF000000", "#FF4CC2FF", "#FF7FFFD4", "#FF6CCB5F", "#FFF4EF74", "#FFF7A501", "#FFE81123", "#FF8B5CF6"];
                    Choice("WidgetColor", "字体颜色", ["跟随主题", "白色", "浅灰", "黑色", "强调色", "青色", "绿色", "黄色", "橙色", "红色", "紫色"],
                        s => Array.IndexOf(colors, Get(s).FontColor), (s, v) => Get(s).FontColor = colors[v]);
                    break;
                case "clock":
                    Switch("WidgetSeconds", "显示秒数", s => Get(s).ShowSeconds, (s, v) => Get(s).ShowSeconds = v);
                    Switch("WidgetFlash", "闪动时间分隔符", s => Get(s).FlashTimeSeparator, (s, v) => Get(s).FlashTimeSeparator = v, "未显示秒数时生效。");
                    break;
                case "schedule":
                    Switch("WidgetFade", "淡化上过的课程", s => Get(s).FadeCompletedClasses, (s, v) => Get(s).FadeCompletedClasses = v);
                    Scale("WidgetSpacing", "课程表文本间距", .5, 1.5, .05, s => Get(s).ScheduleSpacing, (s, v) => Get(s).ScheduleSpacing = v, v => $"{v:0.00}×");
                    break;
                case "nextclass":
                    Switch("WidgetShowNext", "显示下一节课", s => Get(s).ShowNextClass, (s, v) => Get(s).ShowNextClass = v);
                    break;
                case "countdown":
                    Field("WidgetCountdownName", "倒计时名称", s => Get(s).CountdownName, (s, v) => Get(s).CountdownName = v);
                    Date("WidgetCountdownDate", "目标日期", s => Get(s).CountdownTarget, (s, v) => Get(s).CountdownTarget = v);
                    Switch("WidgetCountdownAccent", "使用强调色", s => Get(s).UseCountdownAccent, (s, v) => Get(s).UseCountdownAccent = v);
                    break;
            }
        }
        else _body.Children.Add(Text("还没有组件，可从下方添加或恢复默认布局。"));
        Section("组件库");
        var library = Target("WidgetLibrary", "选择要添加的组件", new ComboBox { ItemsSource = WidgetRegistry.Types.Select(t => t.DisplayName).ToArray(), SelectedIndex = 0, MinWidth = 154 });
        Row("选择组件", "同类组件可以多次添加。", library);
        ActionRow("WidgetAdd", "添加到桌面课表", "添加组件", () =>
        {
            if (library.SelectedIndex < 0 || library.SelectedIndex >= WidgetRegistry.Types.Count) return;
            FlushSlider();
            var type = WidgetRegistry.Types[library.SelectedIndex].Type;
            Edit(s => { s.Widgets = s.Widgets.OrderBy(w => w.Order).ToList(); Reindex(s.Widgets); _widgetIndex = s.Widgets.Count; s.Widgets.Add(new WidgetConfig { Type = type, Order = _widgetIndex }); });
        });
        ActionRow("WidgetsReset", "恢复默认布局", "恢复默认", ConfirmWidgetReset);
    }

    private async void ConfirmWidgetReset()
    {
        var dialog = new ContentDialog { XamlRoot = _shell.XamlRoot, Title = "恢复默认组件布局？", Content = "将用默认组件替换当前布局。你的课表与档案不会改变。", PrimaryButtonText = "恢复", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed) return;
        FlushSlider();
        _widgetIndex = 0;
        _widgetShape = "!";
        Edit(s => s.Widgets = AppSettings.CreateDefaultWidgets());
    }
}
