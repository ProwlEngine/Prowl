// -----------------------------------------------------------------------------
// SMAA (Subpixel Morphological Anti-Aliasing) - reference implementation, HIGH preset
// (16 search steps, 8 diagonal search steps, corner rounding, diagonal + corner
// detection enabled). Predication, temporal reprojection and MSAA-separate passes
// are not used by Prowl and were dropped during the Slang port.
//
// Ported from the upstream SMAA.hlsl at https://github.com/iryoku/smaa. The
// AreaTex/SearchTex lookup textures are uploaded by SMAALookupTextures in upstream
// orientation (Prowl must NOT apply turol/smaaDemo's OpenGL row-flip; flipping
// blends diagonals on the wrong side - see that class).
//
// Copyright (C) 2013 Jorge Jimenez   (jorge@iryoku.com)
// Copyright (C) 2013 Jose I. Echevarria (joseignacioechevarria@gmail.com)
// Copyright (C) 2013 Belen Masia     (bmasia@unizar.es)
// Copyright (C) 2013 Fernando Navarro (fernandn@microsoft.com)
// Copyright (C) 2013 Diego Gutierrez (diegog@unizar.es)
//
// Distributed under the MIT License. As permitted by that license, the original
// doc-comment header has been trimmed; the copyright notice above is retained.
// Full license: https://github.com/iryoku/smaa/blob/master/LICENSE.txt
// -----------------------------------------------------------------------------

module SMAA;

// HIGH preset.
static const int SMAA_MAX_SEARCH_STEPS = 16;
static const int SMAA_MAX_SEARCH_STEPS_DIAG = 8;
static const float SMAA_CORNER_ROUNDING_NORM = 25.0 / 100.0;
static const float SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR = 2.0;

static const float SMAA_AREATEX_MAX_DISTANCE = 16.0;
static const float SMAA_AREATEX_MAX_DISTANCE_DIAG = 20.0;
static const float2 SMAA_AREATEX_PIXEL_SIZE = float2(1.0 / 160.0, 1.0 / 560.0);
static const float SMAA_AREATEX_SUBTEX_SIZE = 1.0 / 7.0;
static const float2 SMAA_SEARCHTEX_SIZE = float2(66.0, 33.0);
static const float2 SMAA_SEARCHTEX_PACKED_SIZE = float2(64.0, 16.0);

// -----------------------------------------------------------------------------
// Misc functions

void SMAAMovc(bool2 cond, inout float2 variable, float2 value)
{
    if (cond.x) variable.x = value.x;
    if (cond.y) variable.y = value.y;
}

void SMAAMovc(bool4 cond, inout float4 variable, float4 value)
{
    SMAAMovc(cond.xy, variable.xy, value.xy);
    SMAAMovc(cond.zw, variable.zw, value.zw);
}

// -----------------------------------------------------------------------------
// Vertex Shaders

public void SMAAEdgeDetectionVS(float4 rtMetrics, float2 texcoord, out float4 offset[3])
{
    offset[0] = mad(rtMetrics.xyxy, float4(-1.0, 0.0, 0.0, -1.0), texcoord.xyxy);
    offset[1] = mad(rtMetrics.xyxy, float4( 1.0, 0.0, 0.0,  1.0), texcoord.xyxy);
    offset[2] = mad(rtMetrics.xyxy, float4(-2.0, 0.0, 0.0, -2.0), texcoord.xyxy);
}

public void SMAABlendingWeightCalculationVS(float4 rtMetrics, float2 texcoord, out float2 pixcoord, out float4 offset[3])
{
    pixcoord = texcoord * rtMetrics.zw;

    // We will use these offsets for the searches later on (see @PSEUDO_GATHER4):
    offset[0] = mad(rtMetrics.xyxy, float4(-0.25, -0.125,  1.25, -0.125), texcoord.xyxy);
    offset[1] = mad(rtMetrics.xyxy, float4(-0.125, -0.25, -0.125,  1.25), texcoord.xyxy);

    // And these for the searches, they indicate the ends of the loops:
    offset[2] = mad(rtMetrics.xxyy,
        float4(-2.0, 2.0, -2.0, 2.0) * float(SMAA_MAX_SEARCH_STEPS),
        float4(offset[0].xz, offset[1].yw));
}

