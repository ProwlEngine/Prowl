#version 450
layout(location=0) out vec4 o;
layout(location=0) flat in int ii;
layout(location=1) in float ff;
layout(set=0,binding=0) buffer B { float data[16]; } b;

void main(){ o=vec4(0); for(int i=0;i<ii;i++){ if (b.data[i] > 0.0) { o.x += 1.0; } else { break; } } }
