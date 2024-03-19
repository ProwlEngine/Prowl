using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime
{
    /// <summary>
    /// The Shader class itself doesnt do much, It stores the properties of the shader and the shader code and Keywords.
    /// This is used in conjunction with the Material class to create shader variants with the correct keywords and to render things
    /// </summary>
    public sealed class Shader : EngineObject, ISerializable
    {
        public class Property
        {
            public string Name = "";
            public string DisplayName = "";
            public enum PropertyType { FLOAT, VEC2, VEC3, VEC4, COLOR, INTEGER, IVEC2, IVEC3, IVEC4, TEXTURE2D }
            public PropertyType Type;
        }

        public class ShaderPass
        {
            public string RenderMode; // Defaults to Opaque
            public string Vertex;
            public string Fragment;
        }

        public class ShaderShadowPass
        {
            public string Vertex;
            public string Fragment;
        }

        internal static HashSet<string> globalKeywords = new();

        public static void EnableKeyword(string keyword)
        {
            keyword = keyword.ToLower().Replace(" ", "").Replace(";", "");
            if (globalKeywords.Contains(keyword)) return;
            globalKeywords.Add(keyword);
        }

        public static void DisableKeyword(string keyword)
        {
            keyword = keyword.ToUpper().Replace(" ", "").Replace(";", "");
            if (!globalKeywords.Contains(keyword)) return;
            globalKeywords.Remove(keyword);
        }

        public static bool IsKeywordEnabled(string keyword) => globalKeywords.Contains(keyword.ToLower().Replace(" ", "").Replace(";", ""));

        public List<Property> Properties = new();
        public List<ShaderPass> Passes = new();
        public ShaderShadowPass? ShadowPass;

        public (uint[], uint) Compile(string[] defines)
        {
            uint[] compiledPasses = new uint[Passes.Count];
            for (int i = 0; i < Passes.Count; i++)
                compiledPasses[i] = CompilePass(i, defines);
            return (compiledPasses, CompileShadowPass(defines));
        }

        public uint CompileShadowPass(string[] defines)
        {
            if (ShadowPass == null)
            {
                var defaultDepth = Find("Defaults/Depth.shader");
                if (!defaultDepth.IsAvailable) throw new Exception($"Failed to default Depth shader for shader: {Name}");
                return defaultDepth.Res!.CompilePass(0, []);
            }
            else
            {
                string frag = ShadowPass.Fragment;
                string vert = ShadowPass.Vertex;
                PrepareFragVert(ref frag, ref vert, defines);
                return CompileShader(frag, vert, "Defaults/Depth.shader");
            }
        }

        public uint CompilePass(int pass, string[] defines)
        {
            string frag = Passes[pass].Fragment;
            string vert = Passes[pass].Vertex;
            PrepareFragVert(ref frag, ref vert, defines);
            return CompileShader(frag, vert, "Defaults/Invalid.shader");
        }

        private uint CompileShader(string frag, string vert, string fallback)
        {
            try {
                return Compile(vert, "", frag);
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }
            var fallbackShader = Find(fallback);
            return fallbackShader.Res.CompilePass(0, []);
        }

        private void PrepareFragVert(ref string frag, ref string vert, string[] defines)
        {
            if (string.IsNullOrEmpty(frag)) throw new Exception($"Failed to compile shader pass of {Name}. Fragment Shader is null or empty.");
            if (string.IsNullOrEmpty(vert)) throw new Exception($"Failed to compile shader pass of {Name}. Vertex Shader is null or empty.");

            // Default Defines
            frag = frag.Insert(0, $"#define PROWL_VERSION 1\n");
            vert = vert.Insert(0, $"#define PROWL_VERSION 1\n");

            // Insert keywords at the start
            for (int j = 0; j < defines.Length; j++)
            {
                frag = frag.Insert(0, $"#define {defines[j]}\n");
                vert = vert.Insert(0, $"#define {defines[j]}\n");
            }

            // Insert the version at the start
            frag = frag.Insert(0, $"#version 410\n");
            vert = vert.Insert(0, $"#version 410\n");
        }

        private uint Compile(string vertexSource, string geometrySource, string fragmentSource)
        {
            // Create the program
            uint shaderProgram = Graphics.GL.CreateProgram();

            // Initialize compilation log info variables
            int statusCode = -1;
            string info = string.Empty;

            // Create vertex shader if requested
            if (!string.IsNullOrEmpty(vertexSource)) {
                // Create and compile the shader
                uint vertexShader = Graphics.GL.CreateShader(ShaderType.VertexShader);
                Graphics.GL.ShaderSource(vertexShader, vertexSource);
                Graphics.GL.CompileShader(vertexShader);

                // Check the compile log
                Graphics.GL.GetShaderInfoLog(vertexShader, out info);
                Graphics.GL.GetShader(vertexShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1) {
                    // Delete every handles when compilation failed
                    Graphics.GL.DeleteShader(vertexShader);
                    Graphics.GL.DeleteProgram(shaderProgram);

                    throw new InvalidOperationException("Failed to Compile Vertex Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                Graphics.GL.AttachShader(shaderProgram, vertexShader);
                Graphics.GL.DeleteShader(vertexShader);

                Graphics.CheckGL();
            }

            // Create geometry shader if requested
            if (!string.IsNullOrEmpty(geometrySource)) {
                // Create and compile the shader
                uint geometryShader = Graphics.GL.CreateShader(ShaderType.GeometryShader);
                Graphics.GL.ShaderSource(geometryShader, geometrySource);
                Graphics.GL.CompileShader(geometryShader);

                // Check the compile log
                Graphics.GL.GetShaderInfoLog(geometryShader, out info);
                Graphics.GL.GetShader(geometryShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1) {
                    // Delete every handles when compilation failed
                    Graphics.GL.DeleteShader(geometryShader);
                    Graphics.GL.DeleteProgram(shaderProgram);

                    throw new InvalidOperationException("Failed to Compile Geometry Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                Graphics.GL.AttachShader(shaderProgram, geometryShader);
                Graphics.GL.DeleteShader(geometryShader);

                Graphics.CheckGL();
            }

            // Create fragment shader if requested
            if (!string.IsNullOrEmpty(fragmentSource)) {
                // Create and compile the shader
                uint fragmentShader = Graphics.GL.CreateShader(ShaderType.FragmentShader);
                Graphics.GL.ShaderSource(fragmentShader, fragmentSource);
                Graphics.GL.CompileShader(fragmentShader);

                // Check the compile log
                Graphics.GL.GetShaderInfoLog(fragmentShader, out info);
                Graphics.GL.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1) {
                    // Delete every handles when compilation failed
                    Graphics.GL.DeleteShader(fragmentShader);
                    Graphics.GL.DeleteProgram(shaderProgram);

                    throw new InvalidOperationException("Failed to Compile Fragment Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                Graphics.GL.AttachShader(shaderProgram, fragmentShader);
                Graphics.GL.DeleteShader(fragmentShader);

                Graphics.CheckGL();
            }

            // Link the compiled program
            Graphics.GL.LinkProgram(shaderProgram);

            Graphics.CheckGL();

            // Check for link status
            Graphics.GL.GetProgramInfoLog(shaderProgram, out info);
            Graphics.GL.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus, out statusCode);
            if (statusCode != 1) {
                // Delete the handles when failed to link the program
                Graphics.GL.DeleteProgram(shaderProgram);

                throw new InvalidOperationException("Failed to Link Shader Program.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
            }

            // Force an OpenGL flush, so that the shader will appear updated
            // in all contexts immediately (solves problems in multi-threaded apps)
            Graphics.GL.Flush();
            Graphics.CheckGL();

            return shaderProgram;
        }

        public static AssetRef<Shader> Find(string path)
        {
            return Application.AssetProvider.LoadAsset<Shader>(path);
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();
            SerializedProperty propertiesTag = SerializedProperty.NewList();
            foreach (var property in Properties)
            {
                SerializedProperty propertyTag = SerializedProperty.NewCompound();
                propertyTag.Add("Name", new(property.Name));
                propertyTag.Add("DisplayName", new(property.DisplayName));
                propertyTag.Add("Type", new((byte)property.Type));
                propertiesTag.ListAdd(propertyTag);
            }
            compoundTag.Add("Properties", propertiesTag);
            SerializedProperty passesTag = SerializedProperty.NewList();
            foreach (var pass in Passes)
            {
                SerializedProperty passTag = SerializedProperty.NewCompound();
                passTag.Add("RenderMode", new(pass.RenderMode));
                passTag.Add("Vertex", new(pass.Vertex));
                passTag.Add("Fragment", new(pass.Fragment));
                passesTag.ListAdd(passTag);
            }
            compoundTag.Add("Passes", passesTag);
            if (ShadowPass != null)
            {
                SerializedProperty shadowPassTag = SerializedProperty.NewCompound();
                shadowPassTag.Add("Vertex", new(ShadowPass.Vertex));
                shadowPassTag.Add("Fragment", new(ShadowPass.Fragment));
                compoundTag.Add("ShadowPass", shadowPassTag);
            }
            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            Properties.Clear();
            var propertiesTag = value.Get("Properties");
            foreach (var propertyTag in propertiesTag.List)
            {
                Property property = new Property();
                property.Name = propertyTag.Get("Name").StringValue;
                property.DisplayName = propertyTag.Get("DisplayName").StringValue;
                property.Type = (Property.PropertyType)propertyTag.Get("Type").ByteValue;
                Properties.Add(property);
            }
            Passes.Clear();
            var passesTag = value.Get("Passes");
            foreach (var passTag in passesTag.List)
            {
                ShaderPass pass = new ShaderPass();
                pass.RenderMode = passTag.Get("RenderMode").StringValue;
                pass.Vertex = passTag.Get("Vertex").StringValue;
                pass.Fragment = passTag.Get("Fragment").StringValue;
                Passes.Add(pass);
            }
            if (value.TryGet("ShadowPass", out var shadowPassTag))
            {
                ShaderShadowPass shadowPass = new ShaderShadowPass();
                shadowPass.Vertex = shadowPassTag.Get("Vertex").StringValue;
                shadowPass.Fragment = shadowPassTag.Get("Fragment").StringValue;
                ShadowPass = shadowPass;
            }
        }
    }
}