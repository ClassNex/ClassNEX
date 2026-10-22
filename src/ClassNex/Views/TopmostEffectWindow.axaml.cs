using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using ClassNex.Controls.NotificationEffects;
using ClassNex.Services;
using Control = Avalonia.Controls.Control;

namespace ClassNex.Views;


public sealed class TopmostEffectWindowViewModel
{
    public ObservableCollection<Control> EffectControls { get; } = new();
}






public partial class TopmostEffectWindow : Window
{
    public TopmostEffectWindowViewModel ViewModel { get; } = new();

    public bool IsShowed { get; set; }

    public TopmostEffectWindow()
    {
        InitializeComponent();
        DataContext = this;
        ViewModel.EffectControls.CollectionChanged += EffectControlsOnCollectionChanged;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
    }

    private void EffectControlsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Debug.WriteLine($"[TopmostEffectWindow] EffectControls.Count = {ViewModel.EffectControls.Count}");
        if (ViewModel.EffectControls.Count > 0)
        {
            if (IsShowed)
                return;
            Show();
            IsShowed = true;
        }
        else
        {
            if (!IsShowed)
                return;
            Hide();
            IsShowed = false;
        }
    }

    public void PlayEffect(INotificationEffectControl effect)
    {
        Debug.WriteLine($"[TopmostEffectWindow] 播放顶层特效：{effect}");
        if (effect is not Control element)
            return;
        if (AppServices.MainWindow?.IsEditMode == true)
        {
            Debug.WriteLine("[TopmostEffectWindow] 由于应用处于编辑模式，将丢弃当前顶层特效");
            return;
        }
        ViewModel.EffectControls.Add(element);
        if (!element.IsLoaded)
        {
            element.Loaded += (_, _) => SetupEffectVisual(element, effect);
        }
        else
        {
            SetupEffectVisual(element, effect);
        }
    }

    private void SetupEffectVisual(Control visual1, INotificationEffectControl effect)
    {
        effect.EffectCompleted += (_, _) =>
        {
            Debug.WriteLine("[TopmostEffectWindow] 结束播放并移除顶层特效");
            ViewModel.EffectControls.Remove(visual1);
        };
        effect.Play();
    }

    
    public void UpdateWindowPos(Screen screen, double scale, bool fullscreen)
    {
        var bounds = fullscreen ? screen.Bounds : screen.WorkingArea;
        Width = bounds.Width * scale - (fullscreen ? 1 : 0);
        Height = bounds.Height * scale;
        Position = new PixelPoint(bounds.X, bounds.Y);
    }

    private void TopmostEffectWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
        {
            return;
        }
        e.Cancel = true;
    }

    public override void Show()
    {
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        base.Show();

        
        
        if (TryGetPlatformHandle() is { } handle)
        {
            var hwnd = handle.Handle;
            WindowsOverlay.SetClickThrough(hwnd, true);
            WindowsOverlay.SetToolWindow(hwnd, true);
        }
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
    }
}
