using System.Numerics;
using System.Text;

namespace AssimpSharp.Formats.Stl
{
    public class STLImporter : BaseImporter
    {
        public override AiImporterDesc Info => new AiImporterDesc {
            Name = "Stereolithography (STL) Importer",
            Flags = AiImporterFlags.SupportTextFlavour | AiImporterFlags.SupportBinaryFlavour,
            FileExtensions = [ "stl" ]
        };

        private byte[] mBuffer;
        private int fileSize;
        private AiScene pScene;
        private Vector4 clrColorDefault = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);

        public static bool IsBinarySTL(byte[] buffer, int fileSize)
        {
            if (fileSize < 84)
            {
                return false;
            }

            int faceCount = BitConverter.ToInt32(buffer, 80);
            int expectedBinaryFileSize = faceCount * 50 + 84;

            return expectedBinaryFileSize == fileSize;
        }

        public static bool IsAsciiSTL(byte[] buffer, int fileSize)
        {
            if (IsBinarySTL(buffer, fileSize)) return false;

            if (fileSize < 6) return false;

            string header = Encoding.ASCII.GetString(buffer, 0, 5);
            bool isASCII = header.Trim().ToLower().StartsWith("solid");

            if (isASCII && fileSize >= 500)
            {
                isASCII = buffer.Take(500).All(b => b <= 127);
            }

            return isASCII;
        }

        public override bool CanRead(Uri file, bool checkSig)
        {
            string extension = Path.GetExtension(file.LocalPath).ToLowerInvariant();
            if (extension == ".stl")
            {
                return true;
            }
            // TODO: Implement signature checking if needed
            return false;
        }

        public override void InternReadFile(Uri pFile, AiScene pScene)
        {
            mBuffer = File.ReadAllBytes(pFile.LocalPath);
            fileSize = mBuffer.Length;

            this.pScene = pScene;

            pScene.RootNode = new AiNode();

            bool bMatClr = false;

            if (IsBinarySTL(mBuffer, fileSize))
                bMatClr = LoadBinaryFile();
            else if (IsAsciiSTL(mBuffer, fileSize))
                LoadASCIIFile();
            else
                throw new Exception($"Failed to determine STL storage representation for {pFile}.");

            pScene.RootNode.NumMeshes = pScene.NumMeshes;
            pScene.RootNode.Meshes = Enumerable.Range(0, pScene.NumMeshes).ToArray();

            var pcMat = new AiMaterial {
                Name = Constants.AI_DEFAULT_MATERIAL_NAME,
                Color = new AiMaterial.MatColor {
                    Diffuse = bMatClr ? new Vector4(clrColorDefault.X, clrColorDefault.Y, clrColorDefault.Z, 1f) : new Vector4(1),
                    Specular = bMatClr ? new Vector4(clrColorDefault.X, clrColorDefault.Y, clrColorDefault.Z, 1f) : new Vector4(1),
                    Ambient = new Vector4(1)
                }
            };

            pScene.NumMaterials = 1;
            pScene.Materials.Add(pcMat);
        }

        private bool LoadBinaryFile()
        {
            pScene.NumMeshes = 1;
            var pMesh = new AiMesh { MaterialIndex = 0 };
            pScene.Meshes.Add(pMesh);

            if (fileSize < 84) throw new Exception("STL: file is too small for the header");

            bool bIsMaterialise = false;

            for (int i = 0; i < 80 - 5; i++)
            {
                if (Encoding.ASCII.GetString(mBuffer, i, 6) == "COLOR=")
                {
                    bIsMaterialise = true;
                    float invByte = 1.0f / 255.0f;
                    clrColorDefault.X = mBuffer[i + 6] * invByte;
                    clrColorDefault.Y = mBuffer[i + 7] * invByte;
                    clrColorDefault.Z = mBuffer[i + 8] * invByte;
                    clrColorDefault.W = mBuffer[i + 9] * invByte;
                    break;
                }
            }

            pScene.RootNode.Name = "<STL_BINARY>";

            pMesh.NumFaces = BitConverter.ToInt32(mBuffer, 80);

            if (fileSize < 84 + pMesh.NumFaces * 50)
                throw new Exception("STL: file is too small to hold all facets");

            if (pMesh.NumFaces == 0)
                throw new Exception("STL: file is empty. There are no facets defined");

            pMesh.NumVertices = pMesh.NumFaces * 3;

            pMesh.Vertices = new List<Vector3>();
            pMesh.Normals = new List<Vector3>();

            int dataPos = 84;
            for (int i = 0; i < pMesh.NumFaces; i++)
            {
                Vector3 vn = new Vector3(
                    BitConverter.ToSingle(mBuffer, dataPos),
                    BitConverter.ToSingle(mBuffer, dataPos + 4),
                    BitConverter.ToSingle(mBuffer, dataPos + 8)
                );
                dataPos += 12;

                for (int v = 0; v < 3; v++)
                {
                    pMesh.Normals.Add(vn);
                    pMesh.Vertices.Add(new Vector3(
                        BitConverter.ToSingle(mBuffer, dataPos),
                        BitConverter.ToSingle(mBuffer, dataPos + 4),
                        BitConverter.ToSingle(mBuffer, dataPos + 8)
                    ));
                    dataPos += 12;
                }

                ushort color = BitConverter.ToUInt16(mBuffer, dataPos);
                dataPos += 2;

                if ((color & (1 << 15)) != 0)
                {
                    // Seems we need to take the color
                    if (pMesh.VertexColorChannels.Count == 0)
                    {
                        pMesh.VertexColorChannels.Add(Enumerable.Repeat(new Vector4(clrColorDefault.X, clrColorDefault.Y, clrColorDefault.Z, clrColorDefault.W), pMesh.NumVertices).ToList());
                        Console.WriteLine("STL: Mesh has vertex colors");
                    }

                    Vector4 clr = new Vector4();
                    clr.W = 1f;
                    float invVal = 1f / 31f;

                    if (bIsMaterialise)
                    {
                        // This is reversed
                        clr.X = (color & 0x31) * invVal;
                        clr.Y = ((color & (0x31 << 5)) >> 5) * invVal;
                        clr.Z = ((color & (0x31 << 10)) >> 10) * invVal;
                    }
                    else
                    {
                        clr.Z = (color & 0x31) * invVal;
                        clr.Y = ((color & (0x31 << 5)) >> 5) * invVal;
                        clr.X = ((color & (0x31 << 10)) >> 10) * invVal;
                    }

                    // Assign the color to all vertices of the face
                    for (int v = 0; v < 3; v++)
                    {
                        pMesh.VertexColorChannels[0][i * 3 + v] = clr;
                    }
                }
            }

            AddFacesToMesh(pMesh);

            return bIsMaterialise && pMesh.VertexColorChannels.Count == 0;
        }

