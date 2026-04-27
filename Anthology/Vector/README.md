# Prowl.Vector 3D Mathematics

A comprehensive mathematics library (Built for the Prowl Game Engine).
The library is designed for both 32-bit and 64-bit 3D applications, providing vector operations, matrix transformations, geometric utilities, noise functions, and collision detection.
The API itself is heavily inspired by the Unity Game Engine.

## Features

- **Core Types**
  - `Double2` / `Float2` / `Int2` — 2D vectors (double, float, int precision)
  - `Double3` / `Float3` / `Int3` — 3D vectors
  - `Double4` / `Float4` / `Int4` — 4D vectors
  - `Double4x4` / `Float4x4` — 4x4 matrices
  - `Double3x3` / `Float3x3` — 3x3 matrices
  - `Double2x2` / `Float2x2` — 2x2 matrices
  - `Quaternion`
  - `Color` / `Color32` — float and byte RGBA color types
  - `Rect` / `IntRect`
  - `RNG` — xoshiro256\*\* pseudo-random number generator

- **Geometry Types** (`Prowl.Vector.Geometry`)
  - `AABB` — Axis-Aligned Bounding Box
  - `Frustum` — View frustum
  - `Plane` / `Ray` / `Sphere` / `Cone` / `Triangle`
  - `LineSegment` / `Spline`
  - `Transform2D` / `Transform3D`
  - `GeometryData` — Mesh data for wireframe and solid visualization
  - `IBoundingShape` — Interface for GJK-compatible shapes
  - `GJK` — Gilbert-Johnson-Keerthi collision detection

- **Vector Operations**
  - Basic arithmetic (add, subtract, multiply, divide)
  - Dot and cross products
  - Normalization and magnitude calculations
  - Linear interpolation (`Maths.Lerp`)
  - Spherical interpolation (`Double3.Slerp`)
  - Min/Max, Clamp, Remap operations
  - Distance and angle calculations
  - Projection, reflection, refraction
  - And More!

- **Matrix Operations**
  - Translation, rotation, and scaling (`CreateTranslation`, `CreateTRS`, `RotateX/Y/Z`)
  - View and projection matrices (`CreateLookAt`, `CreatePerspectiveFov`, `CreateOrtho`)
  - Matrix multiplication and inversion
  - Transpose and determinant
  - And More!

- **Quaternion Features**
  - Euler angle conversions (`FromEuler`, `EulerAngles`)
  - Axis-angle construction (`AxisAngle` / `AngleAxis`)
  - Spherical interpolation (`Slerp`) and normalized lerp (`Nlerp`)
  - Look rotation (`LookRotation`)
  - Matrix conversions (`FromMatrix`)
  - And More!

- **Intersection Utilities** (`Intersection` static class)
  - Ray vs. plane, triangle, AABB, sphere, cylinder
  - Overlap tests: sphere-sphere, AABB-AABB, sphere-AABB, frustum, cone, and more
  - Closest-point queries
  - Signed distance and plane classification

- **Noise Functions** (`Noise` static class)
  - `Noise.SNoise(Float2/3/4)` — Simplex noise (2D, 3D, 4D)
  - `Noise.CNoise(Float2/3/4)` — Classic Perlin noise
  - `Noise.PNoise(Float2/3, rep)` — Periodic Perlin noise
  - `Noise.Cellular2D(Float2)` / `Noise.Cellular3D(Float3)` — Cellular (Worley) noise
  - `Noise.Cellular2x2` / `Noise.Cellular2x2x2` — Optimized cellular variants
  - Periodic simplex noise (`Psrd_*` family)

- **Other Notable Features**
  - Comprehensive set of constants (`Maths.PI`, `Maths.Tau`, `Maths.E`, `Maths.GoldenRatio`, etc.)
  - Trigonometric, logarithmic, and interpolation functions
  - Angle conversions (`Maths.ToDegrees` / `Maths.ToRadians`)
  - Physics constants (`Maths.StandardGravity`, `Maths.SpeedOfLight`)
  - And Much More!


## Usage

### Basic Vector Operations

```csharp
// Create vectors
var v2 = new Double2(1.0, 2.0);
var v3 = new Double3(1.0, 2.0, 3.0);
var v4 = new Double4(1.0, 2.0, 3.0, 4.0);

// Float precision variants
var fv3 = new Float3(1.0f, 2.0f, 3.0f);

// Vector arithmetic
var sum = v3 + new Double3(1.0, 1.0, 1.0);
var scaled = v3 * 2.0;
var normalized = Double3.Normalize(v3);

// Vector operations
double dot = Double3.Dot(v3, normalized);
Double3 cross = Double3.Cross(v3, normalized);
double distance = Double3.Distance(v3, normalized);
double angle = Double3.AngleBetween(v3, normalized);

// Component-wise operations via Maths
var clamped = Maths.Clamp(v3, Double3.Zero, Double3.One);
var lerped = Maths.Lerp(v3, Double3.One, 0.5);
```

