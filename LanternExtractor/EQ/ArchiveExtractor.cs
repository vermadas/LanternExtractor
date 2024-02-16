using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanternExtractor.EQ.Archive;
using LanternExtractor.EQ.Sound;
using LanternExtractor.EQ.Wld;
using LanternExtractor.EQ.Wld.Exporters;
using LanternExtractor.EQ.Wld.Helpers;
using LanternExtractor.Infrastructure;
using LanternExtractor.Infrastructure.Logger;

namespace LanternExtractor.EQ
{
    public static class ArchiveExtractor
    {
        public static void Extract(string path, string rootFolder, ILogger logger, Settings settings)
        {
            string archiveName = Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrEmpty(archiveName))
            {
                return;
            }

            // If ExportAllAnimationFrames is set and exporting obj or gltf then
            // global_chr should already be initialized and we can skip that step
			if (archiveName == "global_chr" && settings.UsingCombinedGlobalChr())
            {
                var globalChrWld = GlobalReference.CharacterWld;
                if (globalChrWld != null)
                {
                    var exportPath = rootFolder + (settings.ExportCharactersToSingleFolder &&
                                    settings.ModelExportFormat == ModelExportFormat.Intermediate
                        ? "characters/Textures/"
                        : ShortnameHelper.GetCorrectZoneShortname("global") + "/Characters/Textures/");
                        
                    InitializeWldAndWriteTextures(globalChrWld, rootFolder, exportPath, GlobalReference.CharacterWld.S3dArchiveReference, settings, logger);

                    return;
                }
            }

            string shortName = archiveName.Split('_')[0];
            var archive = ArchiveFactory.GetArchive(path, logger);

            if (!archive.Initialize())
            {
                logger.LogError("LanternExtractor: Failed to initialize archive at path: " + path);
                return;
            }

            if (settings.RawS3dExtract)
            {
                archive.WriteAllFiles(Path.Combine(rootFolder, archiveName));
                return;
            }

            // For non WLD files, we can just extract their contents
            // Used for pure texture archives (e.g. bmpwad.s3d) and sound archives (e.g. snd1.pfs)
            // The difference between this and the raw export is that it will convert images to PNG
            if (!archive.IsWldArchive)
            {
                WriteS3dTextures(archive, rootFolder + shortName, logger);

                if (EqFileHelper.IsUsedSoundArchive(archiveName))
                {
                    WriteS3dSounds(archive,
                        Path.Combine(rootFolder, settings.ExportSoundsToSingleFolder ? "sounds" : shortName), logger);
                }

                return;
            }

            string wldFileName = archiveName + LanternStrings.WldFormatExtension;

            var wldFileInArchive = archive.GetFile(wldFileName);

            if (wldFileInArchive == null)
            {
                logger.LogError($"Unable to extract WLD file {wldFileName} from archive: {path}");
                return;
            }

            if (EqFileHelper.IsEquipmentArchive(archiveName))
            {
                ExtractArchiveEquipment(rootFolder, logger, settings, wldFileInArchive, shortName, archive);
            }
            else if (EqFileHelper.IsSkyArchive(archiveName))
            {
                ExtractArchiveSky(rootFolder, logger, settings, wldFileInArchive, shortName, archive);
            }
            else if (EqFileHelper.IsCharacterArchive(archiveName))
            {
                ExtractArchiveCharacters(path, rootFolder, logger, settings, archiveName, wldFileInArchive, shortName,
                    archive);
            }
            else if (EqFileHelper.IsObjectsArchive(archiveName))
            {
                ExtractArchiveObjects(path, rootFolder, logger, settings, wldFileInArchive, shortName, archive);
            }
            else
            {
                ExtractArchiveZone(path, rootFolder, logger, settings, shortName, wldFileInArchive, archive);
            }

            MissingTextureFixer.Fix(archiveName);

        }

