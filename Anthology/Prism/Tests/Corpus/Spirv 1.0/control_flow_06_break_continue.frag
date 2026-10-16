#version 460 core

layout(location = 0) out vec4 FragColor;

layout(push_constant) uniform PushConstants
{
    int limit;
};

void main()
{
    float total = 0.0;
    bool outer_break = false;

    for (int i = 0; i < 10; i = i + 1)
    {
        if (outer_break)
        {
            break;
        }

        for (int j = 0; j < 10; j = j + 1)
        {
            if (j == 3)
            {
                continue;
            }

            if (i * j > limit)
            {
                outer_break = true;
                break;
            }

            total += 1.0;
        }
    }

    FragColor = vec4(total * 0.01, 0.0, 0.0, 1.0);
}
