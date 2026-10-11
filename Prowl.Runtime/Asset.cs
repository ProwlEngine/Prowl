// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Ember;

namespace Prowl.Runtime;

// Loaded first, so a runtime asset made any way at all starts loaded.
public enum AssetState { Loaded, Unloaded, Loading, Missing, Failed }

public enum ReloadReason { Load, Reimport, Save, Revert, Undo }

/// <summary>Keeps a field from holding the asset it points at, so the asset can unload while the field still references it.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class NotHeldAttribute : Attribute { }

/// <summary>
/// Makes the asset walk follow a static field, so the assets it holds stay loaded. The walk sees no other static.
/// Reading the field runs its class's static constructor if nothing has yet.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HeldStaticAttribute : Attribute { }

/// <summary>
/// Something the asset database can own. A database asset has one object per GUID for the whole session: unloading
/// frees only its payload, and loading, reimporting and reverting all fill the same object again. A runtime asset
/// (made with new, or cloned) has no GUID, is always loaded and is owned by whoever made it.
/// </summary>
public abstract class Asset : EngineObject
{
    private Guid _assetId;
    private string _assetPath = string.Empty;
    private int _state;
    private int _contentVersion;

    // Database bookkeeping, owned by AssetDatabase.
    internal bool Registered;
    internal int LoadCount;
    internal bool WasEvicted;
    internal int MarkEpoch;
    internal int UnreachedWalks;
    internal long LastReached;
    internal int ReadGeneration;
    internal bool ReportedEmpty;

    protected Asset() { }

    protected Asset(string name) : base(name) { }

    public Guid AssetID => _assetId;
    public string AssetPath => _assetPath;
    public AssetState State => (AssetState)Volatile.Read(ref _state);

    /// <summary>Moves on every load and refill, for anything that derives data from this asset.</summary>
    public int ContentVersion => _contentVersion;

    public bool IsFromDatabase => _assetId != Guid.Empty;
    public bool IsLoaded => State == AssetState.Loaded;
    public bool IsMissing => State == AssetState.Missing;

    /// <summary>Blocks until loaded. Does nothing for a runtime asset.</summary>
    public void Load() => AssetLoader.LoadBlocking(this);

    public Task LoadAsync(CancellationToken cancel = default) => AssetLoader.LoadAsync(this, cancel);

    /// <summary>True once each time <see cref="ContentVersion"/> moves past what the caller last saw.</summary>
    public bool HasChanged(ref int seenVersion)
    {
        if (seenVersion == _contentVersion) return false;
        seenVersion = _contentVersion;
        return true;
    }


    /// <summary>
    /// Call at the top of every accessor of the payload. Loads the asset when it is not loaded, and returns at
    /// once with an empty payload for one that is missing or failed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void EnsureLoaded([CallerMemberName] string? member = null)
    {
        if (_state != (int)AssetState.Loaded)
            LoadOnAccess(member);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LoadOnAccess(string? member)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(Name, $"'{Name}' ({GetType().Name}) was already disposed when '{member}' was accessed.");

        switch (State)
        {
            case AssetState.Missing:
            case AssetState.Failed:
                if (!ReportedEmpty)
                {
                    ReportedEmpty = true;
                    Debug.LogWarning($"{GetType().Name} '{Name}' ({_assetId}) is {State.ToString().ToLowerInvariant()}, so '{member}' reads it as empty.");
                }
                return;
        }

        if (AssetLoader.IsLoaderThread)
        {
            Debug.LogError($"{GetType().Name} '{Name}' was read by '{member}' while another asset was loading. Loading never reads other assets.");
            return;
        }

        if (WasEvicted && LoadCount > 0)
            Debug.LogWarningOnce($"Asset.Reload.{_assetId}",
                $"{GetType().Name} '{Name}' was read by '{member}' after it was unloaded. Keep it in a field or hold it.\n{Environment.StackTrace}");

        AssetLoader.LoadBlocking(this);
    }

    /// <summary>
    /// Frees native resources and anything derived from the payload. Runs when the asset unloads, before a refill
    /// and when a runtime asset is disposed. The database resets every field to its shell value afterwards.
    /// </summary>
    protected virtual void OnUnload() { }