public void SMAANeighborhoodBlendingVS(float4 rtMetrics, float2 texcoord, out float4 offset)
{
    offset = mad(rtMetrics.xyxy, float4(1.0, 0.0, 0.0, 1.0), texcoord.xyxy);
}

// -----------------------------------------------------------------------------
// Edge Detection Pixel Shader (First Pass)

// IMPORTANT NOTICE: luma edge detection requires gamma-corrected colors, and thus
// 'colorTex' should be a non-sRGB texture.
public float2 SMAALumaEdgeDetectionPS(float threshold, float2 texcoord, float4 offset[3], Sampler2D<float4> colorTex)
{
    float3 weights = float3(0.2126, 0.7152, 0.0722);
    float L = dot(colorTex.Sample(texcoord).rgb, weights);

    float Lleft = dot(colorTex.Sample(offset[0].xy).rgb, weights);
    float Ltop  = dot(colorTex.Sample(offset[0].zw).rgb, weights);

    // We do the usual threshold:
    float4 delta;
    delta.xy = abs(L - float2(Lleft, Ltop));
    float2 edges = step(float2(threshold, threshold), delta.xy);

    // Then discard if there is no edge:
    if (dot(edges, float2(1.0, 1.0)) == 0.0)
        discard;

    // Calculate right and bottom deltas:
    float Lright = dot(colorTex.Sample(offset[1].xy).rgb, weights);
    float Lbottom = dot(colorTex.Sample(offset[1].zw).rgb, weights);
    delta.zw = abs(L - float2(Lright, Lbottom));

    // Calculate the maximum delta in the direct neighborhood:
    float2 maxDelta = max(delta.xy, delta.zw);

    // Calculate left-left and top-top deltas:
    float Lleftleft = dot(colorTex.Sample(offset[2].xy).rgb, weights);
    float Ltoptop = dot(colorTex.Sample(offset[2].zw).rgb, weights);
    delta.zw = abs(float2(Lleft, Ltop) - float2(Lleftleft, Ltoptop));

    // Calculate the final maximum delta:
    maxDelta = max(maxDelta.xy, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);

    // Local contrast adaptation:
    edges.xy *= step(float2(finalDelta, finalDelta), SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR * delta.xy);

    return edges;
}

// -----------------------------------------------------------------------------
// Diagonal Search Functions

// Allows to decode two binary values from a bilinear-filtered access.
float2 SMAADecodeDiagBilinearAccess(float2 e)
{
    e.r = e.r * abs(5.0 * e.r - 5.0 * 0.75);
    return round(e);
}

float4 SMAADecodeDiagBilinearAccess(float4 e)
{
    e.rb = e.rb * abs(5.0 * e.rb - 5.0 * 0.75);
    return round(e);
}

// These functions allows to perform diagonal pattern searches.
float2 SMAASearchDiag1(float4 rtMetrics, Sampler2D<float4> edgesTex, float2 texcoord, float2 dir, out float2 e)
{
    float4 coord = float4(texcoord, -1.0, 1.0);
    float3 t = float3(rtMetrics.xy, 1.0);
    while (coord.z < float(SMAA_MAX_SEARCH_STEPS_DIAG - 1) && coord.w > 0.9)
    {
        coord.xyz = mad(t, float3(dir, 1.0), coord.xyz);
        e = edgesTex.SampleLevel(coord.xy, 0).rg;
        coord.w = dot(e, float2(0.5, 0.5));
    }
    return coord.zw;
}

float2 SMAASearchDiag2(float4 rtMetrics, Sampler2D<float4> edgesTex, float2 texcoord, float2 dir, out float2 e)
{
    float4 coord = float4(texcoord, -1.0, 1.0);
    coord.x += 0.25 * rtMetrics.x; // See @SearchDiag2Optimization
    float3 t = float3(rtMetrics.xy, 1.0);
    while (coord.z < float(SMAA_MAX_SEARCH_STEPS_DIAG - 1) && coord.w > 0.9)
    {
        coord.xyz = mad(t, float3(dir, 1.0), coord.xyz);

        // @SearchDiag2Optimization: fetch both edges at once using bilinear filtering.
        e = edgesTex.SampleLevel(coord.xy, 0).rg;
        e = SMAADecodeDiagBilinearAccess(e);

        coord.w = dot(e, float2(0.5, 0.5));
    }
    return coord.zw;
}

