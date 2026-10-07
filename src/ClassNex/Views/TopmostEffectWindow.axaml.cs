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

/// <summary>顶层效果窗口的极简视图模型（对照 CI 的 TopmostEffectWindowViewModel）。</summary>
public sealed class TopmostEffectWindowViewModel
{
    public ObservableCollection<Control> EffectControls { get; } = new();
}

/// <summary>
/// 1:1 移植自 CI（ClassIsland/Views/TopmostEffectWindow.axaml.cs）：
/// 全屏透明、置顶、点击穿透的工具窗口，承载全局特效（重要通知的水波纹）。
/// 有特效时显示，最后一个特效播完自动隐藏。
/// </summary>
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

    /// <summary>把窗口铺到指定屏幕（CI：fullscreen 用 Bounds，否则 WorkingArea）。</summary>
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

        // CI：SetWindowFeature(Transparent | ToolWindow | Topmost | SkipManagement, true)
        // = WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE（点击穿透、不抢焦点）+ WS_EX_TOOLWINDOW（不进 Alt+Tab）
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
