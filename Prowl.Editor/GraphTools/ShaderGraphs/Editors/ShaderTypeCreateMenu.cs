// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.GraphTools.ShaderGraphs;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors;

/// <summary>
/// Populates the Create menu from every <see cref="IShaderType"/> in the registry —
/// each type contributes its own list of <see cref="ShaderTypeMenuEntry"/> rows.
/// Called at editor startup and again after user-script recompile so newly-added
/// types show up without an editor restart.
/// </summary>
public static class ShaderTypeCreateMenu
{
    public const string ShaderGraphExtension = ".shadergraph";
    private const string MenuPrefix = "Shader Graph/";

    public static void Register()
    {
        // Rescan so plugin-defined types picked up after a recompile are visible.
        ShaderTypeRegistry.Reinitialize();

        CreateAssetMenuRegistry.RemoveManualEntriesByPrefix(MenuPrefix);

        foreach (var type in ShaderTypeRegistry.All)
        {
            foreach (var entry in type.MenuEntries)
            {
                // Capture the variant key per iteration stable, unique, survives
                // class rename. Closure over the type + key produces a fresh graph
                // each time the user clicks the menu item.
                var capturedType = type;
                var capturedKey  = entry.VariantKey;

                CreateAssetMenuRegistry.AddManualEntry(
                    entry.MenuPath,
                    ShaderGraphExtension,
                    icon: "",
                    order: entry.Order,
                    typeof(ShaderGraph),
                    () => BuildGraph(capturedType, capturedKey));
            }
        }
    }

    private static EngineObject BuildGraph(IShaderType type, string variantKey)
    {
        var graph = new ShaderGraph();
        type.SeedGraph(graph, variantKey);
        return graph;
    }
}
