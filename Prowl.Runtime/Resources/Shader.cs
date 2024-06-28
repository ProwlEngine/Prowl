using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Primitives;
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
            public RasterizerState State;
            public string Vertex;
            public string Fragment;
        }

        public class ShaderShadowPass
        {
            public RasterizerState State;
            public string Vertex;
            public string Fragment;
        }

        public struct CompiledShader(CompiledShader.Pass[] passes, CompiledShader.Pass shadowPass)
        {
            public struct Pass(RasterizerState State, GraphicsProgram Program )
            {
                public RasterizerState State = State;
                public GraphicsProgram Program = Program;
            }

            public Pass[] passes = passes;
            public Pass shadowPass = shadowPass;
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

        public CompiledShader Compile(string[] defines)
        {
            try
            {
                CompiledShader.Pass[] compiledPasses = new CompiledShader.Pass[Passes.Count];
                for (int i = 0; i < Passes.Count; i++)
                {
                    string frag = Passes[i].Fragment;
                    string vert = Passes[i].Vertex;
                    try
                    {
                        PrepareFragVert(ref frag, ref vert, defines);
                        var program = Graphics.Device.CompileProgram(frag, vert, "");
                        compiledPasses[i] = new(Passes[i].State, program);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Shader compilation failed using fallback shader, Reason: " + e.Message);

                        // We Assume Invalid exists, if it doesn't we have a bigger problem
                        var fallback = Find($"Defaults/Invalid.shader");
                        frag = fallback.Res!.Passes[0].Fragment;
                        vert = fallback.Res!.Passes[0].Vertex;
                        PrepareFragVert(ref frag, ref vert, defines);
                        compiledPasses[i] = new(new(), Graphics.Device.CompileProgram(frag, vert, ""));
                    }

                }

                CompiledShader compiledShader = new();
                compiledShader.passes = compiledPasses;

                if (ShadowPass != null)
                {
                    string frag = ShadowPass.Fragment;
                    string vert = ShadowPass.Vertex;
                    PrepareFragVert(ref frag, ref vert, defines);
                    var program = Graphics.Device.CompileProgram(frag, vert, "");
                    compiledShader.shadowPass = new(ShadowPass.State, program);
                }
                else
                {
                    // We Assume Depth exists, if it doesn't we have a bigger problem
                    var depth = Find($"Defaults/Depth.shader");
                    string frag = depth.Res!.Passes[0].Fragment;
                    string vert = depth.Res!.Passes[0].Vertex;
                    PrepareFragVert(ref frag, ref vert, defines);
                    compiledShader.shadowPass = new(new(), Graphics.Device.CompileProgram(frag, vert, ""));
                }

                return compiledShader;
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to compile shader: " + Name + " Reason: " + e.Message);
                return new();
            }
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
                passTag.Add("State", Serializer.Serialize(pass.State, ctx));
                passTag.Add("Vertex", new(pass.Vertex));
                passTag.Add("Fragment", new(pass.Fragment));
                passesTag.ListAdd(passTag);
            }
            compoundTag.Add("Passes", passesTag);
            if (ShadowPass != null)
            {
                SerializedProperty shadowPassTag = SerializedProperty.NewCompound();
                shadowPassTag.Add("State", Serializer.Serialize(ShadowPass.State, ctx));
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
                FileID = value.Get("FileID")?.UShortValue ?? 0;
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
                pass.State = Serializer.Deserialize<RasterizerState>(passTag.Get("State"), ctx);
                pass.Vertex = passTag.Get("Vertex").StringValue;
                pass.Fragment = passTag.Get("Fragment").StringValue;
                Passes.Add(pass);
            }
            if (value.TryGet("ShadowPass", out var shadowPassTag))
            {
                ShaderShadowPass shadowPass = new ShaderShadowPass();
                shadowPass.State = Serializer.Deserialize<RasterizerState>(shadowPassTag.Get("State"), ctx);
                shadowPass.Vertex = shadowPassTag.Get("Vertex").StringValue;
                shadowPass.Fragment = shadowPassTag.Get("Fragment").StringValue;
                ShadowPass = shadowPass;
            }
        }
    }
}