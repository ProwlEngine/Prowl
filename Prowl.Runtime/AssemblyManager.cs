// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime;

public static class AssemblyManager
{
    private static ExternalAssemblyLoadContext? _externalAssemblyLoadContext;
    private static List<(WeakReference lifetimeDependency, MulticastDelegate @delegate)> _unloadLifetimeDelegates = new();
    private static List<Func<bool>> _unloadDelegates = new();


    public static IEnumerable<Assembly> ExternalAssemblies
    {
        get
        {
            if (_externalAssemblyLoadContext is null)
                yield break;
            foreach (Assembly assembly in _externalAssemblyLoadContext.Assemblies)
            {
                yield return assembly;
            }
        }
    }


    public static void Initialize()
    {
        OnAssemblyUnloadAttribute.FindAll();
        OnAssemblyLoadAttribute.FindAll();
    }


    public static void LoadExternalAssembly(string assemblyPath, bool isDependency)
    {
        try
        {
            _externalAssemblyLoadContext ??= new ExternalAssemblyLoadContext();
            Assembly asm = _externalAssemblyLoadContext.LoadFromAssemblyPath(assemblyPath);

            if (isDependency)
                _externalAssemblyLoadContext.AddDependency(assemblyPath);

            Debug.LogSuccess($"Successfully loaded external assembly from {assemblyPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load External Assembly: {assemblyPath} Exception: " + ex.Message);
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
                Debug.LogError($"Failed to unload external assemblies.");
                onFail?.Invoke();
                _externalAssemblyLoadContext = externalAssemblyLoadContextRef.Target as ExternalAssemblyLoadContext;

                return;
            }

            foreach (Assembly assembly in ExternalAssemblies)
                TypeDescriptor.Refresh(assembly);

            Debug.Log($"GC Attempt ({i + 1}/{MAX_GC_ATTEMPTS})...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Debug.LogSuccess($"Successfully unloaded external assemblies.");
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadInternal(out WeakReference externalAssemblyLoadContextRef)
    {
        foreach (Assembly assembly in ExternalAssemblies)
        {
            Debug.Log($"Unloading external assembly from: '{assembly.Location}'...");
        }

        // crashes after recovery and attempted unloading for the second time
        if (_externalAssemblyLoadContext != null)
            _externalAssemblyLoadContext.Unload();

        externalAssemblyLoadContextRef = new WeakReference(_externalAssemblyLoadContext);
        _externalAssemblyLoadContext = null;
    }


    public static void AddUnloadTask(Func<bool> @delegate)
    {
        _unloadDelegates.Add(@delegate);
    }


    public static void AddUnloadTaskWithLifetime<T>(T lifetimeDependency, Func<T, bool> @delegate)
    {
        _unloadLifetimeDelegates.Add((new WeakReference(lifetimeDependency), @delegate));
    }


    private static void InvokeUnloadDelegate()
    {
        foreach ((WeakReference lifetimeDependency, MulticastDelegate @delegate) in _unloadLifetimeDelegates)
        {
            if (!lifetimeDependency.IsAlive)
                continue;

            bool result = (bool)@delegate.DynamicInvoke([lifetimeDependency.Target])!;
            if (!result)
                Debug.LogError("some unload delegate returned with failure");
        }
        _unloadLifetimeDelegates = new();

        foreach (Func<bool> @delegate in _unloadDelegates)
        {
            bool result = @delegate.Invoke();
            if (!result)
                Debug.LogError("some unload delegate returned with failure");
        }
        _unloadDelegates = new();
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
                if (assemblyDependencyResolver.ResolveAssemblyToPath(assemblyName) is { } resolvedAssemblyPath)
                    return LoadFromAssemblyPath(resolvedAssemblyPath);
            }
            return null;
        }

    }

}
