#version 460 core

layout(std140, binding=0) uniform Light
{
    vec4 color;
    vec3 position;
} lights[4];

layout(location=0) out vec4 FragColor;

void main()
{
    vec3 accum = vec3(0.0);
    for (int i = 0; i < 4; i++)
    {
        accum += lights[i].color.rgb * lights[i].position;
    }
    FragColor = vec4(accum, 1.0);
}
