// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;
using System.Collections.Generic;

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
    /// component is wholly in the world, nothing of it still loading, so while playing it is left alone until something
    /// wakes it: its Transform or an ancestor's moving, its GameObject's layer or static flag, a change to the mesh it
    /// draws, a lightmap rebake, or the component calling <see cref="SceneRendering.Wake"/> from a setting it changed.
    /// </summary>
    bool Sync(RenderWorld world);

    /// <summary>The mesh the component last synced, so editing that mesh wakes it.</summary>
    Mesh? SyncedMesh => null;

    /// <summary>Removes everything the component added to the world.</summary>
    void Detach(RenderWorld world);
}

/// <summary>
/// A scene's link to Graphite: its <see cref="RenderWorld"/>, made when a renderer exists, the components that keep
/// state in it, and the environment (ambient, fog, sky, sun) taken from the scene's settings.
/// </summary>
public sealed class SceneRendering : IDisposable
{
    private readonly SceneInstance _scene;
    private readonly List<IRenderSync> _members = [];
    private readonly HashSet<IRenderSync> _memberSet = [];

    // Members wholly in the world while playing, not synced again until woken or the game stops
    private readonly HashSet<IRenderSync> _parked = [];

    // Parked members by the mesh they draw, so a mesh edit wakes them, and meshes edited since the last sync
    private readonly Dictionary<Mesh, HashSet<IRenderSync>> _meshUsers = [];
    private readonly Dictionary<IRenderSync, Mesh> _parkedMeshes = [];

    // Emptied user sets kept for the next mesh to park, as a mover alone on its mesh empties and refills one every frame
    private readonly Stack<HashSet<IRenderSync>> _spareUserSets = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<Mesh> _changedMeshes = new();
    private readonly Action<Mesh> _onMeshChanged;
    private int _lightingVersion = -1;
    private int _qualityVersion = -1;

    /// <summary>
    /// Moves once every scene sync. A Transform pushes its move to its renderers and its children's at most once a sync,
    /// skipping the walk when it already did this sync.
    /// </summary>
    internal static uint Epoch { get; private set; } = 1;
    private RenderWorld? _world;
    private ulong _syncedFrame = ulong.MaxValue;
    private readonly List<DirectionalLight> _directionals = [];

    internal SceneRendering(SceneInstance scene)
    {
        _scene = scene;
        _onMeshChanged = _changedMeshes.Enqueue;
        Mesh.GeometryChanged += _onMeshChanged;
    }

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

    /// <summary>Syncs a component again next frame, for a setting of it that changed.</summary>
    public static void Wake(IRenderSync member, GameObject? gameObject)
    {
        if (gameObject.IsValid() && gameObject.Scene.IsValid() && !gameObject.Scene.IsDisposed) gameObject.Scene.Rendering.Refresh(member);
    }

    public void Add(IRenderSync member)
    {
        if (_memberSet.Add(member))
            _members.Add(member);
    }

    public void Remove(IRenderSync member)
    {
        if (!_memberSet.Remove(member)) return;
        if (!Unpark(member)) _members.Remove(member);
        if (_world != null)
        {
            try { member.Detach(_world); }
            catch (Exception e) { Debug.LogException($"Removing {member.GetType().Name} from the render world threw", e); }
        }
    }

    /// <summary>Whether a member is parked, left alone until something wakes it.</summary>
    internal bool IsParked(IRenderSync member) => _parked.Contains(member);

    /// <summary>Whether a component is currently joined to this scene's rendering.</summary>
    internal bool IsMember(IRenderSync? member) => member is not null && _memberSet.Contains(member);

    /// <summary>Syncs a parked member again next frame.</summary>
    public void Refresh(IRenderSync member)
    {
        if (Unpark(member)) _members.Add(member);
    }

    /// <summary>Parks a member as a sync that found it settled does, moving <see cref="Epoch"/> as the end of a sync does.</summary>
    internal void Settle(IRenderSync member)
    {
        if (_members.Remove(member)) Park(member);
        Epoch++;
    }

    private void Park(IRenderSync member)
    {
        _parked.Add(member);
        if (member.SyncedMesh is not { } mesh) return;
        if (!_meshUsers.TryGetValue(mesh, out HashSet<IRenderSync>? users))
            _meshUsers[mesh] = users = _spareUserSets.Count > 0 ? _spareUserSets.Pop() : [];
        users.Add(member);
        _parkedMeshes[member] = mesh;
    }

    private bool Unpark(IRenderSync member)
    {
        if (!_parked.Remove(member)) return false;
        if (_parkedMeshes.Remove(member, out Mesh? mesh) && _meshUsers.TryGetValue(mesh, out HashSet<IRenderSync>? users))
        {
            users.Remove(member);
            if (users.Count == 0)
            {
                _meshUsers.Remove(mesh);
                _spareUserSets.Push(users);
            }
        }
        return true;
    }

