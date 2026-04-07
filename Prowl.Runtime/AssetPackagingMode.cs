namespace Prowl.Runtime;

/// <summary>
/// How assets are packaged in a built player.
/// </summary>
public enum AssetPackagingMode
{
    /// <summary>Individual {guid}.asset files in an Assets/ folder. Debug-friendly.</summary>
    LooseFiles,

    /// <summary>ZipArchive .prowlpak files. Production desktop builds. Streamable, auto-split.</summary>
    ProwlPak,

    /// <summary>Embedded resources in the player assembly. Required for web/WASM builds.</summary>
    Embedded,
}
