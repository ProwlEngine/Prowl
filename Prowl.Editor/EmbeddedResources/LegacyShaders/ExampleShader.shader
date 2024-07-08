Shader "Example/NewShaderFormat"

Properties
{
    // Material properties

	_MainTex("Albedo Map", Texture2D)
	_MainColor("Main Color", Color)
    _Intensity("Intensity", Float)
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

        Comparison Greater LessEqual
        Pass IncrementWrap Replace

        ZFail Zero Zero
    }

    DepthWrite Off
    DepthTest Never
    Cull Off

    Inputs
    {
        VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
        }
        
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

            // Binding 1-2: Takes up 2 bindings if this texture has a sampler
            SampledTexture SomeTextureName

            // Binding 3: Takes up just 1 binding if this texture is stand-alone
            Texture SomeOtherTexture
            

            // Binding 4
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
        SOME_FEATURE [ ON OFF ]
        SOME_OTHER_FEATURE [ ON OFF ]
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