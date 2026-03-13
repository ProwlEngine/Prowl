#version 450

layout(set = 0, binding = 0) uniform texture2D Input;
layout(set = 0, binding = 1) uniform sampler Sampler;

layout(set = 0, binding = 2) uniform Tint
{
    vec3 RGBTintColor;
    float TintFactor;
};

layout(location = 0) in vec2 fsin_uv;
layout(location = 0) out vec4 fsout_color;

void main()
{
    vec2 texCoords = fsin_uv;
    vec4 inputColor = texture(sampler2D(Input, Sampler), texCoords);
    vec4 tintedColor = vec4(inputColor.xyz * RGBTintColor, inputColor.z);
    fsout_color =  tintedColor;
}
