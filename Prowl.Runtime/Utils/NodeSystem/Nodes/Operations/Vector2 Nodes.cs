// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Operations/Vector2/Add")]
public class Vector2_Add_Node : Node
{
    public override string Title => "Vec2 A + B";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Sum;

    public override object GetValue(NodePort port) => GetInputValue("A", A) + GetInputValue("B", B);
}

[Node("Operations/Vector2/Subtract")]
public class Vector2_Subtract_Node : Node
{
    public override string Title => "Vec2 A - B";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) - GetInputValue("B", B);
}

[Node("Operations/Vector2/Multiply")]
public class Vector2_Multiply_Node : Node
{
    public override string Title => "Vec2 A * B";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) * GetInputValue("B", B);
}

[Node("Operations/Vector2/Divide")]
public class Vector2_Divide_Node : Node
{
    public override string Title => "Vec2 A / B";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) / GetInputValue("B", B);
}

[Node("Operations/Vector2/Angle Between")]
public class Vector2_Angle_Node : Node
{
    public override string Title => "Vec2 Angle Between";
    public override float Width => 75;

    [Input] public Vector2 From;
    [Input] public Vector2 To;

    [Output, SerializeIgnore] public double Angle;

    public override object GetValue(NodePort port) => Vector2.AngleBetween(GetInputValue("From", From), GetInputValue("To", To));
}

[Node("Operations/Vector2/Clamp Magnitude")]
public class Vector2_ClampMagnitude_Node : Node
{
    public override string Title => "Vec2 Clamp Magnitude";
    public override float Width => 75;

    [Input] public Vector2 Vector;
    [Input] public double Max;

    [Output, SerializeIgnore] public Vector2 Clamped;

    public override object GetValue(NodePort port) => Vector2.ClampMagnitude(GetInputValue("Vector", Vector), GetInputValue("Max", Max));
}

[Node("Operations/Vector2/Direction From To")]
public class Vector2_Direction_Node : Node
{
    public override string Title => "Vec2 Direction From To";
    public override float Width => 75;

    [Input] public Vector2 From;
    [Input] public Vector2 To;

    [Output, SerializeIgnore] public Vector2 Direction;

    public override object GetValue(NodePort port) => GetInputValue("To", To) - GetInputValue("From", From);
}

[Node("Operations/Vector2/Distance")]
public class Vector2_Distance_Node : Node
{
    public override string Title => "Vec2 Distance";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public double Distance;

