using Prowl.Vector;
using Prowl.Photonic.Scene.Lights;

namespace Prowl.Photonic;

/// <summary>
/// The bake scene: build meshes, materials and lights here, then call <see cref="End"/> before
/// starting a job.
/// </summary>
public sealed class BakeScene
{
    /// <summary>Scene name (informational).</summary>
    public string Name { get; }

    /// <summary>True once <see cref="End"/> has been called; further mutation throws.</summary>
    public bool Ended { get; private set; }

    private readonly System.Collections.Generic.Dictionary<string, BakeMesh> _meshes = new();
    private readonly System.Collections.Generic.Dictionary<string, BakeMaterial> _materials = new();
    private readonly System.Collections.Generic.Dictionary<string, BakeTexture> _textures = new();
    private readonly System.Collections.Generic.List<Light> _lights = new();

    /// <summary>Default attenuation applied to point/spot lights that don't override it themselves.</summary>
    public Sampling.IAttenuation DefaultAttenuation { get; set; } = new Sampling.InverseSquareAttenuation();

    internal BakeScene(string name) { Name = name; }

    /// <summary>Meshes added to this scene, keyed by name.</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, BakeMesh> Meshes => _meshes;

    /// <summary>Materials in this scene, keyed by name.</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, BakeMaterial> Materials => _materials;

    /// <summary>Textures in this scene, keyed by name.</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, BakeTexture> Textures => _textures;

    /// <summary>Lights in this scene, in insertion order.</summary>
    public System.Collections.Generic.IReadOnlyList<Light> Lights => _lights;

    // ---- mesh / material / texture builders --------------------------------------------------------

    /// <summary>Begin building a new mesh. Returns a fluent builder; call <c>.End()</c> to finalise.</summary>
    public BakeMesh.Builder BeginMesh(string name)
    {
        EnsureOpen();
        if (_meshes.ContainsKey(name)) throw new System.InvalidOperationException($"Mesh '{name}' already exists.");
        return new BakeMesh.Builder(this, name);
    }

    internal void RegisterMesh(BakeMesh mesh) => _meshes.Add(mesh.Name, mesh);

    /// <summary>Find an existing mesh, or null.</summary>
    public BakeMesh? FindMesh(string name) => _meshes.TryGetValue(name, out var m) ? m : null;

    /// <summary>Create a material. Returns the new instance; populate properties directly.</summary>
    public BakeMaterial CreateMaterial(string name)
    {
        EnsureOpen();
        if (_materials.ContainsKey(name)) throw new System.InvalidOperationException($"Material '{name}' already exists.");
        var m = new BakeMaterial(name);
        _materials.Add(name, m);
        return m;
    }

    /// <summary>Find an existing material, or null.</summary>
    public BakeMaterial? FindMaterial(string name) => _materials.TryGetValue(name, out var m) ? m : null;

    /// <summary>Register an RGBA byte texture (linear or sRGB; <paramref name="inputGamma"/> says which).</summary>
    public BakeTexture CreateTextureRGBA(string name, int width, int height, byte[] pixelsRGBA, float inputGamma = 2.2f)
    {
        EnsureOpen();
        if (_textures.ContainsKey(name)) throw new System.InvalidOperationException($"Texture '{name}' already exists.");
        var t = new BakeTexture(name, width, height, pixelsRGBA, inputGamma);
        _textures.Add(name, t);
        return t;
    }

    /// <summary>Find an existing texture, or null.</summary>
    public BakeTexture? FindTexture(string name) => _textures.TryGetValue(name, out var t) ? t : null;

    // ---- lights ------------------------------------------------------------------------------------

    /// <summary>Add a point light at the translation of <paramref name="xform"/> with the given range and linear-RGB intensity.</summary>
    public PointLight CreatePointLight(string name, Float4x4 xform, Float3 color, float range)
    {
        EnsureOpen();
        var l = new PointLight(name, xform, color, range);
        _lights.Add(l);
        return l;
    }

    /// <summary>Add a directional light. <paramref name="xform"/>'s -Z column is the light direction.</summary>
    public DirectionalLight CreateDirectionalLight(string name, Float4x4 xform, Float3 color)
    {
        EnsureOpen();
        var l = new DirectionalLight(name, xform, color);
        _lights.Add(l);
        return l;
    }

    /// <summary>Add a spot light. Cone angle is in radians; -Z column is the axis.</summary>
    public SpotLight CreateSpotLight(string name, Float4x4 xform, Float3 color, float range, float coneAngle)
    {
        EnsureOpen();
        var l = new SpotLight(name, xform, color, range, coneAngle);
        _lights.Add(l);
        return l;
    }

    // ---- lifecycle ---------------------------------------------------------------------------------

    /// <summary>Finalise the scene (no further mutation).</summary>
    public void End() => Ended = true;

    private void EnsureOpen()
    {
        if (Ended) throw new System.InvalidOperationException("Scene has already been ended.");
    }
}
