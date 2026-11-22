using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class NexEdgeBarWindow : Window
{
    public event Action? WakeRequested;
    public bool IsLeftSide { get; set; }

    public NexEdgeBarWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyToolWindowStyle();
    }

    private void OnWake(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        WakeRequested?.Invoke();
    }

    private void ApplyToolWindowStyle()
    {
        if (TryGetPlatformHandle() is { } handle)
            WindowsOverlay.SetToolWindow(handle.Handle, true);
    }

    public override void Show()
    {
        ShowInTaskbar = false;
        base.Show();
        ApplyToolWindowStyle();
    }

    public void UpdateWindowPos(Avalonia.Platform.Screen screen)
    {
        var area = screen.WorkingArea;
        var x = IsLeftSide ? area.X : area.Right - (int)Math.Ceiling(Width * screen.Scaling);
        var y = area.Y + (area.Height - (int)Math.Ceiling(Height * screen.Scaling)) / 2;
        Position = new PixelPoint(x, y);
    }
}
