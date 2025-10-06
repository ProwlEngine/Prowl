Shader "Paper/UI"

Properties
{
}

Pass "UI"
{
    Tags { "RenderOrder" = "Opaque" }
    
    // Set up blending for UI elements
    Blend Alpha
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;
        
        uniform mat4 projection;
        
        out vec2 fragTexCoord;
        out vec4 fragColor;
        out vec2 fragPos;
        
        void main()
        {
            fragTexCoord = aTexCoord;
            //fragColor = vec4(1.0, 0.0, 0.0, 1.0);
            fragColor = aColor;
            fragPos = aPosition;
            gl_Position = projection * vec4(aPosition, 0.0, 1.0);
        }
    }

    Fragment
    {
        in vec2 fragTexCoord;
        in vec4 fragColor;
        in vec2 fragPos;
        
        out vec4 finalColor;
        
        uniform sampler2D texture0;
        uniform mat4 scissorMat;
        uniform vec2 scissorExt;
        
        uniform mat4 brushMat;
        uniform int brushType;       // 0=none, 1=linear, 2=radial, 3=box
        uniform vec4 brushColor1;    // Start color
        uniform vec4 brushColor2;    // End color
        uniform vec4 brushParams;    // x,y = start point, z,w = end point (or center+radius for radial)
        uniform vec2 brushParams2;   // x = Box radius, y = Box Feather
        
        float calculateBrushFactor() {
            // No brush
            if (brushType == 0) return 0.0;
            
            vec2 transformedPoint = (brushMat * vec4(fragPos, 0.0, 1.0)).xy;
        
            // Linear brush - projects position onto the line between start and end
            if (brushType == 1) {
                vec2 startPoint = brushParams.xy;
                vec2 endPoint = brushParams.zw;
                vec2 line = endPoint - startPoint;
                float lineLength = length(line);
                
                if (lineLength < 0.001) return 0.0;
                
                vec2 posToStart = transformedPoint - startPoint;
                float projection = dot(posToStart, line) / (lineLength * lineLength);
                return clamp(projection, 0.0, 1.0);
            }
            
            // Radial brush - based on distance from center
            if (brushType == 2) {
                vec2 center = brushParams.xy;
                float innerRadius = brushParams.z;
                float outerRadius = brushParams.w;
                
                if (outerRadius < 0.001) return 0.0;
                
                float distance = smoothstep(innerRadius, outerRadius, length(transformedPoint - center));
                return clamp(distance, 0.0, 1.0);
            }
            
            // Box brush - like radial but uses max distance in x or y direction
            if (brushType == 3) {
                vec2 center = brushParams.xy;
                vec2 halfSize = brushParams.zw;
                float radius = brushParams2.x;
                float feather = brushParams2.y;
                
                if (halfSize.x < 0.001 || halfSize.y < 0.001) return 0.0;
                
                // Calculate distance from center (normalized by half-size)
                vec2 q = abs(transformedPoint - center) - (halfSize - vec2(radius));
                
                // Distance field calculation for rounded rectangle
                float dist = min(max(q.x,q.y),0.0) + length(max(q,0.0)) - radius;
                
                return clamp((dist + feather * 0.5) / feather, 0.0, 1.0);
            }
            
            return 0.0;
        }
        
        // Determines whether a point is within the scissor region and returns the appropriate mask value
        float scissorMask(vec2 p) {
            // Early exit if scissoring is disabled (when scissorExt.x is negative or zero)
            if(scissorExt.x <= 0.0) return 1.0;
            
            // Transform point to scissor space
            vec2 transformedPoint = (scissorMat * vec4(p, 0.0, 1.0)).xy;
            
            // Calculate signed distance from scissor edges (negative inside, positive outside)
            vec2 distanceFromEdges = abs(transformedPoint) - scissorExt;
            
            // Apply offset for smooth edge transition (0.5 creates half-pixel anti-aliased edges)
            vec2 smoothEdges = vec2(0.5, 0.5) - distanceFromEdges;
            
            // Clamp each component and multiply to get final mask value
            // Result is 1.0 inside, 0.0 outside, with smooth transition at edges
            return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
        }
        
        void main()
        {
            //finalColor = vec4(1.0, 0.0, 0.0, 1.0);
            //return;
            vec2 pixelSize = fwidth(fragTexCoord);
            vec2 edgeDistance = min(fragTexCoord, 1.0 - fragTexCoord);
            float edgeAlpha = smoothstep(0.0, pixelSize.x, edgeDistance.x) * smoothstep(0.0, pixelSize.y, edgeDistance.y);
            edgeAlpha = clamp(edgeAlpha, 0.0, 1.0);
            
            float mask = scissorMask(fragPos);
            vec4 color = fragColor;
        
            // Apply brush if active
            if (brushType > 0) {
                float factor = calculateBrushFactor();
                color = mix(brushColor1, brushColor2, factor);
            }
            
            vec4 textureColor = texture(texture0, fragTexCoord);
            color *= textureColor;
            color *= edgeAlpha * mask;
            finalColor = color;
        }
    }

    ENDGLSL
}