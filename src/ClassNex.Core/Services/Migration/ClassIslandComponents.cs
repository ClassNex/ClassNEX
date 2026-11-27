using System.Globalization;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

internal static class IslandJson
{
    internal static JsonElement Field(this JsonElement o, string key) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(key, out var v) ? v : default;
    internal static string? Str(this JsonElement o, string key) => o.Field(key) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    internal static bool Flag(this JsonElement o, string key, bool fallback = false) => o.Field(key).ValueKind switch
        { JsonValueKind.True => true, JsonValueKind.False => false, _ => fallback };
    internal static int Int(this JsonElement o, string key, int fallback = 0) => o.Field(key) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : fallback;
    internal static double Num(this JsonElement o, string key, double fallback, double min, double max) => o.Field(key) is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var n) && double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
    internal static JsonElement[] Items(this JsonElement o, string key) => o.Field(key) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().ToArray() : [];
    internal static KeyValuePair<Guid, JsonElement>[] Map(this JsonElement o, string key) => o.Field(key) switch
    {
        { ValueKind: JsonValueKind.Object } obj => obj.EnumerateObject().Select(x => Guid.TryParse(x.Name, out var g) && x.Value.ValueKind == JsonValueKind.Object
            ? KeyValuePair.Create(g, x.Value) : throw new InvalidDataException("档案字典无效：" + key)).ToArray(),
        { ValueKind: JsonValueKind.Undefined } => [],
        _ => throw new InvalidDataException("档案字典无效：" + key)
    };
    internal static Guid Id(this JsonElement o, string key, Guid fallback = default) => Guid.TryParse(o.Str(key), out var id) ? id : fallback;
    internal static string? Color(this JsonElement o, string key) => WidgetPresentationImport.ClassIslandColor(o.Field(key));
}

public static partial class ClassIslandImporter
{
    private static void ReadAppearance(JsonElement s, ClassIslandImportPlan plan)
    {
        var a = plan.Appearance;
        a.MainWindowScale = s.Num("Scale", 1, .5, 4);
        a.BackgroundOpacity = s.Num("Opacity", .5, 0, 1);
        a.FontScale = s.Num("MainWindowBodyFontSize", 16, 8, 48) / 16;
        a.ThemeMode = s.Int("Theme", 2) switch { 1 => "light", 2 => "dark", _ => "system" };
        var palette = s.Items("WallpaperColorPlatte");
        var swatch = s.Int("SelectedPlatteIndex");
        a.ThemeAccentColor = s.Int("ColorSource", 2) switch
        {
            0 => s.Color("PrimaryColor") ?? "#FF00BFFF",
            1 or 3 => (swatch >= 0 && swatch < palette.Length ? WidgetPresentationImport.ClassIslandColor(palette[swatch]) : null) ?? s.Color("SelectedPlatte") ?? "#FF1E90FF",
            _ => "system"
        };
        a.OverlayBackgroundColor = s.Flag("IsCustomBackgroundColorEnabled") ? s.Color("BackgroundColor") ?? "#FF000000" : null;
        a.OverlayForegroundColor = s.Flag("IsCustomForegroundColorEnabled") ? s.Color("CustomForegroundColor") ?? "#FF1E90FF" : null;
        a.OverlayCornerRadius = s.Num("RadiusX", 8, 0, 256);
        a.IsMainWindowVisible = s.Flag("IsMainWindowVisible", true);
        a.Topmost = s.Int("WindowLayer", 1) == 1;
        a.IsClickThrough = !s.Flag("IsMouseClickingEnabled");
        a.HoverOpacity = s.Flag("IsMouseInFadingEnabled", true) ? .05 : 1;
        a.SingleWeekStartTime = s.Field("SingleWeekStartTime") is { ValueKind: JsonValueKind.String } d && d.TryGetDateTime(out var start)
            ? start.Date : DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        var offsets = s.Items("MultiWeekRotationOffset");
        a.WeekRotationOffset = offsets.Length > 2 && offsets[2].ValueKind == JsonValueKind.Number && offsets[2].TryGetInt32(out var offset) ? offset : 0;
        a.DefaultOnClassTimePointMinutes = Math.Clamp(s.Int("DefaultOnClassTimePointMinutes", 40), 1, 1440);
        a.DefaultBreakingTimePointMinutes = Math.Clamp(s.Int("DefaultBreakingTimePointMinutes", 10), 1, 1440);
        a.ImportedDock = new(Math.Clamp(s.Int("WindowDockingLocation", 1), 0, 5), s.Int("WindowDockingOffsetX"), s.Int("WindowDockingOffsetY"), Math.Clamp(s.Int("WindowDockingMonitorIndex"), 0, 63));
    }

    private static void CopyAppearance(AppSettings a, AppSettings s)
    {
        s.MainWindowScale = a.MainWindowScale; s.FontScale = a.FontScale; s.BackgroundOpacity = a.BackgroundOpacity;
        s.ThemeMode = a.ThemeMode; s.ThemeAccentColor = a.ThemeAccentColor;
        s.OverlayBackgroundColor = a.OverlayBackgroundColor; s.OverlayForegroundColor = a.OverlayForegroundColor; s.OverlayCornerRadius = a.OverlayCornerRadius;
        s.IsMainWindowVisible = a.IsMainWindowVisible; s.Topmost = a.Topmost; s.HoverOpacity = a.HoverOpacity; s.IsClickThrough = a.IsClickThrough;
        s.ImportedDock = a.ImportedDock; s.MainWindowLeft = s.MainWindowTop = 80;
        s.DefaultOnClassTimePointMinutes = a.DefaultOnClassTimePointMinutes; s.DefaultBreakingTimePointMinutes = a.DefaultBreakingTimePointMinutes;
        s.SingleWeekStartTime = a.SingleWeekStartTime; s.WeekRotationOffset = a.WeekRotationOffset;
        s.Orientation = a.Orientation;
    }

