// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Projects.Settings;
using Prowl.PaperUI;

namespace Prowl.Editor;

/// <summary>
/// Per-platform scripting define symbols and any future platform-specific
/// build knobs. Serialised as part of <see cref="BuildSettings"/>.
/// </summary>
public class PlatformBuildProfile
{
    /// <summary>
    /// Semicolon-separated list of scripting define symbols that will be
    /// passed to <c>dotnet publish</c> when
    /// building for this platform.
    /// </summary>
    public List<string> ScriptingDefineSymbols { get; set; } = [];

    public virtual Type GetPipelineType()
    {
        return null;
    }

    public virtual void ToDefault() { }

    public virtual void OnGUI(Paper paper) { }

    public virtual void ModifyDefines(List<string> defines) { }
}
