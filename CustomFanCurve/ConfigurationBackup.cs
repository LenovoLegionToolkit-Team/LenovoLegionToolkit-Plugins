using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve;

public static class ConfigurationBackup
{
    public static CustomFanCurveSettings Clone(CustomFanCurveSettings settings) =>
        JsonConvert.DeserializeObject<CustomFanCurveSettings>(JsonConvert.SerializeObject(settings))!;

    public static CustomFanCurveEntry CopyEntry(CustomFanCurveEntry source, int targetFanId) => new()
    {
        FanId = targetFanId,
        SensorSource = source.SensorSource,
        CurveNodes = new(source.CurveNodes.Select(n => new CurveNode
        {
            Temperature = n.Temperature,
            TargetPercent = n.TargetPercent
        }))
    };

    public static string Export(CustomFanCurveSettings settings) => JsonConvert.SerializeObject(new
    {
        Format = "CustomFanCurve",
        Version = 1,
        CreatedUtc = DateTime.UtcNow,
        Settings = settings
    }, Formatting.Indented);

    public static CustomFanCurveSettings Import(string json)
    {
        var document = JObject.Parse(json);
        JObject data;
        if (document.ContainsKey("Format"))
        {
            if ((string?)document["Format"] != "CustomFanCurve" || (int?)document["Version"] != 1)
                throw new InvalidDataException("Unsupported Custom Fan Curve backup format or version.");
            data = document["Settings"] as JObject
                ?? throw new InvalidDataException("The backup has no settings.");
        }
        else
        {
            // Also accept the plugin's original settings.json files.
            data = document;
        }

        if (data["Entries"] is not JArray)
            throw new InvalidDataException("The file does not contain fan curves.");

        // Validate raw values before setters can clamp them or substitute defaults.
        foreach (var entry in (JArray)data["Entries"]!)
        {
            if (entry["CurveNodes"] is not JArray nodes || nodes.Count < 2 || nodes.Count > 200)
                throw new InvalidDataException("Each fan curve must contain between 2 and 200 points.");
            foreach (var node in nodes)
            {
                var temperature = (double?)node["Temperature"];
                var percent = (double?)node["TargetPercent"];
                if (temperature is null || !double.IsFinite(temperature.Value) || temperature < 0 || temperature > 100
                    || percent is null || !double.IsFinite(percent.Value) || percent < 0 || percent > 100 || percent != Math.Truncate(percent.Value))
                    throw new InvalidDataException("Curve points must use temperatures and percentages between 0 and 100.");
            }
        }

        var settings = data.ToObject<CustomFanCurveSettings>()
            ?? throw new InvalidDataException("The backup has no settings.");
        Validate(settings);
        return settings;
    }

    public static void Validate(CustomFanCurveSettings settings)
    {
        if (settings.Entries == null || settings.FanMaxRpms == null
            || settings.Entries.Any(e => e == null || e.FanId <= 0 || !Enum.IsDefined(e.SensorSource)
                || e.CurveNodes == null || e.CurveNodes.Count < 2 || e.CurveNodes.Count > 200
                || e.CurveNodes.Any(n => n == null || !float.IsFinite(n.Temperature) || n.Temperature < 0 || n.Temperature > 100))
            || settings.Entries.Select(e => e.FanId).Distinct().Count() != settings.Entries.Count)
            throw new InvalidDataException("The configuration contains invalid or duplicate fan curves.");

        foreach (var entry in settings.Entries)
        {
            var nodes = entry.CurveNodes.OrderBy(n => n.Temperature).ToArray();
            for (var i = 1; i < nodes.Length; i++)
                if (nodes[i].Temperature <= nodes[i - 1].Temperature || nodes[i].TargetPercent < nodes[i - 1].TargetPercent)
                    throw new InvalidDataException("Curve temperatures must be unique and fan speeds must not decrease as temperature rises.");
        }

        Range(settings.SensorIntervalMs, 50, 60000);
        Range(settings.CalculationDelayMs, 0, 60000);
        Range(settings.UiUpdateIntervalMs, 50, 60000);
        Range(settings.UiDebounceDelayMs, 0, 60000);
        Range(settings.TemperatureDeltaThreshold, 0, 100);
        Range(settings.PowerDeltaThreshold, 0, 10000);
        Range(settings.MinimumRpmChangeToApply, 0, 30000);
        Range(settings.SpinUpBoostRpm, 0, 30000);
        Range(settings.SpinUpBoostDurationMs, 0, 60000);
        Range(settings.ModeSwitchRefreshCount, 0, 100);
        Range(settings.ModeSwitchRefreshDelayMs, 0, 60000);
        Range(settings.CriticalTemp, 1, 110);
        Range(settings.EmaAlpha, 0, 1);
        Range(settings.StepDownRateRpmPerSec, 1, 30000);
        Range(settings.StepDownSpamProtectionDelta, 0, 30000);
        Range(settings.SafeMinTemp, 0, 100);
        Range(settings.SafeMaxTemp, 1, 110);
        Range(settings.SafeMaxPercentAtMaxTemp, 0, 100);
        Range(settings.AcousticOffsetDeltaRpm, 0, 30000);
        Range(settings.AcousticOffsetAddRpm, 0, 30000);
        Range(settings.HysteresisDeadzoneTemp, 0, 100);
        Range(settings.DerivativeSpikeThreshold, 0, 100);
        Range(settings.DerivativeLookaheadSeconds, 0, 60);
        if (settings.SafeMaxTemp <= settings.SafeMinTemp
            || settings.FanMaxRpms.Any(p => p.Key <= 0 || p.Value <= 0 || p.Value > 30000))
            throw new InvalidDataException("Invalid thermal safety limits or fan calibration.");
    }

    private static void Range(double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"A configuration value is outside the supported range ({min}–{max}).");
    }
}
