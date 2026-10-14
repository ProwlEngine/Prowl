#version 460 core

#define USE_RED
#undef USE_GREEN
#define QUALITY 2

layout(location=0) out vec4 FragColor;

void main()
{
    vec3 color = vec3(0.0);

#ifdef USE_RED
    color.r = 1.0;
#else
    color.r = 0.0;
#endif

#ifndef USE_GREEN
    color.g = 0.2;
#else
    color.g = 1.0;
#endif

#if QUALITY == 1
    color.b = 0.25;
#elif QUALITY == 2
    color.b = 0.5;
#else
    color.b = 1.0;
#endif

    FragColor = vec4(color, 1.0);
}
