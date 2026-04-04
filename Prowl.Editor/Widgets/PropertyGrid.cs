using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Draws a property grid for any object using reflection.
/// Discovers serializable fields (matching Echo's rules) and draws appropriate editors.
/// </summary>
public static class PropertyGrid
{
    private static readonly Dictionary<Type, Func<Paper, string, string, object, Action<object>, object>> _customDrawers = new();

    /// <summary>
    /// Register a custom drawer for a specific type.
    /// </summary>
    public static void RegisterDrawer<T>(Func<Paper, string, string, object, Action<object>, object> drawer)
        => _customDrawers[typeof(T)] = drawer;

    /// <summary>
    /// Draw the property grid for an object. Returns true if any value changed.
    /// </summary>
    public static void Draw(Paper paper, string id, object target, Action<object>? onChanged = null, int depth = 0)
    {
        if (target == null) return;
        if (depth > 10) { EditorGUI.Label(paper, $"{id}_deep", "(max depth)", EditorTheme.TextDim); return; }

        var fields = GetSerializableFields(target.GetType());

        for (int i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            string fieldId = $"{id}_{field.Name}_{i}";
            string label = NicifyName(field.Name);
            object? value = field.GetValue(target);
            Type fieldType = field.FieldType;

            DrawField(paper, fieldId, label, fieldType, value, newVal =>
            {
                field.SetValue(target, newVal);
                onChanged?.Invoke(target);
            }, depth);
        }
    }

    /// <summary>
    /// Draw a single field with the appropriate editor.
    /// </summary>
    public static void DrawField(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth = 0)
    {
        // Check custom drawers first
        if (_customDrawers.TryGetValue(type, out var customDrawer))
        {
            customDrawer(paper, id, label, value!, v => onChange(v));
            return;
        }

        // Primitives
        if (type == typeof(bool))
        {
            EditorGUI.Toggle(paper, id, label, (bool)(value ?? false))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(int))
        {
            EditorGUI.IntField(paper, id, label, (int)(value ?? 0))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(float))
        {
            EditorGUI.FloatField(paper, id, label, (float)(value ?? 0f))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(double))
        {
            EditorGUI.FloatField(paper, id, label, (float)(double)(value ?? 0.0))
                .OnValueChanged(v => onChange((double)v));
            return;
        }
        if (type == typeof(string))
        {
            EditorGUI.TextField(paper, id, label, (string)(value ?? ""))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(byte))
        {
            EditorGUI.IntSlider(paper, id, label, (int)(byte)(value ?? (byte)0), 0, 255)
                .OnValueChanged(v => onChange((byte)v));
            return;
        }
        if (type == typeof(short))
        {
            EditorGUI.IntField(paper, id, label, (int)(short)(value ?? (short)0))
                .OnValueChanged(v => onChange((short)Math.Clamp(v, short.MinValue, short.MaxValue)));
            return;
        }
        if (type == typeof(ushort))
        {
            EditorGUI.IntField(paper, id, label, (int)(ushort)(value ?? (ushort)0))
                .OnValueChanged(v => onChange((ushort)Math.Clamp(v, ushort.MinValue, ushort.MaxValue)));
            return;
        }
        if (type == typeof(sbyte))
        {
            EditorGUI.IntField(paper, id, label, (int)(sbyte)(value ?? (sbyte)0))
                .OnValueChanged(v => onChange((sbyte)Math.Clamp(v, sbyte.MinValue, sbyte.MaxValue)));
            return;
        }
        if (type == typeof(long))
        {
            EditorGUI.IntField(paper, id, label, (int)Math.Clamp((long)(value ?? 0L), int.MinValue, int.MaxValue))
                .OnValueChanged(v => onChange((long)v));
            return;
        }
        if (type == typeof(uint))
        {
            EditorGUI.IntField(paper, id, label, (int)Math.Min((uint)(value ?? 0u), int.MaxValue))
                .OnValueChanged(v => onChange((uint)Math.Max(v, 0)));
            return;
        }
        if (type == typeof(ulong))
        {
            EditorGUI.IntField(paper, id, label, (int)Math.Min((ulong)(value ?? 0UL), (ulong)int.MaxValue))
                .OnValueChanged(v => onChange((ulong)Math.Max(v, 0)));
            return;
        }

        // Math types
        if (type == typeof(Float2))
        {
            EditorGUI.Vector2Field(paper, id, label, (Float2)(value ?? Float2.Zero))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(Float3))
        {
            EditorGUI.Vector3Field(paper, id, label, (Float3)(value ?? Float3.Zero))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(Float4))
        {
            // Draw as 4 floats
            var val = (Float4)(value ?? Float4.Zero);
            using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
            {
                if (EditorTheme.DefaultFont != null)
                    paper.Box($"{id}_lbl").Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight).ChildLeft(4).IsNotInteractable()
                        .Text(label, EditorTheme.DefaultFont).TextColor(EditorTheme.Text).FontSize(EditorTheme.FontSize);
                EditorGUI.FloatField(paper, $"{id}_x", "X", (float)val.X).OnValueChanged(v => onChange(new Float4(v, val.Y, val.Z, val.W)));
                EditorGUI.FloatField(paper, $"{id}_y", "Y", (float)val.Y).OnValueChanged(v => onChange(new Float4(val.X, v, val.Z, val.W)));
                EditorGUI.FloatField(paper, $"{id}_z", "Z", (float)val.Z).OnValueChanged(v => onChange(new Float4(val.X, val.Y, v, val.W)));
                EditorGUI.FloatField(paper, $"{id}_w", "W", (float)val.W).OnValueChanged(v => onChange(new Float4(val.X, val.Y, val.Z, v)));
            }
            return;
        }
        if (type == typeof(Prowl.Vector.Color))
        {
            EditorGUI.ColorField(paper, id, label, (Prowl.Vector.Color)(value ?? new Prowl.Vector.Color(1, 1, 1, 1)))
                .OnValueChanged(v => onChange(v));
            return;
        }
        if (type == typeof(Quaternion))
        {
            var q = (Quaternion)(value ?? Quaternion.Identity);
            var euler = q.EulerAngles;
            EditorGUI.Vector3Field(paper, id, label, euler)
                .OnValueChanged(v => onChange(Quaternion.FromEuler(v)));
            return;
        }

