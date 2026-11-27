#version 450
layout(location=0) out vec4 c;
void main(){ c = vec4(1); switch(1){ case 0: continue; } }
