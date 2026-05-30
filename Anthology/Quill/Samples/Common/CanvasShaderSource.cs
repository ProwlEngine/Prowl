// Common GLSL shader source for Canvas rendering, shared across OpenGL-based samples.

namespace Common
{
    /// <summary>
    /// Contains GLSL 330 vertex and fragment shader source for the Canvas rendering system
    /// </summary>
    public static class CanvasShaderSource
    {
        /// <summary>
        /// GLSL 330 vertex shader.
        /// </summary>
        public const string VertexShader = @"#version 330
uniform mat4 projection;

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec2 fragPos;

void main()
{
    fragTexCoord = aTexCoord;
    fragColor = aColor;
    fragPos = aPosition;
    gl_Position = projection * vec4(aPosition, 0.0, 1.0);
}";

        /// <summary>
        /// GLSL 330 fragment shader for Canvas rendering.
        /// </summary>
        public const string FragmentShader = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec2 fragPos;

out vec4 finalColor;

uniform sampler2D texture0;
uniform mat4 scissorMat;
uniform vec2 scissorExt;

uniform mat4 brushMat;
uniform int brushType;       // 0=none, 1=linear, 2=radial, 3=box
uniform vec4 brushColor1;
uniform vec4 brushColor2;
uniform vec4 brushParams;
uniform vec2 brushParams2;

uniform mat4 brushTextureMat;
uniform float dpiScale;

// Backdrop blur
uniform sampler2D backdropTexture;  // blurred copy of the framebuffer behind the shape
uniform vec2 viewportSize;          // framebuffer size in pixels, for screen->uv mapping
uniform float backdropBlurAmount;   // > 0 when the current fill is frosted glass

// ============== Canvas functions ==============

float calculateBrushFactor() {
    if (brushType == 0) return 0.0;
    vec2 logicalPos = fragPos / dpiScale;
    vec2 transformedPoint = (brushMat * vec4(logicalPos, 0.0, 1.0)).xy;

    if (brushType == 1) {
        vec2 startPoint = brushParams.xy; vec2 endPoint = brushParams.zw;
        vec2 line = endPoint - startPoint; float lineLength = length(line);
        if (lineLength < 0.001) return 0.0;
        return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
    }
    if (brushType == 2) {
        vec2 center = brushParams.xy;
        return clamp(smoothstep(brushParams.z, brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
    }
    if (brushType == 3) {
        vec2 center = brushParams.xy; vec2 halfSize = brushParams.zw;
        float radius = brushParams2.x; float feather = brushParams2.y;
        if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
        vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
        float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;
        return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
    }
    return 0.0;
}

float scissorMask(vec2 p) {
    if(scissorExt.x < 0.0 || scissorExt.y < 0.0) return 1.0;
    vec2 logicalP = p / dpiScale;
    vec2 transformedPoint = (scissorMat * vec4(logicalP, 0.0, 1.0)).xy;
    vec2 logicalExt = scissorExt / dpiScale;
    vec2 distanceFromEdges = abs(transformedPoint) - logicalExt;
    float halfPixelLogical = 0.5 / dpiScale;
    vec2 smoothEdges = vec2(halfPixelLogical) - distanceFromEdges;
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}

void main()
{
    float mask = scissorMask(fragPos);
    vec4 color = fragColor;

    if (brushType > 0) {
        float factor = calculateBrushFactor();
        color = mix(brushColor1, brushColor2, factor);
    }

    // Text mode: UV >= 2.0
    if (fragTexCoord.x >= 2.0) {
        finalColor = color * texture(texture0, fragTexCoord - vec2(2.0)) * mask;
        return;
    }

    // Edge anti-aliasing
    vec2 pixelSize = fwidth(fragTexCoord);
    vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);

    vec2 logicalPos = fragPos / dpiScale;
    vec4 fill = color * texture(texture0, (brushTextureMat * vec4(logicalPos, 0.0, 1.0)).xy);

    // Backdrop blur: composite the fill over the blurred framebuffer behind the shape.
    if (backdropBlurAmount > 0.0) {
        vec2 uv = fragPos / viewportSize;
        uv.y = 1.0 - uv.y;  // framebuffer origin is bottom-left
        vec3 blurred = texture(backdropTexture, uv).rgb;
        // fill is premultiplied; over-composite it onto the opaque blurred backdrop
        vec3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;
        finalColor = vec4(outRgb, 1.0) * edgeAlpha * mask;
        return;
    }

    finalColor = fill * edgeAlpha * mask;
}";

        /// <summary>
        /// Fullscreen-triangle vertex shader for the backdrop blur passes. No vertex buffer needed.
        /// </summary>
        public const string BlurVertexShader = @"#version 330
out vec2 vUV;
void main()
{
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vUV = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

        /// <summary>
        /// Dual Kawase downsample pass. Samples the higher-resolution source into a half-size target.
        /// 'halfpixel' is half a texel of the source; 'offset' scales the sample spread (blur strength).
        /// </summary>
        public const string BlurDownsampleShader = @"#version 330
in vec2 vUV;
out vec4 frag;
uniform sampler2D src;
uniform vec2 halfpixel;
uniform float offset;

void main()
{
    vec4 sum = texture(src, vUV) * 4.0;
    sum += texture(src, vUV - halfpixel.xy * offset);
    sum += texture(src, vUV + halfpixel.xy * offset);
    sum += texture(src, vUV + vec2(halfpixel.x, -halfpixel.y) * offset);
    sum += texture(src, vUV - vec2(halfpixel.x, -halfpixel.y) * offset);
    frag = sum / 8.0;
}";

        /// <summary>
        /// Dual Kawase upsample pass. Samples the lower-resolution source into a double-size target.
        /// 'halfpixel' is half a texel of the target; 'offset' scales the sample spread (blur strength).
        /// </summary>
        public const string BlurUpsampleShader = @"#version 330
in vec2 vUV;
out vec4 frag;
uniform sampler2D src;
uniform vec2 halfpixel;
uniform float offset;

void main()
{
    vec4 sum = texture(src, vUV + vec2(-halfpixel.x * 2.0, 0.0) * offset);
    sum += texture(src, vUV + vec2(-halfpixel.x, halfpixel.y) * offset) * 2.0;
    sum += texture(src, vUV + vec2(0.0, halfpixel.y * 2.0) * offset);
    sum += texture(src, vUV + vec2(halfpixel.x, halfpixel.y) * offset) * 2.0;
    sum += texture(src, vUV + vec2(halfpixel.x * 2.0, 0.0) * offset);
    sum += texture(src, vUV + vec2(halfpixel.x, -halfpixel.y) * offset) * 2.0;
    sum += texture(src, vUV + vec2(0.0, -halfpixel.y * 2.0) * offset);
    sum += texture(src, vUV + vec2(-halfpixel.x, -halfpixel.y) * offset) * 2.0;
    frag = sum / 12.0;
}";
    }
}
