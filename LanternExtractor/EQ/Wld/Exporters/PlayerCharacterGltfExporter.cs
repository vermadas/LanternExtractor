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

namespace LanternExtractor.EQ.Wld.Exporters
{
    public static class PlayerCharacterGltfExporter
    { 
        public static void ExportPlayerCharacter(PlayerCharacterModel pcEquipment, WldFileCharacters wldChrFile, WldFileEquipment wldEqFile,
            ILogger logger, Settings settings)
        {
            var actorName = pcEquipment.RaceGender;
            var lookupName = $"{actorName}_ACTORDEF";

            var actor = wldChrFile.GetFragmentByNameIncludingInjectedWlds<Actor>(lookupName);

            var skeleton = actor?.SkeletonReference?.SkeletonHierarchy;

            if (skeleton == null) return;

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);

			pcEquipment.FixHelmMaterial();

			var exportFolder = Path.Combine(wldChrFile.RootExportFolder, actorName);

            AddMeshDataToGltfWriter(pcEquipment, gltfWriter, wldEqFile, skeleton,
                exportFolder, logger, settings);

			var exportFilePath = Path.Combine(exportFolder, $"{FragmentNameCleaner.CleanName(skeleton)}.gltf");
            gltfWriter.WriteAssetToFile(exportFilePath, false, skeleton.ModelBase, true);

            var jsonOutFilePath = Path.Combine(exportFolder, $"PcEquip_{DateTime.Now:yyyyMMddhhmmss}.json");
            var serializerOptions = new JsonSerializerOptions() { WriteIndented = true };
            serializerOptions.Converters.Add(new ColorJsonConverter());
            File.WriteAllText(jsonOutFilePath, JsonSerializer.Serialize(pcEquipment, serializerOptions));
        }

        public static void ExportPlayerCharacterVariationsForActor(string actorName, 
            IEnumerable<(string, PlayerCharacterModel)> pcVariations, WldFileCharacters wldChrFile,
            WldFileEquipment wldEqFile, string zoneName, ILogger logger, Settings settings)
        {
			var lookupName = $"{actorName}_ACTORDEF";

			var actor = wldChrFile.GetFragmentByNameIncludingInjectedWlds<Actor>(lookupName);

			var skeleton = actor?.SkeletonReference?.SkeletonHierarchy;

            if (skeleton == null) return;

            var savedGltfWriters = new Dictionary<PlayerCharacterModel, GltfWriter>(new CharacterVariationComparer());
            var exportFolder = Path.Combine(wldChrFile.RootExportFolder, zoneName, "Characters");
			foreach (var pcVariation in pcVariations)
            {
                var npcName = pcVariation.Item1;
                var pcModel = pcVariation.Item2;
                pcModel.FixHelmMaterial();
				var exportFilePath = Path.Combine(exportFolder, $"{npcName}.gltf");

				if (savedGltfWriters.TryGetValue(pcModel, out var gltfWriter))
                {
                    gltfWriter.WriteAssetToFile(exportFilePath, true);
                    continue;
                }
				var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
				gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);

				AddMeshDataToGltfWriter(pcModel, gltfWriter, wldEqFile, skeleton,
	                exportFolder, logger, settings);

                gltfWriter.WriteAssetToFile(exportFilePath, true, skeleton.ModelBase);
                savedGltfWriters.Add(pcModel, gltfWriter);
			}
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