        // AnimationCurve
        if (type == typeof(AnimationCurve))
        {
            CurveEditor.CurveField(paper, id, label, (AnimationCurve)(value ?? new AnimationCurve()))
                .OnValueChanged(v => onChange(v));
            return;
        }

        // Enums
        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            int selectedIdx = value != null ? Array.IndexOf(values, value) : 0;

            EditorGUI.Dropdown(paper, id, label, selectedIdx, names)
                .OnValueChanged(idx => { if (idx >= 0 && idx < values.Length) onChange(values.GetValue(idx)); });
            return;
        }

        // Guid (read-only)
        if (type == typeof(Guid))
        {
            EditorGUI.Label(paper, id, $"{label}: {value}");
            return;
        }

        // Lists and arrays
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
        {
            DrawCollection(paper, id, label, type, value, onChange, depth);
            return;
        }

        // Dictionary
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            DrawDictionary(paper, id, label, type, value, onChange, depth);
            return;
        }

        // Nested object (class or struct with serializable fields)
        if ((type.IsClass || type.IsValueType) && !type.IsPrimitive)
        {
            DrawNestedObject(paper, id, label, type, value, onChange, depth);
            return;
        }

        // Fallback
        EditorGUI.Label(paper, id, $"{label}: {value ?? "(null)"}", EditorTheme.TextDim);
    }

    static void DrawCollection(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth)
    {
        Type elementType = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
        IList? list = value as IList;
        int count = list?.Count ?? 0;

        EditorGUI.Foldout(paper, $"{id}_fold", $"{label} ({count})", () =>
        {
            if (list == null)
            {
                using (paper.Row($"{id}_null").Height(EditorTheme.RowHeight).ChildLeft(16).Enter())
                {
                    EditorGUI.Button(paper, $"{id}_create", $"Create {elementType.Name}[]")
                        .OnValueChanged(v =>
                        {
                            if (type.IsArray)
                                onChange(Array.CreateInstance(elementType, 0));
                            else
                                onChange(Activator.CreateInstance(type));
                        });
                }
                return;
            }

            using (paper.Column($"{id}_items").Height(UnitValue.Auto).ChildLeft(16).RowBetween(2).Enter())
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    using (paper.Row($"{id}_item_{i}").Height(UnitValue.Auto).RowBetween(4).Enter())
                    {
                        using (paper.Column($"{id}_itemval_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).RowBetween(2).Enter())
                        {
                            DrawField(paper, $"{id}_el_{i}", $"[{i}]", elementType, list[i],
                                newVal =>
                                {
                                    list[idx] = newVal;
                                    onChange(list);
                                }, depth + 1);
                        }

                        EditorGUI.ButtonSquare(paper, $"{id}_rm_{i}", "\u2715")
                            .OnValueChanged(v =>
                            {
                                if (type.IsArray)
                                {
                                    var newArr = Array.CreateInstance(elementType, list.Count - 1);
                                    for (int j = 0, k = 0; j < list.Count; j++)
                                        if (j != idx) newArr.SetValue(list[j], k++);
                                    onChange(newArr);
                                }
                                else
                                {
                                    list.RemoveAt(idx);
                                    onChange(list);
                                }
                            });
                    }
                }

                EditorGUI.Button(paper, $"{id}_add", "+ Add Element")
                    .OnValueChanged(v =>
                    {
                        object? newElement = elementType.IsValueType ? Activator.CreateInstance(elementType) :
                                              elementType == typeof(string) ? "" : null;
                        if (type.IsArray)
                        {
                            var newArr = Array.CreateInstance(elementType, list.Count + 1);
                            for (int j = 0; j < list.Count; j++) newArr.SetValue(list[j], j);
                            newArr.SetValue(newElement, list.Count);
                            onChange(newArr);
                        }
                        else
                        {
                            list.Add(newElement);
                            onChange(list);
                        }
                    });
            }
        });
    }

    static void DrawDictionary(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth)
    {
        var args = type.GetGenericArguments();
        Type keyType = args[0], valType = args[1];
        IDictionary? dict = value as IDictionary;
        int count = dict?.Count ?? 0;

        EditorGUI.Foldout(paper, $"{id}_fold", $"{label} ({count} entries)", () =>
        {
            if (dict == null)
            {
                EditorGUI.Button(paper, $"{id}_create", "Create Dictionary")
                    .OnValueChanged(v => onChange(Activator.CreateInstance(type)));
                return;
            }

            using (paper.Column($"{id}_entries").Height(UnitValue.Auto).ChildLeft(16).RowBetween(2).Enter())
            {
                var keys = new List<object>();
                foreach (var key in dict.Keys) keys.Add(key);

                for (int i = 0; i < keys.Count; i++)
                {
                    int idx = i;
                    var keyObj = keys[i];

                    using (paper.Row($"{id}_entry_{i}").Height(UnitValue.Auto).RowBetween(4).Enter())
                    {
                        EditorGUI.Label(paper, $"{id}_key_{i}", $"[{keyObj}]");

                        using (paper.Column($"{id}_val_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).RowBetween(2).Enter())
                        {
                            DrawField(paper, $"{id}_v_{i}", "", valType, dict[keyObj],
                                newVal =>
                                {
                                    dict[keyObj] = newVal;
                                    onChange(dict);
                                }, depth + 1);
                        }

                        EditorGUI.ButtonSquare(paper, $"{id}_drm_{i}", "\u2715")
                            .OnValueChanged(v =>
                            {
                                dict.Remove(keyObj);
                                onChange(dict);
                            });
                    }
                }
            }
        });
    }


    // ================================================================
    //  Nested Object
    // ================================================================
    static void DrawNestedObject(Paper paper, string id, string label, Type type, object? value,
        Action<object?> onChange, int depth)
    {
        if (value == null)
        {
            using (paper.Row($"{id}_null").Height(EditorTheme.RowHeight).RowBetween(6).Enter())
            {
                EditorGUI.Label(paper, $"{id}_lbl", $"{label}: (null)");

                if (!type.IsAbstract && !type.IsInterface)
                {
                    EditorGUI.Button(paper, $"{id}_create", "Create")
                        .OnValueChanged(v => onChange(Activator.CreateInstance(type)));
                }
                else
                {
                    DrawTypePicker(paper, $"{id}_pick", type, onChange);
                }
            }
            return;
        }

        var fields = GetSerializableFields(type);
        if (fields.Length == 0)
        {
            EditorGUI.Label(paper, id, $"{label}: {value}");
            return;
        }

        EditorGUI.Foldout(paper, $"{id}_fold", $"{label} ({type.Name})", () =>
        {
            using (paper.Column($"{id}_nested").Height(UnitValue.Auto).ChildLeft(12).RowBetween(2).Enter())
            {
                Draw(paper, $"{id}_props", value, changed =>
                {
                    if (type.IsValueType)
                        onChange(changed);
                    else
                        onChange?.Invoke(value);
                }, depth + 1);
            }
        });
    }

    // ================================================================
    //  Type Picker (for polymorphism)
    // ================================================================

    static void DrawTypePicker(Paper paper, string id, Type baseType, Action<object?> onChange)
    {
        // Find all concrete types that derive from baseType
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Take(20) // Limit for performance
            .ToArray();

        if (types.Length == 0)
        {
            EditorGUI.Label(paper, $"{id}_none", "(no implementations found)", EditorTheme.TextDim);
            return;
        }

        var names = types.Select(t => t.Name).ToArray();
        EditorGUI.Dropdown(paper, $"{id}_dd", "Type", 0, names)
            .OnValueChanged(idx =>
            {
                if (idx >= 0 && idx < types.Length)
                    onChange(Activator.CreateInstance(types[idx]));
            });
    }

    // ================================================================
    //  Reflection Helpers
    // ================================================================

    static FieldInfo[] GetSerializableFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var fields = new List<FieldInfo>();
        Type? current = type;

        while (current != null && current != typeof(object))
        {
            foreach (var field in current.GetFields(flags))
            {
                bool shouldSerialize = field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null;
                if (!shouldSerialize) continue;
                bool shouldIgnore = field.GetCustomAttribute<SerializeIgnoreAttribute>() != null ||
                                    field.GetCustomAttribute<NonSerializedAttribute>() != null;
                if (shouldIgnore) continue;
                fields.Add(field);
            }
            current = current.BaseType;
        }

        return fields.ToArray();
    }

    static string NicifyName(string name)
    {
        // Remove common prefixes
        if (name.StartsWith("_")) name = name.Substring(1);
        if (name.StartsWith("m_")) name = name.Substring(2);

        // Insert spaces before capitals (camelCase → Camel Case)
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                result.Append(' ');
            result.Append(i == 0 ? char.ToUpper(c) : c);
        }
        return result.ToString();
    }
}
