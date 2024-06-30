Shader "Example/NewShaderFormat"

Properties
{
	// Material property declarations go here
	_MainTex("Albedo Map", Texture2D)
	_NormalTex("Normal Map", Texture2D)
	_EmissionTex("Emissive Map", Texture2D)
	_SurfaceTex("Surface Map x:AO y:Rough z:Metal", Texture2D)
	_OcclusionTex("Occlusion Map", Texture2D)

	_EmissiveColor("Emissive Color", Color)
	_EmissionIntensity("Emissive Intensity", Float)
	_MainColor("Main Color", Color)
}

// Global state or options applied to every pass. If a pass doesn't specify a value, it will use the ones defined here
Global
{
    Tags { "SomeShaderID" = "IsSomeShaderType", "SomeOtherValue" = "SomeOtherType" }

    Blend
    {    
        Src Color InverseSourceAlpha
        Dest Alpha One

        Mode Alpha ReverseSubtract
        
        Mask None
    }

    // Stencil state
    Stencil
    {
        // Depth write
        DepthWrite On
        
        // Comparison kind
        DepthTest LessEqual

        Ref 25
        ReadMask 26
        WriteMask 27

        Comparison Front Greater

        Pass Front Keep
        Fail Back Zero
        ZFail Front Replace
    }

    // Rasterizer culling mode
    Cull Back

    // Global includes added to every program
    GlobalInclude 
    {
        // This value would be able to be used in every <Program> block
        vec4 aDefaultValue = vec4(0.5, 0.25, 0.75, 1.0);
    }
}



Pass "DefaultPass"
{
    Tags { "SomeValue" = "CustomPassType", "SomeOtherValue" = "SomeOtherType" }

    Blend Override

    Stencil
    {
        Ref 5
        ReadMask 10
        WriteMask 14

        Comparison Back Greater
        Comparison Front LessEqual

        Pass Front IncrementWrap
        Pass Back Replace

        ZFail Front Zero
    }

    DepthWrite Off
    DepthTest Never
    Cull Off

    Inputs
    {
        VertexInput <Position/UV0/UV1/Color/Normals/Tangents/BoneIndices/BoneWeights>


        // Input location 0
        VertexInput Position

        // Input location 1
        VertexInput UV0
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer
            {
                SomeName VECTOR4
                SomeName2 VECTOR4
                SomeName3 VECTOR4
                SomeName4 MATRIX
            }

            // Binding 1/2
            Texture SomeTextureName

            // Binding 3
            Buffer
            {
                SomeValue VECTOR4
            }
        }

        // Set 1
        Set
        {
            // Binding 0
            Buffer
            {
                SomeMatrix MATRIX
            }
        }
    }
    
    Features
    {
        SOME_FEATURE ON OFF
        SOME_OTHER_FEATURE ON OFF
    }

    // Program vertex stage example
    Program Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
		layout (location = 0) out vec4 someColor; 
		
		void main()
		{
			someColor = aDefaultValue;
            gl_Position = vertexPosition;
		}
    }

    // Program fragment stage example
	Program Fragment
    {
        layout (location = 0) in vec4 someColor; 
    	layout (location = 0) out vec4 outColor; 
		
		void main()
		{
			outColor = someColor;
		}
	}
}

Pass "AnotherPass"
{
    Tags { "SomeValue" = "CustomPassType" }

    Blend
    {
        Src Alpha InverseSourceColor
        Src Color DestinationColor

        Dest Alpha One
        Dest Color InverseBlendFactor

        Mode Alpha Maximum
        Mode Color ReverseSubtract
        
        Mask RGB
    }

    DepthWrite On
    DepthTest LessEqual
    Cull Back

    // Program vertex stage example
    Program Vertex
    {
        layout (location = 0) in vec3 vertexPosition;

		layout (location = 0) out vec4 someColor; 
		
		void main()
		{
            #if MY_KEYWORD == 0

            #elif

			someColor = aDefaultValue;
            gl_Position = vertexPosition;
		}
    }

    // Program fragment stage example
	Program Fragment
    {
        layout (location = 0) in vec4 someColor; 

    	layout (location = 0) out vec4 outColor; 
		
		void main()
		{
			outColor = someColor;
		}
	}
}

// If a pass doesn't compile, the whole shader is invalidated. Use a fallback replacement for the entire shader in that case.
// While per-pass fallbacks would be nice, there's no guarantee that the pass will always have a name or the correct index 
Fallback "Fallback/TestShader"