    public override object GetValue(NodePort port) => Vector2.Distance(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector2/Dot")]
public class Vector2_Dot_Node : Node
{
    public override string Title => "Vec2 Dot";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public double Dot;

    public override object GetValue(NodePort port) => Vector2.Dot(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector2/Lerp")]
public class Vector2_Lerp_Node : Node
{
    public override string Title => "Vec2 Lerp";
    public override float Width => 75;

    [Input] public Vector2 From;
    [Input] public Vector2 To;
    [Input] public double Time;

    [Output, SerializeIgnore] public Vector2 Lerp;

    public override object GetValue(NodePort port) => Vector2.Lerp(GetInputValue("From", From), GetInputValue("To", To), GetInputValue("Time", Time));
}

[Node("Operations/Vector2/Magnitude")]
public class Vector2_Magnitude_Node : Node
{
    public override string Title => "Vec2 Magnitude";
    public override float Width => 75;

    [Input] public Vector2 Vector;

    [Output, SerializeIgnore] public double Magnitude;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).magnitude;
}

[Node("Operations/Vector2/Max")]
public class Vector2_Max_Node : Node
{
    public override string Title => "Vec2 Max";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Max;

    public override object GetValue(NodePort port) => Vector2.Max(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector2/Min")]
public class Vector2_Min_Node : Node
{
    public override string Title => "Vec2 Min";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Min;

    public override object GetValue(NodePort port) => Vector2.Min(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector2/Move Towards")]
public class Vector2_MoveTowards_Node : Node
{
    public override string Title => "Vec2 Move Towards";
    public override float Width => 75;

    [Input] public Vector2 Current;
    [Input] public Vector2 Target;
    [Input] public double MaxDistanceDelta;

    [Output, SerializeIgnore] public Vector2 Moved;

    public override object GetValue(NodePort port) => Vector2.MoveTowards(GetInputValue("Current", Current), GetInputValue("Target", Target), GetInputValue("MaxDistanceDelta", MaxDistanceDelta));
}

[Node("Operations/Vector2/Normalize")]
public class Vector2_Normalize_Node : Node
{
    public override string Title => "Vec2 Normalize";
    public override float Width => 75;

    [Input] public Vector2 Vector;

    [Output, SerializeIgnore] public Vector2 Normalized;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).normalized;
}

[Node("Operations/Vector2/Reflect")]
public class Vector2_Reflect_Node : Node
{
    public override string Title => "Vec2 Reflect";
    public override float Width => 75;

    [Input] public Vector2 InDirection;
    [Input] public Vector2 Normal;

    [Output, SerializeIgnore] public Vector2 Reflected;

    public override object GetValue(NodePort port) => Vector2.Reflect(GetInputValue("InDirection", InDirection), GetInputValue("Normal", Normal));
}

[Node("Operations/Vector2/Scale")]
public class Vector2_Scale_Node : Node
{
    public override string Title => "Vec2 Scale";
    public override float Width => 75;

    [Input] public Vector2 A;
    [Input] public Vector2 B;

    [Output, SerializeIgnore] public Vector2 Scaled;

    public override object GetValue(NodePort port) => GetInputValue("A", A) * GetInputValue("B", B);
}

[Node("Operations/Vector2/Squared Magnitude")]
public class Vector2_SqrMagnitude_Node : Node
{
    public override string Title => "Vec2 Sqr Magnitude";
    public override float Width => 75;

    [Input] public Vector2 Vector;

    [Output, SerializeIgnore] public double SqrMagnitude;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).sqrMagnitude;
}

[Node("Operations/Vector2/Zero")]
public class Vector2_Zero_Node : Node
{
    public override string Title => "Vec2 Zero";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector2 Zero;

    public override object GetValue(NodePort port) => Vector2.zero;
}

[Node("Operations/Vector2/One")]
public class Vector2_One_Node : Node
{
    public override string Title => "Vec2 One";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector2 One;

    public override object GetValue(NodePort port) => Vector2.one;
}

[Node("Operations/Vector2/Up")]
public class Vector2_Up_Node : Node
{
    public override string Title => "Vec2 Up";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector2 Up;

    public override object GetValue(NodePort port) => Vector2.up;
}

[Node("Operations/Vector2/Right")]
public class Vector2_Right_Node : Node
{
    public override string Title => "Vec2 Right";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector2 Right;

    public override object GetValue(NodePort port) => Vector2.right;
}

[Node("Operations/Vector2/From XY")]
public class Vector2_FromXY_Node : Node
{
    public override string Title => "Vec2 From XY";
    public override float Width => 75;

    [Input] public double X;
    [Input] public double Y;

    [Output, SerializeIgnore] public Vector2 Vector;

    public override object GetValue(NodePort port) => new Vector2(GetInputValue("X", X), GetInputValue("Y", Y));
}

[Node("Operations/Vector2/To XY")]
public class Vector2_ToXY_Node : Node
{
    public override string Title => "Vec2 To XY";
    public override float Width => 75;

    [Input] public Vector2 Vector;

    [Output, SerializeIgnore] public double X;
    [Output, SerializeIgnore] public double Y;

    public override object GetValue(NodePort port)
    {
        if (port.fieldName == nameof(X))
            return GetInputValue("Vector", Vector).x;
        return GetInputValue("Vector", Vector).y;
    }
}
