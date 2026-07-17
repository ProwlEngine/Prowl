using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

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
                var s = EditorRegistries.GetSettings<PhysicsSettings>();
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
        Origami.Header(paper, "phys_h_grav", $"{EditorIcons.Atom}  Gravity").Underline().Show();

        EditorGUI.Row(paper, "phys_gravity", "Gravity", () =>
            Origami.Float3Field(paper, "phys_gravity_vf", new Float3(GravityX, GravityY, GravityZ),
                v => { GravityX = v.X; GravityY = v.Y; GravityZ = v.Z; EditorRegistries.SaveSettings(); }).Show());

        paper.Box("phys_sp1").Height(8);

        // Solver
        Origami.Header(paper, "phys_h_solver", "Solver").Underline().Show();

        EditorGUI.SettingsIntSlider(paper, "phys_solver_iter", "Solver Iterations", SolverIterations, 1, 32, v => SolverIterations = v);
        EditorGUI.SettingsIntSlider(paper, "phys_relax_iter", "Relaxation Iterations", RelaxIterations, 1, 16, v => RelaxIterations = v);
        EditorGUI.SettingsIntSlider(paper, "phys_substeps", "Sub-Steps", SubSteps, 1, 16, v => SubSteps = v);

        paper.Box("phys_sp2").Height(8);

        // Behavior
        Origami.Header(paper, "phys_h_behavior", "Behavior").Underline().Show();

        EditorGUI.SettingsCheckbox(paper, "phys_sleep", "Allow Sleep", AllowSleep, v => AllowSleep = v);
        EditorGUI.SettingsCheckbox(paper, "phys_mt", "Use Multithreading", UseMultithreading, v => UseMultithreading = v);
        EditorGUI.SettingsCheckbox(paper, "phys_sync", "Auto Sync Transforms", AutoSyncTransforms, v => AutoSyncTransforms = v);

        paper.Box("phys_sp_adv").Height(8);

        // Advanced
        Origami.Header(paper, "phys_h_adv", "Advanced").Underline().Show();

        EditorGUI.SettingsCheckbox(paper, "phys_determ", "Enhanced Determinism", EnhancedDeterminism, v => EnhancedDeterminism = v);
        EditorGUI.SettingsCheckbox(paper, "phys_thread", "Persistent Thread Model",
            ThreadModel == PhysicsThreadModel.Persistent,
            v => ThreadModel = v ? PhysicsThreadModel.Persistent : PhysicsThreadModel.Regular);
        EditorGUI.SettingsCheckbox(paper, "phys_auxcp", "Auxiliary Contact Points", EnableAuxiliaryContactPoints, v => EnableAuxiliaryContactPoints = v);
        EditorGUI.SettingsCheckbox(paper, "phys_persist", "Persistent Contact Manifold", PersistentContactManifold, v => PersistentContactManifold = v);
        EditorGUI.SettingsSliderField(paper, "phys_specrelax", "Speculative Relaxation Factor", SpeculativeRelaxationFactor, 0f, 1f, v => SpeculativeRelaxationFactor = v);

        paper.Box("phys_sp3").Height(8);

        // Collision Matrix
        Origami.Header(paper, "phys_h_coll", "Layer Collision Matrix").Underline().Show();

        DrawCollisionMatrix(paper, font, width);
    }

    private void DrawCollisionMatrix(Paper paper, Scribe.FontFile font, float width)
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
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

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
                            EditorRegistries.SaveSettings();
                        });
                }
            }
        }
    }
}