        public static void InitializeSharedCharacterWld(string rootFolder, ILogger logger, Settings settings)
        {
            var globalChrFileIndices = new List<string>() { "2_chr", "3_chr", "4_chr", "17_amr", "18_amr", "19_amr", "20_amr", "21_amr", "22_amr", "23_amr", "_chr" };
            var injectibleGlobalChrWlds = new List<WldFile>();

            foreach (var fileIndex in globalChrFileIndices)
            {
                var globalChrName = $"global{fileIndex}";
                var globalChrS3d = Path.Combine(settings.EverQuestDirectory, $"{globalChrName}.s3d");

                var archive = ArchiveFactory.GetArchive(globalChrS3d, logger);

                if (!archive.Initialize())
                {
                    logger.LogError("LanternExtractor: Failed to initialize archive at path: " + globalChrS3d);
                    return;
                }

                var wldFileName = globalChrName + LanternStrings.WldFormatExtension;
                var wldFileInArchive = archive.GetFile(wldFileName);
                if (wldFileInArchive == null)
                {
                    logger.LogError($"Unable to extract WLD file {wldFileName} from archive: {globalChrS3d}");
                    return;
                }

                if (fileIndex != "_chr")
                {
                    var injectibleChrWld = new WldFileCharacters(wldFileInArchive, globalChrName, WldType.Characters, logger, settings);
                    injectibleChrWld.Initialize(rootFolder, false);
                    archive.FilenameChanges = injectibleChrWld.FilenameChanges;
                    injectibleChrWld.BaseS3DArchive = archive;
                    injectibleGlobalChrWlds.Add(injectibleChrWld);
                }
                else
                {
                    GlobalReference.InitCharacterWld(archive, wldFileInArchive, rootFolder, "global", WldType.Characters, logger, settings, injectibleGlobalChrWlds);
                }
            }
        }

		public static void InitWldsForPlayerCharacterGltfExport(PlayerCharacterModel pcEquipment, string rootFolder, string pcExportFolder, ILogger logger, Settings settings, out WldFileEquipment mainWldEqFile)
		{
			mainWldEqFile = null;
			if (!pcEquipment.Validate(out var errorMessage))
			{
				Console.WriteLine($"Cannot export player character - {errorMessage}");
				return;
			}

			var actorName = pcEquipment.RaceGender;
			var includeList = GlobalReference.CharacterWld.GetActorImageNames(actorName).ToList();
			var exportFolder = Path.Combine(rootFolder, pcExportFolder, "Textures");
			WriteWldTextures(GlobalReference.CharacterWld, exportFolder, logger, includeList, true);
			includeList.Clear();
			if (!string.IsNullOrEmpty(pcEquipment.Primary_ID) || !string.IsNullOrEmpty(pcEquipment.Secondary_ID) || pcEquipment.Head.Velious)
			{
				mainWldEqFile = InitCombinedEquipmentWld(logger, settings, pcExportFolder, rootFolder);
				if (pcEquipment.Primary_ID != null)
				{
					includeList.AddRange(mainWldEqFile.GetActorImageNames(pcEquipment.Primary_ID));
				}
				if (pcEquipment.Secondary_ID != null)
				{
					includeList.AddRange(mainWldEqFile.GetActorImageNames(pcEquipment.Secondary_ID));
				}
				if (pcEquipment.Head.Velious)
				{
					includeList.AddRange(mainWldEqFile.GetActorImageNames(PlayerCharacterModel.RaceGenderToVeliousHelmModel[pcEquipment.RaceGender]));
				}
				WriteWldTextures(mainWldEqFile, exportFolder, logger, includeList, true);
			}
		}

        public static WldFileEquipment InitWldsForZoneCharacterVariationExport(IEnumerable<string> globalCharacterActors, IEnumerable<string> heldEquipmentIds,
            string rootFolder, string zoneName, ILogger logger, Settings settings)
        {
            WldFileEquipment wldEqFile = null;
            var includeList = new List<string>();
			var exportFolder = Path.Combine(rootFolder, zoneName, "Characters", "Textures");
			if (globalCharacterActors.Any())
            {
				globalCharacterActors.ToList()
					.ForEach(a => includeList.AddRange(GlobalReference.CharacterWld.GetActorImageNames(a)));
				WriteWldTextures(GlobalReference.CharacterWld, exportFolder, logger, includeList.Distinct().ToList(), true);
                includeList.Clear();
			}

            if (heldEquipmentIds.Any())
            {
				wldEqFile = InitCombinedEquipmentWld(logger, settings, zoneName, rootFolder);
                heldEquipmentIds.ToList().ForEach(e => includeList.AddRange(wldEqFile.GetActorImageNames(e)));
				WriteWldTextures(wldEqFile, exportFolder, logger, includeList.Distinct().ToList(), true);
			}

            return wldEqFile;
        }

