// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;

namespace Prowl.Editor.Importers;

/// <summary>
/// Tracks managed and native plugin libraries (.dll/.so/.dylib). Produces no EngineObject -
/// plugins are referenced at compile time and copied next to the player at build time.
/// Per-plugin platform settings live in the .meta companion (see <see cref="PluginInfo.Keys"/>).
/// Only libraries inside a <c>Plugins/</c> folder are treated as plugins.
/// </summary>
[ImporterFor(".dll", ".so", ".dylib")]
public class PluginImporter : AssetImporter
{
    public override int Version => 1;

    public override bool IsEditorOnlyAsset => true;

    public override bool Import(ImportContext ctx)
    {
        // A new or changed plugin can invalidate user assemblies that reference it.
        if (Project.Current != null)
            ScriptAssemblyManager.RequestRecompile();

        // Marker asset so the inspector resolves the editor by type. The plugin itself is consumed
        // by the compiler/build pipeline directly; its settings live in the .meta.
        ctx.SetMainAsset(new PluginAsset { Name = System.IO.Path.GetFileName(ctx.AbsolutePath) });
        return true;
    }

    public override EchoObject? DefaultSettings()
    {
        var s = EchoObject.NewCompound();
        s[PluginInfo.Keys.EditorOnly] = new EchoObject(false);
        s[PluginInfo.Keys.AutoReferenced] = new EchoObject(true);
        s[PluginInfo.Keys.AnyPlatform] = new EchoObject(true);
        s[PluginInfo.Keys.Windows] = new EchoObject(false);
        s[PluginInfo.Keys.Linux] = new EchoObject(false);
        s[PluginInfo.Keys.MacOS] = new EchoObject(false);
        s[PluginInfo.Keys.Cpu] = new EchoObject("x64");
        return s;
    }
}
