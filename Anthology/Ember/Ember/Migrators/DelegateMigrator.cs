using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Ember;

/// <summary>
/// Rebuilds a delegate against the current assembly. A named method remaps its method and target; a lambda or
/// local function is matched by the ordinal of the method it was written inside; a multicast recombines its
/// migrated entries. One that genuinely cannot be rebuilt is reported rather than quietly forgotten.
/// </summary>
public sealed class DelegateMigrator : IValueMigrator
{
    public bool Handles(Type type) => typeof(Delegate).IsAssignableFrom(type);

    public MigrationPlan Plan(Type type, PlanContext context)
    {
        var resolution = context.Types.Resolve(type);
        if (resolution.IsRemoved) return MigrationPlan.Dropped;

        return new DelegatePlan(resolution.Target!, context.Options.BrokenDelegates);
    }

    private sealed class DelegatePlan : MigrationPlan
    {
        private readonly Type _target;
        private readonly BrokenDelegatePolicy _policy;

        public DelegatePlan(Type target, BrokenDelegatePolicy policy)
        {
            _target = target;
            _policy = policy;
        }

        // A delegate is immutable, so there is no in place upgrade. Everything happens during allocation.
        public override bool NeedsFill => false;

        public override Allocation Allocate(object source, MigrationContext context)
        {
            var previous = (Delegate)source;

            var invocations = previous.GetInvocationList();
            return invocations.Length > 1
                ? Multicast(previous, invocations, context)
                : Single(previous, context);
        }

        private Allocation Multicast(Delegate previous, Delegate[] invocations, MigrationContext context)
        {
            var migrated = new List<Delegate>(invocations.Length);

            foreach (var entry in invocations)
            {
                if (context.Map(entry) is not Delegate rebuilt) continue;

                // A stand-in throws when invoked, and a multicast invokes its entries in order, so keeping one
                // would stop every subscriber after it from ever running. Broken entries always leave the list,
                // whatever the policy says about a delegate held on its own.
                if (BrokenDelegate.IsBroken(rebuilt, out _, out _)) continue;

                migrated.Add(rebuilt);
            }

            int dropped = invocations.Length - migrated.Count;
            if (dropped > 0)
                context.Report(ReloadCode.MulticastEntriesDropped, ReloadSeverity.Warning,
                    $"{dropped} subscriber(s) could not be rebuilt and were removed so the remaining {migrated.Count} still fire.",
                    previous.GetType().FullName);

            if (migrated.Count == 0) return Allocation.Drop;

            var combined = Delegate.Combine(migrated.ToArray());
            return combined == null ? Allocation.Drop : Allocation.Replace(combined);
        }

        private Allocation Single(Delegate previous, MigrationContext context)
        {
            // A stand-in from an earlier reload keeps its original reason rather than reporting itself.
            if (BrokenDelegate.IsBroken(previous, out var existingReason, out var existingMethod))
                return Break(previous, existingReason, existingMethod, context, report: false);

            var method = previous.Method;
            if (method.DeclaringType == null)
                return Break(previous, BrokenDelegateReason.NoDeclaringType, method.Name, context);

            string describe = $"{method.DeclaringType.Name}.{method.Name}";
            var previousTarget = previous.Target;

            var current = context.Members.Resolve(method) as MethodInfo;
            bool synthetic = LambdaMatcher.IsSynthetic(method);

            if (current == null)
                return Break(previous, synthetic ? BrokenDelegateReason.NoLambdaMatch : BrokenDelegateReason.NoStaticMatch,
                    describe, context);

            object? currentTarget;

            if (synthetic && context.Types.Resolve(method.DeclaringType).IsSubstituted)
            {
                if (!TryRetarget(method, current, previousTarget, context, out currentTarget))
                    return Break(previous, BrokenDelegateReason.NoRetroactiveCapture, describe, context);
            }
            else
            {
                currentTarget = context.Map(previousTarget);
            }

            if (ReferenceEquals(current, method) && ReferenceEquals(currentTarget, previousTarget))
            {
                context.CountDelegate(rebuilt: true);
                return Allocation.Preserve(previous);
            }

            if (Validate(current, previousTarget, currentTarget) is { } failure)
                return Break(previous, failure, describe, context);

            try
            {
                var rebuilt = currentTarget == null
                    ? current.CreateDelegate(_target)
                    : current.CreateDelegate(_target, currentTarget);

                context.CountDelegate(rebuilt: true);
                return Allocation.Replace(rebuilt);
            }
            catch (ArgumentException)
            {
                return Break(previous, BrokenDelegateReason.SignatureChanged, describe, context);
            }
        }

