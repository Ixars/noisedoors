using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Noise
{
    public sealed class Keyboard : IDisposable
    {
        private static readonly IntPtr SyntheticSignature = new IntPtr(0x4E4F495345);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ------------------------- HOOK CONSTANTS -----------------------------------
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        // ------------------------ SENDINPUT CONSTANTS -----------------------------
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        // --------------------------- EVENTS ------------------------------------
        public event Action<Keys>? KeyPressed;
        public event Action<Keys>? KeyReleased;

        // ------------------------- HOOK STATE -------------------------------
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

        public bool IsHooked => _hookId != IntPtr.Zero;

        public Keyboard()
        {
            // Cache the delegate once, if it's collected the OS will crash on callback.
            _proc = HookCallback;
        }

        // ------------------------------ HOOK -------------------------------

        public void Hook()
        {
            if (_disposed) return;
            if (IsHooked) return;

            using Process process = Process.GetCurrentProcess();
            IntPtr module = GetModuleHandle(process.MainModule?.ModuleName);
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, module, 0);
        }

        public void Unhook()
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Ignore anything we injected ourselves
                if (info.dwExtraInfo == SyntheticSignature) return CallNextHookEx(_hookId, nCode, wParam, lParam);
                Keys key = (Keys)info.vkCode;

                switch (wParam.ToInt32())
                {
                    case WM_KEYDOWN:
                    case WM_SYSKEYDOWN:
                        Raise(KeyPressed, key);
                        break;

                    case WM_KEYUP:
                    case WM_SYSKEYUP:
                        Raise(KeyReleased, key);
                        break;
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static void Raise(Action<Keys>? handler, Keys key)
        {
            if (handler is null) return;
            try { handler(key); }
            catch { }
        }

        // ------------------------ SIMULATED INPUT -----------------------------

        public static void KeyDown(Keys key) => SendKey(key, up: false);
        public static void KeyUp(Keys key) => SendKey(key, up: true);

        public static void KeyPress(Keys key)
        {
            KeyDown(key);
            KeyUp(key);
        }

        public static void TypeChar(char c)
        {
            Send(new[]
            {
                UnicodeInput(c, up: false),
                UnicodeInput(c, up: true)
            });
        }

        private static void SendKey(Keys key, bool up)
        {
            uint flags = up ? KEYEVENTF_KEYUP : 0;
            if (IsExtendedKey(key)) flags |= KEYEVENTF_EXTENDEDKEY;

            Send(new[]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)key,
                            wScan = 0,
                            dwFlags = flags,
                            time = 0,
                            dwExtraInfo = SyntheticSignature
                        }
                    }
                }
            });

        }

        private static INPUT UnicodeInput(char c, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = SyntheticSignature
                }
            }
        };

        private static void Send(INPUT[] inputs)
        {
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        // Some keys need the extended-key flag or apps will misinterpret them
        private static bool IsExtendedKey(Keys key) => key switch
        {
            Keys.RMenu or Keys.LMenu or
            Keys.RControlKey or Keys.LControlKey or
            Keys.Insert or Keys.Delete or Keys.Home or Keys.End or
            Keys.Prior or Keys.Next or
            Keys.Left or Keys.Up or Keys.Right or Keys.Down or
            Keys.NumLock or Keys.Divide or
            Keys.RWin or Keys.LWin or
            Keys.Apps or Keys.PrintScreen => true,
            _ => false
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unhook();
            GC.SuppressFinalize(this);
        }

        ~Keyboard() => Unhook();

        // ----------------------- NATIVE INTEROP ------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}