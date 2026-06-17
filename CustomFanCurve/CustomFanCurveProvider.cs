using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Plugin.CustomFanCurve.Resources;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    internal static class Logger
    {
        private static CustomFanCurveConfigManager? _configManager;

        public static void Init(CustomFanCurveConfigManager cm)
        {
            _configManager = cm;
        }

        public static void Debug(string message, [CallerMemberName] string? caller = null)
        {
            if (_configManager?.Settings.DebugMode == true)
            {
                Log.Instance.Trace($"[CustomFanCurve.{caller}] {message}");
            }
        }
    }

    internal static class Runtime
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<int, FanMonitoringSnapshot> _data = new();

        public static event EventHandler? MonitoringUpdated;
        public static IExtensionProvider? Provider { get; private set; }

        public static IReadOnlyDictionary<int, FanMonitoringSnapshot> Monitoring
        {
            get
            {
                lock (_lock) { return new Dictionary<int, FanMonitoringSnapshot>(_data); }
            }
        }

        public static void Attach(IExtensionProvider provider)
        {
            Provider = provider;
        }

        public static void Detach(IExtensionProvider provider)
        {
            if (ReferenceEquals(Provider, provider))
                Provider = null;

            lock (_lock) { _data.Clear(); }

            MonitoringUpdated?.Invoke(null, EventArgs.Empty);
        }

        public static void Update(int fanId, float temp, int rpm, int targetRpm)
        {
            lock (_lock) { _data[fanId] = new FanMonitoringSnapshot(temp, rpm, targetRpm); }
            MonitoringUpdated?.Invoke(null, EventArgs.Empty);
        }
    }

    public class CustomFanCurveProvider : IExtensionProvider, IDisposable
    {
        private readonly CustomFanHardware _hardware;

        public static CustomFanCurveConfigManager? InstanceConfigManager { get; private set; }
        public CustomFanCurveConfigManager ConfigManager { get; private set; }
        internal CustomFanCurveService ControlService { get; private set; }
        public ICustomFanMonitoringService Monitoring { get; private set; }
        public IReadOnlyList<int> AvailableFanIds => _hardware.AvailableFanIds;

        private PluginSettings _pluginSettings;
        private SensorProvider _sensorProvider;

        public CustomFanCurveProvider()
        {
            _hardware = new CustomFanHardware();
        }

        public void Initialize(IExtensionContext context)
        {
            var storagePath = context.GetPluginStoragePath("CustomFanCurve");
            _pluginSettings = new PluginSettings(storagePath);
            ConfigManager = new CustomFanCurveConfigManager(_pluginSettings);
            InstanceConfigManager = ConfigManager;
            Logger.Init(ConfigManager);

            _sensorProvider = new SensorProvider(IoCContainer.Resolve<SensorsGroupController>());
            Monitoring = new CustomFanMonitoringService();

            ControlService = new CustomFanCurveService(ConfigManager, _hardware, _sensorProvider, Monitoring);

            context.Navigation.Register(new ExtensionNavigationItem
            {
                Id = "custom-fan-curve-wmi",
                TitleGetter = () => 
                {
                    var rc = LenovoLegionToolkit.Lib.Resources.Resource.Culture ?? System.Threading.Thread.CurrentThread.CurrentUICulture;
                    LenovoLegionToolkit.Plugin.CustomFanCurve.Resources.Resource.Culture = rc;
                    return LenovoLegionToolkit.Plugin.CustomFanCurve.Resources.Resource.WindowTitle;
                },
                Icon = ExtensionIcon.Gauge,
                PageTag = "customFanCurveWmi",
                PageType = typeof(CustomFanCurvePage)
            });

            Runtime.Attach(this);
            _ = ControlService.InitializeAsync();
        }

        public Task ExecuteAsync(string action, params object[] args) => Task.CompletedTask;
        public object? GetData(string key) => key switch
        {
            nameof(ExtensionDataKey.Capability) => "FanControl",
            nameof(ExtensionDataKey.Version) => "1.0.0",
            _ => null
        };

        public void SetData(string key, object? value) { }

        public void Dispose()
        {
            ControlService?.Dispose();
            Runtime.Detach(this);
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
            await ValueTask.CompletedTask;
        }
    }
}
