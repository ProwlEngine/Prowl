// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>The generated primitives: wound outward, with exact normals and tangents that follow their UVs.</summary>
public class PrimitiveMeshTests
{
    public static TheoryData<string> Primitives => new() { "Cube", "Sphere", "Cylinder", "Capsule", "Cone", "Plane" };

    private static Mesh Create(string name) => name switch
    {
        "Cube" => Mesh.CreateCube(new Float3(1f, 2f, 3f)),
        "Sphere" => Mesh.CreateSphere(0.5f, 16, 24),
        "Cylinder" => Mesh.CreateCylinder(0.5f, 2f, 24),
        "Capsule" => Mesh.CreateCapsule(0.5f, 2f, 24, 6),
        "Cone" => Mesh.CreateCone(0.5f, 2f, 24),
        "Plane" => Mesh.CreatePlane(4f, 6f),
        _ => throw new System.ArgumentException(name),
    };

    [Theory, MemberData(nameof(Primitives))]
    public void Triangles_FaceTheWayTheirNormalsDo(string name)
    {
        Mesh mesh = Create(name);
        Float3[] v = mesh.Vertices, n = mesh.Normals;
        uint[] idx = mesh.Indices;
        for (int i = 0; i < idx.Length; i += 3)
        {
            uint a = idx[i], b = idx[i + 1], c = idx[i + 2];
            Float3 face = Float3.Cross(v[b] - v[a], v[c] - v[a]);
            Assert.True(Float3.LengthSquared(face) > 1e-12f, $"triangle {i / 3} has no area");
            Assert.True(Float3.Dot(face, n[a] + n[b] + n[c]) > 0f, $"triangle {i / 3} faces inward");
        }
    }

    [Theory, MemberData(nameof(Primitives))]
    public void Tangents_AreUnitAndFollowTheUVs(string name)
    {
        Mesh mesh = Create(name);
        Float3[] n = mesh.Normals;
        Float4[] written = mesh.Tangents;
        Assert.Equal(mesh.Vertices.Length, written.Length);
        Assert.Equal(mesh.Vertices.Length, mesh.UV.Length);

        for (int i = 0; i < n.Length; i++)
        {
            Assert.Equal(1f, Float3.Length(n[i]), 1e-4f);
            Assert.Equal(1f, Float3.Length(written[i].XYZ), 1e-4f);
            Assert.True(MathF.Abs(Float3.Dot(written[i].XYZ, n[i])) < 1e-4f, $"tangent {i} leans off its surface");
            Assert.True(MathF.Abs(written[i].W) == 1f, $"tangent {i} has no handedness");
        }

        mesh.RecalculateTangents();
        Float4[] fromUVs = mesh.Tangents;
        for (int i = 0; i < n.Length; i++)
        {
            if (MathF.Abs(n[i].Y) > 0.999f && name is "Sphere" or "Capsule") continue;
            Assert.True(Float3.Dot(written[i].XYZ, fromUVs[i].XYZ) > 0.9f, $"tangent {i} runs against u");
            Assert.Equal(fromUVs[i].W, written[i].W);
        }
    }

    [Fact]
    public void Sphere_NormalsPointFromTheCentre()
    {
        Mesh mesh = Mesh.CreateSphere(0.5f, 16, 24);
        for (int i = 0; i < mesh.Vertices.Length; i++)
            AssertNear(Float3.Normalize(mesh.Vertices[i]), mesh.Normals[i], i);
    }

    [Fact]
    public void Cylinder_SidesAreRoundAndCapsFlat()
    {
        Mesh mesh = Mesh.CreateCylinder(0.5f, 2f, 24);
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            Float3 p = mesh.Vertices[i], nrm = mesh.Normals[i];
            bool side = MathF.Abs(nrm.Y) < 0.5f;
            AssertNear(side ? Float3.Normalize(new Float3(p.X, 0, p.Z)) : new Float3(0, MathF.Sign(p.Y), 0), nrm, i);
        }
    }

    [Fact]
    public void Capsule_NormalsPointFromItsSpine()
    {
        Mesh mesh = Mesh.CreateCapsule(0.5f, 2f, 24, 6);
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            Float3 p = mesh.Vertices[i];
            Float3 spine = new(0, Math.Clamp(p.Y, -0.5f, 0.5f), 0);
            AssertNear(Float3.Normalize(p - spine), mesh.Normals[i], i);
        }
    }

    [Fact]
    public void Cone_SidesFollowTheSlantAndTheBaseIsFlat()
    {
        const float r = 0.5f, h = 2f;
        float slant = MathF.Sqrt(r * r + h * h);
        Mesh mesh = Mesh.CreateCone(r, h, 24);
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            Float3 nrm = mesh.Normals[i];
            if (nrm.Y < -0.5f) { AssertNear(new Float3(0, -1, 0), nrm, i); continue; }
            Assert.Equal(r / slant, nrm.Y, 1e-4f);
        }
    }

    [Fact]
    public void DefaultModels_KeepTheirSizesAndGetLightmapUVs()
    {
        (DefaultModel Model, Float3 Min, Float3 Max)[] expected =
        [
            (DefaultModel.Cube, new(-0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, 0.5f)),
            (DefaultModel.Sphere, new(-0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, 0.5f)),
            (DefaultModel.Cylinder, new(-0.5f, -1f, -0.5f), new(0.5f, 1f, 0.5f)),
            (DefaultModel.Capsule, new(-0.5f, -1f, -0.5f), new(0.5f, 1f, 0.5f)),
            (DefaultModel.Cone, new(-0.5f, -1f, -0.5f), new(0.5f, 1f, 0.5f)),
            (DefaultModel.Plane, new(-5f, 0f, -5f), new(5f, 0f, 5f)),
        ];

        foreach ((DefaultModel model, Float3 min, Float3 max) in expected)
        {
            Mesh mesh = DefaultModels.Generate(model);
            AssertNear(min, mesh.bounds.Min, (int)model, 0.01f);
            AssertNear(max, mesh.bounds.Max, (int)model, 0.01f);
            Assert.Equal(mesh.Vertices.Length, mesh.UV2.Length);
        }
    }

    private static void AssertNear(Float3 expected, Float3 actual, int index, float tolerance = 1e-4f) =>
        Assert.True(Float3.Distance(expected, actual) < tolerance, $"{index}: expected {expected}, got {actual}");
}
