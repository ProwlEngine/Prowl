// Defines the transformer interface, which implements a single
// `transform` operation.
interface ITransformer
{
    float transform(float x);
}


struct BaseTransformer : ITransformer
{
    float transform(float x) { return x; }
}


// Represents a transform function f(x) = x + c.
struct AddTransformer : ITransformer
{
    float c;

    __init(float constValue) { c = constValue; }

    float transform(float x) { return x + c + 10.0f; }
};


// Represents a transform function f(x) = x * c.
struct MulTransformer : ITransformer
{
    float c;

    __init(float constValue) { c = constValue; }

    float transform(float x) { return x * c; }
};


// Represents a transform function f(x) = x * c.
struct DivTransformer : ITransformer
{
    float c;

    __init(float constValue) { c = constValue; }

    float transform(float x) { return x / c; }
};


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