using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClassNex.MaskProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 6 || args[0] is not ("reference" or "candidate")) throw new ArgumentException("side, runtime, original loader, source root, output, theme");
        var runtime = Path.GetFullPath(args[1]);
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var path = Path.Combine(name.Name == "Avalonia.Markup.Xaml.Loader" ? args[2] : runtime, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            var leaf = Path.HasExtension(name) ? name : name + ".dll";
            foreach (var folder in new[] { runtime, Path.Combine(runtime, "runtimes", "win-x64", "native") })
                if (NativeLibrary.TryLoad(Path.Combine(folder, leaf), out var handle)) return handle;
            return IntPtr.Zero;
        };
        try { Capture(args[0] == "reference", args[3], args[4], args[5]); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void BindAssets(Assembly island)
    {
        var standard = typeof(Avalonia.Platform.AssetLoader).Assembly.GetType("Avalonia.Platform.StandardAssetLoader", true)!;
        var overlay = Activator.CreateInstance(island.GetType("ClassIsland.OverlayAssetLoader", true)!,
            [Activator.CreateInstance(standard, [island]), island, "ClassIsland", "/Assets/", Path.Combine(Path.GetDirectoryName(island.Location)!, "Assets")]);
        Assembly.Load("ClassIsland.Desktop").GetType("ClassIsland.Desktop.Program", true)!.GetMethod("BindAssetLoader", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [overlay]);
    }

    private static Border ReferenceMask(string source, string text)
    {
        XNamespace ui = "https://github.com/avaloniaui", x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = XElement.Load(Path.Combine(source, "ClassIsland/XamlThemes/FluentTheme/Styles.axaml"));
        var mask = new XElement(style.Descendants(ui + "Border").Single(e => (string?)e.Attribute(x + "Name") == "OverlayMask"));
        mask.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        mask.SetAttributeValue(XNamespace.Xmlns + "controls", "clr-namespace:ClassIsland.Controls;assembly=ClassIsland");
        mask.SetAttributeValue("CornerRadius", "8");
        mask.Descendants().Single(e => e.Name.LocalName == "SlantedMaskControl").Name = XName.Get("SlantedMaskControl", "clr-namespace:ClassIsland.Controls;assembly=ClassIsland");
        var presenter = mask.Descendants(ui + "ContentPresenter").Single();
        presenter.Attribute("Content")!.Remove();
        presenter.Attribute("ContentTemplate")!.Remove();
        var styles = new XElement(ui + "Border.Styles");
        foreach (var (flag, stage) in new[] { ("in", "entering"), ("out", "revealing") })
        {
            var state = style.Descendants(ui + "Style").Single(e => (string?)e.Attribute("Selector") == "^:mask-" + flag);
            var animations = state.Elements(ui + "Style").Single(e => ((string?)e.Attribute("Selector"))?.EndsWith("#OverlayMaskContent", StringComparison.Ordinal) == true).Element(ui + "Style.Animations")!;
            styles.Add(new XElement(ui + "Style", new XAttribute("Selector", "ContentPresenter#OverlayMaskContent." + stage), new XElement(animations)));
        }
        mask.AddFirst(styles);
        var sample = (Border)AvaloniaRuntimeXamlLoader.Load(mask.ToString(SaveOptions.DisableFormatting), typeof(Program).Assembly, rootInstance: null, uri: new Uri("avares://NotificationMaskProbe/Mask.axaml"));
        var title = sample.FindControl<ContentPresenter>("OverlayMaskContent")!;
        var models = Assembly.Load("ClassIsland.Core");
        var data = Activator.CreateInstance(models.GetType("ClassIsland.Core.Models.Notification.Templates.TwoIconsMaskTemplateData", true)!)!;
        data.GetType().GetProperty("Text")!.SetValue(data, text);
        data.GetType().GetProperty("RightIconSource")!.SetValue(data, Activator.CreateInstance(models.GetType("ClassIsland.Core.Controls.LucideIconSource", true)!, ["\ue54f"]));
        var templates = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://ClassIsland.Core/Controls/NotificationTemplates/TwoIconsMaskTemplate.axaml"));
        title.ContentTemplate = (IDataTemplate)templates["NotificationTwoIconsMaskTemplate"]!;
        title.Content = data;
        return sample;
    }

    private static Window CandidateWindow(Assembly client)
    {
        var working = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        if (!File.Exists(Path.Combine(working, ".classnex-mask-probe")) || Directory.Exists(Path.Combine(working, "data"))) throw new InvalidDataException("Refusing to use a non-isolated settings directory");
        var services = client.GetType("ClassNex.Services.AppServices", true)!;
        services.GetMethod("Initialize")!.Invoke(null, null);
        var settings = services.GetProperty("Settings")!.GetValue(null)!;
        foreach (var (key, value) in new (string, object)[] { ("Topmost",false), ("ShowStartupSplash",false), ("FontScale",1d), ("MainWindowScale",1d) })
            settings.GetType().GetProperty(key)!.SetValue(settings, value);
        return (Window)Activator.CreateInstance(client.GetType("ClassNex.Views.MainWindow", true)!)!;
    }

    private static async Task Run(bool upstream, Assembly module, string source, string output, string variant)
    {
        const string text = "即将上课";
        const int width = 320, height = 40;
        var window = upstream ? new Window { Height = height, Width = width } : CandidateWindow(module);
        window.ShowInTaskbar = window.ShowActivated = window.CanResize = false;
        window.SystemDecorations = SystemDecorations.None;
        window.Opacity = 0;
        window.Position = new PixelPoint(-32000, -32000);
        var mask = upstream ? ReferenceMask(source, text) : window.FindControl<Border>("NotificationMask")!;
        if (upstream)
        {
            var zoom = new LayoutTransformControl { Child = mask };
            TextElement.SetFontFamily(zoom, new FontFamily("avares://ClassIsland/Assets/Fonts/#HarmonyOS Sans SC"));
            TextElement.SetFontWeight(zoom, FontWeight.Medium);
            TextElement.SetFontSize(zoom, 16);
            window.Content = zoom;
        }
        var title = upstream ? (Control)mask.FindControl<ContentPresenter>("OverlayMaskContent")! : window.FindControl<Border>("NotificationMaskContent")!;
        var slants = upstream ? mask.FindControl<Control>("SlantedMaskControlSeperated")! : window.FindControl<Control>("NotificationMaskBg")!;
        var clockType = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
        var clock = Activator.CreateInstance(clockType, true)!;
        var clockProperty = typeof(Animatable).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pulse = clockType.GetMethod("Pulse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var transforms = (TransformGroup)title.RenderTransform!;
        var scale = transforms.Children.OfType<ScaleTransform>().Single();
        foreach (var target in new Animatable[] { mask, slants, title, transforms, scale }) clockProperty.SetValue(target, clock);
        Directory.CreateDirectory(output);
        var shots = new List<object>();
        var viewport = new Rect(0, 0, width, height);
        var last = -1;
        try
        {
            window.Show();
            await Task.Delay(250);
            if (upstream) { title.Classes.Add("entering"); slants.GetType().GetProperty("IsOpened")!.SetValue(slants, true); }
            else window.GetType().GetMethod("ShowNotificationMask")!.Invoke(window, [text, "\ue0ff", "\ue54f", TimeSpan.Zero]);
            mask.Width = width;
            window.UpdateLayout();
            foreach (var (id, time) in new[] { ("opening",100), ("title-entering",330), ("open",1100) }) { Advance(time); Shot(id, time); }
            if (upstream)
            {
                title.Classes.Remove("entering");
                slants.GetType().GetProperty("IsOpened")!.SetValue(slants, false);
                title.Classes.Add("revealing");
            }
            else window.GetType().GetMethod("HideNotificationMask")!.Invoke(window, null);
            Advance(1100);
            foreach (var (id, time) in new[] { ("title-leaving",1200), ("closing",1400) }) { Advance(time); Shot(id, time - 1100); }
            if (window.ActualThemeVariant.ToString() != (variant == "dark" ? "Dark" : "Light")) throw new InvalidDataException("Requested theme not applied");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
                Module = module.GetName().Name, ModuleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.Location))),
                Avalonia = FileVersionInfo.GetVersionInfo(typeof(Window).Assembly.Location).ProductVersion,
                window.RenderScaling, Theme = variant, Shots = shots
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); }

        void Advance(int until)
        {
            for (var i = last < 0 ? 0 : last; i <= until; i += 5)
            {
                Dispatcher.UIThread.RunJobs();
                pulse.Invoke(clock, [TimeSpan.FromMilliseconds(i)]);
                Dispatcher.UIThread.RunJobs();
            }
            last = until;
        }

        void Shot(string id, int time)
        {
            mask.Measure(viewport.Size);
            mask.Arrange(viewport);
            var label = title.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);
            var origin = label.TranslatePoint(default, mask) ?? throw new InvalidDataException("Title not in mask");
            var regions = Enumerable.Range(0,5).Select(i => (double)slants.GetType().GetProperty($"Region{i}Progress")!.GetValue(slants)!).ToArray();
            shots.Add(new { Id = id, Time = time, Regions = regions, title.Opacity, scale.ScaleX, scale.ScaleY, TitleBrush = label.Foreground?.ToString(),
                Text = label.Text, X = origin.X, Y = origin.Y, LabelWidth = label.Bounds.Width, LabelHeight = label.Bounds.Height,
                Width = mask.Bounds.Width, Height = mask.Bounds.Height, Radius = mask.CornerRadius.ToString(), Fill = slants.GetType().GetProperty("Fill")!.GetValue(slants)?.ToString() });
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96,96));
            bitmap.Render(mask);
            bitmap.Save(Path.Combine(output, id + ".png"));
            var opacity = slants.Opacity;
            try
            {
                slants.Opacity = 0;
                using var titleLayer = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96,96));
                titleLayer.Render(mask);
                titleLayer.Save(Path.Combine(output, id + "-title.png"));
            }
            finally { slants.Opacity = opacity; }
        }
    }

    private static void Capture(bool upstream, string source, string output, string variant)
    {
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
        var module = Assembly.Load(upstream ? "ClassIsland" : "ClassNex");
        var appType = upstream ? typeof(Application) : module.GetType("ClassNex.App", true)!;
        var builder = AppBuilder.Configure<Application>(() => (Application)Activator.CreateInstance(appType)!).UseWin32().UseSkia()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        if (upstream) builder.AfterPlatformServicesSetup(_ => BindAssets(module));
        builder.SetupWithoutStarting();
        var app = Application.Current!;
        var skin = upstream ? "FluentAvalonia" : "AvaloniaFluentUI";
        var skinType = Assembly.Load(skin).GetType(skin + ".Styling.FluentAvaloniaTheme", true)!;
        var theme = upstream ? (IStyle)Activator.CreateInstance(skinType)! : app.Styles.Single(s => s.GetType() == skinType);
        skinType.GetProperty("PreferSystemTheme")!.SetValue(theme, false);
        skinType.GetProperty("PreferUserAccentColor")!.SetValue(theme, false);
        skinType.GetProperty("CustomAccentColor")!.SetValue(theme, Color.Parse("#459BAC"));
        if (upstream)
        {
            var align = skinType.GetProperty("TextVerticalAlignmentOverrideBehavior")!;
            align.SetValue(theme, Enum.Parse(align.PropertyType, "AlwaysEnabled"));
            app.Styles.Add(theme);
            app.Styles.Add((Styles)AvaloniaXamlLoader.Load(new Uri("avares://ClassIsland.Core/Themes/TextBaselineFixes.axaml")));
            app.Resources["MainWindowEmphasizedFontSize"] = 18d;
            app.Resources["IslandContainerHeight"] = 40d;
        }
        app.RequestedThemeVariant = variant switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => throw new ArgumentException(variant) };
        using var stop = new CancellationTokenSource();
        Exception? error = null;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Run(upstream, module, source, output, variant); }
            catch (Exception ex) { error = ex; }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        if (error is not null) throw new InvalidOperationException("Mask fixture", error);
    }
}
