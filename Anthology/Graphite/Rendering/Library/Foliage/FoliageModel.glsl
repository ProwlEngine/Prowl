// How a deferred view lights the pixels of the Foliage model: as its forward pass lights them, from the values its
// GBuffer pass kept. The code of the model's Lighting, see ShadingModels.
#include "Graphite/Foliage/FoliageLight.glsl"

vec3 Model_Light(PBR_Closure c, Graphite_LightContribution l, vec4 custom) { return Foliage_DeferredLight(c, l, custom); }
