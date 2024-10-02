namespace AssimpSharp
{
    public static class Constants
    {
        // Scene-related constants
        public const int AI_SCENE_FLAGS_INCOMPLETE = 0x1;
        public const int AI_SCENE_FLAGS_VALIDATED = 0x2;
        public const int AI_SCENE_FLAGS_VALIDATION_WARNING = 0x4;
        public const int AI_SCENE_FLAGS_NON_VERBOSE_FORMAT = 0x8;
        public const int AI_SCENE_FLAGS_TERRAIN = 0x10;
        public const int AI_SCENE_FLAGS_ALLOW_SHARED = 0x20;

        // Mesh-related constants
        public const int AI_MAX_FACE_INDICES = 0x7fff;
        public const int AI_MAX_BONE_WEIGHTS = 0x7fffffff;
        public const int AI_MAX_VERTICES = 0x7fffffff;
        public const int AI_MAX_FACES = 0x7fffffff;
        public const int AI_MAX_NUMBER_OF_COLOR_SETS = 0x8;
        public const int AI_MAX_NUMBER_OF_TEXTURECOORDS = 0x8;

        // Material-related constants
        public const string AI_DEFAULT_MATERIAL_NAME = "DefaultMaterial";

        // Importer-related constants
        public const string AI_CONFIG_PP_CT_MAX_SMOOTHING_ANGLE = "PP_CT_MAX_SMOOTHING_ANGLE";
        public const string AI_CONFIG_PP_SBP_REMOVE = "PP_SBP_REMOVE";
        public const string AI_CONFIG_PP_FID_ANIM_ACCURACY = "PP_FID_ANIM_ACCURACY";
        public const string AI_CONFIG_PP_TUV_EVALUATE = "PP_TUV_EVALUATE";
        public const string AI_CONFIG_GLOB_MEASURE_TIME = "GLOB_MEASURE_TIME";
        public const string AI_CONFIG_IMPORT_NO_SKELETON_MESHES = "IMPORT_NO_SKELETON_MESHES";
        public const string AI_CONFIG_PP_ICL_PTCACHE_SIZE = "PP_ICL_PTCACHE_SIZE";
        public const string AI_CONFIG_PP_RRM_EXCLUDE_LIST = "PP_RRM_EXCLUDE_LIST";
        public const string AI_CONFIG_PP_FD_REMOVE = "PP_FD_REMOVE";
        public const string AI_CONFIG_PP_OG_EXCLUDE_LIST = "PP_OG_EXCLUDE_LIST";
        public const string AI_CONFIG_PP_SLM_TRIANGLE_LIMIT = "PP_SLM_TRIANGLE_LIMIT";
        public const string AI_CONFIG_PP_SLM_VERTEX_LIMIT = "PP_SLM_VERTEX_LIMIT";
        public const string AI_CONFIG_PP_LBW_MAX_WEIGHTS = "PP_LBW_MAX_WEIGHTS";
        public const string AI_CONFIG_PP_DB_THRESHOLD = "PP_DB_THRESHOLD";
        public const string AI_CONFIG_PP_DB_ALL_OR_NONE = "PP_DB_ALL_OR_NONE";
        public const string AI_CONFIG_PP_GSN_MAX_SMOOTHING_ANGLE = "PP_GSN_MAX_SMOOTHING_ANGLE";
        public const string AI_CONFIG_IMPORT_MD2_KEYFRAME = "IMPORT_MD2_KEYFRAME";
        public const string AI_CONFIG_PP_RVC_FLAGS = "PP_RVC_FLAGS";

        // Other constants
        public const int MAXLEN = 1024;
        public const float EPSILON = 1e-5f;

        public const float AI_MATH_PI = 3.141592653589793238462643383279f;
        public const float AI_MATH_TWO_PI = AI_MATH_PI * 2.0f;
        public const float AI_MATH_HALF_PI = AI_MATH_PI * 0.5f;

        // Default config properties
        public const int PP_ICL_PTCACHE_SIZE = 12;
        public const int PP_SBP_REMOVE = 0x0;
        public const double PP_FID_ANIM_ACCURACY = 0.0f;
        public const float PP_GSN_MAX_SMOOTHING_ANGLE = 80.0f;
        public const float PP_CT_MAX_SMOOTHING_ANGLE = 80.0f;
        public const int SLM_DEFAULT_MAX_VERTICES = 1000000;
        public const int SLM_DEFAULT_MAX_TRIANGLES = 1000000;
        public const int LMW_MAX_WEIGHTS = 0x4;
        public const float PP_DB_THRESHOLD = 1.0f;


        public const bool ASSIMP_LOAD_TEXTURES = true;
        public static bool NO_VALIDATEDS_PROCESS = false;
    }
}