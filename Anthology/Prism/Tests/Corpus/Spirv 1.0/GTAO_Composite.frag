#version 460 core

layout(std140, set = 2, binding = 0) uniform PassUniforms
{
    float _Intensity;
};

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _AOTex;

layout(location = 1) in vec2 TexCoords;

layout(location = 0) out vec4 fragColor;

vec3 ApproxMultiBounce(float ao, vec3 albedo) {
    vec3 a = 2.0404 * albedo - 0.3324;
    vec3 b = 4.7951 * albedo - 0.6417;
    vec3 c = 2.7552 * albedo + 0.6903;

    return max(vec3(ao), ((ao * a - b) * ao + c) * ao);
}

void main()
{
    vec4 sceneColor = texture(_MainTex, TexCoords);
    float ao = texture(_AOTex, TexCoords).r;

    vec3 finalColor = sceneColor.rgb;

    finalColor *= ao;

    fragColor = vec4(finalColor, sceneColor.a);
}
