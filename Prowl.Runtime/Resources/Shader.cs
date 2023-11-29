using Raylib_cs;
using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Resources
{
    /// <summary>
    /// The Shader class itself doesnt do much, It stores the properties of the shader and the shader code and Keywords.
    /// This is used in conjunction with the Material class to create shader variants with the correct keywords and to render things
    /// </summary>
    public sealed class Shader : EngineObject
    {
        public class Property
        {
            public string Name = "";
            public string DisplayName = "";
            public enum PropertyType { FLOAT, VEC2, VEC3, VEC4, INTEGER, IVEC2, IVEC3, IVEC4, TEXTURE2D }
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

        public (Raylib_cs.Shader[], Raylib_cs.Shader) Compile(string[] defines)
        {
            Raylib_cs.Shader[] compiledPasses = new Raylib_cs.Shader[Passes.Count];
            for (int i = 0; i < Passes.Count; i++)
                compiledPasses[i] = CompilePass(i, defines);
            return (compiledPasses, CompileShadowPass(defines));
        }

        public Raylib_cs.Shader CompileShadowPass(string[] defines)
        {
            if(ShadowPass == null)
            {
                var defaultDepth = Shader.Find("Defaults/Depth.shader");
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

        public Raylib_cs.Shader CompilePass(int pass, string[] defines)
        {
            string frag = Passes[pass].Fragment;
            string vert = Passes[pass].Vertex;
            PrepareFragVert(ref frag, ref vert, defines);
            return CompileShader(frag, vert, "Defaults/Invalid.shader");
        }

        private Raylib_cs.Shader CompileShader(string frag, string vert, string fallback)
        {
            Raylib_cs.Shader compiled = Raylib.LoadShaderFromMemory(vert, frag);
            if (compiled.id <= 0)
            {
                var fallbackShader = Shader.Find(fallback);
                if (fallbackShader.IsAvailable)
                    return fallbackShader.Res!.CompilePass(0, []);
            }
            return compiled;
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
            frag = frag.Insert(0, $"#version 330\n");
            vert = vert.Insert(0, $"#version 330\n");
        }

        public static AssetRef<Shader> Find(string path)
        {
            return Application.AssetProvider.LoadAsset<Shader>(path);
        }

    }
}