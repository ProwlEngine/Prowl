using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    /// <summary>
    /// A composite layout value built from four independent components:
    /// <c>Px + Pct% + Grow * remainderShare + AutoFactor * contentSize</c>.
    /// Lerping interpolates each component linearly. Arithmetic combines components
    /// so things like <c>Stretch(1) + Pixels(10)</c> ("10px floor plus a share of the leftover")
    /// or <c>Percentage(50) + Pixels(-8)</c> ("50% minus 8px") fall out naturally.
    /// </summary>
    public struct UnitValue : IEquatable<UnitValue>
    {
        /// <summary>Raw pixel offset.</summary>
        public float Px;

        /// <summary>Percent of parent (0-100).</summary>
        public float Pct;

        /// <summary>Stretch factor: share of leftover space, weighted against sibling stretches.</summary>
        public float Grow;

        /// <summary>Content-size multiplier. 1 = use full content size; 0 = ignore content.</summary>
        public float AutoFactor;

        public UnitValue(float px, float pct = 0f, float grow = 0f, float autoFactor = 0f)
        {
            Px = px;
            Pct = pct;
            Grow = grow;
            AutoFactor = autoFactor;
        }

        #region Factory Methods

        /// <summary>Pre-allocated common values to avoid repeating the constructor.</summary>
        public static readonly UnitValue Auto = new UnitValue(0f, 0f, 0f, 1f);
        public static readonly UnitValue ZeroPixels = new UnitValue(0f, 0f, 0f, 0f);
        public static readonly UnitValue StretchOne = new UnitValue(0f, 0f, 1f, 0f);

        /// <summary>A stretch factor with no pixel/percent floor.</summary>
        public static UnitValue Stretch(float factor = 1f) => new UnitValue(0f, 0f, factor, 0f);

        /// <summary>A pure pixel length.</summary>
        public static UnitValue Pixels(float value) => new UnitValue(value, 0f, 0f, 0f);

        /// <summary>A percentage of the parent, optionally combined with a pixel offset.</summary>
        public static UnitValue Percentage(float value, float offset = 0f) => new UnitValue(offset, value, 0f, 0f);

        #endregion

        #region Predicates

        /// <summary>True when this value participates in stretch (flex-grow) distribution.</summary>
        public readonly bool HasGrow => Grow > 0f;

        /// <summary>True when this value contributes a content-size component.</summary>
        public readonly bool HasAuto => AutoFactor > 0f;

        /// <summary>True when this value resolves to a length without needing stretch competition or content measurement.</summary>
        public readonly bool IsFixed => Grow == 0f && AutoFactor == 0f;

        /// <summary>True when this value is exactly the Auto sentinel. Useful for default-detection ("user didn't set this").</summary>
        public readonly bool IsAuto => Px == 0f && Pct == 0f && Grow == 0f && AutoFactor == 1f;

        /// <summary>True when this value is purely a stretch factor with no other components.</summary>
        public readonly bool IsStretch => Px == 0f && Pct == 0f && AutoFactor == 0f && Grow > 0f;

        /// <summary>True when this value is purely a pixel length (no percent / grow / auto).</summary>
        public readonly bool IsPixels => Pct == 0f && Grow == 0f && AutoFactor == 0f;

        /// <summary>True when this value carries a percentage component (with no grow / auto).</summary>
        public readonly bool IsPercentage => Grow == 0f && AutoFactor == 0f && Pct != 0f;

        #endregion

        /// <summary>
        /// The fixed-length floor: <c>Px + (Pct/100) * parentValue</c>. Excludes stretch and content contributions.
        /// </summary>
        public readonly float Floor(float parentValue) => Px + (Pct * 0.01f) * parentValue;

        /// <summary>
        /// Resolves this value to pixels.
        /// <paramref name="defaultValue"/> is multiplied by the Grow and AutoFactor components, mirroring
        /// the legacy "default" fallback for stretch/auto units. Most layout sites pass 0 here so only the
        /// fixed floor is returned; the layout engine then layers stretch and content contributions on top.
        /// </summary>
        public readonly float ToPx(float parentValue, float defaultValue)
            => Px + (Pct * 0.01f) * parentValue + (Grow + AutoFactor) * defaultValue;

        /// <summary>Resolves to pixels and clamps between min and max.</summary>
        public readonly float ToPxClamped(float parentValue, float defaultValue, in UnitValue min, in UnitValue max)
        {
            float minValue = min.ToPx(parentValue, float.MinValue);
            float maxValue = max.ToPx(parentValue, float.MaxValue);
            float value = ToPx(parentValue, defaultValue);
            return Maths.Min(maxValue, Maths.Max(minValue, value));
        }

        /// <summary>
        /// Component-wise linear interpolation. Result lerps each of Px / Pct / Grow / AutoFactor
        /// independently, so Lerp(Pixels(0), Auto, 0.5f) yields {AutoFactor: 0.5} (half content size).
        /// </summary>
        public static UnitValue Lerp(in UnitValue a, in UnitValue b, float t)
        {
            t = Maths.Clamp(t, 0f, 1f);
            return new UnitValue(
                a.Px + (b.Px - a.Px) * t,
                a.Pct + (b.Pct - a.Pct) * t,
                a.Grow + (b.Grow - a.Grow) * t,
                a.AutoFactor + (b.AutoFactor - a.AutoFactor) * t
            );
        }

        /// <summary>UnitValue is a value type; Clone is provided for source compatibility.</summary>
        public readonly UnitValue Clone() => this;

        #region Operators

        public static UnitValue operator +(in UnitValue a, in UnitValue b)
            => new UnitValue(a.Px + b.Px, a.Pct + b.Pct, a.Grow + b.Grow, a.AutoFactor + b.AutoFactor);

        public static UnitValue operator -(in UnitValue a, in UnitValue b)
            => new UnitValue(a.Px - b.Px, a.Pct - b.Pct, a.Grow - b.Grow, a.AutoFactor - b.AutoFactor);

        public static UnitValue operator -(in UnitValue a)
            => new UnitValue(-a.Px, -a.Pct, -a.Grow, -a.AutoFactor);

        public static UnitValue operator *(in UnitValue a, float scalar)
            => new UnitValue(a.Px * scalar, a.Pct * scalar, a.Grow * scalar, a.AutoFactor * scalar);

        public static UnitValue operator *(float scalar, in UnitValue a) => a * scalar;

        public static UnitValue operator /(in UnitValue a, float scalar)
            => new UnitValue(a.Px / scalar, a.Pct / scalar, a.Grow / scalar, a.AutoFactor / scalar);

        public static implicit operator UnitValue(int value) => new UnitValue(value);
        public static implicit operator UnitValue(float value) => new UnitValue(value);

        public static bool operator ==(in UnitValue a, in UnitValue b) => a.Equals(b);
        public static bool operator !=(in UnitValue a, in UnitValue b) => !a.Equals(b);

        #endregion

        #region Equality

        public readonly bool Equals(UnitValue other)
            => Px == other.Px && Pct == other.Pct && Grow == other.Grow && AutoFactor == other.AutoFactor;

        public override readonly bool Equals(object? obj) => obj is UnitValue other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(Px, Pct, Grow, AutoFactor);

        #endregion

        public override readonly string ToString()
        {
            var parts = new List<string>(4);
            if (Px != 0f) parts.Add($"{Px}px");
            if (Pct != 0f) parts.Add($"{Pct}%");
            if (Grow != 0f) parts.Add($"{Grow}grow");
            if (AutoFactor != 0f) parts.Add($"{AutoFactor}auto");
            return parts.Count == 0 ? "0px" : string.Join(" + ", parts);
        }
    }
}
