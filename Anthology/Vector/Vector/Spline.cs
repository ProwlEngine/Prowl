// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Linq;

namespace Prowl.Vector
{

    /// <summary>
    /// Represents a parametric spline curve in 3D space.
    /// </summary>
    public struct Spline : IEquatable<Spline>, IFormattable
    {
        /// <summary>
        /// Represents a coordinate frame along the spline.
        /// </summary>
        public struct SplineFrame
        {
            /// <summary>The position on the spline.</summary>
            public Double3 Position;
            
            /// <summary>The forward direction (tangent to the spline).</summary>
            public Double3 Forward;
            
            /// <summary>The up direction (binormal or twist-controlled up vector).</summary>
            public Double3 Up;
            
            /// <summary>The right direction (cross product of forward and up).</summary>
            public Double3 Right;
            
            /// <summary>The curvature at this point.</summary>
            public double Curvature;
            
            /// <summary>The torsion (twist) at this point.</summary>
            public double Torsion;

            public SplineFrame(Double3 position, Double3 forward, Double3 up, Double3 right, double curvature = 0.0, double torsion = 0.0)
            {
                Position = position;
                Forward = forward;
                Up = up;
                Right = right;
                Curvature = curvature;
                Torsion = torsion;
            }
        }

        /// <summary>
        /// Represents different types of spline interpolation.
        /// </summary>
        public enum SplineType
        {
            /// <summary>Linear interpolation between points.</summary>
            Linear,
            /// <summary>Catmull-Rom spline (cardinal spline with tension 0).</summary>
            CatmullRom,
            /// <summary>Cubic Bezier spline.</summary>
            Bezier,
            /// <summary>B-spline (uniform cubic B-spline).</summary>
            BSpline,
            /// <summary>Hermite spline with tangent control.</summary>
            Hermite
        }

        /// <summary>
        /// Method for calculating the up vector along the spline.
        /// </summary>
        public enum UpVectorMethod
        {
            /// <summary>Use a fixed world-space up vector (e.g., Y-up).</summary>
            FixedWorldUp,
            /// <summary>Use Frenet frame (normal from curvature).</summary>
            FrenetFrame,
            /// <summary>Use custom up vectors provided per control point.</summary>
            Custom
        }

        /// <summary>The control points of the spline.</summary>
        public Double3[] ControlPoints;

        /// <summary>The tangent vectors for Hermite splines.</summary>
        public Double3[]? Tangents;

        /// <summary>The type of spline interpolation.</summary>
        public SplineType Type;

        /// <summary>Whether the spline is closed (forms a loop).</summary>
        public bool IsClosed;

        /// <summary>Tension parameter for cardinal splines (0 = Catmull-Rom).</summary>
        public double Tension;

        /// <summary>Custom up vectors for UpVectorMethod.Custom.</summary>
        public Double3[]? CustomUpVectors;

        /// <summary>Fixed world up vector for UpVectorMethod.FixedWorldUp.</summary>
        public Double3 WorldUpVector;

        /// <summary>
        /// Initializes a new spline with the specified control points and type.
        /// </summary>
        /// <param name="controlPoints">The control points defining the spline.</param>
        /// <param name="type">The type of spline interpolation.</param>
        /// <param name="closed">Whether the spline should be closed.</param>
        /// <param name="tension">Tension parameter for cardinal splines.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Spline(Double3[] controlPoints, SplineType type = SplineType.CatmullRom, bool closed = false, double tension = 0.0)
        {
            ControlPoints = controlPoints ?? throw new ArgumentNullException(nameof(controlPoints));
            Tangents = null;
            Type = type;
            IsClosed = closed;
            Tension = tension;
            CustomUpVectors = null;
            WorldUpVector = Double3.UnitY;

            if (type == SplineType.Hermite)
            {
                // Initialize default tangents for Hermite splines
                GenerateDefaultTangents();
            }
        }

        /// <summary>
        /// Initializes a new Hermite spline with control points and tangents.
        /// </summary>
        /// <param name="controlPoints">The control points defining the spline.</param>
        /// <param name="tangents">The tangent vectors at each control point.</param>
        /// <param name="closed">Whether the spline should be closed.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Spline(Double3[] controlPoints, Double3[] tangents, bool closed = false)
        {
            ControlPoints = controlPoints ?? throw new ArgumentNullException(nameof(controlPoints));
            Tangents = tangents ?? throw new ArgumentNullException(nameof(tangents));
            Type = SplineType.Hermite;
            IsClosed = closed;
            Tension = 0.0;
            CustomUpVectors = null;
            WorldUpVector = Double3.UnitY;

            if (controlPoints.Length != tangents.Length)
                throw new ArgumentException("Control points and tangents arrays must have the same length.");
        }

        /// <summary>
        /// Gets the number of control points in the spline.
        /// </summary>
        public int ControlPointCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ControlPoints?.Length ?? 0;
        }

        /// <summary>
        /// Gets the number of segments in the spline.
        /// </summary>
        public int SegmentCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (ControlPoints == null || ControlPoints.Length < 2) return 0;
                
