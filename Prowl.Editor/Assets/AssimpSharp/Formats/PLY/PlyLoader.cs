using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace AssimpSharp.Formats.Ply
{
    public class PlyLoader : BaseImporter
    {
        public override AiImporterDesc Info => new AiImporterDesc
        {
            Name = "Stanford Polygon Library (PLY) Importer",
            Flags = AiImporterFlags.SupportBinaryFlavour | AiImporterFlags.SupportTextFlavour,
            FileExtensions = ["ply"]
        };

        private DOM _pcDom;

        public override bool CanRead(Uri file, bool checkSig)
        {
            string extension = Path.GetExtension(file.LocalPath).ToLowerInvariant();
            if (extension == ".ply")
            {
                return true;
            }
            return false;
        }

        public override void InternReadFile(Uri file, AiScene scene)
        {
            // Read the entire file into a byte array
            byte[] fileData = File.ReadAllBytes(file.LocalPath);
            ByteBuffer buffer = new ByteBuffer(fileData);

            // Read magic number
            string magic = new string(new[] { (char)buffer.ReadByte(), (char)buffer.ReadByte(), (char)buffer.ReadByte() });
            if (magic.ToLower() != "ply")
                throw new Exception("Invalid .ply file: Magic number 'ply' is not there");

            buffer.SkipSpacesAndLineEnd();

            var sPlyDom = new DOM();
            string format = buffer.NextWord();
            if (format == "format")
            {
                string formatType = buffer.NextWord();
                if (formatType == "ascii")
                {
                    buffer.SkipLine();
                    if (!DOM.ParseInstance(buffer, sPlyDom))
                        throw new Exception("Invalid .ply file: Unable to build DOM (#1)");
                }
                else
                {
                    buffer.Position -= formatType.Length;
                    buffer.SkipSpaces();
                    if (buffer.StartsWith("binary_"))
                    {
                        bool bIsBE = formatType == "binary_big_endian";
                        buffer.IsBigEndian = bIsBE;
                        buffer.SkipLine();
                        if (!DOM.ParseInstanceBinary(buffer, sPlyDom))
                            throw new Exception("Invalid .ply file: Unable to build DOM (#2)");
                    }
                    else
                        throw new Exception("Invalid .ply file: Unknown file format");
                }
            }
            else
                throw new Exception("Invalid .ply file: Missing format specification");

            _pcDom = sPlyDom;

            var avPositions = new List<Vector3>();
            LoadVertices(avPositions, false);

            var avNormals = new List<Vector3>();
            LoadVertices(avNormals, true);

            var avFaces = new List<Face>();
            LoadFaces(avFaces);

            if (avFaces.Count == 0)
            {
                if (avPositions.Count < 3)
                    throw new Exception("Invalid .ply file: Not enough vertices to build a proper face list.");

                int iNum = avPositions.Count / 3;
                for (int i = 0; i < iNum; i++)
                {
                    var sFace = new Face
                    {
                        Indices = new int[] { i * 3, i * 3 + 1, i * 3 + 2 }
                    };
                    avFaces.Add(sFace);
                }
            }

            var avMaterials = new List<AiMaterial>();
            LoadMaterial(avMaterials);

            var avColors = new List<Vector4>();
            LoadVertexColor(avColors);

            var avTexCoords = new List<Vector2>();
            LoadTextureCoordinates(avTexCoords);

            ReplaceDefaultMaterial(avFaces, avMaterials);

            var avMeshes = new List<AiMesh>();
            ConvertMeshes(avFaces, avPositions, avNormals, avColors, avTexCoords, avMaterials, avMeshes);

            if (avMeshes.Count == 0)
                throw new Exception("Invalid .ply file: Unable to extract mesh data");

            scene.Materials = avMaterials;
            scene.Meshes = avMeshes;

            scene.RootNode = new AiNode
            {
                Name = "ply_root",
                Meshes = Enumerable.Range(0, scene.NumMeshes).ToArray()
            };
        }

        private void SkipSpacesAndLineEnd(BinaryReader reader)
        {
            while (char.IsWhiteSpace((char)reader.PeekChar()))
            {
                reader.ReadChar();
            }
        }

        private string ReadWord(BinaryReader reader)
        {
            string word = "";
            SkipSpacesAndLineEnd(reader);
            while (reader.PeekChar() != -1 && !char.IsWhiteSpace((char)reader.PeekChar()))
            {
                word += (char)reader.ReadChar();
            }
            Console.WriteLine($"Read word: '{word}'"); // Debug output
            return word;
        }

        private void SkipLine(BinaryReader reader)
        {
            while (reader.PeekChar() != '\n' && reader.PeekChar() != -1)
            {
                reader.ReadChar();
            }
            if (reader.PeekChar() == '\n') reader.ReadChar();
        }

        private void LoadVertices(List<Vector3> pvOut, bool p_bNormals)
        {
            int[] aiPositions = { -1, -1, -1 };
            EDataType[] aiTypes = { EDataType.Char, EDataType.Char, EDataType.Char };
            ElementInstanceList pcList = null;
            int cnt = 0;

            foreach (var element in _pcDom.Elements)
            {
                int i = _pcDom.Elements.IndexOf(element);

                if (element.Semantic == EElementSemantic.Vertex)
                {
                    pcList = _pcDom.ElementData[i];

                    foreach (var property in element.Properties)
                    {
                        if (property.IsList) continue;

                        int a = element.Properties.IndexOf(property);

                        if (p_bNormals)
                        {
                            if (property.Semantic == ESemantic.XNormal)
                            {
                                cnt++;
                                aiPositions[0] = a;
                                aiTypes[0] = property.Type;
                            }
                            if (property.Semantic == ESemantic.YNormal)
                            {
                                cnt++;
                                aiPositions[1] = a;
                                aiTypes[1] = property.Type;
                            }
                            if (property.Semantic == ESemantic.ZNormal)
                            {
                                cnt++;
                                aiPositions[2] = a;
                                aiTypes[2] = property.Type;
                            }
                        }
                        else
                        {
                            if (property.Semantic == ESemantic.XCoord)
                            {
                                cnt++;
                                aiPositions[0] = a;
                                aiTypes[0] = property.Type;
                            }
                            if (property.Semantic == ESemantic.YCoord)
                            {
                                cnt++;
                                aiPositions[1] = a;
                                aiTypes[1] = property.Type;
                            }
                            if (property.Semantic == ESemantic.ZCoord)
                            {
                                cnt++;
                                aiPositions[2] = a;
                                aiTypes[2] = property.Type;
                            }
                        }
                        if (cnt == 3) break;
                    }
                    break;
                }
            }

            if (pcList != null && cnt != 0)
            {
                foreach (var instance in pcList.Instances)
                {
                    Vector3 vOut = new Vector3();

                    if (aiPositions[0] != -1)
                        vOut.X = (float)instance.Properties[aiPositions[0]].Values[0];

                    if (aiPositions[1] != -1)
                        vOut.Y = (float)instance.Properties[aiPositions[1]].Values[0];

                    if (aiPositions[2] != -1)
                        vOut.Z = (float)instance.Properties[aiPositions[2]].Values[0];

                    pvOut.Add(vOut);
                }
            }
        }

        private void LoadFaces(List<Face> pvOut)
        {
            ElementInstanceList pcList = null;
            bool bOne = false;

            int iProperty = -1;
            EDataType eType = EDataType.Char;
            bool bIsTriStrip = false;

            int iMaterialIndex = -1;
            EDataType eType2 = EDataType.Char;

            foreach (var element in _pcDom.Elements)
            {
                int i = _pcDom.Elements.IndexOf(element);
                if (element.Semantic == EElementSemantic.Face)
                {
                    pcList = _pcDom.ElementData[i];
                    foreach (var property in element.Properties)
                    {
                        int a = element.Properties.IndexOf(property);

                        if (property.Semantic == ESemantic.VertexIndex)
                        {
                            if (!property.IsList) continue;
                            iProperty = a;
                            bOne = true;
                            eType = property.Type;
                        }
                        else if (property.Semantic == ESemantic.MaterialIndex)
                        {
                            if (property.IsList) continue;
                            iMaterialIndex = a;
                            bOne = true;
                            eType2 = property.Type;
                        }
                    }
                    break;
                }
                else if (element.Semantic == EElementSemantic.TriStrip)
                {
                    pcList = _pcDom.ElementData[i];
                    foreach (var property in element.Properties)
                    {
                        int a = element.Properties.IndexOf(property);
                        if (!property.IsList) continue;
                        iProperty = a;
                        bOne = true;
                        bIsTriStrip = true;
                        eType = property.Type;
                        break;
                    }
                    break;
                }
            }

            if (pcList != null && bOne)
            {
                if (!bIsTriStrip)
                {
                    foreach (var instance in pcList.Instances)
                    {
                        Face sFace = new Face();

                        if (iProperty != -1)
                        {
                            int iNum = instance.Properties[iProperty].Values.Count;
                            sFace.Indices = new int[iNum];

                            var p = instance.Properties[iProperty].Values;

                            for (int i = 0; i < iNum; i++)
                            {
                                sFace.Indices[i] = (int)p[i];
                            }
                        }

                        if (iMaterialIndex != -1)
                            sFace.MaterialIndex = (int)instance.Properties[iMaterialIndex].Values[0];

                        pvOut.Add(sFace);
                    }
                }
                else
                {
                    bool flip = false;
                    foreach (var instance in pcList.Instances)
                    {
                        var quak = instance.Properties[iProperty].Values;

                        int[] aiTable = { -1, -1 };
                        foreach (var number in quak)
                        {
                            int p = (int)number;

                            if (p == -1)
                            {
                                aiTable[0] = -1;
                                aiTable[1] = -1;
                                flip = false;
                                continue;
                            }
                            if (aiTable[0] == -1)
                            {
                                aiTable[0] = p;
                                continue;
                            }
                            if (aiTable[1] == -1)
                            {
                                aiTable[1] = p;
                                continue;
                            }
                            pvOut.Add(new Face());
                            Face sFace = pvOut[pvOut.Count - 1];
                            sFace.Indices = new int[3];
                            sFace.Indices[0] = aiTable[0];
                            sFace.Indices[1] = aiTable[1];
                            sFace.Indices[2] = p;
                            flip = !flip;
                            if (flip)
                            {
                                int t = sFace.Indices[0];
                                sFace.Indices[0] = sFace.Indices[1];
                                sFace.Indices[1] = t;
                            }
                            aiTable[0] = aiTable[1];
                            aiTable[1] = p;
                        }
                    }
                }
            }
        }

        private void LoadMaterial(List<AiMaterial> pvOut)
        {
            int[][] aaiPositions = new int[3][] { new int[4], new int[4], new int[4] };
            EDataType[][] aaiTypes = new EDataType[3][] { new EDataType[4], new EDataType[4], new EDataType[4] };
            ElementInstanceList pcList = null;

            int iPhong = -1;
            EDataType ePhong = EDataType.Char;

            int iOpacity = -1;
            EDataType eOpacity = EDataType.Char;

            foreach (var element in _pcDom.Elements)
            {
                int i = _pcDom.Elements.IndexOf(element);

                if (element.Semantic == EElementSemantic.Material)
                {
                    pcList = _pcDom.ElementData[i];

                    foreach (var property in element.Properties)
                    {
                        if (property.IsList) continue;

                        int a = element.Properties.IndexOf(property);

                        switch (property.Semantic)
                        {
                            case ESemantic.PhongPower:
                                iPhong = a;
                                ePhong = property.Type;
                                break;
                            case ESemantic.Opacity:
                                iOpacity = a;
                                eOpacity = property.Type;
                                break;
                            case ESemantic.DiffuseRed:
                                aaiPositions[0][0] = a;
                                aaiTypes[0][0] = property.Type;
                                break;
                            case ESemantic.DiffuseGreen:
                                aaiPositions[0][1] = a;
                                aaiTypes[0][1] = property.Type;
                                break;
                            case ESemantic.DiffuseBlue:
                                aaiPositions[0][2] = a;
                                aaiTypes[0][2] = property.Type;
                                break;
                            case ESemantic.DiffuseAlpha:
                                aaiPositions[0][3] = a;
                                aaiTypes[0][3] = property.Type;
                                break;
                            case ESemantic.SpecularRed:
                                aaiPositions[1][0] = a;
                                aaiTypes[1][0] = property.Type;
                                break;
                            case ESemantic.SpecularGreen:
                                aaiPositions[1][1] = a;
                                aaiTypes[1][1] = property.Type;
                                break;
                            case ESemantic.SpecularBlue:
                                aaiPositions[1][2] = a;
                                aaiTypes[1][2] = property.Type;
                                break;
                            case ESemantic.SpecularAlpha:
                                aaiPositions[1][3] = a;
                                aaiTypes[1][3] = property.Type;
                                break;
                            case ESemantic.AmbientRed:
                                aaiPositions[2][0] = a;
                                aaiTypes[2][0] = property.Type;
                                break;
                            case ESemantic.AmbientGreen:
                                aaiPositions[2][1] = a;
                                aaiTypes[2][1] = property.Type;
                                break;
                            case ESemantic.AmbientBlue:
                                aaiPositions[2][2] = a;
                                aaiTypes[2][2] = property.Type;
                                break;
                            case ESemantic.AmbientAlpha:
                                aaiPositions[2][3] = a;
                                aaiTypes[2][3] = property.Type;
                                break;
                        }
                    }
                    break;
                }
            }

            if (pcList != null)
            {
                foreach (var elementInstance in pcList.Instances)
                {
                    var material = new AiMaterial();

                    Vector4 dOut = new Vector4();
                    Vector4 sOut = new Vector4();
                    Vector4 aOut = new Vector4();
                    GetMaterialColor(elementInstance.Properties, aaiPositions[0], aaiTypes[0], ref dOut);
                    GetMaterialColor(elementInstance.Properties, aaiPositions[1], aaiTypes[1], ref sOut);
                    GetMaterialColor(elementInstance.Properties, aaiPositions[2], aaiTypes[2], ref aOut);
                    material.Color = new AiMaterial.MatColor()
                    {
                        Diffuse = new Vector4(dOut.X, dOut.Y, dOut.Z, dOut.W),
                        Specular = new Vector4(sOut.X, sOut.Y, sOut.Z, sOut.W),
                        Ambient = new Vector4(aOut.X, aOut.Y, aOut.Z, aOut.W)
                    };

                    AiShadingMode iMode = AiShadingMode.Gouraud;
                    if (iPhong != -1)
                    {
                        float fSpec = (float)elementInstance.Properties[iPhong].Values[0];

                        if (fSpec != 0f)
                        {
                            fSpec *= 15;
                            material.Shininess = fSpec;

                            iMode = AiShadingMode.Phong;
                        }
                    }
                    material.ShadingModel = iMode;

                    if (iOpacity != -1)
                    {
                        float fOpacity = (float)elementInstance.Properties[iOpacity].Values[0];
                        material.Opacity = fOpacity;
                    }

                    material.IsTwoSided = true;

                    pvOut.Add(material);
                }
            }
        }

        private void GetMaterialColor(List<PropertyInstance> avList, int[] aiPosition, EDataType[] aiTypes, ref Vector4 clrOut)
        {
            clrOut.X = aiPosition[0] == -1 ? 0f : NormalizeColorValue(avList[aiPosition[0]].Values[0], aiTypes[0]);
            clrOut.Y = aiPosition[1] == -1 ? 0f : NormalizeColorValue(avList[aiPosition[1]].Values[0], aiTypes[1]);
            clrOut.Z = aiPosition[2] == -1 ? 0f : NormalizeColorValue(avList[aiPosition[2]].Values[0], aiTypes[2]);
            clrOut.W = aiPosition[3] == -1 ? 1f : NormalizeColorValue(avList[aiPosition[3]].Values[0], aiTypes[3]);
        }

        private float NormalizeColorValue(object value, EDataType eType)
        {
            switch (eType)
            {
                case EDataType.Float:
                case EDataType.Double:
                    return Convert.ToSingle(value);
                case EDataType.UChar:
                    return Convert.ToByte(value) / 255f;
                case EDataType.Char:
                    return Convert.ToSByte(value) / 127f;
                case EDataType.UShort:
                    return Convert.ToUInt16(value) / 65535f;
                case EDataType.Short:
                    return Convert.ToInt16(value) / 32767f;
                case EDataType.UInt:
                    return Convert.ToUInt32(value) / 4294967295f;
                case EDataType.Int:
                    return Convert.ToInt32(value) / 2147483647f;
                default:
                    return 0f;
            }
        }

        private void LoadVertexColor(List<Vector4> pvOut)
        {
            int[] aiPositions = new int[4] { -1, -1, -1, -1 };
            EDataType[] aiTypes = new EDataType[4];
            int cnt = 0;
            ElementInstanceList pcList = null;

            foreach (var element in _pcDom.Elements)
            {
                int i = _pcDom.Elements.IndexOf(element);

                if (element.Semantic == EElementSemantic.Vertex)
                {
                    pcList = _pcDom.ElementData[i];

                    foreach (var property in element.Properties)
                    {
                        if (property.IsList) continue;

                        int a = element.Properties.IndexOf(property);

                        if (property.Semantic == ESemantic.Red)
                        {
                            cnt++;
                            aiPositions[0] = a;
                            aiTypes[0] = property.Type;
                        }
                        if (property.Semantic == ESemantic.Green)
                        {
                            cnt++;
                            aiPositions[1] = a;
                            aiTypes[1] = property.Type;
                        }
                        if (property.Semantic == ESemantic.Blue)
                        {
                            cnt++;
                            aiPositions[2] = a;
                            aiTypes[2] = property.Type;
                        }
                        if (property.Semantic == ESemantic.Alpha)
                        {
                            cnt++;
                            aiPositions[3] = a;
                            aiTypes[3] = property.Type;
                        }
                        if (cnt == 4) break;
                    }
                    break;
                }
            }

            if (pcList != null && cnt != 0)
            {
                foreach (var elementInstance in pcList.Instances)
                {
                    Vector4 vOut = new Vector4();

                    for (int i = 0; i < 4; i++)
                    {
                        if (aiPositions[i] != -1)
                            vOut[i] = NormalizeColorValue(elementInstance.Properties[aiPositions[i]].Values[0], aiTypes[i]);
                    }

                    if (aiPositions[3] == -1)
                        vOut.W = 1.0f;

                    pvOut.Add(vOut);
                }
            }
        }

        private void LoadTextureCoordinates(List<Vector2> pvOut)
        {
            int[] aiPosition = new int[2] { -1, -1 };
            EDataType[] aiTypes = new EDataType[2];
            ElementInstanceList pcList = null;
            int cnt = 0;

            foreach (var element in _pcDom.Elements)
            {
                int i = _pcDom.Elements.IndexOf(element);

                if (element.Semantic == EElementSemantic.Vertex)
                {
                    pcList = _pcDom.ElementData[i];

                    foreach (var property in element.Properties)
                    {
                        if (property.IsList) continue;

                        int a = element.Properties.IndexOf(property);

                        if (property.Semantic == ESemantic.UTextureCoord)
                        {
                            cnt++;
                            aiPosition[0] = a;
                            aiTypes[0] = property.Type;
                        }
                        else if (property.Semantic == ESemantic.VTextureCoord)
                        {
                            cnt++;
                            aiPosition[1] = a;
                            aiTypes[1] = property.Type;
                        }
                    }
                }
            }

            if (pcList != null && cnt != 0)
            {
                foreach (var elementInstance in pcList.Instances)
                {
                    Vector2 vOut = new Vector2();

                    if (aiPosition[0] != -1)
                        vOut.X = (float)elementInstance.Properties[aiPosition[0]].Values[0];

                    if (aiPosition[1] != -1)
                        vOut.Y = (float)elementInstance.Properties[aiPosition[1]].Values[0];

                    pvOut.Add(vOut);
                }
            }
        }

        private void ReplaceDefaultMaterial(List<Face> avFaces, List<AiMaterial> avMaterials)
        {
            bool bNeedDefaultMat = false;

            foreach (var face in avFaces)
            {
                if (face.MaterialIndex == -1)
                {
                    bNeedDefaultMat = true;
                    face.MaterialIndex = avMaterials.Count;
                }
                else if (face.MaterialIndex >= avMaterials.Count)
                {
                    face.MaterialIndex = avMaterials.Count - 1;
                }
            }

            if (bNeedDefaultMat)
            {
                avMaterials.Add(new AiMaterial
                {
                    ShadingModel = AiShadingMode.Gouraud,
                    Color = new AiMaterial.MatColor
                    {
                        Diffuse = new Vector4(0.6f),
                        Specular = new Vector4(0.6f),
                        Ambient = new Vector4(0.05f)
                    },
                    IsTwoSided = true
                });
            }
        }

        private void ConvertMeshes(List<Face> avFaces, List<Vector3> avPositions, List<Vector3> avNormals,
                                   List<Vector4> avColors, List<Vector2> avTexCoords, List<AiMaterial> avMaterials,
                                   List<AiMesh> avOut)
        {
            var aiSplit = new List<List<int>>();
            for (int i = 0; i < avMaterials.Count; i++)
            {
                aiSplit.Add(new List<int>());
            }

            for (int i = 0; i < avFaces.Count; i++)
            {
                aiSplit[avFaces[i].MaterialIndex].Add(i);
            }

            for (int p = 0; p < avMaterials.Count; p++)
            {
                if (aiSplit[p].Count > 0)
                {
                    var mesh = new AiMesh
                    {
                        MaterialIndex = p,
                        PrimitiveType = AiPrimitiveType.Triangle
                    };

                    int vertexCount = aiSplit[p].Sum(faceIndex => avFaces[faceIndex].Indices.Length);

                    mesh.Vertices = new List<Vector3>(vertexCount);
                    mesh.Faces = new List<AiFace>();

                    if (avColors.Count > 0)
                        mesh.VertexColorChannels[0] = new List<Vector4>(vertexCount);
                    if (avTexCoords.Count > 0)
                        mesh.TextureCoordinateChannels[0] = new List<float[]>(vertexCount);
                    if (avNormals.Count > 0)
                        mesh.Normals = new List<Vector3>(vertexCount);

                    int vertexIndex = 0;
                    foreach (int faceIndex in aiSplit[p])
                    {
                        var face = avFaces[faceIndex];
                        var aiFace = new AiFace();

                        foreach (int idx in face.Indices)
                        {
                            aiFace.Add(vertexIndex);

                            if (idx < avPositions.Count)
                            {
                                mesh.Vertices.Add(avPositions[idx]);

                                if (avColors.Count > 0)
                                    mesh.VertexColorChannels[0].Add(avColors[idx]);

                                if (avTexCoords.Count > 0)
                                    mesh.TextureCoordinateChannels[0].Add([ avTexCoords[idx].X, avTexCoords[idx].Y, 0 ]);

                                if (avNormals.Count > 0)
                                    mesh.Normals.Add(avNormals[idx]);

                                vertexIndex++;
                            }
                        }

                        mesh.Faces.Add(aiFace);
                    }

                    avOut.Add(mesh);
                }
            }
        }
    }
}
