using System;
using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    /// <summary>
    /// Represents a value with a unit type for UI layout measurements.
    /// Supports pixels, percentages, auto-sizing, and stretch units with interpolation capabilities.
    /// </summary>
    public struct UnitValue
    {
        /// <summary>
        /// Helper class for interpolation between two UnitValue instances.
        /// Using a simplified class approach to avoid struct cycles.
        /// </summary>
        private class LerpData
        {
            public readonly UnitValue Start;
            public readonly UnitValue End;
            public readonly float Progress;

            public LerpData(UnitValue start, UnitValue end, float progress)
            {
                Start = start;
                End = end;
                Progress = progress;
            }
        }

        /// <summary>The unit type of this value</summary>
        public Units Type { get; set; }

        /// <summary>The numeric value in the specified units</summary>
        public float Value { get; set; }

        /// <summary>Additional pixel offset when using percentage units</summary>
        public float PercentPixelOffset { get; set; }

        /// <summary>Data for interpolation between two UnitValues (null when not interpolating)</summary>
        private LerpData? _lerpData;

        /// <summary>
        /// Creates a UnitValue with the specified type and value.
        /// </summary>
        /// <param name="type">The unit type</param>
        /// <param name="value">The numeric value</param>
        /// <param name="offset">Additional pixel offset for percentage units</param>
        public UnitValue(Units type, float value = 0f, float offset = 0f)
        {
            Type = type;
            Value = value;
            PercentPixelOffset = offset;
            
            _lerpData = null;
        }

        #region Factory Methods

        /// <summary>Pre-allocated common values to avoid allocations</summary>
        public static readonly UnitValue Auto = new UnitValue(Units.Auto);
        public static readonly UnitValue ZeroPixels = new UnitValue(Units.Pixels, 0);
        public static readonly UnitValue StretchOne = new UnitValue(Units.Stretch, 1);

        /// <summary>
        /// Creates a Stretch unit value with the specified factor.
        /// </summary>
        /// <param name="factor">Stretch factor (relative to other stretch elements)</param>
        public static UnitValue Stretch(float factor = 1f) => new UnitValue(Units.Stretch, factor);

        /// <summary>
        /// Creates a Pixel unit value.
        /// </summary>
        /// <param name="value">Size in pixels</param>
        public static UnitValue Pixels(float value) => new UnitValue(Units.Pixels, value);

        /// <summary>
        /// Creates a Percentage unit value.
        /// </summary>
        /// <param name="value">Percentage value (0-100)</param>
        /// <param name="offset">Additional pixel offset</param>
        public static UnitValue Percentage(float value, float offset = 0f) => new UnitValue(Units.Percentage, value, offset);

        #endregion

        #region Type Checking Properties

        /// <summary>Returns true if this value is using Auto units</summary>
        public bool IsAuto => Type == Units.Auto;

        /// <summary>Returns true if this value is using Stretch units</summary>
        public bool IsStretch => Type == Units.Stretch;

        /// <summary>Returns true if this value is using Pixel units</summary>
        public bool IsPixels => Type == Units.Pixels;

        /// <summary>Returns true if this value is using Percentage units</summary>
        public bool IsPercentage => Type == Units.Percentage;

        #endregion

        /// <summary>
        /// Converts this unit value to pixels based on the parent's size.
        /// </summary>
        /// <param name="parentValue">The parent element's size in pixels</param>
        /// <param name="defaultValue">Default value to use for Auto and Stretch units</param>
        /// <returns>Size in pixels</returns>
        public readonly float ToPx(float parentValue, float defaultValue)
        {
            // Handle interpolation if active
            if (_lerpData != null)
            {
                var startPx = _lerpData.Start.ToPx(parentValue, defaultValue);
                var endPx = _lerpData.End.ToPx(parentValue, defaultValue);
                return startPx + (endPx - startPx) * _lerpData.Progress;
            }

            // Convert based on unit type
            return Type switch {
                Units.Pixels => Value,
                Units.Percentage => ((Value / 100f) * parentValue) + PercentPixelOffset,
                _ => defaultValue
            };
        }

        /// <summary>
        /// Converts this unit value to pixels and clamps it between minimum and maximum values.
        /// </summary>
        /// <param name="parentValue">The parent element's size in pixels</param>
        /// <param name="defaultValue">Default value to use for Auto and Stretch units</param>
        /// <param name="min">Minimum allowed value</param>
        /// <param name="max">Maximum allowed value</param>
        /// <returns>Size in pixels, clamped between min and max</returns>
        public readonly float ToPxClamped(float parentValue, float defaultValue, in UnitValue min, in UnitValue max)
        {
            float minValue = min.ToPx(parentValue, float.MinValue);
            float maxValue = max.ToPx(parentValue, float.MaxValue);
            float value = ToPx(parentValue, defaultValue);

            return Maths.Min(maxValue, Maths.Max(minValue, value));
        }

        /// <summary>
        /// Linearly interpolates between two UnitValue instances.
        /// In reality, it creates a new UnitValue with special interpolation data which is calculated when ToPx is called.
        /// </summary>
        /// <param name="a">Starting value</param>
        /// <param name="b">Ending value</param>
        /// <param name="blendFactor">Interpolation factor (0.0 to 1.0)</param>
        /// <returns>Interpolated UnitValue</returns>
        public static UnitValue Lerp(in UnitValue a, in UnitValue b, float blendFactor)
        {
            // Ensure blend factor is between 0 and 1
            blendFactor = Maths.Clamp(blendFactor, 0f, 1f);

            // If units are the same, we can blend directly
            if (a.Type == b.Type)
            {
                return new UnitValue(
                    a.Type,
                    a.Value + (b.Value - a.Value) * blendFactor,
                    a.PercentPixelOffset + (b.PercentPixelOffset - a.PercentPixelOffset) * blendFactor
                );
            }

            // If units are different, use interpolation data
            var result = new UnitValue {
                Type = a.Type,
                Value = a.Value,
                PercentPixelOffset = a.PercentPixelOffset,
                _lerpData = new LerpData(a, b, blendFactor)
            };
            return result;
        }

        /// <summary>
        /// Creates a deep copy of this UnitValue.
        /// </summary>
        /// <returns>A new UnitValue with the same properties</returns>
        public readonly UnitValue Clone() => new UnitValue {
            Type = Type,
            Value = Value,
            PercentPixelOffset = PercentPixelOffset,
            _lerpData = _lerpData != null ? new LerpData(_lerpData.Start, _lerpData.End, _lerpData.Progress) : null
        };

        #region Implicit Conversions

        /// <summary>
        /// Implicitly converts an integer to a pixel UnitValue.
        /// </summary>
        public static implicit operator UnitValue(int value)
        {
            return new UnitValue(Units.Pixels, value);
        }

        /// <summary>
        /// Implicitly converts a float to a pixel UnitValue.
        /// </summary>
        public static implicit operator UnitValue(float value)
        {
            return new UnitValue(Units.Pixels, value);
        }

        #endregion

        #region Equality and Hashing

        /// <summary>
        /// Compares this UnitValue with another object for equality.
        /// </summary>
        public override readonly bool Equals(object? obj)
        {
            if (obj is UnitValue other)
            {
                // First, check the basic properties
                bool basicPropertiesEqual = Type == other.Type &&
                                           Value == other.Value &&
                                           PercentPixelOffset == other.PercentPixelOffset;

                // If either value isn't interpolating, they're equal only if both aren't
                if (_lerpData is null || other._lerpData is null)
                    return basicPropertiesEqual && _lerpData is null && other._lerpData is null;

                // Both values are interpolating – compare their interpolation data safely
                var thisLerp = _lerpData;
                var otherLerp = other._lerpData;
                bool lerpPropsEqual = thisLerp.Start.Equals(otherLerp.Start) &&
                                      thisLerp.End.Equals(otherLerp.End) &&
                                      thisLerp.Progress == otherLerp.Progress;

                return basicPropertiesEqual && lerpPropsEqual;
            }

            return false;
        }

        /// <summary>
        /// Returns a hash code for this UnitValue.
        /// </summary>
        public override readonly int GetHashCode() => HashCode.Combine(Type, Value, PercentPixelOffset);

        #endregion

        /// <summary>
        /// Returns a string representation of this UnitValue.
        /// </summary>
        public override readonly string ToString() => Type switch {
            Units.Pixels => $"{Value}px",
            Units.Percentage => $"{Value}% + {PercentPixelOffset}px",
            Units.Stretch => $"Stretch({Value})",
            Units.Auto => "Auto",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
