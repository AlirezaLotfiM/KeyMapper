using System;
using System.Threading;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KeyMapper
{
    /// <summary>
    /// Tracks the Windows default render endpoint and exposes its master volume.
    /// Core Audio callbacks can arrive on non-UI threads, so consumers must
    /// marshal <see cref="VolumeChanged"/> to their dispatcher.
    /// </summary>
    public sealed class SystemVolumeService : IMMNotificationClient, IDisposable
    {
        private static readonly Lazy<SystemVolumeService> LazyInstance =
            new(() => new SystemVolumeService());

        private readonly object _syncRoot = new();
        private MMDeviceEnumerator? _deviceEnumerator;
        private MMDevice? _device;
        private AudioEndpointVolume? _endpointVolume;
        private string? _deviceId;
        private int _rebindQueued;
        private bool _disposed;

        public static SystemVolumeService Instance => LazyInstance.Value;

        public event Action<double, bool>? VolumeChanged;

        public bool IsAvailable
        {
            get
            {
                lock (_syncRoot)
                {
                    return !_disposed && _endpointVolume != null;
                }
            }
        }

        public double VolumePercent
        {
            get
            {
                lock (_syncRoot)
                {
                    try
                    {
                        return (_endpointVolume?.MasterVolumeLevelScalar ?? 0f) * 100d;
                    }
                    catch
                    {
                        return 0d;
                    }
                }
            }
        }

        public bool IsMuted
        {
            get
            {
                lock (_syncRoot)
                {
                    try
                    {
                        return _endpointVolume?.Mute ?? false;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        private SystemVolumeService()
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                _deviceEnumerator.RegisterEndpointNotificationCallback(this);
                RebindDefaultEndpoint();

                if (Application.Current != null)
                {
                    Application.Current.Exit += Application_Exit;
                }
            }
            catch
            {
                ReleaseEndpoint();
            }
        }

        public bool TrySetVolumePercent(double volumePercent)
        {
            AudioEndpointVolume? endpoint;
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return false;
                }

                endpoint = _endpointVolume;
            }

            if (endpoint == null)
            {
                QueueEndpointRebind();
                return false;
            }

            try
            {
                float scalar = (float)(Math.Clamp(volumePercent, 0d, 100d) / 100d);
                endpoint.MasterVolumeLevelScalar = scalar;

                // Moving away from zero should restore audible output, matching
                // the Windows volume flyout behavior.
                if (scalar > 0f && endpoint.Mute)
                {
                    endpoint.Mute = false;
                }

                return true;
            }
            catch
            {
                QueueEndpointRebind();
                return false;
            }
        }

        private void EndpointVolume_OnVolumeNotification(
            AudioVolumeNotificationData notification)
        {
            double volume = Math.Clamp(notification.MasterVolume * 100d, 0d, 100d);
            VolumeChanged?.Invoke(volume, notification.Muted);
        }

        private void RebindDefaultEndpoint()
        {
            MMDevice? oldDevice;
            AudioEndpointVolume? oldEndpoint;
            double volume = 0d;
            bool muted = false;
            bool available = false;

            lock (_syncRoot)
            {
                if (_disposed || _deviceEnumerator == null)
                {
                    return;
                }

                oldDevice = _device;
                oldEndpoint = _endpointVolume;
                _device = null;
                _endpointVolume = null;
                _deviceId = null;

                try
                {
                    _device = _deviceEnumerator.GetDefaultAudioEndpoint(
                        DataFlow.Render,
                        Role.Multimedia);
                    _endpointVolume = _device.AudioEndpointVolume;
                    _deviceId = _device.ID;
                    _endpointVolume.OnVolumeNotification +=
                        EndpointVolume_OnVolumeNotification;
                    volume = _endpointVolume.MasterVolumeLevelScalar * 100d;
                    muted = _endpointVolume.Mute;
                    available = true;
                }
                catch
                {
                    _device = null;
                    _endpointVolume = null;
                    _deviceId = null;
                }
            }

            if (oldEndpoint != null)
            {
                try
                {
                    oldEndpoint.OnVolumeNotification -=
                        EndpointVolume_OnVolumeNotification;
                    oldEndpoint.Dispose();
                }
                catch { }
            }

            try
            {
                oldDevice?.Dispose();
            }
            catch { }

            if (available)
            {
                VolumeChanged?.Invoke(Math.Clamp(volume, 0d, 100d), muted);
            }
        }

        private void QueueEndpointRebind()
        {
            if (Interlocked.Exchange(ref _rebindQueued, 1) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RebindDefaultEndpoint();
                }
                finally
                {
                    Interlocked.Exchange(ref _rebindQueued, 0);
                }
            });
        }

        public void OnDefaultDeviceChanged(
            DataFlow dataFlow,
            Role deviceRole,
            string defaultDeviceId)
        {
            if (dataFlow == DataFlow.Render &&
                (deviceRole == Role.Multimedia || deviceRole == Role.Console))
            {
                QueueEndpointRebind();
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (string.Equals(deviceId, _deviceId, StringComparison.OrdinalIgnoreCase) &&
                newState != DeviceState.Active)
            {
                QueueEndpointRebind();
            }
        }

        public void OnDeviceRemoved(string deviceId)
        {
            if (string.Equals(deviceId, _deviceId, StringComparison.OrdinalIgnoreCase))
            {
                QueueEndpointRebind();
            }
        }

        public void OnDeviceAdded(string deviceId)
        {
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
        }

        private void Application_Exit(object? sender, ExitEventArgs e)
        {
            Dispose();
        }

        private void ReleaseEndpoint()
        {
            AudioEndpointVolume? endpoint;
            MMDevice? device;

            lock (_syncRoot)
            {
                endpoint = _endpointVolume;
                device = _device;
                _endpointVolume = null;
                _device = null;
                _deviceId = null;
            }

            if (endpoint != null)
            {
                try
                {
                    endpoint.OnVolumeNotification -= EndpointVolume_OnVolumeNotification;
                    endpoint.Dispose();
                }
                catch { }
            }

            try
            {
                device?.Dispose();
            }
            catch { }
        }

        public void Dispose()
        {
            MMDeviceEnumerator? enumerator;
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                enumerator = _deviceEnumerator;
                _deviceEnumerator = null;
            }

            if (Application.Current != null)
            {
                Application.Current.Exit -= Application_Exit;
            }

            if (enumerator != null)
            {
                try
                {
                    enumerator.UnregisterEndpointNotificationCallback(this);
                }
                catch { }
            }

            ReleaseEndpoint();

            try
            {
                enumerator?.Dispose();
            }
            catch { }

            GC.SuppressFinalize(this);
        }
    }
}
