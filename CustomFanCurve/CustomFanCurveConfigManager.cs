using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public class CustomFanCurveConfigManager
    {
        private readonly PluginSettings _store;
        private CustomFanCurveSettings _settings;
        private readonly object _lock = new();
        private readonly SemaphoreSlim _asyncLock = new(1, 1);

        public CustomFanCurveSettings Settings => _settings;
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
                    prop.SetValue(_settings, value);
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
                _store.Save(_settings);
                SettingsChanged?.Invoke();
            }
        }

        public async Task SaveEntryAsync(CustomFanCurveEntry entry)
        {
            await _asyncLock.WaitAsync();
            try
            {
                lock (_lock)
                {
                    var existing = _settings.Entries.FirstOrDefault(e => e.FanId == entry.FanId);
                    if (existing != null) _settings.Entries.Remove(existing);
                    _settings.Entries.Add(entry);
                }
                
                await _store.SaveAsync(_settings);
                SettingsChanged?.Invoke();
            }
            finally
            {
                _asyncLock.Release();
            }
        }

        public List<CustomFanCurveEntry> GetAllEntries()
        {
            lock (_lock) { return _settings.Entries.ToList(); }
        }
    }
}
