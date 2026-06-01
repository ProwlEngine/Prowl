namespace Prowl.Veldrid;


/// <summary>
/// The precise version of the graphics API that was loaded by a GraphicsDevice.
/// </summary>
public readonly struct GraphicsApiVersion
{
    /// <summary>
    /// An unknown API version.
    /// </summary>
    public static GraphicsApiVersion Unknown => default;

    /// <summary>
    /// The major version (X.x.x.x) of the loaded API.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// The minor (x.X.x.x) version of the loaded API.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// The subminor (x.x.X.x) version of the loaded API.
    /// </summary>
    public int Subminor { get; }

    /// <summary>
    /// The patch (x.x.x.X) version of the loaded API.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// Returns true when this graphics API has any nonzero version numbers, and as such has been initialized.
    /// </summary>
    public bool IsKnown => Major != 0 && Minor != 0 && Subminor != 0 && Patch != 0;


    /// <summary>
    /// Creates a <see cref="GraphicsApiVersion"/> from the given patch info.
    /// </summary>
    public GraphicsApiVersion(int major, int minor, int subminor, int patch)
    {
        Major = major;
        Minor = minor;
        Subminor = subminor;
        Patch = patch;
    }

    /// <summary>
    /// Returns a string of the API version in (major.minor.subminor.patch) format.
    /// </summary>
    public override string ToString()
    {
        return $"{Major}.{Minor}.{Subminor}.{Patch}";
    }

    /// <summary>
    /// Parses OpenGL version strings with either of following formats:
    /// <list type="bullet">
    ///   <item>
    ///     <description>major_number.minor_number</description>
    ///   </item>
    ///   <item>
    ///     <description>major_number.minor_number.release_number</description>
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="versionString">The OpenGL version string.</param>
    /// <param name="version">The parsed <see cref="GraphicsApiVersion"/>.</param>
    /// <returns>True whether the parse succeeded; otherwise false.</returns>
    public static bool TryParseGLVersion(string versionString, out GraphicsApiVersion version)
    {
        string[] versionParts = versionString.Split(' ')[0].Split('.');

        if (!int.TryParse(versionParts[0], out int major) ||
           !int.TryParse(versionParts[1], out int minor))
        {
            version = default;
            return false;
        }

        int releaseNumber = 0;
        if (versionParts.Length == 3)
        {
            if (!int.TryParse(versionParts[2], out releaseNumber))
            {
                version = default;
                return false;
            }
        }

        version = new GraphicsApiVersion(major, minor, 0, releaseNumber);
        return true;
    }
}
