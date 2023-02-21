using LanternExtractor.EQ.Pfs;
using LanternExtractor.EQ.Wld.DataTypes;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.Infrastructure.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanternExtractor.EQ.Wld.Helpers;
using static LanternExtractor.EQ.Wld.Exporters.GltfWriter;

namespace LanternExtractor.EQ.Wld.Exporters
{
    public static class ActorGltfExporter
    {
        public static void ExportActors(WldFile wldFile, Settings settings, ILogger logger)
        {
            // For a zone wld, we ignore actors and just export all meshes
            if (wldFile.WldType == WldType.Zone)
            {
                ExportZone((WldFileZone)wldFile, settings, logger);
                return;
            }

            foreach (var actor in wldFile.GetFragmentsOfType<Actor>())
            {
                switch (actor.ActorType)
                {
                    case ActorType.Static:
                        ExportStaticActor(actor, settings, wldFile, logger);
                        break;
                    case ActorType.Skeletal:
                        ExportSkeletalActor(actor, settings, wldFile, logger);
                        break;
                    default:
                        continue;
                }
            }
        }

        public static void ExportPlayerCharacter(WldFileCharacters wldChrFile, (WldFileEquipment, WldFileEquipment) wldEqFiles,
            Settings settings, ILogger logger, PlayerCharacterModel playerCharacterModel)
        {
            var actorName = playerCharacterModel.RaceGender;
            var lookupName = $"{actorName}_ACTORDEF";

            var actor = wldChrFile.GetFragmentByName<Actor>(lookupName);

            var skeleton = actor?.SkeletonReference?.SkeletonHierarchy;

            if (skeleton == null) return;

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger);

            var allMeshes = skeleton.Meshes.Union(skeleton.SecondaryMeshes);
            var bodyMeshName = playerCharacterModel.Robe ? $"{actorName}01" : actorName;
            bodyMeshName = $"{bodyMeshName}_DMSPRITEDEF";
            var headMeshName = $"{actorName}HE{playerCharacterModel.Head.Material:00}";
            headMeshName = $"{headMeshName}_DMSPRITEDEF";
            var bodyMesh = allMeshes.Where(m => m.Name.Equals(bodyMeshName, StringComparison.InvariantCultureIgnoreCase)).Single();
            var headMesh = allMeshes.Where(m => m.Name.Equals(headMeshName, StringComparison.InvariantCultureIgnoreCase)).Single();
            WldFragment primaryMeshOrSkeleton = null;
            WldFragment secondaryMeshOrSkeleton = null;

