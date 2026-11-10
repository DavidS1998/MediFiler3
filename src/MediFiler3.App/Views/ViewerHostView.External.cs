using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public partial class ViewerHostView
{
    private IExternalFileService? _externalFiles;
    private bool _externalLaunchInProgress;
    private DefaultApplicationResolver? _defaultApplication;
    private FileEntry? _associationFile;
    public ExternalOpenState ExternalOpenState => new(_active && _currentFile is not null,
        _externalLaunchInProgress, ReferenceEquals(_associationFile, _currentFile) ? _defaultApplication?.Application : null);

    private void ShowExternalFallback(FileEntry file, bool previewFailed = false)
    {
        ExternalFallbackTitle.Text = previewFailed ? "Preview unavailable" : "No integrated viewer";
        ExternalFileDetails.Text = $"{file.Name}\n{file.FullPath}";
        ExternalFallbackPanel.Visibility = Visibility.Visible;
        SetExternalStatus(ExternalActionNoticeText.Text);
        PresentationChanged?.Invoke();
    }

    private void EnsureDefaultApplication()
    {
        if (!_active || _currentFile is not { } file || ReferenceEquals(_associationFile, file)) return;
        _associationFile = file;
        if (_defaultApplication is not null) _ = _defaultApplication.ResolveAsync(file.FullPath);
    }

    private void SetExternalStatus(string message)
    {
        ExternalLaunchStatus.Text = ExternalFallbackPanel.Visibility == Visibility.Visible ? message : string.Empty;
        ExternalActionNoticeText.Text = message;
        ExternalActionNotice.Visibility = ExternalFallbackPanel.Visibility == Visibility.Visible || string.IsNullOrEmpty(message)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HideExternalFallback()
    {
        ExternalFallbackPanel.Visibility = Visibility.Collapsed;
        _associationFile = null;
        _defaultApplication?.Clear();
        PresentationChanged?.Invoke();
        SetExternalStatus(string.Empty);
        ExternalFileDetails.Text = string.Empty;
    }

    public async Task OpenExternallyAsync(ExternalFileAction action)
    {
        if (_externalFiles is null || !_active || _externalLaunchInProgress || _currentFile is not { } file) return;
        if (action == ExternalFileAction.DefaultApplication && !ExternalOpenState.CanOpenDefault) return;
        _externalLaunchInProgress = true;
        PresentationChanged?.Invoke();
        SetExternalStatus(action == ExternalFileAction.ChooseApplication ? "Choose an application in Windows…" : "Opening…");
        var window = Window.GetWindow(this);
        var owner = window is null ? 0 : new WindowInteropHelper(window).Handle;
        try
        {
            var result = await _externalFiles.OpenAsync(file.FullPath, action, owner);
            if (_active && ReferenceEquals(file, _currentFile))
            {
                SetExternalStatus(result.Message);
                if (_defaultApplication is not null) await _defaultApplication.ResolveAsync(file.FullPath);
            }
        }
        catch (Exception exception)
        {
            if (_active && ReferenceEquals(file, _currentFile))
                SetExternalStatus($"Could not open the file: {exception.Message}");
        }
        finally
        {
            _externalLaunchInProgress = false;
            PresentationChanged?.Invoke();
        }
    }

    private static bool IsButtonInput(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase) return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
