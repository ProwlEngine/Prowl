using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.Inspector;

/// <summary>
/// PropertyEditor for List&lt;T&gt; and T[] types.
/// Registered manually in PropertyEditorRegistry since it handles open generic types.
/// </summary>
public class CollectionPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var type = value?.GetType() ?? typeof(object[]);
        Type elementType = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
        IList? list = value as IList;
        int count = list?.Count ?? 0;

        EditorGUI.Foldout(paper, $"{id}_fold", $"{label} ({count})", () =>
        {
            if (list == null)
            {
                EditorGUI.Button(paper, $"{id}_create", $"Create {elementType.Name}[]")
                    .OnValueChanged(v =>
                    {
                        onChange(type.IsArray
                            ? Array.CreateInstance(elementType, 0)
                            : Activator.CreateInstance(type));
                    });
                return;
            }

            var colEl = paper.CurrentParent;
            var stableIds = paper.GetElementStorage<List<string>>(colEl, "stableIds", null!) ?? new List<string>();
            while (stableIds.Count < list.Count) stableIds.Add(Guid.NewGuid().ToString("N"));
            while (stableIds.Count > list.Count) stableIds.RemoveAt(stableIds.Count - 1);
            paper.SetElementStorage(colEl, "stableIds", stableIds);

            using (paper.Column($"{id}_items").Height(UnitValue.Auto).ChildLeft(16).ColBetween(6).Enter())
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    string stableKey = stableIds[i];

                    using (paper.Row($"{id}_item_{stableKey}").Height(UnitValue.Auto).RowBetween(4).Enter())
                    {
                        using (paper.Column($"{id}_itemval_{stableKey}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            PropertyGrid.DrawField(paper, $"{id}_el_{stableKey}", $"[{idx}]", elementType, list[i],
                                newVal => { list[idx] = newVal; onChange(list); }, depth + 1);
                        }

                        EditorGUI.ButtonSquare(paper, $"{id}_rm_{stableKey}", EditorIcons.Xmark)
                            .OnValueChanged(v =>
                            {
                                stableIds.RemoveAt(idx);
                                paper.SetElementStorage(colEl, "stableIds", stableIds);
                                if (type.IsArray)
                                {
                                    var newArr = Array.CreateInstance(elementType, list.Count - 1);
                                    for (int j = 0, k = 0; j < list.Count; j++)
                                        if (j != idx) newArr.SetValue(list[j], k++);
                                    onChange(newArr);
                                }
                                else
                                {
                                    var newList = (IList)Activator.CreateInstance(list.GetType())!;
                                    for (int j = 0; j < list.Count; j++)
                                        if (j != idx) newList.Add(list[j]);
                                    onChange(newList);
                                }
                            });
                    }
                }

                EditorGUI.Button(paper, $"{id}_add", "+ Add Element")
                    .OnValueChanged(v =>
                    {
                        object? newElement = elementType.IsValueType
                            ? Activator.CreateInstance(elementType)
                            : elementType == typeof(string) ? "" : null;
                        stableIds.Add(Guid.NewGuid().ToString("N"));
                        paper.SetElementStorage(colEl, "stableIds", stableIds);
                        if (type.IsArray)
                        {
                            var newArr = Array.CreateInstance(elementType, list.Count + 1);
                            for (int j = 0; j < list.Count; j++) newArr.SetValue(list[j], j);
                            newArr.SetValue(newElement, list.Count);
                            onChange(newArr);
                        }
                        else
                        {
                            var newList = (IList)Activator.CreateInstance(list.GetType())!;
                            for (int j = 0; j < list.Count; j++) newList.Add(list[j]);
                            newList.Add(newElement);
                            onChange(newList);
                        }
                    });
            }
        });
    }
}

/// <summary>
/// PropertyEditor for Dictionary&lt;K,V&gt; types.
/// Registered manually in PropertyEditorRegistry since it handles open generic types.
/// </summary>
public class DictionaryPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var type = value?.GetType() ?? typeof(Dictionary<string, object>);
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

            using (paper.Column($"{id}_entries").Height(UnitValue.Auto).ChildLeft(16).ColBetween(6).Enter())
            {
                var keys = new System.Collections.Generic.List<object>();
                foreach (var key in dict.Keys) keys.Add(key);

                for (int i = 0; i < keys.Count; i++)
                {
                    int idx = i;
                    var keyObj = keys[i];

                    using (paper.Row($"{id}_entry_{i}").Height(UnitValue.Auto).RowBetween(4).Enter())
                    {
                        EditorGUI.Label(paper, $"{id}_key_{i}", $"[{keyObj}]");

                        using (paper.Column($"{id}_val_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                        {
                            PropertyGrid.DrawField(paper, $"{id}_v_{i}", "", valType, dict[keyObj],
                                newVal => { dict[keyObj] = newVal; onChange(dict); }, depth + 1);
                        }

                        EditorGUI.ButtonSquare(paper, $"{id}_drm_{i}", EditorIcons.Xmark)
                            .OnValueChanged(v => { dict.Remove(keyObj); onChange(dict); });
                    }
                }

                EditorGUI.Separator(paper, $"{id}_sep");

                using (paper.Row($"{id}_addrow").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                {
                    var addRowEl = paper.CurrentParent;
                    string pendingKey = paper.GetElementStorage(addRowEl, "pendingKey", "");

                    EditorGUI.TextField(paper, $"{id}_newkey", "Key", pendingKey)
                        .OnValueChanged(v => paper.SetElementStorage(addRowEl, "pendingKey", v));

                    EditorGUI.Button(paper, $"{id}_addentry", "+ Add")
                        .OnValueChanged(v =>
                        {
                            string pk = paper.GetElementStorage(addRowEl, "pendingKey", "");
                            if (string.IsNullOrWhiteSpace(pk)) return;
                            try
                            {
                                object? typedKey = keyType == typeof(string) ? pk
                                    : Convert.ChangeType(pk, keyType, CultureInfo.InvariantCulture);
                                if (dict.Contains(typedKey!)) return;
                                object? newVal = valType.IsValueType ? Activator.CreateInstance(valType)
                                    : valType == typeof(string) ? "" : null;
                                dict.Add(typedKey!, newVal);
                                onChange(dict);
                                paper.SetElementStorage(addRowEl, "pendingKey", "");
                            }
                            catch { }
                        });
                }
            }
        });
    }
}
