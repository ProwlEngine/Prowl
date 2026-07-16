// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Core;

/// <summary>
/// Implemented by long-lived editor objects (typically dock panels) that cache references to
/// scene objects or user-script types. Hot-reload tears the script
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> down, and any surviving reference
/// into it pins the old context and blocks the unload.
///
/// <see cref="EditorApplication.ReleaseScriptReferences"/> calls <see cref="OnScriptReloadCleanup"/>
/// on every open panel right before the unload. Implementations must drop their cached
/// scene/user references (set fields to null, clear collections). They do NOT need to dispose
/// themselves the panel instance lives on; only its references into the dying context go away.
/// </summary>
public interface IScriptReloadCleanup
{
    void OnScriptReloadCleanup();
}
