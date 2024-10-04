using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace AssimpSharp.Formats.Obj
{
    public class ObjFileParser
    {
        private const string DEFAULT_MATERIAL = Constants.AI_DEFAULT_MATERIAL_NAME;
        private const string DefaultObjName = "defaultobject";

        private readonly FileInfo file;
        public Model Model { get; } = new Model();

        public ObjFileParser(FileInfo file)
        {
            this.file = file;

            // Create the model instance to store all the data
            Model.ModelName = file.Name;

            // Create default material and store it
            Model.DefaultMaterial = new ObjMaterial() { MaterialName = DEFAULT_MATERIAL };
            Model.MaterialLib.Add(DEFAULT_MATERIAL);
            Model.MaterialMap[DEFAULT_MATERIAL] = Model.DefaultMaterial;

            // Start parsing the file
            ParseFile(File.ReadAllLines(file.FullName));
        }

        private void ParseFile(string[] streamBuffer)
        {
            foreach (var line in streamBuffer)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                switch (line[0])
                {
                    case 'v':
                        ParseVertexData(line, words);
                        break;
                    case 'p':
                        GetFace(AiPrimitiveType.Point, line);
                        break;
                    case 'l':
                        GetFace(AiPrimitiveType.Line, line);
                        break;
                    case 'f':
                        GetFace(AiPrimitiveType.Polygon, line);
                        break;
                    case 'u':
                        if (words[0] == "usemtl") GetMaterialDesc(line);
                        break;
                    case 'm':
                        if (words[0] == "mg") GetGroupNumberAndResolution();
                        else if (words[0] == "mtllib") GetMaterialLib(words);
                        break;
                    case 'g':
                        GetGroupName(line);
                        break;
                    case 's':
                        GetGroupNumber();
                        break;
                    case 'o':
                        GetObjectName(line);
                        break;
                }
            }
        }

        private void ParseVertexData(string line, string[] words)
        {
            switch (line[1])
            {
                case ' ':
                case '\t':
                    switch (words.Length - 1)
                    {
                        case 3:
                            Model.Vertices.Add(new Vector3(
                                float.Parse(words[1]),
                                float.Parse(words[2]),
                                float.Parse(words[3])
                            ));
                            break;
                        case 4:
                            float w = float.Parse(words[4]);
                            if (w == 0) throw new Exception("Invalid w coordinate");
                            Model.Vertices.Add(new Vector3(
                                float.Parse(words[1]) / w,
                                float.Parse(words[2]) / w,
                                float.Parse(words[3]) / w
                            ));
                            break;
                        case 6:
                            Model.Vertices.Add(new Vector3(
                                float.Parse(words[1]),
                                float.Parse(words[2]),
                                float.Parse(words[3])
                            ));
                            Model.VertexColors.Add(new Vector3(
                                float.Parse(words[4]),
                                float.Parse(words[5]),
                                float.Parse(words[6])
                            ));
                            break;
                    }
                    break;
                case 't':
                    Model.TextureCoord.Add(new List<float>
                    {
                        float.Parse(words[1]),
                        float.Parse(words[2]),
                        words.Length > 3 ? float.Parse(words[3]) : 0f
                    });
                    break;
                case 'n':
                    Model.Normals.Add(new Vector3(
                        float.Parse(words[1]),
                        float.Parse(words[2]),
                        float.Parse(words[3])
                    ));
                    break;
            }
        }

        private void GetFace(AiPrimitiveType type, string line)
        {
            var vertices = line.Substring(1).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var face = new Face { PrimitiveType = type };
            bool hasNormal = false;

            int vSize = Model.Vertices.Count;
            int vtSize = Model.TextureCoord.Count;
            int vnSize = Model.Normals.Count;

            foreach (var vertex in vertices)
            {
                if (vertex[0] == '/' && type == AiPrimitiveType.Point)
                    throw new Exception("Obj: Separator unexpected in point statement");

                var component = vertex.Split('/');

                for (int i = 0; i < component.Length; i++)
                {
                    if (string.IsNullOrEmpty(component[i])) continue;

                    int iVal = int.Parse(component[i]);

                    if (iVal > 0)
                    {
                        switch (i)
                        {
                            case 0: face.Vertices.Add(iVal - 1); break;
                            case 1: face.TextureCoords.Add(iVal - 1); break;
                            case 2:
                                face.Normals.Add(iVal - 1);
                                hasNormal = true;
                                break;
                            default: throw new Exception("OBJ: Unsupported token in face description detected");
                        }
                    }
                    else if (iVal < 0)
                    {
                        switch (i)
                        {
                            case 0: face.Vertices.Add(vSize + iVal); break;
                            case 1: face.TextureCoords.Add(vtSize + iVal); break;
                            case 2:
                                face.Normals.Add(vnSize + iVal);
                                hasNormal = true;
                                break;
                            default: throw new Exception("OBJ: Unsupported token in face description detected");
                        }
                    }
                }
            }

            if (face.Vertices.Count == 0) throw new Exception("OBJ: Ignoring empty face");

            // Set active material, if one set
            face.Material = Model.CurrentMaterial ?? Model.DefaultMaterial;

            // Create a default object, if nothing is there
            if (Model.Current == null)
                CreateObject(DefaultObjName);

            // Assign face to mesh
            if (Model.CurrentMesh == null)
                CreateMesh(DefaultObjName);

            // Store the face
            Model.CurrentMesh.Faces.Add(face);
            Model.CurrentMesh.NumIndices += face.Vertices.Count;
            Model.CurrentMesh.UVCoordinates[0] += face.TextureCoords.Count;
            if (!Model.CurrentMesh.HasNormals && hasNormal)
                Model.CurrentMesh.HasNormals = true;
        }

        private void CreateObject(string objName)
        {
            Model.Current = new Object { ObjName = objName };
            Model.Objects.Add(Model.Current);

            CreateMesh(objName);

            if (Model.CurrentMaterial != null)
            {
                Model.CurrentMesh.MaterialIndex = Model.MaterialLib.IndexOf(Model.CurrentMaterial.MaterialName);
                Model.CurrentMesh.Material = Model.CurrentMaterial;
            }
        }

        private void CreateMesh(string meshName)
        {
            Model.CurrentMesh = new ObjMesh() { Name = meshName };
            Model.Meshes.Add(Model.CurrentMesh);
            int meshId = Model.Meshes.Count - 1;
            if (Model.Current != null)
                Model.Current.Meshes.Add(meshId);
            else
                throw new Exception("OBJ: No object detected to attach a new mesh instance.");
        }

        private void GetMaterialDesc(string line)
        {
            string strName = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[1].Trim();

            if (Model.CurrentMaterial == null || Model.CurrentMaterial.MaterialName != strName)
            {
                if (!Model.MaterialMap.TryGetValue(strName, out ObjMaterial material))
                {
                    Console.Error.WriteLine($"OBJ: failed to locate material {strName}, creating new material");
                    material = new ObjMaterial() { MaterialName = strName };
                    Model.MaterialLib.Add(strName);
                    Model.MaterialMap[strName] = material;
                }
                Model.CurrentMaterial = material;
            }

            if (NeedsNewMesh(strName))
                CreateMesh(strName);

            Model.CurrentMesh.MaterialIndex = Model.MaterialLib.IndexOf(strName);
        }

        private bool NeedsNewMesh(string materialName)
        {
            if (Model.CurrentMesh == null) return true;

            int matIdx = Model.MaterialLib.IndexOf(materialName);
            int curMatIdx = Model.CurrentMesh.MaterialIndex;
            return curMatIdx != ObjMesh.NoMaterial && curMatIdx != matIdx && Model.CurrentMesh.Faces.Count > 0;
        }

        private void GetGroupNumberAndResolution()
        {
            // Not implemented
        }

        private void GetMaterialLib(string[] words)
        {
            if (words.Length < 2) throw new Exception("File name of the material is absent.");

            string filename = words[1];
            string fullPath = Path.Combine(file.DirectoryName, filename);

            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"OBJ: Unable to locate material file {filename}");
                string strMatFallbackName = Path.ChangeExtension(filename, ".mtl");
                Console.WriteLine($"OBJ: Opening fallback material file {strMatFallbackName}");
                fullPath = Path.Combine(file.DirectoryName, strMatFallbackName);
                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"OBJ: Unable to locate fallback material file {strMatFallbackName}");
                    return;
                }
            }

            var buffer = File.ReadAllLines(fullPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            new ObjFileMtlImporter(buffer, Model);
        }

        private void GetGroupName(string line)
        {
            string groupName = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[1];
            if (Model.ActiveGroup != groupName)
            {
                CreateObject(groupName);
                if (!Model.Groups.ContainsKey(groupName))
                    Model.Groups[groupName] = new List<int>();
                else
                    Model.GroupFaceIDs = Model.Groups[groupName];

                Model.ActiveGroup = groupName;
            }
        }

        private void GetGroupNumber()
        {
            // Not implemented
        }

        private void GetObjectName(string line)
        {
            string objectName = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[1];

            if (!string.IsNullOrEmpty(objectName))
            {
                Model.Current = Model.Objects.FirstOrDefault(o => o.ObjName == objectName);

                if (Model.Current == null)
                    CreateObject(objectName);
            }
        }
    }
}
