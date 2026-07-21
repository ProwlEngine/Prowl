namespace Prowl.Graphite;


/// <summary>
/// Exact graphics API version loaded by a GraphicsDevice.
/// </summary>
public readonly struct GraphicsApiVersion
{
    /// <summary>
    /// Unknown API version.
    /// </summary>
    public static GraphicsApiVersion Unknown => default;

    /// <summary>
    /// Major version (X.x.x.x).
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// Minor version (x.X.x.x).
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// Subminor version (x.x.X.x).
    /// </summary>
    public int Subminor { get; }

    /// <summary>
    /// Patch version (x.x.x.X).
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// True if any version number is nonzero, i.e. initialized.
    /// </summary>
    public bool IsKnown => Major != 0 && Minor != 0 && Subminor != 0 && Patch != 0;


    /// <summary>
    /// Builds from major/minor/subminor/patch.
    /// </summary>
    public GraphicsApiVersion(int major, int minor, int subminor, int patch)
    {
        Major = major;
        Minor = minor;
        Subminor = subminor;
        Patch = patch;
    }

    /// <summary>
    /// String as major.minor.subminor.patch.
    /// </summary>
    public override string ToString()
    {
        return $"{Major}.{Minor}.{Subminor}.{Patch}";
    }
}