            var materialLists = new HashSet<MaterialList>();
            materialLists.Add(bodyMesh.MaterialList);
            materialLists.Add(headMesh.MaterialList);
            if (wldEqFiles.Item1 != null)
            {
                if (!string.IsNullOrEmpty(playerCharacterModel.Primary))
                {
                    var primaryActorLookupName = $"{playerCharacterModel.Primary}_ACTORDEF";
                    var primaryActor = wldEqFiles.Item1.GetFragmentByName<Actor>(primaryActorLookupName);
                    if (primaryActor == null)
                    {
                        primaryActor = wldEqFiles.Item2.GetFragmentByName<Actor>(primaryActorLookupName);
                        if (primaryActor == null)
                        {
                            logger.LogError($"Player character model primary '{primaryActorLookupName}' not found!");
                            return;
                        }
                    }
                    if (primaryActor.ActorType == ActorType.Static)
                    {
                        primaryMeshOrSkeleton = primaryActor.MeshReference?.Mesh;
                        if (primaryMeshOrSkeleton != null)
                        {
                            materialLists.Add(((Mesh)primaryMeshOrSkeleton).MaterialList);
                        }
                    }
                    else if (primaryActor.ActorType == ActorType.Skeletal)
                    {
                        primaryMeshOrSkeleton = primaryActor.SkeletonReference?.SkeletonHierarchy;
                        if (primaryMeshOrSkeleton != null)
                        {
                            foreach (var bone in ((SkeletonHierarchy)primaryMeshOrSkeleton).Skeleton.Where(b => b.MeshReference?.Mesh != null))
                            {
                                materialLists.Add(bone.MeshReference.Mesh.MaterialList);
                            }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(playerCharacterModel.Secondary))
                {
                    var secondaryActorLookupName = $"{playerCharacterModel.Secondary}_ACTORDEF";
                    var secondaryActor = wldEqFiles.Item1.GetFragmentByName<Actor>(secondaryActorLookupName);
                    if (secondaryActor == null)
                    {
                        secondaryActor = wldEqFiles.Item2.GetFragmentByName<Actor>(secondaryActorLookupName);
                        if (secondaryActor == null)
                        {
                            logger.LogError($"Player character model secondary '{secondaryActorLookupName}' not found!");
                            return;
                        }
                    }
                    secondaryMeshOrSkeleton = secondaryActor.MeshReference?.Mesh;
                    if (secondaryMeshOrSkeleton != null)
                    {
                        materialLists.Add(((Mesh)secondaryMeshOrSkeleton).MaterialList);
                    }
                }
            }

            var exportFolder = wldChrFile.GetExportFolderForWldType();
            if (exportFolder.EndsWith("Characters/"))
            {
                exportFolder = exportFolder.Replace("Characters/", "");
            }
            gltfWriter.GenerateGltfMaterials(materialLists, Path.Combine(exportFolder, "Textures"), true);

            MeshExportHelper.ShiftMeshVertices(bodyMesh, skeleton, true, "pos", 0);
            gltfWriter.AddFragmentData(bodyMesh, skeleton, playerCharacterModel);
            MeshExportHelper.ShiftMeshVertices(headMesh, skeleton, true, "pos", 0);
            gltfWriter.AddFragmentData(headMesh, skeleton, playerCharacterModel);

            var boneIndexOffset = skeleton.Skeleton.Count;

            if (primaryMeshOrSkeleton != null)
            {
                var primaryBoneIndex = skeleton.BoneMappingClean.Where(kv => kv.Value == "r_point").Single().Key;
                if (primaryMeshOrSkeleton is Mesh)
                {
                    MeshExportHelper.ShiftMeshVertices((Mesh)primaryMeshOrSkeleton, skeleton, true, "pos", 0, primaryBoneIndex, true);
                    gltfWriter.AddFragmentData(
                        mesh: (Mesh)primaryMeshOrSkeleton,
                        generationMode: ModelGenerationMode.Combine,
                        isSkinned: true,
                        singularBoneIndex: primaryBoneIndex);
                }
                else
                {
                    var primarySkeleton = (SkeletonHierarchy)primaryMeshOrSkeleton;
                    for (int i = 0; i < primarySkeleton.Skeleton.Count; i++)
                    {
                        var bone = primarySkeleton.Skeleton[i];
                        var mesh = bone?.MeshReference?.Mesh;
                        if (mesh != null)
                        {
                            MeshExportHelper.ShiftMeshVerticesMultipleSkeletons(
                                mesh,
                                new List<SkeletonHierarchy>() { primarySkeleton, skeleton },
                                new List<bool>() { false, true },
                                "pos",
                                0,
                                new List<int>() { i, primaryBoneIndex },
                                true);

                            gltfWriter.AddFragmentData(mesh, primarySkeleton, null, i + boneIndexOffset, skeleton.ModelBase, "r_point");
                            gltfWriter.ApplyAnimationToSkeleton(primarySkeleton, "pos", false, true);
                        }
                    }
                    boneIndexOffset += primarySkeleton.Skeleton.Count;
                }
            }
            if (secondaryMeshOrSkeleton != null)
            {
                var attachBone = IsShield(playerCharacterModel.Secondary) ? "shield_point" : "l_point";
                var secondaryBoneIndex = skeleton.BoneMappingClean.Where(kv => kv.Value == attachBone).Single().Key;
                MeshExportHelper.ShiftMeshVertices((Mesh)secondaryMeshOrSkeleton, skeleton, true, "pos", 0, secondaryBoneIndex);
                gltfWriter.AddFragmentData(
                    mesh: (Mesh)secondaryMeshOrSkeleton,
                    generationMode: ModelGenerationMode.Combine,
                    isSkinned: true,
                    singularBoneIndex: secondaryBoneIndex);
            }

            gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", true, true);

            if (settings.ExportAllAnimationFrames)
            {
                foreach (var animationKey in skeleton.Animations.Keys)
                {
                    gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, true, false);
                }
            }

            var exportFilePath = $"{exportFolder}{FragmentNameCleaner.CleanName(skeleton)}.gltf";
            gltfWriter.WriteAssetToFile(exportFilePath, true, skeleton.ModelBase);
        }

