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
using LanternExtractor.Infrastructure;

namespace LanternExtractor.EQ.Wld.Exporters
{
    public static class ActorGltfExporter
    {
		private const float ObjInstanceYAxisThreshold = -1000f;

		public static void ExportActors(WldFile wldFile, Settings settings, ILogger logger)
        {
            // For a zone wld, we ignore actors and just export all meshes
            if (wldFile.WldType == WldType.Zone)
            {
                ExportZone((WldFileZone)wldFile, settings, logger);
                return;
            }

            if (wldFile.WldType == WldType.Sky)
            {
                ExportSky(settings, wldFile, logger);
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

            if (settings.ExportZoneCharacterVariations && wldFile.WldType == WldType.Characters &&
                !wldFile.ZoneShortname.StartsWith("global"))
            {
                ExportZoneCharacterVariations((WldFileCharacters)wldFile, settings, logger);
            }
        }

        public static IEnumerable<MaterialList> GatherMaterialLists(ICollection<WldFragment> wldFragments)
        {
            var materialLists = new HashSet<MaterialList>();
            foreach (var fragment in wldFragments)
            {
                if (fragment == null) continue;

                if (fragment is Actor actor)
                {
                    if (actor.ActorType == ActorType.Static)
                    {
                        materialLists.UnionWith(GatherMaterialLists(new List<WldFragment>() { actor.MeshReference?.Mesh }));
                    }
                    else if (actor.ActorType == ActorType.Skeletal)
                    {
                        materialLists.UnionWith(GatherMaterialLists(new List<WldFragment>() { actor.SkeletonReference?.SkeletonHierarchy }));
                    }
                }
                else if (fragment is Mesh mesh)
                {
                    if (mesh.MaterialList != null)
                    {
                        materialLists.Add(mesh.MaterialList);
                    }
                }
                else if (fragment is SkeletonHierarchy skeletonHierarchy)
                {
                    materialLists.UnionWith(GatherMaterialLists(skeletonHierarchy.Meshes.Cast<WldFragment>().ToList()));
                    materialLists.UnionWith(GatherMaterialLists(skeletonHierarchy.Skeleton.Select(b => (WldFragment)b.MeshReference?.Mesh).ToList()));
                }
            }
            return materialLists;
        }

        private static void ExportZone(WldFileZone wldFileZone, Settings settings, ILogger logger)
        {
            var zoneMeshes = wldFileZone.GetFragmentsOfType<Mesh>();

            if (!zoneMeshes.Any()) return;

            var actors = new List<Actor>();
            var materialLists = wldFileZone.GetFragmentsOfType<MaterialList>();
            var objects = new List<ObjInstance>();
            var shortName = wldFileZone.ShortName;
            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
			var rootFolder = wldFileZone.RootFolder;
			if (settings.ExportZoneWithObjects || settings.ExportZoneWithDoors)
            {
                // Get object instances within this zone file to map up and instantiate later
                var zoneObjectsFileInArchive =
                    wldFileZone.S3dArchiveReference.GetFile("objects" + LanternStrings.WldFormatExtension);
                if (zoneObjectsFileInArchive == null)
                {
                    logger.LogError($"Cannot find S3dArchive for Zone {shortName} objects!");
                    return;
                }

                var zoneObjectsWldFile = new WldFileZoneObjects(zoneObjectsFileInArchive, shortName,
                    WldType.ZoneObjects, logger, settings, wldFileZone.WldFileToInject);
                zoneObjectsWldFile.Initialize(rootFolder, false);

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
                    s3dObjArchive.FilenameChanges = objWldFile.FilenameChanges;
                    objWldFile.S3dArchiveReference = s3dObjArchive;
                    ArchiveExtractor.WriteWldTextures(objWldFile, rootFolder + shortName + "/Zone/Textures/", logger);
                    actors.AddRange(objWldFile.GetFragmentsOfType<Actor>());
                    materialLists.AddRange(objWldFile.GetFragmentsOfType<MaterialList>());
                }

                if (settings.ExportZoneWithObjects)
                {
                    objects.AddRange(zoneObjectsWldFile.GetFragmentsOfType<ObjectInstance>().Select(o => new ObjInstance(o)));
                }
                if (settings.ExportZoneWithDoors)
                {
                    var doors = GlobalReference.ServerDatabaseConnector.QueryDoorsInZoneFromDatabase(shortName);
                    objects.AddRange(doors.Select(d => new ObjInstance(d)));
                }
            }

			var lightInstances = new List<LightInstance>();
			if (settings.ExportZoneWithLights)
            {
				if (wldFileZone.WldFileToInject != null) // optional kunark _lit wld exists
				{
					var kunarkLitPath = wldFileZone.BasePath.Replace(".s3d", "_lit.s3d");
				    var s3dArchiveLit = new PfsArchive(kunarkLitPath, logger);
                    s3dArchiveLit.Initialize();
                    var litWldFileInArchive = s3dArchiveLit.GetFile(shortName + "_lit.wld");

					var lightsWldFile =
						new WldFileLights(litWldFileInArchive, shortName, WldType.Lights, logger, settings, wldFileZone.WldFileToInject);
					lightsWldFile.Initialize(rootFolder, false);
                    lightInstances.AddRange(lightsWldFile.GetFragmentsOfType<LightInstance>());
				}

				var lightsFileInArchive = wldFileZone.S3dArchiveReference.GetFile("lights" + LanternStrings.WldFormatExtension);

				if (lightsFileInArchive != null)
				{
					var lightsWldFile =
						new WldFileLights(lightsFileInArchive, shortName, WldType.Lights, logger, settings, wldFileZone.WldFileToInject);
					lightsWldFile.Initialize(rootFolder, false);
                    lightInstances.AddRange(lightsWldFile.GetFragmentsOfType<LightInstance>());
				}
			}

            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);
            var textureImageFolder = $"{wldFileZone.GetExportFolderForWldType()}Textures/";
            gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);

