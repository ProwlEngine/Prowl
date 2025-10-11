// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Runtime.Rendering;
using System;

namespace Prowl.Runtime;

public class PointLight : Light
{
    public enum Resolution : int
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }

    public Resolution shadowResolution = Resolution._512;

    public float range = 10f;

    public override void Update()
    {
        GameObject.Scene.PushLight(this);
        Debug.DrawWireSphere(Transform.position, range, color);
    }

    public override LightType GetLightType() => LightType.Point;

    public override void GetShadowMatrix(out Double4x4 view, out Double4x4 projection)
    {
        // Default implementation - will be overridden by GetShadowMatrixForFace
        projection = Double4x4.CreatePerspectiveFov(90f * Maths.Deg2Rad, 1.0f, 0.1f, range);
        view = Double4x4.CreateLookTo(Transform.position, Transform.forward, Transform.up);
    }

    // Get shadow matrix for a specific cubemap face
    public void GetShadowMatrixForFace(int faceIndex, out Double4x4 view, out Double4x4 projection)
    {
        // 90 degree FOV perspective projection for cubemap faces
        projection = Double4x4.CreatePerspectiveFov(90f * Maths.Deg2Rad, 1.0f, 0.1f, range);

        Double3 position = Transform.position;
        Double3 forward, up;

        // Define view matrices for each cubemap face
        // 0: +X, 1: -X, 2: +Y, 3: -Y, 4: +Z, 5: -Z
        switch (faceIndex)
        {
            case 0: // Positive X
                forward = Double3.UnitX;
                up = -Double3.UnitY;
                break;
            case 1: // Negative X
                forward = -Double3.UnitX;
                up = -Double3.UnitY;
                break;
            case 2: // Positive Y
                forward = Double3.UnitY;
                up = Double3.UnitZ;
                break;
            case 3: // Negative Y
                forward = -Double3.UnitY;
                up = -Double3.UnitZ;
                break;
            case 4: // Positive Z
                forward = Double3.UnitZ;
                up = -Double3.UnitY;
                break;
            case 5: // Negative Z
                forward = -Double3.UnitZ;
                up = -Double3.UnitY;
                break;
            default:
                throw new ArgumentException($"Invalid face index: {faceIndex}. Must be 0-5.");
        }

        view = Double4x4.CreateLookTo(position, forward, up);
    }

    public void UploadToGPU(bool cameraRelative, Double3 cameraPosition, int atlasX, int atlasY, int atlasWidth, int lightIndex)
    {
        Double3 position = cameraRelative ? Transform.position - cameraPosition : Transform.position;

        PropertyState.SetGlobalVector($"_PointLights[{lightIndex}].position", position);
        PropertyState.SetGlobalVector($"_PointLights[{lightIndex}].color", new Double3(color.R, color.G, color.B));
        PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].intensity", intensity);
        PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].range", range);

        if (castShadows && atlasX >= 0)
        {
            // For point lights, we store the base atlas position
            // The shader will calculate offsets for each of the 6 faces arranged in a 2x3 grid
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowBias", shadowBias);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowNormalBias", shadowNormalBias);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowStrength", shadowStrength);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowQuality", (float)shadowQuality);

            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasX", atlasX);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasY", atlasY);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasWidth", atlasWidth); // Width of a single face
        }
        else
        {
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasX", -1);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasY", -1);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].atlasWidth", 0);
            PropertyState.SetGlobalFloat($"_PointLights[{lightIndex}].shadowStrength", 0);
        }
    }
}
