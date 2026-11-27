#version 460 core

// `precise` on an interface (output) variable, not a local. glslang emits NoContraction on the
// value written to it; Prism throws for precise on interface variables (only local precise works).

layout(location = 0) in vec3 v_in;
layout(location = 0) precise out vec4 fragColor;

void main()
{
    fragColor = vec4(v_in.x * v_in.y + v_in.z);
}
