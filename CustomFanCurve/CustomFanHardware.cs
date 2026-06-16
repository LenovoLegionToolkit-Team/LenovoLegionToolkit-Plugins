using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public interface ICustomFanHardware
    {
        bool IsSupported { get; }
        Task InitializeAsync();
        int GetMaxRpm(FanType fanType);
        int GetMinRpm(FanType fanType);
        Task SetFanRpmAsync(FanType fanType, int rpm);
        Task<int> GetFanRpmAsync(FanType fanType);
    }

    internal class CustomFanHardware : ICustomFanHardware
    {
        private readonly Dictionary<FanType, int> _maxRpms = new();
        private readonly Dictionary<FanType, int> _minRpms = new();

        public bool IsSupported { get; private set; }

        public async Task InitializeAsync()
        {
            foreach (var fanType in new[] { FanType.Cpu, FanType.Gpu, FanType.System })
            {
                var fanId = GetFanId(fanType);
                try
                {
                    var maxRpm = await WMI.LenovoFanTestData.GetFanMaxSpeedAsync(fanId).ConfigureAwait(false);
                    if (maxRpm > 0)
                    {
                        _maxRpms[fanType] = maxRpm;
                    }

                    var minRpm = await WMI.LenovoFanTestData.GetFanMinSpeedAsync(fanId).ConfigureAwait(false);
                    if (minRpm > 0)
                    {
                        _minRpms[fanType] = minRpm;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FanHardware init fail {fanType}: {ex.Message}");
                }
            }

            IsSupported = await CheckSupportAsync().ConfigureAwait(false);
        }

        private async Task<bool> CheckSupportAsync()
        {
            if (_maxRpms.Count == 0 && _minRpms.Count == 0)
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

        public int GetMaxRpm(FanType fanType)
        {
            return _maxRpms.TryGetValue(fanType, out var r) ? r : 6400;
        }

        public int GetMinRpm(FanType fanType)
        {
            return _minRpms.TryGetValue(fanType, out var r) ? r : 1200;
        }

        public async Task SetFanRpmAsync(FanType fanType, int rpm)
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync(GetCapabilityId(fanType), Math.Max(0, rpm)).ConfigureAwait(false);
        }

        public async Task<int> GetFanRpmAsync(FanType fanType)
        {
            try
            {
                return await WMI.LenovoOtherMethod.GetFeatureValueAsync(GetCapabilityId(fanType)).ConfigureAwait(false);
            }
            catch
            {
                return 0;
            }
        }

        private static int GetFanId(FanType fanType)
        {
            return fanType switch
            {
                FanType.Cpu => 0,
                FanType.Gpu => 1,
                FanType.System => 2,
                _ => 0
            };
        }

        private static CapabilityID GetCapabilityId(FanType fanType)
        {
            return fanType switch
            {
                FanType.Cpu => CapabilityID.CpuCurrentFanSpeed,
                FanType.Gpu => CapabilityID.GpuCurrentFanSpeed,
                FanType.System => CapabilityID.PchCurrentFanSpeed,
                _ => CapabilityID.CpuCurrentFanSpeed
            };
        }
    }
}