		private static WldFileEquipment InitCombinedEquipmentWld(ILogger logger, Settings settings, string zoneName, string rootFolder)
		{
			WldFileEquipment mainWldEqFile = null;
            var eqFiles = new string[] { "gequip8", "gequip6", "gequip5", "gequip4", "gequip3", "gequip2", "gequip" };
            var mainWldEqFileName = eqFiles.Last();
            var filesToInject = new List<WldFile>();
            foreach (var eqFile in eqFiles)
            {
                var eqFilePath = Path.Combine(settings.EverQuestDirectory, $"{eqFile}.s3d");

                if (!File.Exists(eqFilePath)) continue;

                var archive = ArchiveFactory.GetArchive(eqFilePath, logger);
                if (!archive.Initialize())
                {
                    logger.LogError("LanternExtractor: Failed to initialize PFS archive at path: " + eqFilePath);
                    return null;
                }
                var eqWldFileName = eqFile + LanternStrings.WldFormatExtension;
                var eqWldFileInArchive = archive.GetFile(eqWldFileName);
                WldFileEquipment wldEqFile;

                if (eqFile == mainWldEqFileName)
                {
                    wldEqFile = new WldFileEquipment(eqWldFileInArchive, zoneName, WldType.Equipment, logger, settings, filesToInject);
                    mainWldEqFile = wldEqFile;
                }
                else
                {
                    wldEqFile = new WldFileEquipment(eqWldFileInArchive, zoneName, WldType.Equipment, logger, settings);
                    filesToInject.Add(wldEqFile);
                }
                wldEqFile.Initialize(rootFolder, false);
                archive.FilenameChanges = wldEqFile.FilenameChanges;
                wldEqFile.BaseS3DArchive = archive;
            }

            return mainWldEqFile;
		}

        private static void ExtractArchiveZone(string path, string rootFolder, ILogger logger, Settings settings,
            string shortName, ArchiveFile wldFileInArchive, ArchiveBase archive)
        {
            // Some Kunark zones have a "_lit" which needs to be injected into the main zone file
            var archiveLit = ArchiveFactory.GetArchive(path.Replace(shortName, shortName + "_lit"), logger);
            WldFileZone wldFileLit = null;

            if (archiveLit.Initialize())
            {
                var litWldFileInArchive = archiveLit.GetFile(shortName + "_lit.wld");
                wldFileLit = new WldFileZone(litWldFileInArchive, shortName, WldType.Zone,
                    logger, settings);
                wldFileLit.Initialize(rootFolder, false);

                var litWldLightsFileInArchive = archiveLit.GetFile(shortName + "_lit.wld");

                if (litWldLightsFileInArchive != null)
                {
                    var lightsWldFile =
                        new WldFileLights(litWldLightsFileInArchive, shortName, WldType.Lights, logger, settings,
                            wldFileLit);
                    lightsWldFile.Initialize(rootFolder);
                }
            }

            var wldFile = new WldFileZone(wldFileInArchive, shortName, WldType.Zone, logger, settings, wldFileLit);

            // If we're trying to merge zone objects, inject here rather than pass down the chain to pull out later
            if (settings.ExportZoneWithObjects || settings.ExportZoneWithDoors || settings.ExportZoneWithLights)
            {
                wldFile.BasePath = path;
                wldFile.BaseS3DArchive = archive;
                wldFile.WldFileToInject = wldFileLit;
                wldFile.RootFolder = rootFolder;
                wldFile.ShortName = shortName;
            }

            InitializeWldAndWriteTextures(wldFile, rootFolder, rootFolder + shortName + "/Zone/Textures/",
                archive, settings, logger);

            var lightsFileInArchive = archive.GetFile("lights" + LanternStrings.WldFormatExtension);

            if (lightsFileInArchive != null)
            {
                var lightsWldFile =
                    new WldFileLights(lightsFileInArchive, shortName, WldType.Lights, logger, settings, wldFileLit);
                lightsWldFile.Initialize(rootFolder);
            }

            var zoneObjectsFileInArchive = archive.GetFile("objects" + LanternStrings.WldFormatExtension);

            if (zoneObjectsFileInArchive != null)
            {
                WldFileZoneObjects zoneObjectsWldFile = new WldFileZoneObjects(zoneObjectsFileInArchive, shortName,
                    WldType.ZoneObjects, logger, settings, wldFileLit);
                zoneObjectsWldFile.Initialize(rootFolder);
            }

            ExtractSoundData(shortName, rootFolder, logger, settings);
        }

