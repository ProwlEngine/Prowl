using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.NodeSystem
{
    public abstract class OperatorNode : Node
    {
        public abstract string MethodName { get; }

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.AssignableTo), SerializeIgnore] public object A;

        public override void OnValidate()
        {
            Error = "";

            var aPort = GetPort("A");
            if (!aPort.IsConnected || aPort.Connection.ValueType == null)
            {
                ClearDynamicPorts();
                Error = !aPort.IsConnected ? "A is not connected" : "A's ValueType is Null?";
                return;
            }

            // Check if A type has a + operator, if not clear dynamic ports and return
            MethodInfo mi = null;
            foreach (var method in aPort.Connection.ValueType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name.Contains(MethodName))
                    if (method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType == aPort.Connection.ValueType)
                    {
                        // Has operator and first value is the same type as A
                        mi = method;
                        break;
                    }
            //MethodInfo mi = aPort.Connection.ValueType.GetMethod(MethodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null || mi.GetParameters().Length != 2)
            {
                ClearDynamicPorts();
                Error = $"Type: {aPort.Connection.ValueType} has no {Title}({aPort.Connection.ValueType.Name}, {aPort.Connection.ValueType.Name}) Operator";
                return;
            }

            if (DynamicInputs.Count() > 0 && aPort.Connection.ValueType != DynamicInputs.ElementAt(0).ValueType)
                ClearDynamicPorts();

            if (DynamicInputs.Count() <= 0)
            {
                AddDynamicInput(mi.GetParameters()[1].ParameterType, ConnectionType.Override, TypeConstraint.AssignableTo, "B");
                AddDynamicOutput(mi.ReturnType, ConnectionType.Multiple, TypeConstraint.None, "Result");
            }
        }

        public override object GetValue(NodePort port)
        {
            var aPort = GetPort("A");
            if (!aPort.IsConnected || aPort.Connection.ValueType == null)
                return null;
            var bPort = GetPort("B");
            if (!bPort.IsConnected || bPort.Connection.ValueType != aPort.Connection.ValueType)
                return null;

            MethodInfo mi = aPort.Connection.ValueType.GetMethod(MethodName, BindingFlags.Static | BindingFlags.Public);
            return mi.Invoke(null, new object[] { GetInputValue("A", A), GetInputValue<object>("B") });
        }
    }

    [Node("General/Add")]
    public class AddNode : OperatorNode
    {
        public override string Title => "Add";
        public override string MethodName => "op_Addition";
        public override float Width => 75;
    }

    [Node("General/Subtract")]
    public class SubtractNode : OperatorNode
    {
        public override string Title => "Subtract";
        public override string MethodName => "op_Subtraction";
        public override float Width => 75;
    }

    [Node("General/Multiply")]
    public class MultiplyNode : OperatorNode
    {
        public override string Title => "Multiply";
        public override string MethodName => "op_Multiply";
        public override float Width => 75;
    }

    [Node("General/Divide")]
    public class DivideNode : OperatorNode
    {
        public override string Title => "Divide";
        public override string MethodName => "op_Division";
        public override float Width => 75;
    }

    [Node("General/Modulus")]
    public class ModulusNode : OperatorNode
    {
        public override string Title => "Modulus";
        public override string MethodName => "op_Modulus";
        public override float Width => 75;
    }

    [Node("General/Equals")]
    public class EqualsNode : OperatorNode
    {
        public override string Title => "Equals";
        public override string MethodName => "op_Equality";
        public override float Width => 75;
    }

    [Node("General/Not Equals")]
    public class NotEqualsNode : OperatorNode
    {
        public override string Title => "Not Equals";
        public override string MethodName => "op_Inequality";
        public override float Width => 75;
    }

    [Node("General/Greater Than")]
    public class GreaterThanNode : OperatorNode
    {
        public override string Title => "Greater Than";
        public override string MethodName => "op_GreaterThan";
        public override float Width => 100;
    }

    [Node("General/Less Than")]
    public class LessThanNode : OperatorNode
    {
        public override string Title => "Less Than";
        public override string MethodName => "op_LessThan";
        public override float Width => 75;
    }

    [Node("General/Greater Than or Equal")]
    public class GreaterThanOrEqualNode : OperatorNode
    {
        public override string Title => "Greater Than or Equal";
        public override string MethodName => "op_GreaterThanOrEqual";
        public override float Width => 125;
    }

    [Node("General/Less Than or Equal")]
    public class LessThanOrEqualNode : OperatorNode
    {
        public override string Title => "Less Than or Equal";
        public override string MethodName => "op_LessThanOrEqual";
        public override float Width => 125;
    }

    [Node("General/Increment")]
    public class IncrementNode : OperatorNode
    {
        public override string Title => "Increment";
        public override string MethodName => "op_Increment";
        public override float Width => 100;
    }

    [Node("General/Decrement")]
    public class DecrementNode : OperatorNode
    {
        public override string Title => "Decrement";
        public override string MethodName => "op_Decrement";
        public override float Width => 100;
    }

}
