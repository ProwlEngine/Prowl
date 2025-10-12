using System; 
using System.Runtime.CompilerServices;

namespace Prowl.Vector
{
    /// <summary>
    /// A static class containing mathematical functions for vectors and scalars.
    /// </summary>
    public static partial class Maths
    {
        #region Constants

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

        #endregion


        #region Basic Math

        /// <summary>Returns the abs of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Abs(float x) => MathF.Abs(x);
        /// <summary>Returns the abs of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Abs(double x) => Math.Abs(x);
        /// <summary>Returns the abs of int x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Abs(int x) => Math.Abs(x);
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Abs(Float2 x) => new Float2(Abs(x.X), Abs(x.Y));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Abs(Float3 x) => new Float3(Abs(x.X), Abs(x.Y), Abs(x.Z));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Abs(Float4 x) => new Float4(Abs(x.X), Abs(x.Y), Abs(x.Z), Abs(x.W));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Abs(Double2 x) => new Double2(Abs(x.X), Abs(x.Y));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Abs(Double3 x) => new Double3(Abs(x.X), Abs(x.Y), Abs(x.Z));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Abs(Double4 x) => new Double4(Abs(x.X), Abs(x.Y), Abs(x.Z), Abs(x.W));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Abs(Int2 x) => new Int2(Abs(x.X), Abs(x.Y));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Abs(Int3 x) => new Int3(Abs(x.X), Abs(x.Y), Abs(x.Z));
        /// <summary>Returns the componentwise abs of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Abs(Int4 x) => new Int4(Abs(x.X), Abs(x.Y), Abs(x.Z), Abs(x.W));


        /// <summary>Returns the acos of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Acos(float x) => MathF.Acos(x);
        /// <summary>Returns the acos of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Acos(double x) => Math.Acos(x);
        /// <summary>Returns the componentwise acos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Acos(Float2 x) => new Float2(Acos(x.X), Acos(x.Y));
        /// <summary>Returns the componentwise acos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Acos(Float3 x) => new Float3(Acos(x.X), Acos(x.Y), Acos(x.Z));
        /// <summary>Returns the componentwise acos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Acos(Float4 x) => new Float4(Acos(x.X), Acos(x.Y), Acos(x.Z), Acos(x.W));
        /// <summary>Returns the componentwise acos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Acos(Double2 x) => new Double2(Acos(x.X), Acos(x.Y));
        /// <summary>Returns the componentwise acos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Acos(Double3 x) => new Double3(Acos(x.X), Acos(x.Y), Acos(x.Z));
        /// <summary>Returns the componentwise acos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Acos(Double4 x) => new Double4(Acos(x.X), Acos(x.Y), Acos(x.Z), Acos(x.W));


        /// <summary>Returns the asin of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Asin(float x) => MathF.Asin(x);
        /// <summary>Returns the asin of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Asin(double x) => Math.Asin(x);
        /// <summary>Returns the componentwise asin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Asin(Float2 x) => new Float2(Asin(x.X), Asin(x.Y));
        /// <summary>Returns the componentwise asin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Asin(Float3 x) => new Float3(Asin(x.X), Asin(x.Y), Asin(x.Z));
        /// <summary>Returns the componentwise asin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Asin(Float4 x) => new Float4(Asin(x.X), Asin(x.Y), Asin(x.Z), Asin(x.W));
        /// <summary>Returns the componentwise asin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Asin(Double2 x) => new Double2(Asin(x.X), Asin(x.Y));
        /// <summary>Returns the componentwise asin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Asin(Double3 x) => new Double3(Asin(x.X), Asin(x.Y), Asin(x.Z));
        /// <summary>Returns the componentwise asin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Asin(Double4 x) => new Double4(Asin(x.X), Asin(x.Y), Asin(x.Z), Asin(x.W));


        /// <summary>Returns the atan of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Atan(float x) => MathF.Atan(x);
        /// <summary>Returns the atan of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Atan(double x) => Math.Atan(x);
        /// <summary>Returns the componentwise atan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Atan(Float2 x) => new Float2(Atan(x.X), Atan(x.Y));
        /// <summary>Returns the componentwise atan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Atan(Float3 x) => new Float3(Atan(x.X), Atan(x.Y), Atan(x.Z));
        /// <summary>Returns the componentwise atan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Atan(Float4 x) => new Float4(Atan(x.X), Atan(x.Y), Atan(x.Z), Atan(x.W));
        /// <summary>Returns the componentwise atan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Atan(Double2 x) => new Double2(Atan(x.X), Atan(x.Y));
        /// <summary>Returns the componentwise atan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Atan(Double3 x) => new Double3(Atan(x.X), Atan(x.Y), Atan(x.Z));
        /// <summary>Returns the componentwise atan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Atan(Double4 x) => new Double4(Atan(x.X), Atan(x.Y), Atan(x.Z), Atan(x.W));


        /// <summary>Returns the arctangent of y/x in radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Atan2(float x, float y) => MathF.Atan2(x, y);
        /// <summary>Returns the arctangent of y/x in radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Atan2(double x, double y) => Math.Atan2(x, y);
        /// <summary>Returns the componentwise arctangent of y/x for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Atan2(Float2 x, Float2 y) => new Float2(Atan2(x.X, y.X), Atan2(x.Y, y.Y));
        /// <summary>Returns the componentwise arctangent of y/x for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Atan2(Float3 x, Float3 y) => new Float3(Atan2(x.X, y.X), Atan2(x.Y, y.Y), Atan2(x.Z, y.Z));
        /// <summary>Returns the componentwise arctangent of y/x for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Atan2(Float4 x, Float4 y) => new Float4(Atan2(x.X, y.X), Atan2(x.Y, y.Y), Atan2(x.Z, y.Z), Atan2(x.W, y.W));
        /// <summary>Returns the componentwise arctangent of y/x for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Atan2(Double2 x, Double2 y) => new Double2(Atan2(x.X, y.X), Atan2(x.Y, y.Y));
        /// <summary>Returns the componentwise arctangent of y/x for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Atan2(Double3 x, Double3 y) => new Double3(Atan2(x.X, y.X), Atan2(x.Y, y.Y), Atan2(x.Z, y.Z));
        /// <summary>Returns the componentwise arctangent of y/x for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Atan2(Double4 x, Double4 y) => new Double4(Atan2(x.X, y.X), Atan2(x.Y, y.Y), Atan2(x.Z, y.Z), Atan2(x.W, y.W));


        /// <summary>Returns the cos of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cos(float x) => MathF.Cos(x);
        /// <summary>Returns the cos of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Cos(double x) => Math.Cos(x);
        /// <summary>Returns the componentwise cos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Cos(Float2 x) => new Float2(Cos(x.X), Cos(x.Y));
        /// <summary>Returns the componentwise cos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Cos(Float3 x) => new Float3(Cos(x.X), Cos(x.Y), Cos(x.Z));
        /// <summary>Returns the componentwise cos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Cos(Float4 x) => new Float4(Cos(x.X), Cos(x.Y), Cos(x.Z), Cos(x.W));
        /// <summary>Returns the componentwise cos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Cos(Double2 x) => new Double2(Cos(x.X), Cos(x.Y));
        /// <summary>Returns the componentwise cos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Cos(Double3 x) => new Double3(Cos(x.X), Cos(x.Y), Cos(x.Z));
        /// <summary>Returns the componentwise cos of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Cos(Double4 x) => new Double4(Cos(x.X), Cos(x.Y), Cos(x.Z), Cos(x.W));


        /// <summary>Returns e raised to the power of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Exp(float x) => MathF.Exp(x);
        /// <summary>Returns e raised to the power of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Exp(double x) => Math.Exp(x);
        /// <summary>Returns the componentwise exponential function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Exp(Float2 x) => new Float2(Exp(x.X), Exp(x.Y));
        /// <summary>Returns the componentwise exponential function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Exp(Float3 x) => new Float3(Exp(x.X), Exp(x.Y), Exp(x.Z));
        /// <summary>Returns the componentwise exponential function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Exp(Float4 x) => new Float4(Exp(x.X), Exp(x.Y), Exp(x.Z), Exp(x.W));
        /// <summary>Returns the componentwise exponential function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Exp(Double2 x) => new Double2(Exp(x.X), Exp(x.Y));
        /// <summary>Returns the componentwise exponential function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Exp(Double3 x) => new Double3(Exp(x.X), Exp(x.Y), Exp(x.Z));
        /// <summary>Returns the componentwise exponential function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Exp(Double4 x) => new Double4(Exp(x.X), Exp(x.Y), Exp(x.Z), Exp(x.W));


