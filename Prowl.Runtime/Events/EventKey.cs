// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Events {

/// <summary>
/// Marker type for source-generated event domains.
/// Declare <c>private static readonly EventKey _OnXxx</c> fields inside a class marked with
/// <see cref="EventDomainAttribute"/> to define events. The leading underscore is stripped
/// by the generator to produce the public event name. Optionally decorate each
/// field with <see cref="EventArgsAttribute"/> to specify the event's argument type
/// (defaults to <see cref="Unit"/> when omitted).
/// <para>
/// <b>Example:</b>
/// <code>
/// [EventDomain]
/// public static partial class MyEvents
/// {
///     [EventArgs(typeof(MyPayload))]
///     private static readonly EventKey _OnSomething = new();
/// }
/// </code>
/// The generator will emit the <c>OnSomething</c> event accessor property (for +=/Invoke) along with
/// per-event <c>InvokeOnSomething</c> / <c>GlobalInvokeOnSomething</c> convenience methods.
/// Full subscription with priority, tags, etc. is done via <c>OnSomething.Subscribe(...)</c>.
/// </para>
/// </summary>
public readonly struct EventKey;

}
