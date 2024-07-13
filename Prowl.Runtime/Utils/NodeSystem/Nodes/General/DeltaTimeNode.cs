namespace Prowl.Runtime.NodeSystem
{
    [Node("General")]
    public class DeltaTimeNode : Node
    {
        public override bool ShowTitle => false;
        public override string Title => "Delta Time";
        public override float Width => 100;

        [Output, SerializeIgnore] public double Time;

        public override object GetValue(NodePort port) => Runtime.Time.deltaTime;
    }
}
