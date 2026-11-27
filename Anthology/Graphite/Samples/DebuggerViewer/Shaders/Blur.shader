Shader "Quill/Blur"
{
    Pass
    {
        Name "Blur"

        Cull Off
        ZTest Disabled
        Blend One InverseSourceAlpha

        GLSLPROGRAM
        Shared
        {
            // This file is part of the Prowl Game Engine
            // Licensed under the MIT License. See the LICENSE file in the project root for details.
            //
            // Dual Kawase backdrop blur.

            #pragma variant Upsample

            uniform sampler2D src;
            uniform vec2 halfpixel; // half a texel of the basis surface
            uniform float offset;   // sample spread, i.e. blur strength
        }

        // Fullscreen triangle; needs no vertex buffer.
        Vertex
        {
            out vec2 vUV;

            void main()
            {
                vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
                // p is bottom-left relative (matches the standard clip-space Y the backend's viewport transform
                // produces), so the top-left UV origin needs V flipped to sample the right way up.
                vUV = vec2(p.x, 1.0 - p.y);
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }
        }

        Fragment
        {
            in vec2 vUV;

            out vec4 target;


            // Samples the higher-resolution source into a half-size target.
            vec4 BlurDownsample(vec2 uv)
            {
                vec4 sum = texture(src, uv) * 4.0;
                sum += texture(src, uv - halfpixel * offset);
                sum += texture(src, uv + halfpixel * offset);
                sum += texture(src, uv + vec2(halfpixel.x, -halfpixel.y) * offset);
                sum += texture(src, uv - vec2(halfpixel.x, -halfpixel.y) * offset);
                return sum / 8.0;
            }


            // Samples the lower-resolution source into a double-size target.
            vec4 BlurUpsample(vec2 uv)
            {
                vec4 sum = texture(src, uv + vec2(-halfpixel.x * 2.0, 0.0) * offset);
                sum += texture(src, uv + vec2(-halfpixel.x, halfpixel.y) * offset) * 2.0;
                sum += texture(src, uv + vec2(0.0, halfpixel.y * 2.0) * offset);
                sum += texture(src, uv + vec2(halfpixel.x, halfpixel.y) * offset) * 2.0;
                sum += texture(src, uv + vec2(halfpixel.x * 2.0, 0.0) * offset);
                sum += texture(src, uv + vec2(halfpixel.x, -halfpixel.y) * offset) * 2.0;
                sum += texture(src, uv + vec2(0.0, -halfpixel.y * 2.0) * offset);
                sum += texture(src, uv + vec2(-halfpixel.x, -halfpixel.y) * offset) * 2.0;
                return sum / 12.0;
            }


            void main()
            {
            #ifdef Upsample
                target = BlurUpsample(vUV);
            #else
                target = BlurDownsample(vUV);
            #endif
            }
        }
        ENDGLSL
    }
}