            var bspNodes = wldFileZone.GetFragmentsOfType<BspTree>();
            foreach (var mesh in zoneMeshes)
            {
                if (settings.ExportZoneRegions)
                {
                    var frag = bspNodes[0].Nodes.Find(n => n.Region?.Mesh == mesh);
                    if (frag?.Region?.RegionType != null)
                    {
                        gltfWriter.AddRegionData(mesh, frag);
                    }
                }
                gltfWriter.AddFragmentData(
                    mesh: mesh,
                    generationMode: ModelGenerationMode.Combine,
                    meshNameOverride: shortName);
            }

            gltfWriter.AddCombinedMeshToScene(true, shortName);

            foreach (var actor in actors)
            {
                if (actor.ActorType == ActorType.Static)
                {
                    var objMesh = actor.MeshReference?.Mesh;
                    if (objMesh == null) continue;

                    var instances = objects.FindAll(o =>
                        objMesh.Name.Split('_')[0].Equals(o.Name, StringComparison.InvariantCultureIgnoreCase));
                    var instanceIndex = 0;
                    foreach (var instance in instances)
                    {
                        if (instance.Position.Y < ObjInstanceYAxisThreshold) continue;

                        gltfWriter.AddFragmentData(
                            mesh: objMesh,
                            generationMode: ModelGenerationMode.Separate,
                            objectInstance: instance,
                            isZoneMesh: true,
                            instanceIndex: instanceIndex++);
                    }
                }
                else if (actor.ActorType == ActorType.Skeletal)
                {
                    var skeleton = actor.SkeletonReference?.SkeletonHierarchy;
                    if (skeleton == null) continue;

                    var instances = objects.FindAll(o =>
                        skeleton.Name.Split('_')[0].Equals(o.Name, StringComparison.InvariantCultureIgnoreCase));
                    var instanceIndex = 0;
                    var combinedMeshName = FragmentNameCleaner.CleanName(skeleton);
                    var addedMeshOnce = false;

                    foreach (var instance in instances)
                    {
						if (instance.Position.Y < ObjInstanceYAxisThreshold) continue;

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
                                        MeshExportHelper.ShiftMeshVertices(mesh, skeleton, false, "pos", 0, i, true);
                                    gltfWriter.AddFragmentData(
                                        mesh: mesh,
                                        generationMode: ModelGenerationMode.Combine,
                                        isSkinned: settings.ExportZoneObjectsWithSkeletalAnimations,
                                        meshNameOverride: combinedMeshName,
                                        singularBoneIndex: i,
                                        objectInstance: instance,
                                        instanceIndex: instanceIndex);
                                    mesh.Vertices = originalVertices;
                                }
                            }
						}

