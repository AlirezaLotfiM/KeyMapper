using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyMapper
{
    public class MouseHook : IDisposable
    {
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        private const int WH_MOUSE_LL = 14;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int VK_SHIFT = 0x10;

        private bool _swallowedDown = false;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public event Action<int, int>? OnShiftRightClick;

        public bool IsEnabled { get; set; } = true;

        public MouseHook()
        {
            _proc = HookCallback;
        }

        public void Hook()
        {
            if (_hookId == IntPtr.Zero)
            {
                _hookId = SetHook(_proc);
            }
        }

        public void Unhook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule == null || string.IsNullOrEmpty(curModule.ModuleName)) return IntPtr.Zero;
                return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsEnabled)
            {
                int message = wParam.ToInt32();
                if (message == WM_RBUTTONDOWN)
                {
                    // Check if Shift key is currently down
                    bool isShiftPressed = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                    if (isShiftPressed)
                    {
                        try
                        {
                            _swallowedDown = true;
                            MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                            OnShiftRightClick?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                            return (IntPtr)1; // Swallow event
                        }
                        catch { }
                    }
                }
                else if (message == WM_RBUTTONUP)
                {
                    if (_swallowedDown)
                    {
                        _swallowedDown = false;
                        return (IntPtr)1; // Swallow event
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetKeyState(int keyCode);

        public void Dispose()
        {
            Unhook();
        }
    }
}
