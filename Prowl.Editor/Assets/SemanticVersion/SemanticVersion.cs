

// Based on https://github.com/Ruhrpottpatriot/SemanticVersion
// Licensed under the MIT License
// The RangeParser was not working correctly so it got replaced with a new one

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SemVersion;

/// <summary>Represents a version object, compliant with the Semantic Version standard 2.0 (http://semver.org)</summary>
public partial class SemanticVersion : IEquatable<SemanticVersion>, IComparable<SemanticVersion>
{
    private const string WildcardSymbol = "*";
    private const char WildcardSymbolChar = '*';

    private static IComparer<SemanticVersion> s_comparer = new VersionComparer();

    private static readonly Regex s_versionExpression = VersionExpressionRegex();

    /// <summary>Initializes a new instance of the <see cref="SemanticVersion"/> class.</summary>
    /// <param name="major">The major version component. <see langword="null"/> is treated as a '*' wildcard.</param>
    /// <param name="minor">The minor version component. <see langword="null"/> is treated as a '*' wildcard.</param>
    /// <param name="patch">The patch version component. <see langword="null"/> is treated as a '*' wildcard.</param>
    /// <param name="prerelease">The pre-release version component. '*' wildcard is equal to any <i>existing</i> pre-release.</param>
    /// <param name="build">The build version component. '*' wildcard is ignored since build component must not affect comparison</param>
    public SemanticVersion(int? major, int? minor, int? patch, string prerelease = "", string build = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build == WildcardSymbol ? string.Empty : build;

        string versionString = ToString();

        if (!s_versionExpression.IsMatch(versionString))
        {
            throw new ArgumentException($"{versionString} is not a valid semantic version");
        }

        CheckWildcardOrder(major, minor, patch, prerelease);
        HasWildcard = Major is null || Minor is null || Patch is null || Prerelease == WildcardSymbol;

    }

    /// <summary>Gets the major version component. <see langword="null"/> is a '*' wildcard.</summary>
    public int? Major { get; }

    /// <summary>Gets the minor version component. <see langword="null"/> is a '*' wildcard.</summary>
    public int? Minor { get; }

    /// <summary>Gets the patch version component. <see langword="null"/> is a '*' wildcard.</summary>
    public int? Patch { get; }

    /// <summary>Gets the pre-release version component. '*' wildcard is equal to any <i>existing</i> pre-release</summary>
    public string Prerelease { get; }

    /// <summary>Gets the build version component.</summary>
    public string Build { get; }

    /// <summary> Indicates whether this semantic version instance has a wildcard in one of its components </summary>
    public bool HasWildcard { get; }



    public static bool operator ==(SemanticVersion left, SemanticVersion right)
    {
        return s_comparer.Compare(left, right) == 0;
    }

    public static bool operator !=(SemanticVersion left, SemanticVersion right)
    {
        return s_comparer.Compare(left, right) != 0;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right)
    {
        return s_comparer.Compare(left, right) < 0;
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right)
    {
        return s_comparer.Compare(left, right) > 0;
    }

    public static bool operator <=(SemanticVersion left, SemanticVersion right)
    {
        return left == right || left < right;
    }

    public static bool operator >=(SemanticVersion left, SemanticVersion right)
    {
        return left == right || left > right;
    }


    /// <summary>Implicitly converts a string into a <see cref="SemanticVersion"/>.</summary>
    /// <param name="versionString">The string to convert.</param>
    /// <returns>The <see cref="SemanticVersion"/> object.</returns>
    public static implicit operator SemanticVersion(string versionString)
    {
        // ReSharper disable once ArrangeStaticMemberQualifier
        return SemanticVersion.Parse(versionString);
    }