        private static void ExtractArchiveObjects(string path, string rootFolder, ILogger logger, Settings settings,
            ArchiveFile wldFileInArchive, string shortName, ArchiveBase archive)
        {
            // Some zones have a "_2_obj" which needs to be injected into the main zone file
            var archiveObj2 = ArchiveFactory.GetArchive(path.Replace(shortName + "_obj", shortName + "_2_obj"), logger);
            WldFileZone wldFileObj2 = null;

            if (archiveObj2.Initialize())
            {
                var obj2WldFileInArchive = archiveObj2.GetFile(shortName + "_2_obj.wld");
                wldFileObj2 = new WldFileZone(obj2WldFileInArchive, shortName, WldType.Zone,
                    logger, settings);
                wldFileObj2.Initialize(rootFolder, false);
            }

            var wldFile = new WldFileZone(wldFileInArchive, shortName, WldType.Objects, logger, settings, wldFileObj2);
            InitializeWldAndWriteTextures(wldFile, rootFolder,
                rootFolder + ShortnameHelper.GetCorrectZoneShortname(shortName) + "/Objects/Textures/",
                archive, settings, logger);
        }

        private static void ExtractArchiveCharacters(string path, string rootFolder, ILogger logger, Settings settings,
            string archiveName, ArchiveFile wldFileInArchive, string shortName, ArchiveBase archive)
        {
            var wldFilesToInject = new List<WldFile>();

            // global3_chr contains just animations
            if (archiveName.StartsWith("global3_chr"))
            {
                var archive2 = ArchiveFactory.GetArchive(path.Replace("global3_chr", "global_chr"), logger);

                if (!archive2.Initialize())
                {
                    logger.LogError("Failed to initialize archive at path: " + path);
                    return;
                }

                var wldFileInArchive2 = archive2.GetFile("global_chr.wld");

                var wldFileToInject = new WldFileCharacters(wldFileInArchive2, "global_chr", WldType.Characters,
                    logger, settings);
                wldFileToInject.Initialize(rootFolder, false);
                wldFilesToInject.Add(wldFileToInject);
            }

            var wldFile = new WldFileCharacters(wldFileInArchive, shortName, WldType.Characters,
                logger, settings, wldFilesToInject );

            string exportPath = rootFolder + (settings.ExportCharactersToSingleFolder &&
                                              settings.ModelExportFormat == ModelExportFormat.Intermediate
                ? "characters/Textures/"
                : ShortnameHelper.GetCorrectZoneShortname(shortName) + "/Characters/Textures/");

            InitializeWldAndWriteTextures(wldFile, rootFolder, exportPath,
                archive, settings, logger);
        }

        private static void ExtractArchiveSky(string rootFolder, ILogger logger, Settings settings,
            ArchiveFile wldFileInArchive, string shortName, ArchiveBase archive)
        {
            var wldFile = new WldFileZone(wldFileInArchive, shortName, WldType.Sky, logger, settings);
            InitializeWldAndWriteTextures(wldFile, rootFolder, rootFolder + shortName + "/Textures/",
                archive, settings, logger);
        }

        private static void ExtractArchiveEquipment(string rootFolder, ILogger logger, Settings settings,
            ArchiveFile wldFileInArchive, string shortName, ArchiveBase archive)
        {
            var wldFile = new WldFileEquipment(wldFileInArchive, shortName, WldType.Equipment, logger, settings);
            var exportPath = rootFolder +
                             (settings.ExportEquipmentToSingleFolder &&
                              settings.ModelExportFormat == ModelExportFormat.Intermediate
                                 ? "equipment/Textures/"
                                 : shortName + "/Textures/");

            InitializeWldAndWriteTextures(wldFile, rootFolder, exportPath, archive, settings, logger);
        }

