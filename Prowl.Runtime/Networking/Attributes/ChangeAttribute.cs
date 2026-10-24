// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Networking.Collections;

namespace Prowl.Runtime.Networking.Attributes;

/// <summary>
///     Calls a method of the component whenever a <see cref="SyncAttribute" /> property's value changes, as in
///     <c>[Sync, Change(nameof(OnHealthChanged))] public int Health { get; set; }</c>. The method takes the old
///     and new values, <c>void OnHealthChanged(int oldValue, int newValue)</c>, or nothing.
/// </summary>
/// <remarks>
///     It runs where the change happens: on the peer that set it, right away, and on every peer that receives
///     it, once the whole update it came in is applied, so it sees the other values that changed with it. It
///     doesn't run for values that come with a scene snapshot or spawn. Those are in place before the component
///     starts, so read them in <c>OnEnable</c> or <c>Start</c>. Setting the value it already has calls nothing. For
///     <see cref="NetList{T}" />, <see cref="NetDictionary{TKey,TValue}" /> and <see cref="NetSet{T}" />,
///     use their <c>Changed</c> event instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ChangeAttribute(string method) : Attribute
{
    /// <summary>The name of the method to call.</summary>
    public string Method { get; } = method;
}