        private static BrokenDelegateReason? Validate(MethodInfo current, object? previousTarget, object? currentTarget)
        {
            if (current.IsStatic)
                return currentTarget != null ? BrokenDelegateReason.StaticWithTarget : null;

            // A null target on an instance method is an open delegate, where the instance is passed as the
            // first argument. That is valid, so only a target that failed to migrate is a problem.
            if (previousTarget != null && currentTarget == null)
                return BrokenDelegateReason.TargetTypeRemoved;

            if (currentTarget != null && !current.DeclaringType!.IsAssignableFrom(currentTarget.GetType()))
                return BrokenDelegateReason.TargetMismatch;

            return null;
        }

        private static bool TryRetarget(MethodInfo previousMethod, MethodInfo currentMethod, object? previousTarget,
            MigrationContext context, out object? currentTarget)
        {
            currentTarget = null;

            if (ReferenceEquals(previousMethod, currentMethod)
                && (previousTarget == null || !context.Types.Resolve(previousTarget.GetType()).IsSubstituted))
            {
                currentTarget = context.Map(previousTarget);
                return true;
            }

            var before = LambdaMatcher.Identify(previousMethod).Capture;
            var after = LambdaMatcher.Identify(currentMethod).Capture;

            switch (before, after)
            {
                case (CaptureMode.Unknown, _):
                case (_, CaptureMode.Unknown):
                    return false;

                // Captures nothing now, so the target is the cache singleton, or nothing for a static.
                case (_, CaptureMode.None):
                    currentTarget = CacheSingleton(currentMethod);
                    return true;

                // It captures something it did not before, and there is no value in the previous world for it.
                case (CaptureMode.None, _):
                    return false;

                case (CaptureMode.Instance, CaptureMode.Instance):
                case (CaptureMode.DisplayClass, CaptureMode.DisplayClass):
                    currentTarget = context.Map(previousTarget);
                    return true;

                // The closure collapsed to needing only the instance, which it still holds.
                case (CaptureMode.DisplayClass, CaptureMode.Instance):
                    var capturedThis = previousTarget == null ? null : CapturedThis(previousTarget);
                    if (capturedThis == null) return false;
                    currentTarget = context.Map(capturedThis);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>The singleton a non capturing lambda's cache class holds in a public static field.</summary>
        private static object? CacheSingleton(MethodInfo lambda)
        {
            if (lambda.IsStatic) return null;

            var declaring = lambda.DeclaringType!;
            foreach (var field in declaring.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.FieldType == declaring)
                    return field.GetValue(null);

            return null;
        }

        /// <summary>The captured instance a closure holds, when the lambda turned out to need only that.</summary>
        private static object? CapturedThis(object closure)
        {
            var closureType = closure.GetType();
            var instanceType = closureType.DeclaringType;

            foreach (var field in closureType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (field.FieldType == instanceType && field.Name.EndsWith("__this", StringComparison.Ordinal))
                    return field.GetValue(closure);

            return null;
        }

        private Allocation Break(Delegate previous, BrokenDelegateReason reason, string describe,
            MigrationContext context, bool report = true)
        {
            if (report)
            {
                context.CountDelegate(rebuilt: false);
                context.Report(ReloadCode.DelegateBroken, ReloadSeverity.Warning,
                    ReloadedDelegateException.Describe(reason), describe);
            }

            switch (_policy)
            {
                case BrokenDelegatePolicy.Preserve:
                    return Allocation.Preserve(previous);

                case BrokenDelegatePolicy.Throwing:
                    var thrower = BrokenDelegate.Create(_target, reason, describe);
                    if (thrower != null) return Allocation.Replace(thrower);
                    goto default;

                default:
                    return Allocation.Drop;
            }
        }

        public override string Describe() => $"delegate rebuilt as {_target.Name}, broken policy {_policy}";
    }
}
