// Draws a full screen triangle and writes the depth ramp (y * width + x) / (width * height).
Vertex
{
    void main()
    {
        float x = -1.0 + float((gl_VertexIndex & 1) << 2);
        float y = -1.0 + float((gl_VertexIndex & 2) << 1);
        gl_Position = vec4(x, y, 0, 1);
    }
}

Fragment
{
    layout(set = 0, binding = 0, std140) uniform ExecutionTask { vec4 OutputSize; };

    void main()
    {
        vec2 pixel = gl_FragCoord.xy - vec2(0.5, 0.5);
        float xComp = pixel.x;
        float yComp = pixel.y * OutputSize.x;
        gl_FragDepth = (yComp + xComp) / (OutputSize.x * OutputSize.y);
    }
}
