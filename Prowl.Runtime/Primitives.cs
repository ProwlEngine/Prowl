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
    public static GameObject Cube(string name, Double3 size)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(name, size, _standardMaterial, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name, Double3 size, Material material)
    {
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return Cube(name, size, material, _cubeMesh);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject Cube(string name, Double3 size, Material material, Mesh mesh)
    {
        // game object
        var go = new GameObject(name);
        go.Transform.localScale = size;

        // visuals
        var ren = go.AddComponent<MeshRenderer>();
        ren.Mesh = mesh;
        ren.Material = material;

        return go;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 size, bool isStatic = false)
    {
        _standardMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Standard));
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(name, size, _standardMaterial, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 size, Material material, bool isStatic = false)
    {
        _cubeMesh ??= Mesh.CreateCube(Double3.One);
        return PhysicsCube(name, size, material, _cubeMesh, isStatic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GameObject PhysicsCube(string name, Double3 size, Material material, Mesh mesh, bool isStatic = false)
    {
        // game object
        var go = new GameObject(name);
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