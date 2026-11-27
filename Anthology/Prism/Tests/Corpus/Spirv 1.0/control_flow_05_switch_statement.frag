#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int mode;
};

void main()
{
    vec4 color = vec4(0.0);

    switch (mode)
    {
        case 0:
            color = vec4(1.0, 0.0, 0.0, 1.0);
            // intentional fallthrough into case 1
        case 1:
            color += vec4(0.0, 1.0, 0.0, 1.0);
            break;
        case 2:
            color = vec4(0.0, 0.0, 1.0, 1.0);
            break;
        default:
            color = vec4(1.0, 1.0, 1.0, 1.0);
            break;
    }

    FragColor = color;
}
