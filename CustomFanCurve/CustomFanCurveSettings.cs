using System.Collections.Generic;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public class CustomFanCurveSettings
    {
        public bool IsCustomFanEnabled { get; set; }
        public bool IsFullSpeed { get; set; }
        public bool ApplyToAllPowerModes { get; set; }
        public bool DebugMode { get; set; }

        public int SensorIntervalMs { get; set; } = 500;
        public int CalculationDelayMs { get; set; } = 1000;
        public double TemperatureDeltaThreshold { get; set; } = 0.5;
        public bool EnableTemperatureDeltaThreshold { get; set; } = true;
        public double PowerDeltaThreshold { get; set; } = 5.0;
        public bool EnablePowerDeltaThreshold { get; set; } = true;
        public bool IgnoreZeroTemperature { get; set; } = true;

        public Dictionary<int, int> FanMaxRpms { get; set; } = new();
        public bool FallbackProbeDone { get; set; }
        public bool AlwaysWriteRpm { get; set; } = true;
        public bool ForceWriteWhenRpmZero { get; set; } = true;
        public bool WriteRpmInHundreds { get; set; } = false;
        public int MinimumRpmChangeToApply { get; set; } = 50;
        public bool EnableMinimumRpmChangeToApply { get; set; } = true;

        public bool SpinUpBoostEnabled { get; set; }
        public int SpinUpBoostRpm { get; set; } = 3000;
        public int SpinUpBoostDurationMs { get; set; } = 300;

        public bool ForceRefreshOnModeSwitch { get; set; } = true;
        public int ModeSwitchRefreshCount { get; set; } = 2;
        public int ModeSwitchRefreshDelayMs { get; set; } = 250;
        public bool ForceRefreshOnEnable { get; set; } = true;
        public bool ClearCachedStateWhenLeavingCustomMode { get; set; } = true;

        public int UiUpdateIntervalMs { get; set; } = 1000;
        public bool UseCachedSnapshotForForcedRefresh { get; set; } = true;
        public bool EnableMaxFanWriteEachCycle { get; set; } = false;
        public int CriticalTemp { get; set; } = 95;

        public double EmaAlpha { get; set; } = 0.2;
        public int StepDownRateRpmPerSec { get; set; } = 400;
        public int StepDownSpamProtectionDelta { get; set; } = 400;
        public int UiDebounceDelayMs { get; set; } = 300;
        public int SafeMinTemp { get; set; } = 75;
        public int SafeMaxTemp { get; set; } = 90;
        public int SafeMaxPercentAtMaxTemp { get; set; } = 80;

        public bool EnableAcousticOffset { get; set; } = false;
        public int AcousticOffsetDeltaRpm { get; set; } = 100;
        public int AcousticOffsetAddRpm { get; set; } = 150;
        public int HysteresisDeadzoneTemp { get; set; } = 3;
        public int DerivativeSpikeThreshold { get; set; } = 5;
        public int DerivativeLookaheadSeconds { get; set; } = 2;

        public bool IsSmartAutoEnabled { get; set; } = false;
        public double SmartSpikeIgnoreSeconds { get; set; } = 5.0;
        public int SmartRampUpRpmPerSec { get; set; } = 300;
        public double SmartDecreaseHoldSeconds { get; set; } = 3.0;
        public int SmartRampDownRpmPerSec { get; set; } = 200;
        public int SmartUrgencyStartTemp { get; set; } = 70;
        public int SmartUrgencyFullTemp { get; set; } = 85;
        public int SmartUrgentRampUpRpmPerSec { get; set; } = 1000;
        public double SmartSustainedWindowSeconds { get; set; } = 120.0;
        public double SmartSustainedHoldSeconds { get; set; } = 15.0;
        public int SmartSustainedRampDownRpmPerSec { get; set; } = 50;
        public int SmartSustainedRampUpRpmPerSec { get; set; } = 400;
        public bool SyncFanLevel { get; set; } = false;
        public bool EnablePredictiveEngine { get; set; } = false;
        public bool EnableHysteresis { get; set; } = false;
        public bool EnableStepDownGlide { get; set; } = false;
        public bool EnableThermalSafetyNet { get; set; } = true;
        public bool EnableEma { get; set; } = false;

        public List<CustomFanCurveEntry> Entries { get; set; } = new();
    }
}
