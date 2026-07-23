using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace KeyMapper
{
    /// <summary>
    /// Turns mouse-wheel steps into a short eased motion while preserving
    /// native touch and scrollbar interaction.
    /// </summary>
    public sealed class SmoothScrollViewer : ScrollViewer
    {
        private const double WheelDistance = 88;
        private double _targetOffset;
        private bool _isAnimating;

        public static readonly DependencyProperty AnimatedVerticalOffsetProperty =
            DependencyProperty.Register(
                nameof(AnimatedVerticalOffset),
                typeof(double),
                typeof(SmoothScrollViewer),
                new PropertyMetadata(0d, OnAnimatedVerticalOffsetChanged));

        public double AnimatedVerticalOffset
        {
            get => (double)GetValue(AnimatedVerticalOffsetProperty);
            set => SetValue(AnimatedVerticalOffsetProperty, value);
        }

        public SmoothScrollViewer()
        {
            CanContentScroll = false;
            PanningMode = PanningMode.VerticalOnly;
            ScrollChanged += (_, _) =>
            {
                if (!_isAnimating)
                {
                    _targetOffset = VerticalOffset;
                    SetCurrentValue(
                        AnimatedVerticalOffsetProperty,
                        VerticalOffset);
                }
            };
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (ScrollableHeight <= 0)
            {
                base.OnPreviewMouseWheel(e);
                return;
            }

            e.Handled = true;
            double direction = e.Delta > 0 ? -1 : 1;
            _targetOffset = Math.Clamp(
                (_isAnimating ? _targetOffset : VerticalOffset) +
                (direction * WheelDistance),
                0,
                ScrollableHeight);

            BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetCurrentValue(AnimatedVerticalOffsetProperty, VerticalOffset);

            var animation = new DoubleAnimation
            {
                From = VerticalOffset,
                To = _targetOffset,
                Duration = TimeSpan.FromMilliseconds(190),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            _isAnimating = true;
            animation.Completed += (_, _) =>
            {
                double finalOffset = _targetOffset;
                BeginAnimation(AnimatedVerticalOffsetProperty, null);
                SetCurrentValue(
                    AnimatedVerticalOffsetProperty,
                    finalOffset);
                ScrollToVerticalOffset(finalOffset);
                _isAnimating = false;
            };

            BeginAnimation(
                AnimatedVerticalOffsetProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private static void OnAnimatedVerticalOffsetChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is SmoothScrollViewer viewer &&
                e.NewValue is double offset)
            {
                viewer.ScrollToVerticalOffset(offset);
            }
        }
    }
}
