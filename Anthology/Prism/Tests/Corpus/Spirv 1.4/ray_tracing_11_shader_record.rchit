#version 460
#extension GL_EXT_ray_tracing : require
layout(shaderRecordEXT) buffer ShaderRecord {
    vec4 color;
    float scale;
} record;
layout(location = 0) rayPayloadInEXT vec4 payload;
void main() { payload = record.color * record.scale; }
