using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{

    internal abstract class StackableGraphics<T> : IDisposable
    {
        public static readonly Stack<T> Stack = new();
        public static T Current => Stack.TryPeek(out var v) ? v : default;
        protected T value;

        public StackableGraphics(T val)
        {
            Stack.Push(val);
            value = val;
            Apply();
        }

        public abstract void Apply();

        public void Dispose()
        {
            Stack.Pop();
            if (Stack.Count > 0) Apply();
            else {
                // use Reflection to find the static method SetDefault
                var method = GetType().GetMethod("SetDefault", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (method != null) method.Invoke(null, null);
            }
        }
    }

    internal class ActiveDepthTest : StackableGraphics<bool>
    {
        public static bool ActiveInOGL = true;
        public static void SetDefault()
        {
            Graphics.GL.Enable(EnableCap.DepthTest);
            ActiveInOGL = true;
        }

        public ActiveDepthTest(bool val) : base(val) { }

        public override void Apply()
        {
            if (ActiveInOGL != Current) {
                if (Current) Graphics.GL.Enable(EnableCap.DepthTest);
                else Graphics.GL.Disable(EnableCap.DepthTest);
                ActiveInOGL = Current;
            }
        }
    }

    internal class ActiveColorBlend : StackableGraphics<bool>
    {
        public static bool ActiveInOGL = true;
        public static void SetDefault()
        {
            Graphics.GL.Enable(EnableCap.Blend);
            ActiveInOGL = true;
        }

        public ActiveColorBlend(bool val) : base(val) { }

        public override void Apply()
        {
            if (ActiveInOGL != Current) {
                if (Current) Graphics.GL.Enable(EnableCap.Blend);
                else Graphics.GL.Disable(EnableCap.Blend);
                ActiveInOGL = Current;
            }
        }
    }

    internal class ActiveCullFace : StackableGraphics<bool>
    {
        public static bool ActiveInOGL = true;
        public static void SetDefault()
        {
            Graphics.GL.Enable(EnableCap.CullFace);
            ActiveInOGL = true;
        }

        public ActiveCullFace(bool val) : base(val) { }

        public override void Apply()
        {
            if (ActiveInOGL != Current) {
                if (Current) Graphics.GL.Enable(EnableCap.CullFace);
                else Graphics.GL.Disable(EnableCap.CullFace);
                ActiveInOGL = Current;
            }
        }
    }

    public enum BlendMode { Alpha, Additive, Multiply, AddColors, Subtract, Premultiply, Custom }

    internal class ActiveBlendMode : StackableGraphics<BlendMode>
    {
        public static BlendMode ActiveInOGL = BlendMode.Alpha;
        public static void SetDefault() 
        { 
            Graphics.GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); 
            Graphics.GL.BlendEquation(BlendEquationModeEXT.FuncAdd);
            ActiveInOGL = BlendMode.Alpha;
        }

        public ActiveBlendMode(BlendMode val) : base(val) { }

        public override void Apply()
        {
            if (ActiveInOGL != Current) {
                var equation = BlendEquationModeEXT.FuncAdd;
                switch (Current) {
                    case BlendMode.Alpha: Graphics.GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha); break;
                    case BlendMode.Additive: Graphics.GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One); break;
                    case BlendMode.Multiply: Graphics.GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha); break;
                    case BlendMode.AddColors: Graphics.GL.BlendFunc(BlendingFactor.One, BlendingFactor.One); break;
                    case BlendMode.Subtract: Graphics.GL.BlendFunc(BlendingFactor.One, BlendingFactor.One); equation = BlendEquationModeEXT.FuncSubtract; break;
                    case BlendMode.Premultiply: Graphics.GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha); break;
                    case BlendMode.Custom: Graphics.GL.BlendFunc(Graphics.CustomBlendSrcFactor, Graphics.CustomBlendDstFactor); equation = Graphics.CustomBlendEquation; break;
                }
                Graphics.GL.BlendEquation(equation);
                ActiveInOGL = Current;
            }
        }
    }

    internal class ActiveFaceCull : StackableGraphics<TriangleFace>
    {
        public static TriangleFace ActiveInOGL = TriangleFace.Back;
        public static void SetDefault()
        {
            Graphics.GL.CullFace(TriangleFace.Back);
            ActiveInOGL = TriangleFace.Back;
        }

        public ActiveFaceCull(TriangleFace val) : base(val) { }

        public override void Apply()
        {
            if (ActiveInOGL != Current) {
                Graphics.GL.CullFace(Current);
                ActiveInOGL = Current;
            }
        }
    }
}
