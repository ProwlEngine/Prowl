#version 460

#define MODE (2 + 1)

layout(location = 0) out vec4 fragColor;

void main() {
#if MODE == 1
    fragColor = vec4(1.0, 0.0, 0.0, 1.0);
#elif MODE == 3
    fragColor = vec4(0.0, 1.0, 0.0, 1.0);
#else
    fragColor = vec4(0.0, 0.0, 1.0, 1.0);
#endif
}
