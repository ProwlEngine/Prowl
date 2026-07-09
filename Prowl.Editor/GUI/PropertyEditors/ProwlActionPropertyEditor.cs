// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.PropertyEditors;

/// <summary>
/// Inspector for <see cref="ProwlAction"/> - the persistent call list. Each row picks a scene
/// GameObject, chooses a method / settable property / field on it (or any of its components) via
/// reflection, and supplies one basic argument.
/// </summary>
[CustomPropertyEditor(typeof(ProwlAction))]
public class ProwlActionPropertyEditor : PropertyEditor
{
    // One invokable member surfaced in the function dropdown, tagged with the object that owns it.
    private readonly struct MemberOption
    {
        public readonly string Display;
        public readonly EngineObject Target;
        public readonly string Name;
        public readonly ProwlActionArgType ArgType;

        public MemberOption(string display, EngineObject target, string name, ProwlActionArgType argType)
        {
            Display = display; Target = target; Name = name; ArgType = argType;
        }
    }

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var action = value as ProwlAction;
        if (action == null)
        {
            action = new ProwlAction();
            onChange(action);
        }

        Prowl.Scribe.FontFile? font = EditorTheme.Font;
        if (font == null) return;
        Prowl.Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        Prowl.Scribe.FontFile mono = EditorTheme.FontMono ?? font;
        UnitValue ST = UnitValue.Stretch();

        List<ProwlCall> calls = action.Calls;
        string title = string.IsNullOrEmpty(label) ? "Actions" : label;

