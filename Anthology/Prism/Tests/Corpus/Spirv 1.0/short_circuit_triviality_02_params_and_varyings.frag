#version 460 core

// A shader in/out varying and an `out` parameter are not trivial symbols to read, while a local, a
// plain global, a const and an `in`/`inout` parameter all are. Each of these operands sits on the right
// of a logical operator, which is the side that decides whether it short circuits.

layout(location = 0) in float vIn;
layout(location = 0) out vec4 FragColor;

const float LIMIT = 0.5;
float gGlobal = 0.25;

bool viaOutParam(float a, out float written, inout float both, in float plain)
{
    written = a * 2.0;

    bool fromOut = a > 0.0 && written > LIMIT;      // out param on the right: not trivial
    bool fromInOut = a > 0.0 && both > LIMIT;       // inout param: trivial
    bool fromIn = a > 0.0 && plain > LIMIT;         // in param: trivial
    bool fromVarying = a > 0.0 && vIn > LIMIT;      // varying: not trivial
    bool fromGlobal = a > 0.0 && gGlobal > LIMIT;   // plain global: trivial
    bool fromConst = a > 0.0 && LIMIT > 0.25;       // const: folds

    both = written;
    return fromOut || fromInOut || fromIn || fromVarying || fromGlobal || fromConst;
}

void main()
{
    float w = 0.0;
    float b = vIn;
    bool r = viaOutParam(vIn, w, b, gGlobal);

    // A bare local on the right is trivial, a call is not.
    bool localRight = r && w > 0.0;
    bool callRight = r && viaOutParam(b, w, b, LIMIT);

    FragColor = vec4(r ? 1.0 : 0.0, localRight ? 1.0 : 0.0, callRight ? 1.0 : 0.0, w + b);
}
