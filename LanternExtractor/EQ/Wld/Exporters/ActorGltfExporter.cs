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

            if (settings.ExportZoneWithObjects || settings.ExportZoneWithDoors)
            {
                var rootFolder = wldFileZone.RootFolder;

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
                        objMesh.Name.Split('_')[0].Equals(o.Name, StringComparison.InvariantCultureIgnoreCase));
                    var instanceIndex = 0;
                    foreach (var instance in instances)
                    {
                        if (instance.Position.Z < short.MinValue) continue;
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
                        skeleton.Name.Split('_')[0].Equals(o.Name, StringComparison.InvariantCultureIgnoreCase));
                    var instanceIndex = 0;
                    var combinedMeshName = FragmentNameCleaner.CleanName(skeleton);
                    var addedMeshOnce = false;

                    foreach (var instance in instances)
                    {
                        if (instance.Position.Z < short.MinValue) continue;

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

            var materialLists = GatherMaterialLists(new List<WldFragment>() { skeleton });
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

                    gltfWriter.AddFragmentData(mesh, skeleton, i);
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
            var gltfWriter = new GltfWriter(settings.ExportGltfVertexColors, exportFormat, logger);

            var exportFolder = skyWld.GetExportFolderForWldType();

            var textureImageFolder = $"{exportFolder}Textures/";
            gltfWriter.GenerateGltfMaterials(materialLists, textureImageFolder);

            foreach (var mesh in skyMeshes)
            {
                gltfWriter.AddFragmentData(
                    mesh: mesh,
                    generationMode: ModelGenerationMode.Separate);
            }

            foreach (var skeleton in skySkeletons)
            {
                var combinedMeshName = FragmentNameCleaner.CleanName(skeleton);
                for (int i = 0; i < skeleton.Skeleton.Count; i++)
                {
                    var bone = skeleton.Skeleton[i];
                    var mesh = bone?.MeshReference?.Mesh;
                    if (mesh != null)
                    {
                        MeshExportHelper.ShiftMeshVertices(mesh, skeleton, false, "pos", 0, i);
                        gltfWriter.AddFragmentData(
                            mesh: mesh,
                            skeleton: skeleton,
                            meshNameOverride: combinedMeshName,
                            singularBoneIndex: i);
                    }
                }

                gltfWriter.AddCombinedMeshToScene(true, combinedMeshName, skeleton.ModelBase);
                gltfWriter.ApplyAnimationToSkeleton(skeleton, "pos", false, true);

				if (settings.ExportAllAnimationFrames)
				{
					foreach (var animationKey in skeleton.Animations.Keys)
					{
						gltfWriter.ApplyAnimationToSkeleton(skeleton, animationKey, false, false);
					}
				}
			}

            var exportFilePath = $"{exportFolder}sky.gltf";
            gltfWriter.WriteAssetToFile(exportFilePath, true);
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
