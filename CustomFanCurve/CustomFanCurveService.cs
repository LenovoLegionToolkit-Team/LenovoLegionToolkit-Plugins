using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    internal class CustomFanCurveService : IDisposable
    {
        private readonly CustomFanCurveConfigManager _configManager;
        private readonly ICustomFanHardware _hardware;
        private readonly SensorProvider _sensorProvider;
        private readonly ICustomFanMonitoringService _monitoring;

        private readonly PowerModeFeature? _powerModeFeature;
        private readonly ITSModeFeature? _itsModeFeature;
        private readonly PowerModeListener? _powerModeListener;
        private readonly ThermalModeListener? _thermalModeListener;
        private readonly ITSModeListener? _itsModeListener;

        private bool _disposed;
        private bool _isEnabled;
        private bool _isFullSpeed;
        private bool _isMaxPerformanceMode;
        private int _uiOpenCount;
        private long _lastUiUpdateTick;

        private readonly Dictionary<FanType, int> _lastAppliedRpm = new();
        private readonly Dictionary<FanType, float> _lastTemp = new();
        private readonly Dictionary<FanType, int> _lastCalcRpm = new();
        private readonly Dictionary<FanType, long> _lastCalcTick = new();
        private readonly Dictionary<FanType, string> _lastFingerprint = new();

        private readonly SemaphoreSlim _modeLock = new(1, 1);

        public bool IsActive
        {
            get
            {
                return _isEnabled || _isFullSpeed;
            }
        }

        public CustomFanCurveService(CustomFanCurveConfigManager configManager, ICustomFanHardware hardware,
            SensorProvider sensorProvider, ICustomFanMonitoringService monitoring)
        {
            _configManager = configManager;
            _hardware = hardware;
            _sensorProvider = sensorProvider;
            _monitoring = monitoring;

            try { _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>(); }
            catch { }

            if (_powerModeFeature != null)
            {
                try { _powerModeListener = IoCContainer.Resolve<PowerModeListener>(); }
                catch { }

                try { _thermalModeListener = IoCContainer.Resolve<ThermalModeListener>(); }
                catch { }
            }
            else
            {
                try { _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>(); }
                catch { }

                try { _itsModeListener = IoCContainer.Resolve<ITSModeListener>(); }
                catch { }
            }

            if (_powerModeListener != null)
            {
                _powerModeListener.Changed += OnPowerModeChanged;
            }

            if (_thermalModeListener != null)
            {
                _thermalModeListener.Changed += OnThermalModeChanged;
            }

            if (_itsModeListener != null)
            {
                _itsModeListener.Changed += OnITSModeChanged;
            }

            _configManager.SettingsChanged += () =>
            {
                if (!_disposed)
                {
                    _ = ReevaluateStateAsync();
                }
            };

            MessagingCenter.Subscribe<FanStateMessage>(this, m => _ = SetEnabledAsync(m.State != FanState.Auto));
            _sensorProvider.SensorUpdated += OnSensorUpdated;
        }

        public async Task InitializeAsync()
        {
            await _hardware.InitializeAsync().ConfigureAwait(false);
            InitMaxRpmFromHardware();
            await ReevaluateStateAsync();
        }

        public void OnUIOpened()
        {
            _uiOpenCount++;
            StartSensorsIfNeeded();
        }

        public void OnUIClosed()
        {
            _uiOpenCount--;
            if (_uiOpenCount <= 0)
            {
                _uiOpenCount = 0;
                StopSensorsIfNeeded();
            }
        }

        public async Task SetFullSpeed(bool isFullSpeed)
        {
            _isFullSpeed = isFullSpeed;
            _configManager.UpdateSetting(nameof(CustomFanCurveSettings.IsFullSpeed), isFullSpeed);
            if (_isFullSpeed)
            {
                StartSensorsIfNeeded();
            }
            else if (!_isEnabled && _uiOpenCount <= 0)
            {
                StopSensorsIfNeeded();
            }

            await ReevaluateStateAsync();
        }

        public async Task SetCustomFanEnabled(bool enabled)
        {
            _configManager.UpdateSetting(nameof(CustomFanCurveSettings.IsCustomFanEnabled), enabled);
            await ReevaluateStateAsync();
        }

        private async Task SetEnabledAsync(bool enable)
        {
            if (_disposed)
            {
                return;
            }

            _isEnabled = enable;
            if (enable)
            {
                StartSensorsIfNeeded();
                if (_configManager.Settings.ForceRefreshOnEnable)
                {
                    await ForceRefreshAsync();
                }
            }
            else if (!_isFullSpeed && _uiOpenCount <= 0)
            {
                StopSensorsIfNeeded();
                await RestoreAutoFanAsync();
            }
        }

        private async Task RestoreAutoFanAsync()
        {
            foreach (var type in new[] { FanType.Cpu, FanType.Gpu, FanType.System })
            {
                try
                {
                    await _hardware.SetFanRpmAsync(type, 0).ConfigureAwait(false);
                }
                catch { }
            }
        }

        private void StartSensorsIfNeeded()
        {
            if (!_sensorProvider.IsRunning)
            {
                _sensorProvider.Start(_configManager.Settings.SensorIntervalMs);
            }
        }

        private void StopSensorsIfNeeded()
        {
            if (_sensorProvider.IsRunning)
            {
                _sensorProvider.Stop();
            }
        }

        private async void OnSensorUpdated(HardwareSensorSnapshot snapshot)
        {
            if (_uiOpenCount > 0)
            {
                UpdateMonitoringOnly(snapshot);
            }

            if (_isEnabled || _isFullSpeed)
            {
                await Task.Run(() => ProcessAsync(snapshot));
            }
        }

        private async Task ProcessAsync(HardwareSensorSnapshot snapshot)
        {
            if (!CanProcess(snapshot, out var cpu, out var gpu))
            {
                return;
            }

            foreach (var entry in _configManager.GetAllEntries())
            {
                var temp = entry.Type == FanType.Gpu ? gpu : cpu;
                if (_isFullSpeed)
                {
                    ProcessFullSpeed(entry, temp);
                }
                else
                {
                    await ProcessCurveAsync(entry, temp);
                }
            }
        }

        private void ProcessFullSpeed(CustomFanCurveEntry entry, float temp)
        {
            var max = _hardware.GetMaxRpm(entry.Type);
            _ = _hardware.SetFanRpmAsync(entry.Type, max);
            var rpm = 0;
            try { rpm = _hardware.GetFanRpmAsync(entry.Type).GetAwaiter().GetResult(); }
            catch { }

            _lastAppliedRpm[entry.Type] = max;
            TryUpdateMonitoring(entry.Type, temp, rpm, max, true);
        }

        private async Task ProcessCurveAsync(CustomFanCurveEntry entry, float temp)
        {
            var settings = _configManager.Settings;
            var now = DateTime.UtcNow.Ticks;
            var delayTicks = TimeSpan.FromMilliseconds(settings.CalculationDelayMs).Ticks;

            _lastCalcTick.TryGetValue(entry.Type, out var lastTick);
            _lastTemp.TryGetValue(entry.Type, out var lastTemp);
            _lastCalcRpm.TryGetValue(entry.Type, out var cachedRpm);

            var elapsed = !_lastCalcTick.ContainsKey(entry.Type) || now - lastTick >= delayTicks;
            var tempDelta = _lastTemp.ContainsKey(entry.Type) ? Math.Abs(lastTemp - temp) : double.MaxValue;

            var fp = string.Join("|", entry.CurveNodes.OrderBy(n => n.Temperature).Select(n => $"{n.Temperature:F1}:{n.TargetPercent}"));
            _lastFingerprint.TryGetValue(entry.Type, out var lastFp);
            if (lastFp != fp)
            {
                _lastFingerprint[entry.Type] = fp;
                _lastCalcRpm.Remove(entry.Type);
                _lastTemp.Remove(entry.Type);
                _lastCalcTick.Remove(entry.Type);
                cachedRpm = 0;
                lastTemp = 0;
                elapsed = true;
            }

            var needRecalc = !_lastTemp.ContainsKey(entry.Type) || !_lastCalcRpm.ContainsKey(entry.Type)
                || (elapsed && (tempDelta >= settings.TemperatureDeltaThreshold
                    || !_lastAppliedRpm.ContainsKey(entry.Type) || _lastAppliedRpm[entry.Type] != cachedRpm));
            if (!elapsed && _lastCalcRpm.ContainsKey(entry.Type))
            {
                needRecalc = false;
            }

            int targetRpm;
            if (needRecalc)
            {
                var r = CustomFanCurveCalculator.Calculate(entry, temp, settings.MaxRpm);
                if (!r.HasValue)
                {
                    return;
                }

                targetRpm = r.Value;
                if (temp > 50)
                {
                    targetRpm = Math.Max(targetRpm, _hardware.GetMinRpm(entry.Type));
                }

                _lastTemp[entry.Type] = temp;
                _lastCalcRpm[entry.Type] = targetRpm;
                _lastCalcTick[entry.Type] = now;
            }
            else
            {
                targetRpm = cachedRpm;
            }

            var rpm = 0;
            try { rpm = await _hardware.GetFanRpmAsync(entry.Type).ConfigureAwait(false); }
            catch { }

            var hadLast = _lastAppliedRpm.TryGetValue(entry.Type, out var lastApplied);
            var delta = hadLast ? Math.Abs(lastApplied - targetRpm) : int.MaxValue;
            var shouldWrite = settings.AlwaysWriteRpm || (settings.ForceWriteWhenRpmZero && rpm == 0)
                || !hadLast || delta >= settings.MinimumRpmChangeToApply;

            if (shouldWrite)
            {
                if (settings.SpinUpBoostEnabled && rpm == 0 && targetRpm < settings.SpinUpBoostRpm)
                {
                    await _hardware.SetFanRpmAsync(entry.Type, settings.SpinUpBoostRpm).ConfigureAwait(false);
                    await Task.Delay(settings.SpinUpBoostDurationMs);
                }

                await _hardware.SetFanRpmAsync(entry.Type, targetRpm).ConfigureAwait(false);
                try { rpm = await _hardware.GetFanRpmAsync(entry.Type).ConfigureAwait(false); }
                catch { }

                _lastAppliedRpm[entry.Type] = targetRpm;
            }

            TryUpdateMonitoring(entry.Type, temp, rpm, targetRpm, false);
        }

        private void TryUpdateMonitoring(FanType type, float temp, int rpm, int targetRpm, bool force)
        {
            var interval = TimeSpan.FromMilliseconds(_configManager.Settings.UiUpdateIntervalMs).Ticks;
            var now = DateTime.UtcNow.Ticks;
            if (force || now - _lastUiUpdateTick >= interval)
            {
                _monitoring.Update(type, temp, rpm, targetRpm);
                _lastUiUpdateTick = now;
            }
        }

        private static bool CanProcess(HardwareSensorSnapshot s, out float cpu, out float gpu)
        {
            cpu = Math.Max(0, s.CpuTemp);
            gpu = Math.Max(0, s.GpuTemp);
            return true;
        }

        private void UpdateMonitoringOnly(HardwareSensorSnapshot snapshot)
        {
            foreach (var type in new[] { FanType.Cpu, FanType.Gpu, FanType.System })
            {
                var temp = type == FanType.Gpu ? snapshot.GpuTemp : snapshot.CpuTemp;
                var rpm = 0;
                try { rpm = _hardware.GetFanRpmAsync(type).GetAwaiter().GetResult(); }
                catch { }

                _monitoring.Update(type, temp, rpm, _lastAppliedRpm.TryGetValue(type, out var tr) ? tr : 0);
            }
        }

        private void InitMaxRpmFromHardware()
        {
            if (_configManager.Settings.IsMaxRpmInitialized)
            {
                return;
            }

            try
            {
                var maxRpm = _hardware.GetMaxRpm(FanType.Cpu);
                if (maxRpm > 0)
                {
                    _configManager.UpdateSetting(nameof(CustomFanCurveSettings.MaxRpm), maxRpm);
                    _configManager.UpdateSetting(nameof(CustomFanCurveSettings.IsMaxRpmInitialized), true);
                }
            }
            catch { }
        }

        private async void OnPowerModeChanged(object? s, PowerModeListener.ChangedEventArgs e)
        {
            await HandleModeChangeAsync();
        }

        private async void OnThermalModeChanged(object? s, ThermalModeListener.ChangedEventArgs e)
        {
            await HandleModeChangeAsync();
        }

        private async void OnITSModeChanged(object? s, ITSModeListener.ChangedEventArgs e)
        {
            await HandleModeChangeAsync();
        }

        private async Task HandleModeChangeAsync()
        {
            await _modeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                _isMaxPerformanceMode = await CheckIsMaxPerformanceModeAsync().ConfigureAwait(false);

                var user = _configManager.Settings.IsCustomFanEnabled;
                var shouldEnable = user && (_hardware.IsSupported &&
                    (_isMaxPerformanceMode || _configManager.Settings.ApplyToAllPowerModes));
                await SetEnabledAsync(shouldEnable).ConfigureAwait(false);

                var settings = _configManager.Settings;
                if (settings.ForceRefreshOnModeSwitch && (shouldEnable || _isFullSpeed))
                {
                    for (var i = 0; i < settings.ModeSwitchRefreshCount; i++)
                    {
                        await ForceRefreshAsync();
                        if (i < settings.ModeSwitchRefreshCount - 1)
                        {
                            await Task.Delay(settings.ModeSwitchRefreshDelayMs);
                        }
                    }
                }
                else if (settings.ClearCachedStateWhenLeavingCustomMode && !shouldEnable && !_isFullSpeed)
                {
                    _lastAppliedRpm.Clear();
                    _lastTemp.Clear();
                    _lastCalcRpm.Clear();
                    _lastCalcTick.Clear();
                    _lastFingerprint.Clear();
                }
            }
            finally
            {
                if (_modeLock.CurrentCount == 0)
                {
                    _modeLock.Release();
                }
            }
        }

        private async Task<bool> CheckIsMaxPerformanceModeAsync()
        {
            if (_powerModeFeature != null)
            {
                try
                {
                    var state = await _powerModeFeature.GetStateAsync()
                        .WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token).ConfigureAwait(false);
                    return state == PowerModeState.GodMode;
                }
                catch { }
            }
            else if (_itsModeFeature != null)
            {
                try
                {
                    var state = await _itsModeFeature.GetStateAsync()
                        .WaitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token).ConfigureAwait(false);
                    return state == ITSMode.MmcGeek;
                }
                catch { }
            }

            return false;
        }

        private async Task ForceRefreshAsync()
        {
            var snapshot = _sensorProvider.LatestSnapshot;
            await ProcessAsync(snapshot ?? new HardwareSensorSnapshot());
        }

        private async Task ReevaluateStateAsync()
        {
            _isMaxPerformanceMode = await CheckIsMaxPerformanceModeAsync().ConfigureAwait(false);
            await HandleModeChangeAsync();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sensorProvider.SensorUpdated -= OnSensorUpdated;
            if (_powerModeListener != null)
            {
                _powerModeListener.Changed -= OnPowerModeChanged;
            }

            if (_thermalModeListener != null)
            {
                _thermalModeListener.Changed -= OnThermalModeChanged;
            }

            if (_itsModeListener != null)
            {
                _itsModeListener.Changed -= OnITSModeChanged;
            }

            MessagingCenter.Unsubscribe(this);
            StopSensorsIfNeeded();
            _modeLock.Dispose();
        }
    }
}
