#version 450
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
void main(){ for(int i=0;i<ii;i++){ if(i==1) { continue; } else if(i==2) { break; } else { o.x+=1.0; } } }
