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