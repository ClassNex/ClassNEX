using ClassNex.Models;
using ClassNex.Services;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private int _widgetIndex;
    private bool _widgetLibrary;
    private bool _widgetListView;
    private const string WidgetTypeFormat = "ClassNEX.WidgetType";
    private string _widgetShape = "";
    private static string WidgetShape(AppSettings s) => string.Join(';', s.Widgets.Select(w => $"{w.Row}:{w.Order}:{w.Type}:{w.IsEnabled}"));
    private static void Reindex(List<WidgetConfig> list) { for (var i = 0; i < list.Count; ++i) list[i].Order = i; }

    public void SelectWidget(int index)
    {
        CommitInputs();
        _widgetIndex = index;
        _widgetLibrary = false;
        if (CurrentPageKey == "widget-layout") RenderPage();
    }

    private void ChangeWidgetLayout(Action<List<WidgetConfig>> change)
    {
        CommitInputs();
        Edit(s => { var sorted = WidgetConfig.InDisplayOrder(s.Widgets).ToList(); change(sorted); Reindex(sorted); s.Widgets = sorted; _widgetShape = "!"; });
    }

    private void WidgetLayoutPage()
    {
        var settings = _bridge.ReadSettings();
        _widgetShape = WidgetShape(settings);
        _widgetIndex = Math.Clamp(_widgetIndex, 0, Math.Max(0, settings.Widgets.Count - 1));
        AddWidgetPresetPicker();
        CommandRow("EditDesktop", "在桌面上编辑", "进入编辑模式", NativeSettingsKeys.EditOverlay);
        var layout = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        layout.ColumnDefinitions.Add(new()); layout.ColumnDefinitions.Add(new());
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var master = new StackPanel { Spacing = 6 }; var inspector = new StackPanel { Spacing = 6 };
        layout.Children.Add(master); layout.Children.Add(inspector); _body.Children.Add(layout);
        void Arrange(double width) { var wide = width >= 690; layout.ColumnDefinitions[0].Width = wide ? new GridLength(_widgetListView ? 240 : Math.Clamp(width * .40, 280, 440)) : new GridLength(1, GridUnitType.Star); layout.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0); Grid.SetColumn(inspector, wide ? 1 : 0); Grid.SetRow(inspector, wide ? 0 : 1); layout.ColumnSpacing = wide ? 16 : 0; }
        Arrange(0); layout.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        var orderHeader = new Grid { ColumnSpacing = 6 };
        orderHeader.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        orderHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        orderHeader.Children.Add(Text("桌面组件", "SubtitleTextBlockStyle"));
        var viewMode = Target("WidgetOrderMode", "排列视图", new ComboBox { ItemsSource = new[] { "按行排列", "顺序列表" }, SelectedIndex = _widgetListView ? 1 : 0, MinWidth = 100 });
        viewMode.SelectionChanged += (_, _) => { if (viewMode.SelectedIndex < 0 || _widgetListView == (viewMode.SelectedIndex == 1)) return; CommitInputs(); _widgetListView = viewMode.SelectedIndex == 1; RenderPage(); };
        Grid.SetColumn(viewMode, 1); orderHeader.Children.Add(viewMode); master.Children.Add(orderHeader);
        var sorted = WidgetConfig.InDisplayOrder(settings.Widgets).ToList();
        var rows = sorted.Select(w => w.Row).Distinct().ToList();
        void Add(string type, int row, int? before = null) => ChangeWidgetLayout(v =>
        {
            if (WidgetRegistry.Find(type) == null || !v.SequenceEqual(sorted)) return;
            _widgetIndex = before ?? (v.FindLastIndex(w => w.Row == row) + 1);
            v.Insert(_widgetIndex, new() { Type = type, Row = row }); _widgetLibrary = false;
        });
        void AppendSelected(string type) => Add(type, sorted.Count == 0 ? 0 : sorted[_widgetIndex].Row);
        if (!_widgetListView) AddWidgetRows(master, sorted, Add);
        else
        {
        var entries = new ObservableCollection<ListViewItem>(sorted.Select((w, i) => new ListViewItem
        {
            Content = (rows.Count > 1 ? $"第 {rows.IndexOf(w.Row) + 1} 行 · " : "") + WidgetRegistry.DisplayNameOf(w.Type) + (w.IsEnabled ? "" : "（已隐藏）"), Tag = i,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        }));
        var list = Target("WidgetSelection", "桌面组件顺序", new ListView { ItemsSource=entries, CanDragItems=true, CanReorderItems=true, AllowDrop=true, SelectedIndex=sorted.Count==0 ? -1 : _widgetIndex, MinHeight=54 });
        bool dragging=false;
        int dragIndex=-1;
        list.DragItemsStarting += (_,e) => { dragging=true; dragIndex=e.Items.OfType<ListViewItem>().FirstOrDefault()?.Tag is int i ? i : list.SelectedIndex; };
        list.DragItemsCompleted += (_,_) =>
        {
            dragging = false;
            var indices = entries.Select(item => (int)item.Tag).ToArray();
            if (indices.SequenceEqual(Enumerable.Range(0, sorted.Count))) return;
            ChangeWidgetLayout(v =>
            {
                if (v.Count != indices.Length) return;
                var destination = Array.IndexOf(indices, dragIndex);
                if (dragIndex >= 0 && destination >= 0) v[dragIndex].Row = v[destination].Row;
                var reordered = indices.Select(i => v[i]).ToList();
                _widgetIndex = Array.IndexOf(indices, _widgetIndex);
                v.Clear(); v.AddRange(reordered);
            });
        };
        list.SelectionChanged += (_,_) => { if(!dragging && list.SelectedIndex>=0 && (_widgetLibrary || list.SelectedIndex!=_widgetIndex)) SelectWidget(list.SelectedIndex); };
        list.DragOver += (_,e) => { if(e.DataView.Contains(WidgetTypeFormat)){e.AcceptedOperation=DataPackageOperation.Copy;e.Handled=true;} };
        list.Drop += async (_,e) =>
        {
            if (!e.DataView.Contains(WidgetTypeFormat)) return;
            e.Handled = true; var deferral = e.GetDeferral();
            try
            {
                var row = sorted.LastOrDefault()?.Row ?? 0; var position = sorted.Count;
                for (var i = 0; i < sorted.Count; ++i)
                    if (list.ContainerFromIndex(i) is ListViewItem item && e.GetPosition(item).Y is var y && y < item.ActualHeight)
                    { row = sorted[i].Row; position = i + (y >= item.ActualHeight / 2 ? 1 : 0); break; }
                if (await e.DataView.GetDataAsync(WidgetTypeFormat) is string type) Add(type, row, position);
            }
            finally { deferral.Complete(); }
        };
        master.Children.Add(list);
        }
        if (sorted.Count>0)
        {
            var index=_widgetIndex; var actions=Target("WidgetActions","组件操作",new StackPanel{Orientation=Orientation.Horizontal,Spacing=2}); master.Children.Add(actions);
            void Tool(string id,string label,string glyph,bool enabled,Action action){var b=ActionButton(id,label,action); b.Content=new FontIcon { Glyph=glyph, FontSize=16 }; b.MinWidth=32;b.Padding=new Thickness(5);b.IsEnabled=enabled;ToolTipService.SetToolTip(b,label);actions.Children.Add(b);}
            void Move(int offset)=>ChangeWidgetLayout(v=>{var w=v[index];_widgetIndex=index+offset;w.Row=v[_widgetIndex].Row;v.RemoveAt(index);v.Insert(_widgetIndex,w);});
            Tool("WidgetUp","上移","\uE70E",index>0,()=>Move(-1)); Tool("WidgetDown","下移","\uE70D",index+1<sorted.Count,()=>Move(1));
            Tool("WidgetCopy","创建副本","\uE8C8",true,()=>ChangeWidgetLayout(v=>{v.Insert(index+1,v[index].Copy());_widgetIndex=index+1;}));
            Tool("WidgetRemove","移除组件","\uE74D",true,()=>ChangeWidgetLayout(v=>v.RemoveAt(index)));
        }
        else master.Children.Add(SettingCaption("从组件库添加第一个组件。"));
        var tabs = Target("WidgetLibrary", "组件库与设置", new SelectorBar());
        var library = new SelectorBarItem { Text="组件库" }; var properties=new SelectorBarItem { Text="组件设置",IsEnabled=sorted.Count>0 };
        tabs.Items.Add(library); tabs.Items.Add(properties); tabs.SelectedItem = sorted.Count==0 || _widgetLibrary ? library : properties;
        tabs.SelectionChanged+=(_,_)=>{ var show=tabs.SelectedItem==library; if(show==_widgetLibrary)return;CommitInputs();_widgetLibrary=show;RenderPage();}; inspector.Children.Add(tabs);
        var pageBody=_body;
        try
        {
            _body=inspector;
            if (tabs.SelectedItem==library)
            {
                foreach(var info in WidgetRegistry.Types)
                {
                    var button=ActionButton("WidgetAdd_"+info.Type,"添加"+info.DisplayName,()=>AppendSelected(info.Type));
                    button.Content=new FontIcon{Glyph="\uE710",FontSize=14}; ToolTipService.SetToolTip(button,"添加"+info.DisplayName);
                    var row=MakeSettingRow(info.DisplayName,info.Description,button); row.CanDrag=true;row.DragStarting+=(_,e)=>{e.AllowedOperations=DataPackageOperation.Copy;e.Data.SetData(WidgetTypeFormat,info.Type);};_body.Children.Add(row);
                }
            }
            else
            {
                var index=_widgetIndex;
                var destinations=rows.Select((_,i)=>$"第 {i+1} 行").Append("移到新的一行").ToArray();
                Choice("WidgetRow", "所在行", destinations,
                    s=>rows.IndexOf(WidgetConfig.InDisplayOrder(s.Widgets).ElementAt(index).Row),
                    (s,value)=>
                    {
                        if (value < 0 || value > rows.Count) return;
                        var moved = WidgetConfig.MoveToRowEnd(s.Widgets, sorted[index], value < rows.Count ? rows[value] : null);
                        if (moved >= 0) { _widgetIndex = moved; _widgetShape = "!"; }
                    });
                WidgetProperties(index,sorted[index].Type);
            }
        }
        finally{_body=pageBody;}
        ActionRow("WidgetsReset", "恢复默认布局", "恢复默认", ConfirmWidgetReset);
    }

    private void WidgetProperties(int index, string type)
    {
            WidgetConfig Get(AppSettings s) => WidgetConfig.InDisplayOrder(s.Widgets).ElementAt(index);
            Switch("WidgetEnabled", "启用该组件", s => Get(s).IsEnabled, (s, v) => Get(s).IsEnabled = v);
            Choice("WidgetDisplayWhen", "显示时段", ["始终显示", "仅上课时", "仅课间时", "仅活动时", "仅当天无课时"],
                s => Enum.IsDefined(Get(s).DisplayWhen) ? (int)Get(s).DisplayWhen : 0, (s, v) => Get(s).DisplayWhen = (WidgetDisplayWhen)v);
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
                    Switch("WidgetHideFinished", "隐藏已结束的课程", s => Get(s).HideFinishedClass, (s, v) => Get(s).HideFinishedClass = v);
                    Switch("WidgetCurrentOnly", "上课时只显示当前课程", s => Get(s).ShowCurrentLessonOnlyOnClass, (s, v) => Get(s).ShowCurrentLessonOnlyOnClass = v);
                    Switch("WidgetTimeDetails", "显示剩余时间", s => Get(s).ShowTimeDetails, (s, v) => Get(s).ShowTimeDetails = v);
                    Number("WidgetCountdownSeconds", "倒计时胶囊提前秒数", 0, 86400, s => Get(s).CountdownSeconds, (s, v) => Get(s).CountdownSeconds = v, "设为 0 不显示胶囊。");
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
            WidgetAppearance(Get);
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
