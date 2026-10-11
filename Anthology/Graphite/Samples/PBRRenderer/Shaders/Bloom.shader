Shader "PBRRenderer/Bloom"
{
    Pass
    {
        Name "Bloom"

        Cull Off
        ZTest Disabled

        GLSLPROGRAM
        Shared
        {
            #pragma variant Upsample


            uniform BloomParams
            {
                vec2 halfPixel;
                float offset;
            };

            uniform sampler2D sourceTexture;
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
            #ifdef Upsample
                vec4 sum = texture(sourceTexture, UV + vec2(-halfPixel.x * 2.0, 0.0) * offset);
                sum += texture(sourceTexture, UV + vec2(-halfPixel.x, halfPixel.y) * offset) * 2.0;
                sum += texture(sourceTexture, UV + vec2(0.0, halfPixel.y * 2.0) * offset);
                sum += texture(sourceTexture, UV + vec2(halfPixel.x, halfPixel.y) * offset) * 2.0;
                sum += texture(sourceTexture, UV + vec2(halfPixel.x * 2.0, 0.0) * offset);
                sum += texture(sourceTexture, UV + vec2(halfPixel.x, -halfPixel.y) * offset) * 2.0;
                sum += texture(sourceTexture, UV + vec2(0.0, -halfPixel.y * 2.0) * offset);
                sum += texture(sourceTexture, UV + vec2(-halfPixel.x, -halfPixel.y) * offset) * 2.0;
                target = sum / 12.0;
            #else
                vec4 sum = texture(sourceTexture, UV) * 4.0;
                sum += texture(sourceTexture, UV - halfPixel * offset);
                sum += texture(sourceTexture, UV + halfPixel * offset);
                sum += texture(sourceTexture, UV + vec2(halfPixel.x, -halfPixel.y) * offset);
                sum += texture(sourceTexture, UV - vec2(halfPixel.x, -halfPixel.y) * offset);
                target = sum / 8.0;
            #endif
            }
        }
        ENDGLSL
    }
}
