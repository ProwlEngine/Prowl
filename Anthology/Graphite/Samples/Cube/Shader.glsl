Shared
{
    uniform Model
    {
        mat4 MatrixMVP;
        vec4 Color;
    };

    uniform sampler2D MainTexture;
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
        gl_Position = MatrixMVP * vec4(POSITION0, 1.0);
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
        target = texture(MainTexture, uv);
    }
}
