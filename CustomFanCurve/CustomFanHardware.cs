using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public interface ICustomFanHardware
    {
        bool IsSupported { get; }
        IReadOnlyList<int> AvailableFanIds { get; }
        Task InitializeAsync();
        int GetMaxRpm(int fanId);
        int GetMinRpm(int fanId);
        Task SetFanRpmAsync(int fanId, int rpm);
        Task<int> GetFanRpmAsync(int fanId);
    }

    internal class CustomFanHardware : ICustomFanHardware
    {
        private readonly Dictionary<int, int> _maxRpms = new();
        private readonly Dictionary<int, int> _minRpms = new();
        private readonly Dictionary<int, CapabilityID> _capabilityIds = new();
        private readonly List<int> _fanIds = new();

        public bool IsSupported { get; private set; }
        public IReadOnlyList<int> AvailableFanIds => _fanIds;

        public async Task InitializeAsync()
        {
            var fanTestDataWorks = false;

            for (var fanId = 0; fanId <= 5; fanId++)
            {
                try
                {
                    var maxRpm = await WMI.LenovoFanTestData.GetFanMaxSpeedAsync(fanId).ConfigureAwait(false);
                    if (maxRpm > 0)
                    {
                        fanTestDataWorks = true;
                        _fanIds.Add(fanId);
                        _maxRpms[fanId] = maxRpm;

                        var minRpm = await WMI.LenovoFanTestData.GetFanMinSpeedAsync(fanId).ConfigureAwait(false);
                        if (minRpm > 0)
                        {
                            _minRpms[fanId] = minRpm;
                        }
                    }
                }
                catch { }
            }

            if (!fanTestDataWorks)
            {
                await ProbeFansAsync();
            }

            foreach (var fanId in _fanIds)
            {
                _capabilityIds[fanId] = fanId switch
                {
                    2 => CapabilityID.GpuCurrentFanSpeed,
                    4 => CapabilityID.PchCurrentFanSpeed,
                    _ => CapabilityID.CpuCurrentFanSpeed,
                };
            }

            IsSupported = await CheckSupportAsync().ConfigureAwait(false);
        }

        private async Task ProbeFansAsync()
        {
            var tasks = new[] { 1, 2, 4 }.Select(async fanId =>
            {
                var cid = fanId switch
                {
                    2 => CapabilityID.GpuCurrentFanSpeed,
                    4 => CapabilityID.PchCurrentFanSpeed,
                    _ => CapabilityID.CpuCurrentFanSpeed,
                };

                var maxRpm = await ProbeMaxRpmAsync(cid).ConfigureAwait(false);
                await WMI.LenovoOtherMethod.SetFeatureValueAsync(cid, 0).ConfigureAwait(false);
                return (fanId, maxRpm);
            });

            var all = await Task.WhenAll(tasks);
            foreach (var (fanId, maxRpm) in all)
            {
                if (maxRpm > 0)
                {
                    _fanIds.Add(fanId);
                    _maxRpms[fanId] = maxRpm;
                    _capabilityIds[fanId] = fanId switch
                    {
                        2 => CapabilityID.GpuCurrentFanSpeed,
                        4 => CapabilityID.PchCurrentFanSpeed,
                        _ => CapabilityID.CpuCurrentFanSpeed,
                    };
                }
            }
        }

        private static async Task<int> ProbeMaxRpmAsync(CapabilityID cid)
        {
            int maxRpm = 0;

            for (var target = 1000; target <= 10000; target += 1000)
            {
                var actual = await WriteAndWaitStableAsync(cid, target);
                if (actual <= maxRpm + 150)
                {
                    break;
                }

                maxRpm = actual;
            }

            for (var target = maxRpm + 100; target <= maxRpm + 2000; target += 100)
            {
                var actual = await WriteAndWaitStableAsync(cid, target);
                if (actual <= maxRpm + 50)
                {
                    break;
                }

                maxRpm = actual;
            }

            return maxRpm;
        }

        private static async Task<int> WriteAndWaitStableAsync(CapabilityID cid, int targetRpm)
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync(cid, targetRpm).ConfigureAwait(false);

            int lastRead = 0;
            int stableCount = 0;
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                var current = await WMI.LenovoOtherMethod.GetFeatureValueAsync(cid).ConfigureAwait(false);

                if (Math.Abs(current - lastRead) <= 100)
                {
                    stableCount++;
                    if (stableCount >= 3)
                    {
                        return current;
                    }
                }
                else
                {
                    stableCount = 0;
                }

                lastRead = current;
            }

            return lastRead;
        }

        private async Task<bool> CheckSupportAsync()
        {
            if (_maxRpms.Count == 0)
            {
                return false;
            }

            try
            {
                var rpm = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentFanSpeed).ConfigureAwait(false);
                return rpm >= 0;
            }
            catch
            {
                return false;
            }
        }

        public int GetMaxRpm(int fanId)
        {
            return _maxRpms.TryGetValue(fanId, out var r) ? r : 6400;
        }

        public int GetMinRpm(int fanId)
        {
            return _minRpms.TryGetValue(fanId, out var r) ? r : 1200;
        }

        public async Task SetFanRpmAsync(int fanId, int rpm)
        {
            if (!_capabilityIds.TryGetValue(fanId, out var cid))
            {
                return;
            }

            await WMI.LenovoOtherMethod.SetFeatureValueAsync(cid, Math.Max(0, rpm)).ConfigureAwait(false);
        }

        public async Task<int> GetFanRpmAsync(int fanId)
        {
            if (!_capabilityIds.TryGetValue(fanId, out var cid))
            {
                return 0;
            }

            try
            {
                return await WMI.LenovoOtherMethod.GetFeatureValueAsync(cid).ConfigureAwait(false);
            }
            catch
            {
                return 0;
            }
        }
    }
}
