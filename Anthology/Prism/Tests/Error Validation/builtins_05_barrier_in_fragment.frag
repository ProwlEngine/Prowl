// barrier is only valid in compute and tessellation control stages called here in a fragment shader
#version 450

layout(location=0) out vec4 outColor;

void main() {
    barrier();
    outColor = vec4(1.0);
}
