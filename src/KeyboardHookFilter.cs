using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
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
        
        private readonly FilterConfig _config;
        private readonly long[] _lastUpTicks = new long[256];
        private readonly bool[] _isPressed = new bool[256];
        private readonly bool[] _excludedKeys = new bool[256];
        private readonly double[] _thresholdTicksByVk = new double[256];
        private readonly ManualResetEventSlim _startedSignal = new ManualResetEventSlim(false);

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
            var defaultThresholdTicks = Stopwatch.Frequency * _config.MinRepeatIntervalMs / 1000.0;
            for (var i = 0; i < _thresholdTicksByVk.Length; i++)
            {
                _thresholdTicksByVk[i] = defaultThresholdTicks;
            }

            foreach (var kvp in _config.PerKeyMinRepeatIntervalMs)
            {
                if (kvp.Key >= 0 && kvp.Key < 256 && kvp.Value >= 0)
                {
                    _thresholdTicksByVk[kvp.Key] = Stopwatch.Frequency * kvp.Value / 1000.0;
                }
            }

            Array.Clear(_excludedKeys, 0, _excludedKeys.Length);
            foreach (var vkCode in _config.ExcludedVkCodes)
            {
                if (vkCode >= 0 && vkCode < _excludedKeys.Length)
                {
                    _excludedKeys[vkCode] = true;
                }
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

            Console.WriteLine("Keyboard hook active with threshold {0}ms.", _config.MinRepeatIntervalMs);
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

                if (vk >= 0 && vk < 256 && !_excludedKeys[vk])
                {
                    var now = Stopwatch.GetTimestamp();

                    if (message == WmKeyDown || message == WmSysKeyDown)
                    {
                        if (!_isPressed[vk] && (now - _lastUpTicks[vk]) < _thresholdTicksByVk[vk])
                        {
                            if (_config.LogLevel == "Trace")
                            {
                                string keyName = VirtualKeys.ResourceManager.GetString(vk.ToString("X2"));
                                try
                                {
                                    File.AppendAllText(_config.LogFilePath, $@"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")} - {keyName}={vk} filtered{Environment.NewLine}");
                                }
                                catch
                                {
                                    // silent any errors when writing to the log file.
                                }
                            }
                            // Filter this key press as it's a bounce.
                            return new IntPtr(1);
                        }

                        _isPressed[vk] = true;
                    }
                    else if (message == WmKeyUp || message == WmSysKeyUp)
                    {
                        // Always update the last-up time on a key-up event. This is crucial
                        // for the filter to work correctly after a key-down was filtered.
                        _lastUpTicks[vk] = now;
                        _isPressed[vk] = false;
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
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
