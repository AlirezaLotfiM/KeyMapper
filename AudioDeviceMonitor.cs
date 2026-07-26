using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KeyMapper
{
    public class AudioDeviceMonitor : IDisposable
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int DBT_DEVICEREMOVALCOMPLETE = 0x8004;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_CONFIGCHANGED = 0x0018;

        private HwndSource? _hwndSource;
        private bool _isHooked;
        private DateTime _lastDisconnectTime = DateTime.MinValue;

        public event Action? OnAudioDeviceDisconnected;

        public void Initialize(Window targetWindow)
        {
            if (_isHooked) return;

            IntPtr handle = new WindowInteropHelper(targetWindow).Handle;
            if (handle != IntPtr.Zero)
            {
                AttachHook(handle);
            }
            else
            {
                targetWindow.SourceInitialized += (s, e) =>
                {
                    IntPtr h = new WindowInteropHelper(targetWindow).Handle;
                    if (h != IntPtr.Zero)
                    {
                        AttachHook(h);
                    }
                };
            }
        }

        private void AttachHook(IntPtr hwnd)
        {
            if (_isHooked) return;
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(HwndHook);
            _isHooked = true;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int eventType = wParam.ToInt32();
                // Device nodes changed or device removal (e.g. Bluetooth earbuds disconnected/removed)
                if (eventType == DBT_DEVNODES_CHANGED ||
                    eventType == DBT_DEVICEREMOVALCOMPLETE ||
                    eventType == DBT_CONFIGCHANGED)
                {
                    DateTime now = DateTime.Now;
                    // Debounce device notifications (within 800ms)
                    if ((now - _lastDisconnectTime).TotalMilliseconds > 800)
                    {
                        _lastDisconnectTime = now;
                        OnAudioDeviceDisconnected?.Invoke();
                    }
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hwndSource != null && _isHooked)
            {
                _hwndSource.RemoveHook(HwndHook);
                _isHooked = false;
            }
        }
    }
}
