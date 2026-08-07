// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime;

/// <summary>
/// The settings files a built player reads, by name.
/// </summary>
/// <remarks>
/// The build writes one file per exported settings type, named after the type. Naming them after the
/// type rather than the category's display label matters: the label is UI text someone may reasonably
/// rename, and doing so would otherwise stop the player applying that whole group of settings with
/// nothing failing anywhere. The build checks this list against what it exported, so a rename that does
/// break the contract is reported instead of discovered in a shipped game.
/// </remarks>
public static class PlayerSettingsFiles
{
    public const string Physics = "PhysicsSettings";
    public const string Audio = "AudioSettings";
    public const string Time = "TimeSettings";
    public const string Assets = "AssetSettings";
    public const string TagsAndLayers = "TagsAndLayersSettings";

    /// <summary>
    /// Every file the player looks for. What the build validates against. General settings are absent
    /// on purpose: product name, company and version reach the player through its manifest.
    /// </summary>
    public static IReadOnlyList<string> All => [Physics, Audio, Time, Assets, TagsAndLayers];
}
