// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Editor.Assets;

using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor;

[CustomEditor(typeof(MonoBehaviour))]
public class ComponentEditor : ScriptedEditor
{
    private MonoBehaviour? _component;


    public override void OnEnable()
    {
        _component = target as MonoBehaviour;
    }


    public override void OnInspectorGUI()
    {
        if (_component == null)
            return;

        object componentRef = _component;
        if (PropertyGrid("ComponentPropertyGrid", ref componentRef, TargetFields.Serializable | TargetFields.Properties, PropertyGridConfig.NoHeader | PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground))
            _component.OnValidate();
    }
}
