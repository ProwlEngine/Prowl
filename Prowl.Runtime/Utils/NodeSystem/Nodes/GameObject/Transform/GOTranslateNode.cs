// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("GameObject/Transform/Translate")]
public class GOTranslateNode : InOutFlowNode
{
    public override bool ShowTitle => true;
    public override string Title => "Translate";
    public override float Width => 100;

    [Input(ShowBackingValue.Never)] public GameObject Target;
    [Input] public Vector3 Translation;
    [Input(ShowBackingValue.Never)] public GameObject RelativeTo;

    public override void Execute(NodePort input)
    {
        GameObject t = GetInputValue("Target", Target);
        Vector3 translation = GetInputValue("Translation", Translation);
        GameObject relativeTo = GetInputValue("RelativeTo", RelativeTo);

        if (t != null)
            t.Transform.Translate(translation, relativeTo?.Transform);

        ExecuteNext();
    }
}
