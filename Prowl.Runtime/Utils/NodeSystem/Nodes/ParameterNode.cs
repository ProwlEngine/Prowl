// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

namespace Prowl.Runtime.NodeSystem
{
    public class ParameterNode : Node
    {
        public override bool ShowTitle => false;
        public override string Title => "ParameterNode";
        public override float Width => 200;

        public string Parameter;

        public override void OnValidate()
        {
            var param = graph.parameters.FirstOrDefault(p => string.Equals(p.name, Parameter, System.StringComparison.OrdinalIgnoreCase));
            if (param == null)
            {
                ClearDynamicPorts();
                return;
            }

            var paramType = param.type switch
            {
                GraphParameter.ParameterType.Int => typeof(int),
                GraphParameter.ParameterType.Double => typeof(double),
                GraphParameter.ParameterType.Bool => typeof(bool),
                GraphParameter.ParameterType.Texture => typeof(Texture),
                GraphParameter.ParameterType.Material => typeof(Material),
                _ => null
            };

            if (DynamicOutputs.Count() != 1 || paramType != DynamicOutputs.ElementAt(0).ValueType)
            {
                ClearDynamicPorts();
                AddDynamicOutput(paramType, ConnectionType.Multiple, TypeConstraint.None, param.name);
            }
        }

        public override object GetValue(NodePort port)
        {
            var param = graph.parameters.FirstOrDefault(p => p.name.Equals(Parameter, System.StringComparison.OrdinalIgnoreCase));
            if (param == null)
                return null;

            switch (param.type)
            {
                case GraphParameter.ParameterType.Int:
                    return param.intVal;
                case GraphParameter.ParameterType.Double:
                    return param.doubleVal;
                case GraphParameter.ParameterType.Bool:
                    return param.boolVal;
                case GraphParameter.ParameterType.Texture:
                    return param.textureRef;
                case GraphParameter.ParameterType.Material:
                    return param.materialRef;
                default:
                    return null;
            }
        }
    }
}
