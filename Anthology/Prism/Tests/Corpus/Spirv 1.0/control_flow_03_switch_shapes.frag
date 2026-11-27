#version 460 core

// Switch segments are laid out in readable order with the default wherever it was written, so a default
// in the middle, fallthrough between cases and an empty case all move the block layout around.

layout(location = 0) flat in int mode;
layout(location = 0) out vec4 FragColor;

void main()
{
    float v = 0.0;

    switch (mode)
    {
        case 0:
            v = 1.0;
            break;
        case 1:
        case 2:            // shares the body with case 1
            v = 2.0;
            break;
        default:           // in the middle rather than last
            v = 3.0;
            break;
        case 3:
            v += 4.0;      // falls through into case 4
        case 4:
            v += 8.0;
            break;
        case 5:
            break;         // empty
    }

    // no default at all, and a switch whose last case falls out
    switch (mode + 1)
    {
        case 0: v *= 2.0; break;
        case 1: v *= 3.0;
    }

    // nested, with a break belonging to the inner one
    switch (mode)
    {
        case 0:
            for (int i = 0; i < 3; i++)
            {
                if (i == 1) break;   // breaks the loop, not the switch
                v += 0.5;
            }
            break;
        default:
            v -= 0.5;
            break;
    }

    FragColor = vec4(v, 0.0, 0.0, 1.0);
}