        /// <summary>Returns 2 raised to the power of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Exp2(float x) => MathF.Pow(2f, x);
        /// <summary>Returns 2 raised to the power of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Exp2(double x) => Math.Pow(2, x);
        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Exp2(Float2 x) => new Float2(Exp2(x.X), Exp2(x.Y));
        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Exp2(Float3 x) => new Float3(Exp2(x.X), Exp2(x.Y), Exp2(x.Z));
        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Exp2(Float4 x) => new Float4(Exp2(x.X), Exp2(x.Y), Exp2(x.Z), Exp2(x.W));
        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Exp2(Double2 x) => new Double2(Exp2(x.X), Exp2(x.Y));
        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Exp2(Double3 x) => new Double3(Exp2(x.X), Exp2(x.Y), Exp2(x.Z));
        /// <summary>Returns the componentwise power of 2 raised to x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Exp2(Double4 x) => new Double4(Exp2(x.X), Exp2(x.Y), Exp2(x.Z), Exp2(x.W));


        /// <summary>Returns the floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FMod(float x, float y) => x % y;
        /// <summary>Returns the floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double FMod(double x, double y) => x % y;
        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 FMod(Float2 x, Float2 y) => new Float2(FMod(x.X, y.X), FMod(x.Y, y.Y));
        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 FMod(Float3 x, Float3 y) => new Float3(FMod(x.X, y.X), FMod(x.Y, y.Y), FMod(x.Z, y.Z));
        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 FMod(Float4 x, Float4 y) => new Float4(FMod(x.X, y.X), FMod(x.Y, y.Y), FMod(x.Z, y.Z), FMod(x.W, y.W));
        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 FMod(Double2 x, Double2 y) => new Double2(FMod(x.X, y.X), FMod(x.Y, y.Y));
        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 FMod(Double3 x, Double3 y) => new Double3(FMod(x.X, y.X), FMod(x.Y, y.Y), FMod(x.Z, y.Z));
        /// <summary>Returns the componentwise floating-point remainder of x/y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 FMod(Double4 x, Double4 y) => new Double4(FMod(x.X, y.X), FMod(x.Y, y.Y), FMod(x.Z, y.Z), FMod(x.W, y.W));


        /// <summary>Returns the fractional part of a number.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Frac(float x) => x - Floor(x);
        /// <summary>Returns the fractional part of a number.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Frac(double x) => x - Floor(x);
        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Frac(Float2 x) => new Float2(Frac(x.X), Frac(x.Y));
        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Frac(Float3 x) => new Float3(Frac(x.X), Frac(x.Y), Frac(x.Z));
        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Frac(Float4 x) => new Float4(Frac(x.X), Frac(x.Y), Frac(x.Z), Frac(x.W));
        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Frac(Double2 x) => new Double2(Frac(x.X), Frac(x.Y));
        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Frac(Double3 x) => new Double3(Frac(x.X), Frac(x.Y), Frac(x.Z));
        /// <summary>Returns the componentwise fractional part of a vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Frac(Double4 x) => new Double4(Frac(x.X), Frac(x.Y), Frac(x.Z), Frac(x.W));


        /// <summary>Returns the natural logarithm of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Log(float x) => MathF.Log(x);
        /// <summary>Returns the natural logarithm of x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Log(double x) => Math.Log(x);
        /// <summary>Returns the componentwise natural logarithm.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Log(Float2 x) => new Float2(Log(x.X), Log(x.Y));
        /// <summary>Returns the componentwise natural logarithm.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Log(Float3 x) => new Float3(Log(x.X), Log(x.Y), Log(x.Z));
        /// <summary>Returns the componentwise natural logarithm.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Log(Float4 x) => new Float4(Log(x.X), Log(x.Y), Log(x.Z), Log(x.W));
        /// <summary>Returns the componentwise natural logarithm.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Log(Double2 x) => new Double2(Log(x.X), Log(x.Y));
        /// <summary>Returns the componentwise natural logarithm.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Log(Double3 x) => new Double3(Log(x.X), Log(x.Y), Log(x.Z));
        /// <summary>Returns the componentwise natural logarithm.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Log(Double4 x) => new Double4(Log(x.X), Log(x.Y), Log(x.Z), Log(x.W));


        /// <summary>Splits a floating-point value into integer and fractional parts.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ModF(float x, out float integerPart)
        {
            integerPart = Floor(x);
            return x - integerPart;
        }
        /// <summary>Splits a floating-point value into integer and fractional parts.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ModF(double x, out double integerPart)
        {
            integerPart = Floor(x);
            return x - integerPart;
        }
        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 ModF(Float2 x, out Float2 integer)
        {
            integer = new Float2();
            return new Float2(ModF(x.X, out integer.X), ModF(x.Y, out integer.Y));
        }
        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 ModF(Float3 x, out Float3 integer)
        {
            integer = new Float3();
            return new Float3(ModF(x.X, out integer.X), ModF(x.Y, out integer.Y), ModF(x.Z, out integer.Z));
        }
        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 ModF(Float4 x, out Float4 integer)
        {
            integer = new Float4();
            return new Float4(ModF(x.X, out integer.X), ModF(x.Y, out integer.Y), ModF(x.Z, out integer.Z), ModF(x.W, out integer.W));
        }
        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 ModF(Double2 x, out Double2 integer)
        {
            integer = new Double2();
            return new Double2(ModF(x.X, out integer.X), ModF(x.Y, out integer.Y));
        }
        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 ModF(Double3 x, out Double3 integer)
        {
            integer = new Double3();
            return new Double3(ModF(x.X, out integer.X), ModF(x.Y, out integer.Y), ModF(x.Z, out integer.Z));
        }
        /// <summary>Splits a vector into integer and fractional parts componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 ModF(Double4 x, out Double4 integer)
        {
            integer = new Double4();
            return new Double4(ModF(x.X, out integer.X), ModF(x.Y, out integer.Y), ModF(x.Z, out integer.Z), ModF(x.W, out integer.W));
        }


        /// <summary>Returns x raised to the power of y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Pow(float x, float y) => MathF.Pow(x, y);
        /// <summary>Returns x raised to the power of y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Pow(double x, double y) => Math.Pow(x, y);
        /// <summary>Returns the componentwise power of x raised to y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Pow(Float2 x, Float2 y) => new Float2(Pow(x.X, y.X), Pow(x.Y, y.Y));
        /// <summary>Returns the componentwise power of x raised to y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Pow(Float3 x, Float3 y) => new Float3(Pow(x.X, y.X), Pow(x.Y, y.Y), Pow(x.Z, y.Z));
        /// <summary>Returns the componentwise power of x raised to y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Pow(Float4 x, Float4 y) => new Float4(Pow(x.X, y.X), Pow(x.Y, y.Y), Pow(x.Z, y.Z), Pow(x.W, y.W));
        /// <summary>Returns the componentwise power of x raised to y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Pow(Double2 x, Double2 y) => new Double2(Pow(x.X, y.X), Pow(x.Y, y.Y));
        /// <summary>Returns the componentwise power of x raised to y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Pow(Double3 x, Double3 y) => new Double3(Pow(x.X, y.X), Pow(x.Y, y.Y), Pow(x.Z, y.Z));
        /// <summary>Returns the componentwise power of x raised to y.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Pow(Double4 x, Double4 y) => new Double4(Pow(x.X, y.X), Pow(x.Y, y.Y), Pow(x.Z, y.Z), Pow(x.W, y.W));


