// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Something edited in a world of its own while the edit world sleeps underneath, such as a prefab. The session owns
/// what is saved and its own dirty flag, and the edit world wakes as it was when the session ends.
/// </summary>
public abstract class IsolatedSession
{
    /// <summary>The world the session edits in.</summary>
    public Scene? World { get; internal set; }

    /// <summary>Whether the session has unsaved changes. Its own flag, so the scene's is left as it was.</summary>
    public bool IsDirty { get; internal set; }

    /// <summary>What is being edited, for the hierarchy, the undo history and messages.</summary>
    public abstract string Name { get; }

    /// <summary>The save toast label for what is being edited.</summary>
    public abstract string SaveLabel { get; }

    /// <summary>The undo history panel's title while the session runs.</summary>
    public abstract string HistoryTitle { get; }

    /// <summary>The title and body of the question asked before unsaved changes are left.</summary>
    public abstract (string Title, string Body) UnsavedQuestion { get; }

    /// <summary>Writes what is being edited. True when it was written.</summary>
    public abstract bool Save();

    /// <summary>Saves, then ends the session.</summary>
    public abstract void SaveAndClose();

    /// <summary>Ends the session without saving.</summary>
    public abstract void Close();
}

/// <summary>
/// The one isolated session open at a time. Panels, scene operations, play mode, saving and undo ask this rather than any
/// one kind of session.
/// </summary>
public static class IsolatedWorld
{
    private static object? _savedSceneUndo;

    public static IsolatedSession? Current { get; private set; }

    public static bool IsOpen => Current != null;

    /// <summary>The session's world while one is open and its world is alive.</summary>
    public static Scene? World => Current?.World is { } world && world.IsValid() ? world : null;

    public static bool IsDirty => Current?.IsDirty == true;

    /// <summary>The name of the edit world the session was opened over, for the hierarchy's breadcrumb.</summary>
    public static string? OriginalSceneName { get; private set; }

    /// <summary>
    /// Runs <paramref name="open"/> once no session is open: an open one is closed first, asking to save it when it has
    /// unsaved changes.
    /// </summary>
    public static void Replace(Action open)
    {
        if (Current is not { } current)
        {
            open();
            return;
        }

        if (current.IsDirty)
        {
            (string title, string body) = current.UnsavedQuestion;
            Origami.Confirm(title, body,
                onYes: () => { current.SaveAndClose(); open(); },
                onNo: () => { current.Close(); open(); });
            return;
        }

        current.Close();
        open();
    }

    /// <summary>Leaves the session, asking to save first when it has unsaved changes.</summary>
    public static void RequestClose()
    {
        if (Current is not { } current) return;
        if (!current.IsDirty)
        {
            current.Close();
            return;
        }

        (string title, string body) = current.UnsavedQuestion;
        Origami.Confirm(title, body, onYes: current.SaveAndClose, onNo: current.Close);
    }

    /// <summary>
    /// Makes a session current over a world already built and active, the edit world asleep under it. Its own undo history
    /// starts empty, the edit world's parked until it ends.
    /// </summary>
    internal static void Begin(IsolatedSession session, Scene world, Scene editWorld)
    {
        session.World = world;
        session.IsDirty = false;
        Current = session;
        _savedSceneUndo = Undo.PushContext();
        OriginalSceneName = editWorld.Name;
    }

    /// <summary>Destroys the session's world and wakes the edit world underneath, refreshing instances of prefabs imported meanwhile.</summary>
    internal static void CloseWorld(IsolatedSession session)
    {
        Scene? world = session.World;
        session.World = null;
        if (world.IsValid()) world!.Dispose();
        EditorSceneManager.EditWorld.Activate();
        Prefabs.PrefabUtility.RefreshStaleInstances();
    }

    /// <summary>Ends the session: the edit world's undo history comes back, which still resolves as it never changed.</summary>
    internal static void End(IsolatedSession session)
    {
        if (!ReferenceEquals(Current, session)) return;
        Current = null;
        OriginalSceneName = null;
        session.IsDirty = false;
        Undo.PopContext(_savedSceneUndo);
        _savedSceneUndo = null;
    }

    /// <summary>
    /// Adds the editor only camera and light a session's world is seen with: hidden, never saved, and the camera backed
    /// off along its tilt far enough to take in what is framed.
    /// </summary>
    internal static void AddViewingRig(Scene world, GameObject? framed, string prefix)
    {
        var camGo = new GameObject($"{prefix} Camera")
        {
            Tag = "Main Camera",
            HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos
        };
        camGo.Transform.Position = FramingPositionFor(framed);
        camGo.Transform.LocalEulerAngles = new Float3(15, 0, 0);
        Camera cam = camGo.AddComponent<Camera>();
        cam.Depth = -1;
        cam.Profile = VolumeProfile.CreateCameraDefault($"{prefix} Camera Profile");
        world.Add(camGo);

        var lightGo = new GameObject($"{prefix} Light")
        {
            HideFlags = HideFlags.HideAndDontSave | HideFlags.NoGizmos
        };
        lightGo.Transform.LocalEulerAngles = new Float3(45, 225, 0);
        lightGo.AddComponent<DirectionalLight>();
        world.Add(lightGo);
    }

    /// <summary>
    /// Where to put the editing camera so the object fills the view. Something that draws nothing keeps a fixed spot,
    /// which is as good a guess as any for something with no extent.
    /// </summary>
    private static Float3 FramingPositionFor(GameObject? go)
    {
        if (go.IsNotValid() || !Panels.SceneViewPanel.TryGetWorldBounds(go!, out Float3 min, out Float3 max))
            return new Float3(0, 2, -5);

        Float3 center = (min + max) * 0.5f;
        Float3 extents = (max - min) * 0.5f;

        // Far enough that the largest axis is comfortably inside the view, with a floor so a tiny object is not framed
        // from a millimetre away
        float radius = MathF.Max(MathF.Max(extents.X, extents.Y), extents.Z);
        float distance = MathF.Max(radius * 3.5f, 1.5f);

        return center + new Float3(0, distance * 0.35f, -distance);
    }
}
