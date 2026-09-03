using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoicemeeterSliderControl
{
    public class ArduinoConnection : IDisposable
    {
        SerialPort _serialPort;
        DateTime _lastDataReceived = DateTime.MinValue;
        string _portName;
        int _baudRate;
        DeviceNotificationWindow _notifyWindow;
        System.Windows.Forms.Timer _healthTimer;

        public event Action<List<float>> ValuesReceived;
        public event Action<bool> ConnectionStateChanged;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public void Start(string portName, int baudRate)
        {
            _portName = portName;
            _baudRate = baudRate;

            _notifyWindow = new DeviceNotificationWindow();
            _notifyWindow.DeviceArrived += () => TryOpen();
            _notifyWindow.DeviceRemoved += () => ClosePort();
            _notifyWindow.RegisterForComPortNotifications();

            // Отдельный редкий таймер — не для опроса портов, а только как
            // подстраховка на случай "тихого" зависания данных (раз в 5 сек, не 1.5)
            _healthTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _healthTimer.Tick += (s, e) => HealthCheck();
            _healthTimer.Start();

            TryOpen();
        }

        public void UpdatePort(string portName, int baudRate)
        {
            bool wasConnected = IsConnected;
            ClosePort();
            _portName = portName;
            _baudRate = baudRate;
            if (wasConnected || Array.Exists(SerialPort.GetPortNames(), p => p == portName))
                TryOpen();
        }

        void HealthCheck()
        {
            if (IsConnected && _lastDataReceived != DateTime.MinValue &&
                (DateTime.Now - _lastDataReceived).TotalSeconds > 5)
            {
                ClosePort();
                TryOpen();
            }
        }

        bool TryOpen()
        {
            if (IsConnected) return true;
            if (string.IsNullOrEmpty(_portName)) return false;
            if (!Array.Exists(SerialPort.GetPortNames(), p => p == _portName)) return false;

            try
            {
                _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One);
                _serialPort.NewLine = "\n";
                _serialPort.ReadTimeout = 500;
                _serialPort.Open();
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;
                _serialPort.DiscardInBuffer();
                _serialPort.DataReceived += OnDataReceived;
                _lastDataReceived = DateTime.MinValue;
                ConnectionStateChanged?.Invoke(true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        void ClosePort()
        {
            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= OnDataReceived;
                    if (_serialPort.IsOpen) _serialPort.Close();
                }
            }
            catch { }
            finally
            {
                _serialPort = null;
                ConnectionStateChanged?.Invoke(false);
            }
        }

        void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            _lastDataReceived = DateTime.Now;
            try
            {
                string latestInput = null;
                while (_serialPort.BytesToRead > 0)
                {
                    string line;
                    try { line = _serialPort.ReadLine(); }
                    catch (TimeoutException) { break; }
                    if (!string.IsNullOrWhiteSpace(line)) latestInput = line;
                }
                if (latestInput == null) return;
                ValuesReceived?.Invoke(ParseValuesFloat(latestInput));
            }
            catch { }
        }

        public static List<float> ParseValuesFloat(string input)
        {
            List<float> floats = new List<float>();
            foreach (var value in input.Split('|'))
            {
                string clean = value.Replace("\r", "").Replace("\n", "").Trim();
                if (!string.IsNullOrEmpty(clean) && int.TryParse(clean, out int parsed))
                    floats.Add(Map(parsed, 0, 1024, 0f, 100f));
            }
            return floats;
        }

        public static float Map(float value, float from1, float to1, float from2, float to2)
            => (value - from1) / (to1 - from1) * (to2 - from2) + from2;

        public void Stop()
        {
            _healthTimer?.Stop();
            _healthTimer?.Dispose();
            _notifyWindow?.Dispose();
            ClosePort();
        }

        public void Dispose() => Stop();
    }

    // Скрытое окно-невидимка, единственная задача — ловить WM_DEVICECHANGE
    class DeviceNotificationWindow : NativeWindow, IDisposable
    {
        const int WM_DEVICECHANGE = 0x0219;
        const int DBT_DEVICEARRIVAL = 0x8000;
        const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        const int DBT_DEVTYP_DEVICEINTERFACE = 5;

        static readonly Guid GUID_DEVINTERFACE_COMPORT = new Guid("86E0D1E0-8089-11D0-9CE4-08003E301F73");

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
                dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE)),
                dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_classguid = GUID_DEVINTERFACE_COMPORT
            };

            IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
            Marshal.StructureToPtr(dbi, buffer, false);

            _notificationHandle = RegisterDeviceNotification(Handle, buffer, 0);

            Marshal.FreeHGlobal(buffer);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int eventType = m.WParam.ToInt32();
                if (eventType == DBT_DEVICEARRIVAL)
                    DeviceArrived?.Invoke();
                else if (eventType == DBT_DEVICEREMOVECOMPLETE)
                    DeviceRemoved?.Invoke();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_notificationHandle != IntPtr.Zero)
                UnregisterDeviceNotification(_notificationHandle);
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
        static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, int Flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterDeviceNotification(IntPtr Handle);
    }
}