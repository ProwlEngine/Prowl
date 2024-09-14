// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Operations/Vector4/Add")]
public class Vector4_Add_Node : Node
{
    public override string Title => "Vec4 A + B";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public Vector4 Sum;

    public override object GetValue(NodePort port) => GetInputValue("A", A) + GetInputValue("B", B);
}

[Node("Operations/Vector4/Subtract")]
public class Vector4_Subtract_Node : Node
{
    public override string Title => "Vec4 A - B";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public Vector4 Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) - GetInputValue("B", B);
}

[Node("Operations/Vector4/Multiply")]
public class Vector4_Multiply_Node : Node
{
    public override string Title => "Vec4 A * B";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public Vector4 Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) * GetInputValue("B", B);
}

[Node("Operations/Vector4/Divide")]
public class Vector4_Divide_Node : Node
{
    public override string Title => "Vec4 A / B";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public Vector4 Result;

    public override object GetValue(NodePort port) => GetInputValue("A", A) / GetInputValue("B", B);
}

[Node("Operations/Vector4/Direction From To")]
public class Vector4_Direction_Node : Node
{
    public override string Title => "Vec4 Direction From To";
    public override float Width => 75;

    [Input] public Vector4 From;
    [Input] public Vector4 To;

    [Output, SerializeIgnore] public Vector4 Direction;

    public override object GetValue(NodePort port) => GetInputValue("To", To) - GetInputValue("From", From);
}

[Node("Operations/Vector4/Distance")]
public class Vector4_Distance_Node : Node
{
    public override string Title => "Vec4 Distance";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public double Distance;

    public override object GetValue(NodePort port) => Vector4.Distance(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector4/Dot")]
public class Vector4_Dot_Node : Node
{
    public override string Title => "Vec4 Dot";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public double Dot;

    public override object GetValue(NodePort port) => Vector4.Dot(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector4/Lerp")]
public class Vector4_Lerp_Node : Node
{
    public override string Title => "Vec4 Lerp";
    public override float Width => 75;

    [Input] public Vector4 From;
    [Input] public Vector4 To;
    [Input] public double Time;

    [Output, SerializeIgnore] public Vector4 Lerp;

    public override object GetValue(NodePort port) => Vector4.Lerp(GetInputValue("From", From), GetInputValue("To", To), GetInputValue("Time", Time));
}

[Node("Operations/Vector4/Magnitude")]
public class Vector4_Magnitude_Node : Node
{
    public override string Title => "Vec4 Magnitude";
    public override float Width => 75;

    [Input] public Vector4 Vector;

    [Output, SerializeIgnore] public double Magnitude;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).magnitude;
}

[Node("Operations/Vector4/Max")]
public class Vector4_Max_Node : Node
{
    public override string Title => "Vec4 Max";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public Vector4 Max;

    public override object GetValue(NodePort port) => Vector4.Max(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector4/Min")]
public class Vector4_Min_Node : Node
{
    public override string Title => "Vec4 Min";
    public override float Width => 75;

    [Input] public Vector4 A;
    [Input] public Vector4 B;

    [Output, SerializeIgnore] public Vector4 Min;

    public override object GetValue(NodePort port) => Vector4.Min(GetInputValue("A", A), GetInputValue("B", B));
}

[Node("Operations/Vector4/Move Towards")]
public class Vector4_MoveTowards_Node : Node
{
    public override string Title => "Vec4 Move Towards";
    public override float Width => 75;

    [Input] public Vector4 Current;
    [Input] public Vector4 Target;
    [Input] public double MaxDistanceDelta;

    [Output, SerializeIgnore] public Vector4 Moved;

    public override object GetValue(NodePort port) => Vector4.MoveTowards(GetInputValue("Current", Current), GetInputValue("Target", Target), GetInputValue("MaxDistanceDelta", MaxDistanceDelta));
}

[Node("Operations/Vector4/Normalize")]
public class Vector4_Normalize_Node : Node
{
    public override string Title => "Vec4 Normalize";
    public override float Width => 75;

    [Input] public Vector4 Vector;

    [Output, SerializeIgnore] public Vector4 Normalized;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).normalized;
}

[Node("Operations/Vector4/Scale")]
public class Vector4_Scale_Node : Node
{
    public override string Title => "Vec4 Scale";
    public override float Width => 75;

    [Input] public Vector4 Vector;
    [Input] public Vector4 ScaleBy;

    [Output, SerializeIgnore] public Vector4 Scaled;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector) * GetInputValue("ScaleBy", ScaleBy);
}

[Node("Operations/Vector4/Squared Magnitude")]
public class Vector4_SqrMagnitude_Node : Node
{
    public override string Title => "Vec4 Sqr Magnitude";
    public override float Width => 75;

    [Input] public Vector4 Vector;

    [Output, SerializeIgnore] public double SqrMagnitude;

    public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).sqrMagnitude;
}

[Node("Operations/Vector4/Zero")]
public class Vector4_Zero_Node : Node
{
    public override string Title => "Vec4 Zero";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector4 Zero;

    public override object GetValue(NodePort port) => Vector4.zero;
}

[Node("Operations/Vector4/One")]
public class Vector4_One_Node : Node
{
    public override string Title => "Vec4 One";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector4 One;

    public override object GetValue(NodePort port) => Vector4.one;
}

[Node("Operations/Vector4/Up")]
public class Vector4_Up_Node : Node
{
    public override string Title => "Vec4 Up";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector4 Up;

    public override object GetValue(NodePort port) => Vector4.up;
}

[Node("Operations/Vector4/Right")]
public class Vector4_Right_Node : Node
{
    public override string Title => "Vec4 Right";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector4 Right;

    public override object GetValue(NodePort port) => Vector4.right;
}

[Node("Operations/Vector4/Forward")]
public class Vector4_Forward_Node : Node
{
    public override string Title => "Vec4 Forward";
    public override float Width => 75;

    [Output, SerializeIgnore] public Vector4 Forward;

    public override object GetValue(NodePort port) => Vector4.forward;
}

[Node("Operations/Vector4/From XYZW")]
public class Vector4_FromXYZW_Node : Node
{
    public override string Title => "From XYZW";
    public override float Width => 75;

    [Input] public double X;
    [Input] public double Y;
    [Input] public double Z;
    [Input] public double W;

    [Output, SerializeIgnore] public Vector4 Vector;

    public override object GetValue(NodePort port) => new Vector4(GetInputValue("X", X), GetInputValue("Y", Y), GetInputValue("Z", Z), GetInputValue("W", W));
}

[Node("Operations/Vector4/To XYZW")]
public class Vector4_ToXYZW_Node : Node
{
    public override string Title => "To XYZW";
    public override float Width => 75;

    [Input] public Vector4 Vector;

    [Output, SerializeIgnore] public double X;
    [Output, SerializeIgnore] public double Y;
    [Output, SerializeIgnore] public double Z;
    [Output, SerializeIgnore] public double W;

    public override object GetValue(NodePort port)
    {
        if (port.fieldName == nameof(X))
            return GetInputValue("Vector", Vector).x;
        if (port.fieldName == nameof(Y))
            return GetInputValue("Vector", Vector).y;
        if (port.fieldName == nameof(Z))
            return GetInputValue("Vector", Vector).z;
        return GetInputValue("Vector", Vector).w;
    }
}
