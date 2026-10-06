using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.PaperUI
{
    /// <summary>
    /// A range of viewport sizes, matched against <see cref="Paper.ViewportWidth"/> and
    /// <see cref="Paper.ViewportHeight"/>. Every bound is inclusive and an unset bound is open.
    /// </summary>
    public struct Breakpoint : IEquatable<Breakpoint>
    {
        public float MinWidth;
        public float MaxWidth;
        public float MinHeight;
        public float MaxHeight;
        /// <summary>Width divided by height.</summary>
        public float MinAspect;
        /// <summary>Width divided by height.</summary>
        public float MaxAspect;

        public static Breakpoint Any => new Breakpoint(0, float.MaxValue);

        public Breakpoint(float minWidth, float maxWidth, float minHeight = 0, float maxHeight = float.MaxValue, float minAspect = 0, float maxAspect = float.MaxValue)
        {
            MinWidth = minWidth;
            MaxWidth = maxWidth;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
            MinAspect = minAspect;
            MaxAspect = maxAspect;
        }

        public static Breakpoint Width(float min, float max = float.MaxValue) => new Breakpoint(min, max);
        public static Breakpoint Height(float min, float max = float.MaxValue) => new Breakpoint(0, float.MaxValue, min, max);
        public static Breakpoint Aspect(float min, float max = float.MaxValue) => new Breakpoint(0, float.MaxValue, 0, float.MaxValue, min, max);
        public static Breakpoint Portrait => Aspect(0, 0.9999f);
        public static Breakpoint Landscape => Aspect(1f);

        public readonly bool Matches(float width, float height)
        {
            if (width < MinWidth || width > MaxWidth) return false;
            if (height < MinHeight || height > MaxHeight) return false;
            float aspect = height > 0 ? width / height : 0;
            return aspect >= MinAspect && aspect <= MaxAspect;
        }

        public readonly bool Equals(Breakpoint other) =>
            MinWidth == other.MinWidth && MaxWidth == other.MaxWidth &&
            MinHeight == other.MinHeight && MaxHeight == other.MaxHeight &&
            MinAspect == other.MinAspect && MaxAspect == other.MaxAspect;

        public override readonly bool Equals(object? obj) => obj is Breakpoint other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(MinWidth, MaxWidth, MinHeight, MaxHeight, MinAspect, MaxAspect);
    }

    public partial class Paper
    {
        private readonly Stack<Float2> _viewportStack = new Stack<Float2>();
        private readonly Dictionary<string, Breakpoint> _breakpoints = new Dictionary<string, Breakpoint>();

        /// <summary>
        /// The width responsive queries test against. This is the screen width unless a
        /// <see cref="PushViewport"/> scope says otherwise.
        /// </summary>
        public float ViewportWidth => _viewportStack.Count > 0 ? _viewportStack.Peek().X : Width;

        /// <summary>The height responsive queries test against. See <see cref="ViewportWidth"/>.</summary>
        public float ViewportHeight => _viewportStack.Count > 0 ? _viewportStack.Peek().Y : Height;

        /// <summary>Viewport width divided by height.</summary>
        public float ViewportAspect => ViewportHeight > 0 ? ViewportWidth / ViewportHeight : 0;

        public bool IsPortrait => ViewportHeight > ViewportWidth;
        public bool IsLandscape => !IsPortrait;

        /// <summary>Every named breakpoint, as defined with <see cref="DefineBreakpoint"/>.</summary>
        public IReadOnlyDictionary<string, Breakpoint> Breakpoints => _breakpoints;

        /// <summary>
        /// Makes everything built inside the scope answer responsive queries as if the screen were
        /// <paramref name="width"/> by <paramref name="height"/>. Useful for split screen, a UI drawn
        /// on an in game monitor, or previewing several resolutions side by side.
        /// </summary>
        public ViewportScope PushViewport(float width, float height)
        {
            _viewportStack.Push(new Float2(width, height));
            return new ViewportScope(this);
        }

        public void PopViewport()
        {
            if (_viewportStack.Count == 0)
                throw new InvalidOperationException("No viewport to pop.");
            _viewportStack.Pop();
        }

        /// <summary>Registers or replaces a named breakpoint, such as "Phone" or "Ultrawide".</summary>
        public void DefineBreakpoint(string name, Breakpoint breakpoint)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Breakpoint name cannot be empty.", nameof(name));
            _breakpoints[name] = breakpoint;
        }

        public bool RemoveBreakpoint(string name) => _breakpoints.Remove(name);

        /// <summary>True when the named breakpoint matches the current viewport. Throws for a name that was never defined.</summary>
        public bool IsBreakpoint(string name)
        {
            if (!_breakpoints.TryGetValue(name, out var breakpoint))
                throw new KeyNotFoundException($"No breakpoint named '{name}'. Define it with DefineBreakpoint first.");
            return breakpoint.Matches(ViewportWidth, ViewportHeight);
        }

        public bool ViewportMatches(in Breakpoint breakpoint) => breakpoint.Matches(ViewportWidth, ViewportHeight);

        public readonly struct ViewportScope : IDisposable
        {
            private readonly Paper _paper;
            internal ViewportScope(Paper paper) => _paper = paper;
            public void Dispose() => _paper?.PopViewport();
        }
    }
}
