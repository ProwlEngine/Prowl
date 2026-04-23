// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Runtime;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Decorate a static method <c>string Generate(string className)</c> to register it as a
/// template in the New Script dialog. The returned string is the C# source written to disk.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScriptTemplateAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    /// <summary>Sort order lower appears first. Built-ins use 0..100.</summary>
    public int Order { get; set; }

    public ScriptTemplateAttribute(string name, string description, string icon)
    {
        Name = name; Description = description; Icon = icon;
    }
}

/// <summary>A discovered script template descriptor + generator callback.</summary>
public sealed class ScriptTemplate
{
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public int Order { get; }
    public Func<string, string> Generate { get; }

    public ScriptTemplate(string name, string description, string icon, int order, Func<string, string> generate)
    {
        Name = name; Description = description; Icon = icon; Order = order; Generate = generate;
    }
}

/// <summary>
/// Discovers <see cref="ScriptTemplateAttribute"/>-tagged static methods across all loaded
/// assemblies. Results are sorted by <see cref="ScriptTemplateAttribute.Order"/> then Name.
/// </summary>
public static class ScriptTemplateRegistry
{
    private static readonly List<ScriptTemplate> _templates = new();
    private static bool _initialized;

    public static IReadOnlyList<ScriptTemplate> Templates => _templates;

    public static void Reinitialize() { _initialized = false; Initialize(); }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _templates.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<ScriptTemplateAttribute>();
                    if (attr == null) continue;

                    if (method.ReturnType != typeof(string)
                        || method.GetParameters() is not { Length: 1 } p
                        || p[0].ParameterType != typeof(string))
                    {
                        Debug.LogWarning($"ScriptTemplateRegistry: {type.Name}.{method.Name} must be 'string Generate(string className)'");
                        continue;
                    }

                    Func<string, string> del;
                    try { del = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), method); }
                    catch (Exception ex) { Debug.LogWarning($"ScriptTemplateRegistry: failed to bind {type.Name}.{method.Name}: {ex.Message}"); continue; }

                    _templates.Add(new ScriptTemplate(attr.Name, attr.Description, attr.Icon, attr.Order, del));
                }
            }
        }

        _templates.Sort((a, b) =>
        {
            int c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }
}
