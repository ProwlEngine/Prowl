#version 310 es
#extension GL_ANDROID_extension_pack_es31a : require
precision mediump float;
layout(location=0) in Inputs { vec4 color; } ins;
layout(location=0) out vec4 o;
void main(){ o = ins.color; }
