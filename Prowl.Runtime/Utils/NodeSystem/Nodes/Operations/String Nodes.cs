// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.NodeSystem;

[Node("Operations/String/Concat")]
public class String_Concat_Node : Node
{
    public override string Title => "String Concat";
    public override float Width => 75;

    [Input] public string A;
    [Input] public string B;

    [Output, SerializeIgnore] public string Sum;

    public override object GetValue(NodePort port) => GetInputValue("A", A) + GetInputValue("B", B);
}

[Node("Operations/String/Replace")]
public class String_Replace_Node : Node
{
    public override string Title => "String Replace";
    public override float Width => 75;

    [Input] public string Replace;
    [Input] public string With;
    [Input] public string In;

    [Output, SerializeIgnore] public string Result;

    public override object GetValue(NodePort port) => GetInputValue("In", In).Replace(GetInputValue("Replace", Replace), GetInputValue("With", With));
}

[Node("Operations/String/Contains")]
public class String_Contains_Node : Node
{
    public override string Title => "String Contains";
    public override float Width => 75;

    [Input] public string Has;
    [Input] public string In;

    [Output, SerializeIgnore] public bool Contains;

    public readonly System.StringComparison Mode = System.StringComparison.Ordinal;

    public override object GetValue(NodePort port) => GetInputValue("In", In).Contains(GetInputValue("Has", Has), Mode);
}

[Node("Operations/String/Starts With")]
public class String_StartsWith_Node : Node
{
    public override string Title => "String Starts With";
    public override float Width => 75;

    [Input] public string StartsWith;
    [Input] public string In;

    [Output, SerializeIgnore] public string Result;

    public readonly System.StringComparison Mode = System.StringComparison.Ordinal;

    public override object GetValue(NodePort port) => GetInputValue("In", In).StartsWith(GetInputValue("StartsWith", StartsWith), Mode);
}

[Node("Operations/String/Ends With")]
public class String_EndsWith_Node : Node
{
    public override string Title => "String Ends With";
    public override float Width => 75;

    [Input] public string EndsWith;
    [Input] public string In;

    [Output, SerializeIgnore] public string Result;

    public readonly System.StringComparison Mode = System.StringComparison.Ordinal;

    public override object GetValue(NodePort port) => GetInputValue("In", In).EndsWith(GetInputValue("EndsWith", EndsWith), Mode);
}

[Node("Operations/String/Length")]
public class String_Length_Node : Node
{
    public override string Title => "String Length";
    public override float Width => 75;

    [Input] public string In;

    [Output, SerializeIgnore] public int Length;

    public override object GetValue(NodePort port) => GetInputValue("In", In).Length;
}

[Node("Operations/String/To Upper")]
public class String_ToUpper_Node : Node
{
    public override string Title => "String To Upper";
    public override float Width => 75;

    [Input] public string In;

    [Output, SerializeIgnore] public string Upper;

    public override object GetValue(NodePort port) => GetInputValue("In", In).ToUpper();
}

[Node("Operations/String/To Lower")]
public class String_ToLower_Node : Node
{
    public override string Title => "String To Lower";
    public override float Width => 75;

    [Input] public string In;

    [Output, SerializeIgnore] public string Lower;

    public override object GetValue(NodePort port) => GetInputValue("In", In).ToLower();
}

[Node("Operations/String/Trim")]
public class String_Trim_Node : Node
{
    public override string Title => "String Trim";
    public override float Width => 75;

    [Input] public string In;

    [Output, SerializeIgnore] public string Trimmed;

    public override object GetValue(NodePort port) => GetInputValue("In", In).Trim();
}

[Node("Operations/String/Sub String")]
public class String_SubString_Node : Node
{
    public override string Title => "String Sub String";
    public override float Width => 75;

    [Input] public string In;
    [Input] public int Start;
    [Input] public int Length;

    [Output, SerializeIgnore] public string SubString;

    public override object GetValue(NodePort port) => GetInputValue("In", In).Substring(GetInputValue("Start", Start), GetInputValue("Length", Length));
}

[Node("Operations/String/Is Null Or White Space")]
public class String_IsNullOrWhiteSpace_Node : Node
{
    public override string Title => "String Is Null Or White Space";
    public override float Width => 75;

    [Input] public string In;

    [Output, SerializeIgnore] public bool IsNullOrWhiteSpace;

    public override object GetValue(NodePort port) => string.IsNullOrWhiteSpace(GetInputValue("In", In));
}

[Node("Operations/String/Last Index Of")]
public class String_LastIndexOf_Node : Node
{
    public override string Title => "String Last Index Of";
    public override float Width => 75;

    [Input] public string In;
    [Input] public string Find;

    [Output, SerializeIgnore] public int Index;

    public override object GetValue(NodePort port) => GetInputValue("In", In).LastIndexOf(GetInputValue("Find", Find));
}

[Node("Operations/String/First Index Of")]
public class String_IndexOf_Node : Node
{
    public override string Title => "String Index Of";
    public override float Width => 75;

    [Input] public string In;
    [Input] public string Find;

    [Output, SerializeIgnore] public int Index;

    public override object GetValue(NodePort port) => GetInputValue("In", In).IndexOf(GetInputValue("Find", Find));
}

[Node("Operations/String/Is Equal")]
public class String_IsEqual_Node : Node
{
    public override string Title => "String Is Equal";
    public override float Width => 75;

    [Input] public string A;
    [Input] public string B;

    [Output, SerializeIgnore] public bool IsEqual;

    public readonly System.StringComparison Mode = System.StringComparison.Ordinal;

    public override object GetValue(NodePort port) => GetInputValue("A", A).Equals(GetInputValue("B", B), Mode);
}
