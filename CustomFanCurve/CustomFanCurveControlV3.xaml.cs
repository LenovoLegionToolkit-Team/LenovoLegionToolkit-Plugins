using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LenovoLegionToolkit.Plugin.CustomFanCurve;

public partial class CustomFanCurveControlV3 : UserControl
{
    private CustomFanCurveControlViewModel? _viewModel;
    private bool _drawRequested;

    public CustomFanCurveControlV3()
    {
        InitializeComponent();
        Loaded += (s, e) => RequestDraw();
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
                RequestDraw();
        };
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CustomFanCurveControlViewModel oldVm)
        {
            oldVm.CurveNodes.CollectionChanged -= OnCurveNodesCollectionChanged;
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is CustomFanCurveControlViewModel newVm)
        {
            _viewModel = newVm;
            _viewModel.CurveNodes.CollectionChanged += OnCurveNodesCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_canvas.ActualWidth > 0 && _canvas.ActualHeight > 0)
                _viewModel.SetGraphSize(_canvas.ActualWidth, _canvas.ActualHeight);
            RequestDraw();
        }
        else
        {
            _viewModel = null;
        }
    }


    private void OnCurveNodesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RequestDraw();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomFanCurveControlViewModel.GraphPoints))
            RequestDraw();
    }

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_canvas.ActualWidth > 0 && _canvas.ActualHeight > 0)
            _viewModel?.SetGraphSize(_canvas.ActualWidth, _canvas.ActualHeight);
        RequestDraw();
    }

    private void RequestDraw()
    {
        if (_drawRequested || _viewModel == null || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
            return;
        _drawRequested = true;
        Dispatcher.InvokeAsync(() =>
        {
            _drawRequested = false;
            DrawGraph();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void DrawGraph()
    {
        if (_viewModel == null || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0) return;

        var points = _viewModel.GraphPoints;
        if (points == null || points.Count < 2) return;

        _canvas.Children.Clear();

        double w = _canvas.ActualWidth;
        double h = _canvas.ActualHeight;

        for (int i = 0; i <= 100; i += 10)
        {
            double y = h * (1.0 - i / 100.0);
            var hLine = new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            hLine.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");
            _canvas.Children.Add(hLine);

            double x = w * (i / 100.0);
            var vLine = new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                IsHitTestVisible = false
            };
            vLine.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");
            _canvas.Children.Add(vLine);
        }

        var settings = CustomFanCurveProvider.InstanceConfigManager?.Settings;
        bool safetyFloorDrawn = false;
        if (settings is { EnableThermalSafetyNet: true })
        {
            var floorPoints = new PointCollection();
            for (int temperature = (int)settings.SafeMinTemp; temperature <= 100; temperature++)
            {
                int percent = CustomFanCurveCalculator.GetSafeMinPercent(temperature);
                floorPoints.Add(new Point(temperature / 100.0 * w, (1.0 - percent / 100.0) * h));
            }

            if (floorPoints.Count >= 2)
            {
                var floor = new Polyline
                {
                    Points = floorPoints,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Opacity = 0.75,
                    IsHitTestVisible = false
                };
                floor.SetResourceReference(Shape.StrokeProperty, "SystemFillColorCriticalBrush");
                _canvas.Children.Add(floor);
                safetyFloorDrawn = true;
            }
        }

        _safetyLegend.Visibility = safetyFloorDrawn ? Visibility.Visible : Visibility.Collapsed;

        var px = points.Select(p => new Point(p.X * w, p.Y * h)).ToList();

        var fillPts = new PointCollection { new(px[0].X, h) };
        foreach (var pt in px) fillPts.Add(pt);
        fillPts.Add(new(px[^1].X, h));
        var poly = new Polygon
        {
            Opacity = 0.18,
            Points = fillPts,
            IsHitTestVisible = false
        };
        poly.SetResourceReference(Shape.FillProperty, "GraphAccentPrimaryBrush");
        _canvas.Children.Add(poly);

        for (int i = 0; i < px.Count - 1; i++)
        {
            var segment = new Line
            {
                X1 = px[i].X, Y1 = px[i].Y,
                X2 = px[i + 1].X, Y2 = px[i + 1].Y,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            segment.SetResourceReference(Shape.StrokeProperty, "GraphAccentPrimaryBrush");
            _canvas.Children.Add(segment);
        }
    }

    private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!IsEnabled) return;
        if (sender is not Thumb thumb) return;
        if (thumb.DataContext is not CurveNodeDisplay display) return;
        if (_canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0) return;
        if (!display.IsSelected) SelectNode(display);

        double newX = Math.Clamp(display.DisplayX + e.HorizontalChange, 0, _canvas.ActualWidth);
        double newY = Math.Clamp(display.DisplayY + e.VerticalChange, 0, _canvas.ActualHeight);

        float temp = (float)(newX / _canvas.ActualWidth * 100.0);
        int pct = (int)Math.Round((1.0 - newY / _canvas.ActualHeight) * 100.0);

        _viewModel?.MoveNode(display, temp, pct);
        RequestDraw();
    }

    private void GraphContainer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        var hitElement = e.OriginalSource as DependencyObject;
        while (hitElement != null && hitElement != _graphContainer)
        {
            if (hitElement == _floatingEditor)
            {
                return;
            }
            if (hitElement is Thumb thumb && thumb.DataContext is CurveNodeDisplay display)
            {
                SelectNode(display);
                return;
            }
            hitElement = VisualTreeHelper.GetParent(hitElement);
        }
        SelectNode(null);
    }

    private void SelectNode(CurveNodeDisplay? display)
    {
        if (_viewModel == null) return;
        foreach (var d in _viewModel.CurveNodeDisplays)
        {
            d.IsSelected = (d == display);
        }
        _viewModel.SelectedNodeDisplay = display;
    }

    private void UserControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hitElement = e.OriginalSource as DependencyObject;
        bool isInsideEditor = false;
        bool isInsideFocusableControl = false;

        var temp = hitElement;
        while (temp != null)
        {
            if (temp == _floatingEditor)
            {
                isInsideEditor = true;
                break;
            }
            if (temp is ComboBox || temp is Button || temp is TextBox || (temp is UIElement ui && ui.Focusable))
            {
                isInsideFocusableControl = true;
            }
            temp = VisualTreeHelper.GetParent(temp);
        }

        if (isInsideEditor)
        {
            return;
        }

        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused != null)
        {
            if (focused is TextBox textBox)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            }
            focused = VisualTreeHelper.GetParent(focused);
        }

        if (!isInsideFocusableControl)
        {
            Keyboard.ClearFocus();
        }
    }
}
