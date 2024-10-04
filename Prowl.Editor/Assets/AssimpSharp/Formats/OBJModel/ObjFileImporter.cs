using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AssimpSharp.Formats.Obj
{
    public class ObjFileImporter : BaseImporter
    {
        private const int ObjMinSize = 16;

        public override AiImporterDesc Info => new AiImporterDesc
        {
            Name = "Wavefront Object Importer",
            Flags = AiImporterFlags.SupportTextFlavour,
            FileExtensions = [ "obj" ]
        };

        private FileInfo file;

        public override bool CanRead(Uri file, bool checkSig)
        {
            if (!checkSig)
            {
                return Path.GetExtension(file.LocalPath).ToLower() == ".obj";
            }
            else
            {
                // TODO: Implement signature checking
                return false;
            }
        }

        public override void InternReadFile(Uri pFile, AiScene pScene)
        {
            file = new FileInfo(pFile.LocalPath);
            if (!file.Exists)
            {
                throw new FileNotFoundException($"Failed to open file {pFile}.");
            }

            var fileSize = file.Length;

            if (fileSize < ObjMinSize)
            {
                throw new Exception("OBJ-file is too small.");
            }

            var parser = new ObjFileParser(file);

            CreateDataFromImport(parser.Model, pScene);
        }

        private void CreateDataFromImport(Model pModel, AiScene pScene)
        {
            // Create the root node of the scene
            pScene.RootNode = new AiNode();
            if (!string.IsNullOrEmpty(pModel.ModelName))
            {
                pScene.RootNode.Name = pModel.ModelName;
            }
            else
            {
                throw new Exception("pModel.ModelName is empty");
            }

            // Create nodes for the whole scene
            var meshArray = new List<AiMesh>();
            for (int index = 0; index < pModel.Objects.Count; index++)
            {
                CreateNodes(pModel, pModel.Objects[index], pScene.RootNode, pScene, meshArray);
            }

            // Create mesh pointer buffer for this scene
            if (pScene.NumMeshes > 0)
            {
                pScene.Meshes.AddRange(meshArray);
            }

            // Create all materials
            CreateMaterials(pModel, pScene);

            LoadTextures(pScene);
        }

        private AiNode CreateNodes(Model pModel, Object pObject, AiNode pParent, AiScene pScene, List<AiMesh> meshArray)
        {
            var oldMeshSize = meshArray.Count;
            var pNode = new AiNode();

            pNode.Name = pObject.ObjName;

            AppendChildToParentNode(pParent, pNode);

            foreach (var meshId in pObject.Meshes)
            {
                var pMesh = CreateTopology(pModel, pObject, meshId);
                if (pMesh != null && pMesh.NumFaces > 0)
                    meshArray.Add(pMesh);
            }

            if (pObject.SubObjects.Count > 0)
            {
                pNode.NumChildren = pObject.SubObjects.Count;
                pNode.Children = new List<AiNode>();
                pNode.NumMeshes = 1;
                pNode.Meshes = new int[1];
            }

            var meshSizeDiff = meshArray.Count - oldMeshSize;
            if (meshSizeDiff > 0)
            {
                pNode.Meshes = new int[meshSizeDiff];
                pNode.NumMeshes = meshSizeDiff;
                int index = 0;
                for (int i = oldMeshSize; i < meshArray.Count; i++)
                {
                    pNode.Meshes[index] = pScene.NumMeshes;
                    pScene.NumMeshes++;
                    index++;
                }
            }

            return pNode;
        }

        private void AppendChildToParentNode(AiNode pParent, AiNode pChild)
        {
            pChild.Parent = pParent;
            pParent.NumChildren++;
            pParent.Children.Add(pChild);
        }

        private AiMesh CreateTopology(Model pModel, Object pData, int meshIndex)
        {
            var pObjMesh = pModel.Meshes[meshIndex];

            if (pObjMesh.Faces.Count == 0) return null;

            var pMesh = new AiMesh();
            if (!string.IsNullOrEmpty(pObjMesh.Name)) pMesh.Name = pObjMesh.Name;

            foreach (var face in pObjMesh.Faces)
            {
                switch (face.PrimitiveType)
                {
                    case AiPrimitiveType.Line:
                        pMesh.NumFaces += face.Vertices.Count - 1;
                        pMesh.PrimitiveType |= AiPrimitiveType.Line;
                        break;
                    case AiPrimitiveType.Point:
                        pMesh.NumFaces += face.Vertices.Count;
                        pMesh.PrimitiveType |= AiPrimitiveType.Point;
                        break;
                    default:
                        pMesh.NumFaces++;
                        pMesh.PrimitiveType |= (face.Vertices.Count > 3) ? AiPrimitiveType.Polygon : AiPrimitiveType.Triangle;
                        break;
                }
            }

            int uiIdxCount = 0;
            if (pMesh.NumFaces > 0)
            {
                var faces = new List<AiFace>();

                if (pObjMesh.MaterialIndex != ObjMesh.NoMaterial)
                    pMesh.MaterialIndex = pObjMesh.MaterialIndex;

                int outIndex = 0;

                foreach (var face in pObjMesh.Faces)
                {
                    var aiFace = new AiFace();
                    switch (face.PrimitiveType)
                    {
                        case AiPrimitiveType.Line:
                            for (int i = 0; i < face.Vertices.Count - 1; i++)
                            {
                                int mNumIndices = 2;
                                uiIdxCount += mNumIndices;
                                aiFace.AddRange(new int[mNumIndices]);
                            }
                            break;
                        case AiPrimitiveType.Point:
                            for (int i = 0; i < face.Vertices.Count; i++)
                            {
                                int mNumIndices = 1;
                                uiIdxCount += mNumIndices;
                                aiFace.AddRange(new int[mNumIndices]);
                            }
                            break;
                        default:
                            int uiNumIndices = face.Vertices.Count;
                            uiIdxCount += uiNumIndices;
                            aiFace.AddRange(new int[uiNumIndices]);
                            break;
                    }
                    faces.Add(aiFace);
                }
                pMesh.Faces = faces;
            }

            CreateVertexArray(pModel, pData, meshIndex, pMesh, uiIdxCount);

            return pMesh;
        }

        private void CreateVertexArray(Model pModel, Object pCurrentObject, int uiMeshIndex, AiMesh pMesh, int numIndices)
        {
            if (pCurrentObject.Meshes.Count == 0) return;

            var pObjMesh = pModel.Meshes[uiMeshIndex];
            if (pObjMesh.NumIndices < 1) return;

            pMesh.NumVertices = numIndices;
            if (pMesh.NumVertices == 0)
                throw new Exception("OBJ: no vertices");
            //else if (pMesh.NumVertices > AI_MAX_ALLOC(Vector3.SizeInBytes))
            //    throw new Exception("OBJ: Too many vertices, would run out of memory");

            pMesh.Vertices = new List<Vector3>(new Vector3[pMesh.NumVertices]);

            if (pModel.Normals.Count > 0 && pObjMesh.HasNormals)
                pMesh.Normals = new List<Vector3>(new Vector3[pMesh.NumVertices]);

            if (pModel.VertexColors.Count > 0)
                pMesh.VertexColorChannels[0] = new List<Vector4>(new Vector4[pMesh.NumVertices]);

            if (pModel.TextureCoord.Count > 0 && pObjMesh.UVCoordinates[0] != 0)
                pMesh.TextureCoordinateChannels.Add(new List<float[]>(new float[pMesh.NumVertices][]));

            int newIndex = 0;
            int outIndex = 0;
            foreach (var pSourceFace in pObjMesh.Faces)
            {
                int outVertexIndex = 0;
                for (int vertexIndex = 0; vertexIndex < pSourceFace.Vertices.Count; vertexIndex++)
                {
                    int vertex = pSourceFace.Vertices[vertexIndex];

                    if (vertex >= pModel.Vertices.Count)
                        throw new Exception("OBJ: vertex index out of range");

                    pMesh.Vertices[newIndex] = pModel.Vertices[vertex];

                    if (pModel.Normals.Count > 0 && vertexIndex < pSourceFace.Normals.Count)
                    {
                        int normal = pSourceFace.Normals[vertexIndex];
                        if (normal >= pModel.Normals.Count)
                            throw new Exception("OBJ: vertex normal index out of range");
                        pMesh.Normals[newIndex] = pModel.Normals[normal];
                    }

                    if (pModel.VertexColors.Count > 0)
                        pMesh.VertexColorChannels[0][newIndex] = new Vector4(pModel.VertexColors[vertex], 1.0f);

                    if (pModel.TextureCoord.Count > 0 && vertexIndex < pSourceFace.TextureCoords.Count)
                    {
                        int tex = pSourceFace.TextureCoords[vertexIndex];
                        if (tex >= pModel.TextureCoord.Count)
                            throw new Exception("OBJ: texture coordinate index out of range");

                        var coord3d = pModel.TextureCoord[tex];
                        pMesh.TextureCoordinateChannels[0][newIndex] = [ coord3d[0], coord3d[1], coord3d.Count > 2 ? coord3d[2] : 0 ];
                    }

                    if (pMesh.NumVertices <= newIndex)
                        throw new Exception("OBJ: bad vertex index");

                    var pDestFace = pMesh.Faces[outIndex];

                    bool last = (vertexIndex == pSourceFace.Vertices.Count - 1);
                    if (pSourceFace.PrimitiveType != AiPrimitiveType.Line || !last)
                    {
                        pDestFace[outVertexIndex] = newIndex;
                        outVertexIndex++;
                    }

                    switch (pSourceFace.PrimitiveType)
                    {
                        case AiPrimitiveType.Point:
                            outIndex++;
                            outVertexIndex = 0;
                            break;
                        case AiPrimitiveType.Line:
                            outVertexIndex = 0;
                            if (!last) outIndex++;
                            if (vertex != 0)
                            {
                                if (!last)
                                {
                                    pMesh.Vertices[newIndex + 1] = pMesh.Vertices[newIndex];
                                    if (pSourceFace.Normals.Count > 0 && pModel.Normals.Count > 0)
                                        pMesh.Normals[newIndex + 1] = pMesh.Normals[newIndex];

                                    if (pModel.TextureCoord.Count > 0)
                                        for (int i = 0; i < pMesh.GetNumUVChannels; i++)
                                            pMesh.TextureCoordinateChannels[i][newIndex + 1] = pMesh.TextureCoordinateChannels[i][newIndex];
                                    ++newIndex;
                                }
                                pMesh.Faces[outIndex - 1][1] = newIndex;
                            }
                            break;
                        default:
                            if (last) outIndex++;
                            break;
                    }
                    ++newIndex;
                }
            }
        }

        private void CreateMaterials(Model pModel, AiScene pScene)
        {
            int numMaterials = pModel.MaterialLib.Count;
            pScene.NumMaterials = 0;
            if (pModel.MaterialLib.Count == 0)
            {
                Console.Error.WriteLine("OBJ: no materials specified");
                return;
            }

            for (int matIndex = 0; matIndex < numMaterials; matIndex++)
            {
                string materialName = pModel.MaterialLib[matIndex];
                ObjMaterial pCurrentMaterial = null;
                pModel.MaterialMap.TryGetValue(materialName, out pCurrentMaterial);

                if (pCurrentMaterial == null) continue;

                var mat = new AiMaterial();
                mat.Name = pCurrentMaterial.MaterialName;

                mat.ShadingModel = pCurrentMaterial.IlluminationModel switch
                {
                    0 => AiShadingMode.NoShading,
                    1 => AiShadingMode.Gouraud,
                    2 => AiShadingMode.Phong,
                    _ => AiShadingMode.Gouraud
                };

                if (pCurrentMaterial.IlluminationModel > 2)
                {
                    Console.Error.WriteLine("OBJ: unexpected illumination model (0-2 recognized)");
                }

                mat.Color = new AiMaterial.MatColor
                {
                    Ambient = new Vector4(pCurrentMaterial.Ambient, 1.0f),
                    Diffuse = new Vector4(pCurrentMaterial.Diffuse, 1.0f),
                    Specular = new Vector4(pCurrentMaterial.Specular, 1.0f),
                    Emissive = new Vector4(pCurrentMaterial.Emissive, 1.0f)
                };
                mat.Shininess = pCurrentMaterial.Shininess;
                mat.Opacity = pCurrentMaterial.Alpha;
                mat.Refracti = pCurrentMaterial.Ior;
                mat.Transparent = pCurrentMaterial.Transparent;

                // Adding textures
                int uvwIndex = 0;

                foreach (var texture in pCurrentMaterial.Textures)
                {
                    AiTexture.Type aiTextureType = GetAiTextureType(texture.TextureType);

                    mat.Textures.Add(new AiMaterial.MatTexture
                    {
                        Type = aiTextureType,
                        FilePath = texture.Name,
                        UVWSource = uvwIndex,
                        MapModeU = texture.Clamp ? AiTexture.MapMode.Clamp : AiTexture.MapMode.Wrap,
                        MapModeV = texture.Clamp ? AiTexture.MapMode.Clamp : AiTexture.MapMode.Wrap
                    });
                }

                pScene.Materials.Add(mat);
                pScene.NumMaterials++;
            }

            System.Diagnostics.Debug.Assert(pScene.NumMaterials == numMaterials);
        }

        private void LoadTextures(AiScene scene)
        {
            foreach (var material in scene.Materials)
            {
                foreach (var texture in material.Textures)
                {
                    if (string.IsNullOrEmpty(texture.FilePath)) continue;

                    if (!scene.Textures.Contains(texture.FilePath))
                    {
                        string cleanedName = texture.FilePath.TrimStart('.', '\\', '/');

                        var texFile = new FileInfo(Path.Combine(file.Directory.FullName, cleanedName));
                        if (texFile.Exists)
                        {
                            scene.Textures.Add(texture.FilePath);
                        }
                        else
                        {
                            Console.WriteLine($"OBJ: Couldn't find texture file {cleanedName}");
                        }
                    }
                }
            }
        }

        private AiTexture.Type GetAiTextureType(ObjMaterial.ObjTexture.Type objTextureType)
        {
            switch (objTextureType)
            {
                case ObjMaterial.ObjTexture.Type.Diffuse:
                    return AiTexture.Type.Diffuse;
                case ObjMaterial.ObjTexture.Type.Specular:
                    return AiTexture.Type.Specular;
                case ObjMaterial.ObjTexture.Type.Ambient:
                    return AiTexture.Type.Ambient;
                case ObjMaterial.ObjTexture.Type.Emissive:
                    return AiTexture.Type.Emissive;
                case ObjMaterial.ObjTexture.Type.Bump:
                    return AiTexture.Type.Height;
                case ObjMaterial.ObjTexture.Type.Normal:
                    return AiTexture.Type.Normals;
                case ObjMaterial.ObjTexture.Type.ReflectionSphere:
                case ObjMaterial.ObjTexture.Type.ReflectionCubeTop:
                case ObjMaterial.ObjTexture.Type.ReflectionCubeBottom:
                case ObjMaterial.ObjTexture.Type.ReflectionCubeFront:
                case ObjMaterial.ObjTexture.Type.ReflectionCubeBack:
                case ObjMaterial.ObjTexture.Type.ReflectionCubeLeft:
                case ObjMaterial.ObjTexture.Type.ReflectionCubeRight:
                    return AiTexture.Type.Reflection;
                case ObjMaterial.ObjTexture.Type.Displacement:
                    return AiTexture.Type.Displacement;
                case ObjMaterial.ObjTexture.Type.Opacity:
                    return AiTexture.Type.Opacity;
                case ObjMaterial.ObjTexture.Type.Specularity:
                    return AiTexture.Type.Shininess;
                default:
                    return AiTexture.Type.Unknown;
            }
        }

    }
}
