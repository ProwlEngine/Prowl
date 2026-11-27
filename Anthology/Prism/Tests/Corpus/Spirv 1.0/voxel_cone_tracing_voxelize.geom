#version 460 core

// Conservative voxelization geometry shader.
// Projects each triangle along its dominant axis (max component of the
// face normal) so the rasterizer gets maximum coverage, then dilates the
// projected triangle outward by roughly one texel to approximate
// conservative rasterization on hardware without native support for it.

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

layout(location = 0) in vec3 vWorldPos[];
layout(location = 1) in vec3 vWorldNormal[];
layout(location = 2) in vec2 vUV[];

layout(location = 0) out vec3 gWorldPos;
layout(location = 1) out vec3 gWorldNormal;
layout(location = 2) out vec2 gUV;
layout(location = 3) flat out int gAxis;

layout(push_constant) uniform VoxelizePush
{
    vec3 volumeOrigin;  // world-space min corner of the voxel volume
    float volumeScale;  // 2.0 / (voxelSize * gridResolution), maps world extent to NDC [-1,1]
    float texelSize;    // 2.0 / gridResolution, one texel in NDC units, used for dilation
} pc;

void main()
{
    vec3 edgeA = vWorldPos[1] - vWorldPos[0];
    vec3 edgeB = vWorldPos[2] - vWorldPos[0];
    vec3 faceNormal = cross(edgeA, edgeB);
    vec3 absNormal = abs(faceNormal);

    int axis;
    if (absNormal.x >= absNormal.y && absNormal.x >= absNormal.z)
        axis = 0;
    else if (absNormal.y >= absNormal.z)
        axis = 1;
    else
        axis = 2;

    // Swizzle world-space NDC into (inPlaneA, inPlaneB, depth) so the two
    // non-dominant axes always land in xy and the dominant axis becomes
    // depth. The fragment shader mirrors this exact ordering to rebuild
    // the 3D voxel coordinate from gl_FragCoord.xy plus world position.
    vec3 projPos[3];
    for (int i = 0; i < 3; ++i)
    {
        vec3 ndc = (vWorldPos[i] - pc.volumeOrigin) * pc.volumeScale - vec3(1.0);
        if (axis == 0)
            projPos[i] = vec3(ndc.y, ndc.z, ndc.x);
        else if (axis == 1)
            projPos[i] = vec3(ndc.x, ndc.z, ndc.y);
        else
            projPos[i] = vec3(ndc.x, ndc.y, ndc.z);
    }

    vec2 p0 = projPos[0].xy;
    vec2 p1 = projPos[1].xy;
    vec2 p2 = projPos[2].xy;

    vec2 edgeDir0 = normalize(p1 - p0);
    vec2 edgeDir1 = normalize(p2 - p1);
    vec2 edgeDir2 = normalize(p0 - p2);

    vec2 normal0 = vec2(-edgeDir0.y, edgeDir0.x);
    vec2 normal1 = vec2(-edgeDir1.y, edgeDir1.x);
    vec2 normal2 = vec2(-edgeDir2.y, edgeDir2.x);

    // Orient every edge normal outward from the triangle interior.
    vec2 centroid2D = (p0 + p1 + p2) / 3.0;
    if (dot(normal0, centroid2D - p0) > 0.0) normal0 = -normal0;
    if (dot(normal1, centroid2D - p1) > 0.0) normal1 = -normal1;
    if (dot(normal2, centroid2D - p2) > 0.0) normal2 = -normal2;

    // Push each vertex outward along the two edge normals that meet there.
    vec2 dilated0 = p0 + (normal2 + normal0) * pc.texelSize;
    vec2 dilated1 = p1 + (normal0 + normal1) * pc.texelSize;
    vec2 dilated2 = p2 + (normal1 + normal2) * pc.texelSize;
    vec2 dilatedXY[3] = vec2[3](dilated0, dilated1, dilated2);

    for (int i = 0; i < 3; ++i)
    {
        gWorldPos = vWorldPos[i];
        gWorldNormal = vWorldNormal[i];
        gUV = vUV[i];
        gAxis = axis;
        gl_Position = vec4(dilatedXY[i], projPos[i].z, 1.0);
        EmitVertex();
    }
    EndPrimitive();
}
