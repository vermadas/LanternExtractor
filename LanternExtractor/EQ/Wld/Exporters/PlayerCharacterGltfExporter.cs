using LanternExtractor.EQ.Pfs;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.EQ.Wld.Helpers;
using LanternExtractor.Infrastructure.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DColor = System.Drawing.Color;
using static LanternExtractor.EQ.Wld.Exporters.GltfWriter;
using LanternExtractor.EQ.Wld.DataTypes;

namespace LanternExtractor.EQ.Wld.Exporters
{
    public static class PlayerCharacterGltfExporter
    {
        public static void InitWldsForPlayerCharacterGltfExport(PlayerCharacterModel pcEquipment, string rootFolder, ILogger logger, Settings settings, out WldFileEquipment mainWldEqFile)
        {
            mainWldEqFile = null;
            if (!pcEquipment.Validate(out var errorMessage))
            {
                Console.WriteLine($"Cannot export player character - {errorMessage}");
                return;
            }

            var actorName = pcEquipment.RaceGender;
            var includeList = GlobalReference.CharacterWld.GetActorImageNames(actorName).ToList();
            var exportFolder = Path.Combine(rootFolder, actorName, "Textures");
            ArchiveExtractor.WriteWldTextures(GlobalReference.CharacterWld, exportFolder, logger, includeList, true);
            WldFileEquipment injectedWldEqFile = null;
            includeList.Clear();
            if (!string.IsNullOrEmpty(pcEquipment.Primary_ID) || !string.IsNullOrEmpty(pcEquipment.Secondary_ID) || pcEquipment.Head.Velious)
            {
                var eqFiles = new string[] { "gequip2", "gequip" };
                for (int i = 0; i < 2; i++)
                {
                    var eqFile = eqFiles[i];
                    var eqFilePath = Path.Combine(settings.EverQuestDirectory, $"{eqFile}.s3d");
                    var eqS3dArchive = new PfsArchive(eqFilePath, logger);
                    if (!eqS3dArchive.Initialize())
                    {
                        logger.LogError("LanternExtractor: Failed to initialize PFS archive at path: " + eqFilePath);
                        return;
                    }
                    var eqWldFileName = eqFile + LanternStrings.WldFormatExtension;
                    var eqWldFileInArchive = eqS3dArchive.GetFile(eqWldFileName);
                    WldFileEquipment wldEqFile;
                    if (i == 0)
                    {
                        wldEqFile = new WldFileEquipment(eqWldFileInArchive, actorName, WldType.Equipment, logger, settings);
                        injectedWldEqFile = wldEqFile;
                    }
                    else
                    {
                        wldEqFile = new WldFileEquipment(eqWldFileInArchive, actorName, WldType.Equipment, logger, settings, new List<WldFile>() { injectedWldEqFile });
                        mainWldEqFile = wldEqFile;
                    }
                    wldEqFile.Initialize(rootFolder, false);
                    eqS3dArchive.FilenameChanges = wldEqFile.FilenameChanges;
                    wldEqFile.S3dArchiveReference = eqS3dArchive;
                }
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
                ArchiveExtractor.WriteWldTextures(mainWldEqFile, exportFolder, logger, includeList, true);
            }
        }

        public static void ExportPlayerCharacter(PlayerCharacterModel pcEquipment, WldFileCharacters wldChrFile, WldFileEquipment wldEqFile,
            ILogger logger, Settings settings)
        {
            var actorName = pcEquipment.RaceGender;
            var lookupName = $"{actorName}_ACTORDEF";

            var actor = wldChrFile.GetFragmentByNameIncludingInjectedWlds<Actor>(lookupName);

            var skeleton = actor?.SkeletonReference?.SkeletonHierarchy;

            if (skeleton == null) return;

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, pcEquipment);

