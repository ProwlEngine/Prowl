using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.GraphicsBackend;
using Prowl.Vector;

using Texture2D = Prowl.Runtime.Resources.Texture2D;

namespace Prowl.Runtime.Rendering
{
    public partial class PropertyState
    {
        [SerializeField] private Dictionary<string, Color> colors = new();
        [SerializeField] private Dictionary<string, Float2> vectors2 = new();
        [SerializeField] private Dictionary<string, Float3> vectors3 = new();
        [SerializeField] private Dictionary<string, Float4> vectors4 = new();
        [SerializeField] private Dictionary<string, float> floats = new();
        [SerializeField] private Dictionary<string, int> ints = new();
        [SerializeField] private Dictionary<string, Float4x4> matrices = new();
        [SerializeField] private Dictionary<string, Float4x4[]> matrixArr = new();
        [SerializeField] private Dictionary<string, Texture2D> textures = new();
        [SerializeField] private Dictionary<string, GraphicsBuffer> buffers = new();
        [SerializeField] private Dictionary<string, uint> bufferBindings = new();

        //private Dictionary<string, int> textureSlots = new();

        public PropertyState() { }

        public PropertyState(PropertyState clone)
        {
            colors = new(clone.colors);
            vectors2 = new(clone.vectors2);
            vectors3 = new(clone.vectors3);
            vectors4 = new(clone.vectors4);
            floats = new(clone.floats);
            ints = new(clone.ints);
            matrices = new(clone.matrices);
            matrixArr = new(clone.matrixArr);
            textures = new(clone.textures);
            buffers = new(clone.buffers);
        }

        public bool isEmpty => colors.Count == 0 && vectors4.Count == 0 && vectors3.Count == 0 && vectors2.Count == 0 && floats.Count == 0 && ints.Count == 0 && matrices.Count == 0 && textures.Count == 0;

        // Setters
        public void SetColor(string name, Color value) => colors[name] = value;
        public void SetVector(string name, Double2 value) => vectors2[name] = (Float2)value;
        public void SetVector(string name, Double3 value) => vectors3[name] = (Float3)value;
        public void SetVector(string name, Double4 value) => vectors4[name] = (Float4)value;
        public void SetFloat(string name, float value) => floats[name] = value;
        public void SetInt(string name, int value) => ints[name] = value;
        public void SetMatrix(string name, Double4x4 value) => matrices[name] = (Float4x4)value;
        public void SetMatrices(string name, Double4x4[] value) => matrixArr[name] = value.Select(x => (Float4x4)x).ToArray();
        public void SetTexture(string name, Texture2D value) => textures[name] = value;
        public void SetBuffer(string name, GraphicsBuffer value, uint bindingPoint = 0)
        {
            buffers[name] = value;
            bufferBindings[name] = bindingPoint;
        }

        // Getters
        public Color GetColor(string name) => colors.TryGetValue(name, out Color value) ? value : Color.white;
        public Double2 GetVector2(string name) => vectors2.TryGetValue(name, out Float2 value) ? value : Double2.Zero;
        public Double3 GetVector3(string name) => vectors3.TryGetValue(name, out Float3 value) ? value : Double3.Zero;
        public Double4 GetVector4(string name) => vectors4.TryGetValue(name, out Float4 value) ? value : Double4.Zero;
        public float GetFloat(string name) => floats.TryGetValue(name, out float value) ? value : 0;
        public int GetInt(string name) => ints.TryGetValue(name, out int value) ? value : 0;
        public Double4x4 GetMatrix(string name) => matrices.TryGetValue(name, out Float4x4 value) ? value : Double4x4.Identity;
        public Texture2D? GetTexture(string name) => textures.TryGetValue(name, out Texture2D value) ? value : null;
        public GraphicsBuffer GetBuffer(string name) => buffers.TryGetValue(name, out GraphicsBuffer value) ? value : null;
        public uint GetBufferBinding(string name) => bufferBindings.TryGetValue(name, out uint value) ? value : 0;


