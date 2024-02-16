using System;
using System.IO;
using System.Linq;
using System.Runtime;
using LanternExtractor.Infrastructure;
using LanternExtractor.Infrastructure.Logger;

namespace LanternExtractor
{
    /// <summary>
    /// Simple class that parses settings for the extractor
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// The logger reference for debug output
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The OS path to the settings file
        /// </summary>
        private readonly string _settingsFilePath;

        /// <summary>
        /// The OS path to the EverQuest directory
        /// </summary>
        public string EverQuestDirectory { get; private set; }

        /// <summary>
        /// Extract data from the WLD file
        /// If false, we just extract the S3D contents
        /// </summary>
        public bool RawS3dExtract { get; private set; }

        /// <summary>
        /// Adds group separation in the zone mesh export
        /// </summary>
        public bool ExportZoneMeshGroups { get; private set; }

        /// <summary>
        /// Adds zone regions to the gltf scene
        /// </summary>
        public bool ExportZoneRegions { get; private set; }

        /// <summary>
        /// Exports hidden geometry like zone boundaries
        /// </summary>
        public bool ExportHiddenGeometry { get; private set; }

        /// <summary>
        /// Sets the desired model export format
        /// </summary>
        public ModelExportFormat ModelExportFormat { get; private set; }
        
        public bool ExportCharactersToSingleFolder { get; private set; }
        
        public bool ExportEquipmentToSingleFolder { get; private set; }
        
        public bool ExportSoundsToSingleFolder { get; private set; }

        /// <summary>
        /// Exports all OBJ frames for all animations
        /// </summary>
        public bool ExportAllAnimationFrames { get; private set; }

        /// <summary>
        /// Exports zone with object instances placed within
        /// </summary>
        public bool ExportZoneWithObjects { get; private set; }

        /// <summary>
        /// Exports zone with door instances placed within
        /// </summary>
        public bool ExportZoneWithDoors { get; private set; }

        /// <summary>
        /// Exports zone with all character variants that can spawn in it
        /// </summary>
        public bool ExportZoneCharacterVariations { get; private set; }

        /// <summary>
        /// Exports zone glTF with light instances with intensity set to the
        /// provided value. If set at 0, lights are not exported
        /// </summary>
        public float LightIntensityMultiplier { get; private set; }
        public bool ExportZoneWithLights => LightIntensityMultiplier > 0;

        /// <summary>
        /// Exports zone with objects with skeletal animations included
        /// </summary>
        public bool ExportZoneObjectsWithSkeletalAnimations { get; private set; }

        /// <summary>
        /// Sets the export scale of the zone when exported
        /// </summary>
        public float ExportZoneScale { get; private set; }

        /// <summary>
        /// Export vertex colors with glTF model. Default behavior of glTF renderers
        /// is to mix the vertex color with the base color, which will not look right.
        /// Only turn this on if you intend to do some post-processing that
        /// requires vertex colors being present.
        /// </summary>
        public bool ExportGltfVertexColors { get; private set; }

        /// <summary>
        /// Exports glTF models in .GLB file format. GLB packages the .glTF json, the
        /// associated .bin, and all of the model's texture images into one file. This will
        /// take up more space since textures can't be shared, however, it will make models
        /// more portable.
        /// </summary>
        public bool ExportGltfInGlbFormat { get; private set; }

        /// <summary>
        /// Generate duplicate sets of vertices for triangles sharing vertices with another
        /// </summary>
        public bool SeparateTwoFacedTriangles { get; private set; }

        /// <summary>
        /// Additional files that should be copied when extracting with `all` or `clientdata`
        /// </summary>
        public string ClientDataToCopy { get; private set; }

        /// <summary>
        /// Animation types to include in the export
        /// </summary>
        public List<string> ExportedAnimationTypes { get; private set; }

        /// <summary>
        /// Path to the server database
        /// </summary>
        public string ServerDbPath { get; private set; }
        
        /// <summary>
        /// If enabled, XMI files will be copied to the 'Exports/Music' folder
        /// </summary>
        public bool CopyMusic { get; private set; }

        /// <summary>
        /// The verbosity of the logger
        /// </summary>
        public int LoggerVerbosity { get; private set; }

        /// <summary>
        /// Constructor which caches the settings file path and the logger
        /// Also sets defaults for the settings in the case the file isn't found
        /// </summary>
        /// <param name="settingsFilePath">The OS path to the settings file</param>
        /// <param name="logger">A reference to the logger for debug info</param>
        public Settings(string settingsFilePath, ILogger logger)
        {
            _settingsFilePath = settingsFilePath;
            _logger = logger;

            EverQuestDirectory = "C:/EverQuest/";
            RawS3dExtract = false;
            ExportZoneMeshGroups = false;
            ExportZoneRegions = false;
            ExportHiddenGeometry = false;
            LoggerVerbosity = 0;
        }


