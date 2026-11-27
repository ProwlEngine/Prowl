#version 310 es
#extension GL_EXT_clip_cull_distance : require
layout(location=0) in vec4 p;
void main(){ gl_Position = p; gl_ClipDistance[1] = p.x; gl_CullDistance[0] = p.y; }
