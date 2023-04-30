using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using LanternExtractor.Infrastructure;
using LanternExtractor.Infrastructure.Logger;


namespace LanternExtractor
{
    public enum ModelExportFormat
    {
        Intermediate = 0,
        Obj = 1,
        GlTF = 2
    }

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
        /// Exports hidden geometry like zone boundaries
        /// </summary>
        public bool ExportHiddenGeometry { get; private set; }

        /// <summary>
        /// Sets the desired model export format
        /// </summary>
        /// Allowing public set so it can be overidden after parsing command-line arguments
        public ModelExportFormat ModelExportFormat { get; set; }

        /// <summary>
        /// Sets the desired model export format
        /// </summary>
        public bool ExportCharactersToSingleFolder { get; private set; }

        /// <summary>
        /// Sets the desired model export format
        /// </summary>
        public bool ExportEquipmentToSingleFolder { get; private set; }

        /// <summary>
        /// Export all sound files to a single folder
        /// </summary>
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

            if (parsedSettings.ContainsKey("EverQuestDirectory"))
            {
                EverQuestDirectory = parsedSettings["EverQuestDirectory"];

                // Ensure the path ends with a /
                EverQuestDirectory = Path.GetFullPath(EverQuestDirectory + "/");
            }

            if (parsedSettings.ContainsKey("RawS3DExtract"))
            {
                RawS3dExtract = Convert.ToBoolean(parsedSettings["RawS3DExtract"]);
            }

            if (parsedSettings.ContainsKey("ExportZoneMeshGroups"))
            {
                ExportZoneMeshGroups = Convert.ToBoolean(parsedSettings["ExportZoneMeshGroups"]);
            }

            if (parsedSettings.ContainsKey("ExportHiddenGeometry"))
            {
                ExportHiddenGeometry = Convert.ToBoolean(parsedSettings["ExportHiddenGeometry"]);
            }

            if (parsedSettings.ContainsKey("ExportZoneWithObjects"))
            {
                ExportZoneWithObjects = Convert.ToBoolean(parsedSettings["ExportZoneWithObjects"]);
            }

            if (parsedSettings.ContainsKey("ExportZoneWithDoors"))
            {
                ExportZoneWithDoors = Convert.ToBoolean(parsedSettings["ExportZoneWithDoors"]);
            }

			if (parsedSettings.ContainsKey("ExportZoneCharacterVariations"))
			{
				ExportZoneCharacterVariations = Convert.ToBoolean(parsedSettings["ExportZoneCharacterVariations"]);
			}

			if (parsedSettings.ContainsKey("LightIntensityMultiplier"))
			{
				LightIntensityMultiplier = Convert.ToSingle(parsedSettings["LightIntensityMultiplier"]);
			}

			if (parsedSettings.ContainsKey("ExportZoneObjectsWithSkeletalAnimations"))
			{
				ExportZoneObjectsWithSkeletalAnimations = Convert.ToBoolean(parsedSettings["ExportZoneObjectsWithSkeletalAnimations"]);
			}

			if (parsedSettings.ContainsKey("ModelExportFormat"))
            {
                var exportFormatSetting = (ModelExportFormat)Convert.ToInt32(parsedSettings["ModelExportFormat"]);
                ModelExportFormat = exportFormatSetting;
            }

            if (parsedSettings.ContainsKey("ExportCharacterToSingleFolder"))
            {
                ExportCharactersToSingleFolder = Convert.ToBoolean(parsedSettings["ExportCharacterToSingleFolder"]);
            }

            if (parsedSettings.ContainsKey("ExportEquipmentToSingleFolder"))
            {
                ExportEquipmentToSingleFolder = Convert.ToBoolean(parsedSettings["ExportEquipmentToSingleFolder"]);
            }

            if (parsedSettings.ContainsKey("ExportSoundsToSingleFolder"))
            {
                ExportSoundsToSingleFolder = Convert.ToBoolean(parsedSettings["ExportSoundsToSingleFolder"]);
            }

            if (parsedSettings.ContainsKey("ExportAllAnimationFrames"))
            {
                ExportAllAnimationFrames = Convert.ToBoolean(parsedSettings["ExportAllAnimationFrames"]);
            }

            if (parsedSettings.ContainsKey("ExportGltfVertexColors"))
            {
                ExportGltfVertexColors = Convert.ToBoolean(parsedSettings["ExportGltfVertexColors"]);
            }

            if (parsedSettings.ContainsKey("ExportGltfInGlbFormat"))
            {
                ExportGltfInGlbFormat = Convert.ToBoolean(parsedSettings["ExportGltfInGlbFormat"]);
            }

			if (parsedSettings.ContainsKey("SeparateTwoFacedTriangles"))
			{
				SeparateTwoFacedTriangles = Convert.ToBoolean(parsedSettings["SeparateTwoFacedTriangles"]);
			}

			if (parsedSettings.ContainsKey("ExportedAnimationTypes"))
            {
                var animationIncludeString = parsedSettings["ExportedAnimationTypes"].Trim();
				ExportedAnimationTypes = animationIncludeString
                    .Split(',').Select(a => a.Trim().ToLower())
                    .Where(a => a.Length == 1).ToList();
            }
			
            if (parsedSettings.ContainsKey("ClientDataToCopy"))
            {
                ClientDataToCopy = parsedSettings["ClientDataToCopy"];
            }

            if (parsedSettings.ContainsKey("ServerDatabasePath"))
            {
                ServerDbPath = parsedSettings["ServerDatabasePath"];
            }

            if (parsedSettings.ContainsKey("LoggerVerbosity"))
            {
                LoggerVerbosity = Convert.ToInt32(parsedSettings["LoggerVerbosity"]);
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