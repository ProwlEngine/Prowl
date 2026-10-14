#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    float values[3] = float[](1.0, 2.0, 3.0);
    ivec3 indices[2] = ivec3[](ivec3(1, 2, 3), ivec3(4, 5, 6));

    float total = values[0] + values[1] + values[2];
    ivec3 combined_index = indices[0] + indices[1];

    FragColor = vec4(total * 0.1, float(combined_index.x) * 0.05, float(combined_index.y) * 0.05, 1.0);
}
