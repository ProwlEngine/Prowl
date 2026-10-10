using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

/// <summary>Device-wide refcounted cache so programs with identical stages and resource layouts share one <see cref="VkShader"/>.</summary>
internal sealed class VkShaderCache
{
    private readonly VkGraphicsDevice _gd;
    private readonly Dictionary<VkShaderKey, VkShader> _shaders = [];
    private readonly object _lock = new();

    public VkShaderCache(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    public VkShader Acquire(ProgramKey program, ShaderStageDescription[] stages, ResourceLayoutDescription[] layouts)
    {
        VkShaderKey key = new(program, layouts);
        lock (_lock)
        {
            if (!_shaders.TryGetValue(key, out VkShader? shader))
            {
                shader = new VkShader(_gd, key, stages, layouts);
                _shaders.Add(key, shader);
            }

            shader.RefCount++;
            return shader;
        }
    }

    public void Release(VkShader shader)
    {
        lock (_lock)
        {
            if (--shader.RefCount > 0)
                return;

            _shaders.Remove(shader.Key);
        }

        shader.Destroy();
    }
}
