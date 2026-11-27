#version 450
#extension GL_NV_viewport_array2 : require
layout(location=0) in int i;
void main(){
    gl_ViewportMask[i] = 1;
    gl_Position = vec4(0);
}
