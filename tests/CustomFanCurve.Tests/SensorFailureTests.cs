using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Plugin.CustomFanCurve;

internal static class SensorFailureTests
{
    public static bool Run(CustomFanCurveConfigManager config)
    {
        var assembly = typeof(ConfigurationBackup).Assembly;
        const string prefix = "LenovoLegionToolkit.Plugin.CustomFanCurve.";
        var serviceType = assembly.GetType(prefix + "CustomFanCurveService", throwOnError: true)!;
        var sensorType = assembly.GetType(prefix + "SensorProvider", throwOnError: true)!;
        var loggerType = assembly.GetType(prefix + "Logger", throwOnError: true)!;
        var logger = new TestLogger();
        loggerType.GetMethod("Init")!.Invoke(null, new object[] { config, logger });

        // Exercise the real async-void boundary without constructing sensor controllers or touching WMI.
        var service = RuntimeHelpers.GetUninitializedObject(serviceType);
        var hardware = new FailingHardware();
        void Set(string name, object value) => serviceType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, value);
        Set("_configManager", config);
        Set("_hardware", hardware);
        Set("_sensorProvider", RuntimeHelpers.GetUninitializedObject(sensorType));
        Set("_isEnabled", true);
        config.Settings.IgnoreZeroTemperature = false;
        config.Settings.IsCustomFanEnabled = true;
        config.Settings.IsFullSpeed = true;

        serviceType.GetMethod("OnSensorUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, new object[] { new HardwareSensorSnapshot() });
        var processing = serviceType.GetField("_isProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (!SpinWait.SpinUntil(() => (int)processing.GetValue(service)! == 0, TimeSpan.FromSeconds(5)))
            throw new Exception("Sensor recovery did not complete.");
        return hardware.Restored && logger.SawMissingMethod && !config.Settings.IsCustomFanEnabled && !config.Settings.IsFullSpeed;
    }

    private sealed class TestLogger : IExtensionLogger
    {
        public bool SawMissingMethod { get; private set; }
        public void Trace(string message) { }
        public void Error(string message, Exception exception) => SawMissingMethod |= exception is MissingMethodException;
    }

    private sealed class FailingHardware : ICustomFanHardware
    {
        public bool Restored { get; private set; }
        public bool IsSupported => true;
        public IReadOnlyList<int> AvailableFanIds => throw new MissingMethodException("Simulated incompatible sensor API");
        public Dictionary<int, int> MaxRpms => new();
        public Task InitializeAsync(Dictionary<int, int> savedMaxRpms) => Task.CompletedTask;
        public int GetMaxRpm(int fanId) => 5000;
        public int GetMinRpm(int fanId) => 1000;
        public Task SetFanRpmAsync(int fanId, int rpm) => throw new Exception("Tests must not write fan RPMs.");
        public Task<int> GetFanRpmAsync(int fanId) => Task.FromResult(0);
        public Task RestoreAutoAsync() { Restored = true; return Task.CompletedTask; }
    }
}
