#version 460 core

void main()
{
    // comma operator updating two loop variables at once
    int sum_i = 0;
    int sum_j = 0;

    for (int i = 0, j = 10; i < 5; i++, j--)
    {
        sum_i += i;
        sum_j += j;
    }

    gl_Position = vec4(float(sum_i), float(sum_j), 0.0, 1.0);
}
