// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Dual Kawase backdrop blur, the second half of the single source of truth for Quill's shaders.
// Every backend's blur shader is generated from this file by Quill.ShaderGen on build.

module Blur;

uniform Sampler2D src;
uniform float2 halfpixel; // half a texel of the basis surface
uniform float offset;     // sample spread, i.e. blur strength

struct BlurVaryings
{
    float4 position : SV_Position;
    float2 vUV : TEXCOORD0;
};


// Fullscreen triangle; needs no vertex buffer.
[shader("vertex")]
BlurVaryings BlurVertex(uint vertexIndex : SV_VertexID)
{
    BlurVaryings output;
    float2 p = float2(float((vertexIndex << 1) & 2), float(vertexIndex & 2));
    output.vUV = p;
    output.position = float4(p * 2.0 - 1.0, 0.0, 1.0);
    return output;
}


// Samples the higher-resolution source into a half-size target.
[shader("fragment")]
float4 BlurDownsample(BlurVaryings input) : SV_Target
{
    float2 uv = input.vUV;
    float4 sum = src.Sample(uv) * 4.0;
    sum += src.Sample(uv - halfpixel * offset);
    sum += src.Sample(uv + halfpixel * offset);
    sum += src.Sample(uv + float2(halfpixel.x, -halfpixel.y) * offset);
    sum += src.Sample(uv - float2(halfpixel.x, -halfpixel.y) * offset);
    return sum / 8.0;
}


// Samples the lower-resolution source into a double-size target.
[shader("fragment")]
float4 BlurUpsample(BlurVaryings input) : SV_Target
{
    float2 uv = input.vUV;
    float4 sum = src.Sample(uv + float2(-halfpixel.x * 2.0, 0.0) * offset);
    sum += src.Sample(uv + float2(-halfpixel.x, halfpixel.y) * offset) * 2.0;
    sum += src.Sample(uv + float2(0.0, halfpixel.y * 2.0) * offset);
    sum += src.Sample(uv + float2(halfpixel.x, halfpixel.y) * offset) * 2.0;
    sum += src.Sample(uv + float2(halfpixel.x * 2.0, 0.0) * offset);
    sum += src.Sample(uv + float2(halfpixel.x, -halfpixel.y) * offset) * 2.0;
    sum += src.Sample(uv + float2(0.0, -halfpixel.y * 2.0) * offset);
    sum += src.Sample(uv + float2(-halfpixel.x, -halfpixel.y) * offset) * 2.0;
    return sum / 12.0;
}