    /// <summary>Explicitly converts a <see cref="System.Version"/> object into a <see cref="SemanticVersion"/>.</summary>
    /// <param name="dotNetVersion">The version to convert.</param>
    /// <remarks>
    /// <para>This operator converts a C# <see cref="System.Version"/> object into the corresponding <see cref="SemanticVersion"/> object.</para>
    /// <para>Note, that with a C# version the <see cref="System.Version.Build"/> property is identical to the <see cref="Patch"/> property on a semantic version compliant object.
    /// Whereas the <see cref="System.Version.Revision"/> property is equivalent to the <see cref="Build"/> property on a semantic version.
    /// The <see cref="Prerelease"/> property is never set, since the C# version object does not use such a notation.</para>
    /// </remarks>
    public static explicit operator SemanticVersion(Version dotNetVersion)
    {
        ArgumentNullException.ThrowIfNull(dotNetVersion);

        int major = dotNetVersion.Major;
        int minor = dotNetVersion.Minor;
        int patch = dotNetVersion.Build >= 0 ? dotNetVersion.Build : 0;
        string build = dotNetVersion.Revision >= 0 ? dotNetVersion.Revision.ToString() : string.Empty;

        return new SemanticVersion(major, minor, patch, string.Empty, build);
    }



    private void CheckWildcardOrder(int? major, int? minor, int? patch, string prerelease)
    {
        var wildcards = new List<bool> { !major.HasValue, !minor.HasValue, !patch.HasValue };

        if (!string.IsNullOrEmpty(prerelease)) wildcards.Add(prerelease == WildcardSymbol);

        bool hasValueAfterWildcard = wildcards.SkipWhile(wildcard => !wildcard).Contains(false);

        if (hasValueAfterWildcard)
        {
            throw new ArgumentException("Version components can't have values after a components with wildcards");
        }
    }


    /// <summary>Change the comparer used to compare two <see cref="SemanticVersion"/> objects.</summary>
    /// <param name="versionComparer">An instance of the comparer to use in future comparisons.</param>
    public static void ChangeComparer(IComparer<SemanticVersion> versionComparer) => s_comparer = versionComparer;

    /// <summary>Describes the first public api version.</summary>
    /// <returns>A <see cref="SemanticVersion"/> with version 1.0.0 as version number.</returns>
    public static SemanticVersion BaseVersion() => new(1, 0, 0);

    /// <summary>Checks if a given string can be considered a valid <see cref="SemanticVersion"/>.</summary>
    /// <param name="inputString">The string to check for validity.</param>
    /// <returns>True, if the passed string is a valid <see cref="SemanticVersion"/>, otherwise false.</returns>
    public static bool IsVersion(string inputString) => s_versionExpression.IsMatch(inputString);


    /// <summary>Parses the specified string to a semantic version.</summary>
    /// <param name="versionString">The version string.</param>
    /// <returns>A new <see cref="SemanticVersion"/> object that has the specified values.</returns>
    /// <exception cref="ArgumentNullException">Raised when the input string is null.</exception>
    /// <exception cref="ArgumentException">Raised when the the input string is in an invalid format.</exception>
    public static SemanticVersion Parse(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            throw new ArgumentException("The provided version string is either null, empty or only consists of whitespace.", nameof(versionString));
        }

        if (!TryParse(versionString, out SemanticVersion? version))
        {
            throw new ArgumentException("The provided version string is invalid.", nameof(versionString));
        }

