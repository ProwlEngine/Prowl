using System;

namespace Prowl.Editor;

/// <summary> When placed on a parameterless static void method, that method is called every time a scene is saved in the editor. </summary>
[AttributeUsage(AttributeTargets.Method)]
public class OnSceneSavedAttribute : Attribute { }

/// <summary> When placed on a parameterless static void method, that method is called every time an undo or redo operation occurs in the editor. </summary>
[AttributeUsage(AttributeTargets.Method)]
public class OnUndoRedoAttribute : Attribute { }
