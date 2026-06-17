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
        Dictionary<int, int> MaxRpms { get; }
        Task InitializeAsync(Dictionary<int, int> savedMaxRpms);
        int GetMaxRpm(int fanId);
        int GetMinRpm(int fanId);
        Task SetFanRpmAsync(int fanId, int rpm);
        Task<int> GetFanRpmAsync(int fanId);
        Task RestoreAutoAsync();
    }

    internal class CustomFanHardware : ICustomFanHardware
    {
        private readonly Dictionary<int, int> _maxRpms = new();
        private readonly Dictionary<int, int> _minRpms = new();
        private readonly Dictionary<int, CapabilityID> _capabilityIds = new();
        private readonly List<int> _fanIds = new();

        public bool IsSupported { get; private set; }
        public IReadOnlyList<int> AvailableFanIds => _fanIds;
        public Dictionary<int, int> MaxRpms => new(_maxRpms);

        public async Task InitializeAsync(Dictionary<int, int> savedMaxRpms)
        {
            var fanTestDataWorks = false;
            Logger.Debug("Fan discovery starting...");

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
                        Logger.Debug($"WMI fan test data: fanId={fanId} maxRpm={maxRpm}");

                        var minRpm = await WMI.LenovoFanTestData.GetFanMinSpeedAsync(fanId).ConfigureAwait(false);
                        if (minRpm > 0)
                        {
                            _minRpms[fanId] = minRpm;
                            Logger.Debug($"WMI fan test data: fanId={fanId} minRpm={minRpm}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"WMI fan test data failed for fanId={fanId}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (fanTestDataWorks)
            {
                Logger.Debug($"Fan discovery: WMI fan test data succeeded, found {_maxRpms.Count} fan(s): [{string.Join(", ", _maxRpms.Select(kv => $"{kv.Key}={kv.Value}rpm"))}]");
                foreach (var kv in _maxRpms)
                {
                    savedMaxRpms[kv.Key] = kv.Value;
                }
            }
            else if (savedMaxRpms.Count > 0)
            {
                Logger.Debug($"Fan discovery: WMI fan test data failed, using {savedMaxRpms.Count} saved fallback RPM(s): [{string.Join(", ", savedMaxRpms.Select(kv => $"{kv.Key}={kv.Value}rpm"))}]");
                foreach (var kv in savedMaxRpms)
                {
                    _fanIds.Add(kv.Key);
                    _maxRpms[kv.Key] = kv.Value;
                }
            }
            else
            {
                Logger.Debug("Fan discovery: WMI fan test data failed and no saved RPMs, starting slow probe...");
                await ProbeFansAsync();
                if (_maxRpms.Count > 0)
                {
                    Logger.Debug($"Fan discovery: slow probe found {_maxRpms.Count} fan(s): [{string.Join(", ", _maxRpms.Select(kv => $"{kv.Key}={kv.Value}rpm"))}]");
                }
                else
                {
                    Logger.Debug("Fan discovery: slow probe found NO fans!");
                }
                foreach (var kv in _maxRpms)
                {
                    savedMaxRpms[kv.Key] = kv.Value;
                }
            }

            foreach (var fanId in _fanIds)
            {
                _capabilityIds[fanId] = GetCapabilityForFanId(fanId);
            }

            IsSupported = await CheckSupportAsync().ConfigureAwait(false);
            Logger.Debug($"Fan discovery complete: IsSupported={IsSupported}, fans=[{string.Join(", ", _fanIds.Select(id => $"{id}({_capabilityIds[id]})"))}]");
        }

        private async Task ProbeFansAsync()
        {
            Logger.Debug("Slow probe: scanning fan IDs 1, 2, 4...");
            var tasks = new[] { 1, 2, 4 }.Select(async fanId =>
            {
                var cid = GetCapabilityForFanId(fanId);
                Logger.Debug($"Slow probe: starting probe for fanId={fanId} capability={cid}");

                var maxRpm = await ProbeMaxRpmAsync(cid).ConfigureAwait(false);
                await WMI.LenovoOtherMethod.SetFeatureValueAsync(cid, 0).ConfigureAwait(false);
                Logger.Debug($"Slow probe: fanId={fanId} capability={cid} maxRpm={maxRpm}");
                return (fanId, maxRpm);
            });

            var all = await Task.WhenAll(tasks);
            foreach (var (fanId, maxRpm) in all)
            {
                if (maxRpm > 0)
                {
                    _fanIds.Add(fanId);
                    _maxRpms[fanId] = maxRpm;
                }
            }
        }

        private static CapabilityID GetCapabilityForFanId(int fanId)
        {
            return fanId switch
            {
                2 => CapabilityID.GpuCurrentFanSpeed,
                4 => CapabilityID.PchCurrentFanSpeed,
                _ => CapabilityID.CpuCurrentFanSpeed,
            };
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

            Logger.Debug($"Slow probe coarse scan done: capability={cid} maxRpm={maxRpm}");

            for (var target = maxRpm + 100; target <= maxRpm + 2000; target += 100)
            {
                var actual = await WriteAndWaitStableAsync(cid, target);
                if (actual <= maxRpm + 50)
                {
                    break;
                }

                maxRpm = actual;
            }

            Logger.Debug($"Slow probe fine scan done: capability={cid} finalMaxRpm={maxRpm}");
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
                Logger.Debug("CheckSupport: FAILED — no fans discovered");
                return false;
            }

            try
            {
                var rpm = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.CpuCurrentFanSpeed).ConfigureAwait(false);
                var supported = rpm >= 0;
                Logger.Debug($"CheckSupport: GetFeatureValue(CpuCurrentFanSpeed) returned {rpm}, supported={supported}");
                return supported;
            }
            catch (Exception ex)
            {
                Logger.Debug($"CheckSupport: FAILED — GetFeatureValue(CpuCurrentFanSpeed) threw {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public int GetMaxRpm(int fanId)
        {
            return _maxRpms.TryGetValue(fanId, out var r) ? r : 6400;
        }

        public int GetMinRpm(int fanId)
        {
            return _minRpms.TryGetValue(fanId, out var r) ? r : 1000;
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

        public async Task RestoreAutoAsync()
        {
            foreach (var fanId in _fanIds)
            {
                try
                {
                    var cid = GetCapabilityForFanId(fanId);
                    await WMI.LenovoOtherMethod.SetFeatureValueAsync(cid, 0).ConfigureAwait(false);
                }
                catch { /* Ignore */ }
            }
        }
    }
}
