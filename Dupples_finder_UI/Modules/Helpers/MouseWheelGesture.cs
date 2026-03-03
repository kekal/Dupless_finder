using System.Windows.Input;

namespace Dupples_finder_UI.Modules.Helpers;

public class MouseWheelGesture : MouseGesture
{
    private MouseWheelGesture() : base(MouseAction.WheelClick)
    {
    }

    private enum WheelDirection
    {
        None,
        Up,
        Down
    }

    public static MouseWheelGesture Down => new()
    {
        Direction = WheelDirection.Down
    };

    public static MouseWheelGesture Up => new()
    {
        Direction = WheelDirection.Up
    };

    private WheelDirection Direction { get; init; }

    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        if (!base.Matches(targetElement, inputEventArgs))
        {
            return false;
        }

        if (inputEventArgs is not MouseWheelEventArgs args)
        {
            return false;
        }

        return Direction switch
        {
            WheelDirection.None => args.Delta == 0,
            WheelDirection.Up => args.Delta > 0,
            WheelDirection.Down => args.Delta < 0,
            _ => false
        };
    }
}