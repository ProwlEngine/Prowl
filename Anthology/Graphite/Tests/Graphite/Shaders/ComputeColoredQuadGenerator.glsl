// Fills a structured buffer with the 4 red vertices of a full screen triangle strip quad.
Compute
{
    layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

    struct ColoredVertex
    {
        vec4 Color;
        vec2 Position;
        vec2 _padding0;
    };

    layout(set = 0, binding = 0, std430) buffer OutputVertices { ColoredVertex vertices[]; } outputVertices;

    void main()
    {
        vec4 red = vec4(1, 0, 0, 1);
        outputVertices.vertices[0] = ColoredVertex(red, vec2(-1, 1), vec2(0, 0));
        outputVertices.vertices[1] = ColoredVertex(red, vec2(1, 1), vec2(0, 0));
        outputVertices.vertices[2] = ColoredVertex(red, vec2(-1, -1), vec2(0, 0));
        outputVertices.vertices[3] = ColoredVertex(red, vec2(1, -1), vec2(0, 0));
    }
}
