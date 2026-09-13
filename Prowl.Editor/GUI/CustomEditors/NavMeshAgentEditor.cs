// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Navigation;

namespace Prowl.Editor.Inspector;

/// <summary>Inspector for <see cref="NavMeshAgent"/>. Only exists to swap the raw <c>AgentTypeId</c>
/// int the default inspector would otherwise draw for a named dropdown - see
/// <see cref="NavMeshAgentTypeDropdown"/>.</summary>
[CustomEditor(typeof(NavMeshAgent))]
public class NavMeshAgentEditor : CustomEditor
{
    /// <summary>Draws the agent-type dropdown followed by the default inspector.</summary>
    public override void OnGUI(Paper paper, string id, object target)
    {
        var agent = (NavMeshAgent)target;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        NavMeshAgentTypeDropdown.Draw(paper, $"{id}_type", font, agent.AgentTypeId, v =>
        {
            Undo.Snapshot(agent);
            agent.AgentTypeId = v;
        });

        DrawDefaultInspector(paper, $"{id}_def", target);
    }
}
