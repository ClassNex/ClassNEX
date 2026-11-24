using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClassNex.RailProbe;

public sealed record LessonSubject(string Name, string TeacherName);
public sealed record LessonTimes(TimeSpan StartTime, TimeSpan EndTime, string BreakNameText);
public sealed record LessonInput(TimeSpan OnClassLeftTime, LessonSubject NextClassSubject, LessonTimes NextClassTimeLayoutItem)
{
    public LessonTimes CurrentTimeLayoutItem => NextClassTimeLayoutItem;
}
public sealed record ProviderInput(string Key, int Width, LessonInput LessonsService, bool ShowTeacherName, string Message, string NextTimeLayoutDurationHumanized)
{
    public string Text => Message;
    public string Time => FormattableString.Invariant($"{LessonsService.NextClassTimeLayoutItem.StartTime:hh\\:mm}–{LessonsService.NextClassTimeLayoutItem.EndTime:hh\\:mm}");
}

internal static class ProviderFixture
{
    private static readonly XNamespace Ui = "https://github.com/avaloniaui", Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static int _document;

    public static int Run(string side, string source, string output, string variant, string accent)
    {
        var reference = side == "provider-reference";
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        var appType = reference ? typeof(Application) : Assembly.Load("ClassNex").GetType("ClassNex.App", true)!;
        var builder = AppBuilder.Configure<Application>(() => (Application)Activator.CreateInstance(appType)!).UseWin32().UseSkia()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        if (reference) builder.AfterPlatformServicesSetup(_ => BindReferenceAssets());
        builder.SetupWithoutStarting();
        var app = Application.Current!;
        var skin = reference ? "FluentAvalonia" : "AvaloniaFluentUI";
        var skinType = Assembly.Load(skin).GetType(skin + ".Styling.FluentAvaloniaTheme", true)!;
        var theme = reference ? (IStyle)Activator.CreateInstance(skinType)! : app.Styles.Single(s => s.GetType() == skinType);
        skinType.GetProperty("PreferSystemTheme")!.SetValue(theme, false);
        skinType.GetProperty("PreferUserAccentColor")!.SetValue(theme, false);
        skinType.GetProperty("CustomAccentColor")!.SetValue(theme, Color.Parse(accent));
        if (reference)
        {
            var alignment = skinType.GetProperty("TextVerticalAlignmentOverrideBehavior")!;
            alignment.SetValue(theme, Enum.Parse(alignment.PropertyType, "AlwaysEnabled"));
            app.Styles.Add(theme);
            ReferenceStyles(app, source, variant);
        }
        app.RequestedThemeVariant = variant switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => throw new ArgumentException(variant) };
        Directory.CreateDirectory(output);
        using var done = new CancellationTokenSource();
        Exception? failure = null;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Capture(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        if (failure is not null) throw new InvalidOperationException(side, failure);
        return 0;

        async Task Capture()
        {
            var window = reference ? new Window() : CandidateWindow();
            window.ShowInTaskbar = window.ShowActivated = window.CanResize = false;
            window.Opacity = 0;
            window.SystemDecorations = SystemDecorations.None;
            window.Position = new PixelPoint(-32000, -32000);
            if (reference)
            {
                window.Height = 40;
            }
            var shots = new List<object>();
            try
            {
                window.Show();
                foreach (var (id, data) in Samples())
                {
                    Control host;
                    if (reference)
                    {
                        window.Width = data.Width;
                        var view = ReferenceBody(source, data);
                        host = new ContentPresenter { Content = data.Key == "simple" ? view : new UserControl { Content = view } };
                        var loader = new Border { Child = host };
                        TextElement.SetFontSize(loader, 16);
                        var zoom = new LayoutTransformControl { Child = loader };
                        TextElement.SetFontFamily(zoom, new FontFamily("avares://ClassIsland/Assets/Fonts/#HarmonyOS Sans SC"));
                        TextElement.SetFontWeight(zoom, FontWeight.Medium);
                        window.Content = zoom;
                    }
                    else host = CandidateBody(window, data);
                    await Task.Delay(400);
                    window.UpdateLayout();
                    var viewport = new Rect(0, 0, data.Width, 40);
                    host.Measure(viewport.Size);
                    host.Arrange(viewport);
                    var course = data.Key != "simple";
                    var surface = course ? host.GetVisualDescendants().OfType<Grid>().Single(g => g.ColumnDefinitions.Count == 2 &&
                        g.Children.OfType<TextBlock>().Any(t => t.Inlines?.OfType<Run>().Any() == true)) :
                        host.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == data.Message && t.IsEffectivelyVisible && t.Margin.Left == 24).Parent as Control
                            ?? throw new InvalidDataException("No populated simple provider");
                    var origin = new Point();
                    for (Visual? v = surface; v != host; v = v.GetVisualParent())
                    {
                        if (v is null) throw new InvalidDataException("Provider body is detached from its allocated row");
                        origin = new Point(origin.X + v.Bounds.X, origin.Y + v.Bounds.Y);
                    }
                    var bounds = new Rect(origin, surface.Bounds.Size);
                    if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.X < 0 || bounds.Y < 0 || bounds.Right > viewport.Right + .1 || bounds.Bottom > viewport.Bottom + .1)
                        throw new InvalidDataException($"{id}: provider body {bounds} is outside {viewport}");
                    if (surface is not Panel panel) throw new InvalidDataException("Provider body is not a panel");
                    var matte = new SolidColorBrush(Color.Parse(variant == "dark" ? "#202020" : "#F4F4F4"));
                    panel.Background = matte;
                    var text = surface.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
                        .Select(t =>
                        {
                            var origin = t.TranslatePoint(default, surface) ?? throw new InvalidDataException("Detached provider text");
                            var runs = t.Inlines?.OfType<Run>().Select(r => new { r.Text, Weight = r.FontWeight.ToString(), Brush = r.Foreground?.ToString() }).ToArray();
                            return new { Text = runs is { Length: > 0 } ? string.Concat(runs.Select(r => r.Text)) : t.Text,
                                origin.X, origin.Y, t.Bounds.Width, t.Bounds.Height, Size = t.FontSize, Weight = t.FontWeight.ToString(),
                                Font = t.FontFamily.ToString(), Brush = t.Foreground?.ToString(), Margin = t.Margin.ToString(), Runs = runs };
                        }).Where(t => !string.IsNullOrEmpty(t.Text)).ToArray();
                    var lesson = data.LessonsService.NextClassSubject;
                    if (data.Key == "simple" ? !text.Any(t => t.Text == data.Message) :
                        !text.Any(t => t.Text == lesson.Name) || !text.Any(t => t.Text == data.Time.Replace('–','-')) ||
                        text.Any(t => t.Text == lesson.TeacherName) != data.ShowTeacherName ||
                        !text.Any(t => t.Text!.Contains(data.Key == "prepare" ? "179" : "10 分钟", StringComparison.Ordinal)))
                        throw new InvalidDataException($"{id} did not display expected live text: {JsonSerializer.Serialize(text)}");
                    using var bodyImage = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height)), new Vector(96, 96));
                    bodyImage.Render(surface);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(data.Width, 40), new Vector(96, 96));
                    using (var dc = bitmap.CreateDrawingContext())
                    {
                        dc.DrawRectangle(matte, null, viewport);
                        dc.DrawImage(bodyImage, new Rect(bounds.Size), bounds);
                    }
                    bitmap.Save(Path.Combine(output, id + ".png"));
                    var hierarchy = surface.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible).GetSelfAndVisualAncestors().OfType<Control>()
                        .Select(c => new { Type=c.GetType().Name, c.Name, c.Bounds.X, c.Bounds.Y, c.Bounds.Width, c.Bounds.Height,
                            Margin=c.Margin.ToString(), VerticalScroll=(c as ScrollViewer)?.VerticalScrollBarVisibility.ToString(),
                            Size=TextElement.GetFontSize(c), Weight=TextElement.GetFontWeight(c).ToString() }).ToArray();
                    shots.Add(new { Id = id, data.Width, Height = 40, Body = new { bounds.X, bounds.Y, bounds.Width, bounds.Height }, Text = text, Hierarchy = hierarchy });
                }
                var model = reference ? Assembly.Load("ClassIsland.Core") : appType.Assembly;
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    Side = side, Runtime = Product(typeof(Window).Assembly), Theme = Product(skinType.Assembly), Module = Product(model),
                    ModuleSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(model.Location))),
                    window.RenderScaling, Variant = window.ActualThemeVariant.ToString(), Accent = accent, Shots = shots
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { window.Close(); }
        }
    }

    private static IEnumerable<(string, ProviderInput)> Samples()
    {
        var times = new LessonTimes(new TimeSpan(9, 5, 0), new TimeSpan(9, 50, 0), "大课间");
        foreach (var (id, key, width, subject, teacher) in new[] {
            ("prepare", "prepare", 560, "数学", false), ("prepare-teacher", "prepare", 860, "信息技术与人工智能", true),
            ("break", "break", 560, "语文", false), ("break-teacher", "break", 860, "科学与实验", true),
            ("dismissal", "simple", 560, "", false) })
            yield return (id, new(key, width, new(TimeSpan.FromSeconds(179), new(subject, "周老师"), times), teacher, "请带好随身物品，明天见。", "10 分钟"));
    }

    private static Window CandidateWindow()
    {
        var work = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        if (!File.Exists(Path.Combine(work, ".classnex-provider-probe")) || Directory.Exists(Path.Combine(work, "data")))
            throw new InvalidOperationException("An isolated, unused provider probe directory is required");
        var services = Assembly.Load("ClassNex").GetType("ClassNex.Services.AppServices", true)!;
        services.GetMethod("Initialize")!.Invoke(null, null);
        var settings = services.GetProperty("Settings")!.GetValue(null)!;
        foreach (var (key, value) in new (string, object)[] { ("MainWindowScale", 1d), ("FontScale", 1d), ("Topmost", false), ("ShowStartupSplash", false) })
            settings.GetType().GetProperty(key)!.SetValue(settings, value);
        return (Window)Activator.CreateInstance(services.Assembly.GetType("ClassNex.Views.MainWindow", true)!)!;
    }

    private static Control CandidateBody(Window window, ProviderInput data)
    {
        var type = window.GetType();
        var classroom = window.FindControl<Border>("CardContent") ?? throw new InvalidDataException("No candidate classroom surface");
        classroom.Width = data.Width - classroom.Margin.Left - classroom.Margin.Right;
        window.UpdateLayout();
        var course = data.Key != "simple";
        var publish = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Single(m => m.Name == "ShowNotificationOverlay" && m.GetParameters().Length == 3);
        var detailType = publish.GetParameters()[0].ParameterType;
        var next = data.LessonsService.NextClassSubject;
        var detail = Activator.CreateInstance(detailType, ["", course ? next.Name : "", data.ShowTeacherName ? next.TeacherName : "",
            course ? data.Time : "", course ? "下节课是" : "", data.Message, false, null])!;
        if (data.Key == "break") detailType.GetProperty("Break")!.SetValue(detail, (data.LessonsService.CurrentTimeLayoutItem.BreakNameText, data.NextTimeLayoutDurationHumanized));
        type.GetMethod("ShowNotificationMask")!.Invoke(window, ["课堂提醒", "\uE0FF", "\uE54F", TimeSpan.Zero]);
        publish.Invoke(window, [detail, data.Key == "prepare" ? 179 : null, null]);
        return window.FindControl<Border>("NotificationOverlay") ?? throw new InvalidDataException("No candidate overlay");
    }

    private static Control ReferenceBody(string root, ProviderInput data)
    {
        if (data.Key == "simple")
        {
            var source = Read(root, "ClassIsland.Core/Controls/NotificationTemplates/SimpleTextOverlayTemplate.axaml");
            var simple = Parse<Grid>(new XElement(source.Descendants(Ui + "DataTemplate").Single().Elements().Single()));
            simple.DataContext = data;
            return simple;
        }
        var provider = Read(root, "ClassIsland/Controls/NotificationProviders/ClassNotificationProviderControl.axaml");
        var templateKey = data.Key == "prepare" ? "ClassPrepareNotifyOverlay" : "ClassOffOverlay";
        var body = new XElement(provider.Descendants(Ui + "Grid").Single(e => (string?)e.Attribute(Xaml + "Key") == templateKey));
        body.Attribute(Xaml + "Key")!.Remove();
        var view = new XElement(provider.Elements(Ui + "Grid").Single());
        view.Attribute("DataContext")!.Remove();
        var presenter = view.Descendants(Ui + "ContentPresenter").Single();
        presenter.Attribute("Content")!.Remove();
        presenter.Add(new XElement(Ui + "ContentPresenter.Content", body));
        view.Descendants(Ui + "TextBlock").Single(e => ((string?)e.Attribute("Text"))?.Contains("{Binding Message,", StringComparison.Ordinal) == true).SetAttributeValue("Text", "{Binding Message}");
        view.SetAttributeValue(XNamespace.Xmlns + "x", Xaml.NamespaceName);
        view.SetAttributeValue(XNamespace.Xmlns + "converters1", provider.GetNamespaceOfPrefix("converters1")!.NamespaceName);
        var converter = provider.Descendants().Single(e => (string?)e.Attribute(Xaml + "Key") == "TimeSpanToTotalSecondsConverter");
        view.AddFirst(new XElement(Ui + "Grid.Resources", new XElement(converter)));
        var control = Parse<Grid>(view);
        control.DataContext = data;
        control.Children.OfType<ListBox>().Single().SelectedIndex = 0;
        return control;
    }

    private static void ReferenceStyles(Application app, string root, string variant)
    {
        foreach (var name in new[] { "SlidingListBox", "Layers" }) app.Styles.Add(Parse<Styles>(Read(root, $"ClassIsland.Core/Themes/{name}.axaml")));
        var baseline = Read(root, "ClassIsland.Core/Themes/TextBaselineFixes.axaml");
        var style = new XElement(baseline.Elements(Ui + "Style").Single(e => ((string?)e.Attribute("Selector"))?.Contains("ListBoxItem TextBlock") == true));
        app.Styles.Add(Parse<Styles>(new XElement(Ui + "Styles", baseline.Attributes().Where(a => a.IsNamespaceDeclaration), style)));
        foreach (var (name, value) in new[] { ("Body",16d), ("Secondary",14d), ("Emphasized",18d), ("Large",20d) })
            app.Resources[$"MainWindow{name}FontSize"] = value;
        var dictionary = Read(root, $"ClassIsland/Themes/{(variant == "dark" ? "Dark" : "Light")}Theme.axaml");
        var brush = dictionary.Descendants(Ui + "SolidColorBrush").Single(e => (string?)e.Attribute(Xaml + "Key") == "TextFillColorInformativeSecondaryBrush");
        app.Resources["TextFillColorInformativeSecondaryBrush"] = new SolidColorBrush(Color.Parse((string)brush.Attribute("Color")!));
    }
    private static void BindReferenceAssets()
    {
        var assembly = Assembly.Load("ClassIsland");
        var defaults = typeof(Avalonia.Platform.AssetLoader).Assembly.GetType("Avalonia.Platform.StandardAssetLoader", true)!;
        var loader = Activator.CreateInstance(assembly.GetType("ClassIsland.OverlayAssetLoader", true)!,
            [Activator.CreateInstance(defaults, [assembly]), assembly, "ClassIsland", "/Assets/", Path.Combine(Path.GetDirectoryName(assembly.Location)!, "Assets")]);
        Assembly.Load("ClassIsland.Desktop").GetType("ClassIsland.Desktop.Program", true)!
            .GetMethod("BindAssetLoader", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [loader]);
    }
    private static XElement Read(string root, string path) => XElement.Load(Path.Combine(root, path), LoadOptions.PreserveWhitespace);
    private static T Parse<T>(XElement root) => (T)AvaloniaRuntimeXamlLoader.LoadGroup([new RuntimeXamlLoaderDocument(
        new Uri($"avares://NotificationRailProbe/Sample{_document++}.axaml"), root.ToString(SaveOptions.DisableFormatting))],
        new() { LocalAssembly = typeof(ProviderFixture).Assembly })[0]!;
    private static string Product(Assembly assembly) => FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion ?? "unknown";
}
