#version 460 core

layout(location=0) out vec4 FragColor;

const float global_scale = 2.0;

float scale_value(const in float value)
{
    return value * global_scale;
}

void main()
{
    const float local_offset = 0.1;
    const vec3 local_color = vec3(0.2, 0.4, 0.6);

    float result = scale_value(local_offset);
    FragColor = vec4(local_color * result, 1.0);
}
