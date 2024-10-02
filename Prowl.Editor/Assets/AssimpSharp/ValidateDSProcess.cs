namespace AssimpSharp
{
    public static class ValidateDSProcess
    {
        private static AiScene scene;
        private const float EPSILON = 0.001f;

        public static bool IsActive(int flags)
        {
            return (flags & (int)AiPostProcessSteps.ValidateDataStructure) != 0;
        }

        public static void ExecuteOnScene(Importer imp)
        {
            scene = imp.Scene;
            Console.WriteLine("ValidateDataStructureProcess begin");

            Validate(scene.RootNode);

            if (scene.NumMeshes != 0)
                DoValidation(scene.Meshes, scene.NumMeshes, "meshes", "numMeshes");
            else if ((scene.Flags &Constants. AI_SCENE_FLAGS_INCOMPLETE) == 0)
                ReportError("AiScene.NumMeshes is 0. At least one mesh must be there");
            else if (scene.Meshes.Count != 0)
                ReportError("AiScene.Meshes is not empty although there are no meshes");

            if (scene.NumAnimations != 0)
                DoValidation(scene.Animations, scene.NumAnimations, "animations", "numAnimations");
            else if (scene.Animations.Count != 0)
                ReportError("AiScene.Animations is not empty although there are no animations");

            if (scene.NumCameras != 0)
                DoValidationWithNameCheck(scene.Cameras, scene.NumCameras, "cameras", "numCameras");
            else if (scene.Cameras.Count != 0)
                ReportError("AiScene.Cameras is not empty although there are no cameras");

            if (scene.NumLights > 0)
                DoValidationWithNameCheck(scene.Lights, scene.NumLights, "lights", "numLights");
            else if (scene.Lights.Count != 0)
                ReportError("AiScene.Lights is not empty although there are no lights");

            if (scene.NumTextures > 0)
                DoValidation(scene.Textures, scene.NumTextures, "textures", "numTextures");
            else if (scene.Textures.Count != 0)
                ReportError("AiScene.Textures is not empty although there are no textures");

            if (scene.NumMaterials > 0)
                DoValidation(scene.Materials, scene.NumMaterials, "materials", "numMaterials");
            else if (scene.Materials.Count != 0)
                ReportError("AiScene.Materials is not empty although there are no materials");

            Console.WriteLine("ValidateDataStructureProcess end");
        }

        private static void ReportError(string msg, params object[] args)
        {
            throw new Exception($"Validation failed: {string.Format(msg, args)}");
        }

        private static void ReportWarning(string msg, params object[] args)
        {
            Console.WriteLine($"Validation warning: {string.Format(msg, args)}");
        }

        private static void Validate(AiMesh mesh)
        {
            if (scene.NumMaterials != 0 && mesh.MaterialIndex >= scene.NumMaterials)
                ReportError($"AiMesh.MaterialIndex is invalid (value: {mesh.MaterialIndex} maximum: {scene.NumMaterials - 1})");

            Validate(mesh.Name);

            for (int i = 0; i < mesh.NumFaces; i++)
            {
                var face = mesh.Faces[i];
                if (mesh.PrimitiveType != 0)
                {
                    switch (face.Count)
                    {
                        case 0:
                            ReportError($"AiMesh.Faces[{i}].NumIndices is 0");
                            break;
                        case 1:
                            if ((mesh.PrimitiveType & AiPrimitiveType.Point) == 0)
                                ReportError($"AiMesh.Faces[{i}] is a POINT but AiMesh.PrimitiveTypes does not report the POINT flag");
                            break;
                        case 2:
                            if ((mesh.PrimitiveType & AiPrimitiveType.Line) == 0)
                                ReportError($"AiMesh.Faces[{i}] is a LINE but AiMesh.PrimitiveTypes does not report the LINE flag");
                            break;
                        case 3:
                            if ((mesh.PrimitiveType & AiPrimitiveType.Triangle) == 0)
                                ReportError($"AiMesh.Faces[{i}] is a TRIANGLE but AiMesh.PrimitiveTypes does not report the TRIANGLE flag");
                            break;
                        default:
                            if ((mesh.PrimitiveType & AiPrimitiveType.Polygon) == 0)
                                ReportError($"AiMesh.Faces[{i}] is a POLYGON but AiMesh.PrimitiveTypes does not report the POLYGON flag");
                            break;
                    }
                }
                if (face.Count == 0) ReportError($"AiMesh.Faces[{i}] is empty");
            }

            if (mesh.NumVertices == 0 || (mesh.Vertices.Count == 0 && scene.Flags == 0))
                ReportError("The mesh contains no vertices");
            if (mesh.NumVertices > Constants.AI_MAX_VERTICES)
                ReportError($"Mesh has too many vertices: {mesh.NumVertices}, but the limit is {Constants.AI_MAX_VERTICES}");
            if (mesh.NumFaces > Constants.AI_MAX_FACES)
                ReportError($"Mesh has too many faces: {mesh.NumFaces}, but the limit is {Constants.AI_MAX_FACES}");

            if (mesh.Tangents.Count != mesh.Bitangents.Count)
                ReportError("If there are tangents, bitangent vectors must be present as well");

            if (mesh.NumFaces == 0 || (mesh.Faces.Count == 0 && scene.Flags == 0))
                ReportError("Mesh contains no faces");

            var abRefList = new bool[mesh.NumVertices];
            for (int i = 0; i < mesh.NumFaces; i++)
            {
                var face = mesh.Faces[i];
                if (face.Count > Constants.AI_MAX_FACE_INDICES)
                    ReportError($"Face {i} has too many faces: {face.Count}, but the limit is {Constants.AI_MAX_FACE_INDICES}");
                for (int a = 0; a < face.Count; a++)
                {
                    if (face[a] >= mesh.NumVertices) ReportError($"AiMesh.Faces[{i}][{a}] is out of range");
                    abRefList[face[a]] = true;
                }
            }

            for (int i = 0; i < mesh.NumVertices; i++)
                if (!abRefList[i])
                    ReportWarning("There are unreferenced vertices");

            for (int i = 0; i < Constants.AI_MAX_NUMBER_OF_TEXTURECOORDS; i++)
            {
                if (!mesh.HasTextureCoords(i)) break;
            }
            for (int i = 0; i < Constants.AI_MAX_NUMBER_OF_TEXTURECOORDS; i++)
            {
                if (mesh.HasTextureCoords(i))
                    ReportError($"Texture coordinate channel {i} exists although the previous channel didn't exist.");
            }

            for (int i = 0; i < Constants.AI_MAX_NUMBER_OF_COLOR_SETS; i++)
            {
                if (!mesh.HasVertexColors(i)) break;
            }
            for (int i = 0; i < Constants.AI_MAX_NUMBER_OF_COLOR_SETS; i++)
            {
                if (mesh.HasVertexColors(i))
                    ReportError($"Vertex color channel {i} exists although the previous channel didn't exist.");
            }

            if (mesh.NumBones > 0)
            {
                if (mesh.Bones.Count == 0)
                    ReportError($"AiMesh.Bones is empty (AiMesh.NumBones is {mesh.NumBones})");
                var afSum = new float[mesh.NumVertices];
                for (int i = 0; i < mesh.NumBones; i++)
                {
                    var bone = mesh.Bones[i];
                    if (bone.NumWeights > Constants.AI_MAX_BONE_WEIGHTS)
                        ReportError($"Bone {i} has too many weights: {bone.NumWeights}, but the limit is {Constants.AI_MAX_BONE_WEIGHTS}");
                    if (i >= mesh.Bones.Count)
                        ReportError($"AiMesh.Bones[{i}] doesn't exist (AiMesh.NumBones is {mesh.NumBones})");
                    Validate(mesh, mesh.Bones[i], afSum);
                    for (int a = i + 1; a < mesh.NumBones; a++)
                        if (mesh.Bones[i].Name == mesh.Bones[a].Name)
                            ReportError($"AiMesh.Bones[{i}] has the same name as AiMesh.Bones[{a}]");
                }
                for (int i = 0; i < mesh.NumVertices; i++)
                    if (afSum[i] != 0f && (afSum[i] <= 0.94f || afSum[i] >= 1.05f))
                        ReportWarning($"AiMesh.Vertices[{i}]: bone weight sum != 1f (sum is {afSum[i]})");
            }
            else if (mesh.Bones.Count != 0)
                ReportError("AiMesh.Bones is not empty although there are no bones");
        }
        private static void Validate(AiMesh mesh, AiBone bone, float[] afSum)
        {
            Validate(bone.Name);
            if (bone.NumWeights == 0) ReportError("AiBone.NumWeights is zero");

            for (int i = 0; i < bone.NumWeights; i++)
            {
                if (bone.Weights[i].VertexId >= mesh.NumVertices)
                    ReportError($"AiBone.Weights[{i}].VertexId is out of range");
                else if (bone.Weights[i].Weight == 0f || bone.Weights[i].Weight > 1f)
                    ReportWarning($"AiBone.Weights[{i}].Weight has an invalid value");
                afSum[bone.Weights[i].VertexId] += bone.Weights[i].Weight;
            }
        }

        private static void Validate(AiAnimation animation)
        {
            Validate(animation.Name);
            if (animation.NodeAnimationChannelCount == 0)
                ReportError("AiAnimation.NumChannels is 0. At least one node animation channel must be there.");

            if (animation.NodeAnimationChannelCount > 0)
            {
                if (animation.NodeAnimationChannels.Count == 0)
                    ReportError($"AiAnimation.Channels is empty (AiAnimation.NumChannels is {animation.NodeAnimationChannelCount})");
                for (int i = 0; i < animation.NodeAnimationChannelCount; i++)
                {
                    if (i >= animation.NodeAnimationChannels.Count)
                        ReportError($"AiAnimation.Channels[{i}] doesn't exist (AiAnimation.NumChannels is {animation.NodeAnimationChannelCount})");
                    Validate(animation, animation.NodeAnimationChannels[i]);
                }
            }
        }

        private static void Validate(AiMaterial material)
        {
            var shadingModel = material.ShadingModel;
            if (shadingModel.HasValue)
            {
                switch (shadingModel.Value)
                {
                    case AiShadingMode.Blinn:
                    case AiShadingMode.CookTorrance:
                    case AiShadingMode.Phong:
                        if (!material.Shininess.HasValue)
                            ReportWarning("A specular shading model is specified but there is no Shininess key");
                        if (material.ShininessStrength.HasValue && material.ShininessStrength.Value == 0f)
                            ReportWarning("A specular shading model is specified but the value of the Shininess Strength key is 0");
                        break;
                }
            }

            if (material.Opacity.HasValue)
            {
                if (material.Opacity.Value == 0f || material.Opacity.Value > 1.01f)
                    ReportWarning("Invalid opacity value (must be 0 < opacity < 1f)");
            }

            SearchForInvalidTextures(material);
        }

        private static void SearchForInvalidTextures(AiMaterial material)
        {
            bool noSpecified = true;
            foreach (var texture in material.Textures)
            {
                if (texture.UVWSource.HasValue)
                {
                    noSpecified = false;
                    int index = texture.UVWSource.Value;

                    for (int a = 0; a < scene.NumMeshes; a++)
                    {
                        var mesh = scene.Meshes[a];
                        if (mesh.MaterialIndex == scene.Materials.IndexOf(material))
                        {
                            int channels = 0;
                            while (mesh.HasTextureCoords(channels)) ++channels;
                            if (index >= channels)
                                ReportWarning($"Invalid UV index: {index} (key UVWSource). Mesh {a} has only {channels} UV channels");
                        }
                    }
                }
            }

            if (noSpecified)
            {
                for (int a = 0; a < scene.NumMeshes; a++)
                {
                    var mesh = scene.Meshes[a];
                    if (mesh.MaterialIndex == scene.Materials.IndexOf(material) && mesh.TextureCoordinateChannels[0].Count == 0)
                        ReportWarning("UV-mapped texture, but there are no UV coords");
                }
            }
        }

        private static void Validate(AiTexture texture)
        {
            if (texture.Data.Length == 0)
                ReportError("AiTexture.Data is empty");
            if (texture.Height > 0 && texture.Width == 0)
                ReportError($"AiTexture.Width is zero (AiTexture.Height is {texture.Height}, uncompressed texture)");
            else
            {
                if (texture.Width == 0)
                    ReportError("AiTexture.Width is zero (compressed texture)");
                else if (texture.FormatHint[0] == '.')
                    ReportWarning($"AiTexture.FormatHint should contain a file extension without a leading dot (format hint: {texture.FormatHint}).");
            }
            if (texture.FormatHint.Any(char.IsUpper))
                ReportError("AiTexture.FormatHint contains non-lowercase letters");
        }

        private static void Validate(AiLight light)
        {
            if (light.Type == AiLightSourceType.Undefined)
                ReportWarning("AiLight.Type is undefined");
            if (light.AttenuationConstant == 0f && light.AttenuationLinear == 0f && light.AttenuationQuadratic == 0f)
                ReportWarning("AiLight.Attenuation* - all are zero");
            if (light.AngleInnerCone > light.AngleOuterCone)
                ReportError("AiLight.AngleInnerCone is larger than AiLight.AngleOuterCone");
            if (light.ColorDiffuse.IsBlack() && light.ColorAmbient.IsBlack() && light.ColorSpecular.IsBlack())
                ReportWarning("AiLight.Color* - all are black and won't have any influence");
        }

        private static void Validate(AiCamera camera)
        {
            if (camera.ClipPlaneFar <= camera.ClipPlaneNear)
                ReportError("AiCamera.ClipPlaneFar must be >= AiCamera.ClipPlaneNear");
            if (camera.HorizontalFOV == 0f || camera.HorizontalFOV >= Math.PI)
                ReportWarning($"{camera.HorizontalFOV} is not a valid value for AiCamera.HorizontalFOV");
        }

        private static void Validate(AiAnimation animation, AiNodeAnim boneAnim)
        {
            Validate(boneAnim.NodeName);
            if (boneAnim.NumPositionKeys == 0 && boneAnim.NumScalingKeys == 0 && boneAnim.NumRotationKeys == 0)
                ReportError("Empty node animation channel");

            ValidateKeys(animation, boneAnim.PositionKeys, boneAnim.NumPositionKeys, "Position");
            ValidateKeys(animation, boneAnim.RotationKeys, boneAnim.NumRotationKeys, "Rotation");
            ValidateKeys(animation, boneAnim.ScalingKeys, boneAnim.NumScalingKeys, "Scaling");

            if (boneAnim.NumScalingKeys == 0 && boneAnim.NumRotationKeys == 0 && boneAnim.NumPositionKeys == 0)
                ReportError("A node animation channel must have at least one subtrack");
        }

        private static void ValidateKeys<T>(AiAnimation animation, List<T> keys, int numKeys, string keyType)
        {
            if (numKeys > 0)
            {
                if (keys.Count == 0)
                    ReportError($"AiNodeAnim.{keyType}Keys is empty (AiNodeAnim.Num{keyType}Keys is {numKeys})");
                double last = -10e10;
                for (int i = 0; i < numKeys; i++)
                {
                    double time = (keys[i] as dynamic).Time;
                    if (animation.DurationInTicks > 0 && time > animation.DurationInTicks + EPSILON)
                    {
                        ReportError($"AiNodeAnim.{keyType}Keys[{i}].Time ({time:F5}) is larger than AiAnimation.Duration (which is {animation.DurationInTicks:F5})");
                    }
                    if (i > 0 && time <= last)
                    {
                        ReportWarning($"AiNodeAnim.{keyType}Keys[{i}].Time ({time:F5}) is smaller than AiAnimation.{keyType}Keys[{i - 1}] (which is {last:F5})");
                    }
                    last = time;
                }
            }
        }

        private static void Validate(AiNode node)
        {
            if (node != scene.RootNode && node.Parent == null)
                ReportError("A node has no valid parent (AiNode.Parent is null)");
            Validate(node.Name);

            if (node.NumMeshes > 0)
            {
                if (node.Meshes.Length == 0)
                    ReportError($"AiNode.Meshes is empty (AiNode.NumMeshes is {node.NumMeshes})");
                var abHadMesh = new bool[scene.NumMeshes];
                for (int i = 0; i < node.NumMeshes; i++)
                {
                    if (node.Meshes[i] >= scene.NumMeshes)
                        ReportError($"AiNode.Meshes[{node.Meshes[i]}] is out of range (maximum is {scene.NumMeshes - 1})");
                    if (abHadMesh[node.Meshes[i]])
                        ReportError($"AiNode.Meshes[{i}] is already referenced by this node (value: {node.Meshes[i]})");
                    abHadMesh[node.Meshes[i]] = true;
                }
            }

            if (node.NumChildren > 0)
            {
                if (node.Children.Count == 0)
                    ReportError($"AiNode.Children is empty (AiNode.NumChildren is {node.NumChildren})");
                for (int i = 0; i < node.NumChildren; i++)
                    Validate(node.Children[i]);
            }
        }

        private static void Validate(string str)
        {
            if (str.Length > Constants.MAXLEN)
                ReportError($"String.Length is too large ({str.Length}, maximum is {Constants.MAXLEN})");
            if (str.Contains('\0'))
                ReportError("String data is invalid: it contains the terminal zero");
        }

        private static void DoValidationWithNameCheck<T>(List<T> array, int size, string firstName, string secondName)
        {
            DoValidationEx(array, size, firstName, secondName);
            for (int i = 0; i < size; i++)
            {
                var element = array[i];
                var name = (element as dynamic).Name;
                int res = HasNameMatch(name, scene.RootNode);
                if (res == 0)
                    ReportError($"AiScene.{firstName}[{i}] has no corresponding node in the scene graph ({name})");
                else if (res != 1)
                    ReportError($"AiScene.{firstName}[{i}]: there are more than one nodes with {name} as name");
            }
        }

        private static void DoValidationEx<T>(List<T> array, int size, string firstName, string secondName)
        {
            DoValidation(array, size, firstName, secondName);
            for (int i = 0; i < size; i++)
            {
                for (int a = i + 1; a < size; a++)
                {
                    var nameI = (array[i] as dynamic).Name;
                    var nameA = (array[a] as dynamic).Name;
                    if (nameI == nameA)
                        ReportError($"AiScene.{firstName}[{i}] has the same name as AiScene.{secondName}[{a}]");
                }
            }
        }

        private static int HasNameMatch(string name, AiNode node)
        {
            int result = (node.Name == name) ? 1 : 0;
            return result + node.Children.Sum(child => HasNameMatch(name, child));
        }

        private static void DoValidation<T>(List<T> array, int size, string firstName, string secondName)
        {
            if (size > 0)
            {
                if (array.Count == 0)
                    ReportError($"AiScene.{firstName} is empty (AiScene.{secondName} is {size})");
                for (int i = 0; i < size; i++)
                {
                    var element = array[i];
                    switch (element)
                    {
                        case AiMesh mesh:
                            Validate(mesh);
                            break;
                        case AiAnimation animation:
                            Validate(animation);
                            break;
                        case AiCamera camera:
                            Validate(camera);
                            break;
                        case AiLight light:
                            Validate(light);
                            break;
                        case AiTexture texture:
                            Validate(texture);
                            break;
                        case AiMaterial material:
                            Validate(material);
                            break;
                    }
                }
            }
        }
    }
}
