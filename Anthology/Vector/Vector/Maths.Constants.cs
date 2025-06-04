using System;

namespace Prowl.Vector
{
    public static partial class Maths
    {
        #region Basic Constants
        /// <summary>Mathematical constant PI (π) ≈ 3.141592653589793</summary>
        public const double PI = Math.PI;

        /// <summary>Mathematical constant E (Euler's number) ≈ 2.718281828459045</summary>
        public const double E = Math.E;

        /// <summary>1 / π</summary>
        public const double One_PI = 1.0 / PI;

        /// <summary>2 / π</summary>
        public const double Two_PI = 2.0 / PI;

        /// <summary>4 / π</summary>
        public const double Four_PI = 4.0 / PI;

        /// <summary>π / 2</summary>
        public const double PI_2 = PI / 2.0;

        /// <summary>π / 3</summary>
        public const double PI_3 = PI / 3.0;

        /// <summary>π / 4</summary>
        public const double PI_4 = PI / 4.0;

        /// <summary>π / 6</summary>
        public const double PI_6 = PI / 6.0;

        /// <summary>π / 8</summary>
        public const double PI_8 = PI / 8.0;

        /// <summary>2π (full circle in radians)</summary>
        public const double TwoPI = 2.0 * PI;

        /// <summary>3π / 2</summary>
        public const double Three_PI_2 = 3.0 * PI / 2.0;
        #endregion

        #region Square Roots
        /// <summary>√2 ≈ 1.414213562373095</summary>
        public const double Sqrt2 = 1.414213562373095;

        /// <summary>√3 ≈ 1.732050807568877</summary>
        public const double Sqrt3 = 1.732050807568877;

        /// <summary>√5 ≈ 2.236067977499789</summary>
        public const double Sqrt5 = 2.236067977499789;

        /// <summary>√π ≈ 1.772453850905516</summary>
        public const double Sqrt_PI = 1.772453850905516;

        /// <summary>√e ≈ 1.648721270700128</summary>
        public const double Sqrt_E = 1.648721270700128;

        /// <summary>1 / √2 ≈ 0.707106781186547</summary>
        public const double One_Sqrt2 = 1.0 / Sqrt2;

        /// <summary>1 / √π ≈ 0.564194490192344</summary>
        public const double One_Sqrt_PI = 1.0 / Sqrt_PI;
        #endregion

        #region Logarithms
        /// <summary>ln(2) ≈ 0.693147180559945</summary>
        public const double Ln2 = 0.693147180559945;

        /// <summary>ln(3) ≈ 1.098612288668109</summary>
        public const double Ln3 = 1.098612288668109;

        /// <summary>ln(10) ≈ 2.302585092994045</summary>
        public const double Ln10 = 2.302585092994045;

        /// <summary>log₂(e) ≈ 1.442695040888963</summary>
        public const double Log2_E = 1.442695040888963;

        /// <summary>log₁₀(e) ≈ 0.434294481903251</summary>
        public const double Log10_E = 0.434294481903251;

        /// <summary>log₁₀(2) ≈ 0.301029995663981</summary>
        public const double Log10_2 = 0.301029995663981;
        #endregion

        #region Degrees/Radians Conversion
        /// <summary>π / 180 (degrees to radians multiplier)</summary>
        public const double Deg2Rad = PI / 180.0;

        /// <summary>180 / π (radians to degrees multiplier)</summary>
        public const double Rad2Deg = 180.0 / PI;
        #endregion

        #region Trigonometric Values
        /// <summary>sin(π/4) = cos(π/4) = 1/√2 ≈ 0.70710678118654752440</summary>
        public const double Sin_PI_4 = One_Sqrt2;

        /// <summary>sin(π/3) = √3/2 ≈ 0.86602540378443864676</summary>
        public const double Sin_PI_3 = Sqrt3 / 2.0;

        /// <summary>sin(π/6) = 1/2</summary>
        public const double Sin_PI_6 = 0.5;

