// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.ProjectSettings;

[FilePath("BuildSettings.projsetting", FilePathAttribute.Location.EditorSetting)]
public class BuildProjectSettings : ScriptableSingleton<BuildProjectSettings>
{
    public AssetRef<Scene>[] Scenes = [];

    public bool AllowUnsafeBlocks = false;
    public bool EnableAOTCompilation = false;

    [SerializeField, HideInInspector]
    private bool _allowUnsafeBlocks = false;

    [SerializeField, HideInInspector]
    private bool _enableAOTCompilation = false;


    public override void OnValidate()
    {
        Scenes ??= []; // Ensure scenes are never null

        bool requiresRecompile =
            AllowUnsafeBlocks != _allowUnsafeBlocks ||
            EnableAOTCompilation != _enableAOTCompilation;

        if (requiresRecompile)
        {
            Program.RegisterReloadOfExternalAssemblies();
            _allowUnsafeBlocks = AllowUnsafeBlocks;
            _enableAOTCompilation = EnableAOTCompilation;
        }
    }
}
