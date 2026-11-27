#version 460 core

layout(input_attachment_index = 0, set = 0, binding = 0) uniform subpassInput inputAlbedo;
layout(input_attachment_index = 1, set = 0, binding = 1) uniform subpassInput inputNormal;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec4 albedo = subpassLoad(inputAlbedo);
    vec4 normalEncoded = subpassLoad(inputNormal);

    // decode normal from [0,1] range back to [-1,1]
    vec3 normal = normalize(normalEncoded.rgb * 2.0 - 1.0);

    // simple deferred lighting combine using a fixed directional light
    vec3 lightDir = normalize(vec3(0.4, 0.8, 0.3));
    float ndotl = max(dot(normal, lightDir), 0.0);

    vec3 lit = albedo.rgb * (0.15 + ndotl * 0.85);

    FragColor = vec4(lit, albedo.a);
}
