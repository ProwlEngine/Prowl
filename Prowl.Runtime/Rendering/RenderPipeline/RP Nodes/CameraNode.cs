using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime.RenderPipelines
{
    public abstract class FlowNode : Node
    {
        public void ExecuteNext(string port = "To")
        {
            var next = GetOutputPort("To").ConnectedNode as FlowNode;
            next?.Execute();
        }

        public abstract void Execute();
    }

    public abstract class InFlowNode : FlowNode
    {
        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore] 
        public FlowNode From;
    }

    public abstract class InOutFlowNode : FlowNode
    {
        [Output(ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore]
        public FlowNode To;
        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore] 
        public FlowNode From;
    }

    public abstract class OutFlowNode : FlowNode
    {
        [Output(ConnectionType.Override, TypeConstraint.Strict, true), SerializeIgnore]
        public FlowNode To;
    }

    [Node("Rendering")]
    public class OnPipelineNode : OutFlowNode
    {
        public override string Title => "On Pipeline";
        public override float Width => 150;

        [Tooltip("Default Pipelines are 'Main' & 'Shadow'")]
        public string Name = "Main";

        public override void Execute()
        {
            ExecuteNext();
        }
    }

    [Node("Rendering")]
    public class DrawRenderablesNode : InOutFlowNode
    {
        public override string Title => "Draw Renderables";
        public override float Width => 215;

        [Input, SerializeIgnore] public List<Renderable> Renderables;
        [Input, SerializeIgnore] public NodeRenderTexture Target;
        [Input, SerializeIgnore] public PropertyState Property;

        public string ShaderTag = "Opaque";
        public AssetRef<Material> Material;
        public AssetRef<Material> Fallback;

        public override void Execute()
        {
            var renderables = GetInputValue<List<Renderable>>("Renderables");
            var target = GetInputValue<NodeRenderTexture>("Target");
            var property = GetInputValue<PropertyState>("Property");

            CommandBuffer cmd = CommandBufferPool.Get("Draw Renderables");
            cmd.SetRenderTarget(target.RenderTexture);

            if (property != null)
                cmd.ApplyPropertyState(property);

            // Draw renderables
            (graph as RenderPipeline).Context.DrawRenderers(cmd, renderables, new(ShaderTag, Material.Res, Fallback.Res), (graph as RenderPipeline).CurrentCamera.LayerMask);

            (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);

            ExecuteNext();
        }
    }

    [Node("Rendering")]
    public class BlitMaterialNode : InOutFlowNode
    {
        public override string Title => "Blit Material";
        public override float Width => 215;
        
        [Input, SerializeIgnore] public NodeRenderTexture Target;
        [Input, SerializeIgnore] public AssetRef<Material> Material;
        [Input, SerializeIgnore] public PropertyState Property;

        public override void Execute()
        {
            var target = GetInputValue<NodeRenderTexture>("Target");
            var material = GetInputValue<AssetRef<Material>>("Material");
            var property = GetInputValue<PropertyState>("Property");

            if(target == null)
            {
                Error = "Target is null!";
                return;
            }

            if (material.IsAvailable)
            {
                CommandBuffer cmd = CommandBufferPool.Get(Title);
                cmd.SetRenderTarget(target.RenderTexture);
                if (property != null)
                    cmd.ApplyPropertyState(property);
                cmd.SetMaterial(material.Res, 0);
                cmd.DrawSingle(Mesh.GetFullscreenQuad());

                (graph as RenderPipeline).Context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }
            else
            {
                Error = "Material is not available!";
            }

            ExecuteNext();
        }
    }

    [Node("Rendering")]
    public class MaterialFromShaderNode : Node
    {
        public override string Title => "Create Material";
        public override float Width => 215;
        [Output, SerializeIgnore] public AssetRef<Material> Material;

        public string ShaderName = "Defaults/Blit";

        [SerializeIgnore] AssetRef<Material> savedMat;
        [SerializeIgnore] string savedName = "";

        public override object GetValue(NodePort port)
        {
            if(savedName == ShaderName)
                return savedMat;

            savedMat = new AssetRef<Material>(new Material(Application.AssetProvider.LoadAsset<Shader>(ShaderName + ".shader")));
            savedName = ShaderName;
            return savedMat;
        }
    }

    [Node("Rendering")]
    public class TargetCameraNode : Node
    {
        public override string Title => "Target Camera";
        public override float Width => 100;
        [Output, SerializeIgnore] public Camera.CameraData Camera;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).CurrentCamera;
    }

    [Node("Rendering")]
    public class TargetResolutionNode : Node
    {
        public override string Title => "Target Resolution";
        public override float Width => 100;
        [Output, SerializeIgnore] public Vector2 Resolution;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).Resolution;
    }

    [Node("Rendering")]
    public class TargetNode : Node
    {
        public override string Title => "Target";
        public override float Width => 100;
        [Output, SerializeIgnore] public NodeRenderTexture Target;
        public override object GetValue(NodePort port) => (graph as RenderPipeline).Target;
    }

    [Node("Rendering")]
    public class PropertyNode : Node
    {
        public class NodeProperty
        {
            public string Name;
            public object Value;
        }

        public override string Title => "Property";
        public override float Width => 150;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.AssignableTo), SerializeIgnore] public object Value;

        [Output, SerializeIgnore] public NodeProperty Property;

        public string Name;

        public override object GetValue(NodePort port)
        {
            var val = GetInputValue<object>("Value");
            if (val == null) throw new System.Exception("[PropertyNode] Value is null");
            
            return new NodeProperty { Name = Name, Value = val };
        }
    }

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

    [Node("Rendering")]
    public class GetRTTextureNode : Node
    {
        public override string Title => "Get RT Texture";
        public override float Width => 150;

        [Input, SerializeIgnore] public NodeRenderTexture RT;

        [Output, SerializeIgnore] public Texture2D Texture;

        public RTBuffer.Type Type = RTBuffer.Type.Color;

        public override object GetValue(NodePort port)
        {
            var rt = GetInputValue<NodeRenderTexture>("RT");
            if (rt == null) return null;

            Error = "";
            if (rt.TargetOnly)
            {
                Error = "RenderTexture is Target Only Cannot Access Buffers!";
                return null;
            }

            return rt.GetTexture(Type);
        }
    }

    [Node("Rendering")]
    public class SplitRenderTextureNode : Node
    {
        public override string Title => "Split RenderTexture";
        public override float Width => 150;

        [Input, SerializeIgnore] public NodeRenderTexture RT;

        [Output, SerializeIgnore] public Texture2D Color;
        [Output, SerializeIgnore] public Texture2D Normals;
        [Output, SerializeIgnore] public Texture2D Position;
        [Output, SerializeIgnore] public Texture2D Surface;
        [Output, SerializeIgnore] public Texture2D Emissive;
        [Output, SerializeIgnore] public Texture2D ObjectID;
        [Output, SerializeIgnore] public Texture2D Custom;
        [Output, SerializeIgnore] public Texture2D Depth;

        public override object GetValue(NodePort port)
        {
            var rt = GetInputValue<NodeRenderTexture>("RT");
            if (rt == null) return null;

            Error = "";
            if (rt.TargetOnly)
            {
                Error = "RenderTexture is Target Only Cannot Access Buffers!";
                return null;
            }


            if (port.fieldName == nameof(Color))
                return rt.GetTexture(RTBuffer.Type.Color);

            if (port.fieldName == nameof(Normals))
                return rt.GetTexture(RTBuffer.Type.Normals);

            if (port.fieldName == nameof(Position))
                return rt.GetTexture(RTBuffer.Type.Position);

            if (port.fieldName == nameof(Surface))
                return rt.GetTexture(RTBuffer.Type.Surface);

            if (port.fieldName == nameof(Emissive))
                return rt.GetTexture(RTBuffer.Type.Emissive);

            if (port.fieldName == nameof(ObjectID))
                return rt.GetTexture(RTBuffer.Type.ObjectID);

            if (port.fieldName == nameof(Custom))
                return rt.GetTexture(RTBuffer.Type.Custom);

            if (port.fieldName == nameof(Depth))
                return rt.RenderTexture.DepthBuffer;

            throw new System.Exception("Output port not found");
        }
    }

    [Node("Rendering")]
    public class GetRenderablesNode : Node
    {
        public override string Title => "Get Renderables";
        public override float Width => 150;

        [Input, SerializeIgnore] public Camera.CameraData Camera;

        [Output, SerializeIgnore] public List<Renderable> Renderables;

        public override object GetValue(NodePort port)
        {
            var cam = GetInputValue<Camera.CameraData>("Camera");

            // Get the culling parameters from the current Camera
            var camFrustrum = cam.GetFrustrum((uint)(graph as RenderPipeline).Resolution.x, (uint)(graph as RenderPipeline).Resolution.y);

            // Use the culling parameters to perform a cull operation, and store the results
            var cullingResults = (graph as RenderPipeline).Context.Cull(camFrustrum);

            return cullingResults ?? new List<Renderable>();
        }
    }

    [Node("Rendering")]
    public class SortRenderablesNode : Node
    {
        public override string Title => "Sort Renderables";
        public override float Width => 200;

        [Input, SerializeIgnore] public List<Renderable> Renderables;

        [Output, SerializeIgnore] public List<Renderable> Sorted;

        public SortMode SortMode = SortMode.FrontToBack;

        public override object GetValue(NodePort port)
        {
            var renderables = GetInputValue<List<Renderable>>("Renderables");

            // Sort renderables
            SortedList<double, List<Renderable>> sorted = (graph as RenderPipeline).Context.SortRenderables(renderables, SortMode);

            // pack them into 1 sorted list
            List<Renderable> packed = new();
            foreach (var kvp in sorted)
                packed.AddRange(kvp.Value);

            return packed;
        }
    }
}
