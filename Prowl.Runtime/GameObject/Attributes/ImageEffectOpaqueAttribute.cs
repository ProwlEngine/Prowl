using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ImageEffectOpaqueAttribute() : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ImageEffectAllowedInSceneViewAttribute() : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ImageEffectTransformsToLDRAttribute() : Attribute
{
}
