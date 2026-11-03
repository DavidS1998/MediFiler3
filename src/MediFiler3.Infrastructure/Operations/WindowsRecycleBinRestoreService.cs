using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using MediFiler3.Application.Operations;
using MediFiler3.Infrastructure.Persistence;
using Microsoft.CSharp.RuntimeBinder;

namespace MediFiler3.Infrastructure.Operations;

/// <summary>Uses the user's Recycle Bin Shell namespace and its Restore verb; never edits $Recycle.Bin directly.</summary>
public sealed class WindowsRecycleBinRestoreService(IFileIdentityProvider identities) : IRecycleBinRestoreService
{
    public Task<RecycleBinRestoreResult> RestoreAsync(string originalPath, string itemIdentity, bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunOnStaAsync(() => RestoreOnSta(originalPath, itemIdentity, isDirectory, cancellationToken));
    }

    private RecycleBinRestoreResult RestoreOnSta(string originalPath, string itemIdentity, bool isDirectory,
        CancellationToken cancellationToken)
    {
        object? shell = null;
        object? recycleBin = null;
        object? shellItems = null;
        var entries = new List<(RecycleBinEntryDescriptor Descriptor, object ShellItem, string? RecycledPath)>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            shell = Activator.CreateInstance(shellType)
                ?? throw new COMException("Windows Shell could not be initialized.");
            dynamic shellObject = shell;
            recycleBin = shellObject.NameSpace(10);
            if (recycleBin is null) return Result(RecycleBinRestoreOutcome.Failed, "Windows could not open the Recycle Bin.");
            dynamic recycleBinObject = recycleBin;
            shellItems = recycleBinObject.Items();
            dynamic itemsObject = shellItems;
            var count = Convert.ToInt32(itemsObject.Count, System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object shellItem = itemsObject.Item(index);
                dynamic item = shellItem;
                var name = Convert.ToString(item.Name, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                var deletedFrom = Convert.ToString(item.ExtendedProperty("System.Recycle.DeletedFrom"),
                    System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                var path = GetOriginalPath(deletedFrom, name);
                var recycledPath = Convert.ToString(item.Path, System.Globalization.CultureInfo.InvariantCulture);
                var identity = string.IsNullOrWhiteSpace(recycledPath)
                    ? null
                    : identities.GetIdentity(recycledPath).Identity;
                var directory = Convert.ToBoolean(item.IsFolder, System.Globalization.CultureInfo.InvariantCulture);
                entries.Add((new RecycleBinEntryDescriptor(path, identity, directory), shellItem, recycledPath));
            }

            var match = RecycleBinItemMatcher.FindUnique(entries.Select(entry => entry.Descriptor).ToArray(),
                originalPath, itemIdentity, isDirectory);
            if (match.Outcome != RecycleBinMatchOutcome.Matched)
                return match.Outcome switch
                {
                    RecycleBinMatchOutcome.NotFound => Result(RecycleBinRestoreOutcome.NotFound,
                        "The matching item is no longer in the Recycle Bin."),
                    RecycleBinMatchOutcome.Ambiguous => Result(RecycleBinRestoreOutcome.Ambiguous,
                        "More than one Recycle Bin item matches this operation; nothing was restored."),
                    _ => Result(RecycleBinRestoreOutcome.IdentityMismatch,
                        "The Recycle Bin item at the recorded path has a different identity; nothing was restored.")
                };

            cancellationToken.ThrowIfCancellationRequested();
            var selectedEntry = entries[match.Index!.Value];
            var recycledFingerprint = !isDirectory && !string.IsNullOrWhiteSpace(selectedEntry.RecycledPath)
                ? TryGetFingerprint(selectedEntry.RecycledPath)
                : null;
            dynamic selectedItem = selectedEntry.ShellItem;
            selectedItem.InvokeVerb("undelete");
            var restoredContentVerified = false;
            if (recycledFingerprint is not null && File.Exists(originalPath))
                restoredContentVerified = recycledFingerprint == TryGetFingerprint(originalPath);
            return Result(RecycleBinRestoreOutcome.Restored,
                "Windows restored the Recycle Bin item.", restoredContentVerified);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is COMException or InvalidComObjectException or
                                           InvalidCastException or System.Reflection.TargetInvocationException or
                                           RuntimeBinderException or
                                           ArgumentException or IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException or InvalidOperationException)
        {
            return Result(RecycleBinRestoreOutcome.Failed, $"Windows could not restore the Recycle Bin item: {exception.Message}");
        }
        finally
        {
            foreach (var entry in entries) ReleaseComObject(entry.ShellItem);
            ReleaseComObject(shellItems);
            ReleaseComObject(recycleBin);
            ReleaseComObject(shell);
        }
    }

    private static string GetOriginalPath(string deletedFrom, string name)
    {
        var normalizedLocation = Path.TrimEndingDirectorySeparator(deletedFrom);
        var fullPath = string.Equals(Path.GetFileName(normalizedLocation), name, StringComparison.OrdinalIgnoreCase)
            ? normalizedLocation
            : Path.Combine(normalizedLocation, name);
        return Path.GetFullPath(fullPath);
    }

    private static Task<T> RunOnStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception exception) { completion.SetException(exception); }
        })
        {
            IsBackground = true,
            Name = "MediFiler3 Recycle Bin Shell"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static FileFingerprint? TryGetFingerprint(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                1024 * 1024, FileOptions.SequentialScan);
            return new FileFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    private static RecycleBinRestoreResult Result(RecycleBinRestoreOutcome outcome, string message,
        bool restoredContentVerified = false) => new(outcome, message, restoredContentVerified);

    private sealed record FileFingerprint(long Length, string Sha256);

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
