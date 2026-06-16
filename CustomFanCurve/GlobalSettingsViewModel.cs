using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public class GlobalSettingsViewModel : INotifyPropertyChanged
    {
        private readonly CustomFanCurveConfigManager _configManager;
        public bool IsLegionDevice { get; }

        public GlobalSettingsViewModel(CustomFanCurveConfigManager configManager, bool isLegionDevice)
        {
            _configManager = configManager;
            IsLegionDevice = isLegionDevice;
            var s = configManager.Settings;

            _isCustomFanEnabled = s.IsCustomFanEnabled;
            _applyToAllPowerModes = s.ApplyToAllPowerModes;
            _debugMode = s.DebugMode;
            _sensorIntervalMs = s.SensorIntervalMs;
            _calculationDelayMs = s.CalculationDelayMs;
            _temperatureDeltaThreshold = s.TemperatureDeltaThreshold;
            _ignoreZeroTemperature = s.IgnoreZeroTemperature;
            _alwaysWriteRpm = s.AlwaysWriteRpm;
            _forceWriteWhenRpmZero = s.ForceWriteWhenRpmZero;
            _minimumRpmChangeToApply = s.MinimumRpmChangeToApply;
            _spinUpBoostEnabled = s.SpinUpBoostEnabled;
            _spinUpBoostRpm = s.SpinUpBoostRpm;
            _spinUpBoostDurationMs = s.SpinUpBoostDurationMs;
            _forceRefreshOnModeSwitch = s.ForceRefreshOnModeSwitch;
            _modeSwitchRefreshCount = s.ModeSwitchRefreshCount;
            _modeSwitchRefreshDelayMs = s.ModeSwitchRefreshDelayMs;
            _forceRefreshOnEnable = s.ForceRefreshOnEnable;
            _clearCachedStateWhenLeavingCustomMode = s.ClearCachedStateWhenLeavingCustomMode;
            _uiUpdateIntervalMs = s.UiUpdateIntervalMs;
            _useCachedSnapshotForForcedRefresh = s.UseCachedSnapshotForForcedRefresh;
            _enableMaxFanWriteEachCycle = s.EnableMaxFanWriteEachCycle;
            
            _emaAlpha = s.EmaAlpha;
            _stepDownRateRpmPerSec = s.StepDownRateRpmPerSec;
            _stepDownSpamProtectionDelta = s.StepDownSpamProtectionDelta;
            _uiDebounceDelayMs = s.UiDebounceDelayMs;
            _safeMinTemp = s.SafeMinTemp;
            _safeMaxTemp = s.SafeMaxTemp;
            _safeMaxPercentAtMaxTemp = s.SafeMaxPercentAtMaxTemp;

            _enableAcousticOffset = s.EnableAcousticOffset;
            _acousticOffsetDeltaRpm = s.AcousticOffsetDeltaRpm;
            _acousticOffsetAddRpm = s.AcousticOffsetAddRpm;
            _hysteresisDeadzoneTemp = s.HysteresisDeadzoneTemp;
            _derivativeSpikeThreshold = s.DerivativeSpikeThreshold;
            _derivativeLookaheadSeconds = s.DerivativeLookaheadSeconds;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SaveAll()
        {
            Save(nameof(CustomFanCurveSettings.IsCustomFanEnabled), _isCustomFanEnabled);
            Save(nameof(CustomFanCurveSettings.ApplyToAllPowerModes), _applyToAllPowerModes);
            Save(nameof(CustomFanCurveSettings.DebugMode), _debugMode);
            Save(nameof(CustomFanCurveSettings.SensorIntervalMs), _sensorIntervalMs);
            Save(nameof(CustomFanCurveSettings.CalculationDelayMs), _calculationDelayMs);
            Save(nameof(CustomFanCurveSettings.TemperatureDeltaThreshold), _temperatureDeltaThreshold);
            Save(nameof(CustomFanCurveSettings.IgnoreZeroTemperature), _ignoreZeroTemperature);
            Save(nameof(CustomFanCurveSettings.AlwaysWriteRpm), _alwaysWriteRpm);
            Save(nameof(CustomFanCurveSettings.ForceWriteWhenRpmZero), _forceWriteWhenRpmZero);
            Save(nameof(CustomFanCurveSettings.MinimumRpmChangeToApply), _minimumRpmChangeToApply);
            Save(nameof(CustomFanCurveSettings.SpinUpBoostEnabled), _spinUpBoostEnabled);
            Save(nameof(CustomFanCurveSettings.SpinUpBoostRpm), _spinUpBoostRpm);
            Save(nameof(CustomFanCurveSettings.SpinUpBoostDurationMs), _spinUpBoostDurationMs);
            Save(nameof(CustomFanCurveSettings.ForceRefreshOnModeSwitch), _forceRefreshOnModeSwitch);
            Save(nameof(CustomFanCurveSettings.ModeSwitchRefreshCount), _modeSwitchRefreshCount);
            Save(nameof(CustomFanCurveSettings.ModeSwitchRefreshDelayMs), _modeSwitchRefreshDelayMs);
            Save(nameof(CustomFanCurveSettings.ForceRefreshOnEnable), _forceRefreshOnEnable);
            Save(nameof(CustomFanCurveSettings.ClearCachedStateWhenLeavingCustomMode), _clearCachedStateWhenLeavingCustomMode);
            Save(nameof(CustomFanCurveSettings.UiUpdateIntervalMs), _uiUpdateIntervalMs);
            Save(nameof(CustomFanCurveSettings.UseCachedSnapshotForForcedRefresh), _useCachedSnapshotForForcedRefresh);
            Save(nameof(CustomFanCurveSettings.EnableMaxFanWriteEachCycle), _enableMaxFanWriteEachCycle);
            
            Save(nameof(CustomFanCurveSettings.EmaAlpha), _emaAlpha);
            Save(nameof(CustomFanCurveSettings.StepDownRateRpmPerSec), _stepDownRateRpmPerSec);
            Save(nameof(CustomFanCurveSettings.StepDownSpamProtectionDelta), _stepDownSpamProtectionDelta);
            Save(nameof(CustomFanCurveSettings.UiDebounceDelayMs), _uiDebounceDelayMs);
            Save(nameof(CustomFanCurveSettings.SafeMinTemp), _safeMinTemp);
            Save(nameof(CustomFanCurveSettings.SafeMaxTemp), _safeMaxTemp);
            Save(nameof(CustomFanCurveSettings.SafeMaxPercentAtMaxTemp), _safeMaxPercentAtMaxTemp);

            Save(nameof(CustomFanCurveSettings.EnableAcousticOffset), _enableAcousticOffset);
            Save(nameof(CustomFanCurveSettings.AcousticOffsetDeltaRpm), _acousticOffsetDeltaRpm);
            Save(nameof(CustomFanCurveSettings.AcousticOffsetAddRpm), _acousticOffsetAddRpm);
            Save(nameof(CustomFanCurveSettings.HysteresisDeadzoneTemp), _hysteresisDeadzoneTemp);
            Save(nameof(CustomFanCurveSettings.DerivativeSpikeThreshold), _derivativeSpikeThreshold);
            Save(nameof(CustomFanCurveSettings.DerivativeLookaheadSeconds), _derivativeLookaheadSeconds);
        }

        private void Save<T>(string name, T value) => _configManager.UpdateSetting(name, value);

        private bool _isCustomFanEnabled;
        public bool IsCustomFanEnabled
        {
            get => _isCustomFanEnabled;
            set
            {
                if (_isCustomFanEnabled != value)
                {
                    _isCustomFanEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _applyToAllPowerModes;
        public bool ApplyToAllPowerModes
        {
            get => _applyToAllPowerModes;
            set
            {
                if (_applyToAllPowerModes != value)
                {
                    _applyToAllPowerModes = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _debugMode;
        public bool DebugMode
        {
            get => _debugMode;
            set
            {
                if (_debugMode != value)
                {
                    _debugMode = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _sensorIntervalMs;
        public int SensorIntervalMs
        {
            get => _sensorIntervalMs;
            set
            {
                if (_sensorIntervalMs != value)
                {
                    _sensorIntervalMs = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _calculationDelayMs;
        public int CalculationDelayMs
        {
            get => _calculationDelayMs;
            set
            {
                if (_calculationDelayMs != value)
                {
                    _calculationDelayMs = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _temperatureDeltaThreshold;
        public double TemperatureDeltaThreshold
        {
            get => _temperatureDeltaThreshold;
            set
            {
                if (Math.Abs(_temperatureDeltaThreshold - value) > 0.001)
                {
                    _temperatureDeltaThreshold = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _ignoreZeroTemperature;
        public bool IgnoreZeroTemperature
        {
            get => _ignoreZeroTemperature;
            set
            {
                if (_ignoreZeroTemperature != value)
                {
                    _ignoreZeroTemperature = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _alwaysWriteRpm;
        public bool AlwaysWriteRpm
        {
            get => _alwaysWriteRpm;
            set
            {
                if (_alwaysWriteRpm != value)
                {
                    _alwaysWriteRpm = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _forceWriteWhenRpmZero;
        public bool ForceWriteWhenRpmZero
        {
            get => _forceWriteWhenRpmZero;
            set
            {
                if (_forceWriteWhenRpmZero != value)
                {
                    _forceWriteWhenRpmZero = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _minimumRpmChangeToApply;
        public int MinimumRpmChangeToApply
        {
            get => _minimumRpmChangeToApply;
            set
            {
                if (_minimumRpmChangeToApply != value)
                {
                    _minimumRpmChangeToApply = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _spinUpBoostEnabled;
        public bool SpinUpBoostEnabled
        {
            get => _spinUpBoostEnabled;
            set
            {
                if (_spinUpBoostEnabled != value)
                {
                    _spinUpBoostEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _spinUpBoostRpm;
        public int SpinUpBoostRpm
        {
            get => _spinUpBoostRpm;
            set
            {
                if (_spinUpBoostRpm != value)
                {
                    _spinUpBoostRpm = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _spinUpBoostDurationMs;
        public int SpinUpBoostDurationMs
        {
            get => _spinUpBoostDurationMs;
            set
            {
                if (_spinUpBoostDurationMs != value)
                {
                    _spinUpBoostDurationMs = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _forceRefreshOnModeSwitch;
        public bool ForceRefreshOnModeSwitch
        {
            get => _forceRefreshOnModeSwitch;
            set
            {
                if (_forceRefreshOnModeSwitch != value)
                {
                    _forceRefreshOnModeSwitch = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _modeSwitchRefreshCount;
        public int ModeSwitchRefreshCount
        {
            get => _modeSwitchRefreshCount;
            set
            {
                if (_modeSwitchRefreshCount != value)
                {
                    _modeSwitchRefreshCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _modeSwitchRefreshDelayMs;
        public int ModeSwitchRefreshDelayMs
        {
            get => _modeSwitchRefreshDelayMs;
            set
            {
                if (_modeSwitchRefreshDelayMs != value)
                {
                    _modeSwitchRefreshDelayMs = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _forceRefreshOnEnable;
        public bool ForceRefreshOnEnable
        {
            get => _forceRefreshOnEnable;
            set
            {
                if (_forceRefreshOnEnable != value)
                {
                    _forceRefreshOnEnable = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _clearCachedStateWhenLeavingCustomMode;
        public bool ClearCachedStateWhenLeavingCustomMode
        {
            get => _clearCachedStateWhenLeavingCustomMode;
            set
            {
                if (_clearCachedStateWhenLeavingCustomMode != value)
                {
                    _clearCachedStateWhenLeavingCustomMode = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _uiUpdateIntervalMs;
        public int UiUpdateIntervalMs
        {
            get => _uiUpdateIntervalMs;
            set
            {
                if (_uiUpdateIntervalMs != value)
                {
                    _uiUpdateIntervalMs = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _useCachedSnapshotForForcedRefresh;
        public bool UseCachedSnapshotForForcedRefresh
        {
            get => _useCachedSnapshotForForcedRefresh;
            set
            {
                if (_useCachedSnapshotForForcedRefresh != value)
                {
                    _useCachedSnapshotForForcedRefresh = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _enableMaxFanWriteEachCycle;
        public bool EnableMaxFanWriteEachCycle
        {
            get => _enableMaxFanWriteEachCycle;
            set
            {
                if (_enableMaxFanWriteEachCycle != value)
                {
                    _enableMaxFanWriteEachCycle = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _emaAlpha;
        public double EmaAlpha
        {
            get => _emaAlpha;
            set { if (_emaAlpha != value) { _emaAlpha = value; OnPropertyChanged(); } }
        }

        private int _stepDownRateRpmPerSec;
        public int StepDownRateRpmPerSec
        {
            get => _stepDownRateRpmPerSec;
            set { if (_stepDownRateRpmPerSec != value) { _stepDownRateRpmPerSec = value; OnPropertyChanged(); } }
        }

        private int _stepDownSpamProtectionDelta;
        public int StepDownSpamProtectionDelta
        {
            get => _stepDownSpamProtectionDelta;
            set { if (_stepDownSpamProtectionDelta != value) { _stepDownSpamProtectionDelta = value; OnPropertyChanged(); } }
        }

        private int _uiDebounceDelayMs;
        public int UiDebounceDelayMs
        {
            get => _uiDebounceDelayMs;
            set { if (_uiDebounceDelayMs != value) { _uiDebounceDelayMs = value; OnPropertyChanged(); } }
        }

        private int _safeMinTemp;
        public int SafeMinTemp
        {
            get => _safeMinTemp;
            set { if (_safeMinTemp != value) { _safeMinTemp = value; OnPropertyChanged(); } }
        }

        private int _safeMaxTemp;
        public int SafeMaxTemp
        {
            get => _safeMaxTemp;
            set { if (_safeMaxTemp != value) { _safeMaxTemp = value; OnPropertyChanged(); } }
        }

        private int _safeMaxPercentAtMaxTemp;
        public int SafeMaxPercentAtMaxTemp
        {
            get => _safeMaxPercentAtMaxTemp;
            set { if (_safeMaxPercentAtMaxTemp != value) { _safeMaxPercentAtMaxTemp = value; OnPropertyChanged(); } }
        }

        private bool _enableAcousticOffset;
        public bool EnableAcousticOffset
        {
            get => _enableAcousticOffset;
            set { if (_enableAcousticOffset != value) { _enableAcousticOffset = value; OnPropertyChanged(); } }
        }

        private int _acousticOffsetDeltaRpm;
        public int AcousticOffsetDeltaRpm
        {
            get => _acousticOffsetDeltaRpm;
            set { var v = Math.Max(0, value); if (_acousticOffsetDeltaRpm != v) { _acousticOffsetDeltaRpm = v; OnPropertyChanged(); } }
        }

        private int _acousticOffsetAddRpm;
        public int AcousticOffsetAddRpm
        {
            get => _acousticOffsetAddRpm;
            set { var v = Math.Max(0, value); if (_acousticOffsetAddRpm != v) { _acousticOffsetAddRpm = v; OnPropertyChanged(); } }
        }

        private int _hysteresisDeadzoneTemp;
        public int HysteresisDeadzoneTemp
        {
            get => _hysteresisDeadzoneTemp;
            set { var v = Math.Max(0, value); if (_hysteresisDeadzoneTemp != v) { _hysteresisDeadzoneTemp = v; OnPropertyChanged(); } }
        }

        private int _derivativeSpikeThreshold;
        public int DerivativeSpikeThreshold
        {
            get => _derivativeSpikeThreshold;
            set { var v = Math.Max(0, value); if (_derivativeSpikeThreshold != v) { _derivativeSpikeThreshold = v; OnPropertyChanged(); } }
        }

        private int _derivativeLookaheadSeconds;
        public int DerivativeLookaheadSeconds
        {
            get => _derivativeLookaheadSeconds;
            set { var v = Math.Max(0, value); if (_derivativeLookaheadSeconds != v) { _derivativeLookaheadSeconds = v; OnPropertyChanged(); } }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
