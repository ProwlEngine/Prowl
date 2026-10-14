#version 460

#define USE_PATH_A

layout(location = 0) out vec4 fragColor;

void main() {
    vec3 c;
#ifdef USE_PATH_A
    c = vec3(1.0, 0.5, 0.0);
#else
    c = vec3(0.0, 0.5, 1.0);
#endif
    fragColor = vec4(c, 1.0);
}
