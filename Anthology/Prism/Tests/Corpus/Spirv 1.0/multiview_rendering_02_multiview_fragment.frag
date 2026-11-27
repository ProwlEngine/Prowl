#version 460 core
#extension GL_EXT_multiview : require

// gl_ViewIndex is also valid in the fragment stage under multiview, used
// here to debug-tint each eye/view with a distinct color.

layout(set = 0, binding = 0) uniform ViewTintUBO
{
    vec4 tint[2];
} viewTint;

layout(location = 0) in vec3 inNormal;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec3 n = normalize(inNormal);
    float ndotl = max(dot(n, vec3(0.0, 1.0, 0.0)), 0.0);

    vec4 tint = viewTint.tint[gl_ViewIndex];
    FragColor = vec4(tint.rgb * ndotl, tint.a);
}
