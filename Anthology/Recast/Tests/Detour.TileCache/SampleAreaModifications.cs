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

using Prowl.Recast;

namespace Prowl.Recast.Detour.TileCache.Tests;

public class SampleAreaModifications
{
    public const int SAMPLE_POLYAREA_TYPE_MASK = 0x07;

    /// Value for the kind of ceil "ground"
    public const int SAMPLE_POLYAREA_TYPE_GROUND = 0x1;

    /// Value for the kind of ceil "water"
    public const int SAMPLE_POLYAREA_TYPE_WATER = 0x2;

    /// Value for the kind of ceil "road"
    public const int SAMPLE_POLYAREA_TYPE_ROAD = 0x3;

    /// Value for the kind of ceil "grass"
    public const int SAMPLE_POLYAREA_TYPE_GRASS = 0x4;

    /// Flag for door area. Can be combined with area types and jump flag.
    public const int SAMPLE_POLYAREA_FLAG_DOOR = 0x08;

    /// Flag for jump area. Can be combined with area types and door flag.
    public const int SAMPLE_POLYAREA_FLAG_JUMP = 0x10;

    public static readonly RcAreaModification SAMPLE_AREAMOD_GROUND = new RcAreaModification(SAMPLE_POLYAREA_TYPE_GROUND, SAMPLE_POLYAREA_TYPE_MASK);
    public static readonly RcAreaModification SAMPLE_AREAMOD_WATER = new RcAreaModification(SAMPLE_POLYAREA_TYPE_WATER, SAMPLE_POLYAREA_TYPE_MASK);
    public static readonly RcAreaModification SAMPLE_AREAMOD_ROAD = new RcAreaModification(SAMPLE_POLYAREA_TYPE_ROAD, SAMPLE_POLYAREA_TYPE_MASK);
    public static readonly RcAreaModification SAMPLE_AREAMOD_GRASS = new RcAreaModification(SAMPLE_POLYAREA_TYPE_GRASS, SAMPLE_POLYAREA_TYPE_MASK);
    public static readonly RcAreaModification SAMPLE_AREAMOD_DOOR = new RcAreaModification(SAMPLE_POLYAREA_FLAG_DOOR, SAMPLE_POLYAREA_FLAG_DOOR);
    public static readonly RcAreaModification SAMPLE_AREAMOD_JUMP = new RcAreaModification(SAMPLE_POLYAREA_FLAG_JUMP, SAMPLE_POLYAREA_FLAG_JUMP);
}