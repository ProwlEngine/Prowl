#version 450
#extension GL_EXT_terminate_invocation : require
#extension GL_EXT_demote_to_helper_invocation : require
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(set=0,binding=0) buffer B { float data[16]; } b;

void die(){ discard; } void main(){ for(int i=0;i<ii;i++, die()) { o.x+=1.0; } }
