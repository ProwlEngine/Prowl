#if NETSTANDARD2_1
// Polyfills for net5+/net6+ APIs that netstandard2.1 lacks (and PolySharp does not provide).

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresUnreferencedCodeAttribute : System.Attribute
    {
        public RequiresUnreferencedCodeAttribute(string message) => Message = message;
        public string Message { get; }
        public string? Url { get; set; }
    }
}

namespace System.Collections.Generic
{
    // net5+; netstandard2.1 lacks it.
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object?>, System.Collections.IEqualityComparer
    {
        private ReferenceEqualityComparer() { }
        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();
        public bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object? obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        bool System.Collections.IEqualityComparer.Equals(object? x, object? y) => ReferenceEquals(x, y);
        int System.Collections.IEqualityComparer.GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

namespace Prowl.Echo
{
    internal static class TypePolyfills
    {
        // Type.IsAssignableTo is net5+; netstandard2.1 only has the reversed IsAssignableFrom.
        public static bool IsAssignableTo(this System.Type from, System.Type? to)
            => to is not null && to.IsAssignableFrom(from);
    }
}
#endif
