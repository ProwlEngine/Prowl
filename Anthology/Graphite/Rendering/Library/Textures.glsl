// The renderer's bindless tables: every 2D texture, every cube array and every sampler, indexed by the slots records hold.
#pragma once

#extension GL_EXT_nonuniform_qualifier : require

layout(set = 0, binding = 0) uniform texture2D Graphite_Textures2D[16384];
layout(set = 0, binding = 4) uniform textureCubeArray Graphite_TexturesCubeArray[2048];
layout(set = 1, binding = 0) uniform sampler Graphite_Samplers[64];

// Samplers cannot live in locals, so the tables are indexed at each read
#define GRAPHITE_SAMPLE_2D(textureSlot, samplerSlot, uv) texture(sampler2D(Graphite_Textures2D[nonuniformEXT(textureSlot)], Graphite_Samplers[nonuniformEXT(samplerSlot)]), uv)
#define GRAPHITE_SAMPLE_CUBE_ARRAY_LOD(textureSlot, samplerSlot, coord, lod) textureLod(samplerCubeArray(Graphite_TexturesCubeArray[nonuniformEXT(textureSlot)], Graphite_Samplers[nonuniformEXT(samplerSlot)]), coord, lod)
