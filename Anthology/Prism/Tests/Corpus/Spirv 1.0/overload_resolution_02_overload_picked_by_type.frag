#version 460 core

// Same arity throughout, discriminated only by argument type, and every overload is reached. Resolving
// by count alone would pick the wrong body.

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

float scale(float v) { return v * 2.0; }
vec2 scale(vec2 v) { return v * 3.0; }
vec3 scale(vec3 v) { return v * 4.0; }

float blend(float a, float b) { return a + b; }
vec2 blend(vec2 a, vec2 b) { return a * b; }
vec2 blend(vec2 a, float b) { return a - b; }

void main()
{
    float s1 = scale(uv.x);
    vec2 s2 = scale(uv);
    vec3 s3 = scale(vec3(uv, 1.0));

    float b1 = blend(s1, 0.5);
    vec2 b2 = blend(s2, uv);
    vec2 b3 = blend(s2, s1);

    FragColor = vec4(s3.xy + b2 + b3, b1, 1.0);
}