        private static void ExportZone(WldFileZone wldFileZone, Settings settings, ILogger logger )
        {
            var zoneMeshes = wldFileZone.GetFragmentsOfType<Mesh>();
            var actors = new List<Actor>();
            var materialLists = wldFileZone.GetFragmentsOfType<MaterialList>();
            var objects = new List<ObjectInstance>();
            var shortName = wldFileZone.ShortName;
            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;

            if (settings.ExportZoneWithObjects)
            {
                var rootFolder = wldFileZone.RootFolder;

                // Get object instances within this zone file to map up and instantiate later
                var zoneObjectsFileInArchive =
                    wldFileZone.BaseS3DArchive.GetFile("objects" + LanternStrings.WldFormatExtension);
                if (zoneObjectsFileInArchive != null)
                {
                    var zoneObjectsWldFile = new WldFileZoneObjects(zoneObjectsFileInArchive, shortName,
                        WldType.ZoneObjects, logger, settings, wldFileZone.WldFileToInject);
                    zoneObjectsWldFile.Initialize(rootFolder, false);
                    objects.AddRange(zoneObjectsWldFile.GetFragmentsOfType<ObjectInstance>()
                        .Where(o => !o.ObjectName.Contains("door")));
                }

                // Find associated _obj archive e.g. qeytoqrg_obj.s3d, open it and add meshes and materials to our list
                var objPath = wldFileZone.BasePath.Replace(".s3d", "_obj.s3d");
                var objArchive = Path.GetFileNameWithoutExtension(objPath);
                var s3dObjArchive = new PfsArchive(objPath, logger);
                if (s3dObjArchive.Initialize())
                {
                    string wldFileName = objArchive + LanternStrings.WldFormatExtension;
                    var objWldFile = new WldFileZone(s3dObjArchive.GetFile(wldFileName), shortName, WldType.Objects,
                        logger, settings);
                    objWldFile.Initialize(rootFolder, false);
                    ArchiveExtractor.WriteWldTextures(s3dObjArchive, objWldFile,
                        rootFolder + shortName + "/Zone/Textures/", logger);
                    actors.AddRange(objWldFile.GetFragmentsOfType<Actor>());
                    materialLists.AddRange(objWldFile.GetFragmentsOfType<MaterialList>());
                }
            }

            if (!zoneMeshes.Any())
            {
                return;
            }

            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger);
            var textureImageFolder = $"{wldFileZone.GetExportFolderForWldType()}Textures/";
            gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);

            foreach (var mesh in zoneMeshes)
            {
                gltfWriter.AddFragmentData(
                    mesh: mesh,
                    generationMode: ModelGenerationMode.Combine,
                    meshNameOverride: shortName,
                    isZoneMesh: true);
            }

            gltfWriter.AddCombinedMeshToScene(true, shortName);

            foreach (var actor in actors)
            {
                if (actor.ActorType == ActorType.Static)
                {
                    var objMesh = actor.MeshReference?.Mesh;
                    if (objMesh == null) continue;

                    var instances = objects.FindAll(o =>
                        objMesh.Name.StartsWith(o.ObjectName, StringComparison.InvariantCultureIgnoreCase));
                    var instanceIndex = 0;
                    foreach (var instance in instances)
                    {
                        if (instance.Position.z < short.MinValue) continue;
                        // TODO: this could be more nuanced, I think this still exports trees below the zone floor

                        gltfWriter.AddFragmentData(
                            mesh: objMesh,
                            generationMode: ModelGenerationMode.Separate,
                            objectInstance: instance,
                            instanceIndex: instanceIndex++,
                            isZoneMesh: true);
                    }
                }
                else if (actor.ActorType == ActorType.Skeletal)
                {
                    var skeleton = actor.SkeletonReference?.SkeletonHierarchy;
                    if (skeleton == null) continue;

                    var instances = objects.FindAll(o =>
                        skeleton.Name.StartsWith(o.ObjectName, StringComparison.InvariantCultureIgnoreCase));
                    var instanceIndex = 0;
                    var combinedMeshName = FragmentNameCleaner.CleanName(skeleton);
                    var addedMeshOnce = false;

                    foreach (var instance in instances)
                    {
                        if (instance.Position.z < short.MinValue) continue;

                        if (!addedMeshOnce ||
                            (settings.ExportGltfVertexColors
                             && instance.Colors?.Colors != null
                             && instance.Colors.Colors.Any()))
                        {
                            for (int i = 0; i < skeleton.Skeleton.Count; i++)
                            {
                                var bone = skeleton.Skeleton[i];
                                var mesh = bone?.MeshReference?.Mesh;
                                if (mesh != null)
                                {
                                    var originalVertices =
                                        MeshExportHelper.ShiftMeshVertices(mesh, skeleton, false, "pos", 0, i);
                                    gltfWriter.AddFragmentData(
                                        mesh: mesh,
                                        generationMode: ModelGenerationMode.Combine,
                                        meshNameOverride: combinedMeshName,
                                        singularBoneIndex: i,
                                        objectInstance: instance,
                                        instanceIndex: instanceIndex,
                                        isZoneMesh: true);
                                    mesh.Vertices = originalVertices;
                                }
                            }
                        }

                        gltfWriter.AddCombinedMeshToScene(true, combinedMeshName, null, instance);
                        addedMeshOnce = true;
                        instanceIndex++;
                    }
                }
            }

