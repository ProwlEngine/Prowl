using Prowl.Runtime.NodeSystem;
using System.Linq;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering")]
    public class PropertyStateNode : Node
    {
        public override string Title => "Property State";
        public override float Width => 175;


        [Output, SerializeIgnore] public PropertyState PropertyState;

        public override void OnValidate()
        {
            // remove any empty inputs
            foreach (var input in DynamicInputs.ToArray())
                if (!input.IsConnected)
                    RemoveDynamicPort(input);

            if (DynamicInputs.Count() == 0)
                AddDynamicInput(typeof(PropertyNode.NodeProperty), ConnectionType.Override, TypeConstraint.Strict, "Property 1");

            // if all inputs are connected, add another one
            if (DynamicInputs.All(p => p.IsConnected))
                AddDynamicInput(typeof(PropertyNode.NodeProperty), ConnectionType.Override, TypeConstraint.Strict, $"Property {DynamicInputs.Count() + 1}");
        }

        public override object GetValue(NodePort port)
        {
            PropertyState state = new();
            foreach (var input in DynamicInputs)
            {
                if (!input.IsConnected) continue;

                var prop = input.GetInputValue<PropertyNode.NodeProperty>();
                if(prop == null)
                {
                    Error = "Property is null";
                    return null;
                }
                else if(prop.Value is double d)
                    state.SetFloat(prop.Name, (float)d);
                else if(prop.Value is float f)
                    state.SetFloat(prop.Name, f);
                else if(prop.Value is int i)
                    state.SetInt(prop.Name, i);
                else if(prop.Value is Texture2D tex)
                    state.SetTexture(prop.Name, tex);
                else if(prop.Value is Vector2 vec2)
                    state.SetVector(prop.Name, vec2);
                else if(prop.Value is Vector3 vec3)
                    state.SetVector(prop.Name, vec3);
                else if(prop.Value is Vector4 vec4)
                    state.SetVector(prop.Name, vec4);
                else if(prop.Value is Color col)
                    state.SetColor(prop.Name, col);
                else
                    throw new System.Exception($"Unsupported type: {prop.Value.GetType()}");
            }

            return state;
        }
    }

    [Node("Rendering")]
    public class ExperimentPropertyStateNode : Node
    {
        public override string Title => "Experiment Property State";
        public override float Width => 175;

        [Input(dynamicPortList = true), SerializeField] public PropertyNode[] Properties;

        [Output, SerializeIgnore] public PropertyState PropertyState;

        public override object GetValue(NodePort port)
        {
            PropertyState state = new();
            foreach (var input in DynamicInputs)
            {
                if (!input.IsConnected) continue;

                var prop = input.GetInputValue<PropertyNode.NodeProperty>();
                if(prop.Value is double d)
                    state.SetFloat(prop.Name, (float)d);
                else if(prop.Value is float f)
                    state.SetFloat(prop.Name, f);
                else if(prop.Value is int i)
                    state.SetInt(prop.Name, i);
                else if(prop.Value is Texture2D tex)
                    state.SetTexture(prop.Name, tex);
                else if(prop.Value is Vector2 vec2)
                    state.SetVector(prop.Name, vec2);
                else if(prop.Value is Vector3 vec3)
                    state.SetVector(prop.Name, vec3);
                else if(prop.Value is Vector4 vec4)
                    state.SetVector(prop.Name, vec4);
                else if(prop.Value is Color col)
                    state.SetColor(prop.Name, col);
                else
                    throw new System.Exception($"Unsupported type: {prop.Value.GetType()}");
            }

            return state;
        }
    }
}
