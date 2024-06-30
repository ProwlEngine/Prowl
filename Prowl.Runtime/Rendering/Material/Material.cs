using System;

namespace Prowl.Runtime
{
    public sealed class Material : EngineObject
    {
        public Utils.KeyGroup<string, string> LocalKeywords;
        public AssetRef<Shader> Shader;
        public PropertyState Properties;

        internal Material() : base("New Material") { }

        public Material(AssetRef<Shader> shader, PropertyState? properties = null, Utils.KeyGroup<string, string>? keywords = null) : base("New Material")
        {
            if (shader.Res == null) 
                throw new ArgumentNullException(nameof(shader));
            
            Shader = shader;
            Properties = properties ?? new();
            LocalKeywords = keywords ?? Utils.KeyGroup<string, string>.Default;
        }

        public void SetKeyword(string keyword, string value) => LocalKeywords.SetKey(keyword, value);

        public void SetColor(string name, Color value) => Properties.SetColor(name, value);
        public void SetVector(string name, Vector2 value) => Properties.SetVector(name, value);
        public void SetVector(string name, Vector3 value) => Properties.SetVector(name, value);
        public void SetVector(string name, Vector4 value) => Properties.SetVector(name, value);
        public void SetFloat(string name, float value) => Properties.SetFloat(name, value);
        public void SetInt(string name, int value) => Properties.SetInt(name, value);
        public void SetMatrix(string name, Matrix4x4 value) => Properties.SetMatrix(name, value);
        public void SetTexture(string name, Texture value) => Properties.SetTexture(name, value);

        //public void SetMatrices(string name, System.Numerics.Matrix4x4[] value) { }

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
