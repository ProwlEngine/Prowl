#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    float grid[3][4];

    for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < 4; j++)
        {
            grid[i][j] = float(i) + float(j) * 0.1;
        }
    }

    float total = 0.0;
    for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < 4; j++)
        {
            total += grid[i][j];
        }
    }

    FragColor = vec4(total * 0.05, 0.0, 0.0, 1.0);
}