    private void UnparkAll()
    {
        _members.AddRange(_parked);
        _parked.Clear();
        _meshUsers.Clear();
        _parkedMeshes.Clear();
        _spareUserSets.Clear();
    }

    // Wakes the members drawing a mesh edited since the last sync, and every member after a lightmap rebake or a quality change
    internal void WakeChanged()
    {
        if (_scene.BakedLightingVersion != _lightingVersion || QualitySettings.Version != _qualityVersion)
        {
            _lightingVersion = _scene.BakedLightingVersion;
            _qualityVersion = QualitySettings.Version;
            UnparkAll();
        }
        while (_changedMeshes.TryDequeue(out Mesh? mesh))
        {
            if (!_meshUsers.TryGetValue(mesh, out HashSet<IRenderSync>? users)) continue;
            foreach (IRenderSync member in (IRenderSync[])[.. users])
                Refresh(member);
        }
    }

    internal void AddDirectional(DirectionalLight light) { if (!_directionals.Contains(light)) _directionals.Add(light); }
    internal void RemoveDirectional(DirectionalLight light) => _directionals.Remove(light);

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

        bool playing = _scene.IsPlaying;
        if (!playing && _parked.Count > 0)
            UnparkAll();
        WakeChanged();
        for (int i = 0; i < _members.Count; i++)
        {
            IRenderSync member = _members[i];
            bool settled = false;
            try { settled = member.Sync(world); }
            catch (Exception e) { Debug.LogErrorOnce($"RenderSync.{member.GetType().Name}", $"Syncing {member.GetType().Name} into the render world threw: {e}"); }
            if (!settled || !playing) continue;
            Park(member);
            _members[i] = _members[^1];
            _members.RemoveAt(_members.Count - 1);
            i--;
        }

        SyncEnvironment(world);
        SyncProbes(world);
        Epoch++;
        RenderStats.AddSceneSync(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, synced, _parked.Count);
    }

    private SceneLighting? _probesFrom;
    private int _probesVersion = -1;

    private void SyncProbes(RenderWorld world)
    {
        SceneLighting baked = _scene.Lighting;
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

    private readonly List<FogVolumeDesc> _fogVolumes = [];

    // The scene's enabled fog volumes, their density a multiple of the volumetric fog's
    private FogVolumeDesc[] FogVolumes(float density)
    {
        _fogVolumes.Clear();
        foreach (GameObject go in _scene.Objects.Where(o => o.EnabledInHierarchy))
        {
            FogVolume? volume = go.GetComponent<FogVolume>();
            if (volume.IsValid() && volume.EnabledInHierarchy && _fogVolumes.Count < EnvironmentDesc.MaxFogVolumes)
                _fogVolumes.Add(volume.Describe(density));
        }
        return [.. _fogVolumes];
    }

    private Graphite.Rendering.VolumetricCloudsFeature? _clouds;

    // The scene's clouds in the world while they are on, their shadow pass ahead of every view's surfaces
    private void SyncClouds(RenderWorld world)
    {
        SceneEnvironment.VolumetricCloudsSettings settings = _scene.Environment.VolumetricClouds;
        if (!settings.Enabled || settings.Coverage <= 0f || Graphics.Renderer is not { } renderer)
        {
            if (_clouds != null)
            {
                world.Features.Remove(_clouds.ShadowPass);
                world.Features.Remove(_clouds);
            }
            return;
        }
        _clouds ??= new Graphite.Rendering.VolumetricCloudsFeature(renderer);
        settings.ApplyTo(_clouds, Time.DeltaTime);
        world.Features.Add(_clouds.ShadowPass);
        world.Features.Add(_clouds);
    }

    private void SyncEnvironment(RenderWorld world)
    {
        EnvironmentDesc env = world.Environment;
        _scene.Environment.ApplyTo(env);
        env.FogVolumes = FogVolumes(_scene.Environment.VolumetricFog.Density);
        SyncClouds(world);

        DirectionalLight? sun = null;
        foreach (DirectionalLight light in _directionals)
            if (light.Handle.IsValid && (sun.IsNotValid() || light.Intensity > sun.Intensity)) sun = light;
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
        Mesh.GeometryChanged -= _onMeshChanged;
        if (_world != null)
        {
            foreach (IRenderSync member in _memberSet)
            {
                try { member.Detach(_world); }
                catch (Exception e) { Debug.LogException($"Removing {member.GetType().Name} from the render world threw", e); }
            }
            _world.Dispose();
            _world = null;
        }
        _clouds?.Dispose();
        _clouds = null;
        _members.Clear();
        _memberSet.Clear();
        _parked.Clear();
        _meshUsers.Clear();
        _parkedMeshes.Clear();
        _spareUserSets.Clear();
        _directionals.Clear();
    }
}
