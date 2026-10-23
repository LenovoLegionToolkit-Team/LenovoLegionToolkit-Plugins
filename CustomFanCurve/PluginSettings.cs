using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public sealed class PluginSettings
    {
        private const string FileName = "custom_fan_curve_settings.json";
        private readonly string _filePath;
        public string RecoveryDirectory => Path.Combine(Path.GetDirectoryName(_filePath)!, "Backups");
        private CustomFanCurveSettings? _cached;
        
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        public PluginSettings(string storagePath) => _filePath = Path.Combine(storagePath, FileName);

        public PluginSettings()
        {
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LenovoLegionToolkit", "Plugins", "CustomFanCurve");
            Directory.CreateDirectory(defaultPath);
            _filePath = Path.Combine(defaultPath, FileName);
        }

        public CustomFanCurveSettings Load()
        {
            if (_cached != null) return _cached;
            _fileLock.Wait();
            try
            {
                if (_cached != null) return _cached;
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _cached = JsonConvert.DeserializeObject<CustomFanCurveSettings>(json) ?? new CustomFanCurveSettings();
                    return _cached;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("CustomFanCurve load error", ex);
            }
            finally
            {
                _fileLock.Release();
            }

            _cached = new CustomFanCurveSettings();
            Save(_cached);
            return _cached;
        }

        public void Save(CustomFanCurveSettings settings)
        {
            _fileLock.Wait();
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                WriteAtomic(_filePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
                _cached = settings;
            }
            catch (Exception ex)
            {
                Logger.Error("CustomFanCurve save error", ex);
                throw;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveAsync(CustomFanCurveSettings settings)
        {
            await _fileLock.WaitAsync();
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                WriteAtomic(_filePath, json);
                _cached = settings;
            }
            catch (Exception ex)
            {
                Logger.Error("CustomFanCurve async save error", ex);
                throw;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public void SaveRecoveryBackup(CustomFanCurveSettings settings)
        {
            var directory = Path.Combine(Path.GetDirectoryName(_filePath)!, "Backups");
            Directory.CreateDirectory(directory);
            WriteAtomic(Path.Combine(directory, $"before-change-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json"),
                ConfigurationBackup.Export(settings));
        }

        private static void WriteAtomic(string path, string json)
        {
            var temporaryPath = path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
