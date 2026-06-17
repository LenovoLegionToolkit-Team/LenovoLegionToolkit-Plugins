using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    internal class SensorProvider
    {
        private readonly SensorsGroupController _controller;
        private readonly ISensorsController? _sensorsController;
        private bool _started;

        public event Action<HardwareSensorSnapshot>? SensorUpdated;
        public bool IsRunning
        {
            get
            {
                return _started;
            }
        }

        public HardwareSensorSnapshot? LatestSnapshot
        {
            get
            {
                return _controller.Snapshot;
            }
        }

        public SensorProvider(SensorsGroupController controller, ISensorsController? sensorsController = null)
        {
            _controller = controller;
            _sensorsController = sensorsController;
            _controller.SensorsUpdated += s => SensorUpdated?.Invoke(s);
        }

        public void Start(int intervalMs)
        {
            if (_started)
            {
                return;
            }

            _controller.Start(this, TimeSpan.FromMilliseconds(intervalMs));
            _started = true;
        }

        public void Stop()
        {
            if (!_started)
            {
                return;
            }

            _controller.Stop(this);
            _started = false;
        }

        public async Task<Dictionary<int, int>> GetFanSpeedsAsync(IReadOnlyList<int> fanIds)
        {
            var result = new Dictionary<int, int>();
            if (_sensorsController == null)
            {
                return result;
            }

            try
            {
                var table = await _sensorsController.GetFanSpeedsAsync().ConfigureAwait(false);
                foreach (var fanId in fanIds)
                {
                    result[fanId] = fanId switch
                    {
                        2 => table.GpuFanSpeed,
                        4 => table.PchFanSpeed,
                        _ => table.CpuFanSpeed,
                    };
                }
            }
            catch { /* Ignore */ }

            return result;
        }
    }
}
