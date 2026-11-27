#version 450
layout(location = 0) flat in int vI;
layout(location = 1) in float vF;
layout(location = 0) out vec4 oColor;
void main() {
    float fromInt = float(vI);
    int fromFloat = int(vF);
    uint asUint = uint(vI);
    bool asBool = bool(vI);
    oColor = vec4(fromInt, float(fromFloat), float(asUint), asBool ? 1.0 : 0.0);
}
