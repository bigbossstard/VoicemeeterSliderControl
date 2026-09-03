using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace VoicemeeterSliderControl
{
    static class Program
    {
        static IDisposable _vmSession;
        static AppConfig _config;
        static bool _enabled = true;
        static NotifyIcon _trayIcon;
        static readonly object _configLock = new object();
        static ArduinoConnection _connection;

        public static volatile int LastKnownSliderCount = 0;

        static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoicemeeterSliderControl", "config.json");

        static System.Drawing.Icon LoadEmbeddedIcon(string name)
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream($"VoicemeeterSliderControl.{name}");
            if (stream == null) return System.Drawing.SystemIcons.Application;
            return new System.Drawing.Icon(stream);
        }

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
                using var form = new SettingsForm(_config, cfg =>
                {
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

            var menu = new ContextMenuStrip();
            var toggleItem = new ToolStripMenuItem("Включено ✓");
            toggleItem.Click += (s, e) =>
            {
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
                Icon = LoadEmbeddedIcon("icon.ico"),
                Text = "Voicemeeter Slider Control",
                ContextMenuStrip = menu,
                Visible = true
            };

            _connection = new ArduinoConnection();
            _connection.ValuesReceived += OnValuesReceived;
            _connection.ConnectionStateChanged += connected =>
            {
                _trayIcon.Icon = LoadEmbeddedIcon(connected ? "icon_connected.ico" : "icon.ico");
            };
            _connection.Start(_config.PortName, _config.BaudRate);
            Application.ApplicationExit += (s, e) => Cleanup();
            Application.Run();
        }

        static void OpenSettings()
        {
            AppConfig snapshot;
            lock (_configLock) { snapshot = _config; }

            using var form = new SettingsForm(snapshot, cfg =>
            {
                lock (_configLock) { _config = cfg; }
                SaveConfig();
                _connection.UpdatePort(cfg.PortName, cfg.BaudRate);
            });
            form.ShowDialog();
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
            try { _connection?.Stop(); } catch { }
            try { _vmSession?.Dispose(); } catch { }
            try { _trayIcon.Visible = false; } catch { }
        }

        static void OnValuesReceived(List<float> valuesFloat)
        {
            if (!_enabled) return;

            LastKnownSliderCount = valuesFloat.Count;

            AppConfig cfg;
            lock (_configLock) { cfg = _config; }

            foreach (var mapping in cfg.SliderSets)
            {
                if (mapping.Slider < valuesFloat.Count && !string.IsNullOrEmpty(mapping.VoicemeeterValue))
                {
                    float setValue = ArduinoConnection.Map(valuesFloat[mapping.Slider], 0f, 100f, -60f, 12f);
                    VoiceMeeter.Remote.SetParameter(mapping.VoicemeeterValue, setValue);
                }
            }
        }
    }
}