    private static void ReadWidgets(JsonElement layout, ClassIslandImportPlan plan)
    {
        if (layout.Field("Lines").ValueKind != JsonValueKind.Array) throw new InvalidDataException("无法识别组件布局（缺少 Lines）。");
        var lines = layout.Items("Lines");
        for (var row = 0; row < lines.Length; row++)
        {
            var line = lines[row];
            if (line.Field("Children").ValueKind != JsonValueKind.Array) throw new InvalidDataException("无法读取组件行。");
            foreach (var child in line.Items("Children")) Add(child, line.Flag("IsVisible", true), row);
        }

        void Add(JsonElement node, bool visible, int row)
        {
            if (plan.Widgets.Count + plan.SkippedWidgetCount >= 4096) throw new InvalidDataException("组件实例过多。");
            visible &= node.Flag("IsVisible", true);
            var options = node.Field("Settings");
            var type = node.Id("Id").ToString() switch
            {
                "9e1af71d-8f77-4b21-a342-448787104dd9" => "clock", "df3f8295-21f6-482e-bada-fa0e5f14bb66" => "date",
                "1db2017d-e374-4bc6-9d57-0b4adf03a6b8" or "e7831603-61a0-4180-b51b-54ad75b1a4d3" => "schedule",
                "ab0f26d5-9df6-4575-b844-73b04d0907c1" => "divider", "ee8f66bd-c423-4e7c-ab46-aa9976b00e08" => "text",
                "7c645d35-8151-48ba-b4ac-15017460d994" => "countdown",
                "c911d762-107f-40c6-84cc-0146ab3c86b1" or "70fcd5ea-3fae-4e06-aca2-4f4df47f9acd" or "7e19a113-d281-4f33-970a-834a0b78b5ad" or "2d849ece-9f21-4c78-9434-415cfc283294" => "container",
                _ => null
            };
            if (type == "container")
            {
                plan.Warn("容器本身没有对应组件；其中可用的组件按原顺序展开。");
                foreach (var child in options.Items("Children")) Add(child, visible, row);
                return;
            }
            if (type == null) { plan.SkippedWidgetCount++; return; }
            if (type == "countdown" && options.Int("CountdownSource") != 0)
            {
                plan.SkippedWidgetCount++; plan.Warn("周期与按日／周动态倒计时没有对应组件，已跳过。"); return;
            }
            var w = new WidgetConfig { Type = type, IsEnabled = visible, Row = row, Order = plan.Widgets.Count, Presentation = WidgetPresentationImport.Read(node) };
            if (node.Flag("HideOnRule")) plan.Warn("组件的条件隐藏规则不会执行，保留静态可见状态。");
            if (node.Flag("IsResourceOverridingEnabled"))
            {
                var font = type == "clock" ? "MainWindowLargeFontSize" : "MainWindowBodyFontSize";
                var basis = type == "clock" ? 20 : 16;
                w.FontScale = node.Num(font, basis, 5, 96) / basis / plan.Appearance.FontScale;
            }
            if (node.Flag("IsCustomForegroundColorEnabled")) { w.UseCustomFontColor = true; w.FontColor = node.Color("ForegroundColor") ?? "#FF1E90FF"; }
            switch (type)
            {
                case "clock":
                    w.ShowSeconds = options.Flag("ShowSeconds"); w.ShowRealTime = options.Flag("ShowRealTime"); w.FlashTimeSeparator = options.Flag("FlashTimeSeparator", true); break;
                case "schedule":
                    w.FadeCompletedClasses = options.Flag("FadeCompletedClasses", true); w.ScheduleSpacing = options.Num("ScheduleSpacing", 1, .5, 2);
                    w.ShowCurrentLessonOnlyOnClass = options.Flag("ShowCurrentLessonOnlyOnClass"); w.HideFinishedClass = options.Flag("HideFinishedClass");
                    w.CountdownSeconds = options.Flag("IsCountdownEnabled", true) ? Math.Clamp(options.Int("CountdownSeconds", 60), 0, 86400) : 0;
                    w.ShowTimeDetails = options.Flag("ShowExtraInfoOnTimePoint", true);
                    if (options.Flag("ShowTomorrowSchedules") || options.Int("ExtraInfoType") != 0) plan.Warn("课表组件的明日课表和扩展信息格式仍使用 ClassNEX 的展示方式。");
                    break;
                case "text":
                    w.Text = options.Str("TextContent") ?? "";
                    w.ImportedLiteralText = true;
                    w.FontScale = options.Num("FontSize", 16, 5, 96) / 14.5 / plan.Appearance.FontScale;
                    if (options.Flag("UseCustomFontColor", true)) { w.UseCustomFontColor = true; w.FontColor = options.Color("FontColor") ?? "#FFFFFFFF"; }
                    break;
                case "countdown":
                    w.CountdownName = options.Str("CountDownName") ?? "倒计时";
                    w.CountdownTarget = options.Field("OverTime") is { ValueKind: JsonValueKind.String } t && t.TryGetDateTimeOffset(out var target) ? target : new DateTimeOffset(DateTime.Today);
                    w.FontScale = options.Num("FontSize", 16, 5, 96) / 13.5 / plan.Appearance.FontScale;
                    w.UseCustomFontColor = true; w.FontColor = options.Color("FontColor") ?? "#FFFF0000";
                    if (options.Flag("ShowProgress") || options.Str("CustomStringFormat") is string f && f != "%D天") plan.Warn("倒计时保留目标、名称和文字颜色；自定义格式和进度图不迁移。");
                    break;
            }
            plan.Widgets.Add(w);
        }
    }
}
