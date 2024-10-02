namespace AssimpSharp
{
    public static class Version
    {
        public static string LegalString = @"
Open Asset Import Library (Assimp).
A free C/C# library to import various 3D file formats into applications

(c) 2008-2017, assimp team
License under the terms and conditions of the 3-clause BSD license
http://assimp.sourceforge.net
";

        public static int VersionMinor => 0;
        public static int VersionMajor => 4;
        public static uint VersionRevision => 0xee56ffa1;
        public static string Branch => "master";

        public static int CompileFlags => ASSIMP.BUILD.DEBUG ? 1 : 0;
    }
}