                        if (settings.ExportZoneObjectsWithSkeletalAnimations)
                        {
                            gltfWriter.AddNewSkeleton(skeleton, null, null, instance, instanceIndex);
                            gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", false, true, instanceIndex);
							gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", false, false, instanceIndex);
							gltfWriter.AddCombinedMeshToScene(true, combinedMeshName, skeleton.ModelBase, instance, instanceIndex);
						}
                        else
                        {
							gltfWriter.AddCombinedMeshToScene(true, combinedMeshName, null, instance);
						}  
                        addedMeshOnce = true;
                        instanceIndex++;
                    }
                }
            }

            if (lightInstances.Any())
            {
                gltfWriter.AddLightInstances(lightInstances, settings.LightIntensityMultiplier);
            }

            var exportFilePath = $"{wldFileZone.GetExportFolderForWldType()}{wldFileZone.ZoneShortname}.gltf";
            gltfWriter.WriteAssetToFile(exportFilePath, true);
        }

        private static void ExportStaticActor(Actor actor, Settings settings, WldFile wldFile, ILogger logger)
        {
            var mesh = actor?.MeshReference?.Mesh;

            if (mesh == null) return;

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);

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

			if (settings.ExportAllAnimationFrames && wldFile.ZoneShortname != "global")
			{
				GlobalReference.CharacterWld.AddAdditionalAnimationsToSkeleton(skeleton);
			}

			var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
			var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);

			var materialLists = GatherMaterialLists(new List<WldFragment>() { skeleton });
			var exportFolder = wldFile.GetExportFolderForWldType();

            var textureImageFolder = Path.Combine(exportFolder, "Textures");
			gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);

			for (int i = 0; i < skeleton.Skeleton.Count; i++)
			{
				var bone = skeleton.Skeleton[i];
				var mesh = bone?.MeshReference?.Mesh;
				if (mesh != null)
				{
                    var originalVertices = MeshExportHelper.ShiftMeshVertices(mesh, skeleton,
						wldFile.WldType == WldType.Characters, "pos", 0, i, true);

					gltfWriter.AddFragmentData(mesh, skeleton, i);

                    mesh.Vertices = originalVertices;
				}
			}

			if (skeleton.Meshes != null)
			{
				foreach (var mesh in skeleton.Meshes)
				{
					var originalVertices = MeshExportHelper.ShiftMeshVertices(mesh, skeleton,
						wldFile.WldType == WldType.Characters, "pos", 0);

					gltfWriter.AddFragmentData(mesh, skeleton);

                    mesh.Vertices = originalVertices;
				}
			}

			if (settings.ExportAllAnimationFrames)
			{
				gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", wldFile.WldType == WldType.Characters, true);

                foreach (var animationKey in skeleton.Animations.Keys
                    .Where(a => a == "pos" ||
                        settings.ExportedAnimationTypes.Contains(a.Substring(0, 1).ToLower()))
					.OrderBy(k => k, new AnimationKeyComparer()))
				{
					gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey,
						wldFile.WldType == WldType.Characters, false);
				}
			}

			var exportFilePath = $"{exportFolder}{FragmentNameCleaner.CleanName(skeleton)}.gltf";
			gltfWriter.WriteAssetToFile(exportFilePath, true, skeleton.ModelBase);
		}

        private static void ExportZoneCharacterVariations(WldFileCharacters wldChrFile, Settings settings,
            ILogger logger)
        {
			var zoneName = wldChrFile.ZoneShortname;

			var zonePcsWithVariations = GlobalReference.ServerDatabaseConnector
                .QueryPlayerCharactersInZoneFromDatabase(zoneName);
            var zoneGlobalCharacters = GlobalReference.ServerDatabaseConnector
                .QueryGlobalNpcsInZone(zoneName);
            var zoneNpcsWithVariations = GlobalReference.ServerDatabaseConnector
                .QueryNpcsWithVariationsInZone(zoneName);

            FixElementals(zoneGlobalCharacters);

            var allUniqueHeldEquipmentIds = GetUniqueHeldEquipmentIds(
                zonePcsWithVariations, zoneGlobalCharacters, zoneNpcsWithVariations);

			var translator = GlobalReference.NpcDatabaseToClientTranslator;
			var groupedZoneGlobalCharacters = zoneGlobalCharacters.ToLookup(
				c => translator.GetClientModelForRaceIdAndGender(c.Race, (int)c.Gender));
			var groupedZonePcsWithVariations = zonePcsWithVariations.ToLookup(
				p => p.Item2.RaceGender);
			var uniqueGlobalActors = groupedZoneGlobalCharacters.Select(g => g.Key)
                .Union(groupedZonePcsWithVariations.Select(g => g.Key));

            var wldEqFile = ArchiveExtractor.InitWldsForZoneCharacterVariationExport(
                uniqueGlobalActors, allUniqueHeldEquipmentIds, wldChrFile.RootExportFolder, 
                zoneName, logger, settings);

            var groupedZoneNpcsWithVariants = zoneNpcsWithVariations.ToLookup(
                n => translator.GetClientModelForRaceIdAndGender(n.Race, (int)n.Gender));
            
            foreach (var actorName in groupedZonePcsWithVariations.Select(g => g.Key))
            {
                PlayerCharacterGltfExporter.ExportPlayerCharacterVariationsForActor(
                    actorName, groupedZonePcsWithVariations[actorName], GlobalReference.CharacterWld,
                    wldEqFile, zoneName, logger, settings);
            }
                
            foreach (var actorName in groupedZoneGlobalCharacters.Select(g => g.Key))
            {
                var lookupName = $"{actorName}_ACTORDEF";

                var actor = GlobalReference.CharacterWld
                    .GetFragmentByNameIncludingInjectedWlds<Actor>(lookupName);

                ExportActorNpcVariations(actor, settings, GlobalReference.CharacterWld,
                    groupedZoneGlobalCharacters[actorName], wldEqFile, zoneName, logger);
            }

            foreach (var actorName in groupedZoneNpcsWithVariants.Select(g => g.Key))
            {
				var lookupName = $"{actorName}_ACTORDEF";

				var actor = wldChrFile.GetFragmentByNameIncludingInjectedWlds<Actor>(lookupName);

				ExportActorNpcVariations(actor, settings, wldChrFile,
					groupedZoneNpcsWithVariants[actorName], wldEqFile, zoneName, logger);
			}
		}

        private static void FixElementals(IEnumerable<Npc> globalNpcs)
        {
            // Earth
            globalNpcs.Where(n => n.Race == 209).ToList().ForEach(
                n => { n.Race = 75; n.Texture = 0; });

			// Fire
			globalNpcs.Where(n => n.Race == 212).ToList().ForEach(
				n => { n.Race = 75; n.Texture = 1; });

			// Water
			globalNpcs.Where(n => n.Race == 211).ToList().ForEach(
	            n => { n.Race = 75; n.Texture = 2; });

			// Air
			globalNpcs.Where(n => n.Race == 210).ToList().ForEach(
	            n => { n.Race = 75; n.Texture = 3; });
		}

        private static IEnumerable<string> GetUniqueHeldEquipmentIds(
            IEnumerable<(string, PlayerCharacterModel)> zonePcsWithVariations,
            IEnumerable<Npc> zoneGlobalCharacters,
            IEnumerable<Npc> zoneNpcsWithVariations)
        {
            return zonePcsWithVariations.Where(p => p.Item2.Primary_ID != null)
					.Select(p => p.Item2.Primary_ID)
					.Union(
						zonePcsWithVariations.Where(p => p.Item2.Secondary_ID != null)
							.Select(p => p.Item2.Secondary_ID))
					.Union(
						zoneGlobalCharacters.Where(c => c.Primary > 0)
							.Select(c => $"IT{c.Primary}"))
					.Union(
						zoneGlobalCharacters.Where(c => c.Secondary > 0)
							.Select(c => $"IT{c.Secondary}"))
					.Union(
						zoneNpcsWithVariations.Where(c => c.Primary > 0)
							.Select(c => $"IT{c.Primary}"))
					.Union(
						zoneNpcsWithVariations.Where(c => c.Secondary > 0)
							.Select(c => $"IT{c.Secondary}"))
					.Distinct();
		}

        private static void ExportActorNpcVariations(Actor actor, Settings settings, WldFile wldFile,
			IEnumerable<Npc> npcVariations, WldFileEquipment wldEqFile, string zoneName, ILogger logger)
        {
			var skeleton = actor?.SkeletonReference?.SkeletonHierarchy;

			if (skeleton == null) return;

            if (skeleton.Meshes == null) return;

			var actorName = FragmentNameCleaner.CleanName(skeleton);

			if (settings.ExportAllAnimationFrames)
			{
				GlobalReference.CharacterWld.AddAdditionalAnimationsToSkeleton(skeleton);
			}

			var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;
			var gltfWriterForCommonMaterials = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);

			var materialLists = GatherMaterialLists(new List<WldFragment>() { skeleton });
			var exportFolder = Path.Combine(wldFile.RootExportFolder, zoneName, "Characters");

            var textureImageFolder = Path.Combine(exportFolder, "Textures");
			gltfWriterForCommonMaterials.GenerateGltfMaterials(materialLists, textureImageFolder, npcVariations != null);

			var variationGltfWriters = new Dictionary<Npc, GltfWriter>();
			var heldEquipmentMeshes = new Dictionary<int, WldFragment>();

			foreach (var npc in npcVariations)
			{
				var variationWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);
				variationWriter.CopyMaterialList(gltfWriterForCommonMaterials);
				var equipmentFragments = new List<WldFragment>();
				if (npc.Primary > 0)
				{
					if (!heldEquipmentMeshes.TryGetValue(npc.Primary, out var primaryMeshOrSkeleton))
					{
						primaryMeshOrSkeleton = GltfCharacterHeldEquipmentHelper.GetMeshOrSkeletonForCharacterHeldEquipment
							($"IT{npc.Primary}", wldEqFile, logger);
						heldEquipmentMeshes.Add(npc.Primary, primaryMeshOrSkeleton);
					}
					equipmentFragments.Add(primaryMeshOrSkeleton);
				}
				if (npc.Secondary > 0)
				{
					if (!heldEquipmentMeshes.TryGetValue(npc.Secondary, out var secondaryMeshOrSkeleton))
					{
						secondaryMeshOrSkeleton = GltfCharacterHeldEquipmentHelper.GetMeshOrSkeletonForCharacterHeldEquipment
							($"IT{npc.Secondary}", wldEqFile, logger);
						heldEquipmentMeshes.Add(npc.Secondary, secondaryMeshOrSkeleton);
					}
					equipmentFragments.Add(secondaryMeshOrSkeleton);
				}
				if (equipmentFragments.Any())
				{
					var eqMaterialLists = GatherMaterialLists(equipmentFragments);
					variationWriter.GenerateGltfMaterials(eqMaterialLists, textureImageFolder);
				}
				variationGltfWriters.Add(npc, variationWriter);
			}

            // Out of the loop to ensure it's done only once per mesh
            var preShiftedVerticesForMeshes = new Dictionary<string, List<GlmSharp.vec3>>();
			foreach (var mesh in skeleton.Meshes.Union(skeleton.SecondaryMeshes))
			{
				var originalVertices = MeshExportHelper.ShiftMeshVertices(mesh, skeleton,
					wldFile.WldType == WldType.Characters, "pos", 0);
                preShiftedVerticesForMeshes.Add(mesh.Name, originalVertices);
			}
            var boneMeshes = new List<Mesh>();
			for (int i = 0; i < skeleton.Skeleton.Count; i++)
			{
				var mesh = skeleton.Skeleton[i]?.MeshReference?.Mesh;
				if (mesh != null)
				{
					var originalVertices = MeshExportHelper.ShiftMeshVertices(mesh, skeleton,
						wldFile.WldType == WldType.Characters, "pos", 0, i, true);
                    preShiftedVerticesForMeshes.Add(mesh.Name, originalVertices);
                    boneMeshes.Add(mesh);
				}
			}

			foreach (var npc in npcVariations)
			{
                if (skeleton.Meshes != null && skeleton.Meshes.Any())
                {
                    var meshes = new List<Mesh>() { skeleton.Meshes[0] };
					if (npc.HelmTexture > 0 && skeleton.SecondaryMeshes.Any())
					{
						meshes.Add(skeleton.SecondaryMeshes[npc.HelmTexture - 1]);
					}
					else if (skeleton.Meshes.Count > 1)
					{
						meshes.Add(skeleton.Meshes[1]);
					}
					foreach (var mesh in meshes)
					{
						variationGltfWriters[npc].AddFragmentData(mesh, skeleton, -1, npc);
					}
				}
                else
                {
					for (int i = 0; i < skeleton.Skeleton.Count; i++)
					{
						var mesh = skeleton.Skeleton[i]?.MeshReference?.Mesh;
						if (mesh != null)
						{
							variationGltfWriters[npc].AddFragmentData(mesh, skeleton, i, npc);
						}
					}
				}

				var boneIndexOffset = skeleton.Skeleton.Count;
				if (npc.Primary > 0)
				{
					GltfCharacterHeldEquipmentHelper.AddCharacterHeldEquipmentToGltfWriter
						(heldEquipmentMeshes[npc.Primary], $"IT{npc.Primary}", skeleton, "r_point",
							variationGltfWriters[npc], ref boneIndexOffset);
				}
				if (npc.Secondary > 0)
				{
					var secondaryAttachBone = GltfCharacterHeldEquipmentHelper.IsShield($"IT{npc.Secondary}") ?
						"shield_point" : "l_point";
					GltfCharacterHeldEquipmentHelper.AddCharacterHeldEquipmentToGltfWriter
						(heldEquipmentMeshes[npc.Secondary], $"IT{npc.Secondary}", skeleton, secondaryAttachBone,
							variationGltfWriters[npc], ref boneIndexOffset);
				}
			}

			foreach (var mesh in skeleton.Meshes.Union(skeleton.SecondaryMeshes).Union(boneMeshes))
			{
                mesh.Vertices = preShiftedVerticesForMeshes[mesh.Name];
			}

			if (settings.ExportAllAnimationFrames)
			{
				foreach (var gltfWriter in variationGltfWriters.Values)
				{
					gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", wldFile.WldType == WldType.Characters, true);

					foreach (var animationKey in skeleton.Animations.Keys
						.Where(a => settings.ExportedAnimationTypes.Contains(a.Substring(0, 1).ToLower()))
						.OrderBy(k => k, new AnimationKeyComparer()))
					{
						gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey,
							wldFile.WldType == WldType.Characters, false);
					}
				}
			}

			foreach (var variationGltfWriter in variationGltfWriters)
			{
				var fileName = GetUniqueNpcString(actorName, variationGltfWriter.Key);
				var exportFilePath = Path.Combine(exportFolder, $"{fileName}.gltf");
				variationGltfWriter.Value.WriteAssetToFile(exportFilePath, true, skeleton.ModelBase);
			}
		}

		private static void ExportSky(Settings settings, WldFile skyWld, ILogger logger)
        {
            var skySkeletons = skyWld.GetFragmentsOfType<SkeletonHierarchy>();
            var skySkeletonMeshNames = new List<string>();
            skySkeletons.ForEach(s => s.Skeleton.ForEach(b =>
            {
                if (b.MeshReference?.Mesh != null)
                {
                    skySkeletonMeshNames.Add(b.MeshReference.Mesh.Name);
                }
            }));
            var skyMeshes = skyWld.GetFragmentsOfType<Mesh>().Where(m => !skySkeletonMeshNames.Contains(m.Name));
            var materialLists = GatherMaterialLists(skySkeletons.Cast<WldFragment>().Concat(skyMeshes.Cast<WldFragment>()).ToList());

            var exportFormat = settings.ExportGltfInGlbFormat ? GltfExportFormat.Glb : GltfExportFormat.GlTF;

            var exportFolder = skyWld.GetExportFolderForWldType();
            var textureImageFolder = $"{exportFolder}Textures/";


            for (int i = 1; ; i++)
            {
                List<Mesh> meshes = new List<Mesh>();
                foreach (var mesh in skyMeshes)
                {
                    if (new System.Text.RegularExpressions.Regex($"LAYER{i}[13]_").IsMatch(mesh.Name))
                    {
                        meshes.Add(mesh);
                    }
                }
                if (meshes.Count == 0)
                {
                    break;
                }
                var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);
                gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);
                foreach (var mesh in meshes)
                {
                    gltfWriter.AddFragmentData(
                        mesh: mesh,
                        generationMode: ModelGenerationMode.Separate);
                }

                var exportFilePath = $"{exportFolder}sky{i}.gltf";
                gltfWriter.WriteAssetToFile(exportFilePath, true);
            }


            foreach (var skeleton in skySkeletons)
            {
                var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger, settings.SeparateTwoFacedTriangles);
                gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);
                var combinedMeshName = FragmentNameCleaner.CleanName(skeleton);
                for (int i = 0; i < skeleton.Skeleton.Count; i++)
                {
                    var bone = skeleton.Skeleton[i];
                    var mesh = bone?.MeshReference?.Mesh;
                    if (mesh != null)
                    {
                        MeshExportHelper.ShiftMeshVertices(mesh, skeleton, false, "pos", 0, i, true);
                        gltfWriter.AddFragmentData(
                            mesh: mesh,
                            skeleton: skeleton,
                            meshNameOverride: combinedMeshName,
                            singularBoneIndex: i);
                    }
                }

                gltfWriter.AddCombinedMeshToScene(true, combinedMeshName, skeleton.ModelBase);

                if (settings.ExportAllAnimationFrames)
                {
                    gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", false, true);
                    foreach (var animationKey in skeleton.Animations.Keys)
                    {
                        gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, false, false);
                    }
                }
                var exportFilePath = $"{exportFolder}{skeleton.ModelBase}.gltf";
                gltfWriter.WriteAssetToFile(exportFilePath, true);
            }
        }

        private static string GetUniqueNpcString(string actorName, Npc npc)
        {
            if (npc.Texture == 0 && npc.Face == 0 &&
                npc.Primary == 0 && npc.Secondary == 0 && npc.HelmTexture == 0)
            {
                return actorName;
            }
            return $"{actorName}_{npc.Texture:00}-{npc.Face:00}-{npc.HelmTexture:00}-{npc.Primary:000}-{npc.Secondary:000}";
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
}
