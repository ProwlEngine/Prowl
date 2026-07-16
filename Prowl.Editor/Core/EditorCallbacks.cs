using System;

namespace Prowl.Editor;

[AttributeUsage(AttributeTargets.Method)]
public class OnSceneSavedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class OnUndoRedoAttribute : Attribute { }
