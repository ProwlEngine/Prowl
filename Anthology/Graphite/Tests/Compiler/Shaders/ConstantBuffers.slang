// Criterion: shaders that use raw constant buffer blocks. Two ConstantBuffer<T> globals with mixed
// scalar / vector / matrix / int fields, so the reflector must recover each block's field layout
// (names, offsets, sizes, scalar types) as well as its binding slot.
module ConstantBuffers;

struct Camera
{
    float4x4 viewProj;
    float3 cameraPos;
    float time;
}

struct Material
{
    float4 baseColor;
    float2 tiling;
    int flags;
}

ConstantBuffer<Camera> camera;
ConstantBuffer<Material> material;

[shader("vertex")]
float4 vertex(float3 position : POSITION) : SV_Position
{
    return mul(camera.viewProj, float4(position, 1)) + camera.time;
}

[shader("fragment")]
float4 fragment(float2 uv : UV0) : SV_Target
{
    return material.baseColor
        + float4(uv * material.tiling, camera.cameraPos.z, float(material.flags));
}
