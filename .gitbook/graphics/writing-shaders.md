---
description: Prowls Shader Documentation.
---

# Writing Shaders

## Shader Structure

A Prowl Shader file consists of the following main sections:

1. Shader Path
2. Properties
3. Passes
4. Shadow Pass (optional)

Here's an example of the basic structure:

```glsl
Shader "MyShaders/ShaderName"

Properties
{
    // Property declarations
}

Pass 0
{
    // Pass configuration and shader code
}

ShadowPass 0
{
    // Shadow pass configuration and shader code
}
```

### Shader Properties

Shader properties allow you to define input parameters for your shader, such as textures, colors, and floats. The Inspector can access these properties when this shader is assigned to a Material.

To declare a property, use the following syntax inside the `Properties` block:

```glsl
Properties
{
    PropertyName("Display Name", PropertyType)
}
```

* `PropertyName`: The name of the uniform used in the shader code this property references.
* `Display Name`: The user-friendly name displayed in the material inspector.
* `PropertyType`: The type of the property. Supported types include `FLOAT`, `VEC2`, `VEC3`, `VEC4`, `COLOR`, `INTEGER`, `IVEC2`, `IVEC3`, `IVEC4`, and `TEXTURE2D`.

Example:

```glsl
Properties
{
    _MainTex("Albedo Map", TEXTURE2D)
    _Color("Main Color", COLOR)
    _Glossiness("Glossiness", FLOAT)
}
```

### Shader Passes

Shader passes define the rendering configuration and shader code for a specific rendering pass. Each pass has its own vertex and fragment shader code.

To declare a pass, use the following syntax:

```glsl
Pass PassIndex
{
    // Rasterizer state configuration

    Vertex
    {
        // Vertex shader code
    }

    Fragment
    {
        // Fragment shader code
    }
}
```

* `PassIndex`: The index of the pass, starting from 0.
* `RasterizerState`: The configuration for the rasterizer state, such as blending, depth testing, and culling.
* `Vertex`: The vertex shader code block.
* `Fragment`: The fragment shader code block.

Example:

```glsl
Pass 0
{
    DepthTest On
    DepthWrite True
    DepthMode Lequal
    Blend On
    BlendSrc SrcAlpha
    BlendDst OneMinusSrcAlpha
    BlendMode Add
    Cull On
    CullFace Back
    Winding CW

    Vertex
    {
        // Vertex shader code
    }

    Fragment
    {
        // Fragment shader code
    }
}
```

### Rasterizer State

Boolean States like DepthTest, DepthWrite, Blend, and Cull allow any of the following: \
`On, Off, True, False, 1, 0, Yes, No`

The others accept the following:

* `DepthMode`: &#x20;
  * Never, Less, Equal, Lequal, Greater, Notequal, Gequal, Always
* `BlendSrc & BlendDst`:&#x20;
  * Zero, One, SrcColor, OneMinusSrcColor, DstColor, OneMinusDstColor, SrcAlpha, OneMinusSrcAlpha, DstAlpha, OneMinusDstAlpha, ConstantColor, OneMinusConstantColor, ConstantAlpha, OneMinusConstantAlpha, SrcAlphaSaturate, Src1Color, OneMinusSrc1Color, Src1Alpha, OneMinusSrc1Alpha
* `BlendMode`:&#x20;
  * Add, Subtract, ReverseSubtract, Min, Max
* `CullFace`:&#x20;
  * Front, Back, FrontAndBack
* `Winding`:&#x20;
  * CW, CCW

### Shaders

