using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoicemeeterSliderControl
{
    public class SliderMapping
    {
        public int Slider { get; set; }
        public string VoicemeeterValue { get; set; }
        public string DisplayName { get; set; }
    }

    public class AppConfig
    {
        public string PortName { get; set; } = "";
        public int BaudRate { get; set; } = 115200;
        public List<SliderMapping> SliderSets { get; set; } = new List<SliderMapping>();
    }

    public static class VmInterop
    {
        [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetVoicemeeterType")]
        static extern long GetType64(ref long pType);

        [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetParameterStringW", CharSet = CharSet.Unicode)]
        static extern long GetParamStringW64(
            [MarshalAs(UnmanagedType.LPStr)] string szParamName,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder szString);

        public static string GetVoicemeeterKindName()
        {
            long type = 0;
            try
            {
                long res = GetType64(ref type);
                if (res != 0) return "неизвестно (код ошибки)";
            }
            catch (DllNotFoundException) { return "неизвестно (DLL не найдена)"; }
            return type switch { 1 => "Basic", 2 => "Banana", 3 => "Potato", 6 => "Potato x64", _ => $"неизвестно (код {type})" };
        }

        public static string TryGetLabel(string paramName)
        {
            var sb = new StringBuilder(512);
            try
            {
                long res = GetParamStringW64(paramName, sb);
                if (res != 0) return null;
            }
            catch { return null; }
            return sb.ToString();
        }

        public class ChannelInfo
        {
            public string Kind;
            public int Index;
            public string Label;
            public string ParamPrefix => $"{Kind}[{Index}]";
        }

        public static List<ChannelInfo> DiscoverChannels()
        {
            var result = new List<ChannelInfo>();
            for (int i = 0; i < 16; i++)
            {
                string label = TryGetLabel($"Strip[{i}].Label");
                if (label == null) break;
                result.Add(new ChannelInfo { Kind = "Strip", Index = i, Label = string.IsNullOrWhiteSpace(label) ? $"Strip {i}" : label });
            }
            for (int i = 0; i < 8; i++)
            {
                string label = TryGetLabel($"Bus[{i}].Label");
                if (label == null) break;
                result.Add(new ChannelInfo { Kind = "Bus", Index = i, Label = string.IsNullOrWhiteSpace(label) ? $"Bus {i}" : label });
            }
            return result;
        }
    }

    public static class AutoStart
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "VoicemeeterSliderControl";

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) != null;
        }

        public static void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;
            if (enabled)
                key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
            else
                key.DeleteValue(AppName, false);
        }
    }

    static class Program
    {
        static SerialPort _serialPort;
        static IDisposable _vmSession;
        static AppConfig _config;
        static bool _enabled = true;
        static NotifyIcon _trayIcon;
        static readonly object _configLock = new object();

        public static volatile int LastKnownSliderCount = 0;

        static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoicemeeterSliderControl", "config.json");

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show($"Необработанная ошибка: {e.ExceptionObject}", "Критическая ошибка");
            Application.ThreadException += (s, e) =>
                MessageBox.Show($"Ошибка UI-потока: {e.Exception}", "Ошибка");

            try
            {
                _vmSession = VoiceMeeter.Remote.Initialize(Voicemeeter.RunVoicemeeterParam.VoicemeeterPotato)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось подключиться к Voicemeeter: {ex.Message}", "Ошибка");
                return;
            }

            LoadConfig();

            if (_config.SliderSets.Count == 0 || string.IsNullOrEmpty(_config.PortName))
            {
                using var form = new SettingsForm(_config, cfg => {
                    _config = cfg;
                    SaveConfig();
                });
                form.ShowDialog();

                if (_config.SliderSets.Count == 0 || string.IsNullOrEmpty(_config.PortName))
                {
                    Cleanup();
                    return;
                }
            }

            if (!TryOpenPort())
            {
                Cleanup();
                return;
            }

            var menu = new ContextMenuStrip();
            var toggleItem = new ToolStripMenuItem("Включено ✓");
            toggleItem.Click += (s, e) => {
                _enabled = !_enabled;
                toggleItem.Text = _enabled ? "Включено ✓" : "Выключено";
            };
            var settingsItem = new ToolStripMenuItem("Настройки...");
            settingsItem.Click += (s, e) => OpenSettings();
            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += (s, e) => { Cleanup(); Application.Exit(); };

            menu.Items.Add(toggleItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = new System.Drawing.Icon("icon.ico"),
                Text = "Voicemeeter Slider Control",
                ContextMenuStrip = menu,
                Visible = true
            };

            Application.ApplicationExit += (s, e) => Cleanup();
            Application.Run();
        }

        static void OpenSettings()
        {
            AppConfig snapshot;
            lock (_configLock) { snapshot = _config; }

            using var form = new SettingsForm(snapshot, cfg => {
                lock (_configLock) { _config = cfg; }
                SaveConfig();

                if (_serialPort != null && _serialPort.PortName != cfg.PortName)
                {
                    try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
                    TryOpenPort();
                }
            });
            form.ShowDialog();
        }

        static bool TryOpenPort()
        {
            try
            {
                _serialPort = new SerialPort(_config.PortName, _config.BaudRate, Parity.None, 8, StopBits.One);
                _serialPort.NewLine = "\n";
                _serialPort.ReadTimeout = 500;
                _serialPort.Open();
                _serialPort.DtrEnable = true;
                _serialPort.RtsEnable = true;
                _serialPort.DiscardInBuffer();
                _serialPort.DataReceived += SerialPort_DataReceived;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть порт {_config.PortName}: {ex.Message}", "Ошибка");
                return false;
            }
        }

        static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    _config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                    return;
                }
            }
            catch { }
            _config = new AppConfig();
        }

        static void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        static void Cleanup()
        {
            try { if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close(); } catch { }
            try { _vmSession?.Dispose(); } catch { }
            try { _trayIcon.Visible = false; } catch { }
        }

        static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!_enabled) return;
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

                List<float> valuesFloat = ParseValuesFloat(latestInput);
                LastKnownSliderCount = valuesFloat.Count;

                AppConfig cfg;
                lock (_configLock) { cfg = _config; }

                foreach (var mapping in cfg.SliderSets)
                {
                    if (mapping.Slider < valuesFloat.Count && !string.IsNullOrEmpty(mapping.VoicemeeterValue))
                    {
                        float setValue = map(valuesFloat[mapping.Slider], 0f, 100f, -60f, 12f);
                        VoiceMeeter.Remote.SetParameter(mapping.VoicemeeterValue, setValue);
                    }
                }
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
                    floats.Add(map(parsed, 0, 1024, 0f, 100f));
            }
            return floats;
        }

        public static float map(float value, float from1, float to1, float from2, float to2)
            => (value - from1) / (to1 - from1) * (to2 - from2) + from2;
    }
}