        return version;
    }

    /// <summary>Tries to parse the specified string into a semantic version.</summary>
    /// <param name="versionString">The version string.</param>
    /// <param name="version">When the method returns, contains a SemVersion instance equivalent
    /// to the version string passed in, if the version string was valid, or <c>null</c> if the
    /// version string was not valid.</param>
    /// <returns><c>False</c> when a invalid version string is passed, otherwise <c>true</c>.</returns>
    public static bool TryParse(string versionString, out SemanticVersion version)
    {
        version = null;

        if (string.IsNullOrEmpty(versionString))
        {
            return false;
        }

        Match versionMatch = s_versionExpression.Match(versionString);

        if (!versionMatch.Success)
        {
            return false;
        }

        Group majorMatch = versionMatch.Groups["major"];
        Group minorMatch = versionMatch.Groups["minor"];
        Group patchMatch = versionMatch.Groups["patch"];
        Group prereleaseMatch = versionMatch.Groups["pre"];
        Group buildMatch = versionMatch.Groups["build"];


        if (!majorMatch.Success)
        {
            return false;
        }
        int? major = majorMatch.Value != WildcardSymbol
        ? int.Parse(majorMatch.Value, CultureInfo.InvariantCulture)
        : (int?)null;


        // Minor can be missing only if Major is a wildcard - this way Minor is a wildcard too (implicitly)
        if (!minorMatch.Success && major != null)
        {
            return false;
        }
        int? minor = minorMatch.Success && minorMatch.Value != WildcardSymbol
        ? int.Parse(minorMatch.Value, CultureInfo.InvariantCulture)
        : (int?)null;


        // Patch can be missing only if Minor is a wildcard - this way Patch is a wildcard too (implicitly)
        if (!patchMatch.Success && minor != null)
        {
            return false;
        }
        int? patch = patchMatch.Success && patchMatch.Value != WildcardSymbol
        ? int.Parse(patchMatch.Value, CultureInfo.InvariantCulture)
        : (int?)null;


        if (prereleaseMatch.Success && patch is null && prereleaseMatch.Value != WildcardSymbol)
        {
            return false;
        }
        string prerelease = prereleaseMatch.Value;


        string build = buildMatch.Value;


        try
        {
            // delegated value-after-wildcard checks to ctor
            version = new SemanticVersion(major, minor, patch, prerelease, build);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }



    /// <inheritdoc />
    public bool Equals(SemanticVersion other) => s_comparer.Compare(this, other) == 0;

    /// <inheritdoc />
    public int CompareTo(object obj) => s_comparer.Compare(this, obj as SemanticVersion);

    /// <inheritdoc />
    public int CompareTo(SemanticVersion other) => s_comparer.Compare(this, other);

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as SemanticVersion);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = Major ?? 0;
            hashCode = (hashCode * 397) ^ Minor ?? 0;
            hashCode = (hashCode * 397) ^ Patch ?? 0;
            hashCode = (hashCode * 397) ^ (!string.IsNullOrWhiteSpace(Prerelease) ? StringComparer.OrdinalIgnoreCase.GetHashCode(Prerelease) : 0);
            hashCode = (hashCode * 397) ^ (!string.IsNullOrWhiteSpace(Build) ? StringComparer.OrdinalIgnoreCase.GetHashCode(Build) : 0);
            return hashCode;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();

        if (!Major.HasValue)
        {
            return WildcardSymbol;
        }

        builder.Append($"{Major.Value}.");

        if (!Minor.HasValue)
        {
            _ = builder.Append(WildcardSymbolChar);
            return builder.ToString();
        }

        builder.Append($"{Minor.Value}.");

        if (!Patch.HasValue)
        {
            _ = builder.Append(WildcardSymbolChar);
            return builder.ToString();
        }

        builder.Append($"{Patch.Value}");
        builder.Append(string.IsNullOrWhiteSpace(Prerelease) ? string.Empty : $"-{Prerelease}");
        builder.Append(string.IsNullOrWhiteSpace(Build) ? string.Empty : $"+{Build}");

        return builder.ToString();
    }

    [GeneratedRegex(@"^(?<major>[0]|[1-9]+[0-9]*|[*])((\.(?<minor>[0]|[1-9]+[0-9]*|[*]))(\.(?<patch>[0]|[1-9]+[0-9]*|[*]))?)?(\-(?<pre>[0-9A-Za-z\-\.]+|[*]))?(\+(?<build>[0-9A-Za-z\-\.]+|[*]))?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex VersionExpressionRegex();
}
