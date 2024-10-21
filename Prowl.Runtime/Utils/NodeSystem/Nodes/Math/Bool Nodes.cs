// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Math/Boolean/AND")]
public class BoolANDNode : Node
{
    public override string Title => "A && B";
    public override float Width => 75;

    [Input] public bool A;
    [Input] public bool B;

    [Output, SerializeIgnore] public bool And;

    public override object GetValue(NodePort port) => GetInputValue("A", A) && GetInputValue("B", B);
}

[Node("Math/Boolean/OR")]
public class BoolORNode : Node
{
    public override string Title => "A || B";
    public override float Width => 75;

    [Input] public bool A;
    [Input] public bool B;

    [Output, SerializeIgnore] public bool Or;

    public override object GetValue(NodePort port) => GetInputValue("A", A) || GetInputValue("B", B);
}

[Node("Math/Boolean/Invert")]
public class BoolInvertNode : Node
{
    public override string Title => "!A";
    public override float Width => 75;

    [Input] public bool A;

    [Output, SerializeIgnore] public bool Inverted;

    public override object GetValue(NodePort port) => !GetInputValue("A", A);
}
