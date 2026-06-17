using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace KeyboardRepeatFilter
{
    public sealed class KeyboardHookFilter
    {
        private const int WhKeyboardLl = 13;
        private const int HcAction = 0;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint WmQuit = 0x0012;

        // Low-level keyboard hook flag: the key is an extended key (right Ctrl/Alt,
        // arrows, etc.). Preserved when we re-inject a deferred key-up.
        private const uint LlkhfExtended = 0x01;

        // SendInput plumbing for re-emitting a deferred key-up in BlockRelease mode.
        private const uint InputKeyboard = 1;
        private const uint KeyeventfExtendedkey = 0x0001;
        private const uint KeyeventfKeyup = 0x0002;

        // Marker stamped into dwExtraInfo on key-ups we inject ourselves, so the
        // hook can recognise and ignore its own synthetic events ("RFLT").
        private const long InjectedMarkerValue = 0x52464C54;
        private static readonly UIntPtr InjectedMarker = (UIntPtr)InjectedMarkerValue;

        private readonly FilterConfig _config;
        private readonly long[] _lastUpTicks = new long[256];
        private readonly bool[] _isPressed = new bool[256];
        // BlockRepress-mode state: when a bounce key-down is swallowed, its matching
        // key-up must be swallowed too, otherwise the OS is left with an unmatched
        // key-up. For modifiers (Shift especially) that unbalanced stream can arm
        // Windows accessibility shortcuts such as Sticky Keys.
        private readonly bool[] _swallowNextUp = new bool[256];
        private readonly bool[] _excludedKeys = new bool[256];
        private readonly double[] _thresholdTicksByVk = new double[256];
        private readonly ManualResetEventSlim _startedSignal = new ManualResetEventSlim(false);

        // BlockRelease-mode state. A deferred key-up is withheld until either a
        // bounce key-down cancels it or its timer fires and re-emits the up.
        private readonly object _sync = new object();
        private readonly bool[] _pendingUp = new bool[256];
        private readonly bool[] _pendingExtended = new bool[256];
        private readonly int[] _thresholdMsByVk = new int[256];
        private readonly Timer[] _releaseTimers = new Timer[256];
        private bool _blockRelease;

        private Thread _messageThread;
        private HookProcDelegate _hookProc;
        private IntPtr _hookId = IntPtr.Zero;
        private int _messageThreadId;
        private Exception _startException;

        public KeyboardHookFilter(FilterConfig config)
        {
            _config = config;
        }

        public void Start()
        {
            _blockRelease = string.Equals(_config.FilterMode, "BlockRelease", StringComparison.OrdinalIgnoreCase);

            var defaultThresholdTicks = Stopwatch.Frequency * _config.MinRepeatIntervalMs / 1000.0;
            var defaultThresholdMs = Math.Max(1, (int)Math.Ceiling(_config.MinRepeatIntervalMs));
            for (var i = 0; i < _thresholdTicksByVk.Length; i++)
            {
                _thresholdTicksByVk[i] = defaultThresholdTicks;
                _thresholdMsByVk[i] = defaultThresholdMs;
            }

            var unresolved = new List<string>();

            if (_config.PerKeyMinRepeatIntervalMs != null)
            {
                foreach (var kvp in _config.PerKeyMinRepeatIntervalMs)
                {
                    if (kvp.Value < 0)
                    {
                        continue;
                    }

                    var codes = KeyTokenResolver.Resolve(kvp.Key);
                    if (codes.Count == 0)
                    {
                        unresolved.Add(kvp.Key);
                        continue;
                    }

                    foreach (var vk in codes)
                    {
                        if (vk >= 0 && vk < _thresholdTicksByVk.Length)
                        {
                            _thresholdTicksByVk[vk] = Stopwatch.Frequency * kvp.Value / 1000.0;
                            _thresholdMsByVk[vk] = Math.Max(1, (int)Math.Ceiling(kvp.Value));
                        }
                    }
                }
            }

            Array.Clear(_excludedKeys, 0, _excludedKeys.Length);

            // Legacy numeric exclusions.
            if (_config.ExcludedVkCodes != null)
            {
                foreach (var vkCode in _config.ExcludedVkCodes)
                {
                    if (vkCode >= 0 && vkCode < _excludedKeys.Length)
                    {
                        _excludedKeys[vkCode] = true;
                    }
                }
            }

            // Name- or number-based exclusions.
            if (_config.ExcludedKeys != null)
            {
                foreach (var token in _config.ExcludedKeys)
                {
                    var codes = KeyTokenResolver.Resolve(token);
                    if (codes.Count == 0)
                    {
                        unresolved.Add(token);
                        continue;
                    }

                    foreach (var vk in codes)
                    {
                        if (vk >= 0 && vk < _excludedKeys.Length)
                        {
                            _excludedKeys[vk] = true;
                        }
                    }
                }
            }

            if (unresolved.Count > 0)
            {
                LogConfigWarning("Unrecognized key name(s) in config (ignored): " + string.Join(", ", unresolved));
            }

            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "KeyboardRepeatFilter.MessageLoop"
            };
            _messageThread.Start();

            if (!_startedSignal.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out waiting for keyboard hook to start.");
            }

            if (_startException != null)
            {
                throw new InvalidOperationException("Unable to start keyboard hook.", _startException);
            }

            Console.WriteLine("Keyboard hook active with threshold {0}ms (mode: {1}).",
                _config.MinRepeatIntervalMs, _blockRelease ? "BlockRelease" : "BlockRepress");
        }

        public void Stop()
        {
            if (_messageThreadId != 0)
            {
                PostThreadMessage(_messageThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            if (_messageThread != null)
            {
                _messageThread.Join(TimeSpan.FromSeconds(5));
                _messageThread = null;
            }

            lock (_sync)
            {
                for (var i = 0; i < _releaseTimers.Length; i++)
                {
                    _releaseTimers[i]?.Dispose();
                    _releaseTimers[i] = null;
                    _pendingUp[i] = false;
                }
            }
        }

        private void MessageThreadMain()
        {
            _messageThreadId = GetCurrentThreadId();
            _hookProc = HookProc;
            _hookId = SetWindowsHookEx(WhKeyboardLl, _hookProc, IntPtr.Zero, 0);
            if (_hookId == IntPtr.Zero)
            {
                _startException = new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
                _startedSignal.Set();
                return;
            }

            _startedSignal.Set();

            try
            {
                Msg msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            finally
            {
                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                }
            }
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HcAction)
            {
                var kb = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
                var vk = unchecked((int)kb.vkCode);
                var message = unchecked((int)wParam.ToInt64());

                // Pass through key-ups we re-injected ourselves; never re-filter them.
                if (kb.dwExtraInfo == InjectedMarker)
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (vk >= 0 && vk < 256 && !_excludedKeys[vk])
                {
                    bool filter = _blockRelease
                        ? HandleBlockRelease(vk, message, kb.flags)
                        : HandleBlockRepress(vk, message);

                    if (filter)
                    {
                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // Original behaviour: let the spurious key-up through and block the
        // duplicate key-down that follows it within the threshold.
        // Returns true when the event should be swallowed.
        private bool HandleBlockRepress(int vk, int message)
        {
            var now = Stopwatch.GetTimestamp();

            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                if (!_isPressed[vk] && (now - _lastUpTicks[vk]) < _thresholdTicksByVk[vk])
                {
                    LogFiltered(vk, "filtered");
                    // Bounce key-down: swallow it, and remember to swallow the
                    // matching key-up too so the OS does not receive an unbalanced
                    // (down, up, up) stream that can arm Sticky Keys for modifiers.
                    _swallowNextUp[vk] = true;
                    return true;
                }

                _isPressed[vk] = true;
                // A genuine press cancels any pending up-swallow so its own release
                // is never mistaken for a bounce key-up.
                _swallowNextUp[vk] = false;
            }
            else if (message == WmKeyUp || message == WmSysKeyUp)
            {
                if (_swallowNextUp[vk])
                {
                    // This is the key-up that pairs with a bounce key-down we already
                    // dropped; swallow it to keep the event stream balanced.
                    _swallowNextUp[vk] = false;
                    return true;
                }

                // Always update the last-up time on a key-up event. This is crucial
                // for the filter to work correctly after a key-down was filtered.
                _lastUpTicks[vk] = now;
                _isPressed[vk] = false;
            }

            return false;
        }

        // Alternative behaviour: withhold the spurious key-up. If a bounce
        // key-down follows within the threshold, drop both so the key stays
        // logically held; otherwise a timer re-emits the genuine key-up.
        // Returns true when the event should be swallowed.
        private bool HandleBlockRelease(int vk, int message, uint flags)
        {
            lock (_sync)
            {
                if (message == WmKeyDown || message == WmSysKeyDown)
                {
                    if (_pendingUp[vk])
                    {
                        // A key-up was deferred and a key-down arrived within the
                        // window: classic bounce. Cancel the pending release and
                        // swallow this duplicate down so the key stays held.
                        CancelPendingUp(vk);
                        LogFiltered(vk, "release-held");
                        return true;
                    }

                    _isPressed[vk] = true;
                    return false;
                }

                if (message == WmKeyUp || message == WmSysKeyUp)
                {
                    if (_pendingUp[vk])
                    {
                        // Already deferring an up for this key; keep withholding.
                        return true;
                    }

                    if (!_isPressed[vk])
                    {
                        // We never observed the matching key-down (e.g. the key was
                        // held across a filter restart or an elevated-window bypass).
                        // Let the up pass untouched rather than deferring and later
                        // injecting an unmatched key-up.
                        return false;
                    }

                    // Defer the key-up and wait to see whether a bounce down
                    // follows within the threshold.
                    _pendingUp[vk] = true;
                    _pendingExtended[vk] = (flags & LlkhfExtended) != 0;
                    EnsureTimer(vk).Change(_thresholdMsByVk[vk], Timeout.Infinite);
                    return true;
                }
            }

            return false;
        }

        private Timer EnsureTimer(int vk)
        {
            if (_releaseTimers[vk] == null)
            {
                _releaseTimers[vk] = new Timer(OnReleaseTimer, vk, Timeout.Infinite, Timeout.Infinite);
            }

            return _releaseTimers[vk];
        }

        private void CancelPendingUp(int vk)
        {
            _pendingUp[vk] = false;
            _releaseTimers[vk]?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnReleaseTimer(object state)
        {
            var vk = (int)state;
            bool extended;

            lock (_sync)
            {
                if (!_pendingUp[vk])
                {
                    // A bounce down already cancelled this release.
                    return;
                }

                _pendingUp[vk] = false;
                _isPressed[vk] = false;
                extended = _pendingExtended[vk];
            }

            // No bounce arrived within the window: emit the genuine key-up now.
            InjectKeyUp(vk, extended);
        }

        private void InjectKeyUp(int vk, bool extended)
        {
            var input = new Input
            {
                type = InputKeyboard,
                u = new InputUnion
                {
                    ki = new KeybdInput
                    {
                        wVk = (ushort)vk,
                        wScan = 0,
                        dwFlags = KeyeventfKeyup | (extended ? KeyeventfExtendedkey : 0u),
                        time = 0,
                        dwExtraInfo = (IntPtr)InjectedMarkerValue
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
        }

        private void LogConfigWarning(string message)
        {
            Console.WriteLine(message);
            try
            {
                File.AppendAllText(_config.LogFilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - ConfigWarning: {message}{Environment.NewLine}");
            }
            catch
            {
                // Best-effort; never block startup on logging.
            }
        }

        private void LogFiltered(int vk, string action)
        {
            if (_config.LogLevel != "Trace")
            {
                return;
            }

            string keyName = VirtualKeys.ResourceManager.GetString(vk.ToString("X2"));
            try
            {
                File.AppendAllText(_config.LogFilePath, $@"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")} - {keyName}={vk} {action}{Environment.NewLine}");
            }
            catch
            {
                // silent any errors when writing to the log file.
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public Point pt;
            public uint lPrivate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint type;
            public InputUnion u;
        }

        // Union sized to the largest member (MouseInput) so the struct marshals
        // correctly on both x86 and x64, even though we only populate the keyboard
        // member.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mi;
            [FieldOffset(0)] public KeybdInput ki;
            [FieldOffset(0)] public HardwareInput hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeybdInput
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private delegate IntPtr HookProcDelegate(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProcDelegate lpfn, IntPtr hmod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern sbyte GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Msg lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
    }
}
