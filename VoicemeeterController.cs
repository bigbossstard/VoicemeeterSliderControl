using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace VoicemeeterSliderControl
{
    public sealed class VoicemeeterController : IDisposable
    {
        const float SendThresholdDb = 0.10f;
        // OSD is a user-facing movement hint, not a level meter. A larger
        // threshold prevents normal potentiometer ADC noise from continually
        // refreshing the overlay while the knob is stationary.
        const float OsdThresholdDb = 1.00f;
        static readonly TimeSpan RetryDelay =
            TimeSpan.FromMilliseconds(500);

        readonly object _sync = new object();
        readonly AutoResetEvent _wake =
            new AutoResetEvent(false);
        readonly Thread _worker;

        SliderMapping[] _mappings = Array.Empty<SliderMapping>();
        float[] _latestValues = Array.Empty<float>();

        Dictionary<string, float> _lastSent =
            new Dictionary<string, float>(
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, float> _lastOsd =
            new Dictionary<string, float>(
                StringComparer.OrdinalIgnoreCase);

        DateTime _nextRetryUtc = DateTime.MinValue;
        bool _enabled = true;
        bool _stopping;

        public event Action<int, SliderMapping, float>
            SliderValueApplied;

        public VoicemeeterController()
        {
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Voicemeeter Output Worker"
            };

            _worker.Start();
        }

        public void UpdateConfiguration(AppConfig config)
        {
            var newMappings =
                (config?.SliderSets ??
                 new List<SliderMapping>())
                .Where(x =>
                    x != null &&
                    x.Slider >= 0 &&
                    !string.IsNullOrWhiteSpace(
                        x.VoicemeeterValue))
                .Select(x => new SliderMapping
                {
                    Slider = x.Slider,
                    VoicemeeterValue =
                        x.VoicemeeterValue.Trim(),
                    DisplayName =
                        string.IsNullOrWhiteSpace(x.DisplayName)
                            ? x.VoicemeeterValue
                            : x.DisplayName.Trim()
                })
                .GroupBy(x => x.Slider)
                .Select(x => x.Last())
                .ToArray();

            lock (_sync)
            {
                _mappings = newMappings;
                _lastSent.Clear();
                _lastOsd.Clear();
                _nextRetryUtc = DateTime.MinValue;
            }

            _wake.Set();
        }

        public void SetEnabled(bool enabled)
        {
            lock (_sync)
            {
                _enabled = enabled;

                if (!enabled)
                {
                    _lastSent.Clear();
                    _lastOsd.Clear();
                }
            }

            _wake.Set();
        }

        public void SubmitValues(
            IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0)
                return;

            lock (_sync)
            {
                _latestValues = values.ToArray();
            }

            _wake.Set();
        }

        void WorkerLoop()
        {
            while (true)
            {
                _wake.WaitOne(20);

                lock (_sync)
                {
                    if (_stopping) return;
                }

                ProcessLatest();
            }
        }

        void ProcessLatest()
        {
            SliderMapping[] mappings;
            float[] values;
            bool enabled;

            lock (_sync)
            {
                mappings = _mappings;
                values = _latestValues;
                enabled = _enabled;
            }

            if (!enabled ||
                mappings.Length == 0 ||
                values.Length == 0)
                return;

            var updates =
                new List<(SliderMapping Mapping, float Db)>();

            foreach (var mapping in mappings)
            {
                if (mapping.Slider >= values.Length)
                    continue;

                float db = ArduinoConnection.Map(
                    values[mapping.Slider],
                    0f,
                    100f,
                    -60f,
                    12f);

                db = Math.Clamp(db, -60f, 12f);

                lock (_sync)
                {
                    if (!_lastSent.TryGetValue(
                            mapping.VoicemeeterValue,
                            out float previous) ||
                        Math.Abs(db - previous) >=
                        SendThresholdDb)
                    {
                        updates.Add((mapping, db));
                    }
                }
            }

            if (updates.Count == 0 ||
                DateTime.UtcNow < _nextRetryUtc)
                return;

            try
            {
                string script = string.Join(
                    ";",
                    updates.Select(x =>
                        $"{x.Mapping.VoicemeeterValue}=" +
                        $"{x.Db.ToString(
                            "0.00",
                            CultureInfo.InvariantCulture)}"));

                VoiceMeeter.Remote.SetParameters(script);

                lock (_sync)
                {
                    foreach (var update in updates)
                        _lastSent[
                            update.Mapping.VoicemeeterValue] =
                            update.Db;

                    _nextRetryUtc = DateTime.MinValue;
                }

                var osdCandidate = updates
                    .Where(x =>
                    {
                        lock (_sync)
                        {
                            return !_lastOsd.TryGetValue(
                                       x.Mapping.VoicemeeterValue,
                                       out float previous) ||
                                   Math.Abs(x.Db - previous) >=
                                   OsdThresholdDb;
                        }
                    })
                    .LastOrDefault();

                if (osdCandidate.Mapping != null)
                {
                    lock (_sync)
                    {
                        _lastOsd[
                            osdCandidate.Mapping.VoicemeeterValue] =
                            osdCandidate.Db;
                    }

                    SliderValueApplied?.Invoke(
                        osdCandidate.Mapping.Slider,
                        osdCandidate.Mapping,
                        osdCandidate.Db);
                }
            }
            catch
            {
                lock (_sync)
                {
                    _lastSent.Clear();
                    _nextRetryUtc =
                        DateTime.UtcNow + RetryDelay;
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_stopping) return;
                _stopping = true;
            }

            _wake.Set();

            try { _worker.Join(1000); }
            catch { }

            _wake.Dispose();
        }
    }
}