        /// <summary>tan(π/4) = 1</summary>
        public const double Tan_PI_4 = 1.0;

        /// <summary>tan(π/3) = √3</summary>
        public const double Tan_PI_3 = Sqrt3;

        /// <summary>tan(π/6) = 1/√3</summary>
        public const double Tan_PI_6 = 1.0 / Sqrt3;
        #endregion

        #region Physics Constants
        /// <summary>Golden ratio (φ) ≈ 1.618033988749894</summary>
        public const double GoldenRatio = 1.618033988749894;

        /// <summary>Silver ratio ≈ 2.414213562373095</summary>
        public const double SilverRatio = 1.0 + Sqrt2;

        /// <summary>Euler-Mascheroni constant (γ) ≈ 0.577215664901532</summary>
        public const double EulerGamma = 0.577215664901532;

        /// <summary>Catalan's constant ≈ 0.915965594177219</summary>
        public const double Catalan = 0.915965594177219;
        #endregion

        #region Precision Constants
        /// <summary>Machine epsilon for double</summary>
        public const double Epsilon = double.Epsilon;

        /// <summary>Machine epsilon for float</summary>
        public const float EpsilonF = float.Epsilon;

        /// <summary>Smallest positive double value</summary>
        public const double MinValue = double.MinValue;

        /// <summary>Smallest positive float value</summary>
        public const float MinValueF = float.MinValue;

        /// <summary>Largest positive double value</summary>
        public const double MaxValue = double.MaxValue;

        /// <summary>Largest positive float value</summary>
        public const float MaxValueF = float.MaxValue;

        /// <summary>Positive infinity</summary>
        public const double PositiveInfinity = double.PositiveInfinity;

        /// <summary>Positive infinity</summary>
        public const float PositiveInfinityF = float.PositiveInfinity;

        /// <summary>Negative infinity</summary>
        public const double NegativeInfinity = double.NegativeInfinity;

        /// <summary>Not a Number</summary>
        public const double NaN = double.NaN;

        /// <summary>Not a Number</summary>
        public const float NaNF = float.NaN;
        #endregion

        #region Common Fractions
        /// <summary>1/2</summary>
        public const double Half = 0.5;

        /// <summary>1/3</summary>
        public const double Third = 1.0 / 3.0;

        /// <summary>2/3</summary>
        public const double TwoThirds = 2.0 / 3.0;

        /// <summary>1/4</summary>
        public const double Quarter = 0.25;

        /// <summary>3/4</summary>
        public const double ThreeQuarters = 0.75;

        /// <summary>1/6</summary>
        public const double Sixth = 1.0 / 6.0;

        /// <summary>5/6</summary>
        public const double FiveSixths = 5.0 / 6.0;
        #endregion

        #region Derived Constants
        /// <summary>2π (tau) - full circle in radians</summary>
        public const double Tau = TwoPI;

        /// <summary>√(2π) - appears in normal distribution</summary>
        public const double Sqrt_TwoPI = 2.506628274631000;

        /// <summary>1 / √(2π) - normalization factor</summary>
        public const double One_Sqrt_TwoPI = 1.0 / Sqrt_TwoPI;

        /// <summary>e^(-0.5) ≈ 0.60653065971263342360</summary>
        public const double E_Neg_Half = 0.606530659712633;

        /// <summary>√(2/π) ≈ 0.79788456080286535588</summary>
        public const double Sqrt_Two_PI = 0.797884560802865;
        #endregion

        #region Physics

        /// <summary>Speed of light in vacuum (m/s)</summary>
        public const double SpeedOfLight = 299792458.0;

        /// <summary>Standard gravitational acceleration (m/s²)</summary>
        public const double StandardGravity = 9.80665;

        /// <summary>Standard temperature (K)</summary>
        public const double StandardTemperature = 273.15;

        /// <summary>Absolute zero (°C)</summary>
        public const double AbsoluteZero = -273.15;

        #endregion
    }
}
