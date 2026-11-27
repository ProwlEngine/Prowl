#version 460 core

// A non-scalar operand cannot short circuit at all, so it counts as trivial outright. A constant index
// is a trivial operation; a dynamic one is not. any()/all() are trivial over a leaf.

layout(location = 0) flat in int idx;
layout(location = 0) out vec4 FragColor;

void main()
{
    bool arr[4] = bool[4](true, false, true, false);
    bvec2 pair = bvec2(true, false);
    vec4 v = vec4(0.25, 0.5, 0.75, 1.0);

    bool constIndex = arr[0] && arr[2];      // constant index: trivial
    bool dynIndex = arr[0] && arr[idx & 3];  // dynamic index: not trivial
    bool anyRight = arr[1] || any(pair);     // any() over a leaf: trivial
    bool allRight = arr[1] || all(pair);
    bool swizzleRight = arr[3] || v.z > 0.5; // a swizzle is not a trivial leaf

    float o = 0.0;
    if (constIndex) o += 1.0;
    if (dynIndex) o += 2.0;
    if (anyRight) o += 4.0;
    if (allRight) o += 8.0;
    if (swizzleRight) o += 16.0;

    FragColor = vec4(o, v.xy, 1.0);
}
