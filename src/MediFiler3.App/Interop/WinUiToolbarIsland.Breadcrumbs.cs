using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace MediFiler3.Interop;

public sealed partial class WinUiToolbarIsland
{
    // Experimental: set false to restore the original location text immediately.
    private static readonly bool FolderBreadcrumbsEnabled = true;
    private string[] _breadcrumbPaths = [];

    public event Action<string>? FolderBreadcrumbRequested;

    public void SetFolderBreadcrumbs(IEnumerable<string> paths)
    {
        var snapshot = paths.ToArray();
        if (_breadcrumbPaths.SequenceEqual(snapshot, StringComparer.OrdinalIgnoreCase)) return;
        _breadcrumbPaths = snapshot;
        UpdateFolderPath();
    }

    private bool TryRenderFolderBreadcrumbs()
    {
        if (!FolderBreadcrumbsEnabled || _folderDetails is null || _breadcrumbPaths.Length == 0 ||
            !string.Equals(_breadcrumbPaths[^1], _folderDetailsText, StringComparison.OrdinalIgnoreCase)) return false;

        var root = _breadcrumbPaths[0].TrimEnd('\\', '/');
        var rootName = System.IO.Path.GetFileName(root);
        if (string.IsNullOrEmpty(rootName)) rootName = _breadcrumbPaths[0];
        var prefixLength = Math.Max(0, root.Length - rootName.Length);
        if (prefixLength > 0)
            _folderDetails.Inlines.Add(new Run { Text = root[..prefixLength] });

        for (var index = 0; index < _breadcrumbPaths.Length; index++)
        {
            var path = _breadcrumbPaths[index];
            var name = index == 0 ? rootName : System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            if (index > 0) _folderDetails.Inlines.Add(new Run { Text = " › " });
            if (index == _breadcrumbPaths.Length - 1)
            {
                _folderDetails.Inlines.Add(new Run { Text = name, Foreground = Brush(255, 255, 255) });
                continue;
            }

            var link = new Hyperlink { Foreground = Brush(163, 166, 174) };
            link.Inlines.Add(new Run { Text = name });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, $"Navigate to {path}");
            ToolTipService.SetToolTip(link, path);
            link.Click += (_, _) => FolderBreadcrumbRequested?.Invoke(path);
            _folderDetails.Inlines.Add(link);
        }

        ToolTipService.SetToolTip(_folderDetails, _folderDetailsText);
        return true;
    }
}
