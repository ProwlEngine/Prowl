// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;

namespace Prowl.Runtime;

/// <summary>
/// Everything about a build that the player has to be told rather than compiled with.
/// </summary>
/// <remarks>
/// This type exists so the player can be an ordinary, compiled, debuggable project instead of source
/// emitted into a string at build time. Anything here varies per build; anything that does not belongs in
/// the player's own code.
/// </remarks>
public sealed class PlayerManifest
{
    public const string FileName = "player.manifest";

    /// <summary>Raise when the shape changes in a way an older player cannot read.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// The manifest's own schema version, not the game's.
    /// </summary>
    /// <remarks>
    /// Without this, a player reading a manifest it does not understand falls back to defaults and
    /// starts up doing nothing, which is a far worse failure than refusing to start.
    /// </remarks>
    public int FormatVersion = CurrentFormatVersion;

    public string ProductName = "Prowl Game";
    public string CompanyName = "";
    public string Version = "0.0.0";

    /// <summary>The registered build target id this was produced for.</summary>
    public string TargetId = "";

    public long BuildDateUtcTicks;

    /// <summary>The scene the player opens.</summary>
    public string DefaultScene = Guid.Empty.ToString();

    /// <summary>User script assemblies to load at startup, in dependency order.</summary>
    public List<string> AssemblyLoadOrder = [];

    public AssetPackagingMode Packaging = AssetPackagingMode.LooseFiles;

    public int WindowWidth = 1280;
    public int WindowHeight = 720;

    /// <summary>
    /// Per target additions the engine has no business knowing about, such as an Android orientation.
    /// Kept loose on purpose: a fixed shape would need editing for every new platform.
    /// </summary>
    public Dictionary<string, string> TargetExtras = [];

    public DateTime BuildDateUtc => new(BuildDateUtcTicks, DateTimeKind.Utc);

    public Guid DefaultSceneGuid => Guid.TryParse(DefaultScene, out var guid) ? guid : Guid.Empty;

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        EchoObject echo = Serializer.Serialize(typeof(PlayerManifest), this, TypeMode.None);
        File.WriteAllText(Path.Combine(directory, FileName), echo.WriteToYaml());
    }

    /// <summary>Reads the manifest beside the executable, or defaults when there is none.</summary>
    public static PlayerManifest Load(string directory)
    {
        string path = Path.Combine(directory, FileName);

        // Not a recoverable default: without it there is no scene, no game assemblies and no packaging
        // mode, and the only other symptom is a window that opens onto nothing.
        if (!File.Exists(path))
        {
            Debug.LogError($"[Player] No {FileName} beside the executable. This build is incomplete.");
            return new PlayerManifest();
        }

        try
        {
            var echo = EchoObject.ReadFromYaml(File.ReadAllText(path));
            var manifest = Serializer.Deserialize<PlayerManifest>(echo) ?? new PlayerManifest();

            // Said loudly, because the symptom otherwise is a game that launches and then ignores its
            // own content.
            if (manifest.FormatVersion != CurrentFormatVersion)
                Debug.LogError(
                    $"[Player] {FileName} is format {manifest.FormatVersion}, this player reads {CurrentFormatVersion}. " +
                    "Rebuild the game with a matching engine version.");

            return manifest;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Player] Could not read {FileName}: {e.Message}");
            return new PlayerManifest();
        }
    }
}
