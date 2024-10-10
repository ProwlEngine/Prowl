// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;


[FilePath("ProjectAssemblies.projsetting", FilePathAttribute.Location.Setting)]
public class ProjectAssemblyReferences : ScriptableSingleton<ProjectAssemblyReferences>
{
    [SerializeField, HideInInspector]
    private List<string> _assemblyNames = [];

    public IEnumerable<string> AssemblyNames => _assemblyNames;


    public void AddAssembly(string name)
    {
        if (!_assemblyNames.Contains(name))
            _assemblyNames.Add(name);

        Save();
    }

    public void RemoveAssembly(string name)
    {
        _assemblyNames.Remove(name);
        Save();
    }

}


public static class AssemblyManager
{
#if DEBUG
    private static bool verboseLoadMessages = true;
#else
    private static bool verboseLoadMessages = false;
#endif

    private static ExternalAssemblyLoadContext? _externalAssemblyLoadContext;
    private static List<(WeakReference, MulticastDelegate)> _unloadLifetimeDelegates = new();
    private static List<Action> _unloadDelegates = new();

    public static IEnumerable<Assembly> ExternalAssemblies => _externalAssemblyLoadContext?.Assemblies ?? [];


    public static void Initialize()
    {
        OnAssemblyUnloadAttribute.FindAll();
        OnAssemblyLoadAttribute.FindAll();
    }


    public static void LoadProjectAssemblies()
    {
        foreach (string assemblyName in ProjectAssemblyReferences.Instance.AssemblyNames)
            Assembly.Load(assemblyName);
    }


    public static Assembly? LoadExternalAssembly(string assemblyPath, bool isDependency)
    {
        try
        {
            _externalAssemblyLoadContext ??= new ExternalAssemblyLoadContext();
            Assembly asm = _externalAssemblyLoadContext.LoadFromAssemblyPath(assemblyPath);

            if (isDependency)
                _externalAssemblyLoadContext.AddDependency(assemblyPath);

            if (verboseLoadMessages)
                Debug.LogSuccess($"Successfully loaded external assembly from {assemblyPath}");

            return asm;
        }
        catch (Exception ex)
        {
            Debug.LogException(new AssemblyLoadException($"Failed to load External Assembly: {assemblyPath}", ex));
            return null;
        }
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Unload(Action? onFail = null)
    {
        if (_externalAssemblyLoadContext is null)
            return;

        OnAssemblyUnloadAttribute.Invoke();

        InvokeUnloadDelegate();

        UnloadInternal(out WeakReference externalAssemblyLoadContextRef);

        const int MAX_GC_ATTEMPTS = 10;

        for (int i = 0; externalAssemblyLoadContextRef.IsAlive; i++)
        {
            if (i >= MAX_GC_ATTEMPTS)
            {
                Debug.LogException(new AssemblyUnloadException($"Failed to unload external assemblies."));

                onFail?.Invoke();
                _externalAssemblyLoadContext = externalAssemblyLoadContextRef.Target as ExternalAssemblyLoadContext;

                return;
            }

            foreach (Assembly assembly in ExternalAssemblies)
                TypeDescriptor.Refresh(assembly);

            if (verboseLoadMessages)
                Debug.Log($"GC Attempt ({i + 1}/{MAX_GC_ATTEMPTS})...");

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (verboseLoadMessages)
            Debug.LogSuccess($"Successfully unloaded external assemblies.");
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadInternal(out WeakReference externalAssemblyLoadContextRef)
    {
        foreach (Assembly assembly in ExternalAssemblies)
            Debug.Log($"Unloading external assembly from: '{assembly.Location}'...");

        // crashes after recovery and attempted unloading for the second time
        if (_externalAssemblyLoadContext != null)
            _externalAssemblyLoadContext.Unload();

        externalAssemblyLoadContextRef = new WeakReference(_externalAssemblyLoadContext);
        _externalAssemblyLoadContext = null;
    }


    public static void AddUnloadTask(Action onUnload)
    {
        _unloadDelegates.Add(onUnload);
    }


    public static void AddUnloadTaskWithLifetime<T>(T lifetimeDependency, Action<T> onUnload)
    {
        _unloadLifetimeDelegates.Add((new WeakReference(lifetimeDependency), onUnload));
    }


    private static void InvokeUnloadDelegate()
    {
        foreach ((WeakReference lifetimeDependency, MulticastDelegate unloadDelegate) in _unloadLifetimeDelegates)
        {
            if (!lifetimeDependency.IsAlive)
                continue;

            try
            {
                _ = unloadDelegate.DynamicInvoke([lifetimeDependency.Target])!;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        _unloadLifetimeDelegates.Clear();

        foreach (Action unloadDelegate in _unloadDelegates)
        {
            try
            {
                unloadDelegate.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        _unloadDelegates.Clear();
    }


    public static void Dispose()
    {
        UnloadInternal(out WeakReference _);
    }


    private class ExternalAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly List<AssemblyDependencyResolver> _assemblyDependencyResolvers;


        public ExternalAssemblyLoadContext() : base(true)
        {
            _assemblyDependencyResolvers = new List<AssemblyDependencyResolver>();
        }


        public void AddDependency(string assemblyPath)
        {
            _assemblyDependencyResolvers.Add(new AssemblyDependencyResolver(assemblyPath));
        }


        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (AssemblyDependencyResolver assemblyDependencyResolver in _assemblyDependencyResolvers)
            {
                string? resolvedPath = assemblyDependencyResolver.ResolveAssemblyToPath(assemblyName);

                if (resolvedPath != null)
                    return LoadFromAssemblyPath(resolvedPath);
            }

            return null;
        }
    }
}


public class AssemblyLoadException : Exception
{
    public AssemblyLoadException(string message) : base(message) { }
    public AssemblyLoadException(string message, Exception? innerException) : base(message, innerException) { }
}


public class AssemblyUnloadException : Exception
{
    public AssemblyUnloadException(string message) : base(message) { }
    public AssemblyUnloadException(string message, Exception? innerException) : base(message, innerException) { }
}