        public void Clear()
        {
            textures.Clear();
            matrices.Clear();
            matrixArr.Clear();
            ints.Clear();
            floats.Clear();
            vectors2.Clear();
            vectors3.Clear();
            vectors4.Clear();
            colors.Clear();
            buffers.Clear();
            bufferBindings.Clear();
        }

        public void ApplyOverride(PropertyState properties)
        {
            foreach (var item in properties.colors)
                colors[item.Key] = item.Value;
            foreach (var item in properties.vectors2)
                vectors2[item.Key] = item.Value;
            foreach (var item in properties.vectors3)
                vectors3[item.Key] = item.Value;
            foreach (var item in properties.vectors4)
                vectors4[item.Key] = item.Value;
            foreach (var item in properties.floats)
                floats[item.Key] = item.Value;
            foreach (var item in properties.ints)
                ints[item.Key] = item.Value;
            foreach (var item in properties.matrices)
                matrices[item.Key] = item.Value;
            foreach (var item in properties.matrixArr)
                matrixArr[item.Key] = item.Value;
            foreach (var item in properties.textures)
                textures[item.Key] = item.Value;
            foreach (var item in properties.buffers)
                buffers[item.Key] = item.Value;
            foreach (var item in properties.bufferBindings)
                bufferBindings[item.Key] = item.Value;
        }

        // Modified Apply method to also apply global properties
        public static unsafe void Apply(PropertyState mpb, GraphicsProgram shader)
        {
            var cache = shader.uniformCache;
            int texSlot = 0;

            // Apply global properties first (so instance properties can override them)
            ApplyGlobals(shader, cache, ref texSlot);

            // Then apply instance properties
            foreach (var item in mpb.floats)
            {
                if (!cache.floats.TryGetValue(item.Key, out var cachedValue) || cachedValue != item.Value)
                {
                    Graphics.Device.SetUniformF(shader, item.Key, item.Value);
                    cache.floats[item.Key] = item.Value;
                }
            }

            foreach (var item in mpb.ints)
            {
                if (!cache.ints.TryGetValue(item.Key, out var cachedValue) || cachedValue != item.Value)
                {
                    Graphics.Device.SetUniformI(shader, item.Key, item.Value);
                    cache.ints[item.Key] = item.Value;
                }
            }

            foreach (var item in mpb.vectors2)
            {
                if (!cache.vectors2.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(item.Value))
                {
                    Graphics.Device.SetUniformV2(shader, item.Key, item.Value);
                    cache.vectors2[item.Key] = item.Value;
                }
            }

            foreach (var item in mpb.vectors3)
            {
                if (!cache.vectors3.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(item.Value))
                {
                    Graphics.Device.SetUniformV3(shader, item.Key, item.Value);
                    cache.vectors3[item.Key] = item.Value;
                }
            }

            foreach (var item in mpb.vectors4)
            {
                if (!cache.vectors4.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(item.Value))
                {
                    Graphics.Device.SetUniformV4(shader, item.Key, item.Value);
                    cache.vectors4[item.Key] = item.Value;
                }
            }

            foreach (var item in mpb.colors)
            {
                Float4 colorVec = new Float4(item.Value.r, item.Value.g, item.Value.b, item.Value.a);
                if (!cache.vectors4.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(item.Value))
                {
                    Graphics.Device.SetUniformV4(shader, item.Key, colorVec);
                    cache.vectors4[item.Key] = colorVec;
                }
            }

            foreach (var item in mpb.matrices)
            {
                if (!cache.matrices.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(item.Value))
                {
                    Graphics.Device.SetUniformMatrix(shader, item.Key, false, item.Value);
                    cache.matrices[item.Key] = item.Value;
                }
            }

            // Matrix arrays - always set (comparison would be expensive)
            foreach (var item in mpb.matrixArr)
                Graphics.Device.SetUniformMatrix(shader, item.Key, (uint)item.Value.Length, false, in item.Value[0].c0.X);

            // Bind uniform buffers - check if buffer changed
            foreach (var item in mpb.buffers)
            {
                if (!cache.buffers.TryGetValue(item.Key, out var cachedBuffer) || cachedBuffer != item.Value)
                {
                    Graphics.Device.BindUniformBuffer(shader, item.Key, item.Value);
                    cache.buffers[item.Key] = item.Value;
                }
            }

            foreach (var item in mpb.textures)
            {
                var tex = item.Value;
                if (tex != null)
                {
                    // Always set textures - slot assignment must be consistent
                    Graphics.Device.SetUniformTexture(shader, item.Key, texSlot, tex.Handle);
                    texSlot++;
                }
            }
        }

