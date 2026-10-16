Shared
{
    uniform Model
    {
        mat4 MatrixMVP;
        vec4 Color;
    };
}


Vertex
{
    in vec3 POSITION0;
    in vec2 UV0;
    in vec4 COLOR0;

    out vec2 uv;
    out vec4 color;

    void main()
    {
        gl_Position = vec4(POSITION0, 1.0);
        uv = UV0;
        color = COLOR0 * Color;
    }
}


Fragment
{
    in vec2 uv;
    in vec4 color;

    out vec4 target;

    void main()
    {
        target = vec4(uv, 0.0, 1.0);
    }
}
