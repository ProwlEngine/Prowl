Shader "Quill/Canvas"
{
    Pass
    {
        Name "Canvas"

        Cull Off
        ZTest Disabled
        Blend One InverseSourceAlpha

        GLSLPROGRAM
        Shared
        {
            // This file is part of the Prowl Game Engine
            // Licensed under the MIT License. See the LICENSE file in the project root for details.
            //
            // The Quill canvas shader.


            uniform mat4 projection;

            // The three transforms below are 2D affines packed as (A, C, B, D) plus (E, F), with the
            // framebuffer scale already folded into the linear part. Evaluating one is two dot products and an
            // add, against the sixteen multiply-adds a float4x4 cost, and it removes the pixel-to-logical
            // divide the fragment stage used to do three times per fragment.
            uniform vec4 scissorTransform;
            uniform vec2 scissorTranslation;
            uniform vec2 scissorExt;      // logical half-extent, half-pixel feather already added

            uniform vec4 brushTransform;
            uniform vec2 brushTranslation;
            uniform int brushType;          // 0=none, 1=linear, 2=radial, 3=box
            uniform vec4 brushColor1;
            uniform vec4 brushColor2;
            uniform vec4 brushParams;
            uniform vec2 brushParams2;

            uniform vec4 textureTransform;
            uniform vec2 textureTranslation;

            // 1 / font atlas size, so the text distance-field screen range is correct at any zoom.
            uniform vec2 atlasTexelSize;

            // Width of the signed-distance range in atlas texels, supplied by Scribe rather than hardcoded.
            uniform float sdfPxRange;

            // Backdrop blur
            uniform vec2 viewportSize;      // framebuffer size in pixels, for screen->uv mapping
            uniform float backdropBlurAmount; // > 0 when the current fill is frosted glass
            uniform int backdropFlipY;        // 1 on bottom-left-origin backends, which store the scene flipped

            uniform sampler2D texture0;
            uniform sampler2D fontTexture;     // dedicated font-atlas unit, so text batches with shapes
            uniform sampler2D backdropTexture; // blurred copy of the framebuffer behind the shape
        }

        Vertex
        {
            in vec2 POSITION0;
            in vec2 TEXCOORD0;
            in vec4 COLOR0;

            out vec2 fragTexCoord;
            out vec4 fragColor;
            out vec2 fragPos;

            void main()
            {
                fragTexCoord = TEXCOORD0;
                fragColor = COLOR0;
                fragPos = POSITION0;
                gl_Position = projection * vec4(POSITION0, 0.0, 1.0);
            }
        }

        Fragment
        {
            in vec2 fragTexCoord;
            in vec4 fragColor;
            in vec2 fragPos;

            out vec4 target;


            /// Applies a packed 2D affine to a pixel-space point.
            vec2 applyTransform(vec4 transform, vec2 translation, vec2 p)
            {
                return vec2(dot(transform.xy, p), dot(transform.zw, p)) + translation;
            }


            float calculateBrushFactor(vec2 fragPos)
            {
                vec2 transformedPoint = applyTransform(brushTransform, brushTranslation, fragPos);

                if (brushType == 1)
                {
                    vec2 startPoint = brushParams.xy;
                    vec2 endPoint = brushParams.zw;
                    vec2 line = endPoint - startPoint;
                    float lineLength = length(line);
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
                    vec2 center = brushParams.xy;
                    vec2 halfSize = brushParams.zw;
                    float radius = brushParams2.x;
                    float feather = brushParams2.y;
                    if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;

                    vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
                    float dist = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
                    return smoothstep(-feather * 0.5, feather * 0.5, dist);
                }
                return 0.0;
            }


            float scissorMask(vec2 p)
            {
                if (scissorExt.x < 0.0 || scissorExt.y < 0.0)
                    return 1.0;

                // scissorExt already carries the half-pixel feather, so the edge distance is a plain subtract.
                vec2 smoothEdges = scissorExt - abs(applyTransform(scissorTransform, scissorTranslation, p));

                return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
            }


            // Screen-space width (in pixels) that one distance-field unit spans at this fragment. Derived from
            // texture-coordinate derivatives, so glyphs stay crisp at any canvas zoom or DPI automatically.
            float sdfScreenPxRange(vec2 uv)
            {
                vec2 unitRange = vec2(sdfPxRange) * atlasTexelSize;
                vec2 screenTexSize = vec2(1.0) / fwidth(uv);

                // Per axis, then the smaller of the two. A glyph is scaled evenly so both agree and this is the
                // average either way. A quad stretched far along one axis and barely at all along the other, an
                // underline or a strikethrough, needs the range of the axis that carries the gradient: averaging
                // would hand an edge that needs a range of about one a range of about seventy, hardening it into
                // an aliased step.
                vec2 range = unitRange * screenTexSize;
                return max(min(range.x, range.y), 1.0);
            }


            void main()
            {
                float mask = scissorMask(fragPos);

                vec4 color = fragColor;

                if (brushType > 0)
                {
                    float factor = calculateBrushFactor(fragPos);
                    color = mix(brushColor1, brushColor2, factor);
                }

                // Text mode: UV >= 2.0. The glyph atlas holds a single-channel signed distance field (replicated
                // across RGB); reconstruct sharp coverage from it.
                if (fragTexCoord.x >= 2.0)
                {
                    vec2 uv = fragTexCoord - vec2(2.0);
                    float sd = texture(fontTexture, uv).r;
                    float screenPxDistance = sdfScreenPxRange(uv) * (sd - 0.5);
                    float coverage = clamp(screenPxDistance + 0.5, 0.0, 1.0);
                    target = color * coverage * mask;
                    return;
                }

                // Edge anti-aliasing: coverage is baked into the geometry (fringe vertices) and carried in
                // fragTexCoord.x (1 = solid core, 0 = outer fringe edge).
                float edgeAlpha = clamp(fragTexCoord.x, 0.0, 1.0);

                vec4 fill = color * texture(texture0, applyTransform(textureTransform, textureTranslation, fragPos));

                // Backdrop blur: composite the fill over the blurred framebuffer behind the shape.
                if (backdropBlurAmount > 0.0)
                {
                    vec2 uv = fragPos / viewportSize;
                    if (backdropFlipY == 1)
                        uv.y = 1.0 - uv.y;
                    vec3 blurred = texture(backdropTexture, uv).rgb;
                    // fill is premultiplied; over-composite it onto the opaque blurred backdrop
                    vec3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;
                    target = vec4(outRgb, 1.0) * edgeAlpha * mask;
                    return;
                }

                target = fill * edgeAlpha * mask;
            }
        }
        ENDGLSL
    }
}
