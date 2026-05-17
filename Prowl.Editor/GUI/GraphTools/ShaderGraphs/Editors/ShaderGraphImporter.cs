// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools;
using Prowl.Runtime.GraphTools.ShaderGraphs;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.GraphTools.ShaderGraphs.Editors;

/// <summary>
/// Asset importer for <c>.shadergraph</c> files. Loads the Echo-serialized graph as
/// the main asset, runs <see cref="ShaderGraphCompiler"/> to generate a Prowl
/// <c>.shader</c> source string, parses that into a <see cref="Shader"/> runtime
/// object, and registers it as a sub-asset. Materials reference the sub-asset
/// directly via AssetRef&lt;Shader&gt;.
/// </summary>
[ImporterFor(".shadergraph")]
public sealed class ShaderGraphImporter : AssetImporter
{
    public override int Version => 2;

    public override bool Import(ImportContext ctx)
    {
        // Belt-and-braces: stdout AND Debug.LogError so the trace shows up regardless
        // of how the editor is wired up to the console panel.
        System.Console.WriteLine($"[ShaderGraphImporter] === ENTERED Import('{ctx.AbsolutePath}') ===");
        Prowl.Runtime.Debug.LogError($"[ShaderGraphImporter] === ENTERED Import('{ctx.AbsolutePath}') ===");

        string text;
        try { text = File.ReadAllText(ctx.AbsolutePath); }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ShaderGraphImporter] Failed to read '{ctx.AbsolutePath}': {ex.Message}");
            return false;
        }

        ShaderGraph graph;
        try
        {
            var echo = EchoObject.ReadFromString(text);
            graph = Serializer.Deserialize<ShaderGraph>(echo)
                  ?? new ShaderGraph();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ShaderGraphImporter] Failed to deserialize: {ex.Message}");
            return false;
        }

        ctx.SetMainAsset(graph);

        // Compile to GLSL → parse to a Shader. Sub-asset name baked off the file name
        // for stability (deterministic GUID through the parent).
        var shaderName = Path.GetFileNameWithoutExtension(ctx.AbsolutePath);
        var result = ShaderGraphCompiler.Compile(graph, shaderName);

        Debug.Log($"[ShaderGraphImporter] Compiled '{shaderName}' {result.Diagnostics.Count} diagnostic(s).");

        // Attach diagnostics to the corresponding nodes via the validation pipeline so
        // the editor's badge renderer picks them up.
        foreach (var d in result.Diagnostics)
        {
            if (d.nodeId.HasValue)
            {
                var n = graph.FindNode(d.nodeId.Value);
                if (n != null) n.Messages.Add(new NodeMessage { Severity = d.severity, Text = d.message });
            }
            else
            {
                if (d.severity == NodeMessageSeverity.Error) Debug.LogError($"[ShaderGraph] {d.message}");
                else                                          Debug.LogWarning($"[ShaderGraph] {d.message}");
            }
        }

        // Parse the generated GLSL into a Shader runtime object via the existing parser
        // pipeline. Reuses the same code path Prowl uses for hand-written .shader files,
        // INCLUDING the same include-resolution chain (relative → Assets root →
        // embedded defaults). Without the embedded fallback, every #include "Fragment"
        // etc. would fail because users author shader graphs in Assets/, not embedded.
        string dir = System.IO.Path.GetDirectoryName(ctx.AbsolutePath) ?? "";
        string? IncludeResolver(string includePath)
        {
            string fullPath = System.IO.Path.Combine(dir, includePath);
            if (System.IO.File.Exists(fullPath))
                return System.IO.File.ReadAllText(fullPath);
            if (Project.Current != null)
            {
                string assetsPath = System.IO.Path.Combine(Project.Current.AssetsPath, includePath);
                if (System.IO.File.Exists(assetsPath))
                    return System.IO.File.ReadAllText(assetsPath);
            }
            string fileName = System.IO.Path.GetFileName(includePath);
            try { return Runtime.Resources.EmbeddedResources.ReadAllText($"Assets/Defaults/{fileName}"); }
            catch { return null; }
        }

        Shader? shader;
        bool parsedOk;
        try
        {
            parsedOk = Prowl.Runtime.AssetImporting.ShaderParser.ParseShader(
                ctx.AbsolutePath, result.ShaderSource, IncludeResolver, out shader);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ShaderGraphImporter] Shader parser threw for '{shaderName}': {ex.Message}");
            // Dump the generated source so the user can see what went wrong.
            Debug.LogError($"[ShaderGraphImporter] Generated source:\n{result.ShaderSource}");
            return true; // main asset still saved sub-asset just missing
        }

        if (parsedOk && shader != null)
        {
            shader.Name = shaderName;
            ctx.AddSubAsset("CompiledShader", shader);
            Debug.Log($"[ShaderGraphImporter] Added compiled Shader sub-asset for '{shaderName}'.");
        }
        else
        {
            Debug.LogError($"[ShaderGraphImporter] ParseShader returned false for '{shaderName}'. Generated source:\n{result.ShaderSource}");
        }

        return true;
    }
}
