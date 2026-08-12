using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace KeyMapper
{
    public partial class FlexVolumeControl : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(double),
                typeof(FlexVolumeControl),
                new FrameworkPropertyMetadata(
                    80d,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged,
                    CoerceValue));

        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent(
                nameof(ValueChanged),
                RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>),
                typeof(FlexVolumeControl));

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register(
                nameof(IsMuted),
                typeof(bool),
                typeof(FlexVolumeControl),
                new PropertyMetadata(false, OnIsMutedChanged));

        private bool _isDragging;
        private bool _isLoaded;
        private int _animationGeneration;

        public FlexVolumeControl()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                _isLoaded = true;
                UpdateGeometry(Value, false);
                UpdateAccessibleValue(Value);
            };
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        public event RoutedPropertyChangedEventHandler<double> ValueChanged
        {
            add => AddHandler(ValueChangedEvent, value);
            remove => RemoveHandler(ValueChangedEvent, value);
        }

        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new FlexVolumeAutomationPeer(this);
        }

        private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
        {
            return Math.Clamp((double)baseValue, 0d, 100d);
        }

        private static void OnValueChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs args)
        {
            var control = (FlexVolumeControl)dependencyObject;
            double oldValue = (double)args.OldValue;
            double newValue = (double)args.NewValue;
            control.UpdateAccessibleValue(newValue);
            control.UpdateGeometry(newValue, control._isLoaded && !control._isDragging);
            control.RaiseEvent(
                new RoutedPropertyChangedEventArgs<double>(
                    oldValue,
                    newValue,
                    ValueChangedEvent));

            if (UIElementAutomationPeer.FromElement(control) is
                FlexVolumeAutomationPeer peer)
            {
                peer.RaiseValueChanged(oldValue, newValue);
            }
        }

        private static void OnIsMutedChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs args)
        {
            var control = (FlexVolumeControl)dependencyObject;
            control.UpdateMuteVisual();
            control.UpdateAccessibleValue(control.Value);
        }

        private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double radius = Math.Max(1d, CapsuleShell.ActualWidth / 2d);
            CapsuleShell.CornerRadius = new CornerRadius(radius);
            OuterBloom.CornerRadius = new CornerRadius(radius);
            CapsuleClip.Rect = new Rect(
                0d,
                0d,
                Math.Max(0d, CapsuleSurface.ActualWidth),
                Math.Max(0d, CapsuleSurface.ActualHeight));
            CapsuleClip.RadiusX = radius;
            CapsuleClip.RadiusY = radius;
            UpdateGeometry(Value, false);
        }

        private void UpdateGeometry(double volume, bool animate)
        {
            if (CapsuleSurface.ActualHeight <= 0 || CapsuleSurface.ActualWidth <= 0)
            {
                return;
            }

            double width = CapsuleSurface.ActualWidth;
            double height = CapsuleSurface.ActualHeight;
            double activeHeight = height * Math.Clamp(volume / 100d, 0d, 1d);
            double coreHeight = activeHeight;
            int generation = ++_animationGeneration;

            FillCore.Width = width;
            FillFeather.Visibility = Visibility.Collapsed;
            BoundaryGlow.Visibility = Visibility.Collapsed;
            OuterBloom.Visibility = Visibility.Collapsed;

            bool useAnimation = animate && SystemParameters.ClientAreaAnimation;
            SetAnimatedValue(FillCore, FrameworkElement.HeightProperty, coreHeight, useAnimation, generation);
            SetAnimatedValue(FillCore, Canvas.TopProperty, height - coreHeight, useAnimation, generation);
            UpdateMuteVisual();
        }

        private void SetAnimatedValue(
            UIElement target,
            DependencyProperty property,
            double value,
            bool animate,
            int generation)
        {
            if (!animate)
            {
                target.BeginAnimation(property, null);
                target.SetValue(property, value);
                return;
            }

            double current = target.GetValue(property) is double effective &&
                             !double.IsNaN(effective)
                ? effective
                : value;
            target.BeginAnimation(property, null);
            target.SetValue(property, current);

            var animation = new DoubleAnimation
            {
                From = current,
                To = value,
                Duration = TimeSpan.FromMilliseconds(105),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            animation.Completed += (_, _) =>
            {
                if (generation != _animationGeneration)
                {
                    return;
                }

                target.BeginAnimation(property, null);
                target.SetValue(property, value);
            };
            target.BeginAnimation(
                property,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void Capsule_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            _isDragging = true;
            CapsuleShell.CaptureMouse();
            SetValueFromPointer(e);
            e.Handled = true;
        }

        private void Capsule_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            SetValueFromPointer(e);
            e.Handled = true;
        }

        private void Capsule_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            SetValueFromPointer(e);
            _isDragging = false;
            CapsuleShell.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void Capsule_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void SetValueFromPointer(MouseEventArgs e)
        {
            double height = Math.Max(1d, CapsuleSurface.ActualHeight);
            double y = Math.Clamp(e.GetPosition(CapsuleSurface).Y, 0d, height);
            Value = Math.Round((1d - (y / height)) * 100d);
        }

        private void Root_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Value += e.Delta > 0 ? 2d : -2d;
            e.Handled = true;
        }

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            double? next = e.Key switch
            {
                Key.Up or Key.Right => Value + 2d,
                Key.Down or Key.Left => Value - 2d,
                Key.PageUp => Value + 10d,
                Key.PageDown => Value - 10d,
                Key.Home => 0d,
                Key.End => 100d,
                _ => null
            };

            if (next.HasValue)
            {
                Value = next.Value;
                e.Handled = true;
            }
        }

        private void UpdateAccessibleValue(double volume)
        {
            string text = IsMuted
                ? $"Volume muted, {Math.Round(volume):0} percent"
                : $"Volume {Math.Round(volume):0} percent";
            ToolTip = text;
            SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, text);
        }

        private void UpdateMuteVisual()
        {
            if (MuteSlash == null)
            {
                return;
            }

            MuteSlash.Visibility = IsMuted || Value <= 0.1d
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private sealed class FlexVolumeAutomationPeer :
            FrameworkElementAutomationPeer,
            IRangeValueProvider
        {
            private FlexVolumeControl Control => (FlexVolumeControl)Owner;

            public FlexVolumeAutomationPeer(FlexVolumeControl owner)
                : base(owner)
            {
            }

            protected override string GetClassNameCore() =>
                nameof(FlexVolumeControl);

            protected override AutomationControlType GetAutomationControlTypeCore() =>
                AutomationControlType.Slider;

            public override object? GetPattern(PatternInterface patternInterface)
            {
                return patternInterface == PatternInterface.RangeValue
                    ? this
                    : base.GetPattern(patternInterface);
            }

            public bool IsReadOnly => false;
            public double LargeChange => 10d;
            public double SmallChange => 2d;
            public double Maximum => 100d;
            public double Minimum => 0d;
            public double Value => Control.Value;

            public void SetValue(double value)
            {
                if (!Control.IsEnabled)
                {
                    throw new ElementNotEnabledException();
                }

                if (value < Minimum || value > Maximum)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (Control.Dispatcher.CheckAccess())
                {
                    Control.Value = value;
                }
                else
                {
                    Control.Dispatcher.Invoke(() => Control.Value = value);
                }
            }

            public void RaiseValueChanged(double oldValue, double newValue)
            {
                RaisePropertyChangedEvent(
                    RangeValuePatternIdentifiers.ValueProperty,
                    oldValue,
                    newValue);
            }
        }
    }
}
