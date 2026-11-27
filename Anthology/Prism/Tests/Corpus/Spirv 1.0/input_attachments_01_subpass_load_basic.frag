#version 460 core

layout(input_attachment_index = 0, set = 0, binding = 0) uniform subpassInput inputColor;

layout(location = 0) out vec4 FragColor;

void main()
{
    vec4 color = subpassLoad(inputColor);

    // simple color grade: contrast boost and slight warm tint
    vec3 graded = (color.rgb - 0.5) * 1.2 + 0.5;
    graded *= vec3(1.05, 1.0, 0.95);

    FragColor = vec4(graded, color.a);
}
