using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private readonly AccessibilitySettings _accessibility = new();
    private readonly SolidColorBrush _clearSurface = new(Microsoft.UI.Colors.Transparent);
    private ThemeSettings? _windowTheme;
    private DispatcherQueue? _surfaceDispatcher;

#if DEBUG
    private bool? _reviewEffects, _reviewContrast, _reviewSupported, _reviewAnimations;

    public void SetSystemEffectsForReview(bool? advancedEffects = null, bool? highContrast = null, bool? micaSupported = null, bool? animationsEnabled = null)
    {
        static bool Marker(string path, string value) => File.Exists(path) && File.ReadAllText(path) == value;
        if (!Marker(Path.Combine(ClassNex.Services.SettingsService.DataDirectory, ".native-verify"), "disposable") &&
            !Marker(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review"), "isolated desktop review"))
            throw new InvalidOperationException("This check is restricted to an isolated desktop review.");
        (_reviewEffects, _reviewContrast, _reviewSupported, _reviewAnimations) = (advancedEffects, highContrast, micaSupported, animationsEnabled);
        RefreshSystemEffects();
    }
#endif

    private bool HighContrast =>
#if DEBUG
        _reviewContrast ??
#endif
        _accessibility.HighContrast;

    private bool AnimationsEnabled =>
#if DEBUG
        _reviewAnimations ??
#endif
        _motion.AnimationsEnabled;

    private void ConfigureSurfaceResources()
    {
        _shell.Style = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Grid">
              <Setter Property="Background" Value="{ThemeResource SolidBackgroundFillColorBaseBrush}"/>
            </Style>
            """);
        foreach (var theme in new[] { "Default", "Light" })
            _navigation.Resources.ThemeDictionaries[theme] = new ResourceDictionary
            {
                ["NavigationViewContentBackground"] = _clearSurface,
                ["NavigationViewContentGridBorderBrush"] = _clearSurface
            };
    }

    private void ObserveSystemEffects()
    {
        _surfaceDispatcher = DispatcherQueue;
        _motion.AdvancedEffectsEnabledChanged += OnSystemEffectsChanged;
        _motion.ColorValuesChanged += OnSystemEffectsChanged;
        _windowTheme = ThemeSettings.CreateForWindowId(AppWindow.Id);
        _windowTheme.Changed += OnSystemEffectsChanged;
        _shell.ActualThemeChanged += OnShellThemeChanged;
        _shell.Loaded += OnShellLoaded;
        RefreshSystemEffects();
    }

    private void StopObservingSystemEffects()
    {
        _motion.AdvancedEffectsEnabledChanged -= OnSystemEffectsChanged;
        _motion.ColorValuesChanged -= OnSystemEffectsChanged;
        if (_windowTheme is not null) _windowTheme.Changed -= OnSystemEffectsChanged;
        _windowTheme = null;
        _shell.ActualThemeChanged -= OnShellThemeChanged;
        _shell.Loaded -= OnShellLoaded;
        _surfaceDispatcher = null;
    }

    private void OnSystemEffectsChanged(object sender, object args) => _surfaceDispatcher?.TryEnqueue(RefreshSystemEffects);
    private void OnShellThemeChanged(FrameworkElement sender, object args) => RefreshSystemEffects();
    private void OnShellLoaded(object sender, RoutedEventArgs args) => RefreshSystemEffects();

    private void RefreshSystemEffects()
    {
        if (_closed) return;
        var contrast = HighContrast;
        var effects = _motion.AdvancedEffectsEnabled;
        var mica = MicaController.IsSupported();
#if DEBUG
        effects = _reviewEffects ?? effects;
        mica = mica && _reviewSupported != false;
#endif
        if (!contrast && effects && mica)
        {
            if (SystemBackdrop is not MicaBackdrop) SystemBackdrop = new MicaBackdrop();
            _shell.Background = _clearSurface;
        }
        else
        {
            if (SystemBackdrop is not null) SystemBackdrop = null;
            if (contrast)
            {
                var color = _motion.UIElementColor(UIElementType.Window);
                if (_shell.Background is not SolidColorBrush brush || brush.Color != color) _shell.Background = new SolidColorBrush(color);
            }
            else _shell.ClearValue(Grid.BackgroundProperty);
        }
        MatchCaptionTheme();
    }
}
