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
        private static readonly Dictionary<FanType, FanMonitoringSnapshot> _data = new();

        public static event EventHandler? MonitoringUpdated;
        public static IExtensionProvider? Provider { get; private set; }

        public static IReadOnlyDictionary<FanType, FanMonitoringSnapshot> Monitoring
        {
            get
            {
                lock (_lock)
                {
                    return new Dictionary<FanType, FanMonitoringSnapshot>(_data);
                }
            }
        }

        public static void Attach(IExtensionProvider provider)
        {
            Provider = provider;
        }

        public static void Detach(IExtensionProvider provider)
        {
            if (ReferenceEquals(Provider, provider))
            {
                Provider = null;
            }

            lock (_lock)
            {
                _data.Clear();
            }

            MonitoringUpdated?.Invoke(null, EventArgs.Empty);
        }

        public static void Update(FanType type, float temp, int rpm, int targetRpm)
        {
            lock (_lock)
            {
                _data[type] = new FanMonitoringSnapshot(temp, rpm, targetRpm);
            }

            MonitoringUpdated?.Invoke(null, EventArgs.Empty);
        }
    }

    public class CustomFanCurveProvider : IExtensionProvider, IDisposable
    {
        public CustomFanCurveConfigManager ConfigManager { get; private set; }
        internal CustomFanCurveService ControlService { get; private set; }
        public ICustomFanMonitoringService Monitoring { get; private set; }

        private PluginSettings _pluginSettings;
        private SensorProvider _sensorProvider;

        public void Initialize(IExtensionContext context)
        {
            var storagePath = context.GetPluginStoragePath("CustomFanCurve");
            _pluginSettings = new PluginSettings(storagePath);
            ConfigManager = new CustomFanCurveConfigManager(_pluginSettings);
            Logger.Init(ConfigManager);

            var hardware = new CustomFanHardware();
            _sensorProvider = new SensorProvider(IoCContainer.Resolve<SensorsGroupController>());
            Monitoring = new CustomFanMonitoringService();

            ControlService = new CustomFanCurveService(ConfigManager, hardware, _sensorProvider, Monitoring);

            var realCulture = LenovoLegionToolkit.Lib.Resources.Resource.Culture ?? System.Threading.Thread.CurrentThread.CurrentUICulture;
            LenovoLegionToolkit.Plugin.CustomFanCurve.Resources.Resource.Culture = realCulture;

            context.Navigation.Register(new ExtensionNavigationItem
            {
                Id = "custom-fan-curve-wmi",
                Title = LenovoLegionToolkit.Plugin.CustomFanCurve.Resources.Resource.WindowTitle,
                Icon = ExtensionIcon.Gauge,
                PageTag = "customFanCurveWmi",
                PageType = typeof(CustomFanCurvePage)
            });
            
            Runtime.Attach(this);
            _ = ControlService.InitializeAsync();
        }

        public Task ExecuteAsync(string action, params object[] args)
        {
            return Task.CompletedTask;
        }

        public object? GetData(string key)
        {
            return null;
        }

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
