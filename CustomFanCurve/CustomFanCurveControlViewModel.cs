using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve
{
    public class CustomFanCurveControlViewModel : INotifyPropertyChanged
    {
        private readonly CustomFanCurveEntry _entry;
        private readonly CustomFanCurveConfigManager _configManager;
        private readonly ICustomFanMonitoringService _monitoring;
        private int _fanId;
        private double _graphWidth, _graphHeight;
        private CancellationTokenSource? _saveCts;
        private bool _isEnforcingMonotonicity;
        private CurveNode? _nodeToSelectNext;

        public event PropertyChangedEventHandler? PropertyChanged;

        public CustomFanCurveControlViewModel(CustomFanCurveEntry entry, CustomFanCurveConfigManager configManager, ICustomFanMonitoringService monitoring, string displayName = "")
        {
            _entry = entry;
            _configManager = configManager;
            _monitoring = monitoring;
            _fanId = entry.FanId;
            DisplayName = displayName;

            CurveNodes = entry.CurveNodes;
            CurveNodeDisplays = new ObservableCollection<CurveNodeDisplay>();

            AddPointCommand = new RelayCommand(AddPoint);
            RemovePointCommand = new RelayCommand<object>(RemovePoint, CanRemovePoint);

            _monitoring.MonitoringUpdated += OnMonitoringUpdated;
            CurveNodes.CollectionChanged += OnCurveNodesCollectionChanged;
            foreach (var node in CurveNodes) node.PropertyChanged += OnCurveNodePropertyChanged;

            RefreshGraphPoints();
        }

        private void OnCurveNodesCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (CurveNode node in e.OldItems) node.PropertyChanged -= OnCurveNodePropertyChanged;
            if (e.NewItems != null) foreach (CurveNode node in e.NewItems) node.PropertyChanged += OnCurveNodePropertyChanged;
            RefreshGraphPoints();
        }

        private void EnforceMonotonicity(CurveNode changedNode, string propertyName)
        {
            if (CurveNodes.Count < 2) return;

            var otherNodes = CurveNodes.Where(n => n != changedNode).OrderBy(n => n.Temperature).ToList();
            var prevNode = otherNodes.LastOrDefault(n => n.Temperature <= changedNode.Temperature);
            var nextNode = otherNodes.FirstOrDefault(n => n.Temperature >= changedNode.Temperature);

            int safeMinPercent = CustomFanCurveCalculator.GetSafeMinPercent(changedNode.Temperature);
            int minPercent = Math.Max(safeMinPercent, prevNode != null ? prevNode.TargetPercent : 0);
            int maxPercent = nextNode != null ? nextNode.TargetPercent : 100;

            if (propertyName == nameof(CurveNode.TargetPercent) || propertyName == nameof(CurveNode.Temperature))
            {
                int clamped = Math.Clamp(changedNode.TargetPercent, minPercent, maxPercent);
                if (changedNode.TargetPercent != clamped)
                {
                    changedNode.TargetPercent = clamped;
                }
            }
        }

        private async void OnCurveNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CurveNode changedNode)
            {
                if (!_isEnforcingMonotonicity)
                {
                    _isEnforcingMonotonicity = true;
                    try
                    {
                        EnforceMonotonicity(changedNode, e.PropertyName ?? string.Empty);
                    }
                    finally
                    {
                        _isEnforcingMonotonicity = false;
                    }
                }

                if (_graphWidth > 0 && _graphHeight > 0)
                {
                    var display = CurveNodeDisplays.FirstOrDefault(d => d.Node == changedNode);
                    if (display != null)
                    {
                        double nx = Math.Clamp(changedNode.Temperature / 100.0, 0, 1);
                        double ny = 1.0 - Math.Clamp(changedNode.TargetPercent / 100.0, 0, 1);
                        display.DisplayX = nx * _graphWidth;
                        display.DisplayY = ny * _graphHeight;
                    }
                }
            }

            UpdateGraphPointsFromNodes();
            
            _saveCts?.Cancel();
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();
            var token = _saveCts.Token;

            try
            {
                await Task.Delay(_configManager.Settings.UiDebounceDelayMs, token);
                
                await _configManager.SaveEntryAsync(_entry);
            }
            catch (TaskCanceledException) 
            { 
            }
        }

        public string DisplayName { get; }
        public int FanId { get => _fanId; set { _fanId = value; OnPropertyChanged(); } }
        public ObservableCollection<CurveNode> CurveNodes { get; }
        public ObservableCollection<CurveNodeDisplay> CurveNodeDisplays { get; }

        private string _displayTemp = "--";
        public string DisplayTemp { get => _displayTemp; set { _displayTemp = value; OnPropertyChanged(); } }

        private string _actualRpmDisplay = "--";
        public string ActualRpmDisplay { get => _actualRpmDisplay; set { _actualRpmDisplay = value; OnPropertyChanged(); } }

        public IEnumerable<SensorSource> SensorSources => Enum.GetValues(typeof(SensorSource)).Cast<SensorSource>();

        public SensorSource SensorSource
        {
            get => _entry.SensorSource;
            set
            {
                if (_entry.SensorSource != value)
                {
                    _entry.SensorSource = value;
                    OnPropertyChanged();
                    _ = _configManager.SaveEntryAsync(_entry);
                }
            }
        }

        private string _targetRpmDisplay = "--";
        public string TargetRpmDisplay { get => _targetRpmDisplay; set { _targetRpmDisplay = value; OnPropertyChanged(); } }

        private int _currentRpmValue;
        public int CurrentRpmValue { get => _currentRpmValue; set { _currentRpmValue = value; OnPropertyChanged(); } }

        private IList<Point> _graphPoints = Array.Empty<Point>();
        public IList<Point> GraphPoints { get => _graphPoints; set { _graphPoints = value; OnPropertyChanged(); } }

        private CurveNodeDisplay? _selectedNodeDisplay;
        public CurveNodeDisplay? SelectedNodeDisplay { get => _selectedNodeDisplay; set { if (_selectedNodeDisplay != value) { _selectedNodeDisplay = value; OnPropertyChanged(); } } }

        public int MaxRpm => _configManager.Settings.FanMaxRpms.TryGetValue(FanId, out var rpm) && rpm > 0 ? rpm : 6400;

        public ICommand AddPointCommand { get; }
        public ICommand RemovePointCommand { get; }

        private bool CanRemovePoint(object? param) => param is CurveNode && CurveNodes.Count > 2;

        private void RemovePoint(object? param)
        {
            if (param is CurveNode node && CurveNodes.Contains(node))
            {
                if (SelectedNodeDisplay?.Node == node)
                {
                    SelectedNodeDisplay = null;
                }
                CurveNodes.Remove(node);
                _configManager.SaveEntry(_entry);
                RefreshGraphPoints();
            }
        }

        public void SetGraphSize(double width, double height)
        {
            if (Math.Abs(_graphWidth - width) < 0.1 && Math.Abs(_graphHeight - height) < 0.1) return;
            _graphWidth = width; _graphHeight = height; RefreshGraphPoints();
        }

        public void MovePoint(CurveNodeDisplay display, float temperature, int targetPercent)
        {
            if (display?.Node == null) return;
            temperature = Math.Clamp(temperature, 0, 100);

            var otherNodes = CurveNodes.Where(n => n != display.Node).OrderBy(n => n.Temperature).ToList();
            var prevNode = otherNodes.LastOrDefault(n => n.Temperature <= temperature);
            var nextNode = otherNodes.FirstOrDefault(n => n.Temperature >= temperature);

            int safeMinPercent = CustomFanCurveCalculator.GetSafeMinPercent(temperature);
            int minPercent = Math.Max(safeMinPercent, prevNode != null ? prevNode.TargetPercent : 0);
            int maxPercent = nextNode != null ? nextNode.TargetPercent : 100;

            targetPercent = Math.Clamp(targetPercent, minPercent, maxPercent);

            display.Node.Temperature = temperature;
            display.Node.TargetPercent = targetPercent;

            UpdateGraphPointsFromNodes();
            if (_graphWidth > 0 && _graphHeight > 0) { display.DisplayX = temperature / 100.0 * _graphWidth; display.DisplayY = (1.0 - targetPercent / 100.0) * _graphHeight; }   
        }

        private void AddPoint()
        {
            float temp;
            int targetPercent;

            var selectedNode = SelectedNodeDisplay?.Node;
            var sorted = CurveNodes.OrderBy(n => n.Temperature).ToList();

            if (selectedNode != null)
            {
                int index = sorted.IndexOf(selectedNode);
                if (index >= 0 && index < sorted.Count - 1)
                {
                    var nextNode = sorted[index + 1];
                    temp = (selectedNode.Temperature + nextNode.Temperature) / 2f;
                    targetPercent = (selectedNode.TargetPercent + nextNode.TargetPercent) / 2;
                }
                else
                {
                    temp = Math.Min(selectedNode.Temperature + 5, 100);
                    int safeMinPercent = CustomFanCurveCalculator.GetSafeMinPercent(temp);
                    targetPercent = Math.Clamp(selectedNode.TargetPercent, safeMinPercent, 100);
                }
            }
            else
            {
                var highest = sorted.LastOrDefault();
                temp = Math.Min((highest?.Temperature ?? 45) + 5, 100);
                int safeMinPercent = CustomFanCurveCalculator.GetSafeMinPercent(temp);
                targetPercent = Math.Clamp(highest?.TargetPercent ?? 50, safeMinPercent, 100);
            }

            var newNode = new CurveNode { Temperature = temp, TargetPercent = targetPercent };
            _nodeToSelectNext = newNode;

            CurveNodes.Add(newNode);
            _configManager.SaveEntry(_entry);
        }

        private void OnMonitoringUpdated(int fanId, FanMonitoringSnapshot snapshot)
        {
            if (fanId != _fanId) return;
            DisplayTemp = $"{snapshot.Temperature:F0} °C";
            ActualRpmDisplay = $"{snapshot.Rpm} RPM";
            TargetRpmDisplay = $"{snapshot.TargetRpm} RPM";
            CurrentRpmValue = snapshot.Rpm;
        }

        private void UpdateGraphPointsFromNodes()
        {
            if (CurveNodes.Count == 0) { GraphPoints = Array.Empty<Point>(); return; }
            GraphPoints = CurveNodes.OrderBy(n => n.Temperature).Select(n => new Point(Math.Clamp(n.Temperature / 100.0, 0, 1), 1.0 - Math.Clamp(n.TargetPercent / 100.0, 0, 1))).ToList<Point>();
        }

        public void RefreshGraphPoints()
        {
            if (CurveNodes.Count == 0)
            {
                GraphPoints = Array.Empty<Point>();
                foreach (var d in CurveNodeDisplays) d.Dispose();
                CurveNodeDisplays.Clear();
                SelectedNodeDisplay = null;
                return;
            }
            var sorted = CurveNodes.OrderBy(n => n.Temperature).ToList();
            var points = new List<Point>();
            var displays = new List<CurveNodeDisplay>();
            var hasSize = _graphWidth > 0 && _graphHeight > 0;
            var selectedNode = _nodeToSelectNext ?? SelectedNodeDisplay?.Node;
            _nodeToSelectNext = null;
            foreach (var node in sorted)
            {
                double nx = Math.Clamp(node.Temperature / 100.0, 0, 1), ny = 1.0 - Math.Clamp(node.TargetPercent / 100.0, 0, 1);
                points.Add(new Point(nx, ny));
                var display = new CurveNodeDisplay(node, hasSize ? nx * _graphWidth : 0, hasSize ? ny * _graphHeight : 0, MaxRpm);
                if (node == selectedNode)
                {
                    display.IsSelected = true;
                }
                displays.Add(display);
            }
            GraphPoints = points;
            foreach (var d in CurveNodeDisplays) d.Dispose();
            CurveNodeDisplays.Clear();
            foreach (var d in displays) CurveNodeDisplays.Add(d);
            SelectedNodeDisplay = CurveNodeDisplays.FirstOrDefault(d => d.IsSelected);
        }

        public void Detach()
        {
            _monitoring.MonitoringUpdated -= OnMonitoringUpdated;
            CurveNodes.CollectionChanged -= OnCurveNodesCollectionChanged;
            foreach (var node in CurveNodes)
            {
                node.PropertyChanged -= OnCurveNodePropertyChanged;
            }
            foreach (var d in CurveNodeDisplays)
            {
                d.Dispose();
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class CurveNodeDisplay : INotifyPropertyChanged, IDisposable
    {
        public CurveNode Node { get; }
        private readonly int _maxRpm;
        private double _displayX, _displayY;
        private bool _isSelected;
        private readonly PropertyChangedEventHandler _nodePropertyChangedHandler;

        public double DisplayX { get => _displayX; set { if (Math.Abs(_displayX - value) > 0.01) { _displayX = value; OnPropertyChanged(); } } }
        public double DisplayY { get => _displayY; set { if (Math.Abs(_displayY - value) > 0.01) { _displayY = value; OnPropertyChanged(); } } }
        public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } }
        public int Rpm => (int)(_maxRpm * (Node.TargetPercent / 100.0));
        public event PropertyChangedEventHandler? PropertyChanged;

        public CurveNodeDisplay(CurveNode node, double x, double y, int maxRpm)
        {
            Node = node;
            _displayX = x;
            _displayY = y;
            _maxRpm = maxRpm;
            _nodePropertyChangedHandler = (s, e) => {
                if (e.PropertyName == nameof(CurveNode.TargetPercent))
                {
                    OnPropertyChanged(nameof(Rpm));
                }
            };
            Node.PropertyChanged += _nodePropertyChangedHandler;
        }

        public void Dispose()
        {
            Node.PropertyChanged -= _nodePropertyChangedHandler;
        }

        protected void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
        public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
        public void Execute(object? p) => _execute();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
        public bool CanExecute(object? p) => _canExecute?.Invoke((T?)p) ?? true;
        public void Execute(object? p) => _execute((T?)p);
    }
}
