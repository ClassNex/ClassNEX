using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClassNex.RailProbe;

public class RailHost : Decorator
{
    public static readonly StyledProperty<double> CountdownProgressValueProperty =
        AvaloniaProperty.Register<RailHost, double>(nameof(CountdownProgressValue), 1);
    public double CountdownProgressValue
    {
        get => GetValue(CountdownProgressValueProperty);
        set => SetValue(CountdownProgressValueProperty, value);
    }
}

public sealed class MainWindowLine : RailHost;
public sealed class MainWindow : RailHost;

internal static class RailFixture
{
    private static readonly XNamespace Ui = "https://github.com/avaloniaui", Xaml = "http://schemas.microsoft.com/winfx/2006/xaml",
        Probe = "clr-namespace:ClassNex.RailProbe;assembly=NotificationRailProbe";

    public static int Run(string side, string source, string output, string themeName, string accent)
    {
        if (side is not ("reference" or "candidate")) throw new ArgumentOutOfRangeException(nameof(side));
        var reference = side == "reference";
        Logger.Sink = new DiagnosticsLog();
        AppBuilder.Configure<Application>().UseWin32().UseSkia()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] }).SetupWithoutStarting();
        var app = Application.Current!;
        var skin = reference ? "FluentAvalonia" : "AvaloniaFluentUI";
        var skinAssembly = Assembly.Load(skin);
        var skinType = skinAssembly.GetType(skin + ".Styling.FluentAvaloniaTheme", true)!;
        var theme = (IStyle)Activator.CreateInstance(skinType)!;
        skinType.GetProperty("PreferSystemTheme")!.SetValue(theme, false);
        skinType.GetProperty("PreferUserAccentColor")!.SetValue(theme, false);
        skinType.GetProperty("CustomAccentColor")!.SetValue(theme, Color.Parse(accent));
        app.Styles.Add(theme);
        app.RequestedThemeVariant = themeName switch { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => throw new ArgumentOutOfRangeException(nameof(themeName)) };

        var doc = XDocument.Load(source, LoadOptions.PreserveWhitespace);
        var canvas = doc.Descendants(Ui + "Canvas").Single(node => (string?)node.Attribute(Xaml + "Name") == "ProgressContainer" &&
            node.Descendants(Ui + "ProgressBar").Any(bar => (string?)bar.Attribute(Xaml + "Name") == "OverlayTimeProgressBar"));
        var hostType = reference ? "MainWindowLine" : "MainWindow";
        var hostPrefix = reference ? "controls" : "views";
        var snippet = new XElement(Probe + hostType,
            new XAttribute(XNamespace.Xmlns + hostPrefix, Probe.NamespaceName), new XAttribute("xmlns", Ui.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", Xaml.NamespaceName),
            new XElement(Ui + "Border", new XAttribute("Width", 320), new XAttribute("Height", 40),
                new XAttribute("Background", themeName == "dark" ? "#202020" : "#F4F4F4"), new XAttribute("CornerRadius", 8), new XAttribute("ClipToBounds", true),
                new XElement(Ui + "Panel", new XElement(canvas)))).ToString(SaveOptions.DisableFormatting);
        var baseUri = new Uri("avares://NotificationRailProbe/Views/MainWindow.axaml");
        var scopedStyles = new HashSet<string>(StringComparer.Ordinal);
        var documents = new List<RuntimeXamlLoaderDocument> { new(baseUri, snippet) };
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(source)))!;
        CollectResources(canvas, baseUri);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "fixture.axaml"), snippet);

        using var done = new CancellationTokenSource();
        Exception? failure = null;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Capture(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        if (failure != null) throw new InvalidOperationException("Rail probe failed: " + side, failure);
        return 0;

        async Task Capture()
        {
            var window = new Window
            {
                Width = 320, Height = 40, SystemDecorations = SystemDecorations.None, ShowInTaskbar = false,
                ShowActivated = false, CanResize = false, Opacity = 0, Position = new PixelPoint(-32000, -32000)
            };
            try
            {
                var host = AvaloniaRuntimeXamlLoader.LoadGroup(documents, new() { LocalAssembly = typeof(RailHost).Assembly })[0] as RailHost
                    ?? throw new InvalidDataException("Fixture root is not the progress source");
                if (!reference)
                {
                    var helper = Assembly.Load("ClassNex").GetType("ClassNex.Styles.ClassroomNotificationColors", true)!;
                    helper.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [host.GetLogicalDescendants().OfType<ProgressBar>().Single(), null, null]);
                }
                window.Content = host;
                window.Show();
                var shots = new List<object>();
                foreach (var (name, value) in new[] { ("100", 1d), ("050", .5d), ("025", .25d) })
                {
                    host.CountdownProgressValue = value;
                    var bar = host.GetVisualDescendants().OfType<ProgressBar>().Single();
                    var ready = Stopwatch.StartNew();
                    do
                    {
                        await Task.Delay(50);
                        window.UpdateLayout();
                    } while ((Math.Abs(bar.Value - value) > 0.001 || bar.Bounds.Width < 319 || bar.Bounds.Height <= 0) && ready.Elapsed < TimeSpan.FromSeconds(2));
                    await Task.Delay(100);
                    window.UpdateLayout();
                    var indicator = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
                    var start = bar.TranslatePoint(default, host) ?? throw new InvalidOperationException("Rail is detached");
                    var fill = indicator.TranslatePoint(default, host) ?? throw new InvalidOperationException("Indicator is detached");
                    if (Math.Abs(bar.Value - value) > 0.001 || bar.Bounds.Width < 319 || bar.Bounds.Height <= 0 || indicator.Bounds.Width < 1)
                        throw new InvalidOperationException($"Rail failed to settle: {value} -> {bar.Value}; {bar.Bounds}; {indicator.Bounds}; ancestor={host.GetType()} value={host.CountdownProgressValue}; priority={bar.GetDiagnostic(ProgressBar.ValueProperty).Priority}; base={bar.GetBaseValue(ProgressBar.ValueProperty)}");
                    using var bitmap = new RenderTargetBitmap(new PixelSize(320, 40), new Vector(96, 96));
                    bitmap.Render(host);
                    bitmap.Save(Path.Combine(output, name + ".png"));
                    shots.Add(new { Name = name, Value = bar.Value, X = start.X, Y = start.Y, bar.Bounds.Width, bar.Bounds.Height,
                        FillX = fill.X, FillY = fill.Y, FillWidth = indicator.Bounds.Width, FillHeight = indicator.Bounds.Height,
                        Foreground = bar.Foreground?.ToString(), Background = bar.Background?.ToString() });
                }
                var options = new JsonSerializerOptions { WriteIndented = true };
                var renderedBar = host.GetVisualDescendants().OfType<ProgressBar>().Single();
                var palette = new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                    "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3", "AccentFillColorDefaultBrush", "ProgressBarForeground", "ProgressBarBackground" }
                    .ToDictionary(key => key, key => renderedBar.TryFindResource(key, renderedBar.ActualThemeVariant, out var resource) ? resource?.ToString() : null);
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    Side = side, Avalonia = Product(typeof(ProgressBar).Assembly), Theme = Product(skinAssembly),
                    SourceCanvasSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canvas.ToString(SaveOptions.DisableFormatting)))),
                    ScopedStyles = scopedStyles, Shots = shots, Palette = palette, LayoutScaling = window.RenderScaling, Variant = renderedBar.ActualThemeVariant.ToString()
                }, options));
            }
            finally { window.Close(); }
        }
        void CollectResources(XElement owner, Uri origin)
        {
            foreach (var include in owner.Descendants(Ui + "ResourceInclude"))
            {
                var uri = new Uri(origin, (string)include.Attribute("Source")!);
                if (reference || uri.Authority != baseUri.Authority || !uri.AbsolutePath.StartsWith("/Styles/", StringComparison.Ordinal))
                    throw new InvalidDataException("Unrecognised fixture resource: " + uri);
                if (!scopedStyles.Add(uri.AbsolutePath)) continue;
                var body = File.ReadAllText(Path.Combine(projectRoot, uri.AbsolutePath[1..]));
                documents.Add(new RuntimeXamlLoaderDocument(uri, body));
                CollectResources(XElement.Parse(body), uri);
            }
        }
        static string Product(Assembly assembly) => FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion ?? "unknown";
    }

    private sealed class DiagnosticsLog : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Console.Error.WriteLine($"{area}: {messageTemplate}");
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] values) =>
            Console.Error.WriteLine($"{area}: {messageTemplate} [{string.Join(", ", values)}]");
    }
}
