// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Runtime.ParticleSystem.Modules;

/// <summary>
/// Defines the shape from which particles are emitted.
/// </summary>
[Serializable]
public enum EmissionShape
{
    Point,
    LineSegment,
    Circle,
    Sphere,
    Box,
    Cone
}

/// <summary>
/// Represents a burst of particles emitted at a specific time.
/// </summary>
[Serializable]
public struct ParticleBurst
{
    public float Time;
    public int MinCount;
    public int MaxCount;
    public int CycleCount;
    public float RepeatInterval;

    public ParticleBurst(float time, int count)
    {
        Time = time;
        MinCount = count;
        MaxCount = count;
        CycleCount = 1;
        RepeatInterval = 0;
    }
}

/// <summary>
/// Controls when and how many particles are emitted.
/// </summary>
[Serializable]
public class EmissionModule : ParticleSystemModule
{
    public MinMaxCurve RateOverTime = new(0.0f);
    public List<ParticleBurst> Bursts = new();

    // Shape properties
    public EmissionShape Shape = EmissionShape.Point;
    public Float3 ShapePosition = Float3.Zero;
    public Float3 ShapeRotation = Float3.Zero; // Euler angles in degrees
    public Float3 ShapeScale = Float3.One;

    // Shape-specific properties
    public float LineLength = 1.0f;          // For LineSegment
    public float Radius = 1.0f;              // For Circle, Sphere, Cone
    public float ConeAngle = 25.0f;          // For Cone (in degrees)
    public bool EmitFromShell = false;       // For Sphere, Circle (emit from surface only)
    public Float3 BoxSize = Float3.One;      // For Box

    [NonSerialized]
    internal float EmitAccumulator = 0.0f;

    [NonSerialized]
    internal Dictionary<int, float> BurstTimers = new();

    public int CalculateEmitCount(float deltaTime, float time, Random random)
    {
        if (!Enabled) return 0;

        int emitCount = 0;

        // Rate over time emission
        float rate = RateOverTime.Evaluate(time, random);
        EmitAccumulator += rate * deltaTime;

        while (EmitAccumulator >= 1.0f)
        {
            emitCount++;
            EmitAccumulator -= 1.0f;
        }

        // Burst emission
        for (int i = 0; i < Bursts.Count; i++)
        {
            var burst = Bursts[i];

            if (!BurstTimers.ContainsKey(i))
            {
                BurstTimers[i] = 0;
            }

            float burstTimer = BurstTimers[i];

            // Check if it's time for this burst
            if (time >= burst.Time && burstTimer == 0)
            {
                int count = burst.MinCount == burst.MaxCount
                    ? burst.MinCount
                    : random.Next(burst.MinCount, burst.MaxCount + 1);
                emitCount += count;

                if (burst.CycleCount > 1 && burst.RepeatInterval > 0)
                {
                    BurstTimers[i] = burst.RepeatInterval;
                }
                else
                {
                    BurstTimers[i] = -1; // Mark as triggered
                }
            }
            else if (burstTimer > 0)
            {
                BurstTimers[i] -= deltaTime;
                if (BurstTimers[i] <= 0)
                {
                    BurstTimers[i] = 0;
                }
            }
        }

        return emitCount;
    }

    public void Reset()
    {
        EmitAccumulator = 0;
        BurstTimers.Clear();
    }

