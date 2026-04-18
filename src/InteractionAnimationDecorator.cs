using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Visual states supported by the interaction animation system.
    /// </summary>
    public enum InteractionVisualState
    {
        Idle,
        Hover,
        Pressed,
        Focused,
        Disabled
    }

    /// <summary>
    /// State values applied by the interaction animation system.
    /// </summary>
    public readonly struct InteractionStateValues
    {
        public InteractionStateValues(double scale, double opacity, double translateY, double shadowOpacity)
        {
            Scale = scale;
            Opacity = opacity;
            TranslateY = translateY;
            ShadowOpacity = shadowOpacity;
        }

        public double Scale { get; }
        public double Opacity { get; }
        public double TranslateY { get; }
        public double ShadowOpacity { get; }
    }

    /// <summary>
    /// Central profile for Material/Fluent-inspired interaction states.
    /// Uses transform and opacity changes only (GPU-friendly, no layout invalidation).
    /// </summary>
    public static class InteractionAnimationProfile
    {
        public static readonly Duration FastDuration = new(TimeSpan.FromMilliseconds(75));
        public static readonly Duration NormalDuration = new(TimeSpan.FromMilliseconds(180));

        private static readonly IEasingFunction SmoothEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static readonly IEasingFunction PressEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        public static IEasingFunction GetEasing(InteractionVisualState state)
        {
            return state == InteractionVisualState.Pressed ? PressEase : SmoothEase;
        }

        public static Duration GetDuration(InteractionVisualState state)
        {
            return state == InteractionVisualState.Pressed ? FastDuration : NormalDuration;
        }

        public static InteractionStateValues GetValues(InteractionVisualState state)
        {
            return state switch
            {
                InteractionVisualState.Hover => new InteractionStateValues(scale: 1.045, opacity: 1.0, translateY: -2.0, shadowOpacity: 0.45),
                InteractionVisualState.Pressed => new InteractionStateValues(scale: 0.955, opacity: 0.95, translateY: 1.5, shadowOpacity: 0.20),
                InteractionVisualState.Focused => new InteractionStateValues(scale: 1.03, opacity: 1.0, translateY: 0.0, shadowOpacity: 0.55),
                InteractionVisualState.Disabled => new InteractionStateValues(scale: 1.0, opacity: 0.50, translateY: 0.0, shadowOpacity: 0.0),
                _ => new InteractionStateValues(scale: 1.0, opacity: 1.0, translateY: 0.0, shadowOpacity: 0.26)
            };
        }
    }

    /// <summary>
    /// Non-invasive animation decorator for interactive controls.
    /// Enable via XAML attached property: local:InteractionAnimationDecorator.IsEnabled="True".
    /// </summary>
    public static class InteractionAnimationDecorator
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(InteractionAnimationDecorator),
                new PropertyMetadata(false, OnIsEnabledChanged));

        private static readonly DependencyProperty ScaleTransformProperty =
            DependencyProperty.RegisterAttached("ScaleTransform", typeof(ScaleTransform), typeof(InteractionAnimationDecorator), new PropertyMetadata(null));

        private static readonly DependencyProperty TranslateTransformProperty =
            DependencyProperty.RegisterAttached("TranslateTransform", typeof(TranslateTransform), typeof(InteractionAnimationDecorator), new PropertyMetadata(null));

        private static readonly DependencyProperty ShadowEffectProperty =
            DependencyProperty.RegisterAttached("ShadowEffect", typeof(DropShadowEffect), typeof(InteractionAnimationDecorator), new PropertyMetadata(null));

        public static void SetIsEnabled(DependencyObject element, bool value)
        {
            element.SetValue(IsEnabledProperty, value);
        }

        public static bool GetIsEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(IsEnabledProperty);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                Attach(element);
            }
            else
            {
                Detach(element);
            }
        }

        private static void Attach(FrameworkElement element)
        {
            EnsureAnimationPrimitives(element);

            element.MouseEnter += OnMouseEnter;
            element.MouseLeave += OnMouseLeave;
            element.PreviewMouseLeftButtonDown += OnMouseDown;
            element.PreviewMouseLeftButtonUp += OnMouseUp;
            element.GotKeyboardFocus += OnGotFocus;
            element.LostKeyboardFocus += OnLostFocus;
            element.IsEnabledChanged += OnControlEnabledChanged;

            ApplyState(element, element.IsEnabled ? InteractionVisualState.Idle : InteractionVisualState.Disabled, animate: false);
        }

        private static void Detach(FrameworkElement element)
        {
            element.MouseEnter -= OnMouseEnter;
            element.MouseLeave -= OnMouseLeave;
            element.PreviewMouseLeftButtonDown -= OnMouseDown;
            element.PreviewMouseLeftButtonUp -= OnMouseUp;
            element.GotKeyboardFocus -= OnGotFocus;
            element.LostKeyboardFocus -= OnLostFocus;
            element.IsEnabledChanged -= OnControlEnabledChanged;
        }

        private static void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsEnabled)
            {
                ApplyState(element, InteractionVisualState.Hover, animate: true);
            }
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var targetState = element.IsKeyboardFocused ? InteractionVisualState.Focused : InteractionVisualState.Idle;
                ApplyState(element, targetState, animate: true);
            }
        }

        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsEnabled)
            {
                ApplyState(element, InteractionVisualState.Pressed, animate: true);
            }
        }

        private static void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsEnabled)
            {
                var targetState = element.IsMouseOver ? InteractionVisualState.Hover : InteractionVisualState.Idle;
                ApplyState(element, targetState, animate: true);
            }
        }

        private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is FrameworkElement element && element.IsEnabled)
            {
                ApplyState(element, InteractionVisualState.Focused, animate: true);
            }
        }

        private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var targetState = element.IsMouseOver ? InteractionVisualState.Hover : InteractionVisualState.Idle;
                ApplyState(element, targetState, animate: true);
            }
        }

        private static void OnControlEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var isEnabled = (bool)e.NewValue;
                ApplyState(element, isEnabled ? InteractionVisualState.Idle : InteractionVisualState.Disabled, animate: true);
            }
        }

        internal static void ApplyState(FrameworkElement element, InteractionVisualState state, bool animate)
        {
            EnsureAnimationPrimitives(element);

            var scaleTransform = (ScaleTransform)element.GetValue(ScaleTransformProperty);
            var translateTransform = (TranslateTransform)element.GetValue(TranslateTransformProperty);
            var shadowEffect = (DropShadowEffect)element.GetValue(ShadowEffectProperty);
            var values = InteractionAnimationProfile.GetValues(state);

            if (!animate)
            {
                scaleTransform.ScaleX = values.Scale;
                scaleTransform.ScaleY = values.Scale;
                translateTransform.Y = values.TranslateY;
                element.Opacity = values.Opacity;
                shadowEffect.Opacity = values.ShadowOpacity;
                return;
            }

            var duration = InteractionAnimationProfile.GetDuration(state);
            var easing = InteractionAnimationProfile.GetEasing(state);

            AnimateDouble(scaleTransform, ScaleTransform.ScaleXProperty, values.Scale, duration, easing);
            AnimateDouble(scaleTransform, ScaleTransform.ScaleYProperty, values.Scale, duration, easing);
            AnimateDouble(translateTransform, TranslateTransform.YProperty, values.TranslateY, duration, easing);
            AnimateDouble(element, UIElement.OpacityProperty, values.Opacity, duration, easing);
            AnimateDouble(shadowEffect, DropShadowEffect.OpacityProperty, values.ShadowOpacity, duration, easing);
        }

        private static void AnimateDouble(DependencyObject target, DependencyProperty property, double to, Duration duration, IEasingFunction easing)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = duration,
                EasingFunction = easing
            };

            if (target is Animatable animatable)
            {
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private static void EnsureAnimationPrimitives(FrameworkElement element)
        {
            if (element.GetValue(ScaleTransformProperty) is not ScaleTransform scaleTransform ||
                element.GetValue(TranslateTransformProperty) is not TranslateTransform translateTransform)
            {
                scaleTransform = new ScaleTransform(1.0, 1.0);
                translateTransform = new TranslateTransform(0.0, 0.0);
                var group = new TransformGroup();
                group.Children.Add(scaleTransform);
                group.Children.Add(translateTransform);

                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform = group;

                element.SetValue(ScaleTransformProperty, scaleTransform);
                element.SetValue(TranslateTransformProperty, translateTransform);
            }

            if (element.GetValue(ShadowEffectProperty) is not DropShadowEffect shadowEffect)
            {
                if (element.Effect is DropShadowEffect existingShadow)
                {
                    shadowEffect = existingShadow.CloneCurrentValue();
                }
                else
                {
                    shadowEffect = new DropShadowEffect
                    {
                        BlurRadius = 12,
                        ShadowDepth = 3,
                        Direction = 270,
                        Color = Colors.Black,
                        Opacity = 0.26
                    };
                }

                element.Effect = shadowEffect;
                element.SetValue(ShadowEffectProperty, shadowEffect);
            }
        }
    }
}
