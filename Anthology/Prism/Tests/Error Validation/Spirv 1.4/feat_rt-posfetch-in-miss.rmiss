#version 460
#extension GL_EXT_ray_tracing : require
#extension GL_EXT_ray_tracing_position_fetch : require
layout(location=0) rayPayloadInEXT vec3 p;
void main(){
    p = gl_HitTriangleVertexPositionsEXT[0];
}
