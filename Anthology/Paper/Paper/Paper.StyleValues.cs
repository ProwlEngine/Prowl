using System;
using System.Runtime.CompilerServices;

using Prowl.Scribe;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Utilities;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Typed, allocation-free storage for an element's resolved current style values. Replaces the
    /// per-property boxed dictionary that layout and rendering read every frame: values live in typed
    /// fields, "is this explicitly set" is a single <see cref="ulong"/> bitmask (GuiProp has fewer
    /// than 64 members), and unset fields already hold their default so a read is just a field access.
    /// </summary>
    internal struct StyleValues
    {
        private ulong _set;

        // One field per GuiProp. An ElementStyle starts each field at its default (see CreateDefaults),
        // so a read is just a field access - no separate default lookup.
        public Color BackgroundColor;
        public Gradient BackgroundGradient;
        public Color BorderColor;
        public float BorderWidth;
        public Float4 Rounded;
        public BoxShadow BoxShadow;
        public float BackdropBlur;
        public float AspectRatio;
        public UnitValue Width, Height, MinWidth, MaxWidth, MinHeight, MaxHeight;
        public UnitValue Left, Right, Top, Bottom;
        public UnitValue MinLeft, MaxLeft, MinRight, MaxRight, MinTop, MaxTop, MinBottom, MaxBottom;
        public UnitValue ChildLeft, ChildRight, ChildTop, ChildBottom, RowBetween, ColBetween;
        public UnitValue PaddingLeft, PaddingRight, PaddingTop, PaddingBottom;
        public float TranslateX, TranslateY, ScaleX, ScaleY, Rotate, OriginX, OriginY, SkewX, SkewY;
        public Transform2D Transform;
        public object BackgroundImage;
        public Color TextColor;
        public float WordSpacing, LetterSpacing, LineHeight;
        public int TabSize;
        public float FontSize;
        public FontQuality TextQuality;

        public readonly bool Has(GuiProp p) => (_set & (1UL << (int)p)) != 0;

        #region Typed access

        /// <summary>The kind of value a property holds. Every property of one kind is stored,
        /// compared and interpolated the same way, so the switches below are per kind rather than
        /// per property.</summary>
        internal enum PropKind { Float, Unit, Color, Float4, Gradient, BoxShadow, Transform, Int, Quality, Object }

        internal static PropKind KindOf(GuiProp p) => p switch
        {
            GuiProp.Width => PropKind.Unit,
            GuiProp.Height => PropKind.Unit,
            GuiProp.MinWidth => PropKind.Unit,
            GuiProp.MaxWidth => PropKind.Unit,
            GuiProp.MinHeight => PropKind.Unit,
            GuiProp.MaxHeight => PropKind.Unit,
            GuiProp.Left => PropKind.Unit,
            GuiProp.Right => PropKind.Unit,
            GuiProp.Top => PropKind.Unit,
            GuiProp.Bottom => PropKind.Unit,
            GuiProp.MinLeft => PropKind.Unit,
            GuiProp.MaxLeft => PropKind.Unit,
            GuiProp.MinRight => PropKind.Unit,
            GuiProp.MaxRight => PropKind.Unit,
            GuiProp.MinTop => PropKind.Unit,
            GuiProp.MaxTop => PropKind.Unit,
            GuiProp.MinBottom => PropKind.Unit,
            GuiProp.MaxBottom => PropKind.Unit,
            GuiProp.ChildLeft => PropKind.Unit,
            GuiProp.ChildRight => PropKind.Unit,
            GuiProp.ChildTop => PropKind.Unit,
            GuiProp.ChildBottom => PropKind.Unit,
            GuiProp.RowBetween => PropKind.Unit,
            GuiProp.ColBetween => PropKind.Unit,
            GuiProp.PaddingLeft => PropKind.Unit,
            GuiProp.PaddingRight => PropKind.Unit,
            GuiProp.PaddingTop => PropKind.Unit,
            GuiProp.PaddingBottom => PropKind.Unit,
            GuiProp.BackgroundColor => PropKind.Color,
            GuiProp.BorderColor => PropKind.Color,
            GuiProp.TextColor => PropKind.Color,
            GuiProp.Rounded => PropKind.Float4,
            GuiProp.BackgroundGradient => PropKind.Gradient,
            GuiProp.BoxShadow => PropKind.BoxShadow,
            GuiProp.Transform => PropKind.Transform,
            GuiProp.TabSize => PropKind.Int,
            GuiProp.TextQuality => PropKind.Quality,
            GuiProp.BackgroundImage => PropKind.Object,
            _ => PropKind.Float,
        };

        /// <summary>The float field a property maps to, by reference so it can be read and written.</summary>
        internal static ref float FloatRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.BorderWidth: return ref v.BorderWidth;
                case GuiProp.BackdropBlur: return ref v.BackdropBlur;
                case GuiProp.AspectRatio: return ref v.AspectRatio;
                case GuiProp.TranslateX: return ref v.TranslateX;
                case GuiProp.TranslateY: return ref v.TranslateY;
                case GuiProp.ScaleX: return ref v.ScaleX;
                case GuiProp.ScaleY: return ref v.ScaleY;
                case GuiProp.Rotate: return ref v.Rotate;
                case GuiProp.OriginX: return ref v.OriginX;
                case GuiProp.OriginY: return ref v.OriginY;
                case GuiProp.SkewX: return ref v.SkewX;
                case GuiProp.SkewY: return ref v.SkewY;
                case GuiProp.WordSpacing: return ref v.WordSpacing;
                case GuiProp.LetterSpacing: return ref v.LetterSpacing;
                case GuiProp.LineHeight: return ref v.LineHeight;
                case GuiProp.FontSize: return ref v.FontSize;
                default: throw new ArgumentException($"{p} is not a float property", nameof(p));
            }
        }

        /// <summary>The UnitValue field a property maps to, by reference so it can be read and written.</summary>
        internal static ref UnitValue UnitRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.Width: return ref v.Width;
                case GuiProp.Height: return ref v.Height;
                case GuiProp.MinWidth: return ref v.MinWidth;
                case GuiProp.MaxWidth: return ref v.MaxWidth;
                case GuiProp.MinHeight: return ref v.MinHeight;
                case GuiProp.MaxHeight: return ref v.MaxHeight;
                case GuiProp.Left: return ref v.Left;
                case GuiProp.Right: return ref v.Right;
                case GuiProp.Top: return ref v.Top;
                case GuiProp.Bottom: return ref v.Bottom;
                case GuiProp.MinLeft: return ref v.MinLeft;
                case GuiProp.MaxLeft: return ref v.MaxLeft;
                case GuiProp.MinRight: return ref v.MinRight;
                case GuiProp.MaxRight: return ref v.MaxRight;
                case GuiProp.MinTop: return ref v.MinTop;
                case GuiProp.MaxTop: return ref v.MaxTop;
                case GuiProp.MinBottom: return ref v.MinBottom;
                case GuiProp.MaxBottom: return ref v.MaxBottom;
                case GuiProp.ChildLeft: return ref v.ChildLeft;
                case GuiProp.ChildRight: return ref v.ChildRight;
                case GuiProp.ChildTop: return ref v.ChildTop;
                case GuiProp.ChildBottom: return ref v.ChildBottom;
                case GuiProp.RowBetween: return ref v.RowBetween;
                case GuiProp.ColBetween: return ref v.ColBetween;
                case GuiProp.PaddingLeft: return ref v.PaddingLeft;
                case GuiProp.PaddingRight: return ref v.PaddingRight;
                case GuiProp.PaddingTop: return ref v.PaddingTop;
                case GuiProp.PaddingBottom: return ref v.PaddingBottom;
                default: throw new ArgumentException($"{p} is not a UnitValue property", nameof(p));
            }
        }

        /// <summary>The Color field a property maps to, by reference so it can be read and written.</summary>
        internal static ref Color ColorRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.BackgroundColor: return ref v.BackgroundColor;
                case GuiProp.BorderColor: return ref v.BorderColor;
                case GuiProp.TextColor: return ref v.TextColor;
                default: throw new ArgumentException($"{p} is not a Color property", nameof(p));
            }
        }

        /// <summary>The Float4 field a property maps to, by reference so it can be read and written.</summary>
        internal static ref Float4 Float4Ref(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.Rounded: return ref v.Rounded;
                default: throw new ArgumentException($"{p} is not a Float4 property", nameof(p));
            }
        }

        /// <summary>The Gradient field a property maps to, by reference so it can be read and written.</summary>
        internal static ref Gradient GradientRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.BackgroundGradient: return ref v.BackgroundGradient;
                default: throw new ArgumentException($"{p} is not a Gradient property", nameof(p));
            }
        }

        /// <summary>The BoxShadow field a property maps to, by reference so it can be read and written.</summary>
        internal static ref BoxShadow BoxShadowRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.BoxShadow: return ref v.BoxShadow;
                default: throw new ArgumentException($"{p} is not a BoxShadow property", nameof(p));
            }
        }

        /// <summary>The Transform2D field a property maps to, by reference so it can be read and written.</summary>
        internal static ref Transform2D TransformRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.Transform: return ref v.Transform;
                default: throw new ArgumentException($"{p} is not a Transform2D property", nameof(p));
            }
        }

        /// <summary>The int field a property maps to, by reference so it can be read and written.</summary>
        internal static ref int IntRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.TabSize: return ref v.TabSize;
                default: throw new ArgumentException($"{p} is not a int property", nameof(p));
            }
        }

        /// <summary>The FontQuality field a property maps to, by reference so it can be read and written.</summary>
        internal static ref FontQuality QualityRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.TextQuality: return ref v.TextQuality;
                default: throw new ArgumentException($"{p} is not a FontQuality property", nameof(p));
            }
        }

        /// <summary>The object field a property maps to, by reference so it can be read and written.</summary>
        internal static ref object ObjectRef(ref StyleValues v, GuiProp p)
        {
            switch (p)
            {
                case GuiProp.BackgroundImage: return ref v.BackgroundImage;
                default: throw new ArgumentException($"{p} is not a object property", nameof(p));
            }
        }

        /// <summary>
        /// Stores a value in the field for a property without boxing it.
        /// <para>
        /// The type is tested before the property, on purpose: <typeparamref name="T"/> is a
        /// compile-time constant at every call site, so the JIT folds all but one of these branches
        /// away and what is left is a single switch onto the field. Testing the property first would
        /// cost a second switch that no amount of inlining can remove.
        /// </para>
        /// </summary>
        public void Set<T>(GuiProp p, T value)
        {
            _set |= 1UL << (int)p;

            if (typeof(T) == typeof(float)) { FloatRef(ref this, p) = Unsafe.As<T, float>(ref value); return; }
            if (typeof(T) == typeof(UnitValue)) { UnitRef(ref this, p) = Unsafe.As<T, UnitValue>(ref value); return; }
            if (typeof(T) == typeof(Color)) { ColorRef(ref this, p) = Unsafe.As<T, Color>(ref value); return; }
            if (typeof(T) == typeof(Float4)) { Float4Ref(ref this, p) = Unsafe.As<T, Float4>(ref value); return; }
            if (typeof(T) == typeof(Gradient)) { GradientRef(ref this, p) = Unsafe.As<T, Gradient>(ref value); return; }
            if (typeof(T) == typeof(BoxShadow)) { BoxShadowRef(ref this, p) = Unsafe.As<T, BoxShadow>(ref value); return; }
            if (typeof(T) == typeof(Transform2D)) { TransformRef(ref this, p) = Unsafe.As<T, Transform2D>(ref value); return; }
            if (typeof(T) == typeof(int)) { IntRef(ref this, p) = Unsafe.As<T, int>(ref value); return; }
            if (typeof(T) == typeof(FontQuality)) { QualityRef(ref this, p) = Unsafe.As<T, FontQuality>(ref value); return; }

            if (KindOf(p) == PropKind.Object) { ObjectRef(ref this, p) = value; return; }

            throw new ArgumentException($"{typeof(T).Name} is not a style value type", nameof(value));
        }

        /// <summary>Copies one property's value across, leaving every other field alone.</summary>
        internal static void Copy(GuiProp p, ref StyleValues from, ref StyleValues to)
        {
            switch (KindOf(p))
            {
                case PropKind.Float: FloatRef(ref to, p) = FloatRef(ref from, p); break;
                case PropKind.Unit: UnitRef(ref to, p) = UnitRef(ref from, p); break;
                case PropKind.Color: ColorRef(ref to, p) = ColorRef(ref from, p); break;
                case PropKind.Float4: Float4Ref(ref to, p) = Float4Ref(ref from, p); break;
                case PropKind.Gradient: GradientRef(ref to, p) = GradientRef(ref from, p); break;
                case PropKind.BoxShadow: BoxShadowRef(ref to, p) = BoxShadowRef(ref from, p); break;
                case PropKind.Transform: TransformRef(ref to, p) = TransformRef(ref from, p); break;
                case PropKind.Int: IntRef(ref to, p) = IntRef(ref from, p); break;
                case PropKind.Quality: QualityRef(ref to, p) = QualityRef(ref from, p); break;
                case PropKind.Object: ObjectRef(ref to, p) = ObjectRef(ref from, p); break;
            }
            to._set |= 1UL << (int)p;
        }

        /// <summary>Whether one property holds the same value in both, without boxing either side.</summary>
        internal static bool SameValue(GuiProp p, ref StyleValues a, ref StyleValues b) => KindOf(p) switch
        {
            PropKind.Float => FloatRef(ref a, p).Equals(FloatRef(ref b, p)),
            PropKind.Unit => UnitRef(ref a, p).Equals(UnitRef(ref b, p)),
            PropKind.Color => ColorRef(ref a, p).Equals(ColorRef(ref b, p)),
            PropKind.Float4 => Float4Ref(ref a, p).Equals(Float4Ref(ref b, p)),
            PropKind.Gradient => GradientRef(ref a, p).Equals(GradientRef(ref b, p)),
            PropKind.BoxShadow => BoxShadowRef(ref a, p).Equals(BoxShadowRef(ref b, p)),
            PropKind.Transform => TransformRef(ref a, p).Equals(TransformRef(ref b, p)),
            PropKind.Int => IntRef(ref a, p) == IntRef(ref b, p),
            PropKind.Quality => QualityRef(ref a, p) == QualityRef(ref b, p),
            _ => ReferenceEquals(ObjectRef(ref a, p), ObjectRef(ref b, p)),
        };

        /// <summary>
        /// Interpolates one property between two sets of values. Kinds that cannot be interpolated
        /// meaningfully snap to the end value, which is what the boxed version did.
        /// </summary>
        internal static void Lerp(GuiProp p, ref StyleValues from, ref StyleValues to, float t, ref StyleValues dst)
        {
            switch (KindOf(p))
            {
                case PropKind.Float:
                    FloatRef(ref dst, p) = FloatRef(ref from, p) + (FloatRef(ref to, p) - FloatRef(ref from, p)) * t;
                    break;
                case PropKind.Unit:
                    UnitRef(ref dst, p) = UnitValue.Lerp(UnitRef(ref from, p), UnitRef(ref to, p), t);
                    break;
                case PropKind.Color:
                    ColorRef(ref dst, p) = ElementStyle.InterpolateColor(ColorRef(ref from, p), ColorRef(ref to, p), t);
                    break;
                case PropKind.Float4:
                    Float4Ref(ref dst, p) = Maths.Lerp(Float4Ref(ref from, p), Float4Ref(ref to, p), t);
                    break;
                case PropKind.Gradient:
                    GradientRef(ref dst, p) = Gradient.Lerp(GradientRef(ref from, p), GradientRef(ref to, p), t);
                    break;
                case PropKind.BoxShadow:
                    BoxShadowRef(ref dst, p) = BoxShadow.Lerp(BoxShadowRef(ref from, p), BoxShadowRef(ref to, p), t);
                    break;
                case PropKind.Transform:
                    TransformRef(ref dst, p) = Transform2D.Lerp(TransformRef(ref from, p), TransformRef(ref to, p), t);
                    break;
                case PropKind.Int:
                    IntRef(ref dst, p) = IntRef(ref from, p) + (int)((IntRef(ref to, p) - IntRef(ref from, p)) * t);
                    break;
                default:
                    Copy(p, ref to, ref dst);
                    break;
            }
            dst._set |= 1UL << (int)p;
        }

        /// <summary>
        /// Copies every property the source has explicitly set into the destination, leaving the
        /// rest of the destination alone. This is how a style template is applied.
        /// </summary>
        internal static void MergeInto(ref StyleValues from, ref StyleValues to)
        {
            ulong set = from._set;
            for (int bit = 0; set != 0; bit++, set >>= 1)
                if ((set & 1) != 0)
                    Copy((GuiProp)bit, ref from, ref to);
        }

        #endregion

        /// <summary>Boxing read of a property (for the compatibility shim / cold callers).</summary>
        public readonly object GetBoxed(GuiProp p) => p switch
        {
            GuiProp.BackgroundColor => BackgroundColor,
            GuiProp.BackgroundGradient => BackgroundGradient,
            GuiProp.BorderColor => BorderColor,
            GuiProp.BorderWidth => BorderWidth,
            GuiProp.Rounded => Rounded,
            GuiProp.BoxShadow => BoxShadow,
            GuiProp.BackdropBlur => BackdropBlur,
            GuiProp.AspectRatio => AspectRatio,
            GuiProp.Width => Width,
            GuiProp.Height => Height,
            GuiProp.MinWidth => MinWidth,
            GuiProp.MaxWidth => MaxWidth,
            GuiProp.MinHeight => MinHeight,
            GuiProp.MaxHeight => MaxHeight,
            GuiProp.Left => Left,
            GuiProp.Right => Right,
            GuiProp.Top => Top,
            GuiProp.Bottom => Bottom,
            GuiProp.MinLeft => MinLeft,
            GuiProp.MaxLeft => MaxLeft,
            GuiProp.MinRight => MinRight,
            GuiProp.MaxRight => MaxRight,
            GuiProp.MinTop => MinTop,
            GuiProp.MaxTop => MaxTop,
            GuiProp.MinBottom => MinBottom,
            GuiProp.MaxBottom => MaxBottom,
            GuiProp.ChildLeft => ChildLeft,
            GuiProp.ChildRight => ChildRight,
            GuiProp.ChildTop => ChildTop,
            GuiProp.ChildBottom => ChildBottom,
            GuiProp.RowBetween => RowBetween,
            GuiProp.ColBetween => ColBetween,
            GuiProp.PaddingLeft => PaddingLeft,
            GuiProp.PaddingRight => PaddingRight,
            GuiProp.PaddingTop => PaddingTop,
            GuiProp.PaddingBottom => PaddingBottom,
            GuiProp.TranslateX => TranslateX,
            GuiProp.TranslateY => TranslateY,
            GuiProp.ScaleX => ScaleX,
            GuiProp.ScaleY => ScaleY,
            GuiProp.Rotate => Rotate,
            GuiProp.OriginX => OriginX,
            GuiProp.OriginY => OriginY,
            GuiProp.SkewX => SkewX,
            GuiProp.SkewY => SkewY,
            GuiProp.Transform => Transform,
            GuiProp.BackgroundImage => BackgroundImage,
            GuiProp.TextColor => TextColor,
            GuiProp.WordSpacing => WordSpacing,
            GuiProp.LetterSpacing => LetterSpacing,
            GuiProp.LineHeight => LineHeight,
            GuiProp.TabSize => TabSize,
            GuiProp.FontSize => FontSize,
            GuiProp.TextQuality => TextQuality,
            _ => null
        };

        /// <summary>Typed read of a UnitValue property (the hot layout path), no boxing.</summary>
        public readonly UnitValue GetUnit(GuiProp p) => p switch
        {
            GuiProp.Width => Width,
            GuiProp.Height => Height,
            GuiProp.MinWidth => MinWidth,
            GuiProp.MaxWidth => MaxWidth,
            GuiProp.MinHeight => MinHeight,
            GuiProp.MaxHeight => MaxHeight,
            GuiProp.Left => Left,
            GuiProp.Right => Right,
            GuiProp.Top => Top,
            GuiProp.Bottom => Bottom,
            GuiProp.MinLeft => MinLeft,
            GuiProp.MaxLeft => MaxLeft,
            GuiProp.MinRight => MinRight,
            GuiProp.MaxRight => MaxRight,
            GuiProp.MinTop => MinTop,
            GuiProp.MaxTop => MaxTop,
            GuiProp.MinBottom => MinBottom,
            GuiProp.MaxBottom => MaxBottom,
            GuiProp.ChildLeft => ChildLeft,
            GuiProp.ChildRight => ChildRight,
            GuiProp.ChildTop => ChildTop,
            GuiProp.ChildBottom => ChildBottom,
            GuiProp.RowBetween => RowBetween,
            GuiProp.ColBetween => ColBetween,
            GuiProp.PaddingLeft => PaddingLeft,
            GuiProp.PaddingRight => PaddingRight,
            GuiProp.PaddingTop => PaddingTop,
            GuiProp.PaddingBottom => PaddingBottom,
            _ => default
        };
    }
}
