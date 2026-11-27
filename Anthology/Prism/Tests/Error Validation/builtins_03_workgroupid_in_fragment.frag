// gl_WorkGroupID is a compute only builtin read here in a fragment shader
#version 450

layout(location=0) out vec4 outColor;

void main() {
    outColor = vec4(float(gl_WorkGroupID.x));
}