The shaders defined inside the [#vertex-shader](writing-shaders.md#vertex-shader "mention") & [#fragment-shader](writing-shaders.md#fragment-shader "mention") blocks both use raw GLSL. \
\#defines are automatically appended to the start of both, alongside `#version 410`

### Vertex Shader

The vertex shader code is written inside the `Vertex` block of a pass. It is responsible for transforming vertex positions, calculating vertex attributes, and passing data to the fragment shader.&#x20;

The vertex shader code follows the standard GLSL syntax. You can define input attributes, uniform variables, and output variables to communicate with the fragment shader.

Example:

```glsl
Vertex
{
    layout (location = 0) in vec3 vertexPosition;
    layout (location = 1) in vec2 vertexTexCoord;

    out vec2 TexCoords;

    uniform mat4 mvp;

    void main()
    {
        TexCoords = vertexTexCoord;
        gl_Position = mvp * vec4(vertexPosition, 1.0);
    }
}
```

Certainly! I'll add information about the vertex attributes and the `#include "VertexAttributes"` directive.

### Vertex Attributes

Vertex attributes are used to pass per-vertex data from the application to the vertex shader. However, not all meshes provide all types of vertex data. To handle this, you can use preprocessor directives to conditionally include or exclude vertex attribute declarations based on their availability.

The following vertex attributes are commonly used:

* `vertexPosition`: The position of the vertex in model space.
* `vertexTexCoord0`: The first set of texture coordinates for the vertex.
* `vertexTexCoord1`: The second set of texture coordinates for the vertex.
* `vertexNormal`: The normal vector of the vertex.
* `vertexColor`: The color of the vertex.
* `vertexTangent`: The tangent vector of the vertex.
* `vertexBoneIndices`: The bone indices for skeletal animation.
* `vertexBoneWeights`: The bone weights for skeletal animation.

To conditionally include vertex attributes based on their availability, you can use the following preprocessor directives:

* `HAS_UV`: Defined if the mesh provides texture coordinates (`vertexTexCoord0`).
* `HAS_UV2`: Defined if the mesh provides a second set of texture coordinates (`vertexTexCoord1`).
* `HAS_NORMALS`: Defined if the mesh provides normal vectors (`vertexNormal`).
* `HAS_COLORS`: Defined if the mesh provides vertex colors (`vertexColor`).
* `HAS_TANGENTS`: Defined if the mesh provides tangent vectors (`vertexTangent`).
* `SKINNED`: Defined if the mesh is skinned and provides bone indices and weights (`vertexBoneIndices` and `vertexBoneWeights`).

Example:

```glsl
Vertex
{
    layout (location = 0) in vec3 vertexPosition;

    #ifdef HAS_UV
        layout (location = 1) in vec2 vertexTexCoord0;
    #else
        vec2 vertexTexCoord0 = vec2(0.0, 0.0);
    #endif

    #ifdef HAS_NORMALS
        layout (location = 2) in vec3 vertexNormal;
    #else
        vec3 vertexNormal = vec3(0.0, 1.0, 0.0);
    #endif

    // ...
}
```

In this example, the `vertexTexCoord0` attribute is conditionally included based on the presence of the `HAS_UV` directive. If `HAS_UV` is not defined, a default value of `vec2(0.0, 0.0)` is used instead. Similarly, the `vertexNormal` attribute is conditionally included based on the `HAS_NORMALS` directive, with a default value of `vec3(0.0, 1.0, 0.0)` if not defined.

#### Including Vertex Attributes

To simplify the process of including vertex attributes with their corresponding preprocessor directives and default values, you can use the `#include "VertexAttributes"` directive in your vertex shader code.

Example:

```glsl
Vertex
{
    #include "VertexAttributes"

    // Rest of the vertex shader code
    // ...
}
```

When you include `VertexAttributes`, it automatically includes all the commonly used vertex attributes with their respective preprocessor directives and default values. This saves you from manually declaring each attribute and handling their availability.

The `VertexAttributes` file contains the following code:

```glsl
#ifndef SHADER_VERTEXATTRIBUTES
#define SHADER_VERTEXATTRIBUTES
  layout (location = 0) in vec3 vertexPosition;

#ifdef HAS_UV
  layout (location = 1) in vec2 vertexTexCoord0;
#else
  vec2 vertexTexCoord0 = vec2(0.0, 0.0);
#endif

#ifdef HAS_UV2
  layout (location = 2) in vec2 vertexTexCoord1;
#else
  vec2 vertexTexCoord1 = vec2(0.0, 0.0);
#endif

#ifdef HAS_NORMALS
  layout (location = 3) in vec3 vertexNormal;
#else
  vec3 vertexNormal = vec3(0.0, 1.0, 0.0);
#endif

#ifdef HAS_COLORS
  layout (location = 4) in vec4 vertexColor;
#else
  vec4 vertexColor = vec4(1.0, 1.0, 1.0, 1.0);
#endif

#ifdef HAS_TANGENTS
  layout (location = 5) in vec3 vertexTangent;
#else
  vec3 vertexTangent = vec3(1.0, 0.0, 0.0);
#endif

#ifdef SKINNED
  #ifdef HAS_BONEINDICES
    layout (location = 6) in vec4 vertexBoneIndices;
  #else
    vec4 vertexBoneIndices = vec4(0, 0, 0, 0);
  #endif

  #ifdef HAS_BONEWEIGHTS
    layout (location = 7) in vec4 vertexBoneWeights;
  #else
    vec4 vertexBoneWeights = vec4(0.0, 0.0, 0.0, 0.0);
  #endif
		
  const int MAX_BONE_INFLUENCE = 4;
  const int MAX_BONES = 100;
  uniform mat4 bindPoses[MAX_BONES];
  uniform mat4 boneTransforms[MAX_BONES];
#endif
#endif
```

By including `VertexAttributes`, you have access to all these vertex attributes in your vertex shader code. You can use them directly without worrying about their availability or default values.

Remember to include the `VertexAttributes` file in your shader code before using any of the vertex attributes.

### Fragment Shader

The fragment shader code is written inside the `Fragment` block of a pass. It is responsible for calculating the final color of each pixel based on the interpolated vertex attributes and uniform variables.

The fragment shader code follows the standard GLSL syntax. You can define input variables from the vertex shader, uniform variables, and output variables for the final color.

Example:

```glsl
Fragment
{
    in vec2 TexCoords;

    uniform sampler2D _MainTex;
    uniform vec4 _Color;

    layout (location = 0) out vec4 FragColor;

    void main()
    {
        vec4 texColor = texture(_MainTex, TexCoords);
        FragColor = texColor * _Color;
    }
}
```

### Shadow Pass

The shadow pass is an optional section that defines the shader code for rendering shadow maps. It follows a similar structure to regular passes but is declared using the `ShadowPass` keyword.\
Only a single shadow pass per shader is supported.

Example:

```json
ShadowPass
{
    // Shadow Rasterizer state configuration

    Vertex
    {
        // Shadow vertex shader code
    }

    Fragment
    {
        // Shadow fragment shader code
    }
}
```

### Preprocessor Directives

The Prowl Shader format supports preprocessor directives to conditionally include or exclude code based on defined symbols. The following directives are commonly used:

* `#define`: Defines a preprocessor symbol.
* `#ifdef` / `#endif`: Conditionally includes code if a symbol is defined.
* `#ifndef` / `#endif`: Conditionally includes code if a symbol is not defined.
* `#if` / `#endif`: Conditionally includes code based on a constant expression.

Example:

```glsl
#define USE_NORMAL_MAP

#ifdef USE_NORMAL_MAP
    uniform sampler2D _NormalMap;
#endif

void main()
{
    // ...

    #ifdef USE_NORMAL_MAP
        vec3 normal = texture(_NormalMap, TexCoords).rgb;
        // Use the normal map
    #else
        vec3 normal = normalize(VertexNormal);
        // Use the vertex normal
    #endif

    // ...
}
```

### Built-in Defines

The Prowl Shader format automatically defines certain symbols that you can use in your shader code. These symbols provide information about the engine version and other built-in features.

#### Engine Version Define

* `PROWL_VERSION`: The version number of the Prowl Engine. You can use this to conditionally include code based on the engine version.

Example:

```glsl
#if PROWL_VERSION >= 02
    // Code specific to Prowl Engine version 0.2 or higher
#else
    // Code specific to older versions of Prowl Engine
#endif
```

#### Mesh Attribute Defines

The following defines are automatically set based on the availability of specific mesh attributes:

* `HAS_UV`: Defined if the mesh provides texture coordinates (`vertexTexCoord0`).
* `HAS_UV2`: Defined if the mesh provides a second set of texture coordinates (`vertexTexCoord1`).
* `HAS_NORMALS`: Defined if the mesh provides normal vectors (`vertexNormal`).
* `HAS_COLORS`: Defined if the mesh provides vertex colors (`vertexColor`).
* `HAS_TANGENTS`: Defined if the mesh provides tangent vectors (`vertexTangent`).
* `SKINNED`: Defined if the mesh is skinned and provides bone indices and weights (`vertexBoneIndices` and `vertexBoneWeights`).
* `HAS_BONEINDICES`: Defined if the mesh provides bone indices (`vertexBoneIndices`).
* `HAS_BONEWEIGHTS`: Defined if the mesh provides bone weights (`vertexBoneWeights`).

You can use these defines to conditionally include or exclude code based on the availability of specific mesh attributes.

### Includes

The Prowl Shader format supports including external GLSL files using the `#include` directive. This allows you to reuse common code across multiple shaders.

To include a file, use the following syntax:

```glsl
#include "filename"
```

The included file will be inserted at the location of the `#include` directive. The file path is relative to the location of the main shader file.

Example:

```glsl
#include "common"

void main()
{
    // ...
}
```

### Shader Example

Here is an example of a complete standard-like shader:

<pre class="language-glsl"><code class="lang-glsl"><strong>Shader "MyShaders/MyStandard"
</strong>
Properties
{
    _MainTex("Albedo Map", TEXTURE2D)
    _NormalTex("Normal Map", TEXTURE2D)
    _SurfaceTex("Surface Map x:AO y:Rough z:Metal", TEXTURE2D)
    _OcclusionTex("Occlusion Map", TEXTURE2D)
    _MainColor("Main Color", COLOR)
}

Pass 0
{
    Blend Off

    Vertex
    {
        #include "VertexAttributes"

        out vec3 FragPos;
        out vec3 Pos;
        out vec2 TexCoords0;
        out vec3 VertNormal;
        out vec4 PosProj;
        out vec4 PosProjOld;
		
        uniform mat4 matModel;
        uniform mat4 matView;
        uniform mat4 mvp;
        uniform mat4 mvpOld;

        void main()
        {
<strong>            vec3 boneVertexPosition = vertexPosition;
</strong>            vec3 boneVertexNormal = vertexNormal;
            vec3 boneVertexTangent = vertexTangent;
			
#ifdef SKINNED    
            vec4 totalPosition = vec4(0.0);
            vec3 totalNormal = vec3(0.0);
            vec3 totalTangent = vec3(0.0);

            for (int i = 0; i &#x3C; MAX_BONE_INFLUENCE; i++)
            {
                int index = int(vertexBoneIndices[i]) - 1;
                if (index &#x3C; 0)
                      continue;

                float weight = vertexBoneWeights[i];
                mat4 boneTransform = boneTransforms[index] * bindPoses[index];

                totalPosition += (boneTransform * vec4(vertexPosition, 1.0)) * weight;
                totalNormal += (mat3(boneTransform) * vertexNormal) * weight;
<strong>                totalTangent += (mat3(boneTransform) * vertexTangent) * weight;
</strong>            }

            boneVertexPosition = totalPosition.xyz;
            boneVertexNormal = normalize(totalNormal);
            boneVertexTangent = normalize(totalTangent);
#endif

            /*
            * Position and Normal are in view space
            */
            vec4 viewPos = matView * matModel * vec4(boneVertexPosition, 1.0);
            Pos = (matModel * vec4(boneVertexPosition, 1.0)).xyz;
            FragPos = viewPos.xyz; 
            TexCoords0 = vertexTexCoord0;

            mat3 normalMatrix = transpose(inverse(mat3(matModel)));
            VertNormal = normalize(normalMatrix * boneVertexNormal);
		
            PosProj = mvp * vec4(boneVertexPosition, 1.0);
            PosProjOld = mvpOld * vec4(boneVertexPosition, 1.0);
		
            gl_Position = PosProj;
        }
    }

    Fragment
    {
<strong>        layout (location = 0) out vec4 gAlbedoAO; // AlbedoR, AlbedoG, AlbedoB, Ambient Occlusion
</strong>        layout (location = 1) out vec4 gNormalMetallic; // NormalX, NormalY, NormalZ, Metallic
        layout (location = 2) out vec4 gPositionRoughness; // PositionX, PositionY, PositionZ, Roughness
        layout (location = 3) out vec3 gEmission; // EmissionR, EmissionG, EmissionB, 
        layout (location = 4) out vec2 gVelocity; // VelocityX, VelocityY
        layout (location = 5) out float gObjectID; // ObjectID

        in vec3 FragPos;
        in vec3 Pos;
        in vec2 TexCoords0;
        in vec3 VertNormal;
        in vec4 PosProj;
        in vec4 PosProjOld;

        uniform int ObjectID;
        uniform mat4 matView;
		
        uniform vec2 Jitter;
        uniform vec2 PreviousJitter;
	
        uniform sampler2D _MainTex; // diffuse
        uniform vec4 _MainColor; // color

        void main()
        {
	    vec3 surface = texture(_SurfaceTex, TexCoords0).rgb;
	
	    // Albedo and AO
	    gAlbedoAO = vec4(pow(alb.xyz, vec3(2.2)), alb.w);
	
	    // Position &#x26; Roughness
	    gPositionRoughness = vec4(FragPos, 0.5);

	    // Normal &#x26; Metallicness
	    gNormalMetallic = vec4(VertNormal, 0.5);

	    // Velocity
	    vec2 a = (PosProj.xy / PosProj.w) - Jitter;
	    vec2 b = (PosProjOld.xy / PosProjOld.w) - PreviousJitter;
	    gVelocity.xy = (b - a) * 0.5;

	    gObjectID = float(ObjectID);
        }
    }
}

			
ShadowPass 0
{
    CullFace Front

    Vertex
    {
        #include "VertexAttributes"
		
        out vec2 TexCoords;

        uniform mat4 mvp;
        void main()
        {
            vec3 boneVertexPosition = vertexPosition;
			
#ifdef SKINNED    
            vec4 totalPosition = vec4(0.0);

            for (int i = 0; i &#x3C; MAX_BONE_INFLUENCE; i++)
            {
                int index = int(vertexBoneIndices[i]) - 1;
                if (index &#x3C; 0)
                    continue;

                float weight = vertexBoneWeights[i];
                mat4 boneTransform = boneTransforms[index] * bindPoses[index];

                totalPosition += (boneTransform * vec4(vertexPosition, 1.0)) * weight;
            }

            boneVertexPosition = totalPosition.xyz;
#endif

            TexCoords = vertexTexCoord0;
			
            gl_Position = mvp * vec4(boneVertexPosition, 1.0);
        }
    }

    Fragment
    {
        layout (location = 0) out float depth;
		
        uniform sampler2D _MainTex;
        uniform vec4 _MainColor;

        in vec2 TexCoords;

        void main()
        {
            float alpha = texture(_MainTex, TexCoords).a; 
            if(alpha * _MainColor.a &#x3C; 0.5) discard;
        }
    }
}
</code></pre>

These examples demonstrate the usage of shader properties, passes, vertex and fragment shaders, and the shadow pass.

You can create your own custom shaders by following the format and utilizing the available features and syntax. Experiment with different properties, passes, and shader code to achieve the desired visual effects in your game.

Happy shader coding!