    /// <summary>
    /// Calculates the spawn position and initial direction for a particle based on the emission shape.
    /// </summary>
    public void GetShapePositionAndDirection(Random random, out Float3 position, out Float3 direction)
    {
        position = Float3.Zero;
        direction = new Float3(0, 1, 0); // Default up

        switch (Shape)
        {
            case EmissionShape.Point:
                position = Float3.Zero;
                direction = RandomDirection(random);
                break;

            case EmissionShape.LineSegment:
                {
                    float t = (float)random.NextDouble();
                    position = new Float3(0, 0, (t - 0.5f) * LineLength);
                    direction = RandomDirection(random);
                }
                break;

            case EmissionShape.Circle:
                {
                    float angle = (float)random.NextDouble() * 2.0f * MathF.PI;
                    float radius =EmitFromShell ? Radius : (float)random.NextDouble() * Radius;
                    position = new Float3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
                    direction = new Float3(0, 1, 0);
                }
                break;

            case EmissionShape.Sphere:
                {
                    // Uniform random point in or on sphere
                    direction = RandomDirection(random);
                    float radius = EmitFromShell ? Radius : (float)Maths.Pow(random.NextDouble(), 1.0 / 3.0) * Radius;
                    position = direction * radius;
                }
                break;

            case EmissionShape.Box:
                {
                    position = new Float3(
                        ((float)random.NextDouble() - 0.5f) * BoxSize.X,
                        ((float)random.NextDouble() - 0.5f) * BoxSize.Y,
                        ((float)random.NextDouble() - 0.5f) * BoxSize.Z
                    );
                    direction = RandomDirection(random);
                }
                break;

            case EmissionShape.Cone:
                {
                    // Random point on cone surface
                    float angle = (float)random.NextDouble() * 2.0f * MathF.PI;
                    float coneAngleRad = ConeAngle * MathF.PI / 180.0f;
                    float heightFactor = (float)random.NextDouble();
                    float height = heightFactor * Maths.Max(0.001f, Radius);
                    float coneRadius = MathF.Tan(coneAngleRad) * height;

                    position = new Float3(
                        MathF.Cos(angle) * coneRadius,
                        height,
                        MathF.Sin(angle) * coneRadius
                    );

                    // Direction along cone surface
                    direction = Float3.Normalize(position);
                }
                break;
        }

        // Apply shape transformation (rotation and scale)
        position = new Float3(position.X * ShapeScale.X, position.Y * ShapeScale.Y, position.Z * ShapeScale.Z);

        // Apply rotation (convert euler angles to rotation)
        if (ShapeRotation.X != 0 || ShapeRotation.Y != 0 || ShapeRotation.Z != 0)
        {
            // Simple euler rotation (X, Y, Z order)
            position = RotateVector(position, ShapeRotation);
            direction = RotateVector(direction, ShapeRotation);
        }

        // Add shape position offset
        position = position + ShapePosition;
    }

    private Float3 RandomDirection(Random random)
    {
        // Uniform random direction on unit sphere
        float theta = (float)random.NextDouble() * 2.0f * MathF.PI;
        float phi = MathF.Acos(2.0f * (float)random.NextDouble() - 1.0f);

        return new Float3(
            MathF.Sin(phi) * MathF.Cos(theta),
            MathF.Sin(phi) * MathF.Sin(theta),
            MathF.Cos(phi)
        );
    }

    private Float3 RotateVector(Float3 v, Float3 eulerDegrees)
    {
        // Convert to radians
        float rx = eulerDegrees.X * MathF.PI / 180.0f;
        float ry = eulerDegrees.Y * MathF.PI / 180.0f;
        float rz = eulerDegrees.Z * MathF.PI / 180.0f;

        // Rotate around X
        float cosX = MathF.Cos(rx), sinX = MathF.Sin(rx);
        float y1 = v.Y * cosX - v.Z * sinX;
        float z1 = v.Y * sinX + v.Z * cosX;

        // Rotate around Y
        float cosY = MathF.Cos(ry), sinY = MathF.Sin(ry);
        float x2 = v.X * cosY + z1 * sinY;
        float z2 = -v.X * sinY + z1 * cosY;

        // Rotate around Z
        float cosZ = MathF.Cos(rz), sinZ = MathF.Sin(rz);
        float x3 = x2 * cosZ - y1 * sinZ;
        float y3 = x2 * sinZ + y1 * cosZ;

        return new Float3(x3, y3, z2);
    }
}