        private static void InitializeWldAndWriteTextures(WldFile wldFile, string rootFolder, string texturePath,
            ArchiveBase archive, Settings settings, ILogger logger)
        {
            wldFile.S3dArchiveReference = s3dArchive;
            if (settings.ModelExportFormat != ModelExportFormat.GlTF)
            {
                wldFile.Initialize(rootFolder);
                archive.FilenameChanges = wldFile.FilenameChanges;
                WriteWldTextures(archive, wldFile, texturePath, logger);
            }
            else // Exporting to GlTF requires that the texture images already be present
            {
                wldFile.Initialize(rootFolder, false);
                archive.FilenameChanges = wldFile.FilenameChanges;
                WriteWldTextures(archive, wldFile, texturePath, logger);
                wldFile.ExportData();
            }
        }

        /// <summary>
        /// Writes sounds from the PFS archive to disk
        /// </summary>
        /// <param name="s3dArchive"></param>
        /// <param name="filePath"></param>
        private static void WriteS3dSounds(ArchiveBase s3dArchive, string filePath, ILogger logger)
        {
            var allFiles = s3dArchive.GetAllFiles();

            foreach (var file in allFiles)
            {
                if (file.Name.EndsWith(".wav"))
                {
                    SoundWriter.WriteSoundAsWav(file.Bytes, filePath, file.Name, logger);
                }
            }
        }

        /// <summary>
        /// Writes textures from the archive to disk, converting them to PNG
        /// </summary>
        /// <param name="archive"></param>
        /// <param name="filePath"></param>
        private static void WriteS3dTextures(ArchiveBase archive, string filePath, ILogger logger)
        {
            var allFiles = archive.GetAllFiles();

            foreach (var file in allFiles)
            {
                if (file.Name.EndsWith(".bmp") || file.Name.EndsWith(".dds"))
                {
                    ImageWriter.WriteImageAsPng(file.Bytes, filePath, file.Name, false, logger);
                }
            }
        }

        /// <summary>
        /// Writes textures from the archive to disk, handling masked materials from the WLD
        /// </summary>
        /// <param name="archive"></param>
        /// <param name="wldFile"></param>
        /// <param name="zoneName"></param>
        public static void WriteWldTextures(ArchiveBase archive, WldFile wldFile, string zoneName, 
            ILogger logger, ICollection<string> includeList = null, bool includeInjectedWlds = false)
        {
            var allBitmaps = wldFile.GetAllBitmapNames(includeList, includeInjectedWlds);
            var maskedBitmaps = wldFile.GetMaskedBitmaps(includeList, includeInjectedWlds);

            var archives = new List<ArchiveBase>() { archive };
            if (includeInjectedWlds)
            {
                archives.AddRange(wldFile.GetInjectedWldsAssociatedS3dArchives());
            }
            foreach (var bitmap in allBitmaps)
            {
                PfsFile pfsFile = null;
                foreach (var archiveElement in archives)
                {
                    string filename = bitmap;
                    if (archiveElement.FilenameChanges != null &&
                        archiveElement.FilenameChanges.ContainsKey(Path.GetFileNameWithoutExtension(bitmap)))
                    {
                        filename = archiveElement.FilenameChanges[Path.GetFileNameWithoutExtension(bitmap)] + ".bmp";
                    }

                    pfsFile = archiveElement.GetFile(filename);

                    if (pfsFile != null)
                    {
                        break;
                    }
                }
                if (pfsFile == null)
                {
                    continue;
                }
                bool isMasked = maskedBitmaps != null && maskedBitmaps.Contains(bitmap);
                ImageWriter.WriteImageAsPng(pfsFile.Bytes, zoneName, bitmap, isMasked, logger);
            }
        }

        private static void ExtractSoundData(string shortName, string rootFolder, ILogger logger, Settings settings)
        {
            var envAudio = EnvAudio.Instance;
            var ealFilePath = Path.Combine(settings.EverQuestDirectory, "defaults.dat");
            if (!envAudio.Load(ealFilePath))
            {
                envAudio.Load(Path.ChangeExtension(ealFilePath, ".eal"));
            }

            var sounds = new EffSndBnk(settings.EverQuestDirectory + shortName + "_sndbnk" +
                                       LanternStrings.SoundFormatExtension);
            sounds.Initialize();

            var soundEntries =
                new EffSounds(
                    settings.EverQuestDirectory + shortName + "_sounds" + LanternStrings.SoundFormatExtension,
                    sounds, envAudio);
            soundEntries.Initialize(logger);
            soundEntries.ExportSoundData(shortName, rootFolder);
        }
    }
}