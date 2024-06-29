using System;
using System.Collections.Generic;
using Veldrid;
using System.Linq;


namespace Prowl.Runtime
{
    public static class ShaderCache    
    {
        private static Dictionary<string, WeakReference<Shader>> shaderCache = new();

        internal static void Dispose()
        {
            foreach (var shader in shaderCache.Values)
                if (shader.TryGetTarget(out Shader sh))
                    sh.Destroy();
                
            shaderCache.Clear();
        }

        internal static void RegisterShader(Shader shader)
        {
            if (!shaderCache.ContainsKey(shader.Name))
                shaderCache.Add(shader.Name, new WeakReference<Shader>(shader));
        }
    }
}