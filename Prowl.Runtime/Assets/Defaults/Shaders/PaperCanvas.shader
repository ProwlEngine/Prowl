Shader "Hidden/Paper/Canvas"
{
    Pass
    {
        Name "Canvas"
        Cull Off
        ZTest Disabled
        ZWrite Off
        Blend One InverseSourceAlpha

        GLSLPROGRAM
        Vertex
        {
            in vec2 aPosition;
            in vec2 aTexCoord;
            in vec4 aColor;

            uniform mat4 projection;

            out vec2 fragTexCoord;
            out vec4 fragColor;
            out vec2 fragPos;

            void main()
            {
                fragTexCoord = aTexCoord;
                fragColor = aColor;
                fragPos = aPosition;
                gl_Position = projection * vec4(aPosition, 0.0, 1.0);
            }
        }

        Fragment
        {
            in vec2 fragTexCoord;
            in vec4 fragColor;
            in vec2 fragPos;

            out vec4 finalColor;

            uniform sampler2D texture0;
            uniform sampler2D fontTexture;

            // Packed 2D affines (A, C, B, D) plus (E, F), the framebuffer scale already folded in by Quill
            uniform vec4 scissorTransform;
            uniform vec2 scissorTranslation;
            uniform vec2 scissorExt;

            uniform vec4 brushTransform;
            uniform vec2 brushTranslation;
            uniform int brushType;
            uniform vec4 brushColor1;
            uniform vec4 brushColor2;
            uniform vec4 brushParams;
            uniform vec2 brushParams2;

            uniform vec4 textureTransform;
            uniform vec2 textureTranslation;

            uniform vec2 atlasTexelSize;
            uniform float sdfPxRange;

            uniform sampler2D backdropTexture;
            uniform sampler2D backdropSharpTexture;
            uniform vec2 viewportSize;
            uniform float backdropBlurAmount;
            uniform float backdropMix;

            vec2 applyTransform(vec4 xf, vec2 t, vec2 p) { return vec2(dot(xf.xy, p), dot(xf.zw, p)) + t; }

            float calculateBrushFactor()
            {
                vec2 transformedPoint = applyTransform(brushTransform, brushTranslation, fragPos);

                if (brushType == 1)
                {
                    vec2 startPoint = brushParams.xy; vec2 endPoint = brushParams.zw;
                    vec2 line = endPoint - startPoint; float lineLength = length(line);
                    if (lineLength < 0.001) return 0.0;
                    return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
                }
                if (brushType == 2)
                {
                    vec2 center = brushParams.xy;
                    return clamp(smoothstep(brushParams.z, brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
                }
                if (brushType == 3)
                {
                    vec2 center = brushParams.xy; vec2 halfSize = brushParams.zw;
                    float radius = brushParams2.x; float feather = brushParams2.y;
                    if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
                    vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
                    float dist = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
                    return smoothstep(-feather * 0.5, feather * 0.5, dist);
                }
                return 0.0;
            }

            float scissorMask(vec2 p)
            {
                if (scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
                vec2 smoothEdges = scissorExt - abs(applyTransform(scissorTransform, scissorTranslation, p));
                return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
            }

            // The screen space span of one distance unit of the single channel SDF font atlas
            float sdfScreenPxRange(vec2 uv)
            {
                vec2 unitRange = vec2(sdfPxRange) * atlasTexelSize;
                vec2 screenTexSize = vec2(1.0) / fwidth(uv);
                return max(0.5 * dot(unitRange, screenTexSize), 1.0);
            }

            float sampleCoverage(vec2 uv)
            {
                float d = texture(fontTexture, uv).r;
                float sd = (d - 0.5) * sdfScreenPxRange(uv);
                return clamp(sd + 0.5, 0.0, 1.0);
            }

            void main()
            {
                float mask = scissorMask(fragPos);
                vec4 color = fragColor;

                if (brushType > 0)
                    color = mix(brushColor1, brushColor2, calculateBrushFactor());

                // Text: UV.x from 2.0 up samples the distance field atlas
                if (fragTexCoord.x >= 2.0)
                {
                    finalColor = color * sampleCoverage(fragTexCoord - vec2(2.0)) * mask;
                    return;
                }

                // Edge coverage baked into the fringe vertices, 1 for the solid core and 0 at the outer edge
                float edgeAlpha = clamp(fragTexCoord.x, 0.0, 1.0);
                vec4 fill = color * texture(texture0, applyTransform(textureTransform, textureTranslation, fragPos));

                if (backdropBlurAmount > 0.0)
                {
                    vec2 uv = vec2(fragPos.x / viewportSize.x, 1.0 - fragPos.y / viewportSize.y);
                    vec3 blurred = mix(texture(backdropSharpTexture, uv).rgb, texture(backdropTexture, uv).rgb, backdropMix);
                    finalColor = vec4(blurred * (1.0 - fill.a) + fill.rgb, 1.0) * edgeAlpha * mask;
                    return;
                }

                finalColor = fill * edgeAlpha * mask;
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Blur"
        Cull Off
        ZTest Disabled
        ZWrite Off

        GLSLPROGRAM
        Shared
        {
            #pragma variant MODE DOWN UP COPY PRESENT
        }

        Vertex
        {
            out vec2 vUV;

            void main()
            {
                vec2 p = vec2(float((gl_VertexIndex << 1) & 2), float(gl_VertexIndex & 2));
                vUV = vec2(p.x, 1.0 - p.y);
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }
        }

        Fragment
        {
            uniform sampler2D _MainTex;
            uniform float _Offset;

            in vec2 vUV;
            out vec4 FragColor;

            void main()
            {
            #if defined(MODE_DOWN)
                vec2 halfpixel = (0.5 / vec2(textureSize(_MainTex, 0))) * _Offset;
                vec4 sum = texture(_MainTex, vUV) * 4.0;
                sum += texture(_MainTex, vUV - halfpixel);
                sum += texture(_MainTex, vUV + halfpixel);
                sum += texture(_MainTex, vUV + vec2(halfpixel.x, -halfpixel.y));
                sum += texture(_MainTex, vUV - vec2(halfpixel.x, -halfpixel.y));
                FragColor = sum / 8.0;
            #elif defined(MODE_UP)
                vec2 halfpixel = (0.25 / vec2(textureSize(_MainTex, 0))) * _Offset;
                vec4 sum = texture(_MainTex, vUV + vec2(-halfpixel.x * 2.0, 0.0));
                sum += texture(_MainTex, vUV + vec2(-halfpixel.x, halfpixel.y)) * 2.0;
                sum += texture(_MainTex, vUV + vec2(0.0, halfpixel.y * 2.0));
                sum += texture(_MainTex, vUV + vec2(halfpixel.x, halfpixel.y)) * 2.0;
                sum += texture(_MainTex, vUV + vec2(halfpixel.x * 2.0, 0.0));
                sum += texture(_MainTex, vUV + vec2(halfpixel.x, -halfpixel.y)) * 2.0;
                sum += texture(_MainTex, vUV + vec2(0.0, -halfpixel.y * 2.0));
                sum += texture(_MainTex, vUV + vec2(-halfpixel.x, -halfpixel.y)) * 2.0;
                FragColor = sum / 12.0;
            #elif defined(MODE_PRESENT)
                FragColor = texture(_MainTex, vec2(vUV.x, 1.0 - vUV.y));
            #else
                FragColor = texture(_MainTex, vUV);
            #endif
            }
        }
        ENDGLSL
    }
}
