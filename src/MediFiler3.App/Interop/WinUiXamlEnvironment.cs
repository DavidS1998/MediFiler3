using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace MediFiler3.Interop;

internal static class WinUiXamlEnvironment
{
    private static DispatcherQueueController? _dispatcherQueueController;
    private static WindowsXamlManager? _xamlManager;
    private static WinUiXamlApplication? _application;
    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        if (_application is not null)
        {
            return;
        }

        _dispatcherQueueController = DispatcherQueueController.CreateOnCurrentThread();
        _application = new WinUiXamlApplication();
        _xamlManager = WindowsXamlManager.InitializeForCurrentThread();
        _application.InitializeResources();
        WinUiInputMessageFilter.Install();
        IsInitialized = true;
    }

    public static void Shutdown()
    {
        IsInitialized = false;
        _xamlManager?.Dispose();
        _xamlManager = null;
        _application = null;

        _dispatcherQueueController?.ShutdownQueue();
        _dispatcherQueueController = null;
    }

    private sealed class WinUiXamlApplication : Microsoft.UI.Xaml.Application, IXamlMetadataProvider
    {
        private readonly XamlControlsXamlMetaDataProvider _metadataProvider = new();

        public void InitializeResources() => Resources.MergedDictionaries.Add(new XamlControlsResources());

        public IXamlType GetXamlType(Type type) => _metadataProvider.GetXamlType(type);

        public IXamlType GetXamlType(string fullName) => _metadataProvider.GetXamlType(fullName);

        public XmlnsDefinition[] GetXmlnsDefinitions() => _metadataProvider.GetXmlnsDefinitions();
    }
}
