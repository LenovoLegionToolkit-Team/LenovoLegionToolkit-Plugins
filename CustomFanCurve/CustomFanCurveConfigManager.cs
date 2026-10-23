using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public class CustomFanCurveConfigManager
    {
        private readonly PluginSettings _store;
        private CustomFanCurveSettings _settings;
        private readonly object _lock = new();

        public CustomFanCurveSettings Settings => _settings;
        public string RecoveryDirectory => _store.RecoveryDirectory;
        public event Action? SettingsChanged;

        public CustomFanCurveConfigManager(PluginSettings store)
        {
            _store = store;
            _settings = store.Load();
        }

        public void EnsureEntriesForFans(IReadOnlyList<int> fanIds)
        {
            lock (_lock)
            {
                var changed = false;

                if (_settings.Entries.Any(e => e.FanId == 0))
                {
                    _settings.Entries.Clear();
                    changed = true;
                }

                foreach (var fanId in fanIds)
                {
                    if (!_settings.Entries.Any(e => e.FanId == fanId))
                    {
                        _settings.Entries.Add(new CustomFanCurveEntry { FanId = fanId });
                        changed = true;
                    }
                }

                _settings.Entries.RemoveAll(e => !fanIds.Contains(e.FanId));

                if (changed)
                {
                    _store.Save(_settings);
                    SettingsChanged?.Invoke();
                }
            }
        }

        public void UpdateSetting<T>(string propertyName, T value)
        {
            lock (_lock)
            {
                var prop = typeof(CustomFanCurveSettings).GetProperty(propertyName);
                if (prop != null && prop.CanWrite)
                {
                    var oldValue = prop.GetValue(_settings);
                    prop.SetValue(_settings, value);
                    Logger.Debug($"Setting '{propertyName}' updated from '{oldValue}' to '{value}'");
                    _store.Save(_settings);
                    SettingsChanged?.Invoke();
                }
            }
        }

        public CustomFanCurveEntry? GetEntry(int fanId)
        {
            lock (_lock) { return _settings.Entries.FirstOrDefault(e => e.FanId == fanId); }
        }

        public void SaveEntry(CustomFanCurveEntry entry)
        {
            lock (_lock)
            {
                var existing = _settings.Entries.FirstOrDefault(e => e.FanId == entry.FanId);
                if (existing != null) _settings.Entries.Remove(existing);
                _settings.Entries.Add(entry);
                Logger.Debug($"Saved fan curve entry for FanId {entry.FanId}: [{string.Join(", ", entry.CurveNodes.Select(n => $"{n.Temperature}C:{n.TargetPercent}%"))}]");
                _store.Save(_settings);
                SettingsChanged?.Invoke();
            }
        }

        public Task SaveEntryAsync(CustomFanCurveEntry entry)
        {
            // A pending edit must not resurrect data replaced by restore or copy.
            lock (_lock)
            {
                if (_settings.Entries.Any(e => ReferenceEquals(e, entry)))
                    SaveEntry(entry);
            }
            return Task.CompletedTask;
        }

        public List<CustomFanCurveEntry> GetAllEntries()
        {
            lock (_lock) { return _settings.Entries.ToList(); }
        }

        public string ExportBackup()
        {
            lock (_lock) { return ConfigurationBackup.Export(_settings); }
        }

        public void CopyFan(int sourceFanId, IReadOnlyList<int> targetFanIds)
        {
            lock (_lock)
            {
                var source = GetEntry(sourceFanId) ?? throw new ArgumentException("Source fan not found.");
                var targets = targetFanIds.Distinct().Where(id => id != sourceFanId).ToArray();
                if (targets.Any(id => GetEntry(id) == null))
                    throw new ArgumentException("Target fan not found.");
                var replacement = ConfigurationBackup.Clone(_settings);
                foreach (var id in targets)
                {
                    replacement.Entries.RemoveAll(e => e.FanId == id);
                    replacement.Entries.Add(ConfigurationBackup.CopyEntry(source, id));
                }
                _store.SaveRecoveryBackup(_settings);
                _store.Save(replacement);
                _settings = replacement;
            }
            SettingsChanged?.Invoke();
        }

        public void Restore(CustomFanCurveSettings settings, IReadOnlyList<int> availableFanIds)
        {
            ConfigurationBackup.Validate(settings);
            lock (_lock)
            {
                var replacement = ConfigurationBackup.Clone(settings);
                // Calibration belongs to this machine, not to the machine that created the backup.
                replacement.FanMaxRpms = new(_settings.FanMaxRpms);
                replacement.FallbackProbeDone = _settings.FallbackProbeDone;
                replacement.Entries.RemoveAll(e => !availableFanIds.Contains(e.FanId));
                foreach (var id in availableFanIds)
                    if (!replacement.Entries.Any(e => e.FanId == id))
                        replacement.Entries.Add(new CustomFanCurveEntry { FanId = id });
                _store.SaveRecoveryBackup(_settings);
                _store.Save(replacement);
                _settings = replacement;
            }
            SettingsChanged?.Invoke();
        }

        public void ResetFan(int fanId)
        {
            lock (_lock)
            {
                if (GetEntry(fanId) == null) throw new ArgumentException("Fan not found.");
                var replacement = ConfigurationBackup.Clone(_settings);
                replacement.Entries.RemoveAll(e => e.FanId == fanId);
                replacement.Entries.Add(new CustomFanCurveEntry { FanId = fanId });
                _store.SaveRecoveryBackup(_settings);
                _store.Save(replacement);
                _settings = replacement;
            }
            SettingsChanged?.Invoke();
        }

        public void DisableFanControl()
        {
            lock (_lock)
            {
                _settings.IsCustomFanEnabled = false;
                _settings.IsFullSpeed = false;
                _store.Save(_settings);
            }
            SettingsChanged?.Invoke();
        }
    }
}
