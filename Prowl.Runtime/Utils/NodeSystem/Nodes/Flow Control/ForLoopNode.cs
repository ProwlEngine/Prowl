﻿namespace Prowl.Runtime.NodeSystem
{
    [Node("Flow Control/For Loop")]
    public class ForLoopNode : InOutFlowNode
    {
        public override string Title => "For Loop";
        public override float Width => 170;

        [Input(ShowBackingValue.Never, ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Break;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode Completed;

        [Output, SerializeIgnore] public int Index;

        [Input] public int FirstIndex = 0;
        [Input] public int LastIndex = 10;

        private int currentIndex;

        public override void Execute(NodePort input)
        {
            if (input.fieldName == nameof(Break))
            {
                ExecuteNext(nameof(Completed));
                return;
            }

            var index = GetInputValue<int>("FirstIndex", FirstIndex);
            var lastIndex = GetInputValue<int>("LastIndex", LastIndex);

            for (int i = index; i < lastIndex; i++)
            {
                currentIndex = i;
                ExecuteNext();
            }

            ExecuteNext(nameof(Completed));
        }

        public override object GetValue(NodePort port) => currentIndex;
    }
}