#version 460 core

// #include "ProwlCG" resolved: no ProwlCG/ShaderVariables symbol is referenced by this
// variant (all optional effects are disabled), so nothing further to inline.

layout(location = 0) out vec4 OutputColor;

layout(location = 1) in vec2 TexCoords;

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    vec2 _Resolution;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

// ── Utility functions ─────────────────────────────

float getLuminance(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// Hash-based noise
float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void main()
{
    vec2 uv = TexCoords;

    // ── Sample color ──────────────────────────────
    vec3 color = texture(_MainTex, uv).rgb;

    float alpha = texture(_MainTex, TexCoords).a;

    OutputColor = vec4(color, alpha);
}
