using System;
using System.Linq;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    internal static class CustomFanCurveCalculator
    {
        public static int? Calculate(CustomFanCurveEntry entry, float temperature, int maxRpm)
        {
            var nodes = entry.CurveNodes;
            if (nodes.Count == 0)
            {
                return null;
            }

            var sorted = nodes.OrderBy(n => n.Temperature).ToList();

            double targetPercent;
            if (temperature <= sorted[0].Temperature)
            {
                targetPercent = sorted[0].TargetPercent;
            }
            else if (temperature >= sorted[^1].Temperature)
            {
                targetPercent = sorted[^1].TargetPercent;
            }
            else
            {
                for (var i = 0; i < sorted.Count - 1; i++)
                {
                    if (temperature >= sorted[i].Temperature && temperature <= sorted[i + 1].Temperature)
                    {
                        var ratio = (temperature - sorted[i].Temperature) / (sorted[i + 1].Temperature - sorted[i].Temperature);
                        targetPercent = sorted[i].TargetPercent + ratio * (sorted[i + 1].TargetPercent - sorted[i].TargetPercent);
                        return (int)Math.Round(targetPercent / 100.0 * maxRpm);
                    }
                }

                targetPercent = sorted[^1].TargetPercent;
            }

            return (int)Math.Round(targetPercent / 100.0 * maxRpm);
        }

        public static int GetSafeMinPercent(float temperature)
        {
            var settings = CustomFanCurveProvider.InstanceConfigManager?.Settings;
            if (settings == null)
            {
                if (temperature <= 75) return 0;
                if (temperature >= 90) return 50;
                var r = (temperature - 75) / 15.0;
                return (int)Math.Round(r * 50);
            }

            if (temperature <= settings.SafeMinTemp) return 0;
            if (temperature >= settings.SafeMaxTemp) return settings.SafeMaxPercentAtMaxTemp;
            
            float tempRange = settings.SafeMaxTemp - settings.SafeMinTemp;
            if (tempRange <= 0) return settings.SafeMaxPercentAtMaxTemp;
            
            var ratio = (temperature - settings.SafeMinTemp) / tempRange;
            return (int)Math.Round(ratio * settings.SafeMaxPercentAtMaxTemp);
        }
    }
}