// Similar to SMAAArea, this calculates the area corresponding to a certain
// diagonal distance and crossing edges 'e'.
float2 SMAAAreaDiag(Sampler2D<float4> areaTex, float2 dist, float2 e, float offset)
{
    float2 texcoord = mad(float2(SMAA_AREATEX_MAX_DISTANCE_DIAG, SMAA_AREATEX_MAX_DISTANCE_DIAG), e, dist);

    // Scale and bias for mapping to texel space:
    texcoord = mad(SMAA_AREATEX_PIXEL_SIZE, texcoord, 0.5 * SMAA_AREATEX_PIXEL_SIZE);

    // Diagonal areas are on the second half of the texture:
    texcoord.x += 0.5;

    // Move to proper place, according to the subpixel offset:
    texcoord.y += SMAA_AREATEX_SUBTEX_SIZE * offset;

    return areaTex.SampleLevel(texcoord, 0).rg;
}

// This searches for diagonal patterns and returns the corresponding weights.
float2 SMAACalculateDiagWeights(float4 rtMetrics, Sampler2D<float4> edgesTex, Sampler2D<float4> areaTex, float2 texcoord, float2 e, float4 subsampleIndices)
{
    float2 weights = float2(0.0, 0.0);

    // Search for the line ends:
    float4 d;
    float2 end;
    if (e.r > 0.0)
    {
        d.xz = SMAASearchDiag1(rtMetrics, edgesTex, texcoord, float2(-1.0, 1.0), end);
        d.x += float(end.y > 0.9);
    }
    else
        d.xz = float2(0.0, 0.0);
    d.yw = SMAASearchDiag1(rtMetrics, edgesTex, texcoord, float2(1.0, -1.0), end);

    if (d.x + d.y > 2.0) // d.x + d.y + 1 > 3
    {
        // Fetch the crossing edges:
        float4 coords = mad(float4(-d.x + 0.25, d.x, d.y, -d.y - 0.25), rtMetrics.xyxy, texcoord.xyxy);
        float4 c;
        c.xy = edgesTex.SampleLevel(coords.xy, 0, int2(-1, 0)).rg;
        c.zw = edgesTex.SampleLevel(coords.zw, 0, int2(1, 0)).rg;
        c.yxwz = SMAADecodeDiagBilinearAccess(c.xyzw);

        // Merge crossing edges at each side into a single value:
        float2 cc = mad(float2(2.0, 2.0), c.xz, c.yw);

        // Remove the crossing edge if we didn't found the end of the line:
        SMAAMovc(bool2(step(float2(0.9, 0.9), d.zw)), cc, float2(0.0, 0.0));

        // Fetch the areas for this line:
        weights += SMAAAreaDiag(areaTex, d.xy, cc, subsampleIndices.z);
    }

    // Search for the line ends:
    d.xz = SMAASearchDiag2(rtMetrics, edgesTex, texcoord, float2(-1.0, -1.0), end);
    if (edgesTex.SampleLevel(texcoord, 0, int2(1, 0)).r > 0.0)
    {
        d.yw = SMAASearchDiag2(rtMetrics, edgesTex, texcoord, float2(1.0, 1.0), end);
        d.y += float(end.y > 0.9);
    }
    else
        d.yw = float2(0.0, 0.0);

    if (d.x + d.y > 2.0) // d.x + d.y + 1 > 3
    {
        // Fetch the crossing edges:
        float4 coords = mad(float4(-d.x, -d.x, d.y, d.y), rtMetrics.xyxy, texcoord.xyxy);
        float4 c;
        c.x  = edgesTex.SampleLevel(coords.xy, 0, int2(-1, 0)).g;
        c.y  = edgesTex.SampleLevel(coords.xy, 0, int2(0, -1)).r;
        c.zw = edgesTex.SampleLevel(coords.zw, 0, int2(1, 0)).gr;
        float2 cc = mad(float2(2.0, 2.0), c.xz, c.yw);

        // Remove the crossing edge if we didn't found the end of the line:
        SMAAMovc(bool2(step(float2(0.9, 0.9), d.zw)), cc, float2(0.0, 0.0));

        // Fetch the areas for this line:
        weights += SMAAAreaDiag(areaTex, d.xy, cc, subsampleIndices.w).gr;
    }

    return weights;
}

