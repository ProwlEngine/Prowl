#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int i;
    uint u;
};

void main() {
    uint a = i & u;
    uint b = i | u;
    uint c = i ^ u;
    fragColor = vec4(float(a), float(b), float(c), 1.0);
}
