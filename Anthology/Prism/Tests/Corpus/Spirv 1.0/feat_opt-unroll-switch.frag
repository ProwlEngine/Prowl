#version 450
#extension GL_EXT_control_flow_attributes : require
layout(location=0) out vec4 o;
layout(location=0) flat in int sel;
layout(set=0,binding=0) buffer B { float data[]; } b;
void main(){ float s = 0.0; [[unroll]] for (int i = 0; i < 3; i++) { switch (sel + i) { case 0: s += b.data[i]; break; case 1: s -= 1.0; break; default: s *= 2.0; } } o = vec4(s); }
