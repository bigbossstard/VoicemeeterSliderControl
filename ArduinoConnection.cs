using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace VoicemeeterSliderControl
{
    public class ArduinoConnection : IDisposable
    {
        public const int DefaultBaudRate = 115200;
        public const int DefaultSliderCount = 8;

        readonly object _sync = new object();
        SerialPort _serialPort;
        StringBuilder _receiveBuffer = new StringBuilder();
        DateTime _lastDataReceivedUtc = DateTime.MinValue;
        DateTime _openedAtUtc = DateTime.MinValue;
        string _portName = "";
        int _baudRate = DefaultBaudRate;
        bool _stopped;
        bool _reportedConnected;

        DeviceNotificationWindow _notifyWindow;
        System.Windows.Forms.Timer _healthTimer;

        public event Action<List<float>> ValuesReceived;
        public event Action<bool> ConnectionStateChanged;

        public bool IsConnected
        {
            get
            {
                lock (_sync)
                    return IsConnectedLocked();
            }
        }

        public void Start(string portName, int baudRate)
        {
            lock (_sync)
            {
                if (_healthTimer != null) return;

                _stopped = false;
                _portName = portName ?? "";
                _baudRate = baudRate > 0 ? baudRate : DefaultBaudRate;
            }

            _notifyWindow = new DeviceNotificationWindow();
            _notifyWindow.DeviceArrived += TryOpen;
            _notifyWindow.DeviceRemoved += ClosePort;
            _notifyWindow.RegisterForComPortNotifications();

            _healthTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _healthTimer.Tick += (s, e) => HealthCheck();
            _healthTimer.Start();

            TryOpen();
        }

        public void UpdatePort(string portName, int baudRate)
        {
            ClosePort();

            lock (_sync)
            {
                _portName = portName ?? "";
                _baudRate = baudRate > 0 ? baudRate : DefaultBaudRate;
            }

            TryOpen();
        }

        void HealthCheck()
        {
            if (_stopped) return;

            if (!IsConnected)
            {
                TryOpen();
                return;
            }

            DateTime last;
            DateTime opened;

            lock (_sync)
            {
                last = _lastDataReceivedUtc;
                opened = _openedAtUtc;
            }

            DateTime now = DateTime.UtcNow;

            bool stale =
                (last != DateTime.MinValue &&
                 (now - last).TotalSeconds > 5) ||
                (last == DateTime.MinValue &&
                 opened != DateTime.MinValue &&
                 (now - opened).TotalSeconds > 5);

            if (!stale) return;

            ClosePort();
            TryOpen();
        }

        bool TryOpen()
        {
            if (_stopped) return false;

            string portName;
            int baudRate;

            lock (_sync)
            {
                if (IsConnectedLocked()) return true;
                portName = _portName;
                baudRate = _baudRate;
            }

            if (string.IsNullOrWhiteSpace(portName))
                return false;

            string[] ports;

            try
            {
                ports = SerialPort.GetPortNames();
            }
            catch
            {
                return false;
            }

            if (Array.IndexOf(ports, portName) < 0)
                return false;

            SerialPort port = null;

            try
            {
                port = new SerialPort(
                    portName,
                    baudRate,
                    Parity.None,
                    8,
                    StopBits.One)
                {
                    NewLine = "\n",
                    ReadTimeout = 500,
                    ReadBufferSize = 8192,
                    DtrEnable = true,
                    RtsEnable = true
                };

                port.DataReceived += OnDataReceived;
                port.Open();
                port.DiscardInBuffer();

                lock (_sync)
                {
                    if (_stopped)
                    {
                        port.DataReceived -= OnDataReceived;
                        port.Dispose();
                        return false;
                    }

                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        port.DataReceived -= OnDataReceived;
                        port.Dispose();
                        return true;
                    }

                    _serialPort = port;
                    _receiveBuffer.Clear();
                    _lastDataReceivedUtc = DateTime.MinValue;
                    _openedAtUtc = DateTime.UtcNow;
                }

                RaiseConnectionState(true);
                return true;
            }
            catch
            {
                try
                {
                    if (port != null)
                    {
                        port.DataReceived -= OnDataReceived;
                        port.Dispose();
                    }
                }
                catch
                {
                }

                return false;
            }
        }

        bool IsConnectedLocked()
            => _serialPort != null && _serialPort.IsOpen;

        void ClosePort()
        {
            SerialPort port;

            lock (_sync)
            {
                port = _serialPort;
                _serialPort = null;
                _receiveBuffer.Clear();
                _lastDataReceivedUtc = DateTime.MinValue;
                _openedAtUtc = DateTime.MinValue;
            }

            bool wasConnected = port != null;

            try
            {
                if (port != null)
                {
                    port.DataReceived -= OnDataReceived;
                    if (port.IsOpen) port.Close();
                    port.Dispose();
                }
            }
            catch
            {
            }

            if (wasConnected)
                RaiseConnectionState(false);
        }

        void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var port = sender as SerialPort;
            if (port == null) return;

            string chunk;

            try
            {
                chunk = port.ReadExisting();
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(chunk)) return;

            string latestLine = null;

            lock (_sync)
            {
                if (_stopped ||
                    !ReferenceEquals(port, _serialPort) ||
                    !port.IsOpen)
                    return;

                _lastDataReceivedUtc = DateTime.UtcNow;
                _receiveBuffer.Append(chunk);

                while (true)
                {
                    string buffer = _receiveBuffer.ToString();
                    int newlineIndex = buffer.IndexOf('\n');

                    if (newlineIndex < 0) break;

                    string line = buffer.Substring(0, newlineIndex);
                    _receiveBuffer.Remove(0, newlineIndex + 1);

                    if (!string.IsNullOrWhiteSpace(line))
                        latestLine = line;
                }
            }

            if (latestLine == null) return;

            List<float> values;

            try
            {
                values = ParseValuesFloat(latestLine);
            }
            catch
            {
                return;
            }

            if (values.Count == 0) return;
            ValuesReceived?.Invoke(values);
        }

        public static List<float> ParseValuesFloat(string input)
        {
            var floats = new List<float>();
            if (string.IsNullOrWhiteSpace(input)) return floats;

            foreach (var value in input.Split('|'))
            {
                string clean = value.Trim();

                if (int.TryParse(
                    clean,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int parsed))
                {
                    parsed = Math.Clamp(parsed, 0, 1023);
                    floats.Add(Map(parsed, 0, 1023, 0f, 100f));
                }
            }

            return floats;
        }

        public static float Map(
            float value,
            float from1,
            float to1,
            float from2,
            float to2)
        {
            if (Math.Abs(to1 - from1) < float.Epsilon)
                throw new ArgumentException(
                    "Input range cannot be zero.",
                    nameof(to1));

            return (value - from1) /
                   (to1 - from1) *
                   (to2 - from2) +
                   from2;
        }

        void RaiseConnectionState(bool connected)
        {
            lock (_sync)
            {
                if (_reportedConnected == connected) return;
                _reportedConnected = connected;
            }

            ConnectionStateChanged?.Invoke(connected);
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_stopped) return;
                _stopped = true;
            }

            try
            {
                if (_healthTimer != null)
                {
                    _healthTimer.Stop();
                    _healthTimer.Dispose();
                    _healthTimer = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_notifyWindow != null)
                {
                    _notifyWindow.DeviceArrived -= TryOpen;
                    _notifyWindow.DeviceRemoved -= ClosePort;
                    _notifyWindow.Dispose();
                    _notifyWindow = null;
                }
            }
            catch
            {
            }

            ClosePort();
        }

        public void Dispose() => Stop();
    }

    class DeviceNotificationWindow : NativeWindow, IDisposable
    {
        const int WM_DEVICECHANGE = 0x0219;
        const int DBT_DEVICEARRIVAL = 0x8000;
        const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        const int DBT_DEVTYP_DEVICEINTERFACE = 5;

        static readonly Guid GUID_DEVINTERFACE_COMPORT =
            new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");

        IntPtr _notificationHandle;

        public event Action DeviceArrived;
        public event Action DeviceRemoved;

        public DeviceNotificationWindow()
        {
            CreateHandle(new CreateParams());
        }

        public void RegisterForComPortNotifications()
        {
            var dbi = new DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size =
                    Marshal.SizeOf(
                        typeof(DEV_BROADCAST_DEVICEINTERFACE)),
                dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_classguid = GUID_DEVINTERFACE_COMPORT
            };

            IntPtr buffer =
                Marshal.AllocHGlobal(dbi.dbcc_size);

            try
            {
                Marshal.StructureToPtr(dbi, buffer, false);
                _notificationHandle =
                    RegisterDeviceNotification(
                        Handle,
                        buffer,
                        0);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                switch (m.WParam.ToInt32())
                {
                    case DBT_DEVICEARRIVAL:
                        DeviceArrived?.Invoke();
                        break;

                    case DBT_DEVICEREMOVECOMPLETE:
                        DeviceRemoved?.Invoke();
                        break;
                }
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_notificationHandle != IntPtr.Zero)
            {
                UnregisterDeviceNotification(_notificationHandle);
                _notificationHandle = IntPtr.Zero;
            }

            DestroyHandle();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterDeviceNotification(
            IntPtr hRecipient,
            IntPtr NotificationFilter,
            int Flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterDeviceNotification(
            IntPtr Handle);
    }
}
