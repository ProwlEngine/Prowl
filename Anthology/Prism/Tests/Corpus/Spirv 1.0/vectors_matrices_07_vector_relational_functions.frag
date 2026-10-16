#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    vec4 a = vec4(0.1, 0.5, 0.3, 0.9);
    vec4 b = vec4(0.2, 0.5, 0.1, 0.8);

    bvec4 lt = lessThan(a, b);
    bvec4 lte = lessThanEqual(a, b);
    bvec4 gt = greaterThan(a, b);
    bvec4 gte = greaterThanEqual(a, b);
    bvec4 eq = equal(a, b);
    bvec4 neq = notEqual(a, b);

    bool any_lt = any(lt);
    bool all_gte = all(gte);
    bvec4 inverted_eq = not(eq);

    float mask = 0.0;
    mask += any_lt ? 1.0 : 0.0;
    mask += all_gte ? 1.0 : 0.0;
    mask += any(inverted_eq) ? 1.0 : 0.0;
    mask += any(lte) ? 1.0 : 0.0;
    mask += any(gt) ? 1.0 : 0.0;
    mask += any(neq) ? 1.0 : 0.0;

    FragColor = vec4(mask * 0.1, mask * 0.2, mask * 0.05, 1.0);
}
