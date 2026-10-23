using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Wpf.Ui.Common;
using LenovoLegionToolkit.Plugin.CustomFanCurve.Resources;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve;

public partial class ConfigurationWindow : UiWindow
{
    private readonly CustomFanCurveConfigManager _config;
    private readonly IReadOnlyList<CustomFanCurveControlViewModel> _fans;
    private readonly Action _reload;

    public ConfigurationWindow(CustomFanCurveConfigManager config,
        IReadOnlyList<CustomFanCurveControlViewModel> fans, int selectedFanId, Action reload)
    {
        _config = config;
        _fans = fans;
        _reload = reload;
        InitializeComponent();
        SourceFan.ItemsSource = fans;
        SourceFan.SelectedItem = fans.FirstOrDefault(f => f.FanId == selectedFanId) ?? fans.FirstOrDefault();
    }

    private void SourceFan_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetFans == null || SourceFan.SelectedItem is not CustomFanCurveControlViewModel source) return;
        TargetFans.ItemsSource = _fans.Where(f => f.FanId != source.FanId)
            .Select(f => new CopyTarget { FanId = f.FanId, DisplayName = f.DisplayName }).ToList();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (SourceFan.SelectedItem is not CustomFanCurveControlViewModel source) return;
        var targets = ((IEnumerable<CopyTarget>)TargetFans.ItemsSource).Where(t => t.IsSelected).Select(t => t.FanId).ToArray();
        if (targets.Length == 0) { Status.Text = Resource.SelectCopyTarget; return; }
        if (!Confirm(Resource.ConfirmCopy)) return;
        _config.CopyFan(source.FanId, targets);
        _reload();
        Status.Text = Resource.ConfigurationSaved;
    });

    private void Export_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Custom Fan Curve backup (*.json)|*.json",
            FileName = $"custom-fan-curve-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt = ".json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, _config.ExportBackup());
        Status.Text = Resource.BackupSaved;
    });

    private void Import_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Custom Fan Curve backup (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 5 * 1024 * 1024)
            throw new InvalidDataException(Resource.BackupTooLarge);
        var settings = ConfigurationBackup.Import(File.ReadAllText(dialog.FileName));
        if (!Confirm(Resource.ConfirmRestore)) return;
        _config.Restore(settings, _fans.Select(f => f.FanId).ToArray());
        _reload();
        Status.Text = Resource.ConfigurationSaved;
    });

    private void ResetFan_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (SourceFan.SelectedItem is not CustomFanCurveControlViewModel source || !Confirm(Resource.ConfirmResetFan)) return;
        _config.ResetFan(source.FanId);
        _reload();
        Status.Text = Resource.ConfigurationSaved;
    });

    private void ResetAll_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        if (!Confirm(Resource.ConfirmResetAll)) return;
        _config.Restore(new CustomFanCurveSettings(), _fans.Select(f => f.FanId).ToArray());
        _reload();
        Status.Text = Resource.ConfigurationSaved;
    });

    private void OpenBackups_Click(object sender, RoutedEventArgs e) => Run(() =>
    {
        var dialog = new OpenFileDialog
        {
            InitialDirectory = _config.RecoveryDirectory,
            Filter = "Custom Fan Curve backup (*.json)|*.json",
            CheckFileExists = true
        };
        Directory.CreateDirectory(_config.RecoveryDirectory);
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 5 * 1024 * 1024)
            throw new InvalidDataException(Resource.BackupTooLarge);
        var settings = ConfigurationBackup.Import(File.ReadAllText(dialog.FileName));
        if (!Confirm(Resource.ConfirmRestore)) return;
        _config.Restore(settings, _fans.Select(f => f.FanId).ToArray());
        _reload();
        Status.Text = Resource.ConfigurationSaved;
    });

    private bool Confirm(string message)
    {
        var confirmed = false;
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            Title = Resource.ConfigurationTitle,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            ButtonLeftName = Resource.ConfigurationConfirm,
            ButtonRightName = Resource.Cancel,
            ButtonLeftAppearance = ControlAppearance.Primary,
            ShowInTaskbar = false,
            Topmost = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            MinHeight = 160,
            MaxHeight = double.PositiveInfinity,
            ResizeMode = ResizeMode.NoResize
        };
        dialog.ButtonLeftClick += (_, _) => { confirmed = true; dialog.Close(); };
        dialog.ButtonRightClick += (_, _) => dialog.Close();
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            dialog.Close();
        };
        dialog.ShowDialog();
        return confirmed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Logger.Error("Configuration operation failed", ex);
            Status.Text = Resource.ConfigurationFailed + " " + ex.Message;
        }
    }

    private sealed class CopyTarget
    {
        public int FanId { get; init; }
        public string DisplayName { get; init; } = "";
        public bool IsSelected { get; set; }
    }
}
