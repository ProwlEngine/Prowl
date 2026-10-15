// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using SphericalHarmonicsL2 = Prowl.Runtime.Rendering.SphericalHarmonicsL2;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Something in a scene that keeps state in the scene's <see cref="RenderWorld"/>: a renderer, a light, a probe. It
/// joins the scene's rendering when enabled and leaves when disabled, and pushes what changed once a frame.
/// </summary>
public interface IRenderSync
{
    /// <summary>
    /// Brings the world up to date with the component. Main thread, once a frame the scene renders. Returns true when the
    /// component is static and wholly in the world, nothing of it still loading, so while playing it is left alone from then
    /// on; static objects do not change at runtime, and <see cref="SceneRendering.Refresh"/> syncs one again if one does.
    /// </summary>
    bool Sync(RenderWorld world);

    /// <summary>Removes everything the component added to the world.</summary>
    void Detach(RenderWorld world);
}

/// <summary>
/// A scene's link to Graphite: its <see cref="RenderWorld"/>, made when a renderer exists, the components that keep
/// state in it, and the environment (ambient, fog, sky, sun) taken from the scene's settings.
/// </summary>
public sealed class SceneRendering : IDisposable
{
    private readonly Scene _scene;
    private readonly List<IRenderSync> _members = [];
    private readonly HashSet<IRenderSync> _memberSet = [];

    // Static members wholly in the world while playing, not synced again until refreshed or the game stops
    private readonly HashSet<IRenderSync> _parked = [];
    private RenderWorld? _world;
    private ulong _syncedFrame = ulong.MaxValue;
    private readonly List<Light> _directionals = [];

    // Instances drawn for one frame through Graphics.DrawMeshInstanced, a batch for each mesh, material and settings
    private sealed class ImmediateBatch
    {
        public readonly List<InstanceData> Instances = [];
        public InstanceBatchHandle Handle;
    }

    private readonly Dictionary<(GpuMesh Mesh, GMaterial Material, RenderableFlags Flags, uint LayerMask), ImmediateBatch> _immediate = [];
    private readonly List<(GpuMesh, GMaterial, RenderableFlags, uint)> _immediateGone = [];

    internal SceneRendering(Scene scene) => _scene = scene;

    /// <summary>The scene's world, made on first use. Null when headless.</summary>
    public RenderWorld? World
    {
        get
        {
            if (_world == null && Graphics.Renderer != null)
                _world = Graphics.Renderer.CreateWorld();
            return _world;
        }
    }

    /// <summary>The world if it was made, without making one.</summary>
    public RenderWorld? WorldIfMade => _world;

    /// <summary>Adds a component to its scene's rendering, for OnEnable.</summary>
    public static void Join(IRenderSync member, GameObject gameObject)
    {
        if (gameObject.IsValid() && gameObject.Scene.IsValid()) gameObject.Scene.Rendering.Add(member);
    }

    /// <summary>Removes a component from its scene's rendering, for OnDisable.</summary>
    public static void Leave(IRenderSync member, GameObject gameObject)
    {
        if (gameObject.IsValid() && gameObject.Scene.IsValid() && !gameObject.Scene.IsDisposed) gameObject.Scene.Rendering.Remove(member);
    }

    public void Add(IRenderSync member)
    {
        if (_memberSet.Add(member))
            _members.Add(member);
    }

    public void Remove(IRenderSync member)
    {
        if (!_memberSet.Remove(member)) return;
        if (!_parked.Remove(member)) _members.Remove(member);
        if (_world != null)
        {
            try { member.Detach(_world); }
            catch (Exception e) { Debug.LogError($"Removing {member.GetType().Name} from the render world threw: {e}"); }
        }
    }

    /// <summary>Whether a component is currently joined to this scene's rendering.</summary>
    internal bool IsMember(IRenderSync? member) => member is not null && _memberSet.Contains(member);

    /// <summary>Syncs a static member again next frame, for one a script changed at runtime.</summary>
    public void Refresh(IRenderSync member)
    {
        if (_parked.Remove(member)) _members.Add(member);
    }

    /// <summary>Adds instances to this frame's batch of a mesh and material, drawn at the next sync and then gone.</summary>
    internal void DrawInstanced(GpuMesh mesh, GMaterial material, RenderableFlags flags, uint layerMask, ReadOnlySpan<Float4x4> transforms, ReadOnlySpan<Color> colors)
    {
        if (!_immediate.TryGetValue((mesh, material, flags, layerMask), out ImmediateBatch? batch))
            _immediate[(mesh, material, flags, layerMask)] = batch = new ImmediateBatch();
        for (int i = 0; i < transforms.Length; i++)
        {
            Color c = i < colors.Length ? colors[i] : Color.White;
            batch.Instances.Add(new InstanceData { Transform = transforms[i], Color = new Float4(c.R, c.G, c.B, c.A) });
        }
    }

