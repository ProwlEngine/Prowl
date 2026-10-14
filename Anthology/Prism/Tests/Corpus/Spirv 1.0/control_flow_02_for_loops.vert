#version 460 core

layout(location = 0) in vec3 in_position;

void main()
{
    // ascending for loop
    float ascending_sum = 0.0;
    for (int i = 0; i < 5; i = i + 1)
    {
        ascending_sum += float(i);
    }

    // descending for loop
    float descending_sum = 0.0;
    for (int i = 5; i > 0; i = i - 1)
    {
        descending_sum += float(i);
    }

    // for loop using the comma operator to update two variables
    float comma_sum = 0.0;
    for (int i = 0, j = 10; i < 10; i = i + 1, j = j - 1)
    {
        comma_sum += float(i - j);
    }

    float total = ascending_sum + descending_sum + comma_sum;
    gl_Position = vec4(in_position * 0.01, 1.0) + vec4(total * 0.0001, 0.0, 0.0, 0.0);
}
