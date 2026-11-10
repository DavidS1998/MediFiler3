using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using MediFiler3.Application.Files;

namespace MediFiler3.Infrastructure.Files;

public interface IWindowsFileLaunchAdapter
{
    int OpenDefault(string fullPath, nint ownerWindow);
    int OpenWith(string fullPath, nint ownerWindow);
}

public sealed class WindowsExternalFileService(IWindowsFileLaunchAdapter launcher) : IExternalFileService
{
    public Task<ExternalFileResult> OpenAsync(string path, ExternalFileAction action, nint ownerWindow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ExternalFileResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { completion.SetResult(Open(path, action, ownerWindow, cancellationToken)); }
            catch (OperationCanceledException) { completion.SetCanceled(cancellationToken); }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true, Name = "MediFiler3 external file launch" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private ExternalFileResult Open(string path, ExternalFileAction action, nint owner, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(action)) return new(ExternalFileOutcome.Failed, "Unknown file-opening action.");
            if (!Path.IsPathFullyQualified(path)) return new(ExternalFileOutcome.Failed, "A full local file path is required.");
            var fullPath = Path.GetFullPath(path);
            if ((File.GetAttributes(fullPath) & FileAttributes.Directory) != 0)
                return new(ExternalFileOutcome.Failed, "Select a file to open, rather than a folder.");
            token.ThrowIfCancellationRequested();
            if (action == ExternalFileAction.DefaultApplication)
            {
                var error = launcher.OpenDefault(fullPath, owner);
                return error switch
                {
                    0 => new(ExternalFileOutcome.Opened, "The file was sent to its default application."),
                    1223 => new(ExternalFileOutcome.Cancelled, "Opening cancelled."),
                    1155 or 31 => new(ExternalFileOutcome.NoAssociation, "No default application is associated with this file. Choose Open with… to select one."),
                    2 or 3 => Missing(),
                    _ => new(ExternalFileOutcome.Failed, $"Could not open the file: {new Win32Exception(error).Message}")
                };
            }
            var result = launcher.OpenWith(fullPath, owner);
            return result switch
            {
                0 => new(ExternalFileOutcome.Opened, "The file was sent to the selected application."),
                1 or unchecked((int)0x800704c7) or unchecked((int)0x80004004) => new(ExternalFileOutcome.Cancelled, "Opening cancelled."),
                unchecked((int)0x80070002) or unchecked((int)0x80070003) => Missing(),
                _ => new(ExternalFileOutcome.Failed, $"Could not open the application chooser: {Marshal.GetExceptionForHR(result)?.Message ?? "Windows reported an unknown error."}")
            };
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return Missing(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Security.SecurityException or Win32Exception or COMException)
        {
            return new(ExternalFileOutcome.Failed, $"Could not open the file: {exception.Message}");
        }
    }

    private static ExternalFileResult Missing() => new(ExternalFileOutcome.MissingFile,
        "The file no longer exists at this path. Refresh the folder and select another file.");
}