// -----------------------------------------------------------------------------
// Horizontal/Vertical Search Functions

// This allows to determine how much length should we add in the last step of the
// searches. It takes the bilinearly interpolated edge (see @PSEUDO_GATHER4), and
// adds 0, 1 or 2, depending on which edges and crossing edges are active.
float SMAASearchLength(Sampler2D<float4> searchTex, float2 e, float offset)
{
    // The texture is flipped vertically, with left and right cases taking half
    // of the space horizontally:
    float2 scale = SMAA_SEARCHTEX_SIZE * float2(0.5, -1.0);
    float2 bias = SMAA_SEARCHTEX_SIZE * float2(offset, 1.0);

    // Scale and bias to access texel centers:
    scale += float2(-1.0, 1.0);
    bias += float2(0.5, -0.5);

    // Convert from pixel coordinates to texcoords (SMAA_SEARCHTEX_PACKED_SIZE
    // because the texture is cropped):
    scale *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;
    bias *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;

    return searchTex.SampleLevel(mad(scale, e, bias), 0).r;
}

// Horizontal/vertical search functions for the 2nd pass.
float SMAASearchXLeft(float4 rtMetrics, Sampler2D<float4> edgesTex, Sampler2D<float4> searchTex, float2 texcoord, float end)
{
    // @PSEUDO_GATHER4: this texcoord has been offset by (-0.25, -0.125) in the
    // vertex shader to sample between edge, thus fetching four edges in a row.
    float2 e = float2(0.0, 1.0);
    while (texcoord.x > end &&
           e.g > 0.8281 && // Is there some edge not activated?
           e.r == 0.0)     // Or is there a crossing edge that breaks the line?
    {
        e = edgesTex.SampleLevel(texcoord, 0).rg;
        texcoord = mad(-float2(2.0, 0.0), rtMetrics.xy, texcoord);
    }

    float offset = mad(-(255.0 / 127.0), SMAASearchLength(searchTex, e, 0.0), 3.25);
    return mad(rtMetrics.x, offset, texcoord.x);
}

