#version 450
#extension GL_ARB_separate_shader_objects : enable
#extension GL_ARB_shading_language_420pack : enable

layout(set = 0, binding = 2) uniform LightInfo
{
    vec3 LightDirection;
    float padding0;
    vec3 CameraPosition;
    float padding1;
};

layout(set = 1, binding = 0) uniform texture2DArray Tex;
layout(set = 1, binding = 1) uniform sampler Samp;

layout(location = 0) in vec3 in_Position_WorldSpace;
layout(location = 1) in vec3 in_Normal;
layout(location = 2) in vec3 in_TexCoord;

layout(location = 0) out vec4 outputColor;

layout(constant_id=102) const float AlphaOffset = 0.25f;

void main()
{
    vec4 texColor = texture(sampler2DArray(Tex, Samp), in_TexCoord);

    float diffuseIntensity = clamp(dot(in_Normal, -LightDirection), 0, 1);
    vec4 diffuseColor = texColor * diffuseIntensity;

    // Specular color
    vec4 specColor = vec4(0, 0, 0, 0);
    vec3 lightColor = vec3(1, 1, 1);
    float specPower = 5.0f;
    float specIntensity = 0.3f;
    vec3 vertexToEye = -normalize(in_Position_WorldSpace - CameraPosition);
    vec3 lightReflect = normalize(reflect(LightDirection, in_Normal));
    float specularFactor = dot(vertexToEye, lightReflect);
    if (specularFactor > 0)
    {
        specularFactor = pow(abs(specularFactor), specPower);
        specColor = vec4(lightColor * specIntensity * specularFactor, 1.0f) * texColor;
    }

    outputColor = diffuseColor + specColor;
    outputColor.a += AlphaOffset;
}
