#version 460 core

// A loop only gets a test block when it HAS a test, so `for(;;)` branches from the header straight to
// the body. do-while tests at the bottom instead. Each shape here exits by break or return.

layout(location = 0) flat in int steps;
layout(location = 0) out vec4 FragColor;

void main()
{
    float acc = 0.0;

    // no init, no test, no update
    int i = 0;
    for (;;)
    {
        acc += 1.0;
        i++;
        if (i >= steps) break;
    }

    // no test but an init and an update
    for (int k = 0; ; k++)
    {
        acc += 0.5;
        if (k > steps) break;
    }

    // test at the bottom
    int d = 0;
    do
    {
        acc += 0.25;
        d++;
    } while (d < steps);

    // while(true) has a test (the literal), unlike for(;;)
    int w = 0;
    while (true)
    {
        acc += 0.125;
        w++;
        if (w >= steps) break;
    }

    // continue inside a testless loop still runs the update
    for (int c = 0; ; c++)
    {
        if (c >= steps) break;
        if ((c & 1) == 0) continue;
        acc += 0.0625;
    }

    FragColor = vec4(acc, float(i), float(d), 1.0);
}
