using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Attribute to register a custom asset editor for a specific EngineObject type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomAssetEditorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomAssetEditorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Base class for custom asset editors shown in the inspector when an asset is selected.
/// </summary>
/// <remarks>
/// Editors edit live: a change takes effect on the instance immediately, so the scene reflects it while
/// you work. What is <i>not</i> immediate is writing to disk, and the difference between the two is
/// measured rather than tracked. An editor exposes its authored state through <see cref="CaptureState"/>
/// and the base diffs that against a baseline snapshot with <see cref="EchoObject.CreateDelta"/>; a delta
/// with any operations means unapplied changes. No editor has to remember to flag each field it touches,
/// and none can forget one.
/// </remarks>
public abstract class AssetImporterEditor
{
    /// <summary>Draw the asset editor UI.</summary>
    public abstract void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset);

    // ============================================================
    // Pending changes
    // ============================================================

    /// <summary>
    /// Each asset's state as last written to disk, keyed by GUID. Static because the inspector reuses one
    /// editor instance per asset type, so per-instance state would be a single slot that the next
    /// selection overwrites.
    /// </summary>
    private static readonly Dictionary<Guid, EchoObject> s_baselines = new();

    /// <summary>
    /// This editor's current authored state as Echo, or null when it has nothing that can be applied.
    /// For an importer this is the <c>.meta</c> settings compound; for a content asset it is the live
    /// object serialized.
    /// </summary>
    /// <remarks>
    /// Keep this cheap - it runs while the asset is selected, so it must not do real work. Serializing a
    /// <c>Texture2D</c> here, for instance, would read the entire image back off the GPU; the texture
    /// editor returns its import settings instead, which is what it actually authors.
    /// </remarks>
    protected virtual EchoObject? CaptureState(AssetEntry entry, EngineObject? asset) => null;

    /// <summary>Writes this editor's current state to disk and reimports. Returns false when nothing was
    /// written, so a failed write keeps its changes pending instead of being marked clean.</summary>
    protected virtual bool ApplyState(AssetEntry entry, EngineObject? asset) => false;

    /// <summary>Puts the editor and the live asset back to <paramref name="baseline"/>.</summary>
    protected virtual void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline) { }

    /// <summary>True when this editor holds edits that differ from what is on disk.</summary>
    public bool HasPendingChanges(AssetEntry entry, EngineObject? asset)
    {
        EchoObject? current = CaptureState(entry, asset);
        if (current is null) return false;

        if (!s_baselines.TryGetValue(entry.Guid, out EchoObject baseline))
        {
            // Nothing seen for this asset yet, so what it holds now is by definition unmodified.
            s_baselines[entry.Guid] = current.Clone();
            return false;
        }

        EchoObject delta = EchoObject.CreateDelta(baseline, current);
        return delta.TryGet("Operations", out EchoObject operations) && operations.List.Count > 0;
    }

    /// <summary>Writes the pending edits and marks the result clean.</summary>
    public void ApplyPendingChanges(AssetEntry entry, EngineObject? asset)
    {
        if (ApplyState(entry, asset))
            Rebaseline(entry, asset);
    }

    /// <summary>Drops the pending edits, restoring the asset to what is on disk.</summary>
    public void RevertPendingChanges(AssetEntry entry, EngineObject? asset)
    {
        if (s_baselines.TryGetValue(entry.Guid, out EchoObject baseline))
            RevertState(entry, asset, baseline.Clone());
    }

    /// <summary>
    /// Records the asset's current state as the clean baseline. Call after loading it from disk and after
    /// writing it back. The snapshot is cloned, because editors hand back the very object they go on to
    /// mutate - sharing it would leave the baseline tracking the edits and every delta empty.
    /// </summary>
    protected void Rebaseline(AssetEntry entry, EngineObject? asset)
    {
        EchoObject? current = CaptureState(entry, asset);
        if (current is null) s_baselines.Remove(entry.Guid);
        else s_baselines[entry.Guid] = current.Clone();
    }

    /// <summary>Forgets every baseline. GUIDs only mean anything within one project.</summary>
    internal static void ClearBaselines() => s_baselines.Clear();

    // ============================================================
    // Shared UI
    // ============================================================

    /// <summary>
    /// Draws the Apply / Revert pair, and nothing at all when there is nothing to apply. Every asset
    /// editor calls this so the choice reads the same wherever it appears, and matches the prompt raised
    /// when navigating away from an asset with unwritten changes.
    /// </summary>
    protected void DrawApplyRevertBar(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (Origami.IsReadOnly || !HasPendingChanges(entry, asset)) return;

        var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;

        using (paper.Row($"{id}_applybar").Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, m.SpacingLarge)
            .RowBetween(m.SpacingMedium).Enter())
        {
            // Pushes both buttons to the right.
            paper.Box($"{id}_applybar_spacer").Height(1).IsNotInteractable();

            paper.Box($"{id}_revert").Width(UnitValue.Auto).Height(30).Rounded(8).Padding(16, 16, 0, 0)
                .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .Hovered.BackgroundColor(EditorTheme.Neutral300).End()
                .Text(Prowl.Rosetta.Loc.Get("dialog.revert"), font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => RevertPendingChanges(entry, asset));

            paper.Box($"{id}_apply").Width(UnitValue.Auto).Height(30).Rounded(8).Padding(16, 16, 0, 0)
                .BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text($"{EditorIcons.FloppyDisk}  {Prowl.Rosetta.Loc.Get("dialog.apply")}", font)
                .TextColor(System.Drawing.Color.White).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => ApplyPendingChanges(entry, asset));
        }
    }
}
