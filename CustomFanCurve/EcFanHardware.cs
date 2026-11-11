using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    /// <summary>
    /// Legion 5 Gen 6 AMD (BIOS G9CN, ITE EC 0x8227) exposes neither the WMI fan speed capabilities nor Fan_Set_Table,
    /// so the fan tables are written to the EC RAM with the layout LegionFanControl used on these machines:
    /// one speed on points 3-10, points 0-2 at 0 and CPU thresholds at 0,0,0,1..8 so the EC stays on its last point.
    /// A non-zero point 0 hangs this EC firmware.
    /// </summary>
    internal class EcFanHardware : ICustomFanHardware
    {
        private const string BiosPrefix = "G9CN";
        private const int EcChipId = 0x8227;

        private const ushort PowerModeAddress = 0xC420;
        private const ushort Fan1TableAddress = 0xC540;
        private const ushort Fan2TableAddress = 0xC550;
        private const ushort AccelerationAddress = 0xC560;
        private const ushort DecelerationAddress = 0xC570;
        private const ushort CpuMaxTemperatureAddress = 0xC580;
        private const ushort CpuMinTemperatureAddress = 0xC590;
        private const ushort Fan1RpmAddress = 0xC5E0;
        private const ushort Fan2RpmAddress = 0xC5E2;
        private static readonly ushort[] FanlessProtectionAddresses = { 0xC5C0, 0xC5C1, 0xC5C2, 0xC5D0, 0xC5D1, 0xC5D2, 0xC5D3 };

        private const int TablePoints = 11;
        private const int Acceleration = 1;
        private const int MaxRpm = 4500;
        private const int RpmStep = 100;

        private readonly Dictionary<int, (ushort Table, ushort Rpm)> _fans = new()
        {
            [1] = (Fan1TableAddress, Fan1RpmAddress),
            [2] = (Fan2TableAddress, Fan2RpmAddress),
        };

        private readonly Dictionary<byte, (ushort Address, byte Value)[]> _stockTables = new();
        private readonly object _lock = new();
        private EmbeddedController? _ec;

        public bool IsSupported { get; private set; }
        public IReadOnlyList<int> AvailableFanIds => _fans.Keys.ToList();
        public Dictionary<int, int> MaxRpms => _fans.Keys.ToDictionary(id => id, _ => MaxRpm);

        public static bool IsAvailable()
        {
            var bios = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion", null) as string;
            return bios != null && bios.StartsWith(BiosPrefix, StringComparison.OrdinalIgnoreCase)
                   && PawnIOHelper.GetPawnIOState() == PawnIOState.Installed;
        }

        public Task InitializeAsync(Dictionary<int, int> savedMaxRpms)
        {
            try
            {
                _ec ??= new EmbeddedController();
                var chipId = _ec.ReadChipId();
                IsSupported = chipId == EcChipId;
                Logger.Debug($"EC fan hardware: chipId=0x{chipId:X4} IsSupported={IsSupported}");
            }
            catch (Exception ex)
            {
                IsSupported = false;
                Logger.Error("EC fan hardware initialization failed", ex);
            }

            return Task.CompletedTask;
        }

        public int GetMaxRpm(int fanId) => MaxRpm;

        public int GetMinRpm(int fanId) => 0;

        public int GetRpmStep(int fanId) => RpmStep;

        public Task SetFanRpmAsync(int fanId, int rpm)
        {
            if (!IsSupported || _ec == null || !_fans.TryGetValue(fanId, out var fan))
                return Task.CompletedTask;

            var speed = (byte)Math.Clamp((int)Math.Round(rpm / (double)RpmStep), 0, MaxRpm / RpmStep);

            lock (_lock)
            {
                SaveStockTable();
                Write(BuildTable(fan.Table, speed));
            }

            return Task.CompletedTask;
        }

        public Task<int> GetFanRpmAsync(int fanId)
        {
            if (!IsSupported || _ec == null || !_fans.TryGetValue(fanId, out var fan))
                return Task.FromResult(0);

            lock (_lock)
                return Task.FromResult(_ec.ReadByte(fan.Rpm) | _ec.ReadByte(fan.Rpm + 1) << 8);
        }

        public Task RestoreAutoAsync()
        {
            if (!IsSupported || _ec == null)
                return Task.CompletedTask;

            lock (_lock)
            {
                var mode = _ec.ReadByte(PowerModeAddress);
                if (_stockTables.TryGetValue(mode, out var stock) && IsOwnTable())
                    Write(stock.Reverse());
            }

            return Task.CompletedTask;
        }

        // Same order as LegionFanControl: acceleration, fanless protection, fan table, thresholds
        private static IEnumerable<(ushort Address, byte Value)> BuildTable(ushort fanTableAddress, byte speed)
        {
            for (var i = 0; i < TablePoints; i++)
                yield return ((ushort)(AccelerationAddress + i), Acceleration);
            for (var i = 0; i < TablePoints; i++)
                yield return ((ushort)(DecelerationAddress + i), Acceleration);
            foreach (var address in FanlessProtectionAddresses)
                yield return (address, 0);
            for (var i = 0; i < TablePoints; i++)
                yield return ((ushort)(fanTableAddress + i), i < 3 ? (byte)0 : speed);
            for (var i = 0; i < TablePoints; i++)
                yield return ((ushort)(CpuMaxTemperatureAddress + i), (byte)Math.Max(0, i - 2));
            for (var i = 0; i < TablePoints; i++)
                yield return ((ushort)(CpuMinTemperatureAddress + i), (byte)Math.Max(0, i - 2));
        }

        // The EC reloads its own table on every power mode change: keep one copy per mode to restore it
        private void SaveStockTable()
        {
            var mode = _ec!.ReadByte(PowerModeAddress);
            if (_stockTables.ContainsKey(mode) || IsOwnTable())
                return;

            var addresses = BuildTable(Fan1TableAddress, 0).Select(e => e.Address)
                .Concat(Enumerable.Range(0, TablePoints).Select(i => (ushort)(Fan2TableAddress + i)))
                .Distinct();
            _stockTables[mode] = addresses.Select(a => (a, _ec.ReadByte(a))).ToArray();
            Logger.Debug($"EC fan hardware: stock table saved for power mode {mode}");
        }

        private bool IsOwnTable() => _ec!.ReadByte(CpuMaxTemperatureAddress) == 0;

        private void Write(IEnumerable<(ushort Address, byte Value)> table)
        {
            foreach (var (address, value) in table)
            {
                if (_ec!.ReadByte(address) != value)
                    _ec.WriteByte(address, value);
            }
        }
    }
}
