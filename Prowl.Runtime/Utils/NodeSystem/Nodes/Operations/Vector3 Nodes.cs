// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem
{
    [Node("Operations/Vector3/Add")]
    public class Vector3_Add_Node : Node
    {
        public override string Title => "Vec3 A + B";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Sum;

        public override object GetValue(NodePort port) => GetInputValue("A", A) + GetInputValue("B", B);
    }

    [Node("Operations/Vector3/Subtract")]
    public class Vector3_Subtract_Node : Node
    {
        public override string Title => "Vec3 A - B";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) - GetInputValue("B", B);
    }

    [Node("Operations/Vector3/Multiply")]
    public class Vector3_Multiply_Node : Node
    {
        public override string Title => "Vec3 A * B";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) * GetInputValue("B", B);
    }

    [Node("Operations/Vector3/Divide")]
    public class Vector3_Divide_Node : Node
    {
        public override string Title => "Vec3 A / B";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Result;

        public override object GetValue(NodePort port) => GetInputValue("A", A) / GetInputValue("B", B);
    }

    [Node("Operations/Vector3/Angle Between")]
    public class Vector3_Angle_Node : Node
    {
        public override string Title => "Vec3 Angle Between";
        public override float Width => 75;

        [Input] public Vector3 From;
        [Input] public Vector3 To;

        [Output, SerializeIgnore] public double Angle;

        public override object GetValue(NodePort port) => Vector3.AngleBetween(GetInputValue("From", From), GetInputValue("To", To));
    }

    [Node("Operations/Vector3/Clamp Magnitude")]
    public class Vector3_ClampMagnitude_Node : Node
    {
        public override string Title => "Vec3 Clamp Magnitude";
        public override float Width => 75;

        [Input] public Vector3 Vector;
        [Input] public double Max;

        [Output, SerializeIgnore] public Vector3 Clamped;

        public override object GetValue(NodePort port) => Vector3.ClampMagnitude(GetInputValue("Vector", Vector), GetInputValue("Max", Max));
    }

    [Node("Operations/Vector3/Direction From To")]
    public class Vector3_Direction_Node : Node
    {
        public override string Title => "Vec3 Direction From To";
        public override float Width => 75;

        [Input] public Vector3 From;
        [Input] public Vector3 To;

        [Output, SerializeIgnore] public Vector3 Direction;

        public override object GetValue(NodePort port) => GetInputValue("To", To) - GetInputValue("From", From);
    }

    [Node("Operations/Vector3/Distance")]
    public class Vector3_Distance_Node : Node
    {
        public override string Title => "Vec3 Distance";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public double Distance;

        public override object GetValue(NodePort port) => Vector3.Distance(GetInputValue("A", A), GetInputValue("B", B));
    }

    [Node("Operations/Vector3/Dot")]
    public class Vector3_Dot_Node : Node
    {
        public override string Title => "Vec3 Dot";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public double Dot;

        public override object GetValue(NodePort port) => Vector3.Dot(GetInputValue("A", A), GetInputValue("B", B));
    }

    [Node("Operations/Vector3/Lerp")]
    public class Vector3_Lerp_Node : Node
    {
        public override string Title => "Vec3 Lerp";
        public override float Width => 75;

        [Input] public Vector3 From;
        [Input] public Vector3 To;
        [Input] public double Time;

        [Output, SerializeIgnore] public Vector3 Lerp;

        public override object GetValue(NodePort port) => Vector3.Lerp(GetInputValue("From", From), GetInputValue("To", To), GetInputValue("Time", Time));
    }

    [Node("Operations/Vector3/Cross")]
    public class Vector3_Cross_Node : Node
    {
        public override string Title => "Vec3 Cross";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Cross;

        public override object GetValue(NodePort port) => Vector3.Cross(GetInputValue("A", A), GetInputValue("B", B));
    }

    [Node("Operations/Vector3/Magnitude")]
    public class Vector3_Magnitude_Node : Node
    {
        public override string Title => "Vec3 Magnitude";
        public override float Width => 75;

        [Input] public Vector3 Vector;

        [Output, SerializeIgnore] public double Magnitude;

        public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).magnitude;
    }

    [Node("Operations/Vector3/Max")]
    public class Vector3_Max_Node : Node
    {
        public override string Title => "Vec3 Max";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Max;

        public override object GetValue(NodePort port) => Vector3.Max(GetInputValue("A", A), GetInputValue("B", B));
    }

    [Node("Operations/Vector3/Min")]
    public class Vector3_Min_Node : Node
    {
        public override string Title => "Vec3 Min";
        public override float Width => 75;

        [Input] public Vector3 A;
        [Input] public Vector3 B;

        [Output, SerializeIgnore] public Vector3 Min;

        public override object GetValue(NodePort port) => Vector3.Min(GetInputValue("A", A), GetInputValue("B", B));
    }

    [Node("Operations/Vector3/Move Towards")]
    public class Vector3_MoveTowards_Node : Node
    {
        public override string Title => "Vec3 Move Towards";
        public override float Width => 75;

        [Input] public Vector3 Current;
        [Input] public Vector3 Target;
        [Input] public double MaxDistanceDelta;

        [Output, SerializeIgnore] public Vector3 Moved;

        public override object GetValue(NodePort port) => Vector3.MoveTowards(GetInputValue("Current", Current), GetInputValue("Target", Target), GetInputValue("MaxDistanceDelta", MaxDistanceDelta));
    }

    [Node("Operations/Vector3/Normalize")]
    public class Vector3_Normalize_Node : Node
    {
        public override string Title => "Vec3 Normalize";
        public override float Width => 75;

        [Input] public Vector3 Vector;

        [Output, SerializeIgnore] public Vector3 Normalized;

        public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).normalized;
    }

    [Node("Operations/Vector3/Reflect")]
    public class Vector3_Reflect_Node : Node
    {
        public override string Title => "Vec3 Reflect";
        public override float Width => 75;

        [Input] public Vector3 InDirection;
        [Input] public Vector3 Normal;

        [Output, SerializeIgnore] public Vector3 Reflected;

        public override object GetValue(NodePort port) => Vector3.Reflect(GetInputValue("InDirection", InDirection), GetInputValue("Normal", Normal));
    }

    [Node("Operations/Vector3/Scale")]
    public class Vector3_Scale_Node : Node
    {
        public override string Title => "Vec3 Scale";
        public override float Width => 75;

        [Input] public Vector3 Vector;
        [Input] public Vector3 ScaleBy;

        [Output, SerializeIgnore] public Vector3 Scaled;

        public override object GetValue(NodePort port) => Vector3.Scale(GetInputValue("Vector", Vector), GetInputValue("ScaleBy", ScaleBy));
    }

    [Node("Operations/Vector3/Squared Magnitude")]
    public class Vector3_SqrMagnitude_Node : Node
    {
        public override string Title => "Vec3 Sqr Magnitude";
        public override float Width => 75;

        [Input] public Vector3 Vector;

        [Output, SerializeIgnore] public double SqrMagnitude;

        public override object GetValue(NodePort port) => GetInputValue("Vector", Vector).sqrMagnitude;
    }

    [Node("Operations/Vector3/Zero")]
    public class Vector3_Zero_Node : Node
    {
        public override string Title => "Vec3 Zero";
        public override float Width => 75;

        [Output, SerializeIgnore] public Vector3 Zero;

        public override object GetValue(NodePort port) => Vector3.zero;
    }

    [Node("Operations/Vector3/One")]
    public class Vector3_One_Node : Node
    {
        public override string Title => "Vec3 One";
        public override float Width => 75;

        [Output, SerializeIgnore] public Vector3 One;

        public override object GetValue(NodePort port) => Vector3.one;
    }

    [Node("Operations/Vector3/Up")]
    public class Vector3_Up_Node : Node
    {
        public override string Title => "Vec3 Up";
        public override float Width => 75;

        [Output, SerializeIgnore] public Vector3 Up;

        public override object GetValue(NodePort port) => Vector3.up;
    }

    [Node("Operations/Vector3/Right")]
    public class Vector3_Right_Node : Node
    {
        public override string Title => "Vec3 Right";
        public override float Width => 75;

        [Output, SerializeIgnore] public Vector3 Right;

        public override object GetValue(NodePort port) => Vector3.right;
    }

    [Node("Operations/Vector3/Forward")]
    public class Vector3_Forward_Node : Node
    {
        public override string Title => "Vec3 Forward";
        public override float Width => 75;

        [Output, SerializeIgnore] public Vector3 Forward;

        public override object GetValue(NodePort port) => Vector3.forward;
    }

    [Node("Operations/Vector3/From XYZ")]
    public class Vector3_FromXYZ_Node : Node
    {
        public override string Title => "Vec3 From XYZ";
        public override float Width => 75;

        [Input] public double X;
        [Input] public double Y;
        [Input] public double Z;

        [Output, SerializeIgnore] public Vector3 Vector;

        public override object GetValue(NodePort port) => new Vector3(GetInputValue("X", X), GetInputValue("Y", Y), GetInputValue("Z", Z));
    }

    [Node("Operations/Vector3/To XYZ")]
    public class Vector3_ToXYZ_Node : Node
    {
        public override string Title => "Vec3 To XYZ";
        public override float Width => 75;

        [Input] public Vector3 Vector;

        [Output, SerializeIgnore] public double X;
        [Output, SerializeIgnore] public double Y;
        [Output, SerializeIgnore] public double Z;

        public override object GetValue(NodePort port)
        {
            if (port.fieldName == nameof(X))
                return GetInputValue("Vector", Vector).x;
            if (port.fieldName == nameof(Y))
                return GetInputValue("Vector", Vector).y;
            return GetInputValue("Vector", Vector).z;
        }
    }
}
