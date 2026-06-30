module ShaderVariables;

public struct FrameData
{
    public float4x4 prowl_MatV;
    public float4x4 prowl_MatIV;
    public float4x4 prowl_MatP;
    public float4x4 prowl_MatVP;
    public float4x4 prowl_PrevViewProj;
    public float4x4 prowl_MatIP;
    public float4x4 prowl_MatIVP;
    public float4x4 prowl_MatVP_NonJittered;

    public float3 _WorldSpaceCameraPos;
    public float _padding0;

    public float4 _ProjectionParams;
    public float4 _ScreenParams;
    public float2 _CameraJitter;
    public float2 _CameraPreviousJitter;

    public float4 _Time;
    public float4 _SinTime;
    public float4 _CosTime;
    public float4 prowl_DeltaTime;
}
public ParameterBlock<FrameData> Frame;

public struct ObjectData
{
    public float4x4 prowl_ObjectToWorld;
    public float4x4 prowl_WorldToObject;
    public float4x4 prowl_PrevObjectToWorld;
    public int _ObjectID;

    // Per-object light-probe spherical harmonics (set by the pipeline for dynamic objects).
    public float4 prowl_SHAr;
    public float4 prowl_SHAg;
    public float4 prowl_SHAb;
    public float4 prowl_SHBr;
    public float4 prowl_SHBg;
    public float4 prowl_SHBb;
    public float4 prowl_SHC;

    public property float4x4 mv { get { return mul(Frame.prowl_MatV, prowl_ObjectToWorld); } }
    public property float4x4 mvp { get { return mul(Frame.prowl_MatVP, prowl_ObjectToWorld); } }
    public property float4x4 tmv { get { return transpose(mul(Frame.prowl_MatV, prowl_ObjectToWorld)); } }
    public property float4x4 itmv { get { return transpose(mul(prowl_WorldToObject, Frame.prowl_MatIV)); } }
}
public ParameterBlock<ObjectData> Object;
