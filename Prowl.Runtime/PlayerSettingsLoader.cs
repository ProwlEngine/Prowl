using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime.Audio;

namespace Prowl.Runtime;

/// <summary>
/// Loads and applies project settings from Echo YAML files in the built player.
/// Reads from Content/Settings/ folder and applies physics, audio, time, and tags/layers.
/// </summary>
public static class PlayerSettingsLoader
{
    private static string? _settingsDir;

    /// <summary>
    /// Applies every project setting and starts XR if the project asks for it. Runs before the first scene loads, so the
    /// scene wakes up with them: assets resolved in OnEnable see the budget, navmesh worlds read their settings as their
    /// surfaces register, and components can see XR running. Physics settings reach each scene as it loads.
    /// </summary>
    public static void Apply(string settingsDir)
    {
        _settingsDir = settingsDir;

        if (!Directory.Exists(settingsDir))
        {
            Debug.LogWarning($"[PlayerSettings] Settings directory not found: {settingsDir}");
            return;
        }

        ApplyAssetConfig(settingsDir);
        ApplyNavigation(settingsDir);
        ApplyAudio(settingsDir);
        ApplyTime(settingsDir);
        ApplyTagsAndLayers(settingsDir);

        // Physics needs to apply to each new scene's PhysicsWorld
        ApplyPhysics(settingsDir);

        // Re-apply physics whenever a new scene loads
        Resources.Scene.OnSceneLoaded += () =>
        {
            if (_settingsDir != null)
                ApplyPhysics(_settingsDir);
        };

        ApplyXR(settingsDir);
    }

    /// <summary>How long unused assets stay loaded, and how much memory they may use.</summary>
    private static void ApplyAssetConfig(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.Assets);
        if (settings == null) return;

        try
        {
            if (settings.TryGet("GracePeriodSeconds", out var grace))
                AssetDatabase.GracePeriod = TimeSpan.FromSeconds(grace!.FloatValue);
            if (settings.TryGet("MemoryBudgetMB", out var budget))
                AssetDatabase.MemoryBudget = (long)budget!.IntValue * 1024 * 1024;
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply asset config: {ex.Message}"); }
    }

    private static void ApplyPhysics(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.Physics);
        if (settings == null) return;