        /// <summary>Returns the reciprocal square root (1/sqrt) of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Rsqrt(float x) => 1.0f / MathF.Sqrt(x);
        /// <summary>Returns the reciprocal square root (1/sqrt) of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Rsqrt(double x) => 1.0 / Math.Sqrt(x);
        /// <summary>Returns the componentwise reciprocal square root (1/sqrt) of the Float2 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Rsqrt(Float2 x) => new Float2(Rsqrt(x.X), Rsqrt(x.Y));
        /// <summary>Returns the componentwise reciprocal square root (1/sqrt) of the Float3 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Rsqrt(Float3 x) => new Float3(Rsqrt(x.X), Rsqrt(x.Y), Rsqrt(x.Z));
        /// <summary>Returns the componentwise reciprocal square root (1/sqrt) of the Float4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Rsqrt(Float4 x) => new Float4(Rsqrt(x.X), Rsqrt(x.Y), Rsqrt(x.Z), Rsqrt(x.W));
        /// <summary>Returns the componentwise reciprocal square root (1/sqrt) of the Double2 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Rsqrt(Double2 x) => new Double2(Rsqrt(x.X), Rsqrt(x.Y));
        /// <summary>Returns the componentwise reciprocal square root (1/sqrt) of the Double3 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Rsqrt(Double3 x) => new Double3(Rsqrt(x.X), Rsqrt(x.Y), Rsqrt(x.Z));
        /// <summary>Returns the componentwise reciprocal square root (1/sqrt) of the Double4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Rsqrt(Double4 x) => new Double4(Rsqrt(x.X), Rsqrt(x.Y), Rsqrt(x.Z), Rsqrt(x.W));


        /// <summary>Wraps the given value between 0 and length.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Repeat(float t, float length) => Clamp(t - MathF.Floor(t / length) * length, 0f, length);
        /// <summary>Wraps the given value between 0 and length.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Repeat(double t, double length) => Clamp(t - Math.Floor(t / length) * length, 0, length);
        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Repeat(Float2 t, Float2 length) => new Float2(Repeat(t.X, length.X), Repeat(t.Y, length.Y));
        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Repeat(Float3 t, Float3 length) => new Float3(Repeat(t.X, length.X), Repeat(t.Y, length.Y), Repeat(t.Z, length.Z));
        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Repeat(Float4 t, Float4 length) => new Float4(Repeat(t.X, length.X), Repeat(t.Y, length.Y), Repeat(t.Z, length.Z), Repeat(t.W, length.W));
        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Repeat(Double2 t, Double2 length) => new Double2(Repeat(t.X, length.X), Repeat(t.Y, length.Y));
        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Repeat(Double3 t, Double3 length) => new Double3(Repeat(t.X, length.X), Repeat(t.Y, length.Y), Repeat(t.Z, length.Z));
        /// <summary>Wraps the given vector between 0 and length componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Repeat(Double4 t, Double4 length) => new Double4(Repeat(t.X, length.X), Repeat(t.Y, length.Y), Repeat(t.Z, length.Z), Repeat(t.W, length.W));


        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 when edge0 < x < edge1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 when edge0 < x < edge1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Smoothstep(double edge0, double edge1, double x)
        {
            double t = Saturate((x - edge0) / (edge1 - edge0));
            return t * t * (3 - 2 * t);
        }
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Smoothstep(Float2 edge0, Float2 edge1, Float2 x) => new Float2(Smoothstep(edge0.X, edge1.X, x.X), Smoothstep(edge0.Y, edge1.Y, x.Y));
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Smoothstep(Float3 edge0, Float3 edge1, Float3 x) => new Float3(Smoothstep(edge0.X, edge1.X, x.X), Smoothstep(edge0.Y, edge1.Y, x.Y), Smoothstep(edge0.Z, edge1.Z, x.Z));
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Smoothstep(Float4 edge0, Float4 edge1, Float4 x) => new Float4(Smoothstep(edge0.X, edge1.X, x.X), Smoothstep(edge0.Y, edge1.Y, x.Y), Smoothstep(edge0.Z, edge1.Z, x.Z), Smoothstep(edge0.W, edge1.W, x.W));
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Smoothstep(Double2 edge0, Double2 edge1, Double2 x) => new Double2(Smoothstep(edge0.X, edge1.X, x.X), Smoothstep(edge0.Y, edge1.Y, x.Y));
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Smoothstep(Double3 edge0, Double3 edge1, Double3 x) => new Double3(Smoothstep(edge0.X, edge1.X, x.X), Smoothstep(edge0.Y, edge1.Y, x.Y), Smoothstep(edge0.Z, edge1.Z, x.Z));
        /// <summary>Performs a smooth Hermite interpolation between 0 and 1 componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Smoothstep(Double4 edge0, Double4 edge1, Double4 x) => new Double4(Smoothstep(edge0.X, edge1.X, x.X), Smoothstep(edge0.Y, edge1.Y, x.Y), Smoothstep(edge0.Z, edge1.Z, x.Z), Smoothstep(edge0.W, edge1.W, x.W));


        /// <summary>Returns the sqrt of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt(float x) => MathF.Sqrt(x);
        /// <summary>Returns the sqrt of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Sqrt(double x) => Math.Sqrt(x);
        /// <summary>Returns the componentwise sqrt of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Sqrt(Float2 x) => new Float2(Sqrt(x.X), Sqrt(x.Y));
        /// <summary>Returns the componentwise sqrt of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Sqrt(Float3 x) => new Float3(Sqrt(x.X), Sqrt(x.Y), Sqrt(x.Z));
        /// <summary>Returns the componentwise sqrt of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Sqrt(Float4 x) => new Float4(Sqrt(x.X), Sqrt(x.Y), Sqrt(x.Z), Sqrt(x.W));
        /// <summary>Returns the componentwise sqrt of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Sqrt(Double2 x) => new Double2(Sqrt(x.X), Sqrt(x.Y));
        /// <summary>Returns the componentwise sqrt of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Sqrt(Double3 x) => new Double3(Sqrt(x.X), Sqrt(x.Y), Sqrt(x.Z));
        /// <summary>Returns the componentwise sqrt of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Sqrt(Double4 x) => new Double4(Sqrt(x.X), Sqrt(x.Y), Sqrt(x.Z), Sqrt(x.W));


        /// <summary>Returns 0 if x < edge, otherwise returns 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Step(float edge, float x) => x < edge ? 0f : 1f;
        /// <summary>Returns 0 if x < edge, otherwise returns 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Step(double edge, double x) => x < edge ? 0.0 : 1.0;
        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Step(Float2 edge, Float2 x) => new Float2(Step(edge.X, x.X), Step(edge.Y, x.Y));
        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Step(Float3 edge, Float3 x) => new Float3(Step(edge.X, x.X), Step(edge.Y, x.Y), Step(edge.Z, x.Z));
        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Step(Float4 edge, Float4 x) => new Float4(Step(edge.X, x.X), Step(edge.Y, x.Y), Step(edge.Z, x.Z), Step(edge.W, x.W));
        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Step(Double2 edge, Double2 x) => new Double2(Step(edge.X, x.X), Step(edge.Y, x.Y));
        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Step(Double3 edge, Double3 x) => new Double3(Step(edge.X, x.X), Step(edge.Y, x.Y), Step(edge.Z, x.Z));
        /// <summary>Returns 0 or 1 for each component based on the step function.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Step(Double4 edge, Double4 x) => new Double4(Step(edge.X, x.X), Step(edge.Y, x.Y), Step(edge.Z, x.Z), Step(edge.W, x.W));


        /// <summary>Returns the tan of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Tan(float x) => MathF.Tan(x);
        /// <summary>Returns the tan of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Tan(double x) => Math.Tan(x);
        /// <summary>Returns the componentwise tan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Tan(Float2 x) => new Float2(Tan(x.X), Tan(x.Y));
        /// <summary>Returns the componentwise tan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Tan(Float3 x) => new Float3(Tan(x.X), Tan(x.Y), Tan(x.Z));
        /// <summary>Returns the componentwise tan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Tan(Float4 x) => new Float4(Tan(x.X), Tan(x.Y), Tan(x.Z), Tan(x.W));
        /// <summary>Returns the componentwise tan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Tan(Double2 x) => new Double2(Tan(x.X), Tan(x.Y));
        /// <summary>Returns the componentwise tan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Tan(Double3 x) => new Double3(Tan(x.X), Tan(x.Y), Tan(x.Z));
        /// <summary>Returns the componentwise tan of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Tan(Double4 x) => new Double4(Tan(x.X), Tan(x.Y), Tan(x.Z), Tan(x.W));


