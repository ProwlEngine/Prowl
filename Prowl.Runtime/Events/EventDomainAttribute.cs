// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Runtime.Events {

/// <summary>
/// Marks a <c>partial class</c> as an event domain.
/// The source generator will produce a backing enum, an <see cref="EventManager{T}"/>,
/// event accessor properties, and typed <c>Invoke*</c> / <c>GlobalInvoke*</c> convenience methods
/// for each <see cref="EventKey"/> field. Advanced subscriptions are available on the accessors
/// via <c>.Subscribe(...)</c> etc.
/// <para>
/// <b>Static domains</b> (the class is <c>static</c>) produce a single shared
/// <see cref="EventManager{T}"/> and static convenience methods — ideal for
/// global engine events.
/// </para>
/// <para>
/// <b>Instance domains</b> (the class is <em>not</em> <c>static</c>) produce a
/// per-instance <see cref="EventManager{T}"/> and instance convenience methods —
/// ideal for component-level or per-object events. Dispose <see cref="EventManager{T}"/>
/// via <c>Manager.Dispose()</c> when the owner is no longer needed.
/// <c>GlobalInvoke</c> methods remain static and broadcast across all global managers.
/// </para>
/// <para>
/// <b>Static example:</b>
/// <code>
/// [EventDomain(Global = true)]
/// public static partial class MyEvents
/// {
///     [EventArgs(typeof(MyArgs))]
///     private static readonly EventKey _OnSomething = new();
///
///     public readonly struct MyArgs { public int Value; }
/// }
/// // Usage: MyEvents.InvokeOnSomething(args);
/// </code>
/// </para>
/// <para>
/// <b>Instance example:</b>
/// <code>
/// [EventDomain]
/// public partial class ActorEvents
/// {
///     [EventArgs(typeof(DamageArgs))]
///     private static readonly EventKey _OnDamaged = new();
///
///     public readonly record struct DamageArgs(float Amount);
/// }
/// // Usage: actor.InvokeOnDamaged(new DamageArgs(10));
/// </code>
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventDomainAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, the generated <see cref="EventManager{T}"/> is created
    /// with <c>global: true</c>, making it participate in
    /// <see cref="EventManager{T}.GlobalInvokeEvent"/> calls.
    /// </summary>
    public bool Global { get; set; }
}

}