                return Type switch
                {
                    SplineType.Bezier => (ControlPoints.Length - 1) / 3,
                    _ => IsClosed ? ControlPoints.Length : ControlPoints.Length - 1
                };
            }
        }

        /// <summary>
        /// Evaluates the spline at the given parameter t.
        /// </summary>
        /// <param name="t">Parameter value (0 to 1 for the entire spline).</param>
        /// <returns>The position on the spline at parameter t.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 Evaluate(double t)
        {
            if (ControlPoints == null || ControlPoints.Length == 0)
                return Double3.Zero;

            if (ControlPoints.Length == 1)
                return ControlPoints[0];

            // Clamp t to [0, 1]
            t = Maths.Clamp(t, 0.0, 1.0);

            return Type switch
            {
                SplineType.Linear => EvaluateLinear(t),
                SplineType.CatmullRom => EvaluateCatmullRom(t),
                SplineType.Bezier => EvaluateBezier(t),
                SplineType.BSpline => EvaluateBSpline(t),
                SplineType.Hermite => EvaluateHermite(t),
                _ => EvaluateLinear(t)
            };
        }

        /// <summary>
        /// Evaluates the first derivative (tangent) of the spline at parameter t.
        /// </summary>
        /// <param name="t">Parameter value (0 to 1).</param>
        /// <returns>The tangent vector at parameter t.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Double3 EvaluateDerivative(double t)
        {
            if (ControlPoints == null || ControlPoints.Length < 2)
                return Double3.Zero;

            t = Maths.Clamp(t, 0.0, 1.0);

            return Type switch
            {
                SplineType.Linear => EvaluateLinearDerivative(t),
                SplineType.CatmullRom => EvaluateCatmullRomDerivative(t),
                SplineType.Bezier => EvaluateBezierDerivative(t),
                SplineType.BSpline => EvaluateBSplineDerivative(t),
                SplineType.Hermite => EvaluateHermiteDerivative(t),
                _ => EvaluateLinearDerivative(t)
            };
        }

        /// <summary>
        /// Evaluates a complete coordinate frame (position, forward, up, right) at parameter t.
        /// </summary>
        /// <param name="t">Parameter value (0 to 1).</param>
        /// <param name="upMethod">Method for calculating the up vector.</param>
        /// <returns>A complete coordinate frame at parameter t.</returns>
        public SplineFrame EvaluateFrame(double t, UpVectorMethod upMethod = UpVectorMethod.FixedWorldUp)
        {
            if (ControlPoints == null || ControlPoints.Length < 2)
                return new SplineFrame(Double3.Zero, Double3.UnitZ, Double3.UnitY, Double3.UnitX);

            t = Maths.Clamp(t, 0.0, 1.0);

            // Get position and first derivative (tangent)
            Double3 position = Evaluate(t);
            Double3 firstDerivative = EvaluateDerivative(t);
            
            // Normalize the forward direction
            Double3 forward = Double3.Normalize(firstDerivative);
            
            // Handle degenerate case
            if (Double3.LengthSquared(forward) < double.Epsilon * double.Epsilon)
            {
                forward = Double3.UnitZ;
            }

            Double3 up, right;
            double curvature = 0.0;
            double torsion = 0.0;

            switch (upMethod)
            {
                case UpVectorMethod.FixedWorldUp:
                    {
                        Double3 worldUp = WorldUpVector.Equals(Double3.Zero) ? Double3.UnitY : Double3.Normalize(WorldUpVector);
                        right = Double3.Normalize(Double3.Cross(forward, worldUp));
                        
                        // Handle case where forward is parallel to world up
                        if (Double3.LengthSquared(right) < double.Epsilon * double.Epsilon)
                        {
                            // Choose an arbitrary perpendicular vector
                            Double3 arbitrary = Maths.Abs(forward.X) < (double)0.9 ? Double3.UnitX : Double3.UnitZ;
                            right = Double3.Normalize(Double3.Cross(forward, arbitrary));
                        }
                        
                        up = Double3.Cross(right, forward);
                    }
                    break;

                case UpVectorMethod.FrenetFrame:
                    {
                        // Calculate second derivative for curvature
                        Double3 secondDerivative = EvaluateSecondDerivative(t);
                        Double3 normal = secondDerivative - Double3.Dot(secondDerivative, forward) * forward;
                        
                        double normalLength = Double3.Length(normal);
                        if (normalLength > double.Epsilon)
                        {
                            up = normal / normalLength;
                            curvature = normalLength / Maths.Max(Double3.LengthSquared(firstDerivative), double.Epsilon);
                        }
                        else
                        {
                            // Fallback to world up when curvature is zero
                            Double3 worldUp = Double3.UnitY;
                            up = worldUp - Double3.Dot(worldUp, forward) * forward;
                            if (Double3.LengthSquared(up) < double.Epsilon * double.Epsilon)
                            {
                                Double3 arbitrary = Maths.Abs(forward.X) < (double)0.9 ? Double3.UnitX : Double3.UnitZ;
                                up = Double3.Normalize(Double3.Cross(Double3.Cross(forward, arbitrary), forward));
                            }
                            else
                            {
                                up = Double3.Normalize(up);
                            }
                        }
                        
                        right = Double3.Cross(forward, up);
                        
                        // Calculate torsion if we have enough derivatives
                        Double3 thirdDerivative = EvaluateThirdDerivative(t);
                        Double3 crossFirstSecond = Double3.Cross(firstDerivative, secondDerivative);
                        double crossLength = Double3.Length(crossFirstSecond);
                        if (crossLength > double.Epsilon)
                        {
                            torsion = Double3.Dot(crossFirstSecond, thirdDerivative) / (crossLength * crossLength);
                        }
                    }
                    break;

                case UpVectorMethod.Custom:
                    {
                        if (CustomUpVectors != null && CustomUpVectors.Length > 0)
                        {
                            Double3 customUp = InterpolateCustomUpVector(t);
                            // Project custom up vector onto plane perpendicular to forward
                            up = customUp - Double3.Dot(customUp, forward) * forward;
                            if (Double3.LengthSquared(up) < double.Epsilon * double.Epsilon)
                            {
                                // Fallback if custom up is parallel to forward
                                Double3 arbitrary = Maths.Abs(forward.X) < (double)0.9 ? Double3.UnitX : Double3.UnitZ;
                                up = Double3.Normalize(Double3.Cross(Double3.Cross(forward, arbitrary), forward));
                            }
                            else
                            {
                                up = Double3.Normalize(up);
                            }
                            right = Double3.Cross(forward, up);
                        }
                        else
                        {
                            // Fallback to fixed world up
                            Double3 worldUp = Double3.UnitY;
                            right = Double3.Normalize(Double3.Cross(forward, worldUp));
                            if (Double3.LengthSquared(right) < double.Epsilon * double.Epsilon)
                            {
                                Double3 arbitrary = Maths.Abs(forward.X) < (double)0.9 ? Double3.UnitX : Double3.UnitZ;
                                right = Double3.Normalize(Double3.Cross(forward, arbitrary));
                            }
                            up = Double3.Cross(right, forward);
                        }
                    }
                    break;

                default:
                    up = Double3.UnitY;
                    right = Double3.UnitX;
                    break;
            }

            return new SplineFrame(position, forward, up, right, curvature, torsion);
        }

        /// <summary>
        /// Evaluates multiple frames along the spline.
        /// </summary>
        /// <param name="sampleCount">Number of frames to generate.</param>
        /// <param name="upMethod">Method for calculating up vectors.</param>
        /// <returns>Array of coordinate frames along the spline.</returns>
        public SplineFrame[] EvaluateFrames(int sampleCount, UpVectorMethod upMethod = UpVectorMethod.FixedWorldUp)
        {
            if (sampleCount < 2)
                throw new ArgumentException("Sample count must be at least 2", nameof(sampleCount));

            var frames = new SplineFrame[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double t = i / (double)(sampleCount - 1);
                frames[i] = EvaluateFrame(t, upMethod);
            }

            return frames;
        }

        /// <summary>
        /// Sets custom up vectors for UpVectorMethod.Custom.
        /// </summary>
        /// <param name="upVectors">Up vectors corresponding to control points.</param>
        public void SetCustomUpVectors(Double3[] upVectors)
        {
            if (upVectors == null)
            {
                CustomUpVectors = null;
                return;
            }

            CustomUpVectors = new Double3[upVectors.Length];
            Array.Copy(upVectors, CustomUpVectors, upVectors.Length);
        }

        /// <summary>
        /// Sets the world up vector for UpVectorMethod.FixedWorldUp.
        /// </summary>
        /// <param name="worldUp">The world up vector.</param>
        public void SetWorldUpVector(Double3 worldUp)
        {
            WorldUpVector = worldUp;
        }

        #region Private Frame Calculation Methods

        private Double3 EvaluateSecondDerivative(double t)
        {
            // Numerical approximation of second derivative
            const double h = double.Epsilon * 1000; // Small step
            double t1 = Maths.Max(t - h, 0.0);
            double t2 = Maths.Min(t + h, 1.0);
            
            Double3 d1 = EvaluateDerivative(t1);
            Double3 d2 = EvaluateDerivative(t2);
            
            return (d2 - d1) / (t2 - t1);
        }

        private Double3 EvaluateThirdDerivative(double t)
        {
            // Numerical approximation of third derivative
            const double h = double.Epsilon * 1000;
            double t1 = Maths.Max(t - h, 0.0);
            double t2 = Maths.Min(t + h, 1.0);
            
            Double3 d1 = EvaluateSecondDerivative(t1);
            Double3 d2 = EvaluateSecondDerivative(t2);
            
            return (d2 - d1) / (t2 - t1);
        }

        private Double3 InterpolateCustomUpVector(double t)
        {
            if (CustomUpVectors == null || CustomUpVectors.Length == 0)
                return Double3.UnitY;

            if (CustomUpVectors.Length == 1)
                return CustomUpVectors[0];

            // Interpolate between custom up vectors based on control point positions
            double scaledT = t * (CustomUpVectors.Length - 1);
            int index = (int)Maths.Floor(scaledT);
            double localT = scaledT - index;

            if (index >= CustomUpVectors.Length - 1)
                return CustomUpVectors[CustomUpVectors.Length - 1];

            // Use spherical linear interpolation for smooth rotation
            return Slerp(CustomUpVectors[index], CustomUpVectors[index + 1], localT);
        }

        private Double3 Slerp(Double3 a, Double3 b, double t)
        {
            // Spherical linear interpolation
            double dot = Maths.Clamp(Double3.Dot(a, b), -1.0, 1.0);
            
            if (Maths.Abs(dot) > (double)0.9995)
            {
                // Vectors are nearly parallel, use linear interpolation
                return Double3.Normalize(Maths.Lerp(a, b, t));
            }
            
            double theta = Maths.Acos(Maths.Abs(dot));
            double sinTheta = Maths.Sin(theta);
            
            double wa = Maths.Sin((1.0 - t) * theta) / sinTheta;
            double wb = Maths.Sin(t * theta) / sinTheta;
            
            if (dot < 0.0)
                wb = -wb;
                
            return Double3.Normalize(a * wa + b * wb);
        }

        #endregion

        /// <summary>
        /// Creates a transformation matrix from a spline frame.
        /// </summary>
        /// <param name="frame">The spline frame.</param>
        /// <returns>A 4x4 transformation matrix.</returns>
        public static Double4x4 FrameToMatrix(SplineFrame frame)
        {
            return new Double4x4(
                frame.Right.X, frame.Up.X, frame.Forward.X, frame.Position.X,
                frame.Right.Y, frame.Up.Y, frame.Forward.Y, frame.Position.Y,
                frame.Right.Z, frame.Up.Z, frame.Forward.Z, frame.Position.Z,
                0.0, 0.0, 0.0, 1.0
            );
        }

        /// <summary>
        /// Transforms a local offset by a spline frame.
        /// </summary>
        /// <param name="frame">The spline frame.</param>
        /// <param name="localOffset">Local offset (X=right, Y=up, Z=forward).</param>
        /// <returns>World space position.</returns>
        public static Double3 TransformByFrame(SplineFrame frame, Double3 localOffset)
        {
            return frame.Position + 
                   frame.Right * localOffset.X + 
                   frame.Up * localOffset.Y + 
                   frame.Forward * localOffset.Z;
        }

        /// <summary>
        /// Gets the approximate length of the spline using adaptive sampling.
        /// </summary>
        /// <param name="subdivisions">Number of subdivisions for length calculation.</param>
        /// <returns>The approximate length of the spline.</returns>
        public double GetLength(int subdivisions = 100)
        {
            if (ControlPoints == null || ControlPoints.Length < 2)
                return 0.0;

            double length = 0.0;
            Double3 previousPoint = Evaluate(0.0);

            for (int i = 1; i <= subdivisions; i++)
            {
                double t = i / (double)subdivisions;
                Double3 currentPoint = Evaluate(t);
                length += Double3.Length(currentPoint - previousPoint);
                previousPoint = currentPoint;
            }

            return length;
        }

        /// <summary>
        /// Finds the parameter t that corresponds to the given arc length.
        /// </summary>
        /// <param name="targetLength">The target arc length.</param>
        /// <param name="tolerance">Tolerance for the binary search.</param>
        /// <returns>The parameter t corresponding to the target length.</returns>
        public double GetParameterAtLength(double targetLength, double tolerance = double.Epsilon * 1000)
        {
            if (targetLength <= 0.0) return 0.0;

            double totalLength = GetLength();
            if (targetLength >= totalLength) return 1.0;

            // Binary search for the parameter
            double tMin = 0.0;
            double tMax = 1.0;
            double t = targetLength / totalLength; // Initial guess

            for (int iteration = 0; iteration < 50; iteration++) // Max iterations
            {
                double currentLength = GetLengthUpToParameter(t);
                double error = currentLength - targetLength;

                if (Maths.Abs(error) < tolerance)
                    break;

                if (error > 0.0)
                    tMax = t;
                else
                    tMin = t;

                t = (tMin + tMax) / 2.0;
            }

            return t;
        }

        /// <summary>
        /// Gets the length of the spline from t=0 to the given parameter.
        /// </summary>
        /// <param name="t">The parameter to measure length to.</param>
        /// <param name="subdivisions">Number of subdivisions for calculation.</param>
        /// <returns>The length from start to parameter t.</returns>
        public double GetLengthUpToParameter(double t, int subdivisions = 100)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return GetLength(subdivisions);

            double length = 0.0;
            Double3 previousPoint = Evaluate(0.0);
            int steps = (int)(subdivisions * t);

            for (int i = 1; i <= steps; i++)
            {
                double currentT = (i / (double)subdivisions);
                Double3 currentPoint = Evaluate(currentT);
                length += Double3.Length(currentPoint - previousPoint);
                previousPoint = currentPoint;
            }

            return length;
        }

        /// <summary>
        /// Samples points along the spline at uniform parameter intervals.
        /// </summary>
        /// <param name="sampleCount">Number of samples to generate.</param>
        /// <returns>Array of sampled points.</returns>
        public Double3[] SampleUniform(int sampleCount)
        {
            if (sampleCount < 2)
                throw new ArgumentException("Sample count must be at least 2", nameof(sampleCount));

            var samples = new Double3[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double t = i / (double)(sampleCount - 1);
                samples[i] = Evaluate(t);
            }

            return samples;
        }

        /// <summary>
        /// Samples points along the spline at uniform arc length intervals.
        /// </summary>
        /// <param name="sampleCount">Number of samples to generate.</param>
        /// <returns>Array of sampled points with uniform spacing.</returns>
        public Double3[] SampleUniformLength(int sampleCount)
        {
            if (sampleCount < 2)
                throw new ArgumentException("Sample count must be at least 2", nameof(sampleCount));

            var samples = new Double3[sampleCount];
            double totalLength = GetLength();

            for (int i = 0; i < sampleCount; i++)
            {
                double targetLength = (i / (double)(sampleCount - 1)) * totalLength;
                double t = GetParameterAtLength(targetLength);
                samples[i] = Evaluate(t);
            }

            return samples;
        }

        /// <summary>
        /// Finds the closest point on the spline to a given point.
        /// </summary>
        /// <param name="point">The point to find the closest point to.</param>
        /// <param name="subdivisions">Number of subdivisions for the search.</param>
        /// <returns>The closest point on the spline and its parameter.</returns>
        public (Double3 Point, double Parameter) GetClosestPoint(Double3 point, int subdivisions = 100)
        {
            double minDistance = double.MaxValue;
            double closestT = 0.0;
            Double3 closestPoint = Double3.Zero;

            // Coarse search
            for (int i = 0; i <= subdivisions; i++)
            {
                double t = i / (double)subdivisions;
                Double3 splinePoint = Evaluate(t);
                double distance = Double3.LengthSquared(point - splinePoint);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestT = t;
                    closestPoint = splinePoint;
                }
            }

            // Refine with Newton-Raphson if we have derivatives
            if (Type != SplineType.Linear)
            {
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    Double3 splinePoint = Evaluate(closestT);
                    Double3 derivative = EvaluateDerivative(closestT);
                    
                    Double3 diff = splinePoint - point;
                    double numerator = Double3.Dot(diff, derivative);
                    double denominator = Double3.Dot(derivative, derivative);

                    if (Maths.Abs(denominator) < double.Epsilon)
                        break;

                    double newT = closestT - numerator / denominator;
                    newT = Maths.Clamp(newT, 0.0, 1.0);

                    if (Maths.Abs(newT - closestT) < double.Epsilon)
                        break;

                    closestT = newT;
                    closestPoint = Evaluate(closestT);
                }
            }

            return (closestPoint, closestT);
        }

        #region Private Evaluation Methods

        private Double3 EvaluateLinear(double t)
        {
            if (ControlPoints.Length == 1) return ControlPoints[0];

            double scaledT = t * (ControlPoints.Length - 1);
            int index = (int)Maths.Floor(scaledT);
            double localT = scaledT - index;

            if (index >= ControlPoints.Length - 1)
            {
                return IsClosed ? 
                    Maths.Lerp(ControlPoints[ControlPoints.Length - 1], ControlPoints[0], localT) :
                    ControlPoints[ControlPoints.Length - 1];
            }

            Double3 p0 = ControlPoints[index];
            Double3 p1 = ControlPoints[index + 1];
            return Maths.Lerp(p0, p1, localT);
        }

        private Double3 EvaluateCatmullRom(double t)
        {
            int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 1;
            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            // Get the four control points for Catmull-Rom
            Double3 p0, p1, p2, p3;
            GetCatmullRomPoints(segment, out p0, out p1, out p2, out p3);

            // Catmull-Rom interpolation
            double t2 = localT * localT;
            double t3 = t2 * localT;

            double b0 = -Tension * t3 + 2.0 * Tension * t2 - Tension * localT;
            double b1 = (2.0 - Tension) * t3 + (Tension - 3) * t2 + 1.0;
            double b2 = (Tension - 2.0) * t3 + (3 - 2.0 * Tension) * t2 + Tension * localT;
            double b3 = Tension * t3 - Tension * t2;

            return p0 * b0 + p1 * b1 + p2 * b2 + p3 * b3;
        }

        private Double3 EvaluateBezier(double t)
        {
            int segmentCount = (ControlPoints.Length - 1) / 3;
            if (segmentCount == 0) return ControlPoints[0];

            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            int baseIndex = segment * 3;
            Double3 p0 = ControlPoints[baseIndex];
            Double3 p1 = ControlPoints[baseIndex + 1];
            Double3 p2 = ControlPoints[baseIndex + 2];
            Double3 p3 = ControlPoints[baseIndex + 3];

            // Cubic Bezier evaluation
            double invT = 1.0 - localT;
            double invT2 = invT * invT;
            double invT3 = invT2 * invT;
            double t2 = localT * localT;
            double t3 = t2 * localT;

            return p0 * invT3 + p1 * (3 * invT2 * localT) + p2 * (3 * invT * t2) + p3 * t3;
        }

        private Double3 EvaluateBSpline(double t)
        {
            if (ControlPoints.Length < 4) return EvaluateLinear(t);

            int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 3;
            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            // Get four control points for B-spline
            Double3 p0, p1, p2, p3;
            GetBSplinePoints(segment, out p0, out p1, out p2, out p3);

            // Uniform cubic B-spline basis functions
            double t2 = localT * localT;
            double t3 = t2 * localT;
            double invT = 1.0 - localT;
            double invT2 = invT * invT;
            double invT3 = invT2 * invT;

            double b0 = invT3 / 6;
            double b1 = (3 * t3 - 6 * t2 + 4) / 6;
            double b2 = (-3 * t3 + 3 * t2 + 3 * localT + 1.0) / 6;
            double b3 = t3 / 6;

            return p0 * b0 + p1 * b1 + p2 * b2 + p3 * b3;
        }

        private Double3 EvaluateHermite(double t)
        {
            if (Tangents == null) return EvaluateLinear(t);

            int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 1;
            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            Double3 p0 = ControlPoints[segment];
            Double3 p1 = ControlPoints[(segment + 1) % ControlPoints.Length];
            Double3 t0 = Tangents[segment];
            Double3 t1 = Tangents[(segment + 1) % Tangents.Length];

            // Hermite basis functions
            double t2 = localT * localT;
            double t3 = t2 * localT;

            double h00 = 2.0 * t3 - 3 * t2 + 1.0;
            double h10 = t3 - 2.0 * t2 + localT;
            double h01 = -2.0 * t3 + 3 * t2;
            double h11 = t3 - t2;

            return p0 * h00 + t0 * h10 + p1 * h01 + t1 * h11;
        }

        private Double3 EvaluateLinearDerivative(double t)
        {
            if (ControlPoints.Length < 2) return Double3.Zero;

            double scaledT = t * (ControlPoints.Length - 1);
            int index = (int)Maths.Floor(scaledT);

            if (index >= ControlPoints.Length - 1)
            {
                return IsClosed ?
                    (ControlPoints[0] - ControlPoints[ControlPoints.Length - 1]) * (ControlPoints.Length - 1) :
                    Double3.Zero;
            }

            return (ControlPoints[index + 1] - ControlPoints[index]) * (ControlPoints.Length - 1);
        }

        private Double3 EvaluateCatmullRomDerivative(double t)
        {
            int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 1;
            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            Double3 p0, p1, p2, p3;
            GetCatmullRomPoints(segment, out p0, out p1, out p2, out p3);

            double t2 = localT * localT;

            double db0 = -3 * Tension * t2 + 4 * Tension * localT - Tension;
            double db1 = 3 * (2.0 - Tension) * t2 + 2.0 * (Tension - 3) * localT;
            double db2 = 3 * (Tension - 2.0) * t2 + 2.0 * (3 - 2.0 * Tension) * localT + Tension;
            double db3 = 3 * Tension * t2 - 2.0 * Tension * localT;

            return (p0 * db0 + p1 * db1 + p2 * db2 + p3 * db3) * segmentCount;
        }

        private Double3 EvaluateBezierDerivative(double t)
        {
            int segmentCount = (ControlPoints.Length - 1) / 3;
            if (segmentCount == 0) return Double3.Zero;

            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            int baseIndex = segment * 3;
            Double3 p0 = ControlPoints[baseIndex];
            Double3 p1 = ControlPoints[baseIndex + 1];
            Double3 p2 = ControlPoints[baseIndex + 2];
            Double3 p3 = ControlPoints[baseIndex + 3];

            double invT = 1.0 - localT;
            double invT2 = invT * invT;
            double t2 = localT * localT;

            Double3 derivative = 
                p0 * (-3 * invT2) +
                p1 * (3 * invT2 - 6 * invT * localT) +
                p2 * (6 * invT * localT - 3 * t2) +
                p3 * (3 * t2);

            return derivative * segmentCount;
        }

        private Double3 EvaluateBSplineDerivative(double t)
        {
            if (ControlPoints.Length < 4) return EvaluateLinearDerivative(t);

            int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 3;
            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            Double3 p0, p1, p2, p3;
            GetBSplinePoints(segment, out p0, out p1, out p2, out p3);

            double t2 = localT * localT;

            double db0 = -t2 / 2.0;
            double db1 = (3 * t2 - 4 * localT) / 2.0;
            double db2 = (-3 * t2 + 2.0 * localT + 1.0) / 2.0;
            double db3 = t2 / 2.0;

            return (p0 * db0 + p1 * db1 + p2 * db2 + p3 * db3) * segmentCount;
        }

        private Double3 EvaluateHermiteDerivative(double t)
        {
            if (Tangents == null) return EvaluateLinearDerivative(t);

            int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 1;
            double scaledT = t * segmentCount;
            int segment = (int)Maths.Floor(scaledT);
            double localT = scaledT - segment;

            if (segment >= segmentCount)
            {
                segment = segmentCount - 1;
                localT = 1.0;
            }

            Double3 p0 = ControlPoints[segment];
            Double3 p1 = ControlPoints[(segment + 1) % ControlPoints.Length];
            Double3 t0 = Tangents[segment];
            Double3 t1 = Tangents[(segment + 1) % Tangents.Length];

            double t2 = localT * localT;

            double dh00 = 6 * t2 - 6 * localT;
            double dh10 = 3 * t2 - 4 * localT + 1.0;
            double dh01 = -6 * t2 + 6 * localT;
            double dh11 = 3 * t2 - 2.0 * localT;

            return (p0 * dh00 + t0 * dh10 + p1 * dh01 + t1 * dh11) * segmentCount;
        }

        private void GetCatmullRomPoints(int segment, out Double3 p0, out Double3 p1, out Double3 p2, out Double3 p3)
        {
            int count = ControlPoints.Length;
            
            p1 = ControlPoints[segment];
            p2 = ControlPoints[(segment + 1) % count];

            if (IsClosed)
            {
                p0 = ControlPoints[(segment - 1 + count) % count];
                p3 = ControlPoints[(segment + 2) % count];
            }
            else
            {
                p0 = segment > 0 ? ControlPoints[segment - 1] : ControlPoints[0] * 2.0 - ControlPoints[1];
                p3 = segment < count - 2 ? ControlPoints[segment + 2] : ControlPoints[count - 1] * 2.0 - ControlPoints[count - 2];
            }
        }

        private void GetBSplinePoints(int segment, out Double3 p0, out Double3 p1, out Double3 p2, out Double3 p3)
        {
            int count = ControlPoints.Length;

            if (IsClosed)
            {
                p0 = ControlPoints[segment % count];
                p1 = ControlPoints[(segment + 1) % count];
                p2 = ControlPoints[(segment + 2) % count];
                p3 = ControlPoints[(segment + 3) % count];
            }
            else
            {
                p0 = ControlPoints[segment];
                p1 = ControlPoints[segment + 1];
                p2 = ControlPoints[segment + 2];
                p3 = ControlPoints[segment + 3];
            }
        }

        private void GenerateDefaultTangents()
        {
            int count = ControlPoints.Length;
            Tangents = new Double3[count];

            for (int i = 0; i < count; i++)
            {
                Double3 prev, next;

                if (IsClosed)
                {
                    prev = ControlPoints[(i - 1 + count) % count];
                    next = ControlPoints[(i + 1) % count];
                }
                else
                {
                    prev = i > 0 ? ControlPoints[i - 1] : ControlPoints[i];
                    next = i < count - 1 ? ControlPoints[i + 1] : ControlPoints[i];
               }

               // Generate tangent as the difference between next and previous points
               Tangents[i] = (next - prev) / 2.0;
           }
       }

       #endregion

       #region Utility Methods

       /// <summary>
       /// Adds a control point to the spline.
       /// </summary>
       /// <param name="point">The control point to add.</param>
       public void AddControlPoint(Double3 point)
       {
           if (ControlPoints == null)
           {
               ControlPoints = new Double3[] { point };
           }
           else
           {
               var newArray = new Double3[ControlPoints.Length + 1];
               Array.Copy(ControlPoints, newArray, ControlPoints.Length);
               newArray[ControlPoints.Length] = point;
               ControlPoints = newArray;
           }

           if (Type == SplineType.Hermite)
           {
               GenerateDefaultTangents();
           }
       }

       /// <summary>
       /// Inserts a control point at the specified index.
       /// </summary>
       /// <param name="index">The index to insert at.</param>
       /// <param name="point">The control point to insert.</param>
       public void InsertControlPoint(int index, Double3 point)
       {
           if (ControlPoints == null)
           {
               ControlPoints = new Double3[] { point };
               return;
           }

           if (index < 0 || index > ControlPoints.Length)
               throw new ArgumentOutOfRangeException(nameof(index));

           var newArray = new Double3[ControlPoints.Length + 1];
           Array.Copy(ControlPoints, 0, newArray, 0, index);
           newArray[index] = point;
           Array.Copy(ControlPoints, index, newArray, index + 1, ControlPoints.Length - index);
           ControlPoints = newArray;

           if (Type == SplineType.Hermite)
           {
               GenerateDefaultTangents();
           }
       }

       /// <summary>
       /// Removes the control point at the specified index.
       /// </summary>
       /// <param name="index">The index of the control point to remove.</param>
       public void RemoveControlPoint(int index)
       {
           if (ControlPoints == null || index < 0 || index >= ControlPoints.Length)
               throw new ArgumentOutOfRangeException(nameof(index));

           if (ControlPoints.Length == 1)
           {
               ControlPoints = null;
               Tangents = null;
               return;
           }

           var newArray = new Double3[ControlPoints.Length - 1];
           Array.Copy(ControlPoints, 0, newArray, 0, index);
           Array.Copy(ControlPoints, index + 1, newArray, index, ControlPoints.Length - index - 1);
           ControlPoints = newArray;

           if (Type == SplineType.Hermite)
           {
               GenerateDefaultTangents();
           }
       }

       /// <summary>
       /// Sets the tangent at the specified control point index (for Hermite splines).
       /// </summary>
       /// <param name="index">The control point index.</param>
       /// <param name="tangent">The tangent vector.</param>
       public void SetTangent(int index, Double3 tangent)
       {
           if (Type != SplineType.Hermite)
               throw new InvalidOperationException("Tangents can only be set for Hermite splines");

           if (Tangents == null || index < 0 || index >= Tangents.Length)
               throw new ArgumentOutOfRangeException(nameof(index));

           Tangents[index] = tangent;
       }

       /// <summary>
       /// Gets the tangent at the specified control point index.
       /// </summary>
       /// <param name="index">The control point index.</param>
       /// <returns>The tangent vector at the specified index.</returns>
       public Double3 GetTangent(int index)
       {
           if (Type != SplineType.Hermite || Tangents == null)
               return EvaluateDerivative(index / (double)(ControlPointCount - 1));

           if (index < 0 || index >= Tangents.Length)
               throw new ArgumentOutOfRangeException(nameof(index));

           return Tangents[index];
       }

       /// <summary>
       /// Reverses the direction of the spline.
       /// </summary>
       public void Reverse()
       {
           if (ControlPoints == null) return;

           Array.Reverse(ControlPoints);

           if (Tangents != null)
           {
               Array.Reverse(Tangents);
               // Negate tangents since we're reversing direction
               for (int i = 0; i < Tangents.Length; i++)
               {
                   Tangents[i] = -Tangents[i];
               }
           }
       }

       /// <summary>
       /// Creates a reversed copy of this spline.
       /// </summary>
       /// <returns>A new spline with reversed direction.</returns>
       public Spline Reversed()
       {
           var copy = this;
           copy.Reverse();
           return copy;
       }

       /// <summary>
       /// Subdivides the spline by inserting new control points.
       /// </summary>
       /// <param name="subdivisionsPerSegment">Number of subdivisions per segment.</param>
       public void Subdivide(int subdivisionsPerSegment = 1)
       {
           if (ControlPoints == null || subdivisionsPerSegment < 1) return;

           var newPoints = new List<Double3>();
           int segmentCount = IsClosed ? ControlPoints.Length : ControlPoints.Length - 1;

           for (int segment = 0; segment < segmentCount; segment++)
           {
               double segmentStart = segment / (double)segmentCount;
               double segmentEnd = (segment + 1) / (double)segmentCount;

               newPoints.Add(ControlPoints[segment]);

               for (int sub = 1; sub <= subdivisionsPerSegment; sub++)
               {
                   double t = segmentStart + (segmentEnd - segmentStart) * (sub / (double)(subdivisionsPerSegment + 1));
                   newPoints.Add(Evaluate(t));
               }
           }

           if (!IsClosed)
           {
               newPoints.Add(ControlPoints[ControlPoints.Length - 1]);
           }

           ControlPoints = newPoints.ToArray();

           if (Type == SplineType.Hermite)
           {
               GenerateDefaultTangents();
           }
       }

       /// <summary>
       /// Smooths the spline by averaging control point positions.
       /// </summary>
       /// <param name="iterations">Number of smoothing iterations.</param>
       /// <param name="strength">Smoothing strength (0 = no smoothing, 1 = full averaging).</param>
       public void Smooth(int iterations = 1, double strength = (double)0.5)
       {
           if (ControlPoints == null || ControlPoints.Length < 3) return;

           strength = Maths.Clamp(strength, 0.0, 1.0);

           for (int iter = 0; iter < iterations; iter++)
           {
               var smoothedPoints = new Double3[ControlPoints.Length];

               for (int i = 0; i < ControlPoints.Length; i++)
               {
                   Double3 prev, next;

                   if (IsClosed)
                   {
                       prev = ControlPoints[(i - 1 + ControlPoints.Length) % ControlPoints.Length];
                       next = ControlPoints[(i + 1) % ControlPoints.Length];
                   }
                   else
                   {
                       prev = i > 0 ? ControlPoints[i - 1] : ControlPoints[i];
                       next = i < ControlPoints.Length - 1 ? ControlPoints[i + 1] : ControlPoints[i];
                   }

                   Double3 averaged = (prev + ControlPoints[i] + next) / 3;
                   smoothedPoints[i] = Maths.Lerp(ControlPoints[i], averaged, strength);
               }

               ControlPoints = smoothedPoints;
           }

           if (Type == SplineType.Hermite)
           {
               GenerateDefaultTangents();
           }
       }

       /// <summary>
       /// Calculates the bounding box of the spline.
       /// </summary>
       /// <param name="subdivisions">Number of subdivisions for accurate bounds calculation.</param>
       /// <returns>The axis-aligned bounding box containing the spline.</returns>
       public AABB GetBounds(int subdivisions = 100)
       {
           if (ControlPoints == null || ControlPoints.Length == 0)
               return new AABB(Double3.Zero, Double3.Zero);

           Double3 min = ControlPoints[0];
           Double3 max = ControlPoints[0];

           // Include all control points
           foreach (var point in ControlPoints)
           {
               min = new Double3(
                   Maths.Min(min.X, point.X),
                   Maths.Min(min.Y, point.Y),
                   Maths.Min(min.Z, point.Z)
               );
               max = new Double3(
                   Maths.Max(max.X, point.X),
                   Maths.Max(max.Y, point.Y),
                   Maths.Max(max.Z, point.Z)
               );
           }

           // Sample the spline for curves that might extend beyond control points
           for (int i = 0; i <= subdivisions; i++)
           {
               double t = i / (double)subdivisions;
               Double3 point = Evaluate(t);

               min = new Double3(
                   Maths.Min(min.X, point.X),
                   Maths.Min(min.Y, point.Y),
                   Maths.Min(min.Z, point.Z)
               );
               max = new Double3(
                   Maths.Max(max.X, point.X),
                   Maths.Max(max.Y, point.Y),
                   Maths.Max(max.Z, point.Z)
               );
           }

           return new AABB(min, max);
       }

       #endregion

       #region Static Factory Methods

       /// <summary>
       /// Creates a linear spline from the given points.
       /// </summary>
       /// <param name="points">The control points.</param>
       /// <param name="closed">Whether the spline should be closed.</param>
       /// <returns>A linear spline.</returns>
       public static Spline CreateLinear(Double3[] points, bool closed = false)
       {
           return new Spline(points, SplineType.Linear, closed);
       }

       /// <summary>
       /// Creates a Catmull-Rom spline from the given points.
       /// </summary>
       /// <param name="points">The control points.</param>
       /// <param name="closed">Whether the spline should be closed.</param>
       /// <param name="tension">Tension parameter (0 = standard Catmull-Rom).</param>
       /// <returns>A Catmull-Rom spline.</returns>
       public static Spline CreateCatmullRom(Double3[] points, bool closed = false, double tension = 0.0)
       {
           return new Spline(points, SplineType.CatmullRom, closed, tension);
       }

       /// <summary>
       /// Creates a cubic Bezier spline from control points.
       /// The number of control points should be 3n+1 for n Bezier segments.
       /// </summary>
       /// <param name="points">The control points (must be 3n+1 points).</param>
       /// <returns>A Bezier spline.</returns>
       public static Spline CreateBezier(Double3[] points)
       {
           if ((points.Length - 1) % 3 != 0)
               throw new ArgumentException("Bezier splines require 3n+1 control points for n segments");

           return new Spline(points, SplineType.Bezier, false);
       }

       /// <summary>
       /// Creates a uniform cubic B-spline from the given points.
       /// </summary>
       /// <param name="points">The control points.</param>
       /// <param name="closed">Whether the spline should be closed.</param>
       /// <returns>A B-spline.</returns>
       public static Spline CreateBSpline(Double3[] points, bool closed = false)
       {
           return new Spline(points, SplineType.BSpline, closed);
       }

       /// <summary>
       /// Creates a Hermite spline with automatic tangent generation.
       /// </summary>
       /// <param name="points">The control points.</param>
       /// <param name="closed">Whether the spline should be closed.</param>
       /// <returns>A Hermite spline with auto-generated tangents.</returns>
       public static Spline CreateHermite(Double3[] points, bool closed = false)
       {
           return new Spline(points, SplineType.Hermite, closed);
       }

       /// <summary>
       /// Creates a Hermite spline with explicit tangents.
       /// </summary>
       /// <param name="points">The control points.</param>
       /// <param name="tangents">The tangent vectors at each control point.</param>
       /// <param name="closed">Whether the spline should be closed.</param>
       /// <returns>A Hermite spline with specified tangents.</returns>
       public static Spline CreateHermiteWithTangents(Double3[] points, Double3[] tangents, bool closed = false)
       {
           return new Spline(points, tangents, closed);
       }

       /// <summary>
       /// Creates a circular arc as a spline.
       /// </summary>
       /// <param name="center">Center of the circle.</param>
       /// <param name="radius">Radius of the circle.</param>
       /// <param name="startAngle">Start angle in radians.</param>
       /// <param name="endAngle">End angle in radians.</param>
       /// <param name="segments">Number of segments to approximate the arc.</param>
       /// <returns>A spline approximating the circular arc.</returns>
       public static Spline CreateCircularArc(Double3 center, double radius, double startAngle, double endAngle, int segments = 16)
       {
           var points = new Double3[segments + 1];
           double angleStep = (endAngle - startAngle) / segments;

           for (int i = 0; i <= segments; i++)
           {
               double angle = startAngle + i * angleStep;
               points[i] = center + new Double3(
                   radius * Maths.Cos(angle),
                   radius * Maths.Sin(angle),
                   0.0
               );
           }

           return CreateCatmullRom(points);
       }

       /// <summary>
       /// Creates a helix spline.
       /// </summary>
       /// <param name="center">Center axis of the helix.</param>
       /// <param name="radius">Radius of the helix.</param>
       /// <param name="pitch">Vertical distance per revolution.</param>
       /// <param name="turns">Number of turns.</param>
       /// <param name="segments">Number of segments per turn.</param>
       /// <returns>A spline representing a helix.</returns>
       public static Spline CreateHelix(Double3 center, double radius, double pitch, double turns, int segments = 16)
       {
           int totalPoints = (int)(segments * turns) + 1;
           var points = new Double3[totalPoints];
           double angleStep = 2.0 * (double)Maths.PI / segments;
           double heightStep = pitch / segments;

           for (int i = 0; i < totalPoints; i++)
           {
               double angle = i * angleStep;
               double height = i * heightStep;
               points[i] = center + new Double3(
                   radius * Maths.Cos(angle),
                   radius * Maths.Sin(angle),
                   height
               );
           }

           return CreateCatmullRom(points);
       }

       #endregion

       // --- IEquatable & IFormattable Implementation ---
       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public bool Equals(Spline other)
       {
           if (Type != other.Type || IsClosed != other.IsClosed || !Tension.Equals(other.Tension))
               return false;

           if (ControlPoints?.Length != other.ControlPoints?.Length)
               return false;

           if (ControlPoints != null)
           {
               for (int i = 0; i < ControlPoints.Length; i++)
               {
                   if (!ControlPoints[i].Equals(other.ControlPoints[i]))
                       return false;
               }
           }

           if (Tangents?.Length != other.Tangents?.Length)
               return false;

           if (Tangents != null)
           {
               for (int i = 0; i < Tangents.Length; i++)
               {
                   if (!Tangents[i].Equals(other.Tangents[i]))
                       return false;
               }
           }

           return true;
       }

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public override bool Equals(object? obj) => obj is Spline other && Equals(other);

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public override int GetHashCode()
       {
           var hash = new HashCode();
           hash.Add(Type);
           hash.Add(IsClosed);
           hash.Add(Tension);
           
           if (ControlPoints != null)
           {
               foreach (var point in ControlPoints)
                   hash.Add(point);
           }
           
           return hash.ToHashCode();
       }

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

       [MethodImpl(MethodImplOptions.AggressiveInlining)]
       public string ToString(string? format, IFormatProvider? formatProvider = null)
       {
           return string.Format(formatProvider, "SplineD(Type: {0}, Points: {1}, Closed: {2})", 
               Type, ControlPointCount, IsClosed);
       }

       public static bool operator ==(Spline left, Spline right) => left.Equals(right);
       public static bool operator !=(Spline left, Spline right) => !left.Equals(right);
   }
}
