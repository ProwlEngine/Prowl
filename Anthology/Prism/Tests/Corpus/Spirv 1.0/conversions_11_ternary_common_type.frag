#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int cond;
};

void main() {
    float r = (cond != 0) ? 1 : 2.0;
    fragColor = vec4(r);
}
