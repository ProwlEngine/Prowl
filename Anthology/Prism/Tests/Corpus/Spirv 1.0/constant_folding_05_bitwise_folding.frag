#version 460 core

layout(location=0) out vec4 FragColor;

void main()
{
    // bitwise constant folding on int and uint
    const int ia = 0x6;
    const int ib = 0x3;
    const int ires = (ia & ib) | (ia ^ ib) | (~ia) & 0xF;
    const int ishift = (ia << 2) ^ (ib >> 1);

    const uint ua = 6u;
    const uint ub = 3u;
    const uint ures = (ua & ub) | (ua ^ ub) | (~ua) & 0xFu;
    const uint ushift = (ua << 2u) ^ (ub >> 1u);

    const float total = float(ires) + float(ishift) + float(ures) + float(ushift);

    FragColor = vec4(total, 0.0, 0.0, 1.0);
}