        /// <summary>Calculates the shortest angle between two angles.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 2f * MathF.PI);
            if (delta > MathF.PI)
                delta -= 2f * MathF.PI;
            return delta;
        }
        /// <summary>Calculates the shortest angle between two angles.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double DeltaAngle(double current, double target)
        {
            double delta = Repeat(target - current, 2 * Math.PI);
            if (delta > Math.PI)
                delta -= 2 * Math.PI;
            return delta;
        }
        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 DeltaAngle(Float2 current, Float2 target) => new Float2(DeltaAngle(current.X, target.X), DeltaAngle(current.Y, target.Y));
        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 DeltaAngle(Float3 current, Float3 target) => new Float3(DeltaAngle(current.X, target.X), DeltaAngle(current.Y, target.Y), DeltaAngle(current.Z, target.Z));
        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 DeltaAngle(Float4 current, Float4 target) => new Float4(DeltaAngle(current.X, target.X), DeltaAngle(current.Y, target.Y), DeltaAngle(current.Z, target.Z), DeltaAngle(current.W, target.W));
        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 DeltaAngle(Double2 current, Double2 target) => new Double2(DeltaAngle(current.X, target.X), DeltaAngle(current.Y, target.Y));
        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 DeltaAngle(Double3 current, Double3 target) => new Double3(DeltaAngle(current.X, target.X), DeltaAngle(current.Y, target.Y), DeltaAngle(current.Z, target.Z));
        /// <summary>Calculates the shortest angle between two angle vectors componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 DeltaAngle(Double4 current, Double4 target) => new Double4(DeltaAngle(current.X, target.X), DeltaAngle(current.Y, target.Y), DeltaAngle(current.Z, target.Z), DeltaAngle(current.W, target.W));


        /// <summary>Remaps a value from one range to another.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Remap(float value, float inputMin, float inputMax, float outputMin, float outputMax) => outputMin + ((value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin));
        /// <summary>Remaps a value from one range to another.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Remap(double value, double inputMin, double inputMax, double outputMin, double outputMax) => outputMin + ((value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin));
        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Remap(Float2 value, Float2 inputMin, Float2 inputMax, Float2 outputMin, Float2 outputMax) => new Float2(Remap(value.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X), Remap(value.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y));
        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Remap(Float3 value, Float3 inputMin, Float3 inputMax, Float3 outputMin, Float3 outputMax) => new Float3(Remap(value.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X), Remap(value.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y), Remap(value.Z, inputMin.Z, inputMax.Z, outputMin.Z, outputMax.Z));
        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Remap(Float4 value, Float4 inputMin, Float4 inputMax, Float4 outputMin, Float4 outputMax) => new Float4(Remap(value.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X), Remap(value.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y), Remap(value.Z, inputMin.Z, inputMax.Z, outputMin.Z, outputMax.Z), Remap(value.W, inputMin.W, inputMax.W, outputMin.W, outputMax.W));
        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Remap(Double2 value, Double2 inputMin, Double2 inputMax, Double2 outputMin, Double2 outputMax) => new Double2(Remap(value.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X), Remap(value.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y));
        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Remap(Double3 value, Double3 inputMin, Double3 inputMax, Double3 outputMin, Double3 outputMax) => new Double3(Remap(value.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X), Remap(value.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y), Remap(value.Z, inputMin.Z, inputMax.Z, outputMin.Z, outputMax.Z));
        /// <summary>Remaps a vector from one range to another componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Remap(Double4 value, Double4 inputMin, Double4 inputMax, Double4 outputMin, Double4 outputMax) => new Double4(Remap(value.X, inputMin.X, inputMax.X, outputMin.X, outputMax.X), Remap(value.Y, inputMin.Y, inputMax.Y, outputMin.Y, outputMax.Y), Remap(value.Z, inputMin.Z, inputMax.Z, outputMin.Z, outputMax.Z), Remap(value.W, inputMin.W, inputMax.W, outputMin.W, outputMax.W));


        /// <summary>Returns the sign of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sign(float x) => MathF.Sign(x);
        /// <summary>Returns the sign of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Sign(double x) => Math.Sign(x);
        /// <summary>Returns the sign of int x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(int x) => Math.Sign(x);
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Sign(Float2 x) => new Float2(Sign(x.X), Sign(x.Y));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Sign(Float3 x) => new Float3(Sign(x.X), Sign(x.Y), Sign(x.Z));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Sign(Float4 x) => new Float4(Sign(x.X), Sign(x.Y), Sign(x.Z), Sign(x.W));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Sign(Double2 x) => new Double2(Sign(x.X), Sign(x.Y));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Sign(Double3 x) => new Double3(Sign(x.X), Sign(x.Y), Sign(x.Z));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Sign(Double4 x) => new Double4(Sign(x.X), Sign(x.Y), Sign(x.Z), Sign(x.W));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Sign(Int2 x) => new Int2(Sign(x.X), Sign(x.Y));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Sign(Int3 x) => new Int3(Sign(x.X), Sign(x.Y), Sign(x.Z));
        /// <summary>Returns the componentwise sign of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Sign(Int4 x) => new Int4(Sign(x.X), Sign(x.Y), Sign(x.Z), Sign(x.W));


        /// <summary>Returns the sin of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sin(float x) => MathF.Sin(x);
        /// <summary>Returns the sin of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Sin(double x) => Math.Sin(x);
        /// <summary>Returns the componentwise sin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Sin(Float2 x) => new Float2(Sin(x.X), Sin(x.Y));
        /// <summary>Returns the componentwise sin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Sin(Float3 x) => new Float3(Sin(x.X), Sin(x.Y), Sin(x.Z));
        /// <summary>Returns the componentwise sin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Sin(Float4 x) => new Float4(Sin(x.X), Sin(x.Y), Sin(x.Z), Sin(x.W));
        /// <summary>Returns the componentwise sin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Sin(Double2 x) => new Double2(Sin(x.X), Sin(x.Y));
        /// <summary>Returns the componentwise sin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Sin(Double3 x) => new Double3(Sin(x.X), Sin(x.Y), Sin(x.Z));
        /// <summary>Returns the componentwise sin of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Sin(Double4 x) => new Double4(Sin(x.X), Sin(x.Y), Sin(x.Z), Sin(x.W));

        #endregion


        #region Clamp-Like

        /// <summary>Returns the ceiling of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Ceiling(float x) => MathF.Ceiling(x);
        /// <summary>Returns the ceiling of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Ceiling(double x) => Math.Ceiling(x);
        /// <summary>Returns the componentwise ceiling of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Ceiling(Float2 x) => new Float2(Ceiling(x.X), Ceiling(x.Y));
        /// <summary>Returns the componentwise ceiling of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Ceiling(Float3 x) => new Float3(Ceiling(x.X), Ceiling(x.Y), Ceiling(x.Z));
        /// <summary>Returns the componentwise ceiling of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Ceiling(Float4 x) => new Float4(Ceiling(x.X), Ceiling(x.Y), Ceiling(x.Z), Ceiling(x.W));
        /// <summary>Returns the componentwise ceiling of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Ceiling(Double2 x) => new Double2(Ceiling(x.X), Ceiling(x.Y));
        /// <summary>Returns the componentwise ceiling of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Ceiling(Double3 x) => new Double3(Ceiling(x.X), Ceiling(x.Y), Ceiling(x.Z));
        /// <summary>Returns the componentwise ceiling of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Ceiling(Double4 x) => new Double4(Ceiling(x.X), Ceiling(x.Y), Ceiling(x.Z), Ceiling(x.W));


