#version 460

#define W 800
#define H 600

layout(location = 0) out vec4 fragColor;

void main() {
#if (W > 640 && H >= 480) || W == 0
    fragColor = vec4(0.0, 1.0, 0.0, 1.0);
#else
    fragColor = vec4(1.0, 0.0, 0.0, 1.0);
#endif
}
