using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Plugin.CustomFanCurve;
using Newtonsoft.Json;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CustomFanCurve-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = new CustomFanCurveConfigManager(new PluginSettings(directory));
            config.EnsureEntriesForFans(new[] { 1, 2, 4 });
            config.Settings.FanMaxRpms = new() { [1] = 5200, [2] = 5400, [4] = 6500 };
            config.Settings.DebugMode = true;
            var source = config.GetEntry(2)!;
            source.SensorSource = SensorSource.MaxCpuGpu;
            source.CurveNodes[4].TargetPercent = 35;
            var backup = config.ExportBackup();
            var imported = ConfigurationBackup.Import(backup);
            Check(imported.DebugMode && imported.Entries.Count == 3, "Full backup round trip");
            Check(imported.Entries.Single(e => e.FanId == 2).CurveNodes.Count == 11, "No duplicated constructor nodes");
            Check(ConfigurationBackup.Import(JsonConvert.SerializeObject(config.Settings)).Entries.Count == 3, "Legacy settings import");

            var stale = config.GetEntry(1)!;
            config.CopyFan(2, new[] { 1, 4 });
            Check(config.GetEntry(1)!.SensorSource == SensorSource.MaxCpuGpu && config.GetEntry(4)!.CurveNodes[4].TargetPercent == 35, "Copy to multiple fans");
            config.GetEntry(1)!.CurveNodes[4].TargetPercent = 32;
            Check(config.GetEntry(2)!.CurveNodes[4].TargetPercent == 35, "Copied curves are independent");
            config.SaveEntryAsync(stale).GetAwaiter().GetResult();
            Check(config.GetEntry(1)!.CurveNodes[4].TargetPercent == 32, "Stale editor cannot overwrite a copied curve");
            Check(config.Settings.FanMaxRpms[1] == 5200, "Copy preserves destination calibration");

            config.ResetFan(1);
            Check(config.GetEntry(1)!.SensorSource == SensorSource.Default && config.GetEntry(1)!.CurveNodes[4].TargetPercent == 40, "Reset one fan");
            imported.FanMaxRpms[1] = 9999;
            imported.Entries.Add(new CustomFanCurveEntry { FanId = 5 });
            config.Restore(imported, new[] { 1, 2, 4 });
            Check(config.Settings.DebugMode && config.Settings.FanMaxRpms[1] == 5200, "Restore settings while preserving local calibration");
            Check(config.GetEntry(5) == null, "Restore ignores unavailable fans");
            config.Restore(new CustomFanCurveSettings(), new[] { 1, 2, 4 });
            Check(!config.Settings.DebugMode && !config.Settings.IsCustomFanEnabled && !config.Settings.IsFullSpeed && config.Settings.Entries.Count == 3, "Reset all settings");
            Check(Directory.GetFiles(config.RecoveryDirectory, "*.json").Length == 4, "Recovery backup before every replacement");
            var recovery = ConfigurationBackup.Import(File.ReadAllText(Directory.GetFiles(config.RecoveryDirectory, "*.json")[0]));
            Check(recovery.Entries.Count == 3, "Recovery backup can be restored");
            Check(new PluginSettings(directory).Load().Entries.Count == 3, "Settings persisted on disk");

            Reject("{}", "Unrelated JSON rejected");
            Reject(backup.Replace("\"Version\": 1", "\"Version\": 999"), "Unknown backup version rejected");
            Reject(backup.Replace("\"TargetPercent\": 35", "\"TargetPercent\": 200"), "Invalid speed rejected before clamping");
            Reject(backup.Replace("\"SensorIntervalMs\": 500", "\"SensorIntervalMs\": -1"), "Invalid interval rejected");
            Reject(backup.Replace("\"SensorSource\": 1", "\"SensorSource\": 999"), "Invalid sensor rejected");
            Reject(backup.Replace("\"Temperature\": 10.0", "\"Temperature\": 0.0"), "Duplicate temperatures rejected");
            var invalid = ConfigurationBackup.Clone(config.Settings);
            invalid.Entries[0].CurveNodes[2].TargetPercent = 0;
            try { ConfigurationBackup.Validate(invalid); throw new Exception("Expected rejection"); }
            catch (InvalidDataException) { Check(true, "Decreasing curve rejected"); }

            Check(SensorFailureTests.Run(config), "MissingMethodException is contained, firmware control restored and custom control disabled");

            var originalEntry = config.GetEntry(1);
            var blockedTemporaryPath = Path.Combine(directory, "custom_fan_curve_settings.json.tmp");
            Directory.CreateDirectory(blockedTemporaryPath);
            try
            {
                config.ResetFan(1);
                throw new Exception("Expected save failure.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Check(ReferenceEquals(config.GetEntry(1), originalEntry), "Failed write leaves live configuration unchanged");
            }
            finally { Directory.Delete(blockedTemporaryPath); }

            if (args.Length > 0)
                Check(ConfigurationBackup.Import(File.ReadAllText(args[0])).Entries.Count > 0, "Existing installed configuration is importable");

            // Load the actual compiled WPF window without starting sensors or writing to hardware.
            var app = new Application();
            var monitoring = new CustomFanMonitoringService();
            var fans = config.GetAllEntries().Select(e => new CustomFanCurveControlViewModel(e, config, monitoring, $"Fan {e.FanId}")).ToArray();
            var window = new ConfigurationWindow(config, fans, 2, () => { });
            Check(window.Content != null, "Configuration window XAML loads");
            Check(((ComboBox)window.FindName("SourceFan")).SelectedItem == fans.Single(f => f.FanId == 2), "Copy dialog selects the current fan");
            Check(((ItemsControl)window.FindName("TargetFans")).Items.Count == 2, "Copy dialog lists the other fans as targets");
            window.Close();
            foreach (var fan in fans) fan.Detach();
            app.Shutdown();

            using var pe = new PEReader(File.OpenRead(typeof(ConfigurationBackup).Assembly.Location));
            var metadata = pe.GetMetadataReader();
            var oldSensorGetters = new[] { "get_CpuTemp", "get_GpuTemp", "get_CpuPower", "get_GpuPower" };
            Check(!metadata.MemberReferences.Any(h => oldSensorGetters.Contains(metadata.GetString(metadata.GetMemberReference(h).Name))),
                "Plugin contains no calls to removed sensor getters");
            Console.WriteLine($"PASS: {_checks} configuration and WPF smoke checks.");
        }
        finally
        {
            // This directory is created by this test, with an unpredictable name under the OS temp folder.
            var resolvedDirectory = Path.GetFullPath(directory);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolvedDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test directory is outside the temporary directory.");
            Directory.Delete(resolvedDirectory, recursive: true);
        }
    }

    private static void Reject(string json, string name)
    {
        try { ConfigurationBackup.Import(json); }
        catch (Exception ex) when (ex is InvalidDataException || ex is JsonException)
        {
            Check(true, name);
            return;
        }
        throw new Exception(name + ": expected rejection");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
    }
}
