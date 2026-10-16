#version 460

#define A
#define B

layout(location = 0) out vec4 fragColor;

void main() {
#if defined(A) && defined B && !defined(C)
    fragColor = vec4(1.0, 1.0, 1.0, 1.0);
#else
    fragColor = vec4(0.0, 0.0, 0.0, 1.0);
#endif
}
