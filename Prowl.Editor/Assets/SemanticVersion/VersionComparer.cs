using System.Collections.Generic;

namespace SemVersion;

/// <summary>Compares two <see cref="SemanticVersion"/> objects for equality.</summary>
public sealed class VersionComparer : IEqualityComparer<SemanticVersion>, IComparer<SemanticVersion>
{
    /// <inheritdoc/>
    public bool Equals(SemanticVersion left, SemanticVersion right)
    {
        return this.Compare(left, right) == 0;
    }

    /// <inheritdoc/>
    public int Compare(SemanticVersion left, SemanticVersion right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        if (right is null)
        {
            return 1;
        }

        var majorComp = left.Major.CompareToWildcard(right.Major);
        if (majorComp != 0)
        {
            return majorComp;
        }

        var minorComp = left.Minor.CompareToWildcard(right.Minor);
        if (minorComp != 0)
        {
            return minorComp;
        }

        var patchComp = left.Patch.CompareToWildcard(right.Patch);
        if (patchComp != 0)
        {
            return patchComp;
        }

        return left.Prerelease.CompareComponent(right.Prerelease);
    }

    /// <inheritdoc/>
    public int GetHashCode(SemanticVersion obj)
    {
        return obj.GetHashCode();
    }
}
