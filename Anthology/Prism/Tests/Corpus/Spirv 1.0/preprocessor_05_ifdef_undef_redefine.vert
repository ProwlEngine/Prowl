#version 460

#define FEATURE
#ifdef FEATURE
#undef FEATURE
#define FEATURE 2.0
#endif

#ifndef OTHER
#define OTHER 0.5
#endif

void main() {
    gl_Position = vec4(FEATURE, OTHER, 0.0, 1.0);
}
