using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardRepeatFilter
{
    // Watches for a known game process starting or stopping and raises an event
    // when the "is a game running" state flips. Detection only: it never touches
    // profiles itself; Program decides what to do with the events.
    //
    // Part of the game-profile-switching feature contributed by GitHub user
    // Timmaykc (https://github.com/Timmaykc).
    //
    // The poll runs on a WinForms Timer, so Tick (and therefore every event this
    // raises) fires on the UI thread. That lets Program activate profiles, restart
    // the hook, and update the tray directly from the handlers with no cross-thread
    // marshalling, matching how the app's other timers already work. Enumeration
    // uses a Toolhelp snapshot rather than Process.GetProcesses() so a poll costs a
    // single system call and no per-process managed objects or handles.
    internal sealed class GameProfileWatcher
    {
        private readonly System.Windows.Forms.Timer _timer;

        // Executable names (e.g. "wow.exe") that count as games, compared case-
        // insensitively. Empty means "detect nothing" (no games.txt yet and no
        // mapped games), in which case the watcher simply never fires.
        private HashSet<string> _known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The game currently considered running, or null. Only transitions
        // none->some and some->none raise events; a switch from one game straight to
        // another (rare) is ignored to avoid profile thrash.
        private string _current;

        // Raised with the game's exe name when a known game starts (none -> running).
        public event Action<string> GameStarted;
        // Raised when the last known game stops (running -> none).
        public event Action GameStopped;

        public string RunningGame => _current;

        public GameProfileWatcher(int intervalMs = 3000)
        {
            _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
            _timer.Tick += (_, __) => Poll();
        }

        // Replaces the set of executables treated as games. Called at startup and
        // again after the game list is refreshed from the tray.
        public void SetKnownGames(HashSet<string> known)
        {
            _known = known ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Start() => _timer.Start();

        // Stops polling and forgets the current game silently (no GameStopped),
        // because the caller that disables the watcher handles any revert itself.
        // Clearing _current means a later Start() re-detects a game that is still
        // running instead of treating it as already handled.
        public void Stop()
        {
            _timer.Stop();
            _current = null;
        }

        private void Poll()
        {
            if (_known.Count == 0)
            {
                if (_current != null)
                {
                    _current = null;
                    GameStopped?.Invoke();
                }
                return;
            }

            string found = FindRunningKnownGame();

            if (found != null && _current == null)
            {
                _current = found;
                GameStarted?.Invoke(found);
            }
            else if (found == null && _current != null)
            {
                _current = null;
                GameStopped?.Invoke();
            }
            // found != null && _current != null: a game is still running (possibly a
            // different one). Leave the active profile alone.
        }

        // Returns the exe name of the first running process that is in the known set,
        // or null. Resilient: any enumeration failure yields null (treated as "no
        // game running") so the tray app never crashes on a bad snapshot.
        private string FindRunningKnownGame()
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
            {
                return null;
            }

            try
            {
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry))
                {
                    return null;
                }

                do
                {
                    string name = entry.szExeFile;
                    if (!string.IsNullOrEmpty(name) && _known.Contains(name))
                    {
                        return name;
                    }
                }
                while (Process32Next(snapshot, ref entry));
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(snapshot);
            }

            return null;
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