        /// <summary>Ceils a value to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
        /// <summary>Ceils a value to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CeilToInt(double x) => (int)Math.Ceiling(x);
        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 CeilToInt(Float2 x) => new Int2(CeilToInt(x.X), CeilToInt(x.Y));
        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 CeilToInt(Float3 x) => new Int3(CeilToInt(x.X), CeilToInt(x.Y), CeilToInt(x.Z));
        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 CeilToInt(Float4 x) => new Int4(CeilToInt(x.X), CeilToInt(x.Y), CeilToInt(x.Z), CeilToInt(x.W));
        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 CeilToInt(Double2 x) => new Int2(CeilToInt(x.X), CeilToInt(x.Y));
        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 CeilToInt(Double3 x) => new Int3(CeilToInt(x.X), CeilToInt(x.Y), CeilToInt(x.Z));
        /// <summary>Ceils each component to the nearest higher integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 CeilToInt(Double4 x) => new Int4(CeilToInt(x.X), CeilToInt(x.Y), CeilToInt(x.Z), CeilToInt(x.W));


        /// <summary>Clamps float x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float x, float min, float max) => (float)Math.Clamp(x, min, max);
        /// <summary>Clamps double x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double x, double min, double max) => (double)Math.Clamp(x, min, max);
        /// <summary>Clamps int x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int x, int min, int max) => (int)Math.Clamp(x, min, max);
        /// <summary>Clamps byte x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp(byte x, byte min, byte max) => (byte)Math.Clamp(x, min, max);
        /// <summary>Clamps ushort x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Clamp(ushort x, ushort min, ushort max) => (ushort)Math.Clamp(x, min, max);
        /// <summary>Clamps uint x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Clamp(uint x, uint min, uint max) => (uint)Math.Clamp(x, min, max);
        /// <summary>Clamps ulong x between min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Clamp(ulong x, ulong min, ulong max) => (ulong)Math.Clamp(x, min, max);
        /// <summary>Returns the componentwise clamp of a Float2 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Clamp(Float2 x, Float2 min, Float2 max) => new Float2(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y));

        /// <summary>Clamps each component of a Float2 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Clamp(Float2 x, float min, float max) => new Float2(Clamp(x.X, min, max), Clamp(x.Y, min, max));
        /// <summary>Returns the componentwise clamp of a Float3 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Clamp(Float3 x, Float3 min, Float3 max) => new Float3(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y), Clamp(x.Z, min.Z, max.Z));

        /// <summary>Clamps each component of a Float3 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Clamp(Float3 x, float min, float max) => new Float3(Clamp(x.X, min, max), Clamp(x.Y, min, max), Clamp(x.Z, min, max));
        /// <summary>Returns the componentwise clamp of a Float4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Clamp(Float4 x, Float4 min, Float4 max) => new Float4(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y), Clamp(x.Z, min.Z, max.Z), Clamp(x.W, min.W, max.W));

        /// <summary>Clamps each component of a Float4 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Clamp(Float4 x, float min, float max) => new Float4(Clamp(x.X, min, max), Clamp(x.Y, min, max), Clamp(x.Z, min, max), Clamp(x.W, min, max));
        /// <summary>Returns the componentwise clamp of a Double2 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Clamp(Double2 x, Double2 min, Double2 max) => new Double2(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y));

        /// <summary>Clamps each component of a Double2 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Clamp(Double2 x, double min, double max) => new Double2(Clamp(x.X, min, max), Clamp(x.Y, min, max));
        /// <summary>Returns the componentwise clamp of a Double3 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Clamp(Double3 x, Double3 min, Double3 max) => new Double3(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y), Clamp(x.Z, min.Z, max.Z));

        /// <summary>Clamps each component of a Double3 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Clamp(Double3 x, double min, double max) => new Double3(Clamp(x.X, min, max), Clamp(x.Y, min, max), Clamp(x.Z, min, max));
        /// <summary>Returns the componentwise clamp of a Double4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Clamp(Double4 x, Double4 min, Double4 max) => new Double4(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y), Clamp(x.Z, min.Z, max.Z), Clamp(x.W, min.W, max.W));

        /// <summary>Clamps each component of a Double4 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Clamp(Double4 x, double min, double max) => new Double4(Clamp(x.X, min, max), Clamp(x.Y, min, max), Clamp(x.Z, min, max), Clamp(x.W, min, max));
        /// <summary>Returns the componentwise clamp of a Int2 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Clamp(Int2 x, Int2 min, Int2 max) => new Int2(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y));

        /// <summary>Clamps each component of a Int2 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Clamp(Int2 x, int min, int max) => new Int2(Clamp(x.X, min, max), Clamp(x.Y, min, max));
        /// <summary>Returns the componentwise clamp of a Int3 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Clamp(Int3 x, Int3 min, Int3 max) => new Int3(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y), Clamp(x.Z, min.Z, max.Z));

        /// <summary>Clamps each component of a Int3 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Clamp(Int3 x, int min, int max) => new Int3(Clamp(x.X, min, max), Clamp(x.Y, min, max), Clamp(x.Z, min, max));
        /// <summary>Returns the componentwise clamp of a Int4 vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Clamp(Int4 x, Int4 min, Int4 max) => new Int4(Clamp(x.X, min.X, max.X), Clamp(x.Y, min.Y, max.Y), Clamp(x.Z, min.Z, max.Z), Clamp(x.W, min.W, max.W));

        /// <summary>Clamps each component of a Int4 vector between scalar min and max values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Clamp(Int4 x, int min, int max) => new Int4(Clamp(x.X, min, max), Clamp(x.Y, min, max), Clamp(x.Z, min, max), Clamp(x.W, min, max));


        /// <summary>Returns the floor of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Floor(float x) => MathF.Floor(x);
        /// <summary>Returns the floor of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Floor(double x) => Math.Floor(x);
        /// <summary>Returns the componentwise floor of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Floor(Float2 x) => new Float2(Floor(x.X), Floor(x.Y));
        /// <summary>Returns the componentwise floor of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Floor(Float3 x) => new Float3(Floor(x.X), Floor(x.Y), Floor(x.Z));
        /// <summary>Returns the componentwise floor of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Floor(Float4 x) => new Float4(Floor(x.X), Floor(x.Y), Floor(x.Z), Floor(x.W));
        /// <summary>Returns the componentwise floor of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Floor(Double2 x) => new Double2(Floor(x.X), Floor(x.Y));
        /// <summary>Returns the componentwise floor of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Floor(Double3 x) => new Double3(Floor(x.X), Floor(x.Y), Floor(x.Z));
        /// <summary>Returns the componentwise floor of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Floor(Double4 x) => new Double4(Floor(x.X), Floor(x.Y), Floor(x.Z), Floor(x.W));


        /// <summary>Floors a value to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(float x) => (int)MathF.Floor(x);
        /// <summary>Floors a value to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(double x) => (int)Math.Floor(x);
        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 FloorToInt(Float2 x) => new Int2(FloorToInt(x.X), FloorToInt(x.Y));
        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 FloorToInt(Float3 x) => new Int3(FloorToInt(x.X), FloorToInt(x.Y), FloorToInt(x.Z));
        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 FloorToInt(Float4 x) => new Int4(FloorToInt(x.X), FloorToInt(x.Y), FloorToInt(x.Z), FloorToInt(x.W));
        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 FloorToInt(Double2 x) => new Int2(FloorToInt(x.X), FloorToInt(x.Y));
        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 FloorToInt(Double3 x) => new Int3(FloorToInt(x.X), FloorToInt(x.Y), FloorToInt(x.Z));
        /// <summary>Floors each component to the nearest lower integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 FloorToInt(Double4 x) => new Int4(FloorToInt(x.X), FloorToInt(x.Y), FloorToInt(x.Z), FloorToInt(x.W));


