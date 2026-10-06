using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        static readonly object _configLock =
            new object();

        static readonly object _cleanupLock =
            new object();

        static ArduinoConnection _connection;
        static VoicemeeterController _vmController;
        static OsdOverlayForm _osd;
        static bool _cleanedUp;

        public static volatile int LastKnownSliderCount = 0;

        static string ConfigPath =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "VoicemeeterSliderControl",
                "config.json");

        static System.Drawing.Icon LoadEmbeddedIcon(
            string name)
        {
            var asm =
                System.Reflection.Assembly
                    .GetExecutingAssembly();

            using var stream =
                asm.GetManifestResourceStream(
                    $"VoicemeeterSliderControl.{name}");

            return stream == null
                ? System.Drawing.SystemIcons.Application
                : new System.Drawing.Icon(stream);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(
                UnhandledExceptionMode.CatchException);

            AppDomain.CurrentDomain.UnhandledException +=
                (s, e) => MessageBox.Show(
                    $"Необработанная ошибка: {e.ExceptionObject}",
                    "Критическая ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

            Application.ThreadException +=
                (s, e) => MessageBox.Show(
                    $"Ошибка UI-потока: {e.Exception}",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

            _config = new AppConfig();
            LoadConfig();

            _vmSession = null;

            for (int attempt = 0;
                 attempt < 10 &&
                 _vmSession == null;
                 attempt++)
            {
                try
                {
                    _vmSession =
                        VoiceMeeter.Remote.Initialize(
                            Voicemeeter.RunVoicemeeterParam
                                .VoicemeeterPotato)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }

            if (_vmSession == null)
            {
                MessageBox.Show(
                    "Не удалось подключиться к Voicemeeter.",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            lock (_configLock)
                NormalizeConfig(_config);

            if (_config.SliderSets.Count == 0 ||
                string.IsNullOrWhiteSpace(
                    _config.PortName))
            {
                using var form =
                    new SettingsForm(
                        _config,
                        cfg =>
                        {
                            lock (_configLock)
                            {
                                _config = cfg;
                                NormalizeConfig(_config);
                            }

                            SaveConfig();
                        });

                form.ShowDialog();

                if (_config.SliderSets.Count == 0 ||
                    string.IsNullOrWhiteSpace(
                        _config.PortName))
                {
                    Cleanup();
                    return;
                }
            }

            _osd = new OsdOverlayForm();
            _vmController = new VoicemeeterController();

            _vmController.SliderValueApplied +=
                (slider, mapping, db) =>
                {
                    bool showOsd;

                    lock (_configLock)
                        showOsd = _config.ShowOsd;

                    if (showOsd && _enabled)
                    {
                        _osd.ShowValue(
                            slider,
                            mapping.DisplayName,
                            mapping.VoicemeeterValue,
                            db);
                    }
                };

            _vmController.UpdateConfiguration(
                _config);

            _vmController.SetEnabled(
                _enabled);

            var menu =
                new ContextMenuStrip();

            var toggleItem =
                new ToolStripMenuItem(
                    "Включено ✓");

            toggleItem.Click +=
                (s, e) =>
                {
                    _enabled = !_enabled;

                    toggleItem.Text =
                        _enabled
                            ? "Включено ✓"
                            : "Выключено";

                    _vmController.SetEnabled(
                        _enabled);

                    if (!_enabled)
                        _osd.HideOverlay();
                };

            var osdItem =
                new ToolStripMenuItem();

            void RefreshOsdText()
            {
                bool showOsd;

                lock (_configLock)
                    showOsd = _config.ShowOsd;

                osdItem.Text =
                    showOsd
                        ? "Подсказка на экране ✓"
                        : "Подсказка на экране";
            }

            osdItem.Click +=
                (s, e) =>
                {
                    lock (_configLock)
                        _config.ShowOsd =
                            !_config.ShowOsd;

                    SaveConfig();
                    RefreshOsdText();

                    bool showOsd;

                    lock (_configLock)
                        showOsd = _config.ShowOsd;

                    if (!showOsd)
                        _osd.HideOverlay();
                };

            RefreshOsdText();

            var settingsItem =
                new ToolStripMenuItem(
                    "Настройки...");

            settingsItem.Click +=
                (s, e) => OpenSettings();

            var exitItem =
                new ToolStripMenuItem("Выход");

            exitItem.Click +=
                (s, e) =>
                {
                    Cleanup();
                    Application.Exit();
                };

            menu.Items.Add(toggleItem);
            menu.Items.Add(osdItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(
                new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon =
                    LoadEmbeddedIcon("icon.ico"),
                Text =
                    "Voicemeeter Slider Control",
                ContextMenuStrip = menu,
                Visible = true
            };

            _connection =
                new ArduinoConnection();

            _connection.ValuesReceived +=
                OnValuesReceived;

            _connection.ConnectionStateChanged +=
                connected =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.Icon =
                            LoadEmbeddedIcon(
                                connected
                                    ? "icon_connected.ico"
                                    : "icon.ico");
                    }

                    if (!connected)
                    {
                        LastKnownSliderCount = 0;
                        _osd?.HideOverlay();
                    }
                };

            _connection.Start(
                _config.PortName,
                _config.BaudRate);

            Application.ApplicationExit += (s, e) => Cleanup();
            Application.Run();
        }

        static void OpenSettings()
        {
            AppConfig snapshot;

            lock (_configLock)
                snapshot =
                    CloneConfig(_config);

            using var form =
                new SettingsForm(
                    snapshot,
                    cfg =>
                    {
                        lock (_configLock)
                        {
                            _config = cfg;
                            NormalizeConfig(_config);
                        }

                        if (!SaveConfig())
                            return;

                        _vmController
                            .UpdateConfiguration(
                                _config);

                        _connection.UpdatePort(
                            _config.PortName,
                            _config.BaudRate);
                    });

            form.ShowDialog();
        }

        static void OnValuesReceived(
            List<float> values)
        {
            LastKnownSliderCount =
                values.Count;

            _vmController?.SubmitValues(values);
        }

        static void LoadConfig()
        {
            if (!File.Exists(ConfigPath))
                return;

            for (int attempt = 0;
                 attempt < 3;
                 attempt++)
            {
                try
                {
                    string json =
                        File.ReadAllText(ConfigPath);

                    var config =
                        JsonSerializer
                            .Deserialize<AppConfig>(
                                json);

                    if (config != null)
                    {
                        NormalizeConfig(config);
                        _config = config;
                    }

                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(
                        250);
                }
            }

            _config = new AppConfig();
        }

        static bool SaveConfig()
        {
            try
            {
                string json;

                lock (_configLock)
                {
                    NormalizeConfig(_config);

                    json =
                        JsonSerializer.Serialize(
                            _config,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
                }

                string directory =
                    Path.GetDirectoryName(
                        ConfigPath);

                Directory.CreateDirectory(
                    directory);

                string tempPath =
                    ConfigPath + ".tmp";

                File.WriteAllText(
                    tempPath,
                    json,
                    new UTF8Encoding(false));

                File.Move(
                    tempPath,
                    ConfigPath,
                    true);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось сохранить настройки:\n{ex.Message}",
                    "Ошибка сохранения",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return false;
            }
        }

        static void NormalizeConfig(
            AppConfig config)
        {
            if (config == null) return;

            if (config.BaudRate <= 0)
                config.BaudRate =
                    ArduinoConnection
                        .DefaultBaudRate;

            config.PortName ??= "";

            config.SliderSets ??=
                new List<SliderMapping>();

            config.SliderSets =
                config.SliderSets
                    .Where(x =>
                        x != null &&
                        x.Slider >= 0 &&
                        !string.IsNullOrWhiteSpace(
                            x.VoicemeeterValue))
                    .GroupBy(x => x.Slider)
                    .Select(x => x.Last())
                    .ToList();

            foreach (var mapping
                     in config.SliderSets)
            {
                mapping.VoicemeeterValue =
                    mapping.VoicemeeterValue.Trim();

                mapping.DisplayName =
                    mapping.DisplayName?.Trim() ?? "";
            }
        }

        static AppConfig CloneConfig(
            AppConfig source)
        {
            if (source == null)
                return new AppConfig();

            var clone = new AppConfig
            {
                PortName = source.PortName,
                BaudRate = source.BaudRate,
                ShowOsd = source.ShowOsd,
                SliderSets =
                    source.SliderSets?
                        .Select(x =>
                            new SliderMapping
                            {
                                Slider = x.Slider,
                                VoicemeeterValue =
                                    x.VoicemeeterValue,
                                DisplayName =
                                    x.DisplayName
                            })
                        .ToList() ??
                    new List<SliderMapping>()
            };

            NormalizeConfig(clone);
            return clone;
        }

        static void Cleanup()
        {
            lock (_cleanupLock)
            {
                if (_cleanedUp) return;
                _cleanedUp = true;
            }

            try { _connection?.Stop(); }
            catch { }

            try { _vmController?.Dispose(); }
            catch { }

            try { _osd?.Dispose(); }
            catch { }

            try { _vmSession?.Dispose(); }
            catch { }

            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            catch
            {
            }
        }
    }
}
