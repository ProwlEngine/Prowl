using System;

namespace Prowl.Editor.GUI.SceneView;

[AttributeUsage(AttributeTargets.Class)]
public class SceneViewEditorForAttribute : Attribute
{
    public Type ComponentType { get; }
    public SceneViewEditorForAttribute(Type componentType) => ComponentType = componentType;
}