        /// <summary>Returns the maximum of two float values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Max(float x, float y) => MathF.Max(x, y);
        /// <summary>Returns the maximum of two double values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Max(double x, double y) => Math.Max(x, y);
        /// <summary>Returns the maximum of two int values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Max(int x, int y) => Math.Max(x, y);
        /// <summary>Returns the maximum of two byte values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Max(byte x, byte y) => Math.Max(x, y);
        /// <summary>Returns the maximum of two ushort values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Max(ushort x, ushort y) => Math.Max(x, y);
        /// <summary>Returns the maximum of two uint values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Max(uint x, uint y) => Math.Max(x, y);
        /// <summary>Returns the maximum of two ulong values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Max(ulong x, ulong y) => Math.Max(x, y);
        /// <summary>Returns the componentwise maximum of two Float2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Max(Float2 x, Float2 y) => new Float2(Max(x.X, y.X), Max(x.Y, y.Y));
        /// <summary>Returns the componentwise maximum of a Float2 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Max(Float2 x, float scalar) => new Float2(Max(x.X, scalar), Max(x.Y, scalar));
        /// <summary>Returns the componentwise maximum of two Float3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Max(Float3 x, Float3 y) => new Float3(Max(x.X, y.X), Max(x.Y, y.Y), Max(x.Z, y.Z));
        /// <summary>Returns the componentwise maximum of a Float3 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Max(Float3 x, float scalar) => new Float3(Max(x.X, scalar), Max(x.Y, scalar), Max(x.Z, scalar));
        /// <summary>Returns the componentwise maximum of two Float4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Max(Float4 x, Float4 y) => new Float4(Max(x.X, y.X), Max(x.Y, y.Y), Max(x.Z, y.Z), Max(x.W, y.W));
        /// <summary>Returns the componentwise maximum of a Float4 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Max(Float4 x, float scalar) => new Float4(Max(x.X, scalar), Max(x.Y, scalar), Max(x.Z, scalar), Max(x.W, scalar));
        /// <summary>Returns the componentwise maximum of two Double2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Max(Double2 x, Double2 y) => new Double2(Max(x.X, y.X), Max(x.Y, y.Y));
        /// <summary>Returns the componentwise maximum of a Double2 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Max(Double2 x, double scalar) => new Double2(Max(x.X, scalar), Max(x.Y, scalar));
        /// <summary>Returns the componentwise maximum of two Double3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Max(Double3 x, Double3 y) => new Double3(Max(x.X, y.X), Max(x.Y, y.Y), Max(x.Z, y.Z));
        /// <summary>Returns the componentwise maximum of a Double3 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Max(Double3 x, double scalar) => new Double3(Max(x.X, scalar), Max(x.Y, scalar), Max(x.Z, scalar));
        /// <summary>Returns the componentwise maximum of two Double4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Max(Double4 x, Double4 y) => new Double4(Max(x.X, y.X), Max(x.Y, y.Y), Max(x.Z, y.Z), Max(x.W, y.W));
        /// <summary>Returns the componentwise maximum of a Double4 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Max(Double4 x, double scalar) => new Double4(Max(x.X, scalar), Max(x.Y, scalar), Max(x.Z, scalar), Max(x.W, scalar));
        /// <summary>Returns the componentwise maximum of two Int2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Max(Int2 x, Int2 y) => new Int2(Max(x.X, y.X), Max(x.Y, y.Y));
        /// <summary>Returns the componentwise maximum of a Int2 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Max(Int2 x, int scalar) => new Int2(Max(x.X, scalar), Max(x.Y, scalar));
        /// <summary>Returns the componentwise maximum of two Int3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Max(Int3 x, Int3 y) => new Int3(Max(x.X, y.X), Max(x.Y, y.Y), Max(x.Z, y.Z));
        /// <summary>Returns the componentwise maximum of a Int3 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Max(Int3 x, int scalar) => new Int3(Max(x.X, scalar), Max(x.Y, scalar), Max(x.Z, scalar));
        /// <summary>Returns the componentwise maximum of two Int4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Max(Int4 x, Int4 y) => new Int4(Max(x.X, y.X), Max(x.Y, y.Y), Max(x.Z, y.Z), Max(x.W, y.W));
        /// <summary>Returns the componentwise maximum of a Int4 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Max(Int4 x, int scalar) => new Int4(Max(x.X, scalar), Max(x.Y, scalar), Max(x.Z, scalar), Max(x.W, scalar));


        /// <summary>Returns the minimum of two float values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Min(float x, float y) => MathF.Min(x, y);
        /// <summary>Returns the minimum of two double values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Min(double x, double y) => Math.Min(x, y);
        /// <summary>Returns the minimum of two int values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Min(int x, int y) => Math.Min(x, y);
        /// <summary>Returns the minimum of two byte values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Min(byte x, byte y) => Math.Min(x, y);
        /// <summary>Returns the minimum of two ushort values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort Min(ushort x, ushort y) => Math.Min(x, y);
        /// <summary>Returns the minimum of two uint values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Min(uint x, uint y) => Math.Min(x, y);
        /// <summary>Returns the minimum of two ulong values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Min(ulong x, ulong y) => Math.Min(x, y);
        /// <summary>Returns the componentwise minimum of two Float2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Min(Float2 x, Float2 y) => new Float2(Min(x.X, y.X), Min(x.Y, y.Y));
        /// <summary>Returns the componentwise minimum of a Float2 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Min(Float2 x, float scalar) => new Float2(Min(x.X, scalar), Min(x.Y, scalar));
        /// <summary>Returns the componentwise minimum of two Float3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Min(Float3 x, Float3 y) => new Float3(Min(x.X, y.X), Min(x.Y, y.Y), Min(x.Z, y.Z));
        /// <summary>Returns the componentwise minimum of a Float3 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Min(Float3 x, float scalar) => new Float3(Min(x.X, scalar), Min(x.Y, scalar), Min(x.Z, scalar));
        /// <summary>Returns the componentwise minimum of two Float4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Min(Float4 x, Float4 y) => new Float4(Min(x.X, y.X), Min(x.Y, y.Y), Min(x.Z, y.Z), Min(x.W, y.W));
        /// <summary>Returns the componentwise minimum of a Float4 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Min(Float4 x, float scalar) => new Float4(Min(x.X, scalar), Min(x.Y, scalar), Min(x.Z, scalar), Min(x.W, scalar));
        /// <summary>Returns the componentwise minimum of two Double2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Min(Double2 x, Double2 y) => new Double2(Min(x.X, y.X), Min(x.Y, y.Y));
        /// <summary>Returns the componentwise minimum of a Double2 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Min(Double2 x, double scalar) => new Double2(Min(x.X, scalar), Min(x.Y, scalar));
        /// <summary>Returns the componentwise minimum of two Double3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Min(Double3 x, Double3 y) => new Double3(Min(x.X, y.X), Min(x.Y, y.Y), Min(x.Z, y.Z));
        /// <summary>Returns the componentwise minimum of a Double3 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Min(Double3 x, double scalar) => new Double3(Min(x.X, scalar), Min(x.Y, scalar), Min(x.Z, scalar));
        /// <summary>Returns the componentwise minimum of two Double4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Min(Double4 x, Double4 y) => new Double4(Min(x.X, y.X), Min(x.Y, y.Y), Min(x.Z, y.Z), Min(x.W, y.W));
        /// <summary>Returns the componentwise minimum of a Double4 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Min(Double4 x, double scalar) => new Double4(Min(x.X, scalar), Min(x.Y, scalar), Min(x.Z, scalar), Min(x.W, scalar));
        /// <summary>Returns the componentwise minimum of two Int2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Min(Int2 x, Int2 y) => new Int2(Min(x.X, y.X), Min(x.Y, y.Y));
        /// <summary>Returns the componentwise minimum of a Int2 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 Min(Int2 x, int scalar) => new Int2(Min(x.X, scalar), Min(x.Y, scalar));
        /// <summary>Returns the componentwise minimum of two Int3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Min(Int3 x, Int3 y) => new Int3(Min(x.X, y.X), Min(x.Y, y.Y), Min(x.Z, y.Z));
        /// <summary>Returns the componentwise minimum of a Int3 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 Min(Int3 x, int scalar) => new Int3(Min(x.X, scalar), Min(x.Y, scalar), Min(x.Z, scalar));
        /// <summary>Returns the componentwise minimum of two Int4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Min(Int4 x, Int4 y) => new Int4(Min(x.X, y.X), Min(x.Y, y.Y), Min(x.Z, y.Z), Min(x.W, y.W));
        /// <summary>Returns the componentwise minimum of a Int4 vector and a scalar value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 Min(Int4 x, int scalar) => new Int4(Min(x.X, scalar), Min(x.Y, scalar), Min(x.Z, scalar), Min(x.W, scalar));