        // Outer card - mirrors the PropertyGrid collection editor: rounded bordered container with a
        // titled header, a padded body of per-element cards, and an accent "add" footer.
        using (paper.Column($"{id}_box").Width(ST).Height(UnitValue.Auto)
            .Rounded(8).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Clip().Enter())
        {
            using (paper.Row($"{id}_hdr").Width(ST).Height(28).RoundedTop(8).Padding(10, 10, 0, 0)
                .BackgroundColor(EditorTheme.Glass).Enter())
            {
                paper.Box($"{id}_hl").Width(ST).Height(28).IsNotInteractable()
                    .Text($"{title}   ({calls.Count})", semi).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
            }
            paper.Box($"{id}_hd").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            using (paper.Column($"{id}_body").Width(ST).Height(UnitValue.Auto).Padding(6, 6, 6, 6).ColBetween(6).Enter())
            {
                if (calls.Count == 0)
                    paper.Box($"{id}_empty").Width(ST).Height(24).IsNotInteractable()
                        .Text("No calls - add one below", font).TextColor(EditorTheme.InkFaint)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                for (int i = 0; i < calls.Count; i++)
                {
                    int idx = i;
                    DrawCallCard(paper, $"{id}_c{idx}", idx, calls[idx], mono, font,
                        changed: () => onChange(action),
                        remove: () => { calls.RemoveAt(idx); onChange(action); });
                }

                paper.Box($"{id}_add").Width(ST).Height(26).Rounded(6)
                    .Hovered.BackgroundColor(EditorTheme.Hover).End()
                    .Text("+ Add Call", semi).TextColor(EditorTheme.AccentText)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => { calls.Add(new ProwlCall()); onChange(action); });
            }
        }
    }

    private static void DrawCallCard(Paper paper, string id, int index, ProwlCall call,
        Prowl.Scribe.FontFile mono, Prowl.Scribe.FontFile font, Action changed, Action remove)
    {
        UnitValue ST = UnitValue.Stretch();

        // The stored Target is the exact object the call runs on (a GameObject or one of its
        // Components); the picker edits the owning GameObject, which we recover from either.
        GameObject? owner = call.Target as GameObject ?? (call.Target as MonoBehaviour)?.GameObject;

        using (paper.Column(id).Width(ST).Height(UnitValue.Auto)
            .Rounded(6).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .BackgroundColor(Color.FromArgb(8, 255, 255, 255)).Clip().Enter())
        {
            // Card header: index label + delete button.
            using (paper.Row($"{id}_ch").Width(ST).Height(26).Padding(8, 6, 0, 0).RowBetween(6).Enter())
            {
                paper.Box($"{id}_ct").Width(ST).Height(26).IsNotInteractable()
                    .Text($"Call {index}", mono).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                paper.Box($"{id}_cx").Width(18).Height(18).Rounded(4).Margin(0, 0, ST, ST)
                    .Hovered.BackgroundColor(Color.FromArgb(40, EditorTheme.Red400)).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => remove());
            }
            paper.Box($"{id}_cd").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // Card body: target, function, argument.
            using (paper.Column($"{id}_cb").Width(ST).Height(UnitValue.Auto).Padding(2, 2, 4, 4).Enter())
            {
                PropertyGridUtils.DrawField(paper, $"{id}_tgt", "Target", typeof(GameObject), owner, v =>
                {
                    call.Target = v as GameObject;
                    call.Member = "";
                    call.ArgType = ProwlActionArgType.None;
                    changed();
                }, 0);

                if (owner != null)
                {
                    List<MemberOption> members = BuildMembers(owner);
                    string[] names = members.Select(m => m.Display).ToArray();
                    int sel = members.FindIndex(m => ReferenceEquals(m.Target, call.Target) && m.Name == call.Member);

                    EditorGUI.Row(paper, $"{id}_fn", "Function", () =>
                        Origami.Dropdown(paper, $"{id}_fnv", sel, i =>
                        {
                            if (i < 0 || i >= members.Count) return;
                            MemberOption m = members[i];
                            call.Target = m.Target;
                            call.Member = m.Name;
                            call.ArgType = m.ArgType;
                            changed();
                        }, names).Placeholder("No Function").Searchable().Show());

                    DrawArg(paper, $"{id}_arg", call, changed);
                }
            }
        }
    }

    private static void DrawArg(Paper paper, string id, ProwlCall call, Action changed)
    {
        switch (call.ArgType)
        {
            case ProwlActionArgType.Bool:
                EditorGUI.Row(paper, id, "Value", () =>
                    Origami.Checkbox(paper, $"{id}_v", call.BoolArg, v => { call.BoolArg = v; changed(); }).Show());
                break;
            case ProwlActionArgType.Int:
                EditorGUI.Row(paper, id, "Value", () =>
                    Origami.NumericField<int>(paper, $"{id}_v", call.IntArg, v => { call.IntArg = v; changed(); }).Show());
                break;
            case ProwlActionArgType.Float:
                EditorGUI.Row(paper, id, "Value", () =>
                    Origami.NumericField<float>(paper, $"{id}_v", call.FloatArg, v => { call.FloatArg = v; changed(); }).Show());
                break;
            case ProwlActionArgType.String:
                EditorGUI.Row(paper, id, "Value", () =>
                    Origami.TextField(paper, $"{id}_v", call.StringArg, v => { call.StringArg = v; changed(); }).Show());
                break;
            case ProwlActionArgType.Object:
                PropertyGridUtils.DrawField(paper, $"{id}_v", "Value", typeof(EngineObject), call.ObjectArg,
                    v => { call.ObjectArg = v as EngineObject; changed(); }, 0);
                break;
        }
    }

    // ---- Reflection: build the function dropdown from the GameObject + each of its components ----

    private static List<MemberOption> BuildMembers(GameObject go)
    {
        var list = new List<MemberOption>();
        AddMembers(list, go, "GameObject", go);
        foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
        {
            if (comp == null) continue;
            AddMembers(list, comp, comp.GetType().Name, comp);
        }
        return list;
    }

    private static void AddMembers(List<MemberOption> list, EngineObject target, string group, object host)
    {
        Type t = host.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        // Action-style methods only: void return, no property accessors/operators/generics, and either
        // no parameter or a single simple one. A method that returns a value is a query, not an action,
        // so it's hidden.
        foreach (MethodInfo m in t.GetMethods(flags))
        {
            if (m.IsSpecialName || m.IsGenericMethodDefinition || m.ContainsGenericParameters) continue;
            if (m.DeclaringType == typeof(object)) continue;
            if (m.ReturnType != typeof(void)) continue;

            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length == 0)
                list.Add(new MemberOption($"{group}/{m.Name} ()", target, m.Name, ProwlActionArgType.None));
            else if (ps.Length == 1 && ProwlActionArg.TryFromType(ps[0].ParameterType, out ProwlActionArgType at))
                list.Add(new MemberOption($"{group}/{m.Name} ({ProwlActionArg.Label(at)})", target, m.Name, at));
        }

        // Settable properties of a supported type.
        foreach (PropertyInfo p in t.GetProperties(flags))
        {
            if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            if (ProwlActionArg.TryFromType(p.PropertyType, out ProwlActionArgType at) && at != ProwlActionArgType.None)
                list.Add(new MemberOption($"{group}/{p.Name} = ({ProwlActionArg.Label(at)})", target, p.Name, at));
        }

        // Public fields of a supported type.
        foreach (FieldInfo f in t.GetFields(flags))
        {
            if (ProwlActionArg.TryFromType(f.FieldType, out ProwlActionArgType at) && at != ProwlActionArgType.None)
                list.Add(new MemberOption($"{group}/{f.Name} = ({ProwlActionArg.Label(at)})", target, f.Name, at));
        }
    }
}
