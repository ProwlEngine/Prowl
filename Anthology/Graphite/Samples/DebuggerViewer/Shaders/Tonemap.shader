Shader "Sample/Tonemap"
{
    Pass
    {
        Name "Post"

        Cull Off
        ZTest Disabled
        ZWrite Off

        GLSLPROGRAM
        Shared
        {
            uniform sampler2D Source;
            uniform float Exposure;
        }

        Vertex
        {
            out vec2 uv;

            void main()
            {
                vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
                uv = vec2(p.x, 1.0 - p.y);
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }
        }

        Fragment
        {
            in vec2 uv;

            out vec4 target;

            void main()
            {
                vec3 hdr = texture(Source, uv).rgb * Exposure;
                vec3 aces = (hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14);
                target = vec4(pow(clamp(aces, 0.0, 1.0), vec3(1.0 / 2.2)), 1.0);
            }
        }
        ENDGLSL
    }
}
