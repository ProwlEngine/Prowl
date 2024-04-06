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
            try
            {
                Debug.Log($"Compiling Shader {Name}");
                return Compile(vert, "", frag);
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
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
            uint shaderProgram = Graphics.Device.CreateProgram();

            // Initialize compilation log info variables
            int statusCode = -1;
            string info = string.Empty;

            // Create vertex shader if requested
            if (!string.IsNullOrEmpty(vertexSource))
            {
                // Create and compile the shader
                uint vertexShader = Graphics.Device.CreateShader(ShaderType.VertexShader);
                Graphics.Device.ShaderSource(vertexShader, vertexSource);
                Graphics.Device.CompileShader(vertexShader);

                // Check the compile log
                Graphics.Device.GetShaderInfoLog(vertexShader, out info);
                Graphics.Device.GetShader(vertexShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1)
                {
                    // Delete every handles when compilation failed
                    Graphics.Device.DeleteShader(vertexShader);
                    Graphics.Device.DeleteProgram(shaderProgram);

                    throw new InvalidOperationException("Failed to Compile Vertex Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                Graphics.Device.AttachShader(shaderProgram, vertexShader);
                Graphics.Device.DeleteShader(vertexShader);
            }

            // Create geometry shader if requested
            if (!string.IsNullOrEmpty(geometrySource))
            {
                // Create and compile the shader
                uint geometryShader = Graphics.Device.CreateShader(ShaderType.GeometryShader);
                Graphics.Device.ShaderSource(geometryShader, geometrySource);
                Graphics.Device.CompileShader(geometryShader);

                // Check the compile log
                Graphics.Device.GetShaderInfoLog(geometryShader, out info);
                Graphics.Device.GetShader(geometryShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1)
                {
                    // Delete every handles when compilation failed
                    Graphics.Device.DeleteShader(geometryShader);
                    Graphics.Device.DeleteProgram(shaderProgram);

                    throw new InvalidOperationException("Failed to Compile Geometry Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                Graphics.Device.AttachShader(shaderProgram, geometryShader);
                Graphics.Device.DeleteShader(geometryShader);
            }

            // Create fragment shader if requested
            if (!string.IsNullOrEmpty(fragmentSource))
            {
                // Create and compile the shader
                uint fragmentShader = Graphics.Device.CreateShader(ShaderType.FragmentShader);
                Graphics.Device.ShaderSource(fragmentShader, fragmentSource);
                Graphics.Device.CompileShader(fragmentShader);

                // Check the compile log
                Graphics.Device.GetShaderInfoLog(fragmentShader, out info);
                Graphics.Device.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out statusCode);

                // Check the compile log
                if (statusCode != 1)
                {
                    // Delete every handles when compilation failed
                    Graphics.Device.DeleteShader(fragmentShader);
                    Graphics.Device.DeleteProgram(shaderProgram);

                    throw new InvalidOperationException("Failed to Compile Fragment Shader Source.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
                }

                // Attach the shader to the program, and delete it (not needed anymore)
                Graphics.Device.AttachShader(shaderProgram, fragmentShader);
                Graphics.Device.DeleteShader(fragmentShader);
            }

            // Link the compiled program
            Graphics.Device.LinkProgram(shaderProgram);

            // Check for link status
            Graphics.Device.GetProgramInfoLog(shaderProgram, out info);
            Graphics.Device.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus, out statusCode);
            if (statusCode != 1)
            {
                // Delete the handles when failed to link the program
                Graphics.Device.DeleteProgram(shaderProgram);

                throw new InvalidOperationException("Failed to Link Shader Program.\n" +
                        info + "\n\n" +
                        "Status Code: " + statusCode.ToString());
            }

            // Force an OpenGL flush, so that the shader will appear updated
            // in all contexts immediately (solves problems in multi-threaded apps)
            Graphics.Device.Flush();

            return shaderProgram;
        }

        public static AssetRef<Shader> Find(string path)
        {
            return Application.AssetProvider.LoadAsset<Shader>(path);
        }

        public SerializedProperty Serialize(Serializer.SerializationContext ctx)
        {
            SerializedProperty compoundTag = SerializedProperty.NewCompound();
            compoundTag.Add("Name", new(Name));

            if (AssetID != Guid.Empty)
            {
                compoundTag.Add("AssetID", new SerializedProperty(AssetID.ToString()));
                if (FileID != 0)
                    compoundTag.Add("FileID", new SerializedProperty(FileID));
            }

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
            Name = value.Get("Name")?.StringValue;

            if (value.TryGet("AssetID", out var assetIDTag))
            {
                AssetID = Guid.Parse(assetIDTag.StringValue);
                FileID = value.Get("FileID").ShortValue;
            }

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