### Matrix Transformations

```csharp
// Create transformation matrices
var translation = Double4x4.CreateTranslation(new Double3(1, 2, 3));
var rotationY = Double4x4.RotateY(Maths.PI * 0.5);
var scale = Double4x4.CreateScale(2.0);

// Combined TRS matrix
var transform = Double4x4.CreateTRS(
    new Double3(1, 2, 3),
    Quaternion.FromEuler(0, 45, 0),
    new Double3(1, 1, 1)
);

// Transform points and normals
var transformedPoint = Double4x4.TransformPoint(v3, transform);
var transformedNormal = Double4x4.TransformNormal(v3, transform);

// View and projection
var view = Double4x4.CreateLookAt(eye, target, Double3.UnitY);
var proj = Double4x4.CreatePerspectiveFov(Maths.PI / 4.0, aspectRatio, 0.1, 1000.0);
```

### Quaternion Rotations

```csharp
// Create quaternions
var fromEuler = Quaternion.FromEuler(new Double3(pitch, yaw, roll));
var fromAxis = Quaternion.AxisAngle(Double3.UnitY, angle);
var lookAt = Quaternion.LookRotation(forward, Double3.UnitY);

// Interpolate between rotations
var interpolated = Quaternion.Slerp(q1, q2, t);
var fast = Quaternion.Nlerp(q1, q2, t);

// Apply rotation to vector (operator overload)
var rotated = rotation * v3;
```

### Collision Detection

```csharp
// GJK collision detection between any IBoundingShape types
var box = new AABB(new Double3(-1, -1, -1), new Double3(1, 1, 1));
var sphere = new Sphere(Double3.Zero, 1.5);

bool colliding = GJK.Intersects(box, sphere);

// Direct intersection tests
bool hit = Intersection.RayAABB(ray.Origin, ray.Direction, box.Min, box.Max, out double t);
bool overlap = Intersection.SphereSphereOverlap(centerA, radiusA, centerB, radiusB);
```

### Random Number Generation

```csharp
// Use the shared instance or create your own
var rng = RNG.Shared;
var seeded = new RNG(42);

// Basic values
double value = rng.NextDouble();        // [0, 1]
float f = rng.NextFloat();              // [0, 1]
int n = rng.Range(0, 100);             // [0, 100] inclusive

// Geometry
Float2 onCircle = rng.OnUnitCircle();   // Point on unit circle
Float3 onSphere = rng.OnUnitSphere();   // Point on unit sphere
Float4 color = rng.NextColor();         // Random opaque color

// Gaussian distribution
float gaussian = rng.NextGaussian(mean: 0f, standardDeviation: 1f);
```

### Angle Conversions

```csharp
// Static methods on Maths
double degrees = Maths.ToDegrees(Maths.PI);   // 180.0
double radians = Maths.ToRadians(90.0);        // π/2

// Works on vectors too
Double3 inDegrees = Maths.ToDegrees(eulerRadians);
Double3 inRadians = Maths.ToRadians(eulerDegrees);
```

### Intersection Tests

```csharp
// Ray-triangle intersection
bool hits = Intersection.RayTriangle(
    rayOrigin, rayDirection,
    v0, v1, v2,
    out double t, out double u, out double v
);

// Closest point queries
Intersection.ClosestPointOnLineSegmentToPoint(lineStart, lineEnd, point, out Double3 closest);
Intersection.ClosestPointOnAABBToPoint(aabbMin, aabbMax, point, out Double3 closest);
```

### Noise

```csharp
// Simplex noise (2D, 3D, 4D)
float n2 = Noise.SNoise(new Float2(x, y));
float n3 = Noise.SNoise(new Float3(x, y, z));
float n4 = Noise.SNoise(new Float4(x, y, z, w));

// 3D simplex noise with gradient
float n3g = Noise.SNoise(new Float3(x, y, z), out Float3 gradient);

// Classic Perlin noise
float perlin = Noise.CNoise(new Float2(x, y));

// Periodic Perlin noise (tileable)
float periodic = Noise.PNoise(new Float2(x, y), new Float2(tileX, tileY));

// Cellular (Worley) noise
Float2 worley = Noise.Cellular2D(new Float2(x, y));
```

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
