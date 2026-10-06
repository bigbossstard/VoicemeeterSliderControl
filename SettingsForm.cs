using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VoicemeeterSliderControl
{
    public class SettingsForm : Form
    {
        ComboBox _portCombo;
        CheckBox _autoStartCheck;
        Button _applyBtn;

        List<Label> _sliderLabels =
            new List<Label>();

        List<ComboBox> _sliderCombos =
            new List<ComboBox>();

        List<VmInterop.ChannelInfo> _channels;
        Action<AppConfig> _onApply;
        AppConfig _current;

        const int RowsTop = 100;
        const int RowHeight = 30;

        public SettingsForm(
            AppConfig current,
            Action<AppConfig> onApply)
        {
            _onApply = onApply;
            _current =
                current ?? new AppConfig();

            Text =
                "Настройки Voicemeeter Slider Control";

            Width = 480;
            StartPosition =
                FormStartPosition.CenterScreen;

            MaximizeBox = false;
            MinimizeBox = false;

            _channels =
                VmInterop.DiscoverChannels();

            var portLabel = new Label
            {
                Text = "COM-порт Arduino:",
                Left = 10,
                Top = 10,
                Width = 150
            };

            _portCombo = new ComboBox
            {
                Left = 170,
                Top = 8,
                Width = 280,
                DropDownStyle =
                    ComboBoxStyle.DropDownList
            };

            _portCombo.Items.AddRange(
                GetPorts());

            if (_portCombo.Items.Contains(
                    _current.PortName))
            {
                _portCombo.SelectedItem =
                    _current.PortName;
            }

            _portCombo.SelectedIndexChanged +=
                (s, e) => RebuildSliderRows();

            Controls.Add(portLabel);
            Controls.Add(_portCombo);

            var typeLabel = new Label
            {
                Text =
                    $"Voicemeeter {VmInterop.GetVoicemeeterKindName()}, каналов: {_channels.Count}",
                Left = 10,
                Top = 40,
                Width = 440
            };

            Controls.Add(typeLabel);

            _autoStartCheck = new CheckBox
            {
                Text =
                    "Запускать при старте Windows",
                Left = 10,
                Top = 65,
                Width = 300,
                Checked =
                    AutoStart.IsEnabled()
            };

            Controls.Add(_autoStartCheck);

            _applyBtn = new Button
            {
                Text = "Применить",
                Left = 170,
                Width = 280,
                Height = 30
            };

            _applyBtn.Click +=
                ApplyBtn_Click;

            Controls.Add(_applyBtn);

            RebuildSliderRows();
        }

        static string[] GetPorts()
        {
            try
            {
                return System.IO.Ports
                    .SerialPort.GetPortNames();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        int DetectSliderCount()
        {
            if (Program.LastKnownSliderCount > 0)
                return Program.LastKnownSliderCount;

            int configured =
                _current.SliderSets?
                    .Where(x =>
                        x != null &&
                        x.Slider >= 0)
                    .Select(x =>
                        x.Slider + 1)
                    .DefaultIfEmpty(0)
                    .Max() ?? 0;

            return Math.Max(
                ArduinoConnection
                    .DefaultSliderCount,
                configured);
        }

        void RebuildSliderRows()
        {
            foreach (var label
                     in _sliderLabels)
            {
                Controls.Remove(label);
            }

            foreach (var combo
                     in _sliderCombos)
            {
                Controls.Remove(combo);
            }

            _sliderLabels.Clear();
            _sliderCombos.Clear();

            int count =
                DetectSliderCount();

            int y = RowsTop;

            for (int i = 0;
                 i < count;
                 i++)
            {
                var label = new Label
                {
                    Text =
                        $"Потенциометр {i + 1}:",
                    Left = 10,
                    Top = y,
                    Width = 150
                };

                var combo = new ComboBox
                {
                    Left = 170,
                    Top = y - 2,
                    Width = 280,
                    DropDownStyle =
                        ComboBoxStyle.DropDownList
                };

                combo.Items.Add(
                    "(не назначено)");

                foreach (var ch in _channels)
                {
                    combo.Items.Add(
                        $"{ch.Kind} {ch.Index + 1} — {ch.Label}");
                }

                var existing =
                    _current.SliderSets?
                        .FirstOrDefault(s =>
                            s != null &&
                            s.Slider == i);

                if (existing != null)
                {
                    var match =
                        _channels.FirstOrDefault(c =>
                            string.Equals(
                                $"{c.ParamPrefix}.Gain",
                                existing.VoicemeeterValue,
                                StringComparison
                                    .OrdinalIgnoreCase));

                    if (match != null)
                    {
                        combo.SelectedItem =
                            $"{match.Kind} {match.Index + 1} — {match.Label}";
                    }
                }

                if (combo.SelectedIndex < 0)
                    combo.SelectedIndex = 0;

                Controls.Add(label);
                Controls.Add(combo);

                _sliderLabels.Add(label);
                _sliderCombos.Add(combo);

                y += RowHeight;
            }

            _applyBtn.Top = y + 10;
            Height = y + 90;
        }

        void ApplyBtn_Click(
            object sender,
            EventArgs e)
        {
            if (_portCombo.SelectedItem == null)
            {
                MessageBox.Show(
                    "Выбери COM-порт.",
                    "Настройки",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (!AutoStart.SetEnabled(
                    _autoStartCheck.Checked))
            {
                MessageBox.Show(
                    "Не удалось изменить автозапуск Windows.",
                    "Настройки",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            var cfg = new AppConfig
            {
                PortName =
                    _portCombo.SelectedItem
                        .ToString(),
                BaudRate =
                    ArduinoConnection
                        .DefaultBaudRate,
                ShowOsd = _current.ShowOsd,
                SliderSets =
                    new List<SliderMapping>()
            };

            for (int i = 0;
                 i < _sliderCombos.Count;
                 i++)
            {
                var combo =
                    _sliderCombos[i];

                if (combo.SelectedIndex <= 0)
                    continue;

                var ch =
                    _channels[
                        combo.SelectedIndex - 1];

                cfg.SliderSets.Add(
                    new SliderMapping
                    {
                        Slider = i,
                        VoicemeeterValue =
                            $"{ch.ParamPrefix}.Gain",
                        DisplayName = ch.Label
                    });
            }

            _current = cfg;
            _onApply(cfg);
            Close();
        }
    }
}
