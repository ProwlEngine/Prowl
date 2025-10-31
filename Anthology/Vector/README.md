# Prowl.Vector 3D Mathematics

A comprehensive mathematics library (Built for the Prowl Game Engine).
The library is designed for 64-bit 3D applications, it provides vector operations, matrix transformations, and geometric utilities.
The API itself is heavily inspired by the Unity Game Engine.

## Features

- **Core Type**
  - Vector2/Vector2Int
  - Vector3/Vector3Int
  - Vector4/Vector4Int
  - Bool3 (Memory-efficient 3-boolean structure)
  - Matrix4x4
  - Quaternion
  - Bounds
  - Bounding Frustrum
  - Rect/IntRect
  - Random (Comprehensive random functions)
  - Ray
  - Plane
  - Less than 3k lines of executable code!

- **Vector Operations**
  - Basic arithmetic (add, subtract, multiply, divide)
  - Dot and cross products
  - Normalization and magnitude calculations
  - Linear interpolation (Lerp)
  - Min/Max operations
  - Distance calculations
  - Vector transformations
  - And More!

- **Matrix Operations**
  - Creation of view and projection matrices
  - Translation, rotation, and scaling transformations
  - Matrix multiplication and inversion
  - Billboard and constrained billboard creation
  - Decomposition into translation/rotation/scale
  - And More!

- **Quaternion Features**
  - Euler angle conversions
  - Spherical interpolation (Slerp)
  - Rotation concatenation
  - Angular interpolation
  - Matrix conversions
  - And More!

- **Other Noteable features**
  - Comprehensive set of constants (π, τ, e, golden ratio)
  - Trigonometric functions
  - Interpolation functions
  - Geometric calculations
  - Angle conversions
  - Ray and line intersection tests
  - And Much More!


## Usage

### Basic Vector Operations

```csharp
// Create vectors
var v2 = new Vector2(1.0, 2.0);
var v3 = new Vector3(1.0, 2.0, 3.0);
var v4 = new Vector4(1.0, 2.0, 3.0, 4.0);

// Vector arithmetic
var sum = v3 + new Vector3(1.0, 1.0, 1.0);
var scaled = v3 * 2.0;
var normalized = v3.normalized;

// Vector operations
float dot = Vector3.Dot(v3, normalized);
Vector3 cross = Vector3.Cross(v3, normalized);
float distance = Vector3.Distance(v3, normalized);
```

### Matrix transformations

```csharp
// Create transformation matrices
var translation = Matrix4x4.CreateTranslation(new Vector3(1, 2, 3));
var rotation = Matrix4x4.CreateRotationY(MathD.PI * 0.5);
var scale = Matrix4x4.CreateScale(2.0);

// Combine transformations
var transform = translation * rotation * scale;

// Transform vectors
var transformed = Vector3.Transform(v3, transform);
```

### Quaternion Rotations

```csharp
// Create quaternions
var rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
var fromAxis = Quaternion.AngleAxis(angle, axis);

// Interpolate between rotations
var interpolated = Quaternion.Slerp(q1, q2, t);

// Apply rotation to vector
var rotated = Vector3.Transform(v3, rotation);
```

### Other Usefull Functions

```csharp
// Generate random values
float value = Random.Value;                // Range [0,1]
Vector2 circle = Random.OnUnitCircle;       // Point on circle
Vector3 sphere = Random.InUnitSphere;       // Point in sphere
Quaternion rotation = Random.Rotation;      // Random rotation

// Quick conversions between Radiens and Degrees
float degree = radian.ToDeg();
float radian = degree.ToRad();

// Line intersection
bool intersects = MathD.DoesLineIntersectLine(start1, end1, start2, end2, out Vector2 intersection);

// Ray-triangle intersection
bool hits = MathD.RayIntersectsTriangle(rayOrigin, rayDir, v1, v2, v3, out Vector3 hitPoint);

// Point in triangle test
bool inside = MathD.IsPointInTriangle(point, v1, v2, v3);
```

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.