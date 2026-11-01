using System;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public static class FuzzyLogicFanController
    {
        private const int OutputSilent = 15;
        private const int OutputMaintain = -1;
        private const int OutputSlightRamp = 40;
        private const int OutputAggressive = 70;
        private const int OutputMax = 100;

        // Machine Power Scaling: Maps a power reading onto 0..1 load between the configured idle and full-load watts.
        private static float GetPowerLoad(float power, int idleW, int fullW)
        {
            if (power < 0) return -1f;
            float range = Math.Max(1, fullW - idleW);
            return Math.Clamp((power - idleW) / range, 0f, 1f);
        }

        private static float GetCpuLoad(HardwareSensorSnapshot snapshot, CustomFanCurveSettings settings) =>
            GetPowerLoad(snapshot[SensorItem.CpuPower], settings.SmartCpuIdlePowerW, settings.SmartCpuFullPowerW);

        private static float GetGpuLoad(HardwareSensorSnapshot snapshot, CustomFanCurveSettings settings) =>
            GetPowerLoad(snapshot[SensorItem.GpuPower], settings.SmartGpuIdlePowerW, settings.SmartGpuFullPowerW);

        public static int CalculateSmartRpm(HardwareSensorSnapshot snapshot, float evalTemp, int maxRpm, int currentTargetRpm, int fanIndex, CustomFanCurveSettings settings)
        {
            if (evalTemp >= 90) return maxRpm;

            float temp = evalTemp;
            float cpuLoad = GetCpuLoad(snapshot, settings);
            float gpuLoad = GetGpuLoad(snapshot, settings);
            float powerLoad = cpuLoad;

            if (fanIndex == 1)
            {
                powerLoad = snapshot[SensorItem.GpuPower] > 0 ? gpuLoad : cpuLoad;
            }
            else if (fanIndex > 1)
            {
                powerLoad = Math.Max(cpuLoad, gpuLoad);
            }

            float targetPercent = 0f;

            float loadFactor;
            if (powerLoad < 0)
            {
                float usage = fanIndex == 1 ? snapshot[SensorItem.GpuUtilization] : (fanIndex == 0 ? snapshot[SensorItem.CpuUtilization] : Math.Max(snapshot[SensorItem.CpuUtilization], snapshot[SensorItem.GpuUtilization]));
                loadFactor = Math.Clamp((usage - 20f) / (70f - 20f), 0f, 1f);
            }
            else
            {
                loadFactor = powerLoad;
            }

            float thermalPercent = Math.Clamp((temp - 50f) / (85f - 50f) * (OutputMax - OutputSlightRamp), 0f, OutputMax - OutputSlightRamp);

            float powerPercent = loadFactor * OutputSlightRamp;

            targetPercent = thermalPercent + powerPercent;

            if (temp >= 55 && targetPercent < OutputSilent)
            {
                targetPercent = OutputSilent;
            }

            if (temp < 45 && targetPercent < OutputSilent)
            {
                targetPercent = 0;
            }

            return (int)(maxRpm * (Math.Clamp(targetPercent, 0f, OutputMax) / OutputMax));
        }

        public static (string ThermalState, string PowerLoad, string Decision) GetGlobalTelemetryStrings(HardwareSensorSnapshot snapshot, CustomFanCurveSettings settings)
        {
            float maxTemp = Math.Max(snapshot[SensorItem.CpuTemperature], snapshot[SensorItem.GpuCoreTemperature]);
            float maxPower = Math.Max(snapshot[SensorItem.CpuPower], snapshot[SensorItem.GpuPower]);

            string thermalState = maxTemp >= 90 ? "Critical" : maxTemp >= 75 ? "Hot" : maxTemp >= 55 ? "Warm" : "Cold";
            string powerLoad;
            string loadString;

            if (maxPower < 0)
            {
                float maxUsage = Math.Max(snapshot[SensorItem.CpuUtilization], snapshot[SensorItem.GpuUtilization]);
                if (maxUsage < 0)
                {
                    powerLoad = maxTemp >= 75 ? "Heavy Load" : maxTemp >= 55 ? "Light Load" : "Idle";
                    loadString = "Unknown";
                }
                else
                {
                    powerLoad = maxUsage >= 70 ? "Heavy Load" : maxUsage >= 30 ? "Light Load" : "Idle";
                    loadString = $"{maxUsage:F0}%";
                }
            }
            else
            {
                // Same scale as CalculateSmartRpm; with the default 20-65 W range these are the original 40 W / 65 W thresholds.
                float load = Math.Max(GetCpuLoad(snapshot, settings), GetGpuLoad(snapshot, settings));
                powerLoad = load >= 1f ? "Heavy Load" : load >= 20f / 45f ? "Light Load" : "Idle";
                loadString = $"{maxPower:F0}W";
            }

            string decision = "Maintaining Silent Profile";
            if (thermalState == "Critical") decision = "Max Cooling Override";
            else if (thermalState == "Hot" && powerLoad == "Heavy Load") decision = "Aggressive Cooling";
            else if (thermalState == "Hot" && powerLoad == "Light Load") decision = "Slight Ramp Up";
            else if (thermalState == "Hot" && powerLoad == "Idle") decision = "Ignoring Harmless Spike";
            else if (thermalState == "Warm" && powerLoad == "Heavy Load") decision = "Slight Ramp Up";
            else if (thermalState == "Warm" && powerLoad == "Light Load") decision = "Maintaining Profile";
            else if (thermalState == "Warm" && powerLoad == "Idle") decision = "Maintaining Silent Profile";
            
            return ($"{thermalState} ({maxTemp:F0}°C)", $"{powerLoad} ({loadString})", decision);
        }
    }
}