float SMAASearchXRight(float4 rtMetrics, Sampler2D<float4> edgesTex, Sampler2D<float4> searchTex, float2 texcoord, float end)
{
    float2 e = float2(0.0, 1.0);
    while (texcoord.x < end &&
           e.g > 0.8281 &&
           e.r == 0.0)
    {
        e = edgesTex.SampleLevel(texcoord, 0).rg;
        texcoord = mad(float2(2.0, 0.0), rtMetrics.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(searchTex, e, 0.5), 3.25);
    return mad(-rtMetrics.x, offset, texcoord.x);
}

float SMAASearchYUp(float4 rtMetrics, Sampler2D<float4> edgesTex, Sampler2D<float4> searchTex, float2 texcoord, float end)
{
    float2 e = float2(1.0, 0.0);
    while (texcoord.y > end &&
           e.r > 0.8281 &&
           e.g == 0.0)
    {
        e = edgesTex.SampleLevel(texcoord, 0).rg;
        texcoord = mad(-float2(0.0, 2.0), rtMetrics.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(searchTex, e.gr, 0.0), 3.25);
    return mad(rtMetrics.y, offset, texcoord.y);
}

float SMAASearchYDown(float4 rtMetrics, Sampler2D<float4> edgesTex, Sampler2D<float4> searchTex, float2 texcoord, float end)
{
    float2 e = float2(1.0, 0.0);
    while (texcoord.y < end &&
           e.r > 0.8281 &&
           e.g == 0.0)
    {
        e = edgesTex.SampleLevel(texcoord, 0).rg;
        texcoord = mad(float2(0.0, 2.0), rtMetrics.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(searchTex, e.gr, 0.5), 3.25);
    return mad(-rtMetrics.y, offset, texcoord.y);
}

// Ok, we have the distance and both crossing edges. So, what are the areas at
// each side of current edge?
float2 SMAAArea(Sampler2D<float4> areaTex, float2 dist, float e1, float e2, float offset)
{
    // Rounding prevents precision errors of bilinear filtering:
    float2 texcoord = mad(float2(SMAA_AREATEX_MAX_DISTANCE, SMAA_AREATEX_MAX_DISTANCE), round(4.0 * float2(e1, e2)), dist);

    // Scale and bias for mapping to texel space:
    texcoord = mad(SMAA_AREATEX_PIXEL_SIZE, texcoord, 0.5 * SMAA_AREATEX_PIXEL_SIZE);

    // Move to proper place, according to the subpixel offset:
    texcoord.y = mad(SMAA_AREATEX_SUBTEX_SIZE, offset, texcoord.y);

    return areaTex.SampleLevel(texcoord, 0).rg;
}

// -----------------------------------------------------------------------------
// Corner Detection Functions

void SMAADetectHorizontalCornerPattern(Sampler2D<float4> edgesTex, inout float2 weights, float4 texcoord, float2 d)
{
    float2 leftRight = step(d.xy, d.yx);
    float2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;

    rounding /= leftRight.x + leftRight.y; // Reduce blending for pixels in the center of a line.

    float2 factor = float2(1.0, 1.0);
    factor.x -= rounding.x * edgesTex.SampleLevel(texcoord.xy, 0, int2(0, 1)).r;
    factor.x -= rounding.y * edgesTex.SampleLevel(texcoord.zw, 0, int2(1, 1)).r;
    factor.y -= rounding.x * edgesTex.SampleLevel(texcoord.xy, 0, int2(0, -2)).r;
    factor.y -= rounding.y * edgesTex.SampleLevel(texcoord.zw, 0, int2(1, -2)).r;

    weights *= saturate(factor);
}

void SMAADetectVerticalCornerPattern(Sampler2D<float4> edgesTex, inout float2 weights, float4 texcoord, float2 d)
{
    float2 leftRight = step(d.xy, d.yx);
    float2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;

    rounding /= leftRight.x + leftRight.y;

    float2 factor = float2(1.0, 1.0);
    factor.x -= rounding.x * edgesTex.SampleLevel(texcoord.xy, 0, int2(1, 0)).g;
    factor.x -= rounding.y * edgesTex.SampleLevel(texcoord.zw, 0, int2(1, 1)).g;
    factor.y -= rounding.x * edgesTex.SampleLevel(texcoord.xy, 0, int2(-2, 0)).g;
    factor.y -= rounding.y * edgesTex.SampleLevel(texcoord.zw, 0, int2(-2, 1)).g;

    weights *= saturate(factor);
}

// -----------------------------------------------------------------------------
// Blending Weight Calculation Pixel Shader (Second Pass)

public float4 SMAABlendingWeightCalculationPS(
    float4 rtMetrics, float2 texcoord, float2 pixcoord, float4 offset[3],
    Sampler2D<float4> edgesTex, Sampler2D<float4> areaTex, Sampler2D<float4> searchTex)
{
    // Just zero for SMAA 1x, see @SUBSAMPLE_INDICES.
    float4 subsampleIndices = float4(0.0, 0.0, 0.0, 0.0);

    float4 weights = float4(0.0, 0.0, 0.0, 0.0);

    float2 e = edgesTex.Sample(texcoord).rg;

    if (e.g > 0.0) // Edge at north
    {
        // Diagonals have both north and west edges, so searching for them in one
        // of the boundaries is enough.
        weights.rg = SMAACalculateDiagWeights(rtMetrics, edgesTex, areaTex, texcoord, e, subsampleIndices);

        // We give priority to diagonals, so if we find a diagonal we skip
        // horizontal/vertical processing.
        if (weights.r == -weights.g) // weights.r + weights.g == 0.0
        {
            float2 d;

            // Find the distance to the left:
            float3 coords;
            coords.x = SMAASearchXLeft(rtMetrics, edgesTex, searchTex, offset[0].xy, offset[2].x);
            coords.y = offset[1].y; // offset[1].y = texcoord.y - 0.25 * rtMetrics.y (@CROSSING_OFFSET)
            d.x = coords.x;

            // Now fetch the left crossing edges, two at a time using bilinear
            // filtering. Sampling at -0.25 (see @CROSSING_OFFSET) enables to
            // discern what value each edge has:
            float e1 = edgesTex.SampleLevel(coords.xy, 0).r;

            // Find the distance to the right:
            coords.z = SMAASearchXRight(rtMetrics, edgesTex, searchTex, offset[0].zw, offset[2].y);
            d.y = coords.z;

            // We want the distances to be in pixel units:
            d = abs(round(mad(rtMetrics.zz, d, -pixcoord.xx)));

            // SMAAArea below needs a sqrt, as the areas texture is compressed
            // quadratically:
            float2 sqrt_d = sqrt(d);

            // Fetch the right crossing edges:
            float e2 = edgesTex.SampleLevel(coords.zy, 0, int2(1, 0)).r;

            // Get the actual area:
            weights.rg = SMAAArea(areaTex, sqrt_d, e1, e2, subsampleIndices.y);

            // Fix corners:
            coords.y = texcoord.y;
            SMAADetectHorizontalCornerPattern(edgesTex, weights.rg, coords.xyzy, d);
        }
        else
            e.r = 0.0; // Skip vertical processing.
    }

    if (e.r > 0.0) // Edge at west
    {
        float2 d;

        // Find the distance to the top:
        float3 coords;
        coords.y = SMAASearchYUp(rtMetrics, edgesTex, searchTex, offset[1].xy, offset[2].z);
        coords.x = offset[0].x; // offset[1].x = texcoord.x - 0.25 * rtMetrics.x
        d.x = coords.y;

        // Fetch the top crossing edges:
        float e1 = edgesTex.SampleLevel(coords.xy, 0).g;

        // Find the distance to the bottom:
        coords.z = SMAASearchYDown(rtMetrics, edgesTex, searchTex, offset[1].zw, offset[2].w);
        d.y = coords.z;

        // We want the distances to be in pixel units:
        d = abs(round(mad(rtMetrics.ww, d, -pixcoord.yy)));

        float2 sqrt_d = sqrt(d);

        // Fetch the bottom crossing edges:
        float e2 = edgesTex.SampleLevel(coords.xz, 0, int2(0, 1)).g;

        // Get the area for this direction:
        weights.ba = SMAAArea(areaTex, sqrt_d, e1, e2, subsampleIndices.x);

        // Fix corners:
        coords.x = texcoord.x;
        SMAADetectVerticalCornerPattern(edgesTex, weights.ba, coords.xyxz, d);
    }

    return weights;
}

// -----------------------------------------------------------------------------
// Neighborhood Blending Pixel Shader (Third Pass)

public float4 SMAANeighborhoodBlendingPS(float4 rtMetrics, float2 texcoord, float4 offset, Sampler2D<float4> colorTex, Sampler2D<float4> blendTex)
{
    // Fetch the blending weights for current pixel:
    float4 a;
    a.x = blendTex.Sample(offset.xy).a; // Right
    a.y = blendTex.Sample(offset.zw).g; // Top
    a.wz = blendTex.Sample(texcoord).xz; // Bottom / Left

    // Is there any blending weight with a value greater than 0.0?
    if (dot(a, float4(1.0, 1.0, 1.0, 1.0)) < 1e-5)
    {
        return colorTex.SampleLevel(texcoord, 0);
    }

    bool h = max(a.x, a.z) > max(a.y, a.w); // max(horizontal) > max(vertical)

    // Calculate the blending offsets:
    float4 blendingOffset = float4(0.0, a.y, 0.0, a.w);
    float2 blendingWeight = a.yw;
    SMAAMovc(bool4(h, h, h, h), blendingOffset, float4(a.x, 0.0, a.z, 0.0));
    SMAAMovc(bool2(h, h), blendingWeight, a.xz);
    blendingWeight /= dot(blendingWeight, float2(1.0, 1.0));

    // Calculate the texture coordinates:
    float4 blendingCoord = mad(blendingOffset, float4(rtMetrics.xy, -rtMetrics.xy), texcoord.xyxy);

    // We exploit bilinear filtering to mix current pixel with the chosen neighbor:
    float4 color = blendingWeight.x * colorTex.SampleLevel(blendingCoord.xy, 0);
    color += blendingWeight.y * colorTex.SampleLevel(blendingCoord.zw, 0);

    return color;
}
