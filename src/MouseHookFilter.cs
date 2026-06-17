using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace KeyboardRepeatFilter
{
    // Low-level mouse-button debouncer. A chattering switch turns one physical
    // click into a rapid down-up-down-up burst; this drops the bounce button-down
    // that arrives within the threshold of that button's previous button-up,
    // mirroring the keyboard filter's "BlockRepress" behaviour. The trailing
    // bounce button-up is harmless (a stray up with no matching down) so it is
    // left to flow through, exactly as the keyboard filter does.
    public sealed class MouseHookFilter
    {
        private const int WhMouseLl = 14;
        private const int HcAction = 0;
        private const uint WmQuit = 0x0012;

        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private const int WmXButtonDown = 0x020B;
        private const int WmXButtonUp = 0x020C;

        // High word of mouseData for the two extra buttons on WM_XBUTTON* events.
        private const int Xbutton1 = 0x0001;
        private const int Xbutton2 = 0x0002;

        // Button slots: Left, Right, Middle, X1, X2.
        private const int ButtonCount = 5;
        private static readonly string[] ButtonNames = { "Left", "Right", "Middle", "X1", "X2" };

        private readonly FilterConfig _config;
        private readonly long[] _lastUpTicks = new long[ButtonCount];
        private readonly bool[] _isPressed = new bool[ButtonCount];
        private readonly bool[] _excluded = new bool[ButtonCount];
        private readonly ManualResetEventSlim _startedSignal = new ManualResetEventSlim(false);

        private long _thresholdTicks;

        private Thread _messageThread;
        private HookProcDelegate _hookProc;
        private IntPtr _hookId = IntPtr.Zero;
        private int _messageThreadId;
        private Exception _startException;

        public MouseHookFilter(FilterConfig config)
        {
            _config = config;
        }

        public void Start()
        {
            _thresholdTicks = (long)(Stopwatch.Frequency * _config.MouseMinRepeatIntervalMs / 1000.0);

            Array.Clear(_excluded, 0, _excluded.Length);
            var unresolved = new List<string>();
            if (_config.ExcludedMouseButtons != null)
            {
                foreach (var token in _config.ExcludedMouseButtons)
                {
                    var index = ResolveButton(token);
                    if (index < 0)
                    {
                        unresolved.Add(token);
                        continue;
                    }

                    _excluded[index] = true;
                }
            }

            if (unresolved.Count > 0)
            {
                LogConfigWarning("Unrecognized mouse button name(s) in config (ignored): " + string.Join(", ", unresolved));
            }

            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "KeyboardRepeatFilter.MouseMessageLoop"
            };
            _messageThread.Start();

            if (!_startedSignal.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out waiting for mouse hook to start.");
            }

            if (_startException != null)
            {
                throw new InvalidOperationException("Unable to start mouse hook.", _startException);
            }

            Console.WriteLine("Mouse hook active with threshold {0}ms.", _config.MouseMinRepeatIntervalMs);
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
        }

        private void MessageThreadMain()
        {
            _messageThreadId = GetCurrentThreadId();
            _hookProc = HookProc;
            _hookId = SetWindowsHookEx(WhMouseLl, _hookProc, IntPtr.Zero, 0);
            if (_hookId == IntPtr.Zero)
            {
                _startException = new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx (mouse) failed.");
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
                var message = unchecked((int)wParam.ToInt64());
                if (TryClassify(message, lParam, out int button, out bool isDown)
                    && !_excluded[button])
                {
                    if (Handle(button, isDown))
                    {
                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // Maps a mouse message to a button slot and whether it is a press. Returns
        // false for messages we do not debounce (moves, wheel, etc.).
        private bool TryClassify(int message, IntPtr lParam, out int button, out bool isDown)
        {
            switch (message)
            {
                case WmLButtonDown: button = 0; isDown = true; return true;
                case WmLButtonUp: button = 0; isDown = false; return true;
                case WmRButtonDown: button = 1; isDown = true; return true;
                case WmRButtonUp: button = 1; isDown = false; return true;
                case WmMButtonDown: button = 2; isDown = true; return true;
                case WmMButtonUp: button = 2; isDown = false; return true;
                case WmXButtonDown:
                case WmXButtonUp:
                    var hook = (MsLlHookStruct)Marshal.PtrToStructure(lParam, typeof(MsLlHookStruct));
                    int which = (int)((hook.mouseData >> 16) & 0xFFFF);
                    if (which == Xbutton1) { button = 3; }
                    else if (which == Xbutton2) { button = 4; }
                    else { button = -1; isDown = false; return false; }
                    isDown = message == WmXButtonDown;
                    return true;
                default:
                    button = -1;
                    isDown = false;
                    return false;
            }
        }

        // Returns true when the event should be swallowed.
        private bool Handle(int button, bool isDown)
        {
            var now = Stopwatch.GetTimestamp();

            if (isDown)
            {
                if (!_isPressed[button] && (now - _lastUpTicks[button]) < _thresholdTicks)
                {
                    LogFiltered(button, "filtered");
                    return true;
                }

                _isPressed[button] = true;
            }
            else
            {
                _lastUpTicks[button] = now;
                _isPressed[button] = false;
            }

            return false;
        }

        // Accepts "Left"/"L"/"LBUTTON", "Right"/"R"/"RBUTTON", "Middle"/"M"/
        // "MBUTTON", "X1"/"XBUTTON1", "X2"/"XBUTTON2"; case-insensitive. Returns
        // the button slot index, or -1 if the token is not recognized.
        private static int ResolveButton(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return -1;
            }

            switch (token.Trim().ToUpperInvariant())
            {
                case "LEFT":
                case "L":
                case "LBUTTON":
                    return 0;
                case "RIGHT":
                case "R":
                case "RBUTTON":
                    return 1;
                case "MIDDLE":
                case "M":
                case "MBUTTON":
                    return 2;
                case "X1":
                case "XBUTTON1":
                    return 3;
                case "X2":
                case "XBUTTON2":
                    return 4;
                default:
                    return -1;
            }
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

        private void LogFiltered(int button, string action)
        {
            if (_config.LogLevel != "Trace")
            {
                return;
            }

            try
            {
                File.AppendAllText(_config.LogFilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - Mouse_{ButtonNames[button]} {action}{Environment.NewLine}");
            }
            catch
            {
                // silent any errors when writing to the log file.
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MsLlHookStruct
        {
            public Point pt;
            public uint mouseData;
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
    }
}
