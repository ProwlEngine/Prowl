#version 460

#define OUTER 1
#define INNER 1

layout(location = 0) out vec4 fragColor;

void main() {
#if OUTER == 1
  #if INNER == 1
    fragColor = vec4(0.2, 0.4, 0.6, 1.0);
  #else
    fragColor = vec4(0.6, 0.4, 0.2, 1.0);
  #endif
#else
    fragColor = vec4(0.0, 0.0, 0.0, 1.0);
#endif
}
