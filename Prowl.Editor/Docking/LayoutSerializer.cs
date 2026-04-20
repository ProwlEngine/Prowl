using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Docking;

/// <summary>
/// Serializes and deserializes dock layouts to/from JSON.
/// Panel types are stored by their full type name for reconstruction.
/// </summary>
public static class LayoutSerializer
{
    /// <summary>Save the current layout to the project's Library/EditorState.json.</summary>
    public static void Save(DockSpace dockSpace)
    {
        var project = Project.Current;
        if (project == null) return;

        try
        {
            var root = new JsonObject
            {
                ["root"] = SerializeNode(dockSpace.Root),
                ["floatingWindows"] = SerializeFloating(dockSpace.FloatingWindows),
            };

            string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(project.EditorStatePath, json);
            Debug.Log("Saved editor layout.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to save layout: {ex.Message}");
        }
    }

    /// <summary>Load layout from the project's Library/EditorState.json. Returns null if not found.</summary>
    public static DockNode? Load(DockSpace dockSpace)
    {
        var project = Project.Current;
        if (project == null) return null;

        string path = project.EditorStatePath;
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            var doc = JsonNode.Parse(json);
            if (doc == null) return null;

            var root = DeserializeNode(doc["root"]);

            // Floating windows
            dockSpace.FloatingWindows.Clear();
            if (doc["floatingWindows"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item == null) continue;
                    var node = DeserializeNode(item["node"]);
                    if (node == null) continue;

                    float x = item["x"]?.GetValue<float>() ?? 200;
                    float y = item["y"]?.GetValue<float>() ?? 200;
                    float w = item["w"]?.GetValue<float>() ?? 400;
                    float h = item["h"]?.GetValue<float>() ?? 300;

                    dockSpace.FloatingWindows.Add(new FloatingWindow(node, new Float2(x, y), new Float2(w, h)));
                }
            }

            Debug.Log("Loaded editor layout.");
            return root;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load layout: {ex.Message}");
            return null;
        }
    }

    private static JsonObject SerializeNode(DockNode? node)
    {
        if (node == null) return new JsonObject { ["type"] = "null" };

        if (node.IsLeaf)
        {
            // Store each tab as {type, state} so each panel instance can round-trip its own
            // data. Kept as a list of objects (not a parallel state array) so adding/removing
            // a tab doesn't desync state with type.
            var tabs = new JsonArray();
            foreach (var panel in node.Tabs!)
            {
                var entry = new JsonObject
                {
                    ["type"] = panel.GetType().FullName,
                };
                var state = new JsonObject();
                try
                {
                    if (panel.SerializeState(state) && state.Count > 0)
                        entry["state"] = state;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Panel '{panel.GetType().Name}' SerializeState threw: {ex.Message}");
                }
                tabs.Add(entry);
            }

            return new JsonObject
            {
                ["type"] = "leaf",
                ["tabs"] = tabs,
                ["activeTab"] = node.ActiveTabIndex,
            };
        }

        return new JsonObject
        {
            ["type"] = "split",
            ["direction"] = node.Direction.ToString(),
            ["ratio"] = node.SplitRatio,
            ["childA"] = SerializeNode(node.ChildA),
            ["childB"] = SerializeNode(node.ChildB),
        };
    }

    private static JsonArray SerializeFloating(List<FloatingWindow> windows)
    {
        var arr = new JsonArray();
        foreach (var fw in windows)
        {
            arr.Add(new JsonObject
            {
                ["node"] = SerializeNode(fw.Node),
                ["x"] = (float)fw.Position.X,
                ["y"] = (float)fw.Position.Y,
                ["w"] = (float)fw.Size.X,
                ["h"] = (float)fw.Size.Y,
            });
        }
        return arr;
    }

    private static DockNode? DeserializeNode(JsonNode? json)
    {
        if (json == null) return null;
        string type = json["type"]?.GetValue<string>() ?? "null";

        if (type == "null") return null;

        if (type == "leaf")
        {
            var tabs = new List<DockPanel>();
            if (json["tabs"] is JsonArray tabArr)
            {
                foreach (var tabNode in tabArr)
                {
                    // Supports both the current {type, state?} object form and the older
                    // bare-string form. Older layouts on disk still load cleanly.
                    string? typeName;
                    JsonObject? state = null;
                    if (tabNode is JsonObject obj)
                    {
                        typeName = obj["type"]?.GetValue<string>();
                        state = obj["state"] as JsonObject;
                    }
                    else
                    {
                        typeName = tabNode?.GetValue<string>();
                    }
                    if (typeName == null) continue;

                    var panelType = FindPanelType(typeName);
                    if (panelType == null)
                    {
                        Debug.LogWarning($"Panel type not found: {typeName}");
                        continue;
                    }

                    if (Activator.CreateInstance(panelType) is DockPanel panel)
                    {
                        if (state != null)
                        {
                            try { panel.RestoreState(state); }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"Panel '{panel.GetType().Name}' RestoreState threw: {ex.Message}");
                            }
                        }
                        tabs.Add(panel);
                    }
                }
            }

            if (tabs.Count == 0) return null;

            int activeTab = json["activeTab"]?.GetValue<int>() ?? 0;
            return new DockNode
            {
                Tabs = tabs,
                ActiveTabIndex = Math.Clamp(activeTab, 0, tabs.Count - 1),
            };
        }

        if (type == "split")
        {
            var dir = Enum.TryParse<SplitDirection>(json["direction"]?.GetValue<string>(), out var d)
                ? d : SplitDirection.Horizontal;
            float ratio = json["ratio"]?.GetValue<float>() ?? 0.5f;
            var childA = DeserializeNode(json["childA"]);
            var childB = DeserializeNode(json["childB"]);

            if (childA == null && childB == null) return null;
            if (childA == null) return childB;
            if (childB == null) return childA;

            return DockNode.Split(dir, ratio, childA, childB);
        }

        return null;
    }

    private static Type? FindPanelType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName);
            if (type != null && typeof(DockPanel).IsAssignableFrom(type))
                return type;
        }
        return null;
    }
}
