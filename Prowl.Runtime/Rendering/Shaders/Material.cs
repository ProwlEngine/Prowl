using System;
using System.Collections.Generic;
using System.Linq;
using Veldrid;

namespace Prowl.Runtime
{
    public sealed class Material : EngineObject
    {
        public AssetRef<Shader> Shader;
        public MaterialPropertyBlock PropertyBlock;


        public Material()
        {

        }

        public Material(AssetRef<Shader> shader)
        {
            if (shader.AssetID == Guid.Empty) throw new ArgumentNullException(nameof(shader));
            Shader = shader;
        }

        public void SetKeyword(string keyword, bool state)
        {
            if (state) EnableKeyword(keyword);
            else DisableKeyword(keyword);
        }

        public void EnableKeyword(string keyword)
        {
            string? key = keyword?.ToUpper().Replace(" ", "").Replace(";", "");
            if (string.IsNullOrWhiteSpace(key)) return;
        }

        public void DisableKeyword(string keyword)
        {
            string? key = keyword?.ToUpper().Replace(" ", "").Replace(";", "");
            if (string.IsNullOrWhiteSpace(key)) return;
        }


        public void SetPass(int pass, bool apply = false)
        {
            
        }



        public void SetColor(string name, Color value) => PropertyBlock.SetColor(name, value);
        public void SetVector(string name, Vector2 value) => PropertyBlock.SetVector(name, value);
        public void SetVector(string name, Vector3 value) => PropertyBlock.SetVector(name, value);
        public void SetVector(string name, Vector4 value) => PropertyBlock.SetVector(name, value);
        public void SetFloat(string name, float value) => PropertyBlock.SetFloat(name, value);
        public void SetInt(string name, int value) => PropertyBlock.SetInt(name, value);
        public void SetMatrix(string name, Matrix4x4 value) => PropertyBlock.SetMatrix(name, value);
        public void SetMatrices(string name, System.Numerics.Matrix4x4[] value) => PropertyBlock.SetMatrices(name, value);
        public void SetTexture(string name, Texture value) => PropertyBlock.SetTexture(name, value);
        public void SetTexture(string name, AssetRef<Texture> value) => PropertyBlock.SetTexture(name, value);

        //public CompoundTag Serialize(string tagName, TagSerializer.SerializationContext ctx)
        //{
        //    CompoundTag compoundTag = new CompoundTag(tagName);
        //    compoundTag.Add(TagSerializer.Serialize(Shader, "Shader", ctx));
        //    compoundTag.Add(TagSerializer.Serialize(PropertyBlock, "PropertyBlock", ctx));
        //    return compoundTag;
        //}
        //
        //public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
        //{
        //    Shader = TagSerializer.Deserialize<AssetRef<Shader>>(value["Shader"], ctx);
        //    PropertyBlock = TagSerializer.Deserialize<MaterialPropertyBlock>(value["PropertyBlock"], ctx);
        //}
    }

}
