Compute
{
    layout(local_size_x = 64) in;

    uniform float Scale;
    uniform float _padding0, _padding1, _padding2;

    layout(std430) readonly buffer Input { float inputData[]; };
    layout(std430) buffer Output { float outputData[]; };

    void main()
    {
        uint id = gl_GlobalInvocationID.x;
        outputData[id] = inputData[id] * Scale + 1.0;
    }
}
