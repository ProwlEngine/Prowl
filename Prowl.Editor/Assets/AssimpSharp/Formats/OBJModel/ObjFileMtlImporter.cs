using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AssimpSharp.Formats.Obj
{
    public class ObjFileMtlImporter
    {
        private readonly Model m_pModel;

        public ObjFileMtlImporter(List<string> buffer, Model model)
        {
            m_pModel = model;

            if (m_pModel.DefaultMaterial == null)
                m_pModel.DefaultMaterial = new ObjMaterial() { MaterialName = "default" };

            Load(buffer);
        }

        private void Load(List<string> buffer)
        {
            foreach (var line in buffer)
            {
                var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;

                switch (words[0][0])
                {
                    case 'k':
                    case 'K':
                        switch (words[0][1])
                        {
                            case 'a': m_pModel.DefaultMaterial.Ambient = ParseVector3(words.Skip(1).ToArray()); break;
                            case 'd': m_pModel.DefaultMaterial.Diffuse = ParseVector3(words.Skip(1).ToArray()); break;
                            case 's': m_pModel.DefaultMaterial.Specular = ParseVector3(words.Skip(1).ToArray()); break;
                            case 'e': m_pModel.DefaultMaterial.Emissive = ParseVector3(words.Skip(1).ToArray()); break;
                        }
                        break;
                    case 'T':
                        if (words[0][1] == 'f')
                            m_pModel.CurrentMaterial.Transparent = ParseVector3(words.Skip(1).ToArray());
                        break;
                    case 'd':
                        if (words[0] == "disp")
                            GetTexture(line);
                        else
                            m_pModel.CurrentMaterial.Alpha = float.Parse(words[1]);
                        break;
                    case 'n':
                    case 'N':
                        switch (words[0][1])
                        {
                            case 's': m_pModel.CurrentMaterial.Shininess = float.Parse(words[1]); break;
                            case 'i': m_pModel.CurrentMaterial.Ior = float.Parse(words[1]); break;
                            case 'e': CreateMaterial(words[1]); break;
                        }
                        break;
                    case 'm':
                    case 'b':
                    case 'r':
                        GetTexture(line);
                        break;
                    //case 'i':
                    //    m_pModel.CurrentMaterial.IlluminationModel = int.Parse(words[1]);
                    //    break;
                }
            }
        }

        private void GetTexture(string line)
        {
            var words = line.Split('#')[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            ObjMaterial.ObjTexture.Type? type = null;
            bool clamped = false;

            if (words[0] == "refl" && words.Contains(TypeOption))
                type = ReflMap[words[Array.IndexOf(words, TypeOption) + 1]];
            else
            {
                TokenMap.TryGetValue(words[0], out var t);
                type = t;
            }

            if (type == null)
                throw new Exception("OBJ/MTL: Encountered unknown texture type");

            if (words.Contains(ClampOption))
                clamped = words[Array.IndexOf(words, ClampOption) + 1] == "on";

            m_pModel.CurrentMaterial.Textures.Add(new ObjMaterial.ObjTexture() { Clamp = clamped, Name = words[1], TextureType = type.Value });
        }

        private void CreateMaterial(string matName)
        {
            if (!m_pModel.MaterialMap.TryGetValue(matName, out ObjMaterial mat))
            {
                m_pModel.CurrentMaterial = new ObjMaterial() { MaterialName = matName };
                if (m_pModel.CurrentMesh != null)
                    m_pModel.CurrentMesh.MaterialIndex = m_pModel.MaterialLib.Count - 1;
                m_pModel.MaterialLib.Add(matName);
                m_pModel.MaterialMap[matName] = m_pModel.CurrentMaterial;
            }
            else
            {
                m_pModel.CurrentMaterial = mat;
            }
        }

        private Vector3 ParseVector3(string[] components)
        {
            return new Vector3(
                float.Parse(components[0]),
                components.Length > 1 ? float.Parse(components[1]) : 0,
                components.Length > 2 ? float.Parse(components[2]) : 0
            );
        }

        private static readonly Dictionary<string, ObjMaterial.ObjTexture.Type> TokenMap = new Dictionary<string, ObjMaterial.ObjTexture.Type>
        {
            {"map_Kd", ObjMaterial.ObjTexture.Type.Diffuse},
            {"map_Ka", ObjMaterial.ObjTexture.Type.Ambient},
            {"map_Ks", ObjMaterial.ObjTexture.Type.Specular},
            {"map_d", ObjMaterial.ObjTexture.Type.Opacity},
            {"map_emissive", ObjMaterial.ObjTexture.Type.Emissive},
            {"map_Ke", ObjMaterial.ObjTexture.Type.Emissive},
            {"map_bump", ObjMaterial.ObjTexture.Type.Bump},
            {"map_Bump", ObjMaterial.ObjTexture.Type.Bump},
            {"bump", ObjMaterial.ObjTexture.Type.Bump},
            {"map_Kn", ObjMaterial.ObjTexture.Type.Normal},
            {"disp", ObjMaterial.ObjTexture.Type.Displacement},
            {"map_ns", ObjMaterial.ObjTexture.Type.Specularity}
        };

        private static readonly Dictionary<string, ObjMaterial.ObjTexture.Type> ReflMap = new Dictionary<string, ObjMaterial.ObjTexture.Type>
        {
            {"sphere", ObjMaterial.ObjTexture.Type.ReflectionSphere},
            {"cube_top", ObjMaterial.ObjTexture.Type.ReflectionCubeTop},
            {"cube_bottom", ObjMaterial.ObjTexture.Type.ReflectionCubeBottom},
            {"cube_front", ObjMaterial.ObjTexture.Type.ReflectionCubeFront},
            {"cube_back", ObjMaterial.ObjTexture.Type.ReflectionCubeBack},
            {"cube_left", ObjMaterial.ObjTexture.Type.ReflectionCubeLeft},
            {"cube_right", ObjMaterial.ObjTexture.Type.ReflectionCubeRight}
        };

        private const string ClampOption = "-clamp";
        private const string TypeOption = "-Type";
    }
}
