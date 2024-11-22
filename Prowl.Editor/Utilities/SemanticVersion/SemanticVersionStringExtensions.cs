using System;

namespace Prowl.Editor.Utilities.SemVersion;

/// <summary>Contains extensions to the string class to improve comparison.</summary>
internal static class SemanticVersionStringExtensions
{
    /// <summary>Compares two component parts for equality. '*' character is treated as a wildcard</summary>
    /// <remarks>
    /// Comparison is made in accordance to <see href="https://semver.org/#spec-item-11">semantic version specification</see>
    /// with an addition that prerelease component can be a wildcard ('*' character) that is equal to any <i>existing</i> prerelease component.
    /// Wildcards in component's dot-separated identifiers are not supported.
    /// </remarks>
    /// <param name="left"> The left part to compare.</param>
    /// <param name="right">The right part to compare.</param>
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
    internal static int CompareComponent(this string left, string right)
    {
        var isLeftEmpty = string.IsNullOrWhiteSpace(left);
        var isRightEmpty = string.IsNullOrWhiteSpace(right);

        if ((isLeftEmpty && isRightEmpty) 
            || (left == "*" && !isRightEmpty) 
            || (!isLeftEmpty && right == "*"))
        {
            return 0;
        }

        if (isLeftEmpty)
        {
            return +1;
        }

        if (isRightEmpty)
        {
            return -1;
        }

        var leftParts = left.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftChar = leftParts[i];
            var rightChar = rightParts[i];

            var leftIsNum = int.TryParse(leftChar, out var componentNumVal);
            var rightIsNum = int.TryParse(rightChar, out var otherNumVal);

            if (leftIsNum && rightIsNum)
            {
                if (componentNumVal.CompareTo(otherNumVal) == 0)
                {
                    continue;
                }
                return componentNumVal.CompareTo(otherNumVal);
            }

            if (leftIsNum)
            {
                return -1;
            }

            if (rightIsNum)
            {
                return 1;
            }

            var comp = string.Compare(leftChar, rightChar, StringComparison.OrdinalIgnoreCase);
            if (comp != 0)
            {
                return comp;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
