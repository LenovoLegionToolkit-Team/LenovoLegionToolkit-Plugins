using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public partial class CustomFanCurvePage : UiPage
    {
        private CustomFanCurvePageViewModel? _viewModel;
        private CustomFanCurveConfigManager? _configManager;
        private MachineInformation _machineInfo;

        public CustomFanCurvePage()
        {
            var realCulture = Resource.Culture ?? Thread.CurrentThread.CurrentUICulture;
            this.Language = XmlLanguage.GetLanguage(realCulture.IetfLanguageTag);
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Runtime.Provider is not CustomFanCurveProvider provider) return;

            _configManager = provider.ConfigManager;
            provider.ControlService.OnUIOpened();

            _machineInfo = await Compatibility.GetMachineInformationAsync().ConfigureAwait(true);

            _viewModel = new CustomFanCurvePageViewModel(_configManager, provider.ControlService, provider.Monitoring);
            DataContext = _viewModel;

            await provider.ControlService.InitializationTask;
            
            if (!this.IsLoaded)
            {
                return;
            }
            
            _viewModel.LoadFans(provider.AvailableFanIds);
            
            _viewModel.IsProbing = false;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (Runtime.Provider is CustomFanCurveProvider provider)
            {
                provider.ControlService?.OnUIClosed();
            }
            _viewModel?.Detach();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (_configManager == null) return;

            var series = _machineInfo.LegionSeries;
            var isLegion = series != LegionSeries.ThinkBook && series != LegionSeries.Lenovo_Slim
                && series != LegionSeries.IdeaPad
                && series != LegionSeries.YOGA
                && series != LegionSeries.Motorola && series != LegionSeries.Unknown;
            var isITSMode = _machineInfo.Properties.SupportsITSMode;
            
            var window = new SettingsWindow(_configManager, isLegion, isITSMode)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.ShowDialog();
        }

        private void Configuration_Click(object sender, RoutedEventArgs e)
        {
            if (_configManager == null || _viewModel == null || Runtime.Provider is not CustomFanCurveProvider provider) return;
            var fans = _viewModel.FanViewModels.ToArray();
            var selectedFanId = _viewModel.SelectedFanViewModel?.FanId ?? 1;
            var window = new ConfigurationWindow(_configManager, fans, selectedFanId,
                () => _viewModel.LoadFans(provider.AvailableFanIds))
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }
    }
}
