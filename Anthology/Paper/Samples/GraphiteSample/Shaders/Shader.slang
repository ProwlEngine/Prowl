import UVOrigin;


enum BrushType : int
{
    None = 0,
    LinearGradient = 1,
    RadialGradient = 2,
    BoxGradient = 3
};


struct VertexInput
{
    float2 aPosition : POSITION0;
    float2 aTexCoord : TEXCOORD0;
    float4 aColor : COLOR0;
};


struct VertexOutput
{
    float4 position : SV_Position;
    float2 fragTexCoord : TEXCOORD0;
    float4 fragColor : COLOR0;
    float2 fragPos : TEXCOORD1;
};


struct FragmentData
{
    float4x4 projection;
    Sampler2D<float4> texture0;
    float4x4 scissorMat;
    float2 scissorExt;

    float4x4 brushMat;
    BrushType brushType;       // 0=none, 1=linear, 2=radial, 3=box
    float4 brushColor1;
    float4 brushColor2;
    float4 brushParams;
    float2 brushParams2;

    float4x4 brushTextureMat;
    float dpiScale;

    // Backdrop blur
    Sampler2D<float4> backdropTexture;  // blurred copy of the framebuffer behind the shape
    float2 viewportSize;          // framebuffer size in pixels, for screen->uv mapping
    float backdropBlurAmount;   // > 0 when the current fill is frosted glass
};
ParameterBlock<FragmentData> DrawData;


[shader("vertex")]
VertexOutput Vertex(VertexInput input)
{
    VertexOutput output;
    output.fragTexCoord = input.aTexCoord;
    output.fragColor = input.aColor;
    output.fragPos = input.aPosition;
    output.position = mul(DrawData.projection, float4(input.aPosition, 0.0, 1.0));
    return output;
}


float calculateBrushFactor(float2 fragPos, float2 fragTexCoord) 
{
    if (DrawData.brushType == BrushType.None) 
        return 0.0;
    
    float2 logicalPos = fragPos / DrawData.dpiScale;
    float2 transformedPoint = (mul(DrawData.brushMat, float4(logicalPos, 0.0, 1.0))).xy;

    switch (DrawData.brushType)
    {
        case BrushType.LinearGradient: // linear gradient
        {
            float2 startPoint = DrawData.brushParams.xy; 
            float2 endPoint = DrawData.brushParams.zw;
            float2 line = endPoint - startPoint; 
            float lineLength = length(line);
            if (lineLength < 0.001) return 0.0;  
            return clamp(dot(transformedPoint - startPoint, line) / (lineLength * lineLength), 0.0, 1.0);
        }
        case BrushType.RadialGradient: // radial gradient
        {
            float2 center = DrawData.brushParams.xy;
            return clamp(smoothstep(DrawData.brushParams.z, DrawData.brushParams.w, length(transformedPoint - center)), 0.0, 1.0);
        }
        case BrushType.BoxGradient: // box gradient
        {
            float2 center = DrawData.brushParams.xy; float2 halfSize = DrawData.brushParams.zw;
            float radius = DrawData.brushParams2.x; float feather = DrawData.brushParams2.y;
        
            if (halfSize.x < 0.001 || halfSize.y < 0.001) 
                return 0.0;
        
            float2 q = abs(transformedPoint - center) - (halfSize - float2(radius));
            float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;

            return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
        }
        default:
            return 0.0;
    };
}


float scissorMask(float2 p) 
{
    if (DrawData.scissorExt.x < 0.0 || DrawData.scissorExt.y < 0.0) 
        return 1.0;

    float2 logicalP = p / DrawData.dpiScale;
    float2 transformedPoint = (mul(DrawData.scissorMat, float4(logicalP, 0.0, 1.0))).xy;
    float2 logicalExt = DrawData.scissorExt / DrawData.dpiScale;
    float2 distanceFromEdges = abs(transformedPoint) - logicalExt;
    float halfPixelLogical = 0.5 / DrawData.dpiScale;
    float2 smoothEdges = float2(halfPixelLogical) - distanceFromEdges;
    
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}


[shader("fragment")]
float4 Fragment(VertexOutput input) : SV_Target
{
    float mask = scissorMask(input.fragPos);

    float4 color = input.fragColor;

    if (DrawData.brushType > 0) 
    {
        float factor = calculateBrushFactor(input.fragPos, input.fragTexCoord);
        color = lerp(DrawData.brushColor1, DrawData.brushColor2, factor);
    }

    // Text mode: UV >= 2.0
    if (input.fragTexCoord.x >= 2.0) 
        return color * DrawData.texture0.Sample(input.fragTexCoord - float2(2.0)) * mask;

    // Edge anti-aliasing
    float2 pixelSize = fwidth(input.fragTexCoord);
    float2 edgeDistance = min(input.fragTexCoord, 1.0 - input.fragTexCoord);
    float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
    edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);

    float2 logicalPos = input.fragPos / DrawData.dpiScale;
    float4 fill = color * DrawData.texture0.Sample(mul(DrawData.brushTextureMat, float4(logicalPos, 0.0, 1.0)).xy);

    // Backdrop blur: composite the fill over the blurred framebuffer behind the shape.
    if (DrawData.backdropBlurAmount > 0.0) 
    {
        float2 uv = input.fragPos / DrawData.viewportSize;
        // Bottom-left-origin backends (OpenGL) store the scene flipped relative to the canvas,
        // so the sampled V is flipped there; top-left-origin backends (Vulkan, D3D11) sample upright.
        if (!IsUVOriginTopLeft)
            uv.y = 1.0 - uv.y;
        float3 blurred = DrawData.backdropTexture.Sample(uv).rgb;
        // fill is premultiplied; over-composite it onto the opaque blurred backdrop
        float3 outRgb = blurred * (1.0 - fill.a) + fill.rgb;
        
        return float4(outRgb, 1.0) * edgeAlpha * mask;
    }

    return fill * edgeAlpha * mask;
}