/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
Prowl.Recast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/


namespace Prowl.Recast.Detour.Crowd.Tests;


public class Crowd4VelocityTest : AbstractCrowdTest
{
    static readonly float[][] EXPECTED_A1Q3TVTA =
    {
        new[] { 6.101694f, 10.197294f, -17.678480f, 0.000000f, 0.000000f, 0.000000f },
        new[] { 6.024141f, 10.197294f, -17.589798f, -0.107331f, 0.000000f, 0.098730f },
        new[] { 6.004839f, 10.197294f, -17.554886f, -0.096506f, 0.000000f, 0.174561f },
        new[] { 5.744515f, 10.197294f, -17.309479f, -2.590961f, 0.000000f, 2.353066f },
        new[] { 5.253671f, 10.197294f, -16.842125f, -2.534360f, 0.000000f, 2.413922f },
        new[] { 4.789658f, 10.197294f, -16.318014f, -2.320063f, 0.000000f, 2.620555f },
        new[] { 4.407527f, 10.197294f, -15.731520f, -1.910654f, 0.000000f, 2.932474f },
        new[] { 4.023476f, 10.197294f, -15.146280f, -1.920256f, 0.000000f, 2.926195f },
        new[] { 3.645756f, 10.197294f, -14.556935f, -1.888601f, 0.000000f, 2.946725f },
        new[] { 3.277534f, 10.197294f, -13.961610f, -1.841108f, 0.000000f, 2.976629f },
        new[] { 2.924562f, 10.197294f, -13.357118f, -1.764861f, 0.000000f, 3.022460f },
        new[] { 2.598673f, 10.197294f, -12.737605f, -1.629447f, 0.000000f, 3.097564f },
        new[] { 2.330214f, 10.197294f, -12.091130f, -1.342291f, 0.000000f, 3.232376f },
        new[] { 2.174726f, 10.197294f, -11.408617f, -0.777438f, 0.000000f, 3.412564f },
        new[] { 2.040282f, 10.197294f, -10.721649f, -0.672225f, 0.000000f, 3.434838f },
        new[] { 1.958181f, 10.197294f, -10.026481f, -0.410503f, 0.000000f, 3.475843f },
        new[] { 1.653389f, 10.197294f, -9.396320f, -1.523958f, 0.000000f, 3.150802f },
        new[] { 1.642715f, 10.197294f, -8.696402f, -0.053371f, 0.000000f, 3.499593f },
        new[] { 1.937517f, 10.197294f, -8.091795f, 2.033996f, 0.000000f, 2.848308f },
        new[] { 2.364934f, 10.197294f, -7.537435f, 2.137084f, 0.000000f, 2.771799f },
        new[] { 2.802262f, 10.197294f, -6.990860f, 2.186641f, 0.000000f, 2.732875f },
        new[] { 3.186367f, 10.197294f, -6.759828f, 1.617082f, 0.000000f, -0.643841f },
        new[] { 3.460433f, 10.197294f, -6.829281f, 1.002684f, 0.000000f, -1.351205f },
        new[] { 3.605715f, 10.197294f, -6.794649f, 0.726412f, 0.000000f, 0.173160f },
        new[] { 3.796394f, 10.197294f, -6.840563f, 0.953395f, 0.000000f, -0.229571f },
        new[] { 3.882745f, 10.197294f, -6.956440f, 0.431757f, 0.000000f, -0.579388f },
        new[] { 3.983807f, 10.197294f, -7.160242f, 0.505308f, 0.000000f, -1.019009f },
        new[] { 4.031534f, 10.197294f, -7.358752f, 0.238635f, 0.000000f, -0.992549f },
        new[] { 4.081295f, 10.197294f, -7.517536f, 0.248804f, 0.000000f, -0.793922f },
        new[] { 4.108567f, 10.197294f, -7.630970f, 0.136363f, 0.000000f, -0.567171f },
        new[] { 4.092495f, 10.197294f, -7.727181f, -0.080361f, 0.000000f, -0.481056f },
        new[] { 4.096027f, 10.197294f, -7.807384f, 0.017662f, 0.000000f, -0.401016f },
        new[] { 4.131466f, 10.197294f, -7.874563f, 0.177196f, 0.000000f, -0.335895f },
        new[] { 4.102508f, 10.197294f, -7.917174f, -0.144795f, 0.000000f, -0.213056f },
        new[] { 4.073549f, 10.197294f, -7.959785f, -0.144795f, 0.000000f, -0.213056f },
        new[] { 4.044590f, 10.197294f, -8.002397f, -0.144795f, 0.000000f, -0.213056f },
        new[] { 3.983432f, 10.197294f, -8.032723f, -0.305791f, 0.000000f, -0.151636f },
        new[] { 3.948404f, 10.197294f, -8.050093f, -0.175139f, 0.000000f, -0.086848f },
        new[] { 3.935988f, 10.197294f, -8.078673f, -0.062080f, 0.000000f, -0.142903f },
        new[] { 3.943177f, 10.197294f, -8.091246f, 0.035943f, 0.000000f, -0.062864f },
        new[] { 3.950365f, 10.197294f, -8.103818f, 0.035943f, 0.000000f, -0.062864f },
        new[] { 3.957554f, 10.197294f, -8.116390f, 0.035943f, 0.000000f, -0.062864f },
        new[] { 3.964742f, 10.197294f, -8.128963f, 0.035943f, 0.000000f, -0.062864f },
        new[] { 4.003838f, 10.197294f, -8.128510f, 0.195477f, 0.000000f, 0.002258f },
        new[] { 4.042933f, 10.197294f, -8.128058f, 0.195477f, 0.000000f, 0.002258f },
        new[] { 4.082028f, 10.197294f, -8.127606f, 0.195477f, 0.000000f, 0.002258f },
        new[] { 4.121124f, 10.197294f, -8.127154f, 0.195477f, 0.000000f, 0.002258f },
        new[] { 4.160219f, 10.197294f, -8.126702f, 0.195477f, 0.000000f, 0.002258f },
        new[] { 4.199315f, 10.197294f, -8.126250f, 0.195477f, 0.000000f, 0.002258f },
        new[] { 4.206211f, 10.197294f, -8.113515f, 0.034481f, 0.000000f, 0.063677f },
        new[] { 4.213107f, 10.197294f, -8.100780f, 0.034481f, 0.000000f, 0.063677f },
        new[] { 4.230340f, 10.197294f, -8.092234f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.247572f, 10.197294f, -8.083688f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.246885f, 10.197294f, -8.098154f, -0.003438f, 0.000000f, -0.072332f },
        new[] { 4.264118f, 10.197294f, -8.089608f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.281351f, 10.197294f, -8.081062f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.280663f, 10.197294f, -8.095529f, -0.003438f, 0.000000f, -0.072332f },
        new[] { 4.297896f, 10.197294f, -8.086983f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.315129f, 10.197294f, -8.078437f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.332362f, 10.197294f, -8.069891f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.322824f, 10.197294f, -8.046370f, -0.047688f, 0.000000f, 0.117607f },
        new[] { 4.322136f, 10.197294f, -8.060836f, -0.003438f, 0.000000f, -0.072332f },
        new[] { 4.321449f, 10.197294f, -8.075302f, -0.003438f, 0.000000f, -0.072332f },
        new[] { 4.320761f, 10.197294f, -8.089768f, -0.003438f, 0.000000f, -0.072332f },
        new[] { 4.337994f, 10.197294f, -8.081223f, 0.086165f, 0.000000f, 0.042728f },
        new[] { 4.328456f, 10.197294f, -8.057701f, -0.047688f, 0.000000f, 0.117607f },
        new[] { 4.327769f, 10.197294f, -8.072167f, -0.003438f, 0.000000f, -0.072332f }
    };

    [Fact]
    public void TestAgent1Quality3TVTA()
    {
        int updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS |
                          DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_VIS |
                          DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO |
                          DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE;

        AddAgentGrid(2, 0.3f, updateFlags, 3, endPoss[0]);
        SetMoveTarget(endPoss[4], false);
        for (int i = 0; i < EXPECTED_A1Q3TVTA.Length; i++)
        {
            crowd.Update(1 / 5f, null);
            if (i == 20)
            {
                SetMoveTarget(startPoss[2], true);
            }

            DtCrowdAgent ag = agents[1];
            Assert.Equal(EXPECTED_A1Q3TVTA[i][0], ag.npos.X, 0.001f);
            Assert.Equal(EXPECTED_A1Q3TVTA[i][1], ag.npos.Y, 0.001f);
            Assert.Equal(EXPECTED_A1Q3TVTA[i][2], ag.npos.Z, 0.001f);
            Assert.Equal(EXPECTED_A1Q3TVTA[i][3], ag.nvel.X, 0.001f);
            Assert.Equal(EXPECTED_A1Q3TVTA[i][4], ag.nvel.Y, 0.001f);
            Assert.Equal(EXPECTED_A1Q3TVTA[i][5], ag.nvel.Z, 0.001f);
        }
    }
}