        private void LoadASCIIFile()
        {
            var meshes = new List<AiMesh>();
            var positionBuffer = new List<Vector3>();
            var normalBuffer = new List<Vector3>();

            string buffer = Encoding.ASCII.GetString(mBuffer);
            buffer = buffer.Substring(buffer.IndexOf("solid") + 5).Trim();

            var pMesh = new AiMesh { MaterialIndex = 0 };
            meshes.Add(pMesh);

            string[] words = buffer.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            pScene.RootNode.Name = !char.IsWhiteSpace(buffer[0]) ? words[0] : "<STL_ASCII>";

            int faceVertexCounter = 3;
            int i = 0;

            while (i < words.Length)
            {
                string word = words[i];

                if (word == "facet")
                {
                    if (faceVertexCounter != 3) Console.Error.WriteLine("STL: A new facet begins but the old is not yet complete");

                    faceVertexCounter = 0;
                    var vn = new Vector3();
                    normalBuffer.Add(vn);

                    if (words[i + 1] != "normal") Console.Error.WriteLine("STL: a facet normal vector was expected but not found");
                    else
                    {
                        try
                        {
                            i++;
                            vn.X = float.Parse(words[++i]);
                            vn.Y = float.Parse(words[++i]);
                            vn.Z = float.Parse(words[++i]);
                            normalBuffer.Add(vn);
                            normalBuffer.Add(vn);
                        }
                        catch (Exception)
                        {
                            throw new Exception("STL: unexpected EOF while parsing facet");
                        }
                    }
                }
                else if (word == "vertex")
                {
                    if (faceVertexCounter >= 3)
                    {
                        Console.Error.WriteLine("STL: a facet with more than 3 vertices has been found");
                        i++;
                    }
                    else
                    {
                        try
                        {
                            var vn = new Vector3 {
                                X = float.Parse(words[++i]),
                                Y = float.Parse(words[++i]),
                                Z = float.Parse(words[++i])
                            };
                            positionBuffer.Add(vn);
                            faceVertexCounter++;
                        }
                        catch (Exception)
                        {
                            throw new Exception("STL: unexpected EOF while parsing facet");
                        }
                    }
                }
                else if (word == "endsolid")
                {
                    break;
                }

                i++;
            }

            if (positionBuffer.Count == 0)
            {
                pMesh.NumFaces = 0;
                throw new Exception("STL: ASCII file is empty or invalid; no data loaded");
            }
            if (positionBuffer.Count % 3 != 0)
            {
                pMesh.NumFaces = 0;
                throw new Exception("STL: Invalid number of vertices");
            }
            if (normalBuffer.Count != positionBuffer.Count)
            {
                pMesh.NumFaces = 0;
                throw new Exception("Normal buffer size does not match position buffer size");
            }

            pMesh.NumFaces = positionBuffer.Count / 3;
            pMesh.NumVertices = positionBuffer.Count;
            pMesh.Vertices = positionBuffer;
            pMesh.Normals = normalBuffer;

            AddFacesToMesh(pMesh);

            pScene.NumMeshes = meshes.Count;
            pScene.Meshes = meshes;
        }

        private void AddFacesToMesh(AiMesh pMesh)
        {
            pMesh.Faces = new List<AiFace>();
            for (int i = 0; i < pMesh.NumFaces; i++)
            {
                pMesh.Faces.Add(new AiFace { i * 3, i * 3 + 1, i * 3 + 2 });
            }
        }
    }
}
