using System.Runtime.InteropServices;
using MediFiler3.Application.Operations;
using Microsoft.Win32.SafeHandles;

namespace MediFiler3.Infrastructure.Persistence;

public sealed class WindowsFileIdentityProvider : IFileIdentityProvider
{
    public FileIdentityLookup GetIdentity(string path)
    {
        using var handle = NativeMethods.CreateFile(path, 0x80, 0x7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            return error is 2 or 3
                ? new FileIdentityLookup(false, true, null)
                : new FileIdentityLookup(true, false, null);
        }

        if (!NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            return new FileIdentityLookup(true, false, null);
        }

        return new FileIdentityLookup(true, true,
            $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}");
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