            var allMeshes = skeleton.Meshes.Union(skeleton.SecondaryMeshes);
            var bodyMeshName = pcEquipment.IsChestRobe() ? $"{actorName}01" : actorName;
            bodyMeshName = $"{bodyMeshName}_DMSPRITEDEF";
            if (pcEquipment.Head.Material == 7)
            {
                pcEquipment.Head.Material = 2; // Kunark chain
            }
            else if (pcEquipment.RequiresMeshModificationsForVeliousHelm() && pcEquipment.RaceGender != "ERM")
            {
                pcEquipment.Head.Material = 3;
            }
            else if (pcEquipment.Head.Material == 4 || pcEquipment.Head.Material > 16 || pcEquipment.Head.Velious) // Monk or Velious
            {
                pcEquipment.Head.Material = 0;
            }
            var headMeshName = $"{actorName}HE{pcEquipment.Head.Material:00}";
            headMeshName = $"{headMeshName}_DMSPRITEDEF";
            var bodyMesh = allMeshes.Where(m => m.Name.Equals(bodyMeshName, StringComparison.InvariantCultureIgnoreCase)).Single();
            var headMesh = allMeshes.Where(m => m.Name.Equals(headMeshName, StringComparison.InvariantCultureIgnoreCase)).Single();
            WldFragment primaryMeshOrSkeleton = null;
            WldFragment secondaryMeshOrSkeleton = null;
            WldFragment veliousHelm = null;
            var veliousHelmModelId = pcEquipment.Head.Velious ? PlayerCharacterModel.RaceGenderToVeliousHelmModel[pcEquipment.RaceGender] : null;

            var wldFragmentsWithMaterialLists = new List<WldFragment>() { bodyMesh, headMesh };
            if (wldEqFile != null)
            {
                primaryMeshOrSkeleton = GetMeshOrSkeletonForPlayerCharacterHeldEquipment(pcEquipment.Primary_ID, wldEqFile, logger);
                wldFragmentsWithMaterialLists.Add(primaryMeshOrSkeleton);
                secondaryMeshOrSkeleton = GetMeshOrSkeletonForPlayerCharacterHeldEquipment(pcEquipment.Secondary_ID, wldEqFile, logger);
                wldFragmentsWithMaterialLists.Add(secondaryMeshOrSkeleton);
                veliousHelm = GetMeshOrSkeletonForPlayerCharacterHeldEquipment(veliousHelmModelId, wldEqFile, logger);
                wldFragmentsWithMaterialLists.Add(veliousHelm);
            }

            var materialLists = ActorGltfExporter.GatherMaterialLists(wldFragmentsWithMaterialLists.Where(m => m != null).ToList());
            var exportFolder = Path.Combine(wldChrFile.RootExportFolder, actorName);
            gltfWriter.GenerateGltfMaterials(materialLists, Path.Combine(exportFolder, "Textures"), true);

            MeshExportHelper.ShiftMeshVertices(bodyMesh, skeleton, true, "pos", 0);
            gltfWriter.AddFragmentData(bodyMesh, skeleton);
            MeshExportHelper.ShiftMeshVertices(headMesh, skeleton, true, "pos", 0);
            gltfWriter.AddFragmentData(headMesh, skeleton);

            var boneIndexOffset = skeleton.Skeleton.Count;

            AddPlayerCharacterHeldEquipmentToGltfWriter(primaryMeshOrSkeleton, pcEquipment.Primary_ID, skeleton,
                "r_point", gltfWriter, ref boneIndexOffset);
            var secondaryAttachBone = IsShield(pcEquipment.Secondary_ID) ? "shield_point" : "l_point";
            AddPlayerCharacterHeldEquipmentToGltfWriter(secondaryMeshOrSkeleton, pcEquipment.Secondary_ID, skeleton,
                secondaryAttachBone, gltfWriter, ref boneIndexOffset);
            AddPlayerCharacterHeldEquipmentToGltfWriter(veliousHelm, veliousHelmModelId, skeleton,
                "he", gltfWriter, ref boneIndexOffset);

            gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", true, true);

            if (settings.ExportAllAnimationFrames)
            {
                foreach (var animationKey in skeleton.Animations.Keys
                    .OrderBy(k => k, new AnimationKeyComparer()))
                {
                    gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, true, false);
                }
            }

            var exportFilePath = Path.Combine(exportFolder, $"{FragmentNameCleaner.CleanName(skeleton)}.gltf");
            gltfWriter.WriteAssetToFile(exportFilePath, false, skeleton.ModelBase, true);

