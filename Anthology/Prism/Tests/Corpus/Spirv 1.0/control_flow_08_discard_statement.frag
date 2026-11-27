#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    float discard_threshold;
};

void main()
{
    if (gl_FragCoord.x < discard_threshold)
    {
        discard;
    }

    FragColor = vec4(1.0, 1.0, 1.0, 1.0);
}
