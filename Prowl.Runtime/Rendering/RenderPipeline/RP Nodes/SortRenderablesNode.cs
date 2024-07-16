using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;

namespace Prowl.Runtime.RenderPipelines
{
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
