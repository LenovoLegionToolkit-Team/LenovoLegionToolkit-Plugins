using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using ZenStates.Core;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    /// <summary>
    /// ITE embedded controller RAM access through I2EC, with the signed PawnIO LpcIO module shipped with LibreHardwareMonitor.
    /// </summary>
    internal sealed class EmbeddedController : IDisposable
    {
        private const string LpcIoModuleResource = "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin";
        private const string IsaBusMutexName = @"Global\Access_ISABUS.HTP.Method";

        // SuperIO at 0x4E/0x4F, then the ITE depth-2 index/data registers and the I2EC registers behind them
        private const long SuperIoSlot4E = 1;
        private const long D2IndexRegister = 0x2E;
        private const long D2DataRegister = 0x2F;
        private const long I2EcAddressLow = 0x10;
        private const long I2EcAddressHigh = 0x11;
        private const long I2EcData = 0x12;

        private const ushort ChipIdAddress = 0x2000;

        private readonly PawnIo _pawnIo;
        private readonly Mutex _isaBusMutex = new(false, IsaBusMutexName);

        public EmbeddedController()
        {
            _pawnIo = PawnIo.LoadModuleFromResource(typeof(Computer).Assembly, LpcIoModuleResource);
            _pawnIo.Execute("ioctl_select_slot", new[] { SuperIoSlot4E }, 0);
        }

        public int ReadChipId()
        {
            return ReadByte(ChipIdAddress) << 8 | ReadByte(ChipIdAddress + 1);
        }

        public byte ReadByte(int address)
        {
            byte value = 0;
            Locked(() =>
            {
                SelectAddress(address);
                SuperIoOut(D2IndexRegister, I2EcData);
                value = (byte)_pawnIo.Execute("ioctl_superio_inb", new[] { D2DataRegister }, 1)[0];
            });
            return value;
        }

        public void WriteByte(int address, byte value)
        {
            Locked(() =>
            {
                SelectAddress(address);
                SuperIoOut(D2IndexRegister, I2EcData);
                SuperIoOut(D2DataRegister, value);
            });
        }

        public void Dispose()
        {
            _pawnIo.Close();
            _isaBusMutex.Dispose();
        }

        private void SelectAddress(int address)
        {
            SuperIoOut(D2IndexRegister, I2EcAddressHigh);
            SuperIoOut(D2DataRegister, (address >> 8) & 0xFF);
            SuperIoOut(D2IndexRegister, I2EcAddressLow);
            SuperIoOut(D2DataRegister, address & 0xFF);
        }

        private void SuperIoOut(long register, long value) => _pawnIo.Execute("ioctl_superio_outb", new[] { register, value }, 0);

        private void Locked(Action action)
        {
            try
            {
                if (!_isaBusMutex.WaitOne(TimeSpan.FromSeconds(1)))
                    throw new TimeoutException("ISA bus mutex is busy.");
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited without releasing it, the mutex is now ours
            }

            try
            {
                action();
            }
            finally
            {
                _isaBusMutex.ReleaseMutex();
            }
        }
    }
}
