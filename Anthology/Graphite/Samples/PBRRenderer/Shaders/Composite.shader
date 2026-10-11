Shader "PBRRenderer/Composite"
{
    Pass
    {
        Name "Composite"

        Cull Off
        ZTest Disabled

        GLSLPROGRAM
        Shared
        {
            uniform CompositeParams
            {
                float bloomIntensity;
            };

            uniform sampler2D sceneTexture;
            uniform sampler2D bloomTexture;
        }


        Vertex
        {
            out vec2 UV;

            void main()
            {
                vec2 baseUV = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
                UV = vec2(baseUV.x, 1.0 - baseUV.y);
                gl_Position = vec4(baseUV * 2.0 - 1.0, 0.0, 1.0);
            }
        }


        Fragment
        {
            in vec2 UV;

            out vec4 target;

            void main()
            {
                vec4 scene = texture(sceneTexture, UV);
                vec4 bloom = texture(bloomTexture, UV);
                target = vec4(scene.rgb + bloom.rgb * bloomIntensity, 1.0);
            }
        }
        ENDGLSL
    }
}
