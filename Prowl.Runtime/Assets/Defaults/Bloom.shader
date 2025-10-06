Shader "Default/Bloom"

Pass "Threshold"
{
    Tags { "RenderOrder" = "Opaque" }

    ZTest Off
    ZWrite Off
    Cull Off
    
    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        
        out vec2 TexCoords;
        
        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "Fragment"
        
        uniform sampler2D _MainTex;
        uniform float _Threshold;
        
        layout(location = 0) out vec4 FragColor;
        
        in vec2 TexCoords;
        
        void main()
        {
            vec3 color = texture(_MainTex, TexCoords).rgb;
            
            // Calculate luminance
            float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
            
            // Apply threshold
            float contribution = max(0.0, luminance - _Threshold);
            contribution /= max(luminance, 0.00001); // Avoid division by zero
            
            // Output is zero below threshold, and preserves color above threshold
            FragColor = vec4(color * contribution, 1.0);
        }
    }
    
    ENDGLSL
}

Pass "KawaseBlur"
{
    ZTest Off
    ZWrite Off
    Cull Off
    
    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        
        out vec2 TexCoords;
        
        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "Fragment"
        
        uniform sampler2D _MainTex;
        uniform float _Offset;
        uniform vec2 _TextureSize;
        
        layout(location = 0) out vec4 FragColor;
        
        in vec2 TexCoords;
        
        void main()
        {
            vec2 texelSize = 1.0 / vec2(textureSize(_MainTex, 0));
            
            // Calculate Kawase blur offset
            float offset = _Offset;
            
            // Kawase blur (5-tap)
            vec4 color = texture(_MainTex, TexCoords);
            color += texture(_MainTex, TexCoords + vec2(offset, offset) * texelSize);
            color += texture(_MainTex, TexCoords + vec2(-offset, offset) * texelSize);
            color += texture(_MainTex, TexCoords + vec2(offset, -offset) * texelSize);
            color += texture(_MainTex, TexCoords + vec2(-offset, -offset) * texelSize);
            
            // Average
            FragColor = color / 5.0;
        }
    }
    
    ENDGLSL
}

Pass "Composite"
{
    ZTest Off
    ZWrite Off
    Cull Off
    
    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        
        out vec2 TexCoords;
        
        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "Fragment"
        
        uniform sampler2D _MainTex;
        uniform sampler2D _BloomTex;
        uniform float _Intensity;
        
        layout(location = 0) out vec4 FragColor;
        
        in vec2 TexCoords;
        
        void main()
        {
            vec3 originalColor = texture(_MainTex, TexCoords).rgb;
            vec3 bloomColor = texture(_BloomTex, TexCoords).rgb;
            
            // Add bloom to original
            vec3 finalColor = originalColor + bloomColor * _Intensity;
            
            FragColor = vec4(finalColor, 1.0);
        }
    }
    
    ENDGLSL
}