        public void Initialize()
        {
            string settingsText;

            try
            {
                settingsText = File.ReadAllText(_settingsFilePath);
            }
            catch (Exception e)
            {
                _logger.LogError("Error loading settings file: " + e.Message);
                return;
            }

            var parsedSettings = TextParser.ParseTextToDictionary(settingsText, '=', '#');

            if (parsedSettings == null)
            {
                return;
            }

            if (parsedSettings.TryGetValue("EverQuestDirectory", out var setting))
            {
                EverQuestDirectory = setting;

                // Ensure the path ends with a /
                EverQuestDirectory = Path.GetFullPath(EverQuestDirectory + "/");
            }

            if (parsedSettings.TryGetValue("RawS3DExtract", out var parsedSetting))
            {
                RawS3dExtract = Convert.ToBoolean(parsedSetting);
            }

            if (parsedSettings.TryGetValue("ExportZoneMeshGroups", out var setting1))
            {
                ExportZoneMeshGroups = Convert.ToBoolean(setting1);
            }

            if (parsedSettings.TryGetValue("ExportZoneRegions", out var setting))
            {
                ExportZoneMeshGroups = Convert.ToBoolean(setting);
            }

            if (parsedSettings.TryGetValue("ExportHiddenGeometry", out var parsedSetting1))
            {
                ExportHiddenGeometry = Convert.ToBoolean(parsedSetting1);
            }

            if (parsedSettings.TryGetValue("ExportZoneWithObjects", out var setting2))
            {
                ExportZoneWithObjects = Convert.ToBoolean(setting2);
            }

            if (parsedSettings.TryGetValue("ExportZoneWithDoors", out var setting))
            {
                ExportZoneWithDoors = Convert.ToBoolean(setting);
            }

            if (parsedSettings.TryGetValue("ExportZoneCharacterVariations", out var setting))
            {
                ExportZoneCharacterVariations = Convert.ToBoolean(setting);
            }

            if (parsedSettings.TryGetValue("LightIntensityMultiplier", out var setting))
            {
                LightIntensityMultiplier = Convert.ToSingle(setting);
            }

            if (parsedSettings.TryGetValue("ExportZoneObjectsWithSkeletalAnimations", out var setting))
            {
                ExportZoneObjectsWithSkeletalAnimations = Convert.ToBoolean(setting);
            }

            if (parsedSettings.TryGetValue("ExportZoneScale", out var setting))
            {
                ExportZoneScale = Convert.ToSingle(setting);
            }
            if (parsedSettings.TryGetValue("ModelExportFormat", out var parsedSetting2))
            {
                var exportFormatSetting = (ModelExportFormat)Convert.ToInt32(parsedSetting2);
                ModelExportFormat = exportFormatSetting;
            }

            if (parsedSettings.TryGetValue("ExportCharacterToSingleFolder", out var setting3))
            {
                ExportCharactersToSingleFolder = Convert.ToBoolean(setting3);
            }

            if (parsedSettings.TryGetValue("ExportEquipmentToSingleFolder", out var parsedSetting3))
            {
                ExportEquipmentToSingleFolder = Convert.ToBoolean(parsedSetting3);
            }
            
            if (parsedSettings.TryGetValue("ExportSoundsToSingleFolder", out var setting4))
            {
                ExportSoundsToSingleFolder = Convert.ToBoolean(setting4);
            }

            if (parsedSettings.TryGetValue("ExportAllAnimationFrames", out var parsedSetting4))
            {
                ExportAllAnimationFrames = Convert.ToBoolean(parsedSetting4);
            }

            if (parsedSettings.TryGetValue("ExportGltfVertexColors", out var setting5))
            {
                ExportGltfVertexColors = Convert.ToBoolean(setting5);
            }

            if (parsedSettings.TryGetValue("ExportGltfInGlbFormat", out var parsedSetting5))
            {
                ExportGltfInGlbFormat = Convert.ToBoolean(parsedSettings["ExportGltfInGlbFormat"]);
            }

            if (parsedSettings.TryGetValue("SeparateTwoFacedTriangles", out var setting))
            {
                SeparateTwoFacedTriangles = Convert.ToBoolean(setting);
            }

            if (parsedSettings.TryGetValue("ExportedAnimationTypes", out var setting))
            {
                ExportedAnimationTypes = setting.Trim()
                    .Split(',').Select(a => a.Trim().ToLower())
                    .Where(a => a.Length == 1).ToList();
            }
            
            if (parsedSettings.TryGetValue("ClientDataToCopy", out var setting6))
            {
                ClientDataToCopy = setting6;
            }
            
            if (parsedSettings.TryGetValue("ClientDataToCopy", out var parsedSetting6))
            {
                ClientDataToCopy = parsedSetting6;
            }
            
            if (parsedSettings.TryGetValue("CopyMusic", out var setting7))
            {
                CopyMusic = Convert.ToBoolean(setting7);
            }

            if (parsedSettings.TryGetValue("LoggerVerbosity", out var parsedSetting7))
            {
                LoggerVerbosity = Convert.ToInt32(parsedSetting7);
            }
        }

        public bool UsingCombinedGlobalChr()
        {
            return ModelExportFormat != ModelExportFormat.Intermediate &&
                (ExportAllAnimationFrames || ExportZoneCharacterVariations)
                && !RawS3dExtract;
        }
    }
}