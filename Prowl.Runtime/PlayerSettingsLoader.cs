using System;
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

    /// <summary>Apply all project settings and register for scene load events.</summary>
    public static void Apply(string settingsDir)
    {
        _settingsDir = settingsDir;

        if (!Directory.Exists(settingsDir))
        {
            Debug.LogWarning($"[PlayerSettings] Settings directory not found: {settingsDir}");
            return;
        }

        ApplyAssetConfig(settingsDir);
        ApplyAudio(settingsDir);
        ApplyTime(settingsDir);
        ApplyTagsAndLayers(settingsDir);
        ApplyGeneral(settingsDir);

        // Physics needs to apply to each new scene's PhysicsWorld
        ApplyPhysics(settingsDir);

        // Re-apply physics whenever a new scene loads
        Resources.Scene.OnSceneLoaded += () =>
        {
            if (_settingsDir != null)
                ApplyPhysics(_settingsDir);
        };
    }

    /// <summary>
    /// Apply the async-asset-loading toggle. Exposed separately so the player can set it
    /// BEFORE the default scene loads (component OnEnable may resolve AssetRefs), not just
    /// during the bulk <see cref="Apply"/> that runs after scene load.
    /// </summary>
    public static void ApplyAssetConfig(string dir)
    {
        var settings = Read(dir, "Assets");
        if (settings == null) return;

        try
        {
            // Default ON if the key is absent.
            bool async = !settings.TryGet("AsyncAssetLoading", out var a) || a!.BoolValue;
            AssetLoadingConfig.AsyncEnabled = async;
            Debug.Log($"[PlayerSettings] Async asset loading: {async}.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply asset config: {ex.Message}"); }
    }

    private static void ApplyPhysics(string dir)
    {
        var settings = Read(dir, "Physics");
        if (settings == null) return;

        try
        {
            float gx = settings.TryGet("GravityX", out var gxp) ? gxp!.FloatValue : 0;
            float gy = settings.TryGet("GravityY", out var gyp) ? gyp!.FloatValue : -9.81f;
            float gz = settings.TryGet("GravityZ", out var gzp) ? gzp!.FloatValue : 0;
            int solverIter = settings.TryGet("SolverIterations", out var si) ? si!.IntValue : 8;
            int relaxIter = settings.TryGet("RelaxIterations", out var ri) ? ri!.IntValue : 4;
            int subSteps = settings.TryGet("SubSteps", out var ss) ? ss!.IntValue : 2;
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
                int i = 0;
                foreach (var row in rows.List)
                {
                    if (i >= 32) break;
                    uint val = row.UIntValue;
                    for (int j = 0; j < 32; j++)
                        CollisionMatrix.SetLayerCollision(i, j, (val & (1u << j)) != 0);
                    i++;
                }
            }

            Debug.Log("[PlayerSettings] Physics applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply physics: {ex.Message}"); }
    }

    private static void ApplyAudio(string dir)
    {
        var settings = Read(dir, "Audio");
        if (settings == null) return;

        try
        {
            float vol = settings.TryGet("GlobalVolume", out var v) ? v!.FloatValue : 1f;
            AudioContext.MasterVolume = vol;
            Debug.Log("[PlayerSettings] Audio applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply audio: {ex.Message}"); }
    }

    private static void ApplyTime(string dir)
    {
        var settings = Read(dir, "Time");
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
        var settings = Read(dir, "Tags & Layers");
        if (settings == null) return;

        try
        {
            // Tags is a List<string> (serializes as a list directly).
            if (settings.TryGet("Tags", out var tagsProp) && tagsProp!.TagType == EchoType.List)
            {
                TagLayerManager.tags.Clear();
                foreach (var tag in tagsProp.List)
                    TagLayerManager.tags.Add(tag.StringValue);
            }

            // Layers is a string[] (serializes as a compound holding an "array" list).
            if (settings.TryGet("Layers", out var layersProp) && layersProp!.TryGet("array", out var layers)
                && layers!.TagType == EchoType.List)
            {
                int i = 0;
                foreach (var layer in layers.List)
                {
                    if (i >= TagLayerManager.layers.Length) break;
                    TagLayerManager.layers[i++] = layer.StringValue;
                }
            }

            Debug.Log("[PlayerSettings] Tags & Layers applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply tags/layers: {ex.Message}"); }
    }

    private static void ApplyGeneral(string dir)
    {
        // Informational only for now
    }

    private static EchoObject? Read(string dir, string name)
    {
        string path = Path.Combine(dir, $"{name}.yaml");
        if (!File.Exists(path)) return null;

        try
        {
            return EchoObject.ReadFromYaml(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }
}
