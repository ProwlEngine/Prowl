#version 450

layout(set = 0, binding = 1) uniform texture2D ReflectionMap;
layout(set = 0, binding = 2) uniform sampler ReflectionMapSampler;
layout(set = 0, binding = 3) uniform texture2D ColorMap;
layout(set = 0, binding = 4) uniform sampler ColorMapSampler;

layout(location = 0) in vec2 fsin_uv;
layout(location = 1) in vec4 fsin_position;
layout(location = 0) out vec4 fsout_color;

void main()
{
    vec4 outFragColor;
    vec2 projCoord = vec2((fsin_position.x / fsin_position.w) / 2 + 0.5, (fsin_position.y / fsin_position.w) / -2 + 0.5);
    float blurSize = 1.f / 512.f;
    vec4 color = texture(sampler2D(ColorMap, ColorMapSampler), fsin_uv);
    outFragColor = color * 0.25f;
    if (gl_FrontFacing)
    {
        vec4 reflection = vec4(0.0f, 0.0f, 0.0f, 0.0f);
        for (int x = -3; x <= 3; x++)
        {
            for (int y = -3; y <= 3; y++)
            {
                reflection += texture(sampler2D(ReflectionMap, ReflectionMapSampler), vec2(projCoord.x + x * blurSize, projCoord.y + y * blurSize)) / 49.0f;
            }
        }
        
        outFragColor += reflection * 1.5f * (color.x);
    }

    fsout_color = outFragColor;
}
