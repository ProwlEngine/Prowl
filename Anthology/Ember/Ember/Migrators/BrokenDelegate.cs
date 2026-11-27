using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Prowl.Ember;

/// <summary>Why a delegate could not be rebuilt against the current assembly.</summary>
public enum BrokenDelegateReason
{
    /// <summary>The method had no declaring type to search.</summary>
    NoDeclaringType,

    /// <summary>A named method had no counterpart after the reload.</summary>
    NoStaticMatch,

    /// <summary>A lambda or local function had no counterpart after the reload.</summary>
    NoLambdaMatch,

    /// <summary>The lambda now captures something it did not before, and there is no value to give it.</summary>
    NoRetroactiveCapture,

    /// <summary>The method exists but no longer fits the delegate type.</summary>
    SignatureChanged,

    /// <summary>The instance the delegate was bound to had its type removed.</summary>
    TargetTypeRemoved,

    /// <summary>The migrated target is not an instance of the method's declaring type.</summary>
    TargetMismatch,

    /// <summary>A static method was left holding a target.</summary>
    StaticWithTarget,
}

/// <summary>
/// Thrown by a delegate that hot reload could not rebuild, when
/// <see cref="BrokenDelegatePolicy.Throwing"/> is in effect. Loud at the exact call site, rather than a null
/// reference somewhere unrelated.
/// </summary>
public sealed class ReloadedDelegateException : Exception
{
    public ReloadedDelegateException(BrokenDelegateReason reason, string previousMethod)
        : base($"This delegate could not survive hot reload: {Describe(reason)} ({previousMethod})")
    {
        Reason = reason;
        PreviousMethod = previousMethod;
    }

    public BrokenDelegateReason Reason { get; }
    public string PreviousMethod { get; }

    public static string Describe(BrokenDelegateReason reason) => reason switch
    {
        BrokenDelegateReason.NoDeclaringType => "the method had no declaring type",
        BrokenDelegateReason.NoStaticMatch => "no matching method exists after the reload",
        BrokenDelegateReason.NoLambdaMatch => "no matching lambda exists after the reload",
        BrokenDelegateReason.NoRetroactiveCapture => "it now captures something that did not exist before",
        BrokenDelegateReason.SignatureChanged => "its signature changed",
        BrokenDelegateReason.TargetTypeRemoved => "the instance it was bound to had its type removed",
        BrokenDelegateReason.TargetMismatch => "the migrated target is not of the expected type",
        BrokenDelegateReason.StaticWithTarget => "a static method was left holding a target",
        _ => "it could not be rebuilt",
    };
}

/// <summary>
/// Builds the stand-in delegate for <see cref="BrokenDelegatePolicy.Throwing"/>, and recognises one it built
/// earlier so a second reload preserves the original reason rather than reporting the stand-in itself.
/// </summary>
internal static class BrokenDelegate
{
    private sealed class Thunk
    {
        public BrokenDelegateReason Reason;
        public string PreviousMethod = string.Empty;

        // Returns the exception rather than throwing it, so the emitted body can end in "throw". A body that
        // called a void method and then returned would be invalid IL for any delegate with a return value.
        public Exception Build() => new ReloadedDelegateException(Reason, PreviousMethod);
    }

    private static readonly MethodInfo s_build =
        typeof(Thunk).GetMethod(nameof(Thunk.Build), BindingFlags.Instance | BindingFlags.Public)!;

    public static bool IsBroken(Delegate value, out BrokenDelegateReason reason, out string previousMethod)
    {
        if (value.Target is Thunk thunk)
        {
            reason = thunk.Reason;
            previousMethod = thunk.PreviousMethod;
            return true;
        }

        reason = default;
        previousMethod = string.Empty;
        return false;
    }

    /// <summary>
    /// A delegate of the right shape whose body throws. The signature is taken from the delegate type's own
    /// Invoke method, not from the previous delegate's target method: a stand-in built by an earlier reload
    /// carries its thunk as a hidden first parameter, and rebuilding from that would append a second one.
    /// </summary>
    /// <remarks>
    /// The reason travels on the target object rather than being encoded into a generated method name, so
    /// recovering it on the next reload is a field read.
    /// </remarks>
    public static Delegate? Create(Type delegateType, BrokenDelegateReason reason, string previousMethod)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported) return null;
        if (delegateType.GetMethod("Invoke") is not { } invoke) return null;

        var parameterTypes = invoke.GetParameters().Select(x => x.ParameterType).ToArray();
        var thunk = new Thunk { Reason = reason, PreviousMethod = previousMethod };

        // The thunk is passed as the delegate's target, so it arrives as the hidden first argument.
        var method = new DynamicMethod(
            "<broken>__reload",
            invoke.ReturnType,
            new[] { typeof(Thunk) }.Concat(parameterTypes).ToArray(),
            typeof(Thunk),
            skipVisibility: true);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, s_build);
        il.Emit(OpCodes.Throw);

        try
        {
            return method.CreateDelegate(delegateType, thunk);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