        /// <summary>Returns the round of float x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Round(float x) => MathF.Round(x);
        /// <summary>Returns the round of double x.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Round(double x) => Math.Round(x);
        /// <summary>Returns the componentwise round of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Round(Float2 x) => new Float2(Round(x.X), Round(x.Y));
        /// <summary>Returns the componentwise round of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Round(Float3 x) => new Float3(Round(x.X), Round(x.Y), Round(x.Z));
        /// <summary>Returns the componentwise round of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Round(Float4 x) => new Float4(Round(x.X), Round(x.Y), Round(x.Z), Round(x.W));
        /// <summary>Returns the componentwise round of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Round(Double2 x) => new Double2(Round(x.X), Round(x.Y));
        /// <summary>Returns the componentwise round of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Round(Double3 x) => new Double3(Round(x.X), Round(x.Y), Round(x.Z));
        /// <summary>Returns the componentwise round of the vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Round(Double4 x) => new Double4(Round(x.X), Round(x.Y), Round(x.Z), Round(x.W));


        /// <summary>Rounds a value to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundToInt(float x) => (int)MathF.Round(x);
        /// <summary>Rounds a value to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundToInt(double x) => (int)Math.Round(x);
        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 RoundToInt(Float2 x) => new Int2(RoundToInt(x.X), RoundToInt(x.Y));
        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 RoundToInt(Float3 x) => new Int3(RoundToInt(x.X), RoundToInt(x.Y), RoundToInt(x.Z));
        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 RoundToInt(Float4 x) => new Int4(RoundToInt(x.X), RoundToInt(x.Y), RoundToInt(x.Z), RoundToInt(x.W));
        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int2 RoundToInt(Double2 x) => new Int2(RoundToInt(x.X), RoundToInt(x.Y));
        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int3 RoundToInt(Double3 x) => new Int3(RoundToInt(x.X), RoundToInt(x.Y), RoundToInt(x.Z));
        /// <summary>Rounds each component to the nearest integer.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Int4 RoundToInt(Double4 x) => new Int4(RoundToInt(x.X), RoundToInt(x.Y), RoundToInt(x.Z), RoundToInt(x.W));


        /// <summary>Clamps a value between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Saturate(float x) => Clamp(x, 0f, 1f);
        /// <summary>Clamps a value between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Saturate(double x) => Clamp(x, 0.0, 1.0);
        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Saturate(Float2 x) => new Float2(Saturate(x.X), Saturate(x.Y));
        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Saturate(Float3 x) => new Float3(Saturate(x.X), Saturate(x.Y), Saturate(x.Z));
        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Saturate(Float4 x) => new Float4(Saturate(x.X), Saturate(x.Y), Saturate(x.Z), Saturate(x.W));
        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Saturate(Double2 x) => new Double2(Saturate(x.X), Saturate(x.Y));
        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Saturate(Double3 x) => new Double3(Saturate(x.X), Saturate(x.Y), Saturate(x.Z));
        /// <summary>Clamps each component of a vector between 0 and 1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Saturate(Double4 x) => new Double4(Saturate(x.X), Saturate(x.Y), Saturate(x.Z), Saturate(x.W));

        #endregion


        #region Lerp-Like

        /// <summary>Finds the t value given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float InverseLerp(float a, float b, float value)
        {
            if (a != b)
                return (value - a) / (b - a);
            else
                return 0f;
        }
        /// <summary>Finds the t value given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double InverseLerp(double a, double b, double value)
        {
            if (a != b)
                return (value - a) / (b - a);
            else
                return 0;
        }
        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 InverseLerp(Float2 a, Float2 b, Float2 value) => new Float2(InverseLerp(a.X, b.X, value.X), InverseLerp(a.Y, b.Y, value.Y));
        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 InverseLerp(Float3 a, Float3 b, Float3 value) => new Float3(InverseLerp(a.X, b.X, value.X), InverseLerp(a.Y, b.Y, value.Y), InverseLerp(a.Z, b.Z, value.Z));
        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 InverseLerp(Float4 a, Float4 b, Float4 value) => new Float4(InverseLerp(a.X, b.X, value.X), InverseLerp(a.Y, b.Y, value.Y), InverseLerp(a.Z, b.Z, value.Z), InverseLerp(a.W, b.W, value.W));
        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 InverseLerp(Double2 a, Double2 b, Double2 value) => new Double2(InverseLerp(a.X, b.X, value.X), InverseLerp(a.Y, b.Y, value.Y));
        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 InverseLerp(Double3 a, Double3 b, Double3 value) => new Double3(InverseLerp(a.X, b.X, value.X), InverseLerp(a.Y, b.Y, value.Y), InverseLerp(a.Z, b.Z, value.Z));
        /// <summary>Finds the t value for each component given a, b, and the result of a lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 InverseLerp(Double4 a, Double4 b, Double4 value) => new Double4(InverseLerp(a.X, b.X, value.X), InverseLerp(a.Y, b.Y, value.Y), InverseLerp(a.Z, b.Z, value.Z), InverseLerp(a.W, b.W, value.W));


        /// <summary>Linearly interpolates between two float values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => a + (b - a) * Saturate(t);
        /// <summary>Linearly interpolates between two double values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Lerp(double a, double b, double t) => a + (b - a) * Saturate(t);
        /// <summary>Linearly interpolates between two Float2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 Lerp(Float2 a, Float2 b, float t) => new Float2(Lerp(a.X, b.X, Saturate(t)), Lerp(a.Y, b.Y, Saturate(t)));
        /// <summary>Linearly interpolates between two Float3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 Lerp(Float3 a, Float3 b, float t) => new Float3(Lerp(a.X, b.X, Saturate(t)), Lerp(a.Y, b.Y, Saturate(t)), Lerp(a.Z, b.Z, Saturate(t)));
        /// <summary>Linearly interpolates between two Float4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 Lerp(Float4 a, Float4 b, float t) => new Float4(Lerp(a.X, b.X, Saturate(t)), Lerp(a.Y, b.Y, Saturate(t)), Lerp(a.Z, b.Z, Saturate(t)), Lerp(a.W, b.W, Saturate(t)));
        /// <summary>Linearly interpolates between two Double2 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 Lerp(Double2 a, Double2 b, double t) => new Double2(Lerp(a.X, b.X, Saturate(t)), Lerp(a.Y, b.Y, Saturate(t)));
        /// <summary>Linearly interpolates between two Double3 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 Lerp(Double3 a, Double3 b, double t) => new Double3(Lerp(a.X, b.X, Saturate(t)), Lerp(a.Y, b.Y, Saturate(t)), Lerp(a.Z, b.Z, Saturate(t)));
        /// <summary>Linearly interpolates between two Double4 vectors.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 Lerp(Double4 a, Double4 b, double t) => new Double4(Lerp(a.X, b.X, Saturate(t)), Lerp(a.Y, b.Y, Saturate(t)), Lerp(a.Z, b.Z, Saturate(t)), Lerp(a.W, b.W, Saturate(t)));


