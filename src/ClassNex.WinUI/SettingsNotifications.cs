using ClassNex.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private const string ReminderPreviewHint = "正式提醒播放期间，请等它结束后再试。新预览会替换上一次预览。";

    private void NotificationOverview(Destination page)
    {
        Switch("Notifications", "启用课堂提醒", s => s.AllowNotification, (s, value) => s.AllowNotification = value,
            "在桌面课表上显示提醒。关闭后仍保留各项设置。");
        Section("提醒来源与方式");
        CategoryList(page);
        Section("预览");
        var events = new[] { ("课前准备", Prepare), ("上课", ClassOn), ("下课", ClassOff), ("放学", AfterSchool) };
        var chooser = Target("PreviewKind", "选择要预览的课堂事件", new ComboBox { MinWidth = 100, ItemsSource = events.Select(item => item.Item1).ToArray(), SelectedIndex = 0 });
        var play = ActionButton("PreviewNotification", "预览", () =>
        {
            if (chooser.SelectedIndex >= 0 && chooser.SelectedIndex < events.Length) PlayReminderPreview(events[chooser.SelectedIndex].Item2);
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(chooser);
        actions.Children.Add(play);
        Row("在桌面课表中预览", ReminderPreviewHint, actions);
        BindPreviewAvailability(play);
    }

    private void ReminderRulesPage()
    {
        static string LeadTime(int seconds) => seconds > 0 && seconds % 60 == 0 ? $"{seconds / 60} 分钟" : $"{seconds} 秒";
        ReminderDetail("PrepareDetails", "课前准备", "\uE823", s => !s.Notification.IsClassOnPreparingNotificationEnabled ? "已关闭" : !s.AllowMinorNotification ? "次要提醒已关闭" : $"室内提前 {s.Notification.ClassOnBreakReminderMinutes} 分钟 · 室外提前 {LeadTime(s.Notification.OutDoorClassPreparingDeltaTime)}", () =>
        {
            Switch("ClassPrepare", "课前提醒", s => s.Notification.IsClassOnPreparingNotificationEnabled, (s, value) => s.Notification.IsClassOnPreparingNotificationEnabled = value,
                "在下一节课开始前，交替显示课程信息和准备提示。");
            Section("室内课程");
            Number("PrepareMinutes", "提前时间（分钟）", 1, 60, s => s.Notification.ClassOnBreakReminderMinutes, (s, value) =>
            {
                s.Notification.ClassOnBreakReminderMinutes = value;
                s.Notification.InDoorClassPreparingDeltaTime = 60 * value;
            });
            Field("PreparingMask", "开场标题", s => s.Notification.ClassOnPreparingMaskText, (s, value) => s.Notification.ClassOnPreparingMaskText = value);
            Field("PreparingText", "准备提示", s => s.Notification.ClassOnPreparingText, (s, value) => s.Notification.ClassOnPreparingText = value, multiline: true);
            Section("室外课程");
            Number("OutdoorSeconds", "提前时间（秒）", 0, 3600, s => s.Notification.OutDoorClassPreparingDeltaTime, (s, value) => s.Notification.OutDoorClassPreparingDeltaTime = value);
            Field("OutdoorMask", "开场标题", s => s.Notification.OutdoorClassOnPreparingMaskText, (s, value) => s.Notification.OutdoorClassOnPreparingMaskText = value);
            Field("OutdoorText", "准备提示", s => s.Notification.OutdoorClassOnPreparingText, (s, value) => s.Notification.OutdoorClassOnPreparingText = value, multiline: true);
        });
        ReminderDetail("ClassOnDetails", "上课", "\uE787", s => s.Notification.IsClassOnNotificationEnabled ? "在课程开始时提醒" : "已关闭", () =>
        {
            Switch("ClassOn", "上课提醒", s => s.Notification.IsClassOnNotificationEnabled, (s, value) => s.Notification.IsClassOnNotificationEnabled = value);
            Field("ClassOnMask", "开场标题", s => s.Notification.ClassOnMaskText, (s, value) => s.Notification.ClassOnMaskText = value);
        });
        ReminderDetail("ClassOffDetails", "下课", "\uE916", s => s.Notification.IsClassOffNotificationEnabled ? "显示课间时长和下一节课" : "已关闭", () =>
        {
            Switch("ClassOff", "下课提醒", s => s.Notification.IsClassOffNotificationEnabled, (s, value) => s.Notification.IsClassOffNotificationEnabled = value);
            Field("ClassOffMask", "开场标题", s => s.Notification.ClassOffMaskText, (s, value) => s.Notification.ClassOffMaskText = value);
            Field("ClassOffText", "课间提示", s => s.Notification.ClassOffOverlayText, (s, value) => s.Notification.ClassOffOverlayText = value)
                .PlaceholderText = "可选";
        });
    }

    private void AfterSchoolPage()
    {
        Switch("AfterSchoolEnabled", "放学时发出提醒", s => s.Notification.IsAfterSchoolNotificationEnabled, (s, value) => s.Notification.IsAfterSchoolNotificationEnabled = value,
            "最后一节课下课时提醒；如果课后还排了课间，就在课间结束时提醒。");
        Field("AfterSchoolText", "放学提示", s => s.Notification.AfterSchoolText, (s, value) => s.Notification.AfterSchoolText = value, multiline: true)
            .PlaceholderText = NotificationSettings.DefaultAfterSchoolText;
        var button = ActionButton("PreviewAfterSchool", "预览", () => PlayReminderPreview(AfterSchool));
        Row("预览放学提醒", ReminderPreviewHint, button);
        BindPreviewAvailability(button);
    }

    private void ReminderDisplayPage()
    {
        Section("桌面课表");
        Switch("NotificationEffects", "提醒强调效果", s => s.AllowNotificationEffect, (s, value) => s.AllowNotificationEffect = value,
            "在提醒出现时播放强调动效；关闭不会隐藏提醒文字。");
        Switch("MinorNotifications", "允许次要提醒", s => s.AllowMinorNotification, (s, value) => s.AllowMinorNotification = value,
            "包括课前准备。关闭不会影响上课和下课提醒。");
        Switch("Teacher", "显示科任老师姓名", s => s.Notification.ShowTeacherName, (s, value) => s.Notification.ShowTeacherName = value,
            "使用课程档案中填写的教师姓名。");
        Section("声音");
        ReminderDetail("SoundDetails", "提醒铃声", "\uE767", s => s.Notification.IsSoundEnabled ? $"{_bridge.Status(ReminderSoundSource)} · {s.Notification.SoundVolume}%" : "静音", () =>
        {
            Switch("ReminderSound", "播放铃声", s => s.Notification.IsSoundEnabled, (s, value) => s.Notification.IsSoundEnabled = value);
            Scale("ReminderVolume", "音量", 0, 100, 1, s => s.Notification.SoundVolume, (s, value) => s.Notification.SoundVolume = (int)value, value => $"{value:0}%");
            StatusRow("CurrentReminderSound", "当前铃声", _ => _bridge.Status(ReminderSoundSource));
            AsyncRow("ReminderSoundFile", "自定义铃声", "选择 WAV 文件", PickReminderSound);
            ActionRow("DefaultReminderSound", "恢复默认铃声", "恢复默认", () => { _bridge.Command(ResetReminderSound); RefreshSettings(); });
        });
    }

    private void PlayReminderPreview(string command)
    {
        CommitInputs();
        if (_bridge.ReadSettings().AllowNotification) _bridge.Command(command);
    }

    private void BindPreviewAvailability(Button button)
    {
        _bindings.Add(settings =>
        {
            button.IsEnabled = settings.AllowNotification && settings.IsMainWindowVisible;
            var help = !settings.AllowNotification ? "请先启用课堂提醒。" : !settings.IsMainWindowVisible ? "请先从托盘显示桌面课表。" :
                "编辑课表浮窗、显示 NEX 面板或播放正式提醒时，暂时无法预览。";
            AutomationProperties.SetHelpText(button, help);
            ToolTipService.SetToolTip(button, help);
        });
    }

    private void ReminderDetail(string id, string title, string glyph, Func<AppSettings, string> describe, Action fill)
    {
        var body = new StackPanel { Spacing = 6 };
        var summary = SettingCaption("");
        var header = CardHeader(glyph, title, summary);
        header.Margin = new Thickness(0, 12, 0, 12);
        var expander = Target(id, title, new Expander { Header = header, Content = body, MinHeight = SettingsCardHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        _bindings.Add(settings =>
        {
            var value = describe(settings);
            if (summary.Text == value) return;
            summary.Text = value;
            AutomationProperties.SetHelpText(expander, value);
        });
        var parent = _body;
        _body = body;
        _currentExpander = expander;
        try { fill(); } finally { _body = parent; _currentExpander = null; }
        parent.Children.Add(expander);
    }
}
