// The renderer's bindless tables: every 2D texture, every cube and every sampler, indexed by the slots records hold.
#pragma once

#extension GL_EXT_nonuniform_qualifier : require

layout(set = 0, binding = 0) uniform texture2D Graphite_Textures2D[16384];
layout(set = 0, binding = 3) uniform textureCube Graphite_TexturesCube[2048];
layout(set = 1, binding = 0) uniform sampler Graphite_Samplers[64];

// Samplers cannot live in locals, so the tables are indexed at each read
#define GRAPHITE_SAMPLE_2D(textureSlot, samplerSlot, uv) texture(sampler2D(Graphite_Textures2D[nonuniformEXT(textureSlot)], Graphite_Samplers[nonuniformEXT(samplerSlot)]), uv)
#define GRAPHITE_SAMPLE_2D_LOD(textureSlot, samplerSlot, uv, lod) textureLod(sampler2D(Graphite_Textures2D[nonuniformEXT(textureSlot)], Graphite_Samplers[nonuniformEXT(samplerSlot)]), uv, lod)
#define GRAPHITE_SAMPLE_CUBE_LOD(textureSlot, samplerSlot, dir, lod) textureLod(samplerCube(Graphite_TexturesCube[nonuniformEXT(textureSlot)], Graphite_Samplers[nonuniformEXT(samplerSlot)]), dir, lod)
