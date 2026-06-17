using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentDictionary<int, int> _lastAppliedRpm = new();
        private readonly ConcurrentDictionary<int, float> _lastTemp = new();
        private readonly ConcurrentDictionary<int, int> _lastCalcRpm = new();
        private readonly ConcurrentDictionary<int, long> _lastCalcTick = new();
        private readonly ConcurrentDictionary<int, string> _lastFingerprint = new();
        private readonly ConcurrentDictionary<int, double> _emaTemp = new();
        private readonly ConcurrentDictionary<int, int> _lastIdealRpm = new();

        private readonly ConcurrentDictionary<int, float> _lastRawTemp = new();
        private readonly ConcurrentDictionary<int, long> _lastRawTempTick = new();
        
        private readonly TaskCompletionSource _initTcs = new();
        private readonly SemaphoreSlim _modeLock = new(1, 1);

        public Task InitializationTask => _initTcs.Task;

        public bool IsActive
        {
            get { return _isEnabled || _isFullSpeed; }
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
                _powerModeListener.Changed += OnPowerModeChanged;
            if (_thermalModeListener != null)
                _thermalModeListener.Changed += OnThermalModeChanged;
            if (_itsModeListener != null)
                _itsModeListener.Changed += OnITSModeChanged;

            _configManager.SettingsChanged += () =>
            {
                if (!_disposed) { _ = ReevaluateStateAsync(); }
            };

            MessagingCenter.Subscribe<FanStateMessage>(this, m => 
            {
                if (m.State == FanState.Auto)
                    _ = SetEnabledAsync(false);
                else if (_configManager.Settings.IsCustomFanEnabled)
                    _ = SetEnabledAsync(true);
            });
            _sensorProvider.SensorUpdated += OnSensorUpdated;
        }

        public async Task InitializeAsync()
        {
            var saved = _configManager.Settings.FanMaxRpms;
            await _hardware.InitializeAsync(saved).ConfigureAwait(false);
            if (saved.Count > 0 && !_configManager.Settings.FallbackProbeDone)
            {
                _configManager.UpdateSetting(nameof(CustomFanCurveSettings.FallbackProbeDone), true);
            }

            _configManager.EnsureEntriesForFans(_hardware.AvailableFanIds);
            await ReevaluateStateAsync();
            _initTcs.TrySetResult();
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
                if (!_isEnabled && !_isFullSpeed)
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
            else
            {
                if (!_isEnabled && _uiOpenCount <= 0)
                    StopSensorsIfNeeded();
                await RestoreAutoFanAsync();
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
            if (_disposed) return;
            if (_isEnabled == enable) return;

            _isEnabled = enable;
            if (enable)
            {
                StartSensorsIfNeeded();
                if (_configManager.Settings.ForceRefreshOnEnable)
                    await ForceRefreshAsync();
            }
            else
            {
                if (!_isFullSpeed)
                    await RestoreAutoFanAsync();
                if (_uiOpenCount <= 0)
                    StopSensorsIfNeeded();
            }
        }

        private async Task RestoreAutoFanAsync()
        {
            await _hardware.RestoreAutoAsync().ConfigureAwait(false);
        }

        private void StartSensorsIfNeeded()
        {
            if (!_sensorProvider.IsRunning)
                _sensorProvider.Start(_configManager.Settings.SensorIntervalMs);
        }

        private void StopSensorsIfNeeded()
        {
            if (_sensorProvider.IsRunning)
                _sensorProvider.Stop();
        }

        private async void OnSensorUpdated(HardwareSensorSnapshot snapshot)
        {
            if (_uiOpenCount > 0)
                UpdateMonitoringOnly(snapshot);

            if (_isEnabled || _isFullSpeed)
                await Task.Run(() => ProcessAsync(snapshot));
        }

        private async Task ProcessAsync(HardwareSensorSnapshot snapshot)
        {
            if (!_isEnabled && !_isFullSpeed) return;

            var cpuTemp = Math.Max(0, snapshot.CpuTemp);
            var gpuTemp = Math.Max(0, snapshot.GpuTemp);

            var results = new Dictionary<int, (bool ShouldWrite, int TargetRpm, int CurrentRpm, float Temp)>();

            foreach (var entry in _configManager.GetAllEntries())
            {
                var temp = GetTemperatureForFan(entry, _hardware.AvailableFanIds, cpuTemp, gpuTemp);
                if (_isFullSpeed)
                {
                    await ProcessFullSpeed(entry, temp).ConfigureAwait(false);
                }
                else
                {
                    var res = await ProcessCurveMathAsync(entry, temp, cpuTemp, gpuTemp);
                    results[entry.FanId] = (res.ShouldWrite, res.TargetRpm, res.CurrentRpm, temp);
                }
            }

            if (!_isFullSpeed && _configManager.Settings.EnableAcousticOffset && _hardware.AvailableFanIds.Count >= 2)
            {
                int fan0 = _hardware.AvailableFanIds[0];
                int fan1 = _hardware.AvailableFanIds[1];
                if (results.ContainsKey(fan0) && results.ContainsKey(fan1))
                {
                    var r0 = results[fan0];
                    var r1 = results[fan1];
                    if (r0.TargetRpm > 0 && r1.TargetRpm > 0 && Math.Abs(r0.TargetRpm - r1.TargetRpm) < _configManager.Settings.AcousticOffsetDeltaRpm)
                    {
                        int maxRpm1 = _hardware.GetMaxRpm(fan1);
                        int minRpm1 = _hardware.GetMinRpm(fan1);
                        int newTargetRpm1 = r1.TargetRpm + _configManager.Settings.AcousticOffsetAddRpm;
                        
                        if (newTargetRpm1 > maxRpm1) 
                        {
                            newTargetRpm1 = r1.TargetRpm - _configManager.Settings.AcousticOffsetAddRpm;
                        }

                        minRpm1 = Math.Min(minRpm1, maxRpm1);
                        newTargetRpm1 = Math.Clamp(newTargetRpm1, minRpm1, maxRpm1);
                        
                        var hadLast = _lastAppliedRpm.TryGetValue(fan1, out var lastApplied);
                        var delta = hadLast ? Math.Abs(lastApplied - newTargetRpm1) : int.MaxValue;
                        bool isSteppingDown = _lastIdealRpm.TryGetValue(fan1, out var ideal) && _lastCalcRpm.TryGetValue(fan1, out var calc) && ideal < calc;
                        var requiredDelta = isSteppingDown ? _configManager.Settings.StepDownSpamProtectionDelta : _configManager.Settings.MinimumRpmChangeToApply;
                        
                        bool shouldWrite = (!isSteppingDown && _configManager.Settings.AlwaysWriteRpm) 
                            || (_configManager.Settings.ForceWriteWhenRpmZero && r1.CurrentRpm == 0)
                            || !hadLast || delta >= requiredDelta;

                        results[fan1] = (shouldWrite, newTargetRpm1, r1.CurrentRpm, r1.Temp);
                    }
                }
            }

            if (!_isFullSpeed)
            {
                if (results.Values.Any(r => r.TargetRpm == 0))
                {
                    await RestoreAutoFanAsync().ConfigureAwait(false);
                }

                foreach (var kvp in results)
                {
                    int fanId = kvp.Key;
                    var r = kvp.Value;
                    int finalRpm = r.CurrentRpm;
                    if (r.ShouldWrite)
                    {
                        finalRpm = await WriteFanRpmAsync(fanId, r.TargetRpm, r.CurrentRpm);
                    }
                    TryUpdateMonitoring(fanId, r.Temp, finalRpm, r.TargetRpm, false);
                }
            }
        }

        private static float GetTemperatureForFan(CustomFanCurveEntry entry, IReadOnlyList<int> fanIds, float cpuTemp, float gpuTemp)
        {
            if (entry.SensorSource == SensorSource.MaxCpuGpu) return Math.Max(cpuTemp, gpuTemp);
            if (entry.SensorSource == SensorSource.AverageCpuGpu) return (cpuTemp + gpuTemp) / 2.0f;
            
            for (var i = 0; i < fanIds.Count; i++)
            {
                if (fanIds[i] == entry.FanId)
                    return i == 1 ? gpuTemp : cpuTemp;
            }
            return cpuTemp;
        }

        private async Task ProcessFullSpeed(CustomFanCurveEntry entry, float temp)
        {
            var fanId = entry.FanId;
            var max = _hardware.GetMaxRpm(fanId);
            await _hardware.SetFanRpmAsync(fanId, max).ConfigureAwait(false);
            var rpm = 0;
            try { rpm = await _hardware.GetFanRpmAsync(fanId).ConfigureAwait(false); }
            catch { }

            _lastAppliedRpm[fanId] = max;
            TryUpdateMonitoring(fanId, temp, rpm, max, true);
        }

        private async Task<(bool ShouldWrite, int TargetRpm, int CurrentRpm)> ProcessCurveMathAsync(CustomFanCurveEntry entry, float temp, float rawCpu, float rawGpu)
        {
            var fanId = entry.FanId;
            var settings = _configManager.Settings;
            var now = DateTime.UtcNow.Ticks;
            var delayTicks = TimeSpan.FromMilliseconds(settings.CalculationDelayMs).Ticks;

            // Derivative Spike Predictor
            float rawCalcTemp = temp;
            if (settings.DerivativeSpikeThreshold > 0)
            {
                _lastRawTempTick.TryGetValue(fanId, out var lastRawTick);
                _lastRawTemp.TryGetValue(fanId, out var lastRaw);
                if (lastRawTick > 0 && now > lastRawTick)
                {
                    double dtSeconds = (now - lastRawTick) / (double)TimeSpan.TicksPerSecond;
                    if (dtSeconds > 0)
                    {
                        double rateOfChange = (temp - lastRaw) / dtSeconds;
                        if (rateOfChange >= settings.DerivativeSpikeThreshold)
                        {
                            float predictedTemp = (float)(temp + (rateOfChange * settings.DerivativeLookaheadSeconds));
                            float maxAllowed = Math.Max(temp, (float)settings.SafeMaxTemp);
                            rawCalcTemp = Math.Clamp(predictedTemp, temp, maxAllowed);
                        }
                    }
                }
                _lastRawTemp[fanId] = temp;
                _lastRawTempTick[fanId] = now;
            }

            // EMA Smoothing
            double alpha = settings.EmaAlpha;
            double smoothedTemp = _emaTemp.TryGetValue(fanId, out var lastEma)
                ? (temp * alpha) + (lastEma * (1.0 - alpha))
                : temp;
            _emaTemp[fanId] = smoothedTemp;
            
            float calcTemp = (float)Math.Max(smoothedTemp, rawCalcTemp);

            _lastCalcTick.TryGetValue(fanId, out var lastTick);
            _lastTemp.TryGetValue(fanId, out var lastTemp);
            _lastCalcRpm.TryGetValue(fanId, out var cachedRpm);

            var elapsed = !_lastCalcTick.ContainsKey(fanId) || now - lastTick >= delayTicks;
            var tempDelta = _lastTemp.ContainsKey(fanId) ? Math.Abs(lastTemp - calcTemp) : double.MaxValue;

            var fp = string.Join("|", entry.CurveNodes.OrderBy(n => n.Temperature).Select(n => $"{n.Temperature:F1}:{n.TargetPercent}"));
            _lastFingerprint.TryGetValue(fanId, out var lastFp);
            if (lastFp != fp)
            {
                _lastFingerprint[fanId] = fp;
                _lastCalcRpm.TryRemove(fanId, out _);
                _lastTemp.TryRemove(fanId, out _);
                _lastCalcTick.TryRemove(fanId, out _);
                _lastIdealRpm.TryRemove(fanId, out _);
                cachedRpm = 0;
                lastTemp = 0;
                elapsed = true;
                Logger.Debug($"Fan{fanId}: fingerprint changed, cachedRpm=0");
            }

            bool isSteppingDown = _lastIdealRpm.ContainsKey(fanId) && _lastCalcRpm.ContainsKey(fanId) && _lastIdealRpm[fanId] < _lastCalcRpm[fanId];

            var needRecalc = !_lastTemp.ContainsKey(fanId) || !_lastCalcRpm.ContainsKey(fanId) || isSteppingDown
                || (elapsed && (tempDelta >= settings.TemperatureDeltaThreshold
                    || !_lastAppliedRpm.ContainsKey(fanId) || _lastAppliedRpm[fanId] != cachedRpm));
            if (!elapsed && _lastCalcRpm.ContainsKey(fanId) && !isSteppingDown)
                needRecalc = false;

            int targetRpm;
            if (needRecalc)
            {
                float evalTemp = calcTemp;
                if (settings.HysteresisDeadzoneTemp > 0 && cachedRpm > 0)
                {
                    evalTemp += settings.HysteresisDeadzoneTemp;
                }

                var r = CustomFanCurveCalculator.Calculate(entry, evalTemp, _hardware.GetMaxRpm(fanId));
                if (!r.HasValue) return (false, cachedRpm, 0);

                int idealRpm = Math.Min(r.Value, _hardware.GetMaxRpm(fanId));
                
                float hardwareMax = Math.Max(rawCpu, rawGpu);
                float safetyEvalTemp = Math.Max(calcTemp, hardwareMax);
                int safeMinPercent = CustomFanCurveCalculator.GetSafeMinPercent(safetyEvalTemp);
                
                int safeMinRpm = (int)Math.Round(safeMinPercent / 100.0 * _hardware.GetMaxRpm(fanId));
                idealRpm = Math.Max(idealRpm, safeMinRpm);

                // Asymmetric Step-Down
                int currentRpm = _lastCalcRpm.TryGetValue(fanId, out int value) ? value : idealRpm;
                targetRpm = idealRpm;

                if (idealRpm < currentRpm)
                {
                    double dtSeconds = (now - lastTick) / (double)TimeSpan.TicksPerSecond;
                    int maxDrop = (int)(settings.StepDownRateRpmPerSec * dtSeconds);
                    targetRpm = Math.Max(idealRpm, currentRpm - maxDrop);
                    Logger.Debug($"Fan{fanId}: stepDown ideal={idealRpm} current={currentRpm} maxDrop={maxDrop} dtSec={dtSeconds:F4} -> target={targetRpm}");
                }

                int minRpm = _hardware.GetMinRpm(fanId);
                if (targetRpm > 0 && targetRpm < minRpm)
                {
                    targetRpm = minRpm;
                }
                _lastIdealRpm[fanId] = Math.Max(idealRpm, targetRpm);

                _lastTemp[fanId] = calcTemp;
                _lastCalcRpm[fanId] = targetRpm;
                _lastCalcTick[fanId] = now;

                Logger.Debug($"Fan{fanId}: recalc idealRpm={idealRpm} cachedRpm={cachedRpm} -> targetRpm={targetRpm} evalTemp={evalTemp:F1} isSteppingDown={isSteppingDown}");
            }
            else
            {
                targetRpm = cachedRpm;
            }

            var rpm = 0;
            try { rpm = await _hardware.GetFanRpmAsync(fanId).ConfigureAwait(false); }
            catch { }

            var hadLast = _lastAppliedRpm.TryGetValue(fanId, out var lastApplied);
            var delta = hadLast ? Math.Abs(lastApplied - targetRpm) : int.MaxValue;

            var requiredDelta = isSteppingDown ? settings.StepDownSpamProtectionDelta : settings.MinimumRpmChangeToApply;

            var shouldWrite = (!isSteppingDown && settings.AlwaysWriteRpm)
                || (settings.ForceWriteWhenRpmZero && rpm == 0)
                || !hadLast || delta >= requiredDelta;

            Logger.Debug($"Fan{fanId}: shouldWrite={shouldWrite} targetRpm={targetRpm} rpm={rpm} lastApplied={lastApplied} delta={delta} isSteppingDown={isSteppingDown}");

            return (shouldWrite, targetRpm, rpm);
        }

        private async Task<int> WriteFanRpmAsync(int fanId, int targetRpm, int rpm)
        {
            var settings = _configManager.Settings;
            if (settings.SpinUpBoostEnabled && rpm == 0 && targetRpm < settings.SpinUpBoostRpm)
            {
                await _hardware.SetFanRpmAsync(fanId, settings.SpinUpBoostRpm).ConfigureAwait(false);
                await Task.Delay(settings.SpinUpBoostDurationMs);
            }

            await _hardware.SetFanRpmAsync(fanId, targetRpm).ConfigureAwait(false);
            
            int newRpm = rpm;
            try { newRpm = await _hardware.GetFanRpmAsync(fanId).ConfigureAwait(false); }
            catch { }

            _lastAppliedRpm[fanId] = targetRpm;
            return newRpm;
        }

        private void TryUpdateMonitoring(int fanId, float temp, int rpm, int targetRpm, bool force)
        {
            var interval = TimeSpan.FromMilliseconds(_configManager.Settings.UiUpdateIntervalMs).Ticks;
            var now = DateTime.UtcNow.Ticks;
            if (force || now - _lastUiUpdateTick >= interval)
            {
                _monitoring.Update(fanId, temp, rpm, targetRpm);
                _lastUiUpdateTick = now;
            }
        }

        private void UpdateMonitoringOnly(HardwareSensorSnapshot snapshot)
        {
            var cpuTemp = Math.Max(0, snapshot.CpuTemp);
            var gpuTemp = Math.Max(0, snapshot.GpuTemp);

            foreach (var fanId in _hardware.AvailableFanIds)
            {
                var entry = _configManager.GetEntry(fanId) ?? new CustomFanCurveEntry { FanId = fanId };
                var temp = GetTemperatureForFan(entry, _hardware.AvailableFanIds, cpuTemp, gpuTemp);
                var rpm = 0;
                try { rpm = _hardware.GetFanRpmAsync(fanId).GetAwaiter().GetResult(); }
                catch { }

                _monitoring.Update(fanId, temp, rpm, _lastAppliedRpm.TryGetValue(fanId, out var tr) ? tr : 0);
            }
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
                if (_disposed) return;

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
                            await Task.Delay(settings.ModeSwitchRefreshDelayMs);
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
                    _modeLock.Release();
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
            if (_disposed) return;

            _disposed = true;
            _sensorProvider.SensorUpdated -= OnSensorUpdated;
            if (_powerModeListener != null)
                _powerModeListener.Changed -= OnPowerModeChanged;
            if (_thermalModeListener != null)
                _thermalModeListener.Changed -= OnThermalModeChanged;
            if (_itsModeListener != null)
                _itsModeListener.Changed -= OnITSModeChanged;

            MessagingCenter.Unsubscribe(this);
            StopSensorsIfNeeded();
            _modeLock.Dispose();
        }
    }
}
