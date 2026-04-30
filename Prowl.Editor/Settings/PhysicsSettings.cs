using System;

using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

[ProjectSettings("Physics", EditorIcons.Atom, order: 20)]
public class PhysicsSettings : ProjectSettingsBase
{
    // Gravity
    public float GravityX = 0f;
    public float GravityY = -9.81f;
    public float GravityZ = 0f;

    // Solver
    public int SolverIterations = 8;
    public int RelaxIterations = 4;
    public int SubSteps = 2;

    // Behavior
    public bool AllowSleep = true;
    public bool UseMultithreading = true;
    public bool AutoSyncTransforms = true;

    // Advanced
    public bool EnhancedDeterminism = false;
    public PhysicsThreadModel ThreadModel = PhysicsThreadModel.Regular;
    public bool EnableAuxiliaryContactPoints = true;
    public bool PersistentContactManifold = true;
    public float SpeculativeRelaxationFactor = 0.9f;

    // Collision matrix stored as 32 uints (bit rows)
    public uint[] CollisionMatrixRows = CreateDefaultCollisionMatrix();

    private static bool s_sceneHookRegistered;

    public override void Apply()
    {
        ApplyToScene(Runtime.Resources.Scene.Current);

        // Apply collision matrix (this is global, not per-scene)
        if (CollisionMatrixRows.Length == 32)
        {
            for (int i = 0; i < 32; i++)
                for (int j = 0; j < 32; j++)
                    CollisionMatrix.SetLayerCollision(i, j, (CollisionMatrixRows[i] & (1u << j)) != 0);
        }

        // Re-apply to any scene that loads after this point
        if (!s_sceneHookRegistered)
        {
            s_sceneHookRegistered = true;
            Runtime.Resources.Scene.OnSceneLoaded += () =>
            {
                var s = ProjectSettingsRegistry.Get<PhysicsSettings>();
                s.ApplyToScene(Runtime.Resources.Scene.Current);
            };
        }
    }

    private void ApplyToScene(Runtime.Resources.Scene scene)
    {
        if (scene == null) return;
        scene.Physics.Gravity = new Float3(GravityX, GravityY, GravityZ);
        scene.Physics.SolverIterations = SolverIterations;
        scene.Physics.RelaxIterations = RelaxIterations;
        scene.Physics.Substep = SubSteps;
        scene.Physics.AllowSleep = AllowSleep;
        scene.Physics.UseMultithreading = UseMultithreading;
        scene.Physics.AutoSyncTransforms = AutoSyncTransforms;
        scene.Physics.EnhancedDeterminism = EnhancedDeterminism;
        scene.Physics.ThreadModel = ThreadModel;
        scene.Physics.EnableAuxiliaryContactPoints = EnableAuxiliaryContactPoints;
        scene.Physics.PersistentContactManifold = PersistentContactManifold;
        scene.Physics.SpeculativeRelaxationFactor = SpeculativeRelaxationFactor;
    }

    public override void ResetToDefaults()
    {
        GravityX = 0; GravityY = -9.81f; GravityZ = 0;
        SolverIterations = 8;
        RelaxIterations = 4;
        SubSteps = 2;
        AllowSleep = true;
        UseMultithreading = true;
        AutoSyncTransforms = true;
        EnhancedDeterminism = false;
        ThreadModel = PhysicsThreadModel.Regular;
        EnableAuxiliaryContactPoints = true;
        PersistentContactManifold = true;
        SpeculativeRelaxationFactor = 0.9f;
        CollisionMatrixRows = CreateDefaultCollisionMatrix();
    }

    private static uint[] CreateDefaultCollisionMatrix()
    {
        var rows = new uint[32];
        for (int i = 0; i < 32; i++) rows[i] = uint.MaxValue; // all collide
        return rows;
    }

    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Gravity
        EditorGUI.Header(paper, "phys_h_grav", $"{EditorIcons.Atom}  Gravity");
        EditorGUI.Separator(paper, "phys_sep_grav");

        EditorGUI.Vector3Field(paper, "phys_gravity", "Gravity", new Float3(GravityX, GravityY, GravityZ))
            .OnValueChanged(v => { GravityX = v.X; GravityY = v.Y; GravityZ = v.Z; ProjectSettingsRegistry.SaveAll(); });

        paper.Box("phys_sp1").Height(8);

        // Solver
        EditorGUI.Header(paper, "phys_h_solver", "Solver");
        EditorGUI.Separator(paper, "phys_sep_solver");

        InspectorRow.Draw(paper, "phys_solver_iter", "Solver Iterations", () =>
            Origami.IntSlider(paper, "phys_solver_iter_v", SolverIterations,
                v => { SolverIterations = v; ProjectSettingsRegistry.SaveAll(); }, 1, 32).Show());

        InspectorRow.Draw(paper, "phys_relax_iter", "Relaxation Iterations", () =>
            Origami.IntSlider(paper, "phys_relax_iter_v", RelaxIterations,
                v => { RelaxIterations = v; ProjectSettingsRegistry.SaveAll(); }, 1, 16).Show());

        InspectorRow.Draw(paper, "phys_substeps", "Sub-Steps", () =>
            Origami.IntSlider(paper, "phys_substeps_v", SubSteps,
                v => { SubSteps = v; ProjectSettingsRegistry.SaveAll(); }, 1, 16).Show());

