#version 450
layout(location=0) in vec4 v;
void main(){
    mediump float m = v.x * 0.5;
    gl_FragDepth = m;
}
