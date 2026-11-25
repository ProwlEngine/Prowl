// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Importers;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

public class AnimationPropertyConsistencyTests
{
    private sealed class DummyComponent : Component
    {
        public float Intensity = 2f;
    }

    [Fact]
    public void EffectiveWrapMode_ReconcilesLoopFlagAndWrapMode()
    {
        var clip = new AnimationClip
        {
            Loop = false,
            WrapMode = SceneClipWrapMode.Loop,
        };
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 0f, 0f);
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 10f);

        Assert.Equal(SceneClipWrapMode.Once, clip.EffectiveWrapMode);
        Assert.False(clip.EffectiveLoop);
        // WrapTime clamps at duration (1.0s) instead of wrapping 1.5s -> 0.5s
        Assert.Equal(1f, clip.WrapTime(1.5f), 3);

        clip.Loop = true;
        clip.WrapMode = SceneClipWrapMode.PingPong;
        Assert.Equal(SceneClipWrapMode.PingPong, clip.EffectiveWrapMode);
        Assert.True(clip.EffectiveLoop);
        Assert.Equal(0.5f, clip.WrapTime(1.5f), 3);
    }

    [Fact]
    public void SetCurveKey_And_ReduceKeyframes_PreserveTangentsAndStepCurves()
    {
        var clip = new AnimationClip();
        SceneCurveTrack bezier = clip.GetOrCreateCurveTrack("", "Transform", "LocalPosition.X");
        bezier.Curve.AddKey(new Keyframe(0f, 0f, 5f, 5f, CurveInterpolation.CubicSpline));
        bezier.Curve.AddKey(new Keyframe(2f, 10f, -4f, -4f, CurveInterpolation.CubicSpline));

        // Inserting a middle key must not wipe out custom tangents on existing keys
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 5f);
        Assert.Equal(3, bezier.Curve.Count);
        Assert.Equal(5f, bezier.Curve[0].OutTangent, 3);
        Assert.Equal(-4f, bezier.Curve[2].InTangent, 3);

        // Step curves must not be simplified away by ReduceKeyframes
        SceneCurveTrack step = clip.GetOrCreateCurveTrack("", "GameObject", "Enabled");
        step.Curve.AddKey(new Keyframe(0f, 0f, 0f, 0f, CurveInterpolation.Step));
        step.Curve.AddKey(new Keyframe(0.5f, 1f, 0f, 0f, CurveInterpolation.Step));
        step.Curve.AddKey(new Keyframe(1f, 0f, 0f, 0f, CurveInterpolation.Step));

        clip.ReduceKeyframes(0.5f);
        Assert.Equal(3, step.Curve.Count);
    }

    [Fact]
    public void AnimationClip_EchoRoundTrip_PreservesTrackFlagsAndRangedEvents()
    {
        var clip = new AnimationClip
        {
            Name = "RoundTripClip",
            FrameRate = 30f,
            WrapMode = SceneClipWrapMode.PingPong,
            Loop = true,
            CompletionMode = SceneClipCompletionMode.RestoreState,
        };
        SceneCurveTrack ct = clip.SetCurveKey("Child", "Transform", "LocalPosition.Y", 0f, 1f);
        ct.BlendMode = SceneTrackBlendMode.Additive;
        ct.Muted = true;
        ct.Locked = true;

        clip.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = 0.2f,
            EndTime = 0.8f,
            FunctionName = "OnWindowEnter",
            StayFunctionName = "OnWindowStay",
            ExitFunctionName = "OnWindowExit",
        });

        EchoObject serialized = Serializer.Serialize(clip);
        var loaded = Serializer.Deserialize<AnimationClip>(serialized)!;

        Assert.Equal(30f, loaded.FrameRate);
        Assert.Equal(SceneClipWrapMode.PingPong, loaded.WrapMode);
        Assert.Equal(SceneClipCompletionMode.RestoreState, loaded.CompletionMode);
        Assert.Single(loaded.CurveTracks);
        Assert.Equal(SceneTrackBlendMode.Additive, loaded.CurveTracks[0].BlendMode);
        Assert.True(loaded.CurveTracks[0].Muted);
        Assert.True(loaded.CurveTracks[0].Locked);
        Assert.Single(loaded.PropertyEvents);
        Assert.True(loaded.PropertyEvents[0].IsRange);
        Assert.Equal("OnWindowStay", loaded.PropertyEvents[0].StayFunctionName);
        Assert.Equal("OnWindowExit", loaded.PropertyEvents[0].ExitFunctionName);
    }

    [Fact]
    public void AnimationGraph_Blend1D_ZeroWeightClipDoesNotOverrideActiveClipProperties()
    {
        EditorRegistries.Initialize();

        using var scene = new Scene(SceneMode.Game);
        var go = new GameObject("Actor");
        DummyComponent comp = go.AddComponent<DummyComponent>();
        Animator animator = go.AddComponent<Animator>();
        scene.Add(go);
        scene.Activate();

        var clipLow = new AnimationClip { Name = "Low" };
        clipLow.SetCurveKey("", nameof(DummyComponent), nameof(DummyComponent.Intensity), 0f, 10f);
        clipLow.SetCurveKey("", nameof(DummyComponent), nameof(DummyComponent.Intensity), 1f, 10f);

        var clipHigh = new AnimationClip { Name = "High" };
        clipHigh.SetCurveKey("", nameof(DummyComponent), nameof(DummyComponent.Intensity), 0f, 100f);
        clipHigh.SetCurveKey("", nameof(DummyComponent), nameof(DummyComponent.Intensity), 1f, 100f);

        var graph = new AnimationGraph { Name = "BlendGraph" };
        graph.Parameters.Add(new GraphParameterRecord { Name = "Blend", Kind = NodeValueKind.Number, Number = 0f });

        var paramRec = new GraphNodeRecord { Id = "param", Type = AnimationNodeIds.Parameter };
        paramRec.Properties["Name"] = NodeValue.FromText("Blend");

        var lowRec = new GraphNodeRecord { Id = "low", Type = AnimationNodeIds.Clip };
        lowRec.Properties["Clip"] = NodeValue.FromClip(clipLow);

        var highRec = new GraphNodeRecord { Id = "high", Type = AnimationNodeIds.Clip };
        highRec.Properties["Clip"] = NodeValue.FromClip(clipHigh);

        var blendRec = new GraphNodeRecord { Id = "blend", Type = AnimationNodeIds.Blend1D };
        blendRec.Inputs.Add(new GraphInputRecord("param"));
        blendRec.Inputs.Add(new GraphInputRecord("low", 0f));
        blendRec.Inputs.Add(new GraphInputRecord("high", 1f));

        graph.Nodes.AddRange([paramRec, lowRec, highRec, blendRec]);
        graph.RootNode = "blend";

        animator.Graph = graph;

        // At Blend = 0, clipLow has weight 1.0 (Intensity = 10) and clipHigh has weight 0.0 (must NOT apply 100!)
        animator.SetFloat("Blend", 0f);
        scene.Animation.Update(0.1f);
        Assert.Equal(10f, comp.Intensity, 2);

        // At Blend = 1, clipHigh has weight 1.0 (Intensity = 100) and clipLow has weight 0.0
        animator.SetFloat("Blend", 1f);
        scene.Animation.Update(0.1f);
        Assert.Equal(100f, comp.Intensity, 2);
    }

    [Fact]
    public void RestartingActiveAdditiveClip_PreservesOriginalBaseline()
    {
        using var scene = new Scene(SceneMode.Game);
        var go = new GameObject("Actor");
        go.Transform.LocalPosition = new Float3(10f, 0f, 0f);
        Animator animator = go.AddComponent<Animator>();

        var clip = new AnimationClip
        {
            Name = "AdditiveLoop",
            WrapMode = SceneClipWrapMode.Loop,
            CompletionMode = SceneClipCompletionMode.RestoreState,
        };
        SceneCurveTrack posX = clip.SetCurveKey("", "Transform", "LocalPosition.X", 0f, 0f);
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 6f);
        posX.BlendMode = SceneTrackBlendMode.Additive;

        animator.Clips.Add(clip);
        scene.Add(go);
        scene.Activate();

        scene.Animation.Update(0.5f);
        Assert.InRange(go.Transform.LocalPosition.X, 12.5f, 13.5f);

        // Restarting mid-playback must not capture 13 as the new baseline
        animator.Play(clip);
        animator.Stop();
        Assert.Equal(10f, go.Transform.LocalPosition.X, 2);
    }

    [Fact]
    public void AnimationPanel_RegistersConflictFreeShortcutsInEditorPreferences()
    {
        EditorRegistries.Initialize();

        ShortcutDefinition[] animShortcuts = ShortcutManager.GetAllShortcuts()
            .Where(s => s.Category == "Animation")
            .ToArray();

        Assert.Equal(20, animShortcuts.Length);
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutPlayPause));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutKeyAtPlayhead));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutDelete));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutCopy));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutPaste));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutDuplicate));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutSelectAll));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutFrameSelection));
        Assert.NotNull(ShortcutManager.GetBinding(AnimationPanel.ShortcutToggleCurvesView));

        foreach (ShortcutDefinition def in animShortcuts)
        {
            Assert.True(def.Default.IsBound, $"Shortcut '{def.Id}' should have a default key binding.");
            Assert.Empty(ShortcutManager.FindConflicts(def, def.Default));
        }
    }

    [Fact]
    public void SelectingGameObjectWithoutAnimatorInParentHierarchy_EntersCreateModeAndCreatesAnimatorWithClip()
    {
        EditorRegistries.Initialize();
        using var scene = new Scene(SceneMode.Edit);

        var riggedRoot = new GameObject("RiggedRoot");
        Animator existingAnimator = riggedRoot.AddComponent<Animator>();
        var existingClip = new AnimationClip { Name = "Idle" };
        existingAnimator.AddClip(existingClip);
        var riggedChild = new GameObject("RiggedChild");
        riggedChild.SetParent(riggedRoot, false);

        var plainRoot = new GameObject("PlainRoot");
        var plainChild = new GameObject("PlainChild");
        plainChild.SetParent(plainRoot, false);

        scene.Add(riggedRoot);
        scene.Add(plainRoot);
        scene.Activate();

        var panel = new AnimationPanel();
        try
        {
            // 1. Selecting a child under a parent Animator binds to that parent Animator and its clip
            Selection.Select(riggedChild);
            panel.SyncTargetFromSelection();
            Assert.False(panel.NeedsAnimatorCreation);
            Assert.Same(existingAnimator, panel.BoundAnimator);
            Assert.Same(existingClip, panel.ActiveClip);

            // 2. Selecting a GameObject with NO Animator in its parent hierarchy clears the clip and shows Create state
            Selection.Select(plainChild);
            panel.SyncTargetFromSelection();
            Assert.True(panel.NeedsAnimatorCreation);
            Assert.Null(panel.BoundAnimator);
            Assert.Null(panel.ActiveClip);
            Assert.Same(plainChild, panel.TargetRoot);

            // 3. Clicking Create creates the clip, adds an Animator to plainChild, and assigns the clip to it
            AnimationClip created = panel.CreateClipForTargetAtPath("Animations/Bounce.animclip", plainChild);
            Animator? addedAnimator = plainChild.GetComponent<Animator>();
            Assert.NotNull(addedAnimator);
            Assert.True(addedAnimator!.IsValid());
            Assert.Contains(created, addedAnimator.Clips);
            Assert.Equal("Bounce", created.Name);
            Assert.False(panel.NeedsAnimatorCreation);
            Assert.Same(created, panel.ActiveClip);
        }
        finally
        {
            Selection.Clear();
            panel.OnClosed();
        }
    }

    [Fact]
    public void PlayModeRecording_RecordsLiveChangesAndSavesWhenSaveClicked()
    {
        EditorRegistries.Initialize();
        bool prevPlaying = Application.IsPlaying;
        var panel = new AnimationPanel();

        try
        {
            using var playScene = new Scene(SceneMode.Game);
            var go = new GameObject("PlayActor");
            go.Transform.LocalPosition = new Float3(1f, 2f, 3f);
            Animator animator = go.AddComponent<Animator>();
            var clip = new AnimationClip { Name = "PlayRecordedClip" };
            animator.AddClip(clip);
            playScene.Add(go);
            playScene.Activate();

            Application.IsPlaying = true;
            animator.Pause();

            panel.BindAnimator(animator);
            panel.ToggleRecording();
            Assert.True(panel.IsRecording);

            // Move the GameObject while recording in Play Mode
            go.Transform.LocalPosition = new Float3(12f, 2f, 3f);
            panel.PollAutoKeyChanges();

            SceneCurveTrack? recordedTrack = clip.FindCurveTrack("", "Transform", "LocalPosition.X");
            Assert.NotNull(recordedTrack);
            Assert.Equal(12f, recordedTrack!.Curve.Evaluate(0f), 2);
            Assert.True(panel.HasUnsavedClipChanges);

            // Clicking Save clears unsaved changes state on the clip
            panel.SaveCurrentClip();
            Assert.False(panel.HasUnsavedClipChanges);
        }
        finally
        {
            Application.IsPlaying = prevPlaying;
            Selection.Clear();
            panel.OnClosed();
        }
    }

    [Fact]
    public void AnimationEventSetupHelpers_DiscoverComponentMethodsValidateSignaturesAndSelectEvents()
    {
        EditorRegistries.Initialize();
        using var scene = new Scene(SceneMode.Edit);

        var root = new GameObject("Character");
        Animator animator = root.AddComponent<Animator>();
        var clip = new AnimationClip { Name = "Attack" };
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 0f, 0f);
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 2f);
        animator.AddClip(clip);

        var weapon = new GameObject("Sword");
        weapon.SetParent(root, false);
        weapon.AddComponent<UserEventReceiverForTests>();

        scene.Add(root);
        scene.Activate();

        // 1. GetCallableEventMethods discovers user script methods before built-in Prowl.Runtime methods and excludes lifecycle hooks
        var rootMethods = SceneClipBinding.GetCallableEventMethods(root);
        var weaponMethods = SceneClipBinding.GetCallableEventMethods(weapon);

        Assert.Contains(weaponMethods, m => m.MethodName == "SpawnSlashVfx" && m.ArgType == ProwlActionArgType.None && m.IsUserScript);
        Assert.Contains(weaponMethods, m => m.MethodName == "PlayFootstep" && m.ArgType == ProwlActionArgType.Float && m.ParameterName == "volume" && m.ParameterType == typeof(float));
        Assert.Contains(weaponMethods, m => m.MethodName == "AttachTarget" && m.ArgType == ProwlActionArgType.Object && m.ParameterType == typeof(GameObject));
        Assert.DoesNotContain(weaponMethods, m => m.MethodName == "Update" || m.MethodName == "OnEnable");
        Assert.NotEmpty(rootMethods); // Animator methods are also available

        // Verify user script methods sort ahead of built-in engine methods when both are on the same GameObject
        root.AddComponent<UserEventReceiverForTests>();
        var combined = SceneClipBinding.GetCallableEventMethods(root);
        Assert.True(combined[0].IsUserScript);

        // 2. TryValidateEventMethod resolves target, signature, parameter name, and expected EngineObject subtype
        bool validFloat = SceneClipBinding.TryValidateEventMethod(
            root, "Sword", "PlayFootstep", ProwlActionArgType.Float,
            out string sigFloat, out string paramName, out System.Type? paramType);
        Assert.True(validFloat);
        Assert.Equal("UserEventReceiverForTests.PlayFootstep(Single)", sigFloat);
        Assert.Equal("volume", paramName);
        Assert.Equal(typeof(float), paramType);

        bool validObj = SceneClipBinding.TryValidateEventMethod(
            root, "Sword", "AttachTarget", ProwlActionArgType.Object,
            out _, out string objParamName, out System.Type? objParamType);
        Assert.True(validObj);
        Assert.Equal("target", objParamName);
        Assert.Equal(typeof(GameObject), objParamType);

        Assert.False(SceneClipBinding.TryValidateEventMethod(
            root, "MissingChild", "PlayFootstep", ProwlActionArgType.Float,
            out _, out _, out _));
        Assert.False(SceneClipBinding.TryValidateEventMethod(
            root, "Sword", "NonExistentMethod", ProwlActionArgType.None,
            out _, out _, out _));

        // 3. AnimationPanel event creation & selection helpers
        var panel = new AnimationPanel();
        try
        {
            panel.BindAnimator(animator);
            SceneAnimationEvent? instant = panel.AddEventAtPlayhead(ranged: false);
            Assert.NotNull(instant);
            Assert.False(instant!.IsRange);
            Assert.Equal(string.Empty, instant.FunctionName);

            SceneAnimationEvent? ranged = panel.AddEventAtPlayhead(ranged: true);
            Assert.NotNull(ranged);
            Assert.True(ranged!.IsRange);
            Assert.True(ranged.EndTime > ranged.Time);

            ranged.Time = 0.4f;
            ranged.EndTime = 0.8f;
            panel.SelectEvent(0);
            panel.SelectEvent(1);
            Assert.Equal(2, clip.PropertyEvents.Count);
        }
        finally
        {
            panel.OnClosed();
        }
    }

    [Fact]
    public void BuildAddPropertyHierarchyNodes_SeparatesParentPropertiesFromChildNamedGameObjectOrTransform()
    {
        EditorRegistries.Initialize();
        using var scene = new Scene(SceneMode.Edit);

        var root = new GameObject("Player");
        Animator animator = root.AddComponent<Animator>();
        animator.AddClip(new AnimationClip { Name = "Idle" });

        // Child explicitly named "gameobject" and another named "Transform"
        var childGo = new GameObject("gameobject");
        childGo.SetParent(root, false);
        var childTf = new GameObject("Transform");
        childTf.SetParent(root, false);

        scene.Add(root);
        scene.Activate();

        var panel = new AnimationPanel();
        try
        {
            panel.BindAnimator(animator);
            var nodes = panel.BuildAddPropertyHierarchyNodes();

            // 1. Parent GameObject.Enabled is a direct "Is Active (Enabled)" item with no "GameObject" component group
            var rootNode = Assert.Single(nodes, n => n.RelativePath == "");
            Assert.Contains(rootNode.Properties, p => p.ComponentGroup == "" && p.PropertyLabel == "Is Active (Enabled)");
            Assert.DoesNotContain(rootNode.Properties, p => p.ComponentGroup.Equals("GameObject", System.StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rootNode.Properties, p => p.ComponentGroup == "Transform" && p.PropertyLabel == "LocalPosition");

            // 2. Children named "gameobject" / "Transform" are separate hierarchy nodes, never merged into parent groups
            var goNode = Assert.Single(nodes, n => n.RelativePath == "gameobject");
            Assert.Equal(1, goNode.Depth);
            Assert.Contains(goNode.Properties, p => p.ComponentGroup == "" && p.PropertyLabel == "Is Active (Enabled)");

            var tfNode = Assert.Single(nodes, n => n.RelativePath == "Transform");
            Assert.Equal("Player › Transform", tfNode.Breadcrumb);
            Assert.Contains(tfNode.Properties, p => p.ComponentGroup == "Transform" && p.PropertyLabel == "LocalPosition");
            Assert.Equal(1, rootNode.Properties.Count(p => p.ComponentGroup == "Transform" && p.PropertyLabel == "LocalPosition"));
        }
        finally
        {
            panel.OnClosed();
        }
    }

    [Fact]
    public void CurvesView_ShowsAllGroupSubCurvesWhenGroupSelectedEvenIfCollapsed_AndSingleCurveWhenChannelSelected()
    {
        EditorRegistries.Initialize();
        using var scene = new Scene(SceneMode.Edit);

        var root = new GameObject("Cube");
        Animator animator = root.AddComponent<Animator>();
        var clip = new AnimationClip { Name = "Move" };
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 0f, 0f);
        clip.SetCurveKey("", "Transform", "LocalPosition.X", 1f, 5f);
        clip.SetCurveKey("", "Transform", "LocalPosition.Y", 0f, 1f);
        clip.SetCurveKey("", "Transform", "LocalPosition.Y", 1f, 3f);
        clip.SetCurveKey("", "Transform", "LocalPosition.Z", 0f, -2f);
        clip.SetCurveKey("", "Transform", "LocalPosition.Z", 1f, 4f);
        animator.AddClip(clip);

        scene.Add(root);
        scene.Activate();

        var panel = new AnimationPanel();
        try
        {
            panel.BindAnimator(animator);
            panel.RebuildRows();

            // Row 0 = LocalPosition (Group), Row 1 = X, Row 2 = Y, Row 3 = Z
            // 1. Selecting the LocalPosition group shows all 3 curves (X, Y, Z)
            panel.SelectTrackRow(0);
            var groupCurves = panel.GetVisibleCurves();
            Assert.Equal(3, groupCurves.Count);
            Assert.Contains(groupCurves, c => c.Track.PropertyPath == "LocalPosition.X");
            Assert.Contains(groupCurves, c => c.Track.PropertyPath == "LocalPosition.Y");
            Assert.Contains(groupCurves, c => c.Track.PropertyPath == "LocalPosition.Z");

            // 2. Selecting LocalPosition.X shows ONLY the single X curve
            panel.SelectTrackRow(1);
            var xOnlyCurves = panel.GetVisibleCurves();
            Assert.Single(xOnlyCurves);
            Assert.Equal("LocalPosition.X", xOnlyCurves[0].Track.PropertyPath);

            // 3. Additive (Ctrl) selection of LocalPosition.X + LocalPosition.Z shows both X and Z
            panel.SelectTrackRow(3, additive: true);
            var xzCurves = panel.GetVisibleCurves();
            Assert.Equal(2, xzCurves.Count);

            // 4. Collapsing the LocalPosition dropdown while LocalPosition is selected STILL shows all 3 curves (X, Y, Z)
            panel.SelectTrackRow(0);
            panel.SetGroupCollapsed(0, collapsed: true);
            var collapsedGroupCurves = panel.GetVisibleCurves();
            Assert.Equal(3, collapsedGroupCurves.Count);
        }
        finally
        {
            panel.OnClosed();
        }
    }

    [Fact]
    public void CompiledPropertyAndEventBindings_EvaluateAccuratelyWithZeroPerFrameAllocations()
    {
        var root = new GameObject("Actor");
        var child = new GameObject("TargetProp");
        child.SetParent(root, false);

        ZeroAllocTestReceiver receiver = root.AddComponent<ZeroAllocTestReceiver>();
        receiver.Intensity = 2f;
        receiver.Tint = new Color(0.1f, 0.2f, 0.3f, 1f);
        receiver.Offset = new Float3(1f, 2f, 3f);
        receiver.Label = "Idle";
        receiver.LinkedObject = null;

        var clip = new AnimationClip { Name = "PerfClip" };
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Intensity), 0f, 10f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Intensity), 1f, 20f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.CountProp), 0f, 0f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.CountProp), 1f, 8f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Flag), 0f, 0f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Flag), 1f, 1f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), "Tint.R", 0f, 0.5f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), "Tint.G", 0f, 0.75f);
        clip.SetCurveKey("", nameof(ZeroAllocTestReceiver), "Offset.Y", 0f, 9f);
        clip.SetStringKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Label), 0f, "Active");
        clip.SetObjectKey("", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.LinkedObject), 0f, child);

        clip.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = 0.1f,
            EndTime = 0.9f,
            FunctionName = nameof(ZeroAllocTestReceiver.OnWindowEnter),
            StayFunctionName = nameof(ZeroAllocTestReceiver.OnWindowStay),
            ExitFunctionName = nameof(ZeroAllocTestReceiver.OnWindowExit),
            ArgType = ProwlActionArgType.Int,
            IntParam = 42,
        });

        var binding = new SceneClipBinding(root, clip);

        // Warm up JIT once before measuring thread allocations
        binding.Sample(0.05f);
        binding.FireEvents(0f, 0.2f, forward: true, looped: false);

        Assert.Equal(42, receiver.LastEnterCode);
        Assert.True(receiver.LastStayProgress > 0f);
        Assert.Equal("Active", receiver.Label);
        Assert.Same(child, receiver.LinkedObject);

        // Measure 100 frames of component scalar/vector/color/string/object tracks + ranged Stay(float) events
        long beforeBytes = System.GC.GetAllocatedBytesForCurrentThread();
        float prevT = 0.2f;
        for (int frame = 0; frame < 100; frame++)
        {
            float currT = 0.2f + (frame + 1) * 0.005f; // stays inside [0.1..0.9] firing Stay(float) every frame
            binding.SampleWeighted(currT, 0.8f);
            binding.FireEvents(prevT, currT, forward: true, looped: false);
            prevT = currT;
        }
        long allocatedBytes = System.GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Assert.Equal(0L, allocatedBytes);

        // Finishing the ranged window fires Exit cleanly, and RestoreBaseline puts pristine state back
        binding.FireEvents(prevT, 0.95f, forward: true, looped: false);
        Assert.Equal(1, receiver.ExitCount);

        binding.Sample(0.5f);
        Assert.Equal(15f, receiver.Intensity, 2);
        Assert.Equal(4, receiver.CountProp);
        Assert.True(receiver.Flag);
        Assert.Equal(0.5f, receiver.Tint.R, 3);
        Assert.Equal(0.75f, receiver.Tint.G, 3);
        Assert.Equal(9f, receiver.Offset.Y, 3);

        binding.RestoreBaseline();
        Assert.Equal(2f, receiver.Intensity, 3);
        Assert.Equal(0.1f, receiver.Tint.R, 3);
        Assert.Equal(2f, receiver.Offset.Y, 3);
        Assert.Equal("Idle", receiver.Label);
        Assert.Null(receiver.LinkedObject);
    }

    [Fact]
    public void PrefabInstantiation_PreservesClipsInternalBindingsAndDrivesEachInstanceIndependently()
    {
        var chest = new GameObject("Chest");
        var lid = new GameObject("Lid");
        lid.SetParent(chest);
        lid.Transform.LocalPosition = new Float3(0f, 1f, 0f);
        ZeroAllocTestReceiver lidReceiver = lid.AddComponent<ZeroAllocTestReceiver>();

        var latch = new GameObject("Latch");
        latch.SetParent(chest);
        ZeroAllocTestReceiver latchReceiver = latch.AddComponent<ZeroAllocTestReceiver>();
        latchReceiver.Intensity = 0f;

        var clip = new AnimationClip { Name = "ChestOpen", FrameRate = 60f, WrapMode = SceneClipWrapMode.Once };
        clip.SetCurveKey("Lid", "Transform", "LocalPosition.Y", 0f, 1f);
        clip.SetCurveKey("Lid", "Transform", "LocalPosition.Y", 1f, 5f);
        clip.SetCurveKey("@LatchSlot", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Intensity), 0f, 0f);
        clip.SetCurveKey("@LatchSlot", nameof(ZeroAllocTestReceiver), nameof(ZeroAllocTestReceiver.Intensity), 1f, 20f);
        clip.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = 0.4f,
            RelativePath = "Lid",
            FunctionName = nameof(ZeroAllocTestReceiver.OnWindowEnter),
            ArgType = ProwlActionArgType.Int,
            IntParam = 77,
        });

        Animator anim = chest.AddComponent<Animator>();
        anim.AddClip(clip);
        anim.SetObjectBinding("LatchSlot", latch);

        var prefab = new PrefabAsset
        {
            Name = "ChestPrefab",
            GameObjectData = Serializer.Serialize(typeof(object), chest, PrefabUtility.TreeValueContext(chest)),
        };

        GameObject instA = GameObject.InstantiateDetached(prefab)!;
        GameObject instB = GameObject.InstantiateDetached(prefab)!;

        Animator animA = instA.GetComponent<Animator>()!;
        Animator animB = instB.GetComponent<Animator>()!;
        Assert.NotNull(animA);
        Assert.NotNull(animB);
        Assert.Single(animA.Clips);
        Assert.Single(animB.Clips);

        GameObject lidA = SceneClipBinding.ResolveGameObject(instA, "Lid")!;
        GameObject lidB = SceneClipBinding.ResolveGameObject(instB, "Lid")!;
        GameObject latchA = SceneClipBinding.ResolveGameObject(instA, "Latch")!;
        GameObject latchB = SceneClipBinding.ResolveGameObject(instB, "Latch")!;

        // Internal @Slot bindings inside the prefab clone to each instance's own child, never sharing references
        Assert.Same(latchA, animA.GetObjectBinding("LatchSlot"));
        Assert.Same(latchB, animB.GetObjectBinding("LatchSlot"));
        Assert.NotSame(latchA, latchB);

        // Overriding @LatchSlot on instance B to an external scene object affects only instance B
        var externalTarget = new GameObject("ExternalLamp");
        ZeroAllocTestReceiver externalReceiver = externalTarget.AddComponent<ZeroAllocTestReceiver>();
        animB.SetObjectBinding("LatchSlot", externalTarget);

        animA.Play("ChestOpen");
        animA.Tick(0.5f);

        animB.Play("ChestOpen");
        animB.Tick(1.0f);

        Assert.Equal(3f, lidA.Transform.LocalPosition.Y, 2);
        Assert.Equal(10f, latchA.GetComponent<ZeroAllocTestReceiver>()!.Intensity, 2);
        Assert.Equal(77, lidA.GetComponent<ZeroAllocTestReceiver>()!.LastEnterCode);

        Assert.Equal(5f, lidB.Transform.LocalPosition.Y, 2);
        Assert.Equal(0f, latchB.GetComponent<ZeroAllocTestReceiver>()!.Intensity, 2); // Untouched because B overrode @LatchSlot
        Assert.Equal(20f, externalReceiver.Intensity, 2);
        Assert.Equal(77, lidB.GetComponent<ZeroAllocTestReceiver>()!.LastEnterCode);
    }

    [Fact]
    public void PreviewSuspendForSave_RestoresBaselineDuringSaveAndResumesSeamlessly()
    {
        var root = new GameObject("PrefabInstanceRoot");
        var lid = new GameObject("Lid");
        lid.SetParent(root);
        lid.Transform.LocalPosition = new Float3(0f, 1.5f, 0f); // Pristine baseline Y = 1.5

        var clip = new AnimationClip { Name = "OpenPreview", FrameRate = 60f };
        clip.SetCurveKey("Lid", "Transform", "LocalPosition.Y", 0f, 0f);
        clip.SetCurveKey("Lid", "Transform", "LocalPosition.Y", 1f, 10f);

        Animator anim = root.AddComponent<Animator>();
        anim.AddClip(clip);

        var panel = new AnimationPanel();
        panel.EnsureUndoHook();

        float observedYDuringSave = float.NaN;
        string? CapturePoseDuringSave()
        {
            observedYDuringSave = (float)lid.Transform.LocalPosition.Y;
            return null;
        }

        SaveManager.OnSave += CapturePoseDuringSave;
        try
        {
            panel.BindAnimator(anim);
            panel.SetPlayhead(0.5f, sampleScene: true);
            Assert.Equal(5f, lid.Transform.LocalPosition.Y, 2); // Scrubbed preview pose

            // RequestSave runs AnimationPanel's pre-save hook BEFORE downstream scene/prefab save handlers
            SaveManager.RequestSave();
            Assert.Equal(1.5f, observedYDuringSave, 3); // Clean baseline was seen by Save!

            // Next GUI frame seamlessly resumes the preview pose at the playhead
            panel.ResumePreviewAfterSaveIfNeeded();
            Assert.Equal(5f, lid.Transform.LocalPosition.Y, 2);
        }
        finally
        {
            SaveManager.OnSave -= CapturePoseDuringSave;
            panel.OnClosed();
        }

        // Closing the panel leaves the target at its pristine baseline
        Assert.Equal(1.5f, lid.Transform.LocalPosition.Y, 3);
    }

    [Fact]
    public void AddPropertyHierarchyNodes_PreservesDeepBreadcrumbsDisambiguatesSiblingsAndSearchesAcrossTokens()
    {
        var player = new GameObject("Player");
        var spine = new GameObject("Spine");
        spine.SetParent(player, false);

        var leftArm = new GameObject("LeftArm");
        leftArm.SetParent(spine, false);
        var leftHand = new GameObject("Hand");
        leftHand.SetParent(leftArm, false);

        var rightArm = new GameObject("RightArm");
        rightArm.SetParent(spine, false);
        var rightHand = new GameObject("Hand");
        rightHand.SetParent(rightArm, false);

        // Two identically-named sibling children under rightHand
        var ring1 = new GameObject("Finger");
        ring1.SetParent(rightHand, false);
        var ring2 = new GameObject("Finger");
        ring2.SetParent(rightHand, false);

        Animator anim = player.AddComponent<Animator>();
        anim.AddClip(new AnimationClip { Name = "Wave" });

        var panel = new AnimationPanel();
        try
        {
            panel.BindAnimator(anim);
            var nodes = panel.BuildAddPropertyHierarchyNodes();

            // Verify hierarchy structure, depths, and breadcrumbs distinguish LeftArm/Hand from RightArm/Hand
            var leftHandNode = Assert.Single(nodes, n => n.RelativePath == "Spine/LeftArm/Hand");
            var rightHandNode = Assert.Single(nodes, n => n.RelativePath == "Spine/RightArm/Hand");
            Assert.Equal(3, leftHandNode.Depth);
            Assert.Equal("Player › Spine › LeftArm › Hand", leftHandNode.Breadcrumb);
            Assert.Equal("Player › Spine › RightArm › Hand", rightHandNode.Breadcrumb);

            // Duplicate siblings are numbered in both DisplayName and Breadcrumb
            Assert.Contains(nodes, n => n.DisplayName == "Finger (1)" && n.Breadcrumb == "Player › Spine › RightArm › Hand › Finger (1)");
            Assert.Contains(nodes, n => n.DisplayName == "Finger (2)" && n.Breadcrumb == "Player › Spine › RightArm › Hand › Finger (2)");

            // Multi-token search ("RightArm Hand LocalPosition") matches across object path + property name
            var matches = AnimationPanel.FilterAddPropertyNodes(nodes, "RightArm Hand LocalPosition");
            Assert.NotEmpty(matches);
            Assert.All(matches, m => Assert.Contains("RightArm", m.Node.Breadcrumb));
            Assert.Contains(matches, m => m.Node == rightHandNode && m.Item.PropertyLabel == "LocalPosition");
            Assert.DoesNotContain(matches, m => m.Node == leftHandNode);

            // Compact track row prefix keeps parent + leaf readable on deeply nested paths
            Assert.Equal("Spine", AnimationPanel.FormatCompactHierarchyPrefix("Spine"));
            Assert.Equal("Spine › RightArm", AnimationPanel.FormatCompactHierarchyPrefix("Spine/RightArm"));
            Assert.Equal("… › LeftArm › Hand", AnimationPanel.FormatCompactHierarchyPrefix("Spine/LeftArm/Hand"));
            Assert.Equal("… › RightArm › Hand", AnimationPanel.FormatCompactHierarchyPrefix("Spine/RightArm/Hand"));
        }
        finally
        {
            panel.OnClosed();
        }
    }
}