        private static void ApplyGlobals(GraphicsProgram shader, GraphicsProgram.UniformCache cache, ref int texSlot)
        {
            foreach (var item in globalFloats)
            {
                if (!cache.floats.TryGetValue(item.Key, out var cachedValue) || cachedValue != item.Value)
                {
                    Graphics.Device.SetUniformF(shader, item.Key, item.Value);
                    cache.floats[item.Key] = item.Value;
                }
            }

            foreach (var item in globalInts)
            {
                if (!cache.ints.TryGetValue(item.Key, out var cachedValue) || cachedValue != item.Value)
                {
                    Graphics.Device.SetUniformI(shader, item.Key, item.Value);
                    cache.ints[item.Key] = item.Value;
                }
            }

            foreach (var item in globalVectors2)
            {
                Float2 value = (Float2)item.Value;
                if (!cache.vectors2.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(value))
                {
                    Graphics.Device.SetUniformV2(shader, item.Key, value);
                    cache.vectors2[item.Key] = value;
                }
            }

            foreach (var item in globalVectors3)
            {
                Float3 value = (Float3)item.Value;
                if (!cache.vectors3.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(value))
                {
                    Graphics.Device.SetUniformV3(shader, item.Key, value);
                    cache.vectors3[item.Key] = value;
                }
            }

            foreach (var item in globalVectors4)
            {
                Float4 value = (Float4)item.Value;
                if (!cache.vectors4.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(value))
                {
                    Graphics.Device.SetUniformV4(shader, item.Key, value);
                    cache.vectors4[item.Key] = value;
                }
            }

            foreach (var item in globalColors)
            {
                Float4 colorVec = new Float4(item.Value.r, item.Value.g, item.Value.b, item.Value.a);
                if (!cache.vectors4.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(colorVec))
                {
                    Graphics.Device.SetUniformV4(shader, item.Key, colorVec);
                    cache.vectors4[item.Key] = colorVec;
                }
            }

            foreach (var item in globalMatrices)
            {
                Float4x4 value = (Float4x4)item.Value;
                if (!cache.matrices.TryGetValue(item.Key, out var cachedValue) || !cachedValue.Equals(value))
                {
                    Graphics.Device.SetUniformMatrix(shader, item.Key, false, value);
                    cache.matrices[item.Key] = value;
                }
            }

            // Matrix arrays - always set (comparison would be expensive)
            foreach (var item in globalMatrixArr)
                Graphics.Device.SetUniformMatrix(shader, item.Key, (uint)item.Value.Length, false, in item.Value[0].M11);

            // Bind global uniform buffers - check if buffer changed
            foreach (var item in globalBuffers)
            {
                if (!cache.buffers.TryGetValue(item.Key, out var cachedBuffer) || cachedBuffer != item.Value)
                {
                    Graphics.Device.BindUniformBuffer(shader, item.Key, item.Value);
                    cache.buffers[item.Key] = item.Value;
                }
            }

            foreach (var item in globalTextures)
            {
                var tex = item.Value;
                if (tex != null)
                {
                    // Always set textures - slot assignment must be consistent
                    Graphics.Device.SetUniformTexture(shader, item.Key, texSlot, tex.Handle);
                    texSlot++;
                }
            }
        }
    }

