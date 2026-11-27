#version 460 core

layout(input_attachment_index = 1, set = 0, binding = 1) uniform subpassInputMS inputColorMS;

layout(location = 0) out vec4 FragColor;

void main()
{
    // manual MSAA resolve: average samples 0-3
    vec4 s0 = subpassLoad(inputColorMS, 0);
    vec4 s1 = subpassLoad(inputColorMS, 1);
    vec4 s2 = subpassLoad(inputColorMS, 2);
    vec4 s3 = subpassLoad(inputColorMS, 3);

    vec4 resolved = (s0 + s1 + s2 + s3) * 0.25;

    FragColor = resolved;
}