        try
        {
            float gx = settings.TryGet("GravityX", out var gxp) ? gxp!.FloatValue : 0;
            float gy = settings.TryGet("GravityY", out var gyp) ? gyp!.FloatValue : -9.81f;
            float gz = settings.TryGet("GravityZ", out var gzp) ? gzp!.FloatValue : 0;
            int solverIter = settings.TryGet("SolverIterations", out var si) ? si!.IntValue : 12;
            int relaxIter = settings.TryGet("RelaxIterations", out var ri) ? ri!.IntValue : 4;
            int subSteps = settings.TryGet("SubSteps", out var ss) ? ss!.IntValue : 3;
            bool sleep = !settings.TryGet("AllowSleep", out var sl) || sl!.BoolValue;
            bool mt = !settings.TryGet("UseMultithreading", out var mtp) || mtp!.BoolValue;
            bool sync = !settings.TryGet("AutoSyncTransforms", out var st) || st!.BoolValue;

            // Advanced settings
            bool determ = settings.TryGet("EnhancedDeterminism", out var dt) && dt!.BoolValue;
            bool persistThreads = settings.TryGet("ThreadModel", out var tmp)
                && tmp!.IntValue == (int)PhysicsThreadModel.Persistent;
            bool auxcp = !settings.TryGet("EnableAuxiliaryContactPoints", out var ax) || ax!.BoolValue;
            bool persistManifold = !settings.TryGet("PersistentContactManifold", out var pm) || pm!.BoolValue;
            float specRelax = settings.TryGet("SpeculativeRelaxationFactor", out var sr) ? sr!.FloatValue : 0.9f;

            var scene = Resources.Scene.Current;
            if (scene != null)
            {
                scene.Physics.Gravity = new Vector.Float3(gx, gy, gz);
                scene.Physics.SolverIterations = solverIter;
                scene.Physics.RelaxIterations = relaxIter;
                scene.Physics.Substep = subSteps;
                scene.Physics.AllowSleep = sleep;
                scene.Physics.UseMultithreading = mt;
                scene.Physics.AutoSyncTransforms = sync;
                scene.Physics.EnhancedDeterminism = determ;
                scene.Physics.ThreadModel = persistThreads ? PhysicsThreadModel.Persistent : PhysicsThreadModel.Regular;
                scene.Physics.EnableAuxiliaryContactPoints = auxcp;
                scene.Physics.PersistentContactManifold = persistManifold;
                scene.Physics.SpeculativeRelaxationFactor = specRelax;
            }

            // Collision matrix (uint[] serializes as a compound holding an "array" list)
            if (settings.TryGet("CollisionMatrixRows", out var cmProp) && cmProp!.TryGet("array", out var rows)
                && rows!.TagType == EchoType.List)
            {
                var packed = new uint[CollisionMatrix.LayerCount];
                int i = 0;
                foreach (var row in rows.List)
                {
                    if (i >= packed.Length) break;
                    packed[i++] = row.UIntValue;
                }

                CollisionMatrix.SetRows(packed);
            }

            Debug.Log("[PlayerSettings] Physics applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply physics: {ex.Message}"); }
    }

    private static void ApplyAudio(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.Audio);
        if (settings == null) return;

        try
        {
            float vol = settings.TryGet("GlobalVolume", out var v) ? v!.FloatValue : 1f;
            AudioContext.MasterVolume = vol;

            // Reopens the device only if the project asked for a format other than the one the game
            // loop opened it with.
            int rate = settings.TryGet("SampleRate", out var r) ? r!.IntValue : AudioContext.SampleRate;
            int channels = settings.TryGet("Channels", out var c) ? c!.IntValue : AudioContext.Channels;
            int buffer = settings.TryGet("BufferSize", out var b) ? b!.IntValue : AudioContext.PeriodSizeInFrames;

            if (rate > 0 && channels > 0 && buffer > 0)
                AudioContext.Restart((uint)rate, (uint)channels, (uint)buffer);

            Debug.Log("[PlayerSettings] Audio applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply audio: {ex.Message}"); }
    }

    /// <summary>Sets the XR render scale and starts XR when the project asks a built player to, which needs the window up.</summary>
    private static void ApplyXR(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.XR);
        if (settings == null) return;

        try
        {
            if (settings.TryGet("RenderScale", out var scale)) XR.RenderScale = scale!.FloatValue;
            bool start = settings.TryGet("StartInPlayer", out var sp) && sp!.BoolValue;
            var origin = settings.TryGet("TrackingOrigin", out var to) ? (XRTrackingOrigin)to!.LongValue : XRTrackingOrigin.Floor;
            if (start) XR.Start(origin);
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply XR: {ex.Message}"); }
    }

    private static void ApplyTime(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.Time);
        if (settings == null) return;

        try
        {
            float fixedDt = settings.TryGet("FixedTimestep", out var ft) ? ft!.FloatValue : 1f / 60f;
            float timeScale = settings.TryGet("DefaultTimeScale", out var ts) ? ts!.FloatValue : 1f;
            int maxIter = settings.TryGet("MaxFixedIterations", out var mi) ? mi!.IntValue : 3;

            Time.FixedDeltaTime = fixedDt;
            Time.TimeScale = timeScale;
            Time.MaxFixedIterations = maxIter;
            Debug.Log("[PlayerSettings] Time applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply time: {ex.Message}"); }
    }

    private static void ApplyTagsAndLayers(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.TagsAndLayers);
        if (settings == null) return;

        try
        {
            if (settings.TryGet("Tags", out var tagsProp) && Serializer.Deserialize<List<string>>(tagsProp) is { Count: > 0 } tags)
                TagLayerManager.tags = tags;

            if (settings.TryGet("Layers", out var layersProp) && Serializer.Deserialize<string[]>(layersProp) is { } layers)
                Array.Copy(layers, TagLayerManager.layers, Math.Min(layers.Length, TagLayerManager.layers.Length));

            Debug.Log("[PlayerSettings] Tags & Layers applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply tags/layers: {ex.Message}"); }
    }

    /// <summary>The navigation tables and world settings, which a navmesh world reads as its surfaces and agents register.</summary>
    private static void ApplyNavigation(string dir)
    {
        var settings = Read(dir, PlayerSettingsFiles.Navigation);
        if (settings == null) return;

        try
        {
            List<string>? names = settings.TryGet("AreaNames", out var namesProp) ? Serializer.Deserialize<List<string>>(namesProp) : null;
            List<float>? costs = settings.TryGet("AreaCosts", out var costsProp) ? Serializer.Deserialize<List<float>>(costsProp) : null;
            if (names?.Count > 0 || costs?.Count > 0)
            {
                NavMeshAreas.ApplyTable(names ?? [], costs ?? []);
                Debug.Log("[PlayerSettings] Navigation areas applied.");
            }

            if (settings.TryGet("AgentTypes", out var typesProp)
                && Serializer.Deserialize<List<NavMeshAgentType>>(typesProp) is { Count: > 0 } types)
            {
                NavMeshAgentTypes.ApplyTable(types);
                Debug.Log($"[PlayerSettings] Navigation agent types applied ({types.Count}).");
            }

            if (settings.TryGet("World", out var worldProp)
                && Serializer.Deserialize<NavMeshWorldSettings>(worldProp) is { } world)
                NavMeshWorld.ApplyProjectSettings(world);
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply navigation settings: {ex.Message}"); }
    }

    /// <summary>
    /// Reads one settings file, or null when there is nothing usable to read. A file that exists but
    /// cannot be parsed is reported, since falling back to defaults silently is how a shipped game ends
    /// up running with physics nobody configured.
    /// </summary>
    private static EchoObject? Read(string dir, string name)
    {
        string path = Path.Combine(dir, $"{name}.yaml");
        if (!File.Exists(path)) return null;

        try
        {
            return EchoObject.ReadFromYaml(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerSettings] Could not read '{name}.yaml', using defaults: {ex.Message}");
            return null;
        }
    }
}
