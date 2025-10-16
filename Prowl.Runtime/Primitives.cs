using System;
using System.Runtime.CompilerServices;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

public static class Primitives
{
    private static Material _standardMaterial;
    private static Mesh _cubeMesh;
    private static Mesh _cylinderMesh;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(name, Double3.Zero, Double3.One, _standardMaterial, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(Double3 position)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(position.ToString(), position, Double3.One, _standardMaterial, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name, Double3 position)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(name, position, Double3.One, _standardMaterial, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name, Double3 position, Double3 scale)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(name, position, scale, _standardMaterial, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name, Double3 position, Double3 scale, Material material)
    {
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(name, scale, position, material, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name, Double3 position, Double3 scale, Material material, Mesh mesh)
    {
        // game object
        var go = new GameObject(name);
        go.Transform.position = position;
        go.Transform.localScale = scale;

        // visuals
        var ren = go.AddComponent<MeshRenderer>();
        ren.Mesh = mesh;
        ren.Material = material;

        return go;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, bool isStatic = false)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(name, Double3.Zero, Double3.One, _standardMaterial, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(Double3 position, bool isStatic = false)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(position.ToString(), position, Double3.One, _standardMaterial, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 position, bool isStatic = false)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(name, position, Double3.One, _standardMaterial, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 position, Double3 size, bool isStatic = false)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(name, position, size, _standardMaterial, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 position, Double3 size, Material material, bool isStatic = false)
    {
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(name, position, size, material, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 position, Double3 size, Material material, Mesh mesh, bool isStatic = false)
    {
        // game object
        var go = new GameObject(name);
        go.Transform.position = position;
        go.Transform.localScale = size;

        // visuals
        var ren = go.AddComponent<MeshRenderer>();
        ren.Mesh = mesh;
        ren.Material = material;

        // physics
        var rb = go.AddComponent<Rigidbody3D>();
        rb.IsStatic = isStatic;
        var col = go.AddComponent<BoxCollider>();
        // col.Size = size; // <- boxCollider is scaled with gameobject, no need to scale it here

        return go;
    }
}