            var jsonOutFilePath = Path.Combine(exportFolder, $"PcEquip_{DateTime.Now:yyyyMMddhhmmss}.json");
            var serializerOptions = new JsonSerializerOptions() { WriteIndented = true };
            serializerOptions.Converters.Add(new ColorJsonConverter());
            File.WriteAllText(jsonOutFilePath, JsonSerializer.Serialize(pcEquipment, serializerOptions));
        }

        public static void AddPcEquipmentClientDataFromDatabase(PlayerCharacterModel pcCharacterModel)
        {
            var dbConnector = GlobalReference.ServerDatabaseConnector;

            if (string.IsNullOrEmpty(pcCharacterModel.Primary_ID) && !string.IsNullOrEmpty(pcCharacterModel.Primary_Name))
            {
                var item = dbConnector.QueryItemFromDatabase(pcCharacterModel.Primary_Name, "Primary");
                pcCharacterModel.Primary_Name = item?.Name ?? "[Not Found in DB]";
                pcCharacterModel.Primary_ID = item?.IdFile;
            }
            if (string.IsNullOrEmpty(pcCharacterModel.Secondary_ID) && !string.IsNullOrEmpty(pcCharacterModel.Secondary_Name))
            {
                var item = dbConnector.QueryItemFromDatabase(pcCharacterModel.Secondary_Name, "Secondary");
                pcCharacterModel.Secondary_Name = item?.Name ?? "[Not Found in DB]";
                pcCharacterModel.Secondary_ID = item?.IdFile;
            }
            AddDatabaseInfoToEquipment(pcCharacterModel.Head, "Head");
            AddDatabaseInfoToEquipment(pcCharacterModel.Wrist, "Wrist");
            AddDatabaseInfoToEquipment(pcCharacterModel.Arms, "Arms");
            AddDatabaseInfoToEquipment(pcCharacterModel.Hands, "Hands");
            AddDatabaseInfoToEquipment(pcCharacterModel.Chest, "Chest");
            AddDatabaseInfoToEquipment(pcCharacterModel.Legs, "Legs");
            AddDatabaseInfoToEquipment(pcCharacterModel.Feet, "Feet");
        }

        private static void AddDatabaseInfoToEquipment(PlayerCharacterModel.Equipment equip, string slot)
        {
            if (!string.IsNullOrEmpty(equip.Name))
            {
                var item = GlobalReference.ServerDatabaseConnector.QueryItemFromDatabase(equip.Name, slot);
                if (item != null)
                {
                    equip.Name = item.Name;
                    equip.Material = item.Material;
                    equip.Color = item.Color > 0 ? System.Drawing.ColorTranslator.FromHtml($"#{item.Color:X6}") : (DColor?)null;
                    if (equip is PlayerCharacterModel.Helm)
                    {
                        ((PlayerCharacterModel.Helm)equip).Velious = item.IdFile == VeliousHelmIdFile;
                    }
                    return;
                }
                equip.Name = "[Not Found in DB]";
            }
        }


        private static WldFragment GetMeshOrSkeletonForPlayerCharacterHeldEquipment(string modelId, WldFileEquipment wldEqFile, ILogger logger)
        {
            if (string.IsNullOrEmpty(modelId)) return null;

            WldFragment meshOrSkeleton = null;
            var actorLookupName = $"{modelId}_ACTORDEF";
            var actor = wldEqFile.GetFragmentByNameIncludingInjectedWlds<Actor>(actorLookupName);
            if (actor == null)
            {
                if (MissingSkeletalActors.Contains(modelId, StringComparer.InvariantCultureIgnoreCase))
                {
                    meshOrSkeleton = wldEqFile.GetFragmentByNameIncludingInjectedWlds<SkeletonHierarchy>($"{modelId}_HS_DEF");
                }
                else
                {
                    logger.LogError($"Player character held equipment model '{actorLookupName}' not found!");
                    return null;
                }
            }
            else if (actor.ActorType == ActorType.Static)
            {
                meshOrSkeleton = actor.MeshReference?.Mesh;
            }
            else if (actor.ActorType == ActorType.Skeletal)
            {
                meshOrSkeleton = actor.SkeletonReference?.SkeletonHierarchy;
            }

            return meshOrSkeleton;
        }

        private static void AddPlayerCharacterHeldEquipmentToGltfWriter(WldFragment meshOrSkeleton, string modelId,
            SkeletonHierarchy pcSkeleton, string attachBoneKey, GltfWriter gltfWriter, ref int boneIndexOffset)
        {
            if (meshOrSkeleton == null) return;

            var boneIndex = pcSkeleton.BoneMappingClean.Where(kv => kv.Value == attachBoneKey).Single().Key;
            if (meshOrSkeleton is Mesh)
            {
                MeshExportHelper.ShiftMeshVertices((Mesh)meshOrSkeleton, pcSkeleton, true, "pos", 0, boneIndex, true);
                gltfWriter.AddFragmentData(
                    mesh: (Mesh)meshOrSkeleton,
                    generationMode: ModelGenerationMode.Combine,
                    isSkinned: true,
                    singularBoneIndex: boneIndex);

                return;
            }

            var eqSkeleton = (SkeletonHierarchy)meshOrSkeleton;
            if (string.Equals(modelId, "it156", StringComparison.InvariantCultureIgnoreCase))
            {
                FixClericEpicPosScale(eqSkeleton);
            }
            if (SkeletalActorsNotUsingBoneMeshes.Contains(modelId, StringComparer.InvariantCultureIgnoreCase))
            {
                foreach (var mesh in eqSkeleton.Meshes)
                {
                    if (mesh != null)
                    {
                        MeshExportHelper.ShiftMeshVerticesMultipleSkeletons(
                            mesh,
                            new List<SkeletonHierarchy>() { eqSkeleton, pcSkeleton },
                            new List<bool>() { false, true },
                            "pos",
                            0,
                            new List<int>() { -1, boneIndex },
                            true);
                        gltfWriter.AddFragmentData(mesh, eqSkeleton, boneIndexOffset, pcSkeleton.ModelBase, attachBoneKey, null, true);
                    }
                }
            }
            else
            {
                for (int i = 0; i < eqSkeleton.Skeleton.Count; i++)
                {
                    var bone = eqSkeleton.Skeleton[i];
                    var mesh = bone?.MeshReference?.Mesh;
                    if (mesh != null)
                    {
                        MeshExportHelper.ShiftMeshVerticesMultipleSkeletons(
                            mesh,
                            new List<SkeletonHierarchy>() { eqSkeleton, pcSkeleton },
                            new List<bool>() { false, true },
                            "pos",
                            0,
                            new List<int>() { i, boneIndex },
                            true);

                        gltfWriter.AddFragmentData(mesh, eqSkeleton, i + boneIndexOffset, pcSkeleton.ModelBase, attachBoneKey);
                    }
                }
            }

            gltfWriter.ApplyAnimationToSkeleton(eqSkeleton, "pos", false, true);
            boneIndexOffset += eqSkeleton.Skeleton.Count;
        }

        private static bool IsShield(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;

            var numericItem = int.Parse(itemId.Substring(2));
            return numericItem >= 200 && numericItem < 300;
        }

        private static void FixClericEpicPosScale(SkeletonHierarchy skeleton)
        {
            var posAnimation = skeleton.Animations["pos"];
            var tracks = posAnimation.TracksCleaned;
            var bone0 = skeleton.Skeleton[0];
            var bone1 = skeleton.Skeleton[1];
            var trackDef = tracks[bone0.CleanedName].TrackDefFragment;
            trackDef.Frames[0].Scale = 1f;
            trackDef = tracks[bone1.CleanedName].TrackDefFragment;
            trackDef.Frames[0].Scale = 1f;
        }

        private static readonly string VeliousHelmIdFile = "IT240";

        private static readonly ISet<string> SkeletalActorsNotUsingBoneMeshes = new HashSet<string>() { "IT4", "IT61", "IT153", "IT154", "IT157", "IT198" };
        private static readonly ISet<string> MissingSkeletalActors = new HashSet<string>() { "IT145" };
    }

    public class PlayerCharacterModel
    {
        public int Face { get; set; }
        public string RaceGender { get; set; }
        [JsonPropertyName("Primary ID")]
        public string Primary_ID { get; set; }
        [JsonPropertyName("Primary Name")]
        public string Primary_Name { get; set; }
        [JsonPropertyName("Secondary ID")]
        public string Secondary_ID { get; set; }
        [JsonPropertyName("Secondary Name")]
        public string Secondary_Name { get; set; }
        public Helm Head { get; set; }
        public Equipment Wrist { get; set; }
        public Equipment Arms { get; set; }
        public Equipment Hands { get; set; }
        public Equipment Chest { get; set; }
        public Equipment Legs { get; set; }
        public Equipment Feet { get; set; }

        public class Equipment
        {
            public string Name { get; set; }
            public int Material { get; set; }
            public DColor? Color { get; set; }
        }

        public class Helm : Equipment
        {
            public bool Velious { get; set; }
        }

        public Equipment GetEquipmentForImageName(string imageName, out bool isChest)
        {
            isChest = false;
            imageName = imageName.ToLower();
            if (imageName.StartsWith("clk"))
            {
                isChest = true;
                return Chest;
            }
            if (imageName.Contains("he00") && (imageName.EndsWith("1") || imageName.EndsWith("2")))
            {
                return new Equipment() { Material = Face };
            }
            if (HeadImagesWithNoVariant.Where(i => imageName.StartsWith(i)).Any() ||
                (Head.Velious && imageName.Contains(RaceGenderToVeliousHelmModel[RaceGender].ToLower())))
            {
                return new Equipment() { Material = 0, Color = Head?.Color };
            }

            var part = imageName.Substring(3, 2);
            switch (part)
            {
                case "he": return Head;
                case "fa": return Wrist;
                case "ua": return Arms;
                case "hn": return Hands;
                case "ch":
                    isChest = true;
                    return Chest;
                case "lg": return Legs;
                case "ft": return Feet;
                default: return new Equipment() { Material = 0 };
            }
        }

        public bool IsChestRobe() => Chest.Material >= 10 && Chest.Material <= 16;

        public bool RequiresMeshModificationsForVeliousHelm()
        {
            return Head.Velious && RaceGendersRequiringMeshModificationsWearingVeliousHelm.Contains(RaceGender);
        }

        public bool Validate(out string errorMessage)
        {
            if (string.IsNullOrEmpty(RaceGender))
            {
                errorMessage = "RaceGender is missing or empty!";
                return false;
            }
            RaceGender = RaceGender.ToUpper();
            if (!ValidRaceGenders.Contains(RaceGender))
            {
                errorMessage = $"RaceGender '{RaceGender}' is not a valid value!";
                return false;
            }
            if (IsChestRobe() && !RaceGendersThatCanWearRobe.Contains(RaceGender))
            {
                errorMessage = $"RaceGender '{RaceGender}' cannot wear a robe!";
                return false;
            }
            Primary_ID = Primary_ID?.ToUpper();
            Secondary_ID = Secondary_ID?.ToUpper();
            errorMessage = string.Empty;
            return true;
        }

        public static readonly HashSet<string> ValidRaceGenders = new HashSet<string>()
        {
            "BAF", "BAM", "DAF", "DAM", "DWF", "DWM", "ELF", "ELM", "ERF", "ERM",
            "GNF", "GNM", "HAF", "HAM", "HIF", "HIM", "HOF", "HOM", "HUF", "HUM",
            "IKF", "IKM", "OGF", "OGM", "TRF", "TRM"
        };

        public static readonly HashSet<string> RaceGendersThatCanWearRobe = new HashSet<string>()
        {
            "DAF", "DAM", "ERF", "ERM", "GNF", "GNM",
            "HIF", "HIM", "HUF", "HUM", "IKF", "IKM"
        };

        public static readonly HashSet<string> RaceGendersRequiringMeshModificationsWearingVeliousHelm = new HashSet<string>()
        { 
            "BAF", "DAF", "ELF", "ERF", "ERM", "HUF" 
        };

        public static readonly IDictionary<string, string> RaceGenderToVeliousHelmModel = new Dictionary<string, string>()
        {
            { "HUM", "IT627" },
            { "HUF", "IT620" },
            { "BAM", "IT537" },
            { "BAF", "IT530" },
            { "ERM", "IT575" },
            { "ERF", "IT570" },
            { "ELM", "IT565" },
            { "ELF", "IT561" },
            { "HIM", "IT605" },
            { "HIF", "IT600" },
            { "DAM", "IT545" },
            { "DAF", "IT540" },
            { "HAM", "IT595" },
            { "HAF", "IT590" },
            { "DWM", "IT557" },
            { "DWF", "IT550" },
            { "TRM", "IT655" },
            { "TRF", "IT650" },
            { "OGM", "IT645" },
            { "OGF", "IT640" },
            { "HOM", "IT615" },
            { "HOF", "IT610" },
            { "GNM", "IT585" },
            { "GNF", "IT580" },
            { "IKM", "IT635" },
            { "IKF", "IT630" }
        };
        private static readonly List<string> HeadImagesWithNoVariant = new List<string>()
        {
            "helm",
            "chain",
            "magecap"
        };
    }

    public class ColorJsonConverter : JsonConverter<DColor?>
    {
        public override DColor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var colorValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(colorValue))
            {
                return null;
            }
            if (colorValue.Substring(0, 1) == "#")
            {
                return System.Drawing.ColorTranslator.FromHtml(colorValue);
            }
            if (int.TryParse(colorValue, out var decimalColor))
            {
                return System.Drawing.ColorTranslator.FromHtml($"#{decimalColor:X6}");
            }
            return DColor.FromName(colorValue);
        }

        public override void Write(Utf8JsonWriter writer, DColor? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteStringValue(string.Empty);
                return;
            }
            var colorName = value.Value.Name;
            if (Enum.TryParse<System.Drawing.KnownColor>(colorName, out _))
            {
                writer.WriteStringValue(colorName);
            }
            else // If it's not named, then it will be ARGB in hex. Strip the A and prefix it with # so it can be
            // read back in correctly by ColorTranslator.FromHtml
            {
                writer.WriteStringValue($"#{colorName.Substring(2)}");
            }
        }
    }
}
