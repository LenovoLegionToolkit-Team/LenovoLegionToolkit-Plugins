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
        public int CalculationDelayMs { get; set; } = 500;
        public double TemperatureDeltaThreshold { get; set; } = 0.5;
        public bool IgnoreZeroTemperature { get; set; }

        public Dictionary<int, int> FanMaxRpms { get; set; } = new();
        public bool FallbackProbeDone { get; set; }
        public bool AlwaysWriteRpm { get; set; } = true;
        public bool ForceWriteWhenRpmZero { get; set; } = true;
        public int MinimumRpmChangeToApply { get; set; } = 50;

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
        public bool EnableMaxFanWriteEachCycle { get; set; } = true;
        public int CriticalTemp { get; set; } = 120;

        public List<CustomFanCurveEntry> Entries { get; set; } = new();
    }
}
