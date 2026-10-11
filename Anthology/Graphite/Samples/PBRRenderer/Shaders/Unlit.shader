Shader "PBRRenderer/Unlit"
{
    Properties
    {
        _AlbedoTex ("Albedo", Texture2D) = "white" {}
        _BaseColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    Pass
    {
        Name "Unlit"
        Cull Off
        ZTest LessEqual
        ZWrite On

        GLSLPROGRAM
        Shared
        {
            uniform Mat
            {
                mat4 MatrixMVP;
                vec4 BaseColor;
            };

            uniform sampler2D AlbedoTexture;
        }

        Vertex
        {
            in vec3 POSITION0;
            in vec3 NORMAL0;
            in vec4 UV0;

            out vec2 uv;
            out vec3 normal;

            void main()
            {
                gl_Position = MatrixMVP * vec4(POSITION0, 1.0);
                vec2 texcoord = UV0.xy;
                texcoord.y = 1.0 - texcoord.y;
                uv = texcoord;
                normal = NORMAL0;
            }
        }

        Fragment
        {
            in vec2 uv;
            in vec3 normal;

            out vec4 target;

            void main()
            {
                vec4 albedo = texture(AlbedoTexture, uv);
                target = albedo * BaseColor;
            }
        }
        ENDGLSL
    }
}
