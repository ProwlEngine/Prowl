// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>
/// Serializes and deserializes dock layouts to/from JSON strings.
/// No file I/O or project dependencies - the host handles persistence.
/// Panel types are stored by full type name; the host provides a factory
/// delegate to reconstruct them on load.
/// </summary>
public static class DockSerializer
{
    /// <summary>Serialize the dock layout to a JSON string.</summary>
    public static string Serialize(DockSpace dockSpace)
    {
        var root = new JsonObject
        {
            ["root"] = SerializeNode(dockSpace.Root),
            ["floatingWindows"] = SerializeFloating(dockSpace.FloatingWindows),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Deserialize a JSON string into a dock layout.
    /// Populates floatingWindows and returns the root node.
    /// The panelFactory resolves type name strings to DockPanel instances.
    /// </summary>
    public static DockNode? Deserialize(string json, List<FloatingWindow> floatingWindows,
        Func<string, JsonObject?, DockPanel?> panelFactory)
    {
        var doc = JsonNode.Parse(json);
        if (doc == null) return null;

        var root = DeserializeNode(doc["root"], panelFactory);

        floatingWindows.Clear();
        if (doc["floatingWindows"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item == null) continue;
                var node = DeserializeNode(item["node"], panelFactory);
                if (node == null) continue;

                float x = item["x"]?.GetValue<float>() ?? 200;
                float y = item["y"]?.GetValue<float>() ?? 200;
                float w = item["w"]?.GetValue<float>() ?? 400;
                float h = item["h"]?.GetValue<float>() ?? 300;

                floatingWindows.Add(new FloatingWindow(node, new Float2(x, y), new Float2(w, h)));
            }
        }

        return root;
    }

    private static JsonObject SerializeNode(DockNode? node)
    {
        if (node == null) return new JsonObject { ["type"] = "null" };

        if (node.IsLeaf)
        {
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
                catch { }
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

    private static DockNode? DeserializeNode(JsonNode? json, Func<string, JsonObject?, DockPanel?> panelFactory)
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

                    var panel = panelFactory(typeName, state);
                    if (panel != null)
                        tabs.Add(panel);
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
            var childA = DeserializeNode(json["childA"], panelFactory);
            var childB = DeserializeNode(json["childB"], panelFactory);

            if (childA == null && childB == null) return null;
            if (childA == null) return childB;
            if (childB == null) return childA;

            return DockNode.Split(dir, ratio, childA, childB);
        }

        return null;
    }
}
