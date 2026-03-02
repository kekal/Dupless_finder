using System.Windows.Input;

namespace Dupples_finder_UI.Modules.Helpers
{
    public class MouseWheelGesture : MouseGesture
    {
        public static MouseWheelGesture Down => new()
        {
            Direction = WheelDirection.Down
        };

        public static MouseWheelGesture Up => new()
        {
            Direction = WheelDirection.Up
        };

        private MouseWheelGesture() : base(MouseAction.WheelClick)
        {
        }

        private WheelDirection Direction { get; init; }

        public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
        {
            if (!base.Matches(targetElement, inputEventArgs))
            {
                return false;
            }

            if (!(inputEventArgs is MouseWheelEventArgs args))
            {
                return false;
            }

            switch (Direction)
            {
                case WheelDirection.None:
                    return args.Delta == 0;
                case WheelDirection.Up:
                    return args.Delta > 0;
                case WheelDirection.Down:
                    return args.Delta < 0;
                default:
                    return false;
            }
        }

        private enum WheelDirection
        {
            None,
            Up,
            Down,
        }
    }
}
