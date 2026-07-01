using System;
using System.IO;
using System.Text.Json;

using Prowl.Runtime.Audio;

namespace Prowl.Runtime;

/// <summary>
/// Loads and applies project settings from JSON files in the built player.
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
        var jsonN = ReadJson(dir, "Assets.json");
        if (jsonN == null) return;
        var json = jsonN.Value;

        try
        {
            // Default ON if the key is absent.
            bool async = !json.TryGetProperty("AsyncAssetLoading", out var a) || a.GetBoolean();
            AssetLoadingConfig.AsyncEnabled = async;
            Debug.Log($"[PlayerSettings] Async asset loading: {async}.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply asset config: {ex.Message}"); }
    }

    private static void ApplyPhysics(string dir)
    {
        var jsonN = ReadJson(dir, "Physics.json");
        if (jsonN == null) return;
        var json = jsonN.Value;

        try
        {
            float gx = json.TryGetProperty("GravityX", out var gxp) ? gxp.GetSingle() : 0;
            float gy = json.TryGetProperty("GravityY", out var gyp) ? gyp.GetSingle() : -9.81f;
            float gz = json.TryGetProperty("GravityZ", out var gzp) ? gzp.GetSingle() : 0;
            int solverIter = json.TryGetProperty("SolverIterations", out var si) ? si.GetInt32() : 8;
            int relaxIter = json.TryGetProperty("RelaxIterations", out var ri) ? ri.GetInt32() : 4;
            int subSteps = json.TryGetProperty("SubSteps", out var ss) ? ss.GetInt32() : 2;
            bool sleep = !json.TryGetProperty("AllowSleep", out var sl) || sl.GetBoolean();
            bool mt = !json.TryGetProperty("UseMultithreading", out var mtp) || mtp.GetBoolean();
            bool sync = !json.TryGetProperty("AutoSyncTransforms", out var st) || st.GetBoolean();

            // Advanced settings
            bool determ = json.TryGetProperty("EnhancedDeterminism", out var dt) && dt.GetBoolean();
            bool persistThreads = false;
            if (json.TryGetProperty("ThreadModel", out var tmp))
            {
                if (tmp.ValueKind == JsonValueKind.Number)
                    persistThreads = tmp.GetInt32() == (int)PhysicsThreadModel.Persistent;
                else if (tmp.ValueKind == JsonValueKind.String)
                    persistThreads = string.Equals(tmp.GetString(), "Persistent", StringComparison.OrdinalIgnoreCase);
            }
            bool auxcp = !json.TryGetProperty("EnableAuxiliaryContactPoints", out var ax) || ax.GetBoolean();
            bool persistManifold = !json.TryGetProperty("PersistentContactManifold", out var pm) || pm.GetBoolean();
            float specRelax = json.TryGetProperty("SpeculativeRelaxationFactor", out var sr) ? sr.GetSingle() : 0.9f;

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

            // Collision matrix
            if (json.TryGetProperty("CollisionMatrixRows", out var cmProp) && cmProp.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var row in cmProp.EnumerateArray())
                {
                    if (i >= 32) break;
                    uint val = row.GetUInt32();
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
        var jsonN = ReadJson(dir, "Audio.json");
        if (jsonN == null) return;
        var json = jsonN.Value;

        try
        {
            float vol = json.TryGetProperty("GlobalVolume", out var v) ? v.GetSingle() : 1f;
            AudioContext.MasterVolume = vol;
            Debug.Log("[PlayerSettings] Audio applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply audio: {ex.Message}"); }
    }

    private static void ApplyTime(string dir)
    {
        var jsonN = ReadJson(dir, "Time.json");
        if (jsonN == null) return;
        var json = jsonN.Value;

        try
        {
            float fixedDt = json.TryGetProperty("FixedTimestep", out var ft) ? ft.GetSingle() : 1f / 60f;
            float timeScale = json.TryGetProperty("DefaultTimeScale", out var ts) ? ts.GetSingle() : 1f;
            int maxIter = json.TryGetProperty("MaxFixedIterations", out var mi) ? mi.GetInt32() : 3;

            Time.FixedDeltaTime = fixedDt;
            Time.TimeScale = timeScale;
            Time.MaxFixedIterations = maxIter;
            Debug.Log("[PlayerSettings] Time applied.");
        }
        catch (Exception ex) { Debug.LogWarning($"[PlayerSettings] Failed to apply time: {ex.Message}"); }
    }

    private static void ApplyTagsAndLayers(string dir)
    {
        var jsonN = ReadJson(dir, "Tags & Layers.json");
        if (jsonN == null) return;
        var json = jsonN.Value;

        try
        {
            if (json.TryGetProperty("Tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                TagLayerManager.tags.Clear();
                foreach (var tag in tagsProp.EnumerateArray())
                    TagLayerManager.tags.Add(tag.GetString() ?? "");
            }

            if (json.TryGetProperty("Layers", out var layersProp) && layersProp.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var layer in layersProp.EnumerateArray())
                {
                    if (i >= TagLayerManager.layers.Length) break;
                    TagLayerManager.layers[i++] = layer.GetString() ?? "";
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

    private static JsonElement? ReadJson(string dir, string fileName)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return null;

        try
        {
            string text = File.ReadAllText(path);
            // Clone() detaches the element from the document so we can dispose the document (which
            // returns its pooled buffer) instead of leaking it for the element's lifetime.
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}
