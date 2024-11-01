namespace Prowl.Editor.Utilities.SemVersion;

internal static class Utilities
{
    /// <summary>Compares two nullable integers treating <see langword="null" /> as an 'equal-to-anything' wildcard.</summary>
    /// <param name="left">The leftside integer to compare.</param>
    /// <param name="right">The rightside integer to compare.</param>
    /// <returns>
    ///     A signed number indicating the relative values of this instance and <paramref name="right"/>.
    ///     <list type="table">
    ///         <listheader>
    ///             <term>Return value</term>
    ///             <description>Description</description>
    ///         </listheader>
    ///         <item>
    ///             <term> &lt; 0 </term>
    ///             <description>This instance is less than <paramref name="right"/>.</description>
    ///         </item><item>
    ///             <term> = 0 </term>
    ///             <description>This instance is equal to <paramref name="right"/> (or one of the instances is a wildcard).</description>
    ///         </item><item>
    ///             <term> &gt; 0 </term>
    ///             <description>This instance is greater than <paramref name="right"/>.</description>
    ///         </item>
    ///     </list>
    /// </returns>
    public static int CompareToWildcard(this int? left, int? right)
		{
        if (left is null || right is null) return 0;
        return left.Value.CompareTo(right.Value);
    }
}