    /// <summary>Moves the payload of a freshly read copy into this object. The copy owns nothing afterwards.</summary>
    protected virtual void TakeContent(Asset staging) => AssetContent.Move(staging, this);

    /// <summary>Roughly how many bytes the loaded payload keeps alive, for the memory budget.</summary>
    protected internal virtual long EstimateBytes() => 0;

    protected sealed override void OnDispose()
    {
        OnUnload();
        _state = (int)AssetState.Unloaded;
    }

    internal void SetIdentity(Guid assetId, string assetPath)
    {
        _assetId = assetId;
        _assetPath = assetPath ?? string.Empty;
    }

    internal void SetPath(string assetPath) => _assetPath = assetPath ?? string.Empty;

    internal void SetState(AssetState state) => Volatile.Write(ref _state, (int)state);

    internal bool TrySetState(AssetState from, AssetState to)
        => Interlocked.CompareExchange(ref _state, (int)to, (int)from) == (int)from;

    internal void Fill(Asset staging)
    {
        FreeNative();
        TakeContent(staging);
        GC.SuppressFinalize(staging);
        _contentVersion++;
        LoadCount++;
        ReportedEmpty = false;
        SetState(AssetState.Loaded);
    }

    /// <summary>Frees the payload, leaving every field as a fresh shell has it until the next load fills it.</summary>
    internal void Unload()
    {
        FreeNative();
        AssetContent.Reset(this);
    }

    private void FreeNative()
    {
        try { OnUnload(); }
        catch (Exception ex) { Debug.LogError($"Unloading {GetType().Name} '{Name}' threw: {ex}"); }
    }

    /// <summary>Frees the payload and gives the asset a fresh shell's values, which is what a missing asset reads as.</summary>
    internal void MarkMissing() => MarkEmpty(AssetState.Missing);

    internal void MarkFailed() => MarkEmpty(AssetState.Failed);

    private void MarkEmpty(AssetState state)
    {
        FreeNative();
        AssetContent.Reset(this);
        _contentVersion++;
        SetState(state);
    }
}

/// <summary>A field of an asset that is not its content, such as a change counter, so loads, unloads and refills leave it alone.</summary>
[AttributeUsage(AttributeTargets.Field)]
internal sealed class NotContentAttribute : Attribute { }

/// <summary>Moves and resets an asset's payload fields: every instance field declared below <see cref="Asset"/>, except events.</summary>
internal static class AssetContent
{
    private static readonly ReloadCache<Type, FieldInfo[]> s_fields = new(Collect);

    /// <summary>Moves every payload field, leaving the source's references empty so it owns nothing.</summary>
    public static void Move(Asset from, Asset to)
    {
        if (from.GetType() != to.GetType())
            throw new InvalidOperationException($"Cannot fill a {to.GetType().Name} from a {from.GetType().Name}.");

        foreach (FieldInfo field in FieldsOf(to.GetType()))
        {
            field.SetValue(to, field.GetValue(from));
            if (!field.FieldType.IsValueType) field.SetValue(from, null);
        }
    }

    /// <summary>Gives every payload field the value a fresh shell has, so an asset that is not loaded reads as empty.</summary>
    public static void Reset(Asset asset)
    {
        Asset shell = AssetDatabase.CreateShell(asset.GetType());
        Move(shell, asset);
        GC.SuppressFinalize(shell);
    }

    public static FieldInfo[] FieldsOf(Type type) => s_fields[type];

    [UnconditionalSuppressMessage("Trimming", "IL2070:DynamicallyAccessedMembers",
        Justification = "Serialized types keep their fields, which the application's trim configuration must preserve.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Serialized types keep their fields, which the application's trim configuration must preserve.")]
    private static FieldInfo[] Collect(Type type)
    {
        var fields = new List<FieldInfo>();
        for (Type? current = type; current != null && current != typeof(Asset); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType) && !field.IsDefined(typeof(NotContentAttribute)))
                    fields.Add(field);
        return fields.ToArray();
    }
}