            var exportFilePath = $"{wldFileZone.GetExportFolderForWldType()}{wldFileZone.ZoneShortname}.gltf";
            gltfWriter.WriteAssetToFile(exportFilePath, true);
        }

        private static void ExportStaticActor(Actor actor, Settings settings, WldFile wldFile, ILogger logger)
        {
            var mesh = actor?.MeshReference?.Mesh;

            if (mesh == null) return;

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger);

            var exportFolder = wldFile.GetExportFolderForWldType();

            var textureImageFolder = $"{exportFolder}Textures/";
            gltfWriter.GenerateGltfMaterials(new List<MaterialList>() { mesh.MaterialList }, textureImageFolder);
            gltfWriter.AddFragmentData(mesh);

            var exportFilePath = $"{exportFolder}{FragmentNameCleaner.CleanName(mesh)}.gltf";
            gltfWriter.WriteAssetToFile(exportFilePath, true);
        }

        private static void ExportSkeletalActor(Actor actor, Settings settings, WldFile wldFile, ILogger logger)
        {
            var skeleton = actor?.SkeletonReference?.SkeletonHierarchy;

            if (skeleton == null) return;

            if (settings.ExportAdditionalAnimations && wldFile.ZoneShortname != "global")
            {
                GlobalReference.CharacterWld.AddAdditionalAnimationsToSkeleton(skeleton);
            }

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger);

            var materialLists = new HashSet<MaterialList>();
            var skeletonMeshMaterialList = skeleton.Meshes?.FirstOrDefault()?.MaterialList;
            if (skeletonMeshMaterialList != null)
            {
                materialLists.Add(skeletonMeshMaterialList);
            }

            foreach (var skeletonBones in skeleton.Skeleton)
            {
                var boneMaterialList = skeletonBones.MeshReference?.Mesh?.MaterialList;
                if (boneMaterialList != null)
                {
                    materialLists.Add(boneMaterialList);
                }
            }

            var exportFolder = wldFile.GetExportFolderForWldType();

            var textureImageFolder = $"{exportFolder}Textures/";
            gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);

            for (int i = 0; i < skeleton.Skeleton.Count; i++)
            {
                var bone = skeleton.Skeleton[i];
                var mesh = bone?.MeshReference?.Mesh;
                if (mesh != null)
                {
                    MeshExportHelper.ShiftMeshVertices(mesh, skeleton,
                        wldFile.WldType == WldType.Characters, "pos", 0, i);

                    gltfWriter.AddFragmentData(mesh, skeleton, null, i);
                }
            }

            if (skeleton.Meshes != null)
            {
                foreach (var mesh in skeleton.Meshes)
                {
                    MeshExportHelper.ShiftMeshVertices(mesh, skeleton,
                        wldFile.WldType == WldType.Characters, "pos", 0);

                    gltfWriter.AddFragmentData(mesh, skeleton);
                }

                for (var i = 0; i < skeleton.SecondaryMeshes.Count; i++)
                {
                    var secondaryMesh = skeleton.SecondaryMeshes[i];
                    var secondaryGltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger);
                    secondaryGltfWriter.CopyMaterialList(gltfWriter);
                    secondaryGltfWriter.AddFragmentData(skeleton.Meshes[0], skeleton);

                    MeshExportHelper.ShiftMeshVertices(secondaryMesh, skeleton,
                        wldFile.WldType == WldType.Characters, "pos", 0);
                    secondaryGltfWriter.AddFragmentData(secondaryMesh, skeleton);
                    secondaryGltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", wldFile.WldType == WldType.Characters,
                        true);

                    if (settings.ExportAllAnimationFrames)
                    {
                        secondaryGltfWriter.AddFragmentData(secondaryMesh, skeleton);
                        foreach (var animationKey in skeleton.Animations.Keys
							.OrderBy(k => k, new AnimationKeyComparer()))
                        {
                            secondaryGltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, 
								wldFile.WldType == WldType.Characters, false);
                        }
                    }

                    var secondaryExportPath = $"{exportFolder}{FragmentNameCleaner.CleanName(skeleton)}_{i:00}.gltf";
                    secondaryGltfWriter.WriteAssetToFile(secondaryExportPath, true, skeleton.ModelBase);
                }
            }

            gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", wldFile.WldType == WldType.Characters, true);

            if (settings.ExportAllAnimationFrames)
            {
                foreach (var animationKey in skeleton.Animations.Keys
					.OrderBy(k => k, new AnimationKeyComparer()))
                {
                    gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, 
						wldFile.WldType == WldType.Characters, false);
                }
            }

            var exportFilePath = $"{exportFolder}{FragmentNameCleaner.CleanName(skeleton)}.gltf";
            gltfWriter.WriteAssetToFile(exportFilePath, true, skeleton.ModelBase);

            // TODO: bother with skin variants? If GLTF can just copy the .gltf and change the
            // corresponding image URIs. If GLB then would have to repackage every variant.
            // KHR_materials_variants extension is made for this, but no support for it in SharpGLTF
        }

        private static bool IsShield(string itemId)
        {
            var numericItem = int.Parse(itemId.Substring(2));
            return numericItem >= 200 && numericItem < 300;
        }
    }

    public class AnimationKeyComparer : Comparer<string>
    {
        public override int Compare(string x, string y)
        {
            if ((x ?? string.Empty) == (y ?? string.Empty)) return 0;
            if (x.ToLower() == y.ToLower()) return 0;
            if (string.IsNullOrEmpty(x)) return -1;
            if (string.IsNullOrEmpty(y)) return 1;

            if (x.ToLower() == "pos") return -1;
            if (y.ToLower() == "pos") return 1;

            if (x.ToLower() == "drf") return -1;
            if (y.ToLower() == "drf") return 1;

            var animationCharCompare =
                AnimationSort.IndexOf(x.Substring(0, 1))
                .CompareTo(
                AnimationSort.IndexOf(y.Substring(0, 1)));

            if (animationCharCompare != 0) return animationCharCompare;

            return int.Parse(x.Substring(1, 2))
                .CompareTo(
                int.Parse(y.Substring(1, 2)));
        }

        // Passive, Idle, Locomotion, Combat, Damage, Spell/Instrument, Social
        private const string AnimationSort = "polcdts";
    }
	
	public class PlayerCharacterModel
    {
        public int Face { get; set; }
        public string RaceGender { get; set; }
        public bool Robe { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
        public Equipment Head { get; set; }
        public Equipment Wrist { get; set; }
        public Equipment Arms { get; set; }
        public Equipment Hands { get; set; }
        public Equipment Chest { get; set; }
        public Equipment Legs { get; set; }
        public Equipment Feet { get; set; }

        public class Equipment
        {
            public int Material { get; set; }
            public Color Color { get; set; }
        }

        public Equipment GetEquipmentForImageName(string imageName)
        {
            imageName = imageName.ToLower();
            if (imageName.StartsWith("clk")) return Chest;
            if (imageName.Contains("he00") && (imageName.EndsWith("1") || imageName.EndsWith("2")))
            {
                return new Equipment() { Material = Face };
            }
            foreach (var headImage in HeadImagesWithNoVariant)
            {
                if (imageName.StartsWith(headImage)) return new Equipment() { Material = 0, Color = Head?.Color };
            }
            
            var part = imageName.Substring(3, 2);
            switch (part)
            {
                case "he": return Head;
                case "fa": return Wrist;
                case "ua": return Arms;
                case "hn": return Hands;
                case "ch": return Chest;
                case "lg": return Legs;
                case "ft": return Feet;
                default: return new Equipment() { Material = 0 };
            }
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
            if (Robe && !RaceGendersThatCanWearRobe.Contains(RaceGender))
            {
                errorMessage = $"RaceGender '{RaceGender}' cannot wear a robe!";
                return false;
            }
            Primary = Primary?.ToUpper();
            Secondary = Secondary?.ToUpper();
            errorMessage = "";
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

        private static readonly List<string> HeadImagesWithNoVariant = new List<string>()
        {
            "helm",
            "chain",
            "magecap"
        };
    }
}