    public partial class PropertyState
    {
        // Global static dictionaries
        private static Dictionary<string, Color> globalColors = new();
        private static Dictionary<string, Double2> globalVectors2 = new();
        private static Dictionary<string, Double3> globalVectors3 = new();
        private static Dictionary<string, Double4> globalVectors4 = new();
        private static Dictionary<string, float> globalFloats = new();
        private static Dictionary<string, int> globalInts = new();
        private static Dictionary<string, Double4x4> globalMatrices = new();
        private static Dictionary<string, System.Numerics.Matrix4x4[]> globalMatrixArr = new();
        private static Dictionary<string, Texture2D> globalTextures = new();
        private static Dictionary<string, GraphicsBuffer> globalBuffers = new();
        private static Dictionary<string, uint> globalBufferBindings = new();

        // Global setters
        public static void SetGlobalColor(string name, Color value) => globalColors[name] = value;
        public static void SetGlobalVector(string name, Double2 value) => globalVectors2[name] = value;
        public static void SetGlobalVector(string name, Double3 value) => globalVectors3[name] = value;
        public static void SetGlobalVector(string name, Double4 value) => globalVectors4[name] = value;
        public static void SetGlobalFloat(string name, float value) => globalFloats[name] = value;
        public static void SetGlobalInt(string name, int value) => globalInts[name] = value;
        public static void SetGlobalMatrix(string name, Double4x4 value) => globalMatrices[name] = value;
        public static void SetGlobalMatrices(string name, Double4x4[] value) => globalMatrixArr[name] = value.Select(x => (Float4x4)x).Cast<System.Numerics.Matrix4x4>().ToArray();
        public static void SetGlobalTexture(string name, Texture2D value) => globalTextures[name] = value;
        public static void SetGlobalBuffer(string name, GraphicsBuffer value, uint bindingPoint = 0)
        {
            globalBuffers[name] = value;
            globalBufferBindings[name] = bindingPoint;
        }

        // Global getters
        public static Color GetGlobalColor(string name) => globalColors.TryGetValue(name, out Color value) ? value : Color.white;
        public static Double2 GetGlobalVector2(string name) => globalVectors2.TryGetValue(name, out Double2 value) ? value : Double2.Zero;
        public static Double3 GetGlobalVector3(string name) => globalVectors3.TryGetValue(name, out Double3 value) ? value : Double3.Zero;
        public static Double4 GetGlobalVector4(string name) => globalVectors4.TryGetValue(name, out Double4 value) ? value : Double4.Zero;
        public static float GetGlobalFloat(string name) => globalFloats.TryGetValue(name, out float value) ? value : 0;
        public static int GetGlobalInt(string name) => globalInts.TryGetValue(name, out int value) ? value : 0;
        public static Double4x4 GetGlobalMatrix(string name) => globalMatrices.TryGetValue(name, out Double4x4 value) ? value : Double4x4.Identity;
        public static Texture2D? GetGlobalTexture(string name) => globalTextures.TryGetValue(name, out Texture2D value) ? value : null;
        public static GraphicsBuffer GetGlobalBuffer(string name) => globalBuffers.TryGetValue(name, out GraphicsBuffer value) ? value : null;
        public static uint GetGlobalBufferBinding(string name) => globalBufferBindings.TryGetValue(name, out uint value) ? value : 0;

        public static void ClearGlobals()
        {
            globalTextures.Clear();
            globalMatrices.Clear();
            globalInts.Clear();
            globalFloats.Clear();
            globalVectors2.Clear();
            globalVectors3.Clear();
            globalVectors4.Clear();
            globalColors.Clear();
            globalMatrixArr.Clear();
            globalBuffers.Clear();
            globalBufferBindings.Clear();
        }
    }

}
