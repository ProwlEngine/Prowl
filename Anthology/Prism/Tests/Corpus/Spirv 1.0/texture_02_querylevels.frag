#version 460

layout(set=0, binding=0) uniform sampler2D tex2d;

layout(location=0) out vec4 fragColor;

void main() {
    int levels = textureQueryLevels(tex2d);
    fragColor = vec4(float(levels), 0.0, 0.0, 1.0);
}
