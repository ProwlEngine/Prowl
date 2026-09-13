// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Navigation;

namespace Prowl.Editor.Inspector;

/// <summary>
/// The agent-type dropdown <see cref="NavMeshSurfaceEditor"/> and <see cref="NavMeshAgentEditor"/> both
/// draw in place of the raw <c>AgentTypeId</c> int the default inspector would otherwise show (see its
/// <c>[HideInInspector]</c> on both components). Rebuilds its options from
/// <see cref="NavMeshAgentTypes.Types"/> on every draw, the same "read the mutable registry live"
/// approach the engine's own Tag/Layer dropdowns use, so a type renamed or added in the Agents project
/// settings page shows up immediately without either inspector needing to know that happened.
/// </summary>
internal static class NavMeshAgentTypeDropdown
{
    /// <summary>Draws a labeled row with the dropdown. <paramref name="onChange"/> receives the newly
    /// picked type's id.</summary>
    internal static void Draw(Paper paper, string id, Prowl.Scribe.FontFile font, int currentTypeId, Action<int> onChange)
    {
        var ids = new List<int>(NavMeshAgentTypes.Types.Count);
        foreach (NavMeshAgentTypeInfo type in NavMeshAgentTypes.Types)
            ids.Add(type.Id);

        using (paper.Row($"{id}_row").Height(EditorTheme.RowHeight).Gap(6).Enter())
        {
            paper.Box($"{id}_label").Width(90)
                .Text("Agent Type", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            using (paper.Box($"{id}_dd_box").Width(UnitValue.Stretch()).Height(22).Enter())
            {
                Origami.Dropdown(paper, $"{id}_dd", currentTypeId, onChange, ids)
                    .Display(v => NavMeshAgentTypes.GetById(v)?.Name ?? $"Unknown (#{v})")
                    .Show();
            }
        }
    }
}
