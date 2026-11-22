using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace ClassNex.WinUI;

public sealed class DesktopApplication : Application, IXamlMetadataProvider
{
    private XamlControlsXamlMetaDataProvider? _metadata;
    private readonly Action _start;
    private XamlControlsXamlMetaDataProvider Metadata => _metadata ??= new();

    private DesktopApplication(Action start)
    {
        _start = start;
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    public static void Run(Action start)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new DesktopApplication(start);
        });
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _start();
    }

    IXamlType IXamlMetadataProvider.GetXamlType(Type type) => Metadata.GetXamlType(type);
    IXamlType IXamlMetadataProvider.GetXamlType(string fullName) => Metadata.GetXamlType(fullName);
    XmlnsDefinition[] IXamlMetadataProvider.GetXmlnsDefinitions() => Metadata.GetXmlnsDefinitions();
}
