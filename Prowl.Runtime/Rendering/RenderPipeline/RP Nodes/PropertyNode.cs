using Prowl.Runtime.NodeSystem;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/State/SetBuffer")]
    public class SetBuffer_PropertyNode : InOutFlowNode
    {
        public override string Title => "Set Buffer";
        public override float Width => 75;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.AssignableTo), SerializeIgnore] public ComputeBuffer Buffer;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public string Name;

        public override void Execute(NodePort input)
        {
            var val = GetInputValue<ComputeBuffer>("Buffer");
            if (val == null) throw new System.Exception("Buffer is null");

            var name = GetInputValue<string>("Name", Name);
            if (name == null) throw new System.Exception("Name is null");

            (graph as RenderPipeline).Context.SetBuffer(name, val);

            ExecuteNext();
        }
    }

    [Node("Rendering/State/SetColor")]
    public class SetColor_PropertyNode : InOutFlowNode
    {
        public override string Title => "Set Color";
        public override float Width => 75;

        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public Color Color;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public string Name;

        public override void Execute(NodePort input)
        {
            var val = GetInputValue<Color>("Color", Color);
            var name = GetInputValue<string>("Name", Name);
            if (name == null) throw new System.Exception("Name is null");

            (graph as RenderPipeline).Context.SetColor(name, val);

            ExecuteNext();
        }
    }

    [Node("Rendering/State/SetFloat")]
    public class SetFloat_PropertyNode : InOutFlowNode
    {
        public override string Title => "Set Float";
        public override float Width => 75;

        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public double Value;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public string Name;

        public override void Execute(NodePort input)
        {
            var val = GetInputValue<double>("Value", Value);
            var name = GetInputValue<string>("Name",Name);
            if (name == null) throw new System.Exception("Name is null");

            (graph as RenderPipeline).Context.SetFloat(name, (float)val);

            ExecuteNext();
        }
    }

    [Node("Rendering/State/SetMatrix")]
    public class SetMatrix_PropertyNode : InOutFlowNode
    {
        public override string Title => "Set Matrix";
        public override float Width => 75;

        [Input(ShowBackingValue.Never, ConnectionType.Override), SerializeIgnore] public Matrix4x4 Matrix;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public string Name;

        public override void Execute(NodePort input)
        {
            var val = GetInputValue<Matrix4x4>("Matrix");
            var name = GetInputValue<string>("Name", Name);
            if (name == null) throw new System.Exception("Name is null");

            (graph as RenderPipeline).Context.SetMatrix(name, val);

            ExecuteNext();
        }
    }

    [Node("Rendering/State/SetTexture")]
    public class SetTexture_PropertyNode : InOutFlowNode
    {
        public override string Title => "Set Texture";
        public override float Width => 75;

        [Input(ShowBackingValue.Never, ConnectionType.Override), SerializeIgnore] public Texture2D Texture;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public string Name;

        public override void Execute(NodePort input)
        {
            var val = GetInputValue<Texture2D>("Texture", Texture);
            if (val == null) throw new System.Exception("Texture is null");
            var name = GetInputValue<string>("Name", Name);
            if (name == null) throw new System.Exception("Name is null");

            (graph as RenderPipeline).Context.SetTexture(name, val);

            ExecuteNext();
        }
    }

    [Node("Rendering/State/SetVector")]
    public class SetVector_PropertyNode : InOutFlowNode
    {
        public override string Title => "Set Vector";
        public override float Width => 75;

        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public Vector4 Vector;
        [Input(ShowBackingValue.Unconnected, ConnectionType.Override)] public string Name;

        public override void Execute(NodePort input)
        {
            var val = GetInputValue<Vector4>("Vector", Vector);
            var name = GetInputValue<string>("Name", Name);
            if (name == null) throw new System.Exception("Name is null");

            (graph as RenderPipeline).Context.SetVector(name, val);

            ExecuteNext();
        }
    }
}
