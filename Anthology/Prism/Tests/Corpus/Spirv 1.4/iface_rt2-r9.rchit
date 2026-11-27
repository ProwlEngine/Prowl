#version 460
#extension GL_EXT_ray_tracing : require
#extension GL_EXT_ray_tracing_position_fetch : require
layout(location=0) rayPayloadInEXT vec3 p;
void main(){ p = gl_HitTriangleVertexPositionsEXT[1] + gl_ObjectToWorld3x4EXT[0].xyz + gl_WorldToObjectEXT[3]; }
