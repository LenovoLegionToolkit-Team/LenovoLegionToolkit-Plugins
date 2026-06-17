using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public partial class GlobalSettingsWindow : UiWindow
    {
        private readonly GlobalSettingsViewModel _viewModel;
        private readonly List<UIElement> _panels;

        public GlobalSettingsWindow(CustomFanCurveConfigManager configManager, bool isLegionDevice, bool isITSModeDevice)
        {
            var realCulture = LenovoLegionToolkit.Lib.Resources.Resource.Culture ?? System.Threading.Thread.CurrentThread.CurrentUICulture;
            this.Language = System.Windows.Markup.XmlLanguage.GetLanguage(realCulture.IetfLanguageTag);
            InitializeComponent();
            _viewModel = new GlobalSettingsViewModel(configManager, isLegionDevice, isITSModeDevice);
            DataContext = _viewModel;

            _panels = new List<UIElement> { _panelBasic, _panelSensor, _panelSpinUp, _panelModeSwitch, _panelAdvanced };
            Loaded += (s, e) =>
            {
                foreach (var tb in FindVisualChildren<System.Windows.Controls.TextBox>(this))
                {
                    tb.PreviewTextInput += (_, a) => a.Handled = !Regex.IsMatch(a.Text, "^[0-9.]+$");
                    DataObject.AddPastingHandler(tb, NumericTextBox_Pasting);
                }
            };
        }

        private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_panels == null)
            {
                return;
            }

            if (_navListBox.SelectedItem is not ListBoxItem selected
                || selected.Tag is not string tagStr
                || !int.TryParse(tagStr, out var index))
            {
                return;
            }

            if (index < 0 || index >= _panels.Count)
            {
                return;
            }

            for (var i = 0; i < _panels.Count; i++)
            {
                _panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }

            _contentScroll?.ScrollToTop();
        }

        private void Save_Click(object sender, RoutedEventArgs e) { _viewModel.SaveAll(); DialogResult = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private static void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text) && Regex.IsMatch((string)e.DataObject.GetData(DataFormats.Text), "^[0-9.]+$"))
            {
                return;
            }

            e.CancelCommand();
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj == null)
            {
                yield break;
            }

            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                {
                    yield return t;
                }

                foreach (var c in FindVisualChildren<T>(child))
                {
                    yield return c;
                }
            }
        }
    }
}
