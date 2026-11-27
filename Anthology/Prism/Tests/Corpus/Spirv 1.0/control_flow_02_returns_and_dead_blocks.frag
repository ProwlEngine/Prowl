#version 460 core

// Every explicit return opens an unreachable post-return block, and a non-void function's trailing
// block is terminated with a value that post-processing rewrites away. Both consume ids, so where the
// returns sit changes the numbering even though the emitted code looks the same.

layout(location = 0) flat in int mode;
layout(location = 0) out vec4 FragColor;

// ends with an explicit return
float endsWithReturn(float x)
{
    if (x > 1.0) return 1.0;
    return x * 0.5;
}

// falls off the end of an if/else instead of returning at the top level
float fallsOffTheEnd(float x)
{
    if (x > 1.0)
        return 1.0;
    else
        return x * 0.25;
}

// several returns, some inside loops
float manyReturns(float x, int n)
{
    for (int i = 0; i < n; i++)
    {
        if (x > float(i)) return float(i);
        if (x < 0.0) return -1.0;
        x += 0.5;
    }
    return x;
}

// void, ends without a return
void writesOut(float x, out float o)
{
    o = x;
    if (x > 0.0)
        o = x * 2.0;
}

// void with an explicit early return
void earlyOut(float x, out float o)
{
    o = 0.0;
    if (x < 0.0) return;
    o = x;
}

void main()
{
    float a = endsWithReturn(float(mode));
    float b = fallsOffTheEnd(a);
    float c = manyReturns(b, mode);
    float d, e;
    writesOut(c, d);
    earlyOut(d, e);
    FragColor = vec4(a, b, c, d + e);
}
