import transformers;


// Main entry-point. Applies the transformation encoded by `transformer`
// to all elements in `buffer`.
[shader("compute")]
[numthreads(4,1,1)]
void computeMain(
    uint3 sv_dispatchThreadID : SV_DispatchThreadID,
    uniform RWStructuredBuffer<float> buffer)
{
    var input = buffer[sv_dispatchThreadID.x];

    ITransformer transformer = GetTransformer(sv_dispatchThreadID.x, input);

    buffer[sv_dispatchThreadID.x] = transformer.transform(input);
}


ITransformer GetTransformer(uint type, float constVal)
{
    switch (type)
    {
        default:
            BaseTransformer base = new BaseTransformer();
            return base;

        case 0:
            AddTransformer add = new AddTransformer(constVal);
            return add;

        case 1:
            MulTransformer mul = new MulTransformer(constVal);
            return mul;

        case 2:
            DivTransformer div = new DivTransformer(constVal);
            return div;
    };
}