        paper.Box("phys_sp2").Height(8);

        // Behavior
        EditorGUI.Header(paper, "phys_h_behavior", "Behavior");
        EditorGUI.Separator(paper, "phys_sep_behavior");

        Origami.Checkbox(paper, "phys_sleep", AllowSleep,
                v => { AllowSleep = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Allow Sleep").Show();

        Origami.Checkbox(paper, "phys_mt", UseMultithreading,
                v => { UseMultithreading = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Use Multithreading").Show();

        Origami.Checkbox(paper, "phys_sync", AutoSyncTransforms,
                v => { AutoSyncTransforms = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Auto Sync Transforms").Show();

        paper.Box("phys_sp_adv").Height(8);

        // Advanced
        EditorGUI.Header(paper, "phys_h_adv", "Advanced");
        EditorGUI.Separator(paper, "phys_sep_adv");

        Origami.Checkbox(paper, "phys_determ", EnhancedDeterminism,
                v => { EnhancedDeterminism = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Enhanced Determinism").Show();

        Origami.Checkbox(paper, "phys_thread", ThreadModel == PhysicsThreadModel.Persistent,
                v => { ThreadModel = v ? PhysicsThreadModel.Persistent : PhysicsThreadModel.Regular; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Persistent Thread Model").Show();

        Origami.Checkbox(paper, "phys_auxcp", EnableAuxiliaryContactPoints,
                v => { EnableAuxiliaryContactPoints = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Auxiliary Contact Points").Show();

        Origami.Checkbox(paper, "phys_persist", PersistentContactManifold,
                v => { PersistentContactManifold = v; ProjectSettingsRegistry.SaveAll(); })
            .LabelRight("Persistent Contact Manifold").Show();

        InspectorRow.Draw(paper, "phys_specrelax", "Speculative Relaxation Factor", () =>
            Origami.Slider(paper, "phys_specrelax_v", SpeculativeRelaxationFactor,
                v => { SpeculativeRelaxationFactor = v; ProjectSettingsRegistry.SaveAll(); },
                0f, 1f).Format("F2").Show());

        paper.Box("phys_sp3").Height(8);

        // Collision Matrix
        EditorGUI.Header(paper, "phys_h_coll", "Layer Collision Matrix");
        EditorGUI.Separator(paper, "phys_sep_coll");

        DrawCollisionMatrix(paper, font, width);
    }

    private void DrawCollisionMatrix(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        // Only show layers that have names
        var layers = TagLayerManager.GetLayers();
        var activeIndices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < layers.Count && i < 32; i++)
            if (!string.IsNullOrEmpty(layers[i]))
                activeIndices.Add(i);

        if (activeIndices.Count == 0) return;

        float cellSize = 18f;
        float labelW = 100f;

        // Header row with rotated labels (just show abbreviated names)
        using (paper.Row("phys_cm_hdr").Height(cellSize).ChildLeft(labelW).RowBetween(1).Enter())
        {
            foreach (int j in activeIndices)
            {
                paper.Box($"phys_cmh_{j}")
                    .Size(cellSize, cellSize)
                    .Text(layers[j].Length > 2 ? layers[j][..2] : layers[j], font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(8f).Alignment(TextAlignment.MiddleCenter);
            }
        }

        // Matrix rows
        foreach (int i in activeIndices)
        {
            using (paper.Row($"phys_cmr_{i}").Height(cellSize).RowBetween(1).Enter())
            {
                paper.Box($"phys_cml_{i}")
                    .Width(labelW).Height(cellSize)
                    .Text(layers[i], font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleRight);

                foreach (int j in activeIndices)
                {
                    if (j < i)
                    {
                        // Below diagonal mirror, don't show
                        paper.Box($"phys_cmc_{i}_{j}").Size(cellSize, cellSize);
                        continue;
                    }

                    int ci = i, cj = j;
                    bool collides = CollisionMatrixRows.Length == 32 && (CollisionMatrixRows[i] & (1u << j)) != 0;

                    paper.Box($"phys_cmc_{i}_{j}")
                        .Size(cellSize, cellSize)
                        .BackgroundColor(collides ? Color.FromArgb(255, 60, 160, 60) : EditorTheme.Ink100)
                        .Rounded(2)
                        .Hovered.BackgroundColor(collides ? Color.FromArgb(255, 80, 180, 80) : EditorTheme.Ink200).End()
                        .OnClick((ci, cj), (pair, _) =>
                        {
                            bool current = (CollisionMatrixRows[pair.ci] & (1u << pair.cj)) != 0;
                            bool newVal = !current;
                            // Set symmetric
                            if (newVal)
                            {
                                CollisionMatrixRows[pair.ci] |= (1u << pair.cj);
                                CollisionMatrixRows[pair.cj] |= (1u << pair.ci);
                            }
                            else
                            {
                                CollisionMatrixRows[pair.ci] &= ~(1u << pair.cj);
                                CollisionMatrixRows[pair.cj] &= ~(1u << pair.ci);
                            }
                            ProjectSettingsRegistry.SaveAll();
                        });
                }
            }
        }
    }
}