    // Brings each immediate batch to this frame's instances, removing the ones nothing drew
    private void SyncImmediate(RenderWorld world)
    {
        foreach (((GpuMesh mesh, GMaterial material, RenderableFlags flags, uint layerMask) key, ImmediateBatch batch) in _immediate)
        {
            if (batch.Instances.Count == 0 || key.mesh.IsDisposed || key.material.IsDisposed)
            {
                if (batch.Handle.IsValid) world.RemoveInstanceBatch(batch.Handle);
                _immediateGone.Add(key);
                continue;
            }
            ReadOnlySpan<InstanceData> instances = CollectionsMarshal.AsSpan(batch.Instances);
            if (batch.Handle.IsValid)
                world.SetInstances(batch.Handle, instances);
            else
                batch.Handle = world.AddInstanceBatch(new InstanceBatchDesc { Mesh = key.mesh, Transform = Double4x4.Identity, LayerMask = key.layerMask, Flags = key.flags }, [key.material], instances);
            batch.Instances.Clear();
        }
        foreach ((GpuMesh, GMaterial, RenderableFlags, uint) key in _immediateGone)
            _immediate.Remove(key);
        _immediateGone.Clear();
    }

    internal void AddDirectional(Light light) { if (!_directionals.Contains(light)) _directionals.Add(light); }
    internal void RemoveDirectional(Light light) => _directionals.Remove(light);

    /// <summary>
    /// Pushes this frame's changes into the world: every member's, then the environment. Once a frame however many
    /// views draw the scene. Main thread.
    /// </summary>
    public void Sync()
    {
        RenderWorld? world = World;
        if (world == null || _syncedFrame == Graphics.FrameIndex) return;
        _syncedFrame = Graphics.FrameIndex;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        int synced = _members.Count;

        bool playing = Application.IsPlaying;
        if (!playing && _parked.Count > 0)
        {
            _members.AddRange(_parked);
            _parked.Clear();
        }
        for (int i = 0; i < _members.Count; i++)
        {
            IRenderSync member = _members[i];
            bool settled = false;
            try { settled = member.Sync(world); }
            catch (Exception e) { Debug.LogErrorOnce($"RenderSync.{member.GetType().Name}", $"Syncing {member.GetType().Name} into the render world threw: {e}"); }
            if (!settled || !playing) continue;
            _parked.Add(member);
            _members[i] = _members[^1];
            _members.RemoveAt(_members.Count - 1);
            i--;
        }

        SyncImmediate(world);
        SyncEnvironment(world);
        SyncProbes(world);
        RenderStats.AddSceneSync(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, synced, _parked.Count);
    }

    private Scene.BakedLightingData? _probesFrom;
    private int _probesVersion = -1;

    private void SyncProbes(RenderWorld world)
    {
        Scene.BakedLightingData baked = _scene.BakedLighting;
        if (baked == _probesFrom && _scene.BakedLightingVersion == _probesVersion) return;
        _probesFrom = baked;
        _probesVersion = _scene.BakedLightingVersion;

        if (baked == null || !baked.HasProbes)
        {
            world.SetLightProbes(null);
            return;
        }

        var positions = new Double3[baked.ProbePositions.Length];
        for (int i = 0; i < positions.Length; i++)
            positions[i] = new Double3(baked.ProbePositions[i].X, baked.ProbePositions[i].Y, baked.ProbePositions[i].Z);
        var coefficients = new Graphite.Rendering.SphericalHarmonicsL2[baked.ProbeSH.Length];
        for (int i = 0; i < coefficients.Length; i++)
        {
            SphericalHarmonicsL2 sh = baked.ProbeSH[i];
            coefficients[i] = new Graphite.Rendering.SphericalHarmonicsL2 { C0 = sh.C0, C1 = sh.C1, C2 = sh.C2, C3 = sh.C3, C4 = sh.C4, C5 = sh.C5, C6 = sh.C6, C7 = sh.C7, C8 = sh.C8 };
        }
        world.SetLightProbes(new LightProbeData
        {
            Positions = positions,
            Coefficients = coefficients,
            Tetrahedra = baked.ProbeTetrahedra,
            Neighbours = baked.ProbeTetNeighbours,
        });
    }

    private void SyncEnvironment(RenderWorld world)
    {
        EnvironmentDesc env = world.Environment;
        _scene.Environment.ApplyTo(env);

        Light? sun = null;
        foreach (Light light in _directionals)
            if (light.Handle.IsValid && (sun == null || light.Intensity > sun.Intensity)) sun = light;
        env.Sun = sun.IsValid() ? sun.Handle : default;
    }

    /// <summary>Whether the scene draws a sky material, which a camera clearing to its sky shows.</summary>
    internal bool HasSky => _scene.Environment.SkyMaterial.IsValid();

    /// <summary>
    /// The ambient light a surface facing <paramref name="normal"/> receives from the scene's environment, intensity
    /// included. Sky ambient is the sky's real light read back from the GPU, the sky colour until it arrives.
    /// </summary>
    public Float3 Ambient(Float3 normal) => _scene.Environment.EvaluateAmbient(normal, _world);

    public void Dispose()
    {
        if (_world != null)
        {
            foreach (IRenderSync member in _memberSet)
            {
                try { member.Detach(_world); }
                catch (Exception e) { Debug.LogError($"Removing {member.GetType().Name} from the render world threw: {e}"); }
            }
            _world.Dispose();
            _world = null;
        }
        _members.Clear();
        _memberSet.Clear();
        _parked.Clear();
        _immediate.Clear();
        _directionals.Clear();
    }
}