internal sealed class ZeroAllocTestReceiver : Component
{
    public float Intensity;
    public int CountProp { get; set; }
    public bool Flag = false;
    public Color Tint;
    public Float3 Offset { get; set; }
    public string Label = "";
    public GameObject? LinkedObject;

    public int LastEnterCode;
    public float LastStayProgress;
    public int ExitCount;

    public void OnWindowEnter(int code) => LastEnterCode = code;
    public void OnWindowStay(float progress) => LastStayProgress = progress;
    public void OnWindowExit() => ExitCount++;
}

internal sealed class UserEventReceiverForTests : Component
{
    public float LastVolume;
    public GameObject? LastAttached;

    public override void Update() { }
    public override void OnEnable() { }

    public void SpawnSlashVfx() { }
    public void PlayFootstep(float volume) => LastVolume = volume;
    public void AttachTarget(GameObject target) => LastAttached = target;
}

public class ImportedAnimationClipWorkflowTests : EditorTestHarness
{
    private static void WriteAnimatedRigGltf(string absolutePath, string clipName, float endY)
    {
        byte[] bin = new byte[32];
        System.Buffer.BlockCopy(new float[] { 0f, 1f }, 0, bin, 0, 8);
        System.Buffer.BlockCopy(new float[] { 0f, 0f, 0f, 0f, endY, 0f }, 0, bin, 8, 24);
        string b64 = System.Convert.ToBase64String(bin);

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [{ "nodes": [0] }],
          "nodes": [
            { "name": "Root", "children": [1] },
            { "name": "Arm", "translation": [0.0, 0.0, 0.0] }
          ],
          "buffers": [{ "byteLength": 32, "uri": "data:application/octet-stream;base64,{{b64}}" }],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
            { "buffer": 0, "byteOffset": 8, "byteLength": 24 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR", "min": [0.0], "max": [1.0] },
            { "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC3" }
          ],
          "animations": [{
            "name": "{{clipName}}",
            "samplers": [{ "input": 0, "output": 1, "interpolation": "LINEAR" }],
            "channels": [{ "sampler": 0, "target": { "node": 1, "path": "translation" } }]
          }]
        }
        """;
        System.IO.File.WriteAllText(absolutePath, json);
    }

    [Fact]
    public void ImportedClip_CanAddEventsAndModifyCurves_AndReimportReappliesEventsWhileOverridingAnimationChanges()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        string gltfPath = AssetAbsolutePath("Character.gltf");
        WriteAnimatedRigGltf(gltfPath, "Walk", endY: 2f);

        System.Guid modelGuid = Assets.ImportFile("Character.gltf");
        Assert.NotEqual(System.Guid.Empty, modelGuid);

        SubAssetEntry clipSub = Assert.Single(Assets.GetSubAssets(modelGuid), s => s.Type == typeof(AnimationClip));
        Assert.True(Assets.IsImportedAnimationSubAsset(clipSub.Guid, out AssetEntry? parentEntry));
        Assert.NotNull(parentEntry);
        Assert.True(Assets.CanSave(clipSub.Guid));

        AnimationClip clip = Load<AnimationClip>(clipSub.Guid)!;
        Assert.NotNull(clip);
        Assert.Equal("Walk", clip.SourceName);
        Assert.True(clip.HasMotionPayload);

        // 1. Add instant + ranged AnimationEvents AND unpack/modify animation curves on the imported clip
        Undo.RecordObject(clip, "Edit Imported Clip");
        clip.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = 0.25f,
            RelativePath = "Arm",
            FunctionName = "PlayFootstep",
            ArgType = ProwlActionArgType.Float,
            FloatParam = 0.8f,
        });
        clip.PropertyEvents.Add(new SceneAnimationEvent
        {
            Time = 0.4f,
            EndTime = 0.85f,
            FunctionName = "OnWindowEnter",
            StayFunctionName = "OnWindowStay",
            ExitFunctionName = "OnWindowExit",
            ArgType = ProwlActionArgType.Int,
            IntParam = 9,
        });

        int unpackedCount = clip.UnpackMotionToCurveTracks();
        Assert.True(unpackedCount > 0);
        SceneCurveTrack? initialArmY = clip.FindCurveTrack("Root/Arm", "Transform", "LocalPosition.Y");
        Assert.NotNull(initialArmY);
        Assert.Equal(2f, initialArmY!.Curve.Evaluate(1f), 2);

        clip.SetCurveKey("Root/Arm", "Transform", "LocalPosition.Y", 1f, 99f); // Override unpacked bone Y at t=1s to 99
        clip.Invalidate();
        EditorUtility.SetDirty(clip);
        Assert.True(EditorUtility.IsDirty(clip));

        // 2. Save the imported clip -> writes Events & Loop to Character.gltf.meta AND updates sub-asset cache
        Assert.True(Assets.SaveAsset(clip));
        EditorUtility.ClearAssetDirty(clip.AssetID);

        // Simulate editor restart: both the in-place curve modification (99f) AND events persist in cache
        ReopenDatabase();
        AnimationClip reopenedClip = Load<AnimationClip>(clipSub.Guid)!;
        Assert.NotNull(reopenedClip);
        Assert.Equal(2, reopenedClip.PropertyEvents.Count);
        SceneCurveTrack? modifiedTrack = reopenedClip.FindCurveTrack("Root/Arm", "Transform", "LocalPosition.Y");
        Assert.NotNull(modifiedTrack);
        Assert.Equal(99f, modifiedTrack!.Curve.Evaluate(1f), 2);

        // 3. Reimport the model after updating the source .gltf (endY changed from 2 -> 6):
        //    Reimport OVERRIDES the animation curve/pose changes with the fresh source file,
        //    while RE-APPLYING all authored PropertyEvents from .meta!
        WriteAnimatedRigGltf(gltfPath, "Walk", endY: 6f);
        Assets.Reimport(modelGuid);

        AnimationClip reimportedClip = Load<AnimationClip>(clipSub.Guid)!;
        Assert.NotNull(reimportedClip);
        Assert.Empty(reimportedClip.CurveTracks); // In-place curve overrides cleared by fresh source import
        Assert.Equal(2, reimportedClip.PropertyEvents.Count); // Authored events re-applied from .meta!
        Assert.Equal("PlayFootstep", reimportedClip.PropertyEvents[0].FunctionName);
        Assert.Equal("Arm", reimportedClip.PropertyEvents[0].RelativePath);
        Assert.Equal(0.8f, reimportedClip.PropertyEvents[0].FloatParam, 3);
        Assert.True(reimportedClip.PropertyEvents[1].IsRange);
        Assert.Equal("OnWindowEnter", reimportedClip.PropertyEvents[1].FunctionName);
        Assert.Equal("OnWindowStay", reimportedClip.PropertyEvents[1].StayFunctionName);
        Assert.Equal("OnWindowExit", reimportedClip.PropertyEvents[1].ExitFunctionName);
        Assert.Equal(9, reimportedClip.PropertyEvents[1].IntParam);

        // Verify the fresh skeletal animation from the updated .gltf (endY = 6) plays accurately
        reimportedClip.UnpackMotionToCurveTracks();
        SceneCurveTrack? freshArmY = reimportedClip.FindCurveTrack("Root/Arm", "Transform", "LocalPosition.Y");
        Assert.NotNull(freshArmY);
        Assert.Equal(6f, freshArmY!.Curve.Evaluate(1f), 2);
    }

    [Fact]
    public void ExtractCurrentImportedClip_CreatesStandaloneCopyImmuneToModelReimport_AndPlacePropertyEventsRespectsTrim()
    {
        EditorRegistries.Initialize();
        EditorRegistries.OnProjectOpened();

        string gltfPath = AssetAbsolutePath("Door.gltf");
        WriteAnimatedRigGltf(gltfPath, "Open", endY: 3f);
        System.Guid modelGuid = Assets.ImportFile("Door.gltf");
        SubAssetEntry clipSub = Assert.Single(Assets.GetSubAssets(modelGuid), s => s.Type == typeof(AnimationClip));
        AnimationClip importedClip = Load<AnimationClip>(clipSub.Guid)!;

        var panel = new AnimationPanel();
        try
        {
            panel.SelectClip(importedClip);
            importedClip.UnpackMotionToCurveTracks();
            importedClip.SetCurveKey("Root/Arm", "Transform", "LocalPosition.Y", 1f, 42f);

            AnimationClip? extracted = panel.ExtractCurrentImportedClip();
            Assert.NotNull(extracted);
            Assert.False(Assets.IsImportedAnimationSubAsset(extracted!.AssetID, out _));
            Assert.True(Assets.SaveAsset(extracted));

            // Reimporting the source model leaves the extracted standalone .animclip completely untouched
            WriteAnimatedRigGltf(gltfPath, "Open", endY: 1f);
            Assets.Reimport(modelGuid);

            AnimationClip reloadedExtracted = Load<AnimationClip>(extracted.AssetID)!;
            Assert.Equal(42f, reloadedExtracted.FindCurveTrack("Root/Arm", "Transform", "LocalPosition.Y")!.Curve.Evaluate(1f), 2);
        }
        finally
        {
            panel.OnClosed();
        }

        // Also verify PlacePropertyEvents shifts & clamps take-timed events against TrimStart / TrimEnd
        var takeEvents = new System.Collections.Generic.List<SceneAnimationEvent>
        {
            new() { Time = 0.1f, FunctionName = "DroppedBeforeTrim" },
            new() { Time = 0.5f, FunctionName = "KeptInstant" },
            new() { Time = 0.3f, EndTime = 0.9f, FunctionName = "ClampedRangeEnter", ExitFunctionName = "ClampedRangeExit" },
        };
        var placed = ModelRigBuilder.PlacePropertyEvents(takeEvents, start: 0.4f, end: 0.8f);
        Assert.Equal(2, placed.Count);
        Assert.Equal("KeptInstant", placed[0].FunctionName);
        Assert.Equal(0.1f, placed[0].Time, 3);
        Assert.True(placed[1].IsRange);
        Assert.Equal(0f, placed[1].Time, 3);
        Assert.Equal(0.4f, placed[1].EndTime, 3);
    }
}
