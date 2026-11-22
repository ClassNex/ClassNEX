namespace ClassNex;

public partial class App
{
    internal static void OpenAccountSwitch()
    {
        OpenLoginWindow();
        (Current as App)?._loginWindow?.BeginAccountSwitch();
    }
}
