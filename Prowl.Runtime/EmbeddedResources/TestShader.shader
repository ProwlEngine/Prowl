Shader "Testing/TestShader"

Properties
{
	// Material property declarations go here
	_MainTex("Albedo Map", TEXTURE2D)
	_NormalTex("Normal Map", TEXTURE2D)
	_EmissionTex("Emissive Map", TEXTURE2D)
	_SurfaceTex("Surface Map x:AO y:Rough z:Metal", TEXTURE2D)
	_OcclusionTex("Occlusion Map", TEXTURE2D)

	_EmissiveColor("Emissive Color", COLOR)
	_EmissionIntensity("Emissive Intensity", FLOAT)
	_MainColor("Main Color", COLOR)
}

// Global state or options applied to every pass. If a pass doesn't specify a value, it will use the one defined here
Global
{
    Tags { "SomeShaderID" = "IsSomeShaderType", "SomeOtherValue" = "SomeOtherType" }

    // Blend state- can be predefined state...
    Blend <Off/SingleAdditive/SingleAlpha/SingleOverride>
    
    // ...or custom values
    Blend
    {    
        Src <Color/Alpha> <One/Zero/SrcColor/SrcAlpha/OneMinusSrcAlpha/OneMinusSrcColor/DstColor/DstAlpha/OneMinusDstAlpha/OneMinusDstColor>
        Dest <Color/Alpha> <One/Zero/SrcColor/SrcAlpha/OneMinusSrcAlpha/OneMinusSrcColor/DstColor/DstAlpha/OneMinusDstAlpha/OneMinusDstColor>

        Mode <Color/Alpha> <Add/Max/Min/SubtractDest/SubtractSrc>
        
        Mask <R/G/B/A/None>
    }

    // Stencil state
    Stencil
    {
        Ref <0-255>
        ReadMask <0-255>
        WriteMask <0-255>

        Comparison <Back/Front> <Always/Equal/Greater/GreaterEqual/Less/LessEqual/Never/NotEqual>

        Pass <Back/Front> <Keep/Zero/Replace/Invert/IncrementClamp/DecrementClamp/IncrementWrap/DecrementWrap>
        Fail <Back/Front> <Keep/Zero/Replace/Invert/IncrementClamp/DecrementClamp/IncrementWrap/DecrementWrap>
        ZFail <Back/Front> <Keep/Zero/Replace/Invert/IncrementClamp/DecrementClamp/IncrementWrap/DecrementWrap>
    }

    // Depth write
    DepthWrite <On/Off>
    
    // Comparison kind
    DepthTest <Always/Equal/Greater/GreaterEqual/Less/LessEqual/Never/NotEqual/Off>

    // Rasterizer culling mode
    Cull <Back/Front/Off>

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

    Blend SingleOverride

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
        // Input location 0
        VertexInput FLOAT

        // Input location 1
        VertexInput VECTOR4
        
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

    // Program block defines actual shader code
    Program <Vertex/Fragment/Geometry/TessControl/TessEvaluation/<etc...>
    {
        
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
        Src Alpha OneMinusSrcAlpha
        Src Color DstColor

        Dest Alpha One
        Dest Color OneMinusBlendFactor

        Mode Alpha Max
        Mode Color SubtractDest
        
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