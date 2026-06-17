using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboardRepeatFilter
{
    /// <summary>
    /// Detects when the foreground window belongs to a process running at a
    /// higher Windows integrity level than this one. A low-level keyboard hook
    /// installed from a medium-integrity process is silently skipped for input
    /// going to elevated ("Run as administrator") windows (UIPI), so while such a
    /// window is focused the filter simply cannot see, or fix, its keystrokes.
    /// This lets the app surface that state instead of appearing broken.
    /// </summary>
    internal static class ElevationDetector
    {
        private const uint TokenQuery = 0x0008;
        private const int TokenIntegrityLevel = 25;
        private const uint ProcessQueryLimitedInformation = 0x1000;

        // Our own integrity level, resolved once. -1 means "unknown".
        private static readonly int SelfIntegrity = GetSelfIntegrity();
        private static readonly int SelfPid = Process.GetCurrentProcess().Id;

        /// <summary>
        /// True when the foreground window's process outranks us, meaning our hook
        /// is being bypassed for it. Conservative: returns false whenever the
        /// integrity can't be positively determined, to avoid false alarms.
        /// </summary>
        public static bool ForegroundOutranksSelf(out string processName, out uint processId)
        {
            processName = null;
            processId = 0;
            if (SelfIntegrity < 0)
            {
                return false;
            }

            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)SelfPid)
            {
                return false;
            }

            int foreground = GetProcessIntegrity(pid);
            if (foreground < 0 || foreground <= SelfIntegrity)
            {
                return false;
            }

            processId = pid;
            processName = SafeProcessName(pid);
            return true;
        }

        private static string SafeProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName;
                }
            }
            catch
            {
                return "an elevated app";
            }
        }

        private static int GetSelfIntegrity()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out IntPtr hToken))
            {
                return -1;
            }

            try
            {
                return GetIntegrityFromToken(hToken);
            }
            finally
            {
                CloseHandle(hToken);
            }
        }

        private static int GetProcessIntegrity(uint pid)
        {
            IntPtr hProc = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (hProc == IntPtr.Zero)
            {
                return -1;
            }

            try
            {
                if (!OpenProcessToken(hProc, TokenQuery, out IntPtr hToken))
                {
                    return -1;
                }

                try
                {
                    return GetIntegrityFromToken(hToken);
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        private static int GetIntegrityFromToken(IntPtr hToken)
        {
            GetTokenInformation(hToken, TokenIntegrityLevel, IntPtr.Zero, 0, out uint size);
            if (size == 0)
            {
                return -1;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(hToken, TokenIntegrityLevel, buffer, size, out size))
                {
                    return -1;
                }

                var label = (TokenMandatoryLabel)Marshal.PtrToStructure(buffer, typeof(TokenMandatoryLabel));
                IntPtr pSid = label.Label.Sid;
                int subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(pSid));
                IntPtr pRid = GetSidSubAuthority(pSid, subAuthorityCount - 1);
                return Marshal.ReadInt32(pRid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SidAndAttributes
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenMandatoryLabel
        {
            public SidAndAttributes Label;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
            IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr GetSidSubAuthority(IntPtr pSid, int nSubAuthority);
    }
}