        private static void AddMeshDataToGltfWriter(PlayerCharacterModel pcEquipment, GltfWriter gltfWriter, 
            WldFileEquipment wldEqFile, SkeletonHierarchy skeleton,
			string exportFolder, ILogger logger, Settings settings)
        {
            //var actorName = pcEquipment.RaceGender;
			GetBodyAndHeadMeshes(pcEquipment, skeleton, out var bodyMesh, out var headMesh);

			WldFragment primaryMeshOrSkeleton = null;
			WldFragment secondaryMeshOrSkeleton = null;
			WldFragment veliousHelm = null;
			var veliousHelmModelId = pcEquipment.Head.Velious ? PlayerCharacterModel.RaceGenderToVeliousHelmModel[pcEquipment.RaceGender] : null;

			var wldFragmentsWithMaterialLists = new List<WldFragment>() { bodyMesh, headMesh };
			if (wldEqFile != null)
			{
				primaryMeshOrSkeleton = GltfCharacterHeldEquipmentHelper.GetMeshOrSkeletonForCharacterHeldEquipment
					(pcEquipment.Primary_ID, wldEqFile, logger);
				wldFragmentsWithMaterialLists.Add(primaryMeshOrSkeleton);
				secondaryMeshOrSkeleton = GltfCharacterHeldEquipmentHelper.GetMeshOrSkeletonForCharacterHeldEquipment
					(pcEquipment.Secondary_ID, wldEqFile, logger);
				wldFragmentsWithMaterialLists.Add(secondaryMeshOrSkeleton);
				veliousHelm = GltfCharacterHeldEquipmentHelper.GetMeshOrSkeletonForCharacterHeldEquipment
					(veliousHelmModelId, wldEqFile, logger);
				wldFragmentsWithMaterialLists.Add(veliousHelm);
			}

			var materialLists = ActorGltfExporter.GatherMaterialLists(wldFragmentsWithMaterialLists.Where(m => m != null).ToList());

			gltfWriter.GenerateGltfMaterials(materialLists, Path.Combine(exportFolder, "Textures"), true);

			var originalVertices = MeshExportHelper.ShiftMeshVertices(bodyMesh, skeleton, true, "pos", 0);
			gltfWriter.AddFragmentData(bodyMesh, skeleton, -1, pcEquipment);
            bodyMesh.Vertices = originalVertices;
			originalVertices = MeshExportHelper.ShiftMeshVertices(headMesh, skeleton, true, "pos", 0);
			gltfWriter.AddFragmentData(headMesh, skeleton, -1, pcEquipment);
            headMesh.Vertices = originalVertices;

			var boneIndexOffset = skeleton.Skeleton.Count;

			GltfCharacterHeldEquipmentHelper.AddCharacterHeldEquipmentToGltfWriter
				(primaryMeshOrSkeleton, pcEquipment.Primary_ID, skeleton, "r_point", gltfWriter, ref boneIndexOffset);
			var secondaryAttachBone = GltfCharacterHeldEquipmentHelper.IsShield(pcEquipment.Secondary_ID) ? "shield_point" : "l_point";
			GltfCharacterHeldEquipmentHelper.AddCharacterHeldEquipmentToGltfWriter
				(secondaryMeshOrSkeleton, pcEquipment.Secondary_ID, skeleton, secondaryAttachBone, gltfWriter, ref boneIndexOffset);
			GltfCharacterHeldEquipmentHelper.AddCharacterHeldEquipmentToGltfWriter
				(veliousHelm, veliousHelmModelId, skeleton, "he", gltfWriter, ref boneIndexOffset);

			gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", true, true);

			if (settings.ExportAllAnimationFrames)
			{
				foreach (var animationKey in skeleton.Animations.Keys
					.Where(a => settings.ExportedAnimationTypes.Contains(a.Substring(0, 1).ToLower()))
					.OrderBy(k => k, new AnimationKeyComparer()))
				{
					gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, true, false);
				}
			}
		}

		private static void GetBodyAndHeadMeshes(PlayerCharacterModel pcEquipment, SkeletonHierarchy skeleton,
			out Mesh bodyMesh, out Mesh headMesh)
		{
			var actorName = pcEquipment.RaceGender;
			var allMeshes = skeleton.Meshes.Union(skeleton.SecondaryMeshes);
			var bodyMeshName = pcEquipment.IsChestRobe() ? $"{actorName}01" : actorName;
			bodyMeshName = $"{bodyMeshName}_DMSPRITEDEF";

			var headMeshName = $"{actorName}HE{pcEquipment.Head.Material:00}";
			headMeshName = $"{headMeshName}_DMSPRITEDEF";
			bodyMesh = allMeshes.Where(m => m.Name.Equals(bodyMeshName, StringComparison.InvariantCultureIgnoreCase)).Single();
			headMesh = allMeshes.Where(m => m.Name.Equals(headMeshName, StringComparison.InvariantCultureIgnoreCase)).Single();
		}

		private static readonly string VeliousHelmIdFile = "IT240";
    }

    public class PlayerCharacterModel : ICharacterModel
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

        public bool TryGetMaterialVariation(string imageName, out int variationIndex, out DColor? color)
        {
            var equipment = GetEquipmentForImageName(imageName, out var isChest);

            variationIndex = equipment.Material;
            color = equipment.Color;

            if (variationIndex == 0) return false;

			variationIndex = isChest && IsChestRobe() ? variationIndex - 7 : variationIndex - 1;

            return true;
        }

        public bool ShouldSkipMeshGenerationForMaterial(string materialName)
        {
            if (!RequiresMeshModificationsForVeliousHelm()) return false;

            var raceGendersWithHelmMaterialName = new HashSet<string>() { "DAF", "ELF", "ERF", "HUF" };
            if (raceGendersWithHelmMaterialName.Contains(RaceGender) && materialName.Contains("helm"))
            {
                return true;
            }
            if (RaceGender == "BAF" && materialName.Contains("bamhe") &&
                (materialName.EndsWith("03") || materialName.EndsWith("05")))
            {
                return true;
            }
            if (RaceGender == "DAF" && materialName.Contains("dafhe00") && materialName.EndsWith("2"))
            {
                return true;
            }
            if (RaceGender == "ERM" &&
                (materialName.Contains("clkerm") || (materialName.Contains("clk") && materialName.EndsWith("06"))))
            {
                return true;
            }
            return false;
        }

        public void FixHelmMaterial()
        {
			if (Head.Material == 7)
			{
				Head.Material = 2; // Kunark chain
			}
			else if (RequiresMeshModificationsForVeliousHelm() && RaceGender != "ERM")
			{
				Head.Material = 3;
			}
			else if (Head.Material == 4 || Head.Material > 16 || Head.Velious) // Monk or Velious
			{
				Head.Material = 0;
			}
		}

		private Equipment GetEquipmentForImageName(string imageName, out bool isChest)
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

	public class CharacterVariationComparer : IEqualityComparer<PlayerCharacterModel>
	{
		public bool Equals(PlayerCharacterModel x, PlayerCharacterModel y)
		{
            return string.Equals(x.RaceGender, y.RaceGender, StringComparison.InvariantCultureIgnoreCase) &&
                string.Equals(x.Primary_ID, y.Primary_ID, StringComparison.InvariantCultureIgnoreCase) &&
                string.Equals(x.Secondary_ID, y.Secondary_ID, StringComparison.InvariantCultureIgnoreCase) &&
                x.Face == y.Face &&
                x.Head.Material == y.Head.Material &&
                x.Head.Velious == y.Head.Velious &&
                x.Wrist.Material == y.Wrist.Material &&
                x.Arms.Material == y.Arms.Material &&
                x.Hands.Material == y.Hands.Material &&
                x.Chest.Material == y.Chest.Material &&
                x.Chest.Color == y.Chest.Color &&
                x.Legs.Material == y.Legs.Material &&
                x.Feet.Material == y.Feet.Material;
		}

		public int GetHashCode(PlayerCharacterModel obj)
		{
            return (obj.RaceGender.ToUpper(),
                obj.Primary_ID?.ToUpper(),
                obj.Secondary_ID?.ToUpper(),
                obj.Face,
                obj.Head.Material,
                obj.Head.Velious,
                obj.Wrist.Material,
                obj.Arms.Material,
                obj.Hands.Material,
                obj.Chest.Material,
                obj.Chest.Color,
                obj.Legs.Material,
                obj.Feet.Material).GetHashCode();
		}
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
