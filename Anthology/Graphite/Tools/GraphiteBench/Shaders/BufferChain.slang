module BufferChain;

struct PassData
{
    float Scale;
    float _padding0;
    float _padding1;
    float _padding2;

    StructuredBuffer<float> Input;
    RWStructuredBuffer<float> Output;
};
ParameterBlock<PassData> Pass;

[shader("compute")]
[numthreads(64, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    Pass.Output[id.x] = Pass.Input[id.x] * Pass.Scale + 1.0;
}
