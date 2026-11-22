using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LifeAstroApi.Pdf.Engine
{
    /// <summary>
    /// A Windows job object that ends the browser and its helper processes when it is closed, which Windows
    /// also does when this process ends in any way. Node.js (and so Puppeteer) runs its children in one too.
    /// </summary>
    internal sealed class WindowsJob
    {
        private const int ExtendedLimitInformation = 9;
        private const int KillOnJobClose = 0x2000;
        private const int LimitFlagsOffset = 16;

        private IntPtr _handle;

        private WindowsJob(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>The job with the process in it, or null when Windows does not allow one.</summary>
        internal static WindowsJob? TryAssign(Process process)
        {
            IntPtr handle = CreateJobObjectW(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            // JOBOBJECT_EXTENDED_LIMIT_INFORMATION, zeroed but for LimitFlags.
            var limits = new byte[IntPtr.Size == 8 ? 144 : 112];
            BitConverter.GetBytes(KillOnJobClose).CopyTo(limits, LimitFlagsOffset);
            try
            {
                if (SetInformationJobObject(handle, ExtendedLimitInformation, limits, limits.Length)
                    && AssignProcessToJobObject(handle, process.Handle))
                {
                    return new WindowsJob(handle);
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception)
            {
                // The process ended already.
            }

            CloseHandle(handle);
            return null;
        }

        /// <summary>Ends every process still in the job.</summary>
        internal void Close()
        {
            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, byte[] information, int length);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
