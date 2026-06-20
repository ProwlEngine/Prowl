// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime;

public abstract class ScriptLifecycleAttribute : Attribute
{
    // Lower values run first. Methods with the same order run in an unspecified order.
    public int Order { get; init; }
}

/// <summary>
/// Use on a static and parameterless method to run just before the editor unloads the ALC for a reload.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class OnAssemblyUnloadAttribute : ScriptLifecycleAttribute { }

/// <summary>
/// Use on a static and parameterless method to run right after the new script assemblies are loaded
/// during a reload (the counterpart to <see cref="OnAssemblyUnloadAttribute"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class OnAssemblyLoadAttribute : ScriptLifecycleAttribute { }

/// <summary>
/// Use on a static, parameterless method to run when script compilation begins (before the build
/// kicks off).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class OnScriptCompileAttribute : ScriptLifecycleAttribute { }
