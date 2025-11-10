using Microsoft.Xaml.Behaviors;
using System.Windows;

namespace WinTabberUI.Behaviors
{
    internal class ControlledWindowBehavior : Behavior<Window>
    {
        // Dependency Property for changeEvents
        public static readonly DependencyProperty ChangeEventsProperty =
            DependencyProperty.Register(
                nameof(ChangeEvents),
                typeof(IObservable<bool>),
                typeof(ControlledWindowBehavior),
                new PropertyMetadata(null));

        public IObservable<bool> ChangeEvents
        {
            get => (IObservable<bool>)GetValue(ChangeEventsProperty);
            set => SetValue(ChangeEventsProperty, value);
        }

        // Dependency Property for strategy
        public static readonly DependencyProperty StrategyProperty =
            DependencyProperty.Register(
                nameof(Strategy),
                typeof(CloseStateStrategy),
                typeof(ControlledWindowBehavior),
                new PropertyMetadata(null));

        public CloseStateStrategy Strategy
        {
            get => (CloseStateStrategy)GetValue(StrategyProperty);
            set => SetValue(StrategyProperty, value);
        }

        // Dependency Property for openFunc
        public static readonly DependencyProperty OpenFuncProperty =
            DependencyProperty.Register(
                nameof(OpenFunc),
                typeof(Action<object>),
                typeof(ControlledWindowBehavior),
                new PropertyMetadata(null));

        public Action<object>? OpenFunc
        {
            get => (Action<object>?)GetValue(OpenFuncProperty);
            set => SetValue(OpenFuncProperty, value);
        }

        // Dependency Property for closeOnEvent
        public static readonly DependencyProperty CloseOnEventProperty =
            DependencyProperty.Register(
                nameof(CloseOnEvent),
                typeof(EventType?),
                typeof(ControlledWindowBehavior),
                new PropertyMetadata(null));

        public EventType? CloseOnEvent
        {
            get => (EventType?)GetValue(CloseOnEventProperty);
            set => SetValue(CloseOnEventProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
        }
    }

    // Placeholder for CloseStateStrategy and EventType
    public class CloseStateStrategy { }
    public enum EventType { }
}
