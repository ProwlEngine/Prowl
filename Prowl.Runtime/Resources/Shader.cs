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
            public string Fallback;
            public string Vertex;
            public string Fragment;
        }

        internal static string globalKeywords = "";

        public static void EnableKeyword(string keyword)
        {
            keyword = keyword.ToLower().Replace(" ", "").Replace(";", "");
            if (globalKeywords.Contains(keyword)) return;
            globalKeywords += keyword + ";";
        }

        public static void DisableKeyword(string keyword)
        {
            keyword = keyword.ToUpper().Replace(" ", "").Replace(";", "");
            if (!globalKeywords.Contains(keyword)) return;
            globalKeywords = globalKeywords.Replace(keyword + ";", "");
        }

        public static bool IsKeywordEnabled(string keyword) => globalKeywords.Contains(keyword.ToLower().Replace(" ", "").Replace(";", ""));

        public List<Property> Properties;
        public List<ShaderPass> Passes;

        public Raylib_cs.Shader[] Compile(string[] defines)
        {
            Raylib_cs.Shader[] compiledPasses = new Raylib_cs.Shader[Passes.Count];
            for (int i = 0; i < Passes.Count; i++)
                compiledPasses[i] = CompilePass(i, defines);
            return compiledPasses;
        }

        public Raylib_cs.Shader CompilePass(int pass, string[] defines)
        {
            string frag = Passes[pass].Fragment;
            string vert = Passes[pass].Vertex;

            if (string.IsNullOrEmpty(frag)) throw new Exception($"Failed to compile shader pass {pass} of {Name}. Fragment Shader is null or empty.");
            if (string.IsNullOrEmpty(vert)) throw new Exception($"Failed to compile shader pass {pass} of {Name}. Vertex Shader is null or empty.");

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

            // Compile the shader
            Raylib_cs.Shader compiled = Raylib.LoadShaderFromMemory(vert, frag);
            // Check for errors
            if (compiled.id <= 0)
            {
                Debug.LogError($"Failed to compile shader pass {pass} of {Name}. Falling back to default.");
                if (!string.IsNullOrWhiteSpace(Passes[pass].Fallback))
                {
                    var fallback = Application.AssetProvider.LoadAsset<Shader>(Passes[pass].Fallback);
                    if (fallback == null) throw new Exception($"Failed to load fallback shader {Passes[pass].Fallback} for shader {Name}");
                    return fallback.CompilePass(0, new string[] { });
                }
                else
                {
#warning A default fallback, aka the Magenta pink color
                }
            }
            return compiled;
        }

        public static AssetRef<Shader> Find(string path)
        {
            AssetRef<Shader> r = Application.AssetProvider.LoadAsset<Shader>(path);
            return r;
        }

    }
}