        /// <summary>Linearly interpolates between two angles, taking the shortest path around the circle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = Repeat(b - a, 2f * MathF.PI);
            if (delta > MathF.PI)
                delta -= 2f * MathF.PI;
            return a + delta * Saturate(t);
        }
        /// <summary>Linearly interpolates between two angles, taking the shortest path around the circle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LerpAngle(double a, double b, double t)
        {
            double delta = Repeat(b - a, 2 * Math.PI);
            if (delta > Math.PI)
                delta -= 2 * Math.PI;
            return a + delta * Saturate(t);
        }
        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 LerpAngle(Float2 a, Float2 b, float t) => new Float2(LerpAngle(a.X, b.X, t), LerpAngle(a.Y, b.Y, t));
        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 LerpAngle(Float3 a, Float3 b, float t) => new Float3(LerpAngle(a.X, b.X, t), LerpAngle(a.Y, b.Y, t), LerpAngle(a.Z, b.Z, t));
        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 LerpAngle(Float4 a, Float4 b, float t) => new Float4(LerpAngle(a.X, b.X, t), LerpAngle(a.Y, b.Y, t), LerpAngle(a.Z, b.Z, t), LerpAngle(a.W, b.W, t));
        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 LerpAngle(Double2 a, Double2 b, double t) => new Double2(LerpAngle(a.X, b.X, t), LerpAngle(a.Y, b.Y, t));
        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 LerpAngle(Double3 a, Double3 b, double t) => new Double3(LerpAngle(a.X, b.X, t), LerpAngle(a.Y, b.Y, t), LerpAngle(a.Z, b.Z, t));
        /// <summary>Linearly interpolates between two angle vectors, taking the shortest path for each component.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 LerpAngle(Double4 a, Double4 b, double t) => new Double4(LerpAngle(a.X, b.X, t), LerpAngle(a.Y, b.Y, t), LerpAngle(a.Z, b.Z, t), LerpAngle(a.W, b.W, t));


        /// <summary>Linearly interpolates between two values without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        /// <summary>Linearly interpolates between two values without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double LerpUnclamped(double a, double b, double t) => a + (b - a) * t;
        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 LerpUnclamped(Float2 a, Float2 b, float t) => new Float2(LerpUnclamped(a.X, b.X, t), LerpUnclamped(a.Y, b.Y, t));
        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 LerpUnclamped(Float3 a, Float3 b, float t) => new Float3(LerpUnclamped(a.X, b.X, t), LerpUnclamped(a.Y, b.Y, t), LerpUnclamped(a.Z, b.Z, t));
        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 LerpUnclamped(Float4 a, Float4 b, float t) => new Float4(LerpUnclamped(a.X, b.X, t), LerpUnclamped(a.Y, b.Y, t), LerpUnclamped(a.Z, b.Z, t), LerpUnclamped(a.W, b.W, t));
        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 LerpUnclamped(Double2 a, Double2 b, double t) => new Double2(LerpUnclamped(a.X, b.X, t), LerpUnclamped(a.Y, b.Y, t));
        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 LerpUnclamped(Double3 a, Double3 b, double t) => new Double3(LerpUnclamped(a.X, b.X, t), LerpUnclamped(a.Y, b.Y, t), LerpUnclamped(a.Z, b.Z, t));
        /// <summary>Linearly interpolates between two vectors without clamping t.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 LerpUnclamped(Double4 a, Double4 b, double t) => new Double4(LerpUnclamped(a.X, b.X, t), LerpUnclamped(a.Y, b.Y, t), LerpUnclamped(a.Z, b.Z, t), LerpUnclamped(a.W, b.W, t));


        /// <summary>PingPongs the value t, so that it is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PingPong(float t, float length)
        {
            t = Repeat(t, length * 2f);
            return length - Abs(t - length);
        }
        /// <summary>PingPongs the value t, so that it is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double PingPong(double t, double length)
        {
            t = Repeat(t, length * 2);
            return length - Abs(t - length);
        }
        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 PingPong(Float2 t, Float2 length) => new Float2(PingPong(t.X, length.X), PingPong(t.Y, length.Y));
        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 PingPong(Float3 t, Float3 length) => new Float3(PingPong(t.X, length.X), PingPong(t.Y, length.Y), PingPong(t.Z, length.Z));
        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 PingPong(Float4 t, Float4 length) => new Float4(PingPong(t.X, length.X), PingPong(t.Y, length.Y), PingPong(t.Z, length.Z), PingPong(t.W, length.W));
        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 PingPong(Double2 t, Double2 length) => new Double2(PingPong(t.X, length.X), PingPong(t.Y, length.Y));
        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 PingPong(Double3 t, Double3 length) => new Double3(PingPong(t.X, length.X), PingPong(t.Y, length.Y), PingPong(t.Z, length.Z));
        /// <summary>PingPongs the vector t, so that each component is never larger than length and never smaller than 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 PingPong(Double4 t, Double4 length) => new Double4(PingPong(t.X, length.X), PingPong(t.Y, length.Y), PingPong(t.Z, length.Z), PingPong(t.W, length.W));


        /// <summary>Smoothly interpolates between two values using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SmoothLerp(float a, float b, float t) => Lerp(a, b, Smoothstep(0f, 1f, t));
        /// <summary>Smoothly interpolates between two values using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double SmoothLerp(double a, double b, double t) => Lerp(a, b, Smoothstep(0, 1, t));
        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 SmoothLerp(Float2 a, Float2 b, float t) => new Float2(SmoothLerp(a.X, b.X, t), SmoothLerp(a.Y, b.Y, t));
        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 SmoothLerp(Float3 a, Float3 b, float t) => new Float3(SmoothLerp(a.X, b.X, t), SmoothLerp(a.Y, b.Y, t), SmoothLerp(a.Z, b.Z, t));
        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 SmoothLerp(Float4 a, Float4 b, float t) => new Float4(SmoothLerp(a.X, b.X, t), SmoothLerp(a.Y, b.Y, t), SmoothLerp(a.Z, b.Z, t), SmoothLerp(a.W, b.W, t));
        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 SmoothLerp(Double2 a, Double2 b, double t) => new Double2(SmoothLerp(a.X, b.X, t), SmoothLerp(a.Y, b.Y, t));
        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 SmoothLerp(Double3 a, Double3 b, double t) => new Double3(SmoothLerp(a.X, b.X, t), SmoothLerp(a.Y, b.Y, t), SmoothLerp(a.Z, b.Z, t));
        /// <summary>Smoothly interpolates between two vectors using cubic Hermite interpolation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 SmoothLerp(Double4 a, Double4 b, double t) => new Double4(SmoothLerp(a.X, b.X, t), SmoothLerp(a.Y, b.Y, t), SmoothLerp(a.Z, b.Z, t), SmoothLerp(a.W, b.W, t));

        #endregion


        /// <summary>Converts radians to degrees.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToDegrees(float radians) => radians * 180f / MathF.PI;
        /// <summary>Converts radians to degrees.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToDegrees(double radians) => radians * 180 / Math.PI;
        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 ToDegrees(Float2 radians) => new Float2(ToDegrees(radians.X), ToDegrees(radians.Y));
        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 ToDegrees(Float3 radians) => new Float3(ToDegrees(radians.X), ToDegrees(radians.Y), ToDegrees(radians.Z));
        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 ToDegrees(Float4 radians) => new Float4(ToDegrees(radians.X), ToDegrees(radians.Y), ToDegrees(radians.Z), ToDegrees(radians.W));
        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 ToDegrees(Double2 radians) => new Double2(ToDegrees(radians.X), ToDegrees(radians.Y));
        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 ToDegrees(Double3 radians) => new Double3(ToDegrees(radians.X), ToDegrees(radians.Y), ToDegrees(radians.Z));
        /// <summary>Converts radians to degrees componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 ToDegrees(Double4 radians) => new Double4(ToDegrees(radians.X), ToDegrees(radians.Y), ToDegrees(radians.Z), ToDegrees(radians.W));


        /// <summary>Converts degrees to radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToRadians(float degrees) => degrees * MathF.PI / 180f;
        /// <summary>Converts degrees to radians.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToRadians(double degrees) => degrees * Math.PI / 180;
        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float2 ToRadians(Float2 degrees) => new Float2(ToRadians(degrees.X), ToRadians(degrees.Y));
        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float3 ToRadians(Float3 degrees) => new Float3(ToRadians(degrees.X), ToRadians(degrees.Y), ToRadians(degrees.Z));
        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4 ToRadians(Float4 degrees) => new Float4(ToRadians(degrees.X), ToRadians(degrees.Y), ToRadians(degrees.Z), ToRadians(degrees.W));
        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double2 ToRadians(Double2 degrees) => new Double2(ToRadians(degrees.X), ToRadians(degrees.Y));
        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double3 ToRadians(Double3 degrees) => new Double3(ToRadians(degrees.X), ToRadians(degrees.Y), ToRadians(degrees.Z));
        /// <summary>Converts degrees to radians componentwise.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Double4 ToRadians(Double4 degrees) => new Double4(ToRadians(degrees.X), ToRadians(degrees.Y), ToRadians(degrees.Z), ToRadians(degrees.W));

    }
}
