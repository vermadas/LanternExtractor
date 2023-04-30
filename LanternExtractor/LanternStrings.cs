using System.Collections.Generic;

namespace LanternExtractor
{
    /// <summary>
    /// A collection of Lantern related strings
    /// </summary>
    public static class LanternStrings
    {
        public const string ExportHeaderTitle = "# Lantern Extractor 0.2 - ";
        public const string ExportHeaderFormat = "# Format: ";
        
        public const string ObjMaterialHeader = "mtllib ";
        public const string ObjUseMtlPrefix = "usemtl ";
        public const string ObjNewMaterialPrefix = "newmtl";
        public const string ObjFormatExtension = ".obj";
        public const string FormatMtlExtension = ".mtl";

        public const string WldFormatExtension = ".wld";
        public const string S3dFormatExtension = ".s3d";
        public const string PfsFormatExtension = ".s3d";
        public const string SoundFormatExtension = ".eff";

        public static readonly HashSet<string> PlayerCharacterActorNames = new HashSet<string>()
        {
            "baf", "bam", "daf", "dam", "dwf", "dwm", "elf", "elm", "erf", "erm",
            "gnf", "gnm", "haf", "ham", "hif", "him", "hof", "hom", "huf", "hum",
            "ikf", "ikm", "ogf", "ogm", "trf", "trm"
        };
    }
}