#version 460 core

// Two overloads with the SAME argument count, only one of which is ever called. glslang's call graph is
// keyed by mangled name, so the unused one is deleted along with everything only it reaches.

layout(set = 0, binding = 0) uniform sampler2D depthTex;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 FragColor;

float onlyReachedByTheSamplerOverload(float x)
{
    return x * 2.0 - 1.0;
}

// Never called: this whole subtree must vanish, including the helper above.
vec3 getPos(vec2 tc, sampler2D tex)
{
    float d = texture(tex, tc).r;
    return vec3(tc, onlyReachedByTheSamplerOverload(d));
}

vec3 getPos(vec2 tc, float depth)
{
    return vec3(tc, depth);
}

void main()
{
    float depth = texture(depthTex, uv).r;
    FragColor = vec4(getPos(uv, depth), 1.0);
}
