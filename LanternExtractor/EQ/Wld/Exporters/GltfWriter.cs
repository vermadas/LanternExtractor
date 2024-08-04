﻿using GlmSharp;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.EQ.Wld.Helpers;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WldColor = LanternExtractor.EQ.Wld.DataTypes.Color;
using Animation = LanternExtractor.EQ.Wld.DataTypes.Animation;
using System.Drawing.Imaging;
using LanternExtractor.Infrastructure.Logger;
using LanternExtractor.Infrastructure;
using SharpGLTF.Transforms;

namespace LanternExtractor.EQ.Wld.Exporters
{
    public class GltfWriter : TextAssetWriter
    {
        public enum GltfExportFormat
        {
            /// <summary>
            /// Separate .gltf json file, .bin binary, and images externally referenced
            /// </summary>
            GlTF = 0,
            /// <summary>
            /// One binary file with json metadata and images packaged within
            /// </summary>
            Glb = 1
        }

        public enum ModelGenerationMode
        {
            /// <summary>
            /// Combine all meshes
            /// </summary>
            Combine = 0,
            /// <summary>
            /// Every mesh remains separated
            /// </summary>
            Separate = 1
        }

        public IDictionary<string, MaterialBuilder> Materials { get; private set; }

        private readonly bool _exportVertexColors;
        private readonly GltfExportFormat _exportFormat = GltfExportFormat.GlTF;
        private readonly ILogger _logger;

        #region Constants
        private static readonly float MaterialRoughness = 0.9f;
        private static readonly string MaterialInvisName = "Invis";
        private static readonly string MaterialBlankName = "Blank";
        private static readonly string DefaultModelPoseAnimationKey = "pos";
        private static readonly ISet<ShaderType> ShaderTypesThatNeedAlphaAddedToImage =
            new HashSet<ShaderType>()
            {
                ShaderType.Transparent25,
                ShaderType.Transparent50,
                ShaderType.Transparent75,
                ShaderType.TransparentAdditive,
                ShaderType.TransparentAdditiveUnlit,
                ShaderType.TransparentAdditiveUnlitSkydome,
                ShaderType.TransparentSkydome
            };
        private static readonly ISet<string> ImagesInZoneFoundInObj = new HashSet<string>()
        {
            "canwall",
            "maywall",
            "kruphse3"
        };
        private static readonly ISet<string> LoopedAnimationKeys = new HashSet<string>()
        {
             "pos", // name is used for animated objects
             "p01", // Stand
             "l01", // Walk
             "l02", // Run
             "l05", // falling
             "l06", // crouch walk
             "l07", // climbing
             "l09", // swim treading
             "p03", // rotating
             "p06", // swim
             "p07", // sitting
             "p08", // stand (arms at sides)
             "sky"
        };

        private static readonly IDictionary<string, string> AnimationDescriptions = new Dictionary<string, string>()
        {
            {"c01", "Combat Kick"},
            {"c02", "Combat Piercing"},
            {"c03", "Combat 2H Slash"},
            {"c04", "Combat 2H Blunt"},
            {"c05", "Combat Throwing"},
            {"c06", "Combat 1H Slash Left"},
            {"c07", "Combat Bash"},
            {"c08", "Combat Hand to Hand"},
            {"c09", "Combat Archery"},
            {"c10", "Combat Swim Attack"},
            {"c11", "Combat Round Kick"},
            {"d01", "Damage 1"},
            {"d02", "Damage 2"},
            {"d03", "Damage from Trap"},
            {"d04", "Drowning_Burning"},
            {"d05", "Dying"},
            {"l01", "Walk"},
            {"l02", "Run"},
            {"l03", "Jump (Running)"},
            {"l04", "Jump (Standing)"},
            {"l05", "Falling"},
            {"l06", "Crouch Walk"},
            {"l07", "Climbing"},
            {"l08", "Crouching"},
            {"l09", "Swim Treading"},
            {"o01", "Idle"},
            {"s01", "Cheer"},
            {"s02", "Mourn"},
            {"s03", "Wave"},
            {"s04", "Rude"},
            {"s05", "Yawn"},
            {"p01", "Stand"},
            {"p02", "Sit_Stand"},
            {"p03", "Shuffle Feet"},
            {"p04", "Float_Walk_Strafe"},
            {"p05", "Kneel"},
            {"p06", "Swim"},
            {"p07", "Sitting"},
            {"t01", "UNUSED????"},
            {"t02", "Stringed Instrument"},
            {"t03", "Wind Instrument"},
            {"t04", "Cast Pull Back"},
            {"t05", "Raise and Loop Arms"},
            {"t06", "Cast Push Forward"},
            {"t07", "Flying Kick"},
            {"t08", "Rapid Punches"},
            {"t09", "Large Punch"},
            {"s06", "Nod"},
            {"s07", "Amazed"},
            {"s08", "Plead"},
            {"s09", "Clap"},
            {"s10", "Distress"},
            {"s11", "Blush"},
            {"s12", "Chuckle"},
            {"s13", "Burp"},
            {"s14", "Duck"},
            {"s15", "Look Around"},
            {"s16", "Dance"},
            {"s17", "Blink"},
            {"s18", "Glare"},
            {"s19", "Drool"},
            {"s20", "Kneel"},
            {"s21", "Laugh"},
            {"s22", "Point"},
            {"s23", "Ponder"},
            {"s24", "Ready"},
            {"s25", "Salute"},
            {"s26", "Shiver"},
            {"s27", "Tap Foot"},
            {"s28", "Bow"},
            {"p08", "Stand (Arms at Sides)"},
            {"o02", "Idle (Arms at Sides)"},
            {"o03", "Idle (Sitting)"},
            {"pos", "Default"},
            {"drf", "Pose"}
        };
        private static readonly IDictionary<string, int> AnimatedDoorObjectOpenTypes = new Dictionary<string, int>()
        {
            {"akalight4gem", 100},
            {"gmspin", 100},
            {"moltglob100", 100},
            {"mooglob100", 100},
            {"nerjewel", 100},
            {"norglob100", 100},
            {"qeylamp", 100},
            {"sblight101", 100},
            {"shaft", 100},
            {"slff200", 100},
            {"airwmbld", 105},
            {"airwmblds", 105},
            {"akawheel", 105},
            {"wmblade", 105}
        };

        private readonly float ZoneScaleMultiplier;
        private readonly Matrix4x4 CorrectedWorldMatrix;
        #endregion

        private SceneBuilder _scene;
        private IMeshBuilder<MaterialBuilder> _combinedMeshBuilder;
        private ISet<string> _meshMaterialsToSkip;
        private IDictionary<string, IMeshBuilder<MaterialBuilder>> _sharedMeshes;
        private IDictionary<string, List<NodeBuilder>> _skeletons;
        private IDictionary<string, List<(string, string)>> _skeletonChildrenAttachBones;
        private bool _separateTwoFacedTriangles;

        public GltfWriter(Settings settings, GltfExportFormat exportFormat, ILogger logger)
        {
            _exportVertexColors = settings.ExportGltfVertexColors;
            _exportFormat = exportFormat;
            _logger = logger;
            ZoneScaleMultiplier = settings.ExportZoneScale;
            CorrectedWorldMatrix = Matrix4x4.CreateScale(ZoneScaleMultiplier);
            Materials = new Dictionary<string, MaterialBuilder>();
            _meshMaterialsToSkip = new HashSet<string>();
            _skeletons = new Dictionary<string, List<NodeBuilder>>();
            _skeletonChildrenAttachBones = new Dictionary<string, List<(string, string)>>();
            _sharedMeshes = new Dictionary<string, IMeshBuilder<MaterialBuilder>>();
            _separateTwoFacedTriangles = settings.SeparateTwoFacedTriangles;
            _scene = new SceneBuilder();
        }

        public override void AddFragmentData(WldFragment fragment)
        {
            AddFragmentData(
                mesh:(Mesh)fragment,
                generationMode:ModelGenerationMode.Separate );
        }

        public void AddFragmentData(Mesh mesh, SkeletonHierarchy skeleton, int singularBoneIndex = -1, 
            ICharacterModel characterModel = null, string parentSkeletonName = null, string parentSkeletonAttachBoneName = null, 
            string meshNameOverride = null, bool usesMobPieces = false )
        {
            if (!_skeletons.ContainsKey(skeleton.ModelBase))
            {
                AddNewSkeleton(skeleton, parentSkeletonName, parentSkeletonAttachBoneName);
            }

            AddFragmentData(
                mesh: mesh, 
                generationMode: ModelGenerationMode.Combine, 
                isSkinned: true, 
                meshNameOverride: meshNameOverride, 
                singularBoneIndex: singularBoneIndex,
                usesMobPieces: usesMobPieces,
                characterModel: characterModel);
        }

        public void CopyMaterialList(GltfWriter gltfWriter)
        {
            Materials = gltfWriter.Materials;
        }

        public void GenerateGltfMaterials(IEnumerable<MaterialList> materialLists, string textureImageFolder, bool loadVariants = false)
        {
            if (!Materials.Any())
            {
                Materials.Add(MaterialBlankName, GetBlankMaterial());
            }

            foreach (var materialList in materialLists)
            {
                if (materialList == null) continue;

                foreach (var eqMaterial in materialList.Materials)
                {
                    CreateGltfMaterialFromEqMaterial(eqMaterial, textureImageFolder);
                    if (loadVariants)
                    {
                        foreach (var variantMaterial in materialList.GetMaterialVariants(eqMaterial, _logger).Where(m => m != null))
                        {
                            CreateGltfMaterialFromEqMaterial(variantMaterial, textureImageFolder);
                        }
                    }
                }
            }
        }

        public void AddRegionData(Mesh mesh, DataTypes.BspNode frag)
        {
            var meshBuilder = new MeshBuilder<VertexPositionNormal>(mesh.Name);
            var materialBuilder = new MaterialBuilder("");
            var meshHelper = new WldMeshHelper(mesh, _separateTwoFacedTriangles, VT.SwapYandZ, VT.NegateX);
            var polygonCount = mesh.MaterialGroups.Sum(material => material.PolygonCount);

            for (var i = 0; i < polygonCount; i++)
            {
                var triangle = meshHelper.GetTriangle(i);
                var vertexPositions = meshHelper.GetVertexPositions(triangle);
                var prim = meshBuilder.UsePrimitive(materialBuilder);
 
                prim.AddTriangle(
                    new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(VectorTransformer.Scale(vertexPositions.v2, ZoneScaleMultiplier))),
                    new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(VectorTransformer.Scale(vertexPositions.v1, ZoneScaleMultiplier))),
                    new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(VectorTransformer.Scale(vertexPositions.v0, ZoneScaleMultiplier)))
                    );
            }

            var regionMetadata = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
            regionMetadata.Add("regions", frag?.Region?.RegionType?.RegionTypes ?? new List<DataTypes.RegionType>());

            if (frag?.Region?.RegionType?.Zoneline != null)
            {
                regionMetadata.Add("zoneLine", frag.Region.RegionType.Zoneline);
            }
            meshBuilder.Extras = JsonNode.Parse(JsonSerializer.Serialize(regionMetadata));
            _scene.AddRigidMesh(meshBuilder, new AffineTransform(Matrix4x4.Identity));
        }

        public void AddFragmentData(
            Mesh mesh,
            ModelGenerationMode generationMode,
            bool isSkinned = false,
            string meshNameOverride = null,
            int singularBoneIndex = -1, 
            ObjInstance objectInstance = null, 
            int instanceIndex = 0,
            bool isZoneMesh = false,
            bool usesMobPieces = false,
            ICharacterModel characterModel = null)
        {
            var meshName = meshNameOverride ?? FragmentNameCleaner.CleanName(mesh);
            var transformMatrix = objectInstance == null ? Matrix4x4.Identity : objectInstance.Transform;
            transformMatrix = transformMatrix *= isZoneMesh ? CorrectedWorldMatrix : Matrix4x4.Identity;

            var canExportVertexColors = _exportVertexColors &&
                ((objectInstance?.Colors?.Colors != null && objectInstance.Colors.Colors.Any())
                || (mesh?.Colors != null && mesh.Colors.Any()));
            
            if (mesh.AnimatedVerticesReference == null && !canExportVertexColors && objectInstance != null && 
                _sharedMeshes.TryGetValue(meshName, out var existingMesh))
            {
                if (generationMode == ModelGenerationMode.Separate)
                {
                    _scene.AddRigidMesh(existingMesh, new AffineTransform(transformMatrix));
                }
                return;
            }

            IMeshBuilder<MaterialBuilder> gltfMesh;

            if (objectInstance != null && (canExportVertexColors || mesh.AnimatedVerticesReference != null))
            {
                meshName += $".{instanceIndex:00}";
            }
            if (generationMode == ModelGenerationMode.Combine)
            {
                if (_combinedMeshBuilder == null)
                {
                    _combinedMeshBuilder = InstantiateMeshBuilder(meshName, isSkinned, canExportVertexColors, mesh.AnimatedVerticesReference != null);
                }
                gltfMesh = _combinedMeshBuilder;
            }
            else
            {
                gltfMesh = InstantiateMeshBuilder(meshName, isSkinned, canExportVertexColors, mesh.AnimatedVerticesReference != null);
            }

            // Keeping track of vertex indexes for each vertex position in case it's an
            // animated mesh so we can create morph targets later
            var gltfVertexPositionToWldVertexIndex = new Dictionary<VertexPositionNormal, int>();

            var polygonIndex = 0;
            var vectorTranslators = new VectorTransformer.VectorTranslator<Vector3>[2] { VT.SwapYandZ, VT.NegateZ };
            if (isZoneMesh)
            {
                vectorTranslators[1] = VT.NegateX;
            }
            var meshHelper = new WldMeshHelper(mesh, _separateTwoFacedTriangles, vectorTranslators);
            foreach (var materialGroup in mesh.MaterialGroups)
            {
                var material = mesh.MaterialList.Materials[materialGroup.MaterialIndex];
                Color? baseColor = null;
                if (characterModel != null)
                {
                    if (characterModel.TryGetMaterialVariation(material.GetFirstBitmapNameWithoutExtension(), out var variationIndex, out var color))
                    {
                        var alternateSkins = mesh.MaterialList.GetMaterialVariants(material, _logger);
                        if (alternateSkins.Any() && alternateSkins.Count > variationIndex && alternateSkins.ElementAt(variationIndex) != null)
                        {
                            material = alternateSkins[variationIndex];
                        }
                    }
                    baseColor = color;
                }
                var materialName = GetMaterialName(material);

                if (_meshMaterialsToSkip.Contains(materialName) || (characterModel != null && characterModel.ShouldSkipMeshGenerationForMaterial(materialName)))
                {
                    polygonIndex += materialGroup.PolygonCount;
                    continue;
                }

                if (!Materials.TryGetValue(materialName, out var gltfMaterial))
                {
                    gltfMaterial = Materials[MaterialBlankName];
                }

                if (baseColor != null)
                {
                    gltfMaterial = gltfMaterial.WithBaseColor(VectorTransformer.DecodeRgbToLinear(VectorTransformer.TranslateColor(baseColor.Value)));
                }

                var primitive = gltfMesh.UsePrimitive(gltfMaterial);
                for (var i = 0; i < materialGroup.PolygonCount; ++i)
                {
                    IDictionary<VertexPositionNormal, int> triangleGtlfVpToWldVi;
                    if (!canExportVertexColors && !isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexTexture1, VertexEmpty>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance, isZoneMesh);
                    }
                    else if (!canExportVertexColors && isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexTexture1, VertexJoints4>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance, isZoneMesh);
                    }
                    else if (canExportVertexColors && !isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance, isZoneMesh);
                    }
                    else //(canExportVertexColors && isSkinned)
                    {
                        triangleGtlfVpToWldVi = AddTriangleToMesh<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>
                            (primitive, meshHelper, polygonIndex++, canExportVertexColors, isSkinned, singularBoneIndex, usesMobPieces, objectInstance, isZoneMesh);
                    }
                    triangleGtlfVpToWldVi.ToList().ForEach(kv => gltfVertexPositionToWldVertexIndex[kv.Key] = kv.Value);
                }

                meshHelper.Reset();
            }

            if (generationMode == ModelGenerationMode.Separate)
            {
                if (mesh.AnimatedVerticesReference != null)
                {
                    AddAnimatedMeshMorphTargets(mesh, gltfMesh, meshName, transformMatrix, gltfVertexPositionToWldVertexIndex, isZoneMesh);
                    // mesh added to scene in ^ method
                }
                else if (AnimatedDoorObjectOpenTypes.TryGetValue(meshName, out var openType))
                {
                    var node = GetDoorAnimationNodeForOpenType(openType, meshName, transformMatrix);
                    _scene.AddRigidMesh(gltfMesh, node);
                }
                else
                {
                    _scene.AddRigidMesh(gltfMesh, new AffineTransform(transformMatrix));
                    _sharedMeshes[meshName] = gltfMesh;
                }
            }
        }

        public void ApplyAnimationToSkeleton(SkeletonHierarchy skeleton, string animationKey, bool isCharacterAnimation, bool staticPose, int? instanceIndex = null)
        {
            if (isCharacterAnimation && !staticPose && animationKey == DefaultModelPoseAnimationKey) return;

            var skeletonName = GetSkeletonName(skeleton, instanceIndex);
            if (!_skeletons.TryGetValue(skeletonName, out var skeletonNodes))
            {
                skeletonNodes = AddNewSkeleton(skeleton);
            }
            var animation = skeleton.Animations[animationKey];
            var trackArray = isCharacterAnimation ? animation.TracksCleanedStripped : animation.TracksCleaned;
            var poseArray = isCharacterAnimation
                ? skeleton.Animations[DefaultModelPoseAnimationKey].TracksCleanedStripped
                : skeleton.Animations[DefaultModelPoseAnimationKey].TracksCleaned;

            if (poseArray == null) return;
            var hasChildren = _skeletonChildrenAttachBones.TryGetValue(skeleton.ModelBase, out var skeletonChildrenAttachBones);
            for (var i = 0; i < skeleton.Skeleton.Count; i++)
            {
                var boneName = isCharacterAnimation
                    ? Animation.CleanBoneAndStripBase(skeleton.BoneMapping[i], skeleton.ModelBase)
                    : Animation.CleanBoneName(skeleton.BoneMapping[i]);

                if (staticPose || !trackArray.ContainsKey(boneName))
                {
                    if (!poseArray.ContainsKey(boneName)) return;

                    var poseTransform = poseArray[boneName].TrackDefFragment.Frames[0];
                    if (poseTransform == null) return;

                    ApplyBoneTransformation(skeletonNodes[i], poseTransform, animationKey, 0, staticPose);
                    if (hasChildren && skeletonChildrenAttachBones.Where(c => c.Item2 == boneName).Any())
                    {
                        foreach (var child in skeletonChildrenAttachBones.Where(c => c.Item2 == boneName))
                        {
                            var childSkeleton = _skeletons[child.Item1];
                            foreach (var childBone in childSkeleton)
                            {
                                ApplyBoneTransformation(childBone, poseTransform, animationKey, 0, staticPose);
                            }
                        }
                    }
                    continue;
                }

                var totalTimeForBone = 0;
                for (var frame = 0; frame < animation.FrameCount; frame++)
                {
                    if (frame >= trackArray[boneName].TrackDefFragment.Frames.Count) break;

                    var boneTransform = trackArray[boneName].TrackDefFragment.Frames[frame];

                    ApplyBoneTransformation(skeletonNodes[i], boneTransform, animationKey, totalTimeForBone, staticPose);
                    if (frame == 0 && LoopedAnimationKeys.Contains(animationKey))
                    {
                        ApplyBoneTransformation(skeletonNodes[i], boneTransform, animationKey, animation.AnimationTimeMs, staticPose);
                    }
                    if (hasChildren && skeletonChildrenAttachBones.Where(c => c.Item2 == boneName).Any())
                    {
                        foreach (var child in skeletonChildrenAttachBones.Where(c => c.Item2 == boneName))
                        {
                            var childSkeleton = _skeletons[child.Item1];
                            ApplyBoneTransformation(childSkeleton[0], boneTransform, animationKey, 0, staticPose);
                        }
                    }

                    totalTimeForBone += isCharacterAnimation ?
                        (animation.AnimationTimeMs / animation.FrameCount) :
                        skeleton.Skeleton[i].Track.FrameMs;
                }
            }
        }

        public void AddLightInstances(IEnumerable<LightInstance> lightInstances, float lightIntensityMultiplier)
        {
            var groupedLightInstances = lightInstances.GroupBy(i => new UniqueLight(i));

            foreach (var uniqueLightGroups in groupedLightInstances)
            {
                var uniqueLight = uniqueLightGroups.Key;
                var light = new LightBuilder.Point()
                {
                    Color = uniqueLight.Color,
                    Intensity = uniqueLight.Radius * lightIntensityMultiplier
                    // Range = uniqueLight.Radius * ZoneScaleMultiplier
                };
                foreach (var lightInstance in uniqueLightGroups)
                {
                    var position = VectorTransformer.TranslateVector(lightInstance.Position, VT.SwapYandZ, VT.NegateX);
                    var translationMatrix = Matrix4x4.CreateTranslation(position) * CorrectedWorldMatrix;
                    Matrix4x4.Decompose(translationMatrix, out _, out _, out var translation);
                    var lightName = lightInstance.LightReference?.LightSource?.Name;
                    var node = new NodeBuilder(lightName != null ? lightName.Split('_')[0] : "");
                    // node.WithTranslation(translation) makes VS complain for some reason
                    node.LocalTransform = node.LocalTransform.WithTranslation(translation);
                    _scene.AddLight(light, node);
                }
            }
        }

        public void AddCombinedMeshToScene(
            bool isZoneMesh = false, 
            string meshName = null, 
            string skeletonModelBase = null, 
            ObjInstance objectInstance = null,
            int? instanceIndex = null)
        {
            IMeshBuilder<MaterialBuilder> combinedMesh;
            if (meshName != null && _sharedMeshes.TryGetValue(meshName, out var existingMesh))
            {
                combinedMesh = existingMesh;
            }
            else
            {
                combinedMesh = _combinedMeshBuilder;
            }
            if (combinedMesh == null) return;

            var skeletonName = GetSkeletonName(skeletonModelBase, instanceIndex);
            
            var worldTransformMatrix = Matrix4x4.Identity;
            if (objectInstance != null && skeletonName == null)
            {
                worldTransformMatrix *= objectInstance.Transform;
                worldTransformMatrix *= CorrectedWorldMatrix;
            }
            else if (isZoneMesh)
            {
                worldTransformMatrix *= CorrectedWorldMatrix;
            }

            if (skeletonName == null || !_skeletons.TryGetValue(skeletonName, out var skeletonNodes))
            {
                _scene.AddRigidMesh(combinedMesh, new AffineTransform(worldTransformMatrix));
            }
            else
            {
                if (_skeletonChildrenAttachBones.TryGetValue(skeletonName, out var children))
                {
                    foreach (var child in children)
                    {
                        skeletonNodes.AddRange(_skeletons[child.Item1]);
                    }
                }
                if (objectInstance != null)
                {
                    worldTransformMatrix = skeletonNodes[0].Parent.WorldMatrix;
                }

                _scene.AddSkinnedMesh(combinedMesh, worldTransformMatrix, skeletonNodes.ToArray());       
            }

            if (meshName != null && !_sharedMeshes.ContainsKey(meshName))
            {
                _sharedMeshes.Add(meshName, combinedMesh);
            }
            _combinedMeshBuilder = null;
        }

        public override void WriteAssetToFile(string fileName)
        {
            WriteAssetToFile(fileName, false);
        }

        public void WriteAssetToFile(string fileName, bool useExistingImages, string skeletonModelBase = null, bool cleanupTexturesFolder = false)
        {
            AddCombinedMeshToScene(false, null, skeletonModelBase);

            var outputFilePath = FixFilePath(fileName);
            var model = _scene.ToGltf2();

            foreach (var node in model.LogicalNodes)
            {
                if (node.Mesh?.Extras != null)
                {
                    node.Extras = node.Mesh.Extras;
                    var reg = node.Extras["regions"].AsArray().Select(a => a.GetValue<int>().ToString());
                    node.Name = $"region_{string.Join("-", reg)}";
                }
            }

            if (_exportFormat == GltfExportFormat.GlTF)
            {
                if (!useExistingImages)
                {
                    // Don't rename the image files
                    var writeSettings = new SharpGLTF.Schema2.WriteSettings()
                    {
                        JsonIndented = true,
                        ImageWriteCallback = (context, uri, image) =>
                        {
                            var imageSourcePath = image.SourcePath;
                            var imageFileName = Path.GetFileName(imageSourcePath);
                            // Save image to same path as the .gltf
                            var newImagePath = Path.Combine(Path.GetDirectoryName(fileName), imageFileName);
                            image.SaveToFile(newImagePath);

                            return imageFileName;
                        }
                    };
                    model.SaveGLTF(outputFilePath, writeSettings);
                }
                else
                {
                    var writeSettings = new SharpGLTF.Schema2.WriteSettings()
                    {
                        JsonIndented = true,
                        ImageWriteCallback = (context, uri, image) =>
                        {
                            var imageSourcePath = image.SourcePath;
                            return $"Textures/{Path.GetFileName(imageSourcePath)}";
                        }
                    };
                    model.SaveGLTF(outputFilePath, writeSettings);
                }
            }
            else // Glb
            {
                model.SaveGLB(outputFilePath);
            }
            if (cleanupTexturesFolder)
            {
                var outputFolder = Path.GetDirectoryName(outputFilePath);
                Directory.Delete(Path.Combine(outputFolder, "Textures"), true);
            }
        }

        public List<NodeBuilder> AddNewSkeleton(SkeletonHierarchy skeleton, string parent = null, string attachBoneName = null, ObjInstance objInstance = null, int? instanceIndex = null)
        {
            var skeletonNodes = new List<NodeBuilder>();
            var duplicateNameDictionary = new Dictionary<string, int>();
            var skeletonName = GetSkeletonName(skeleton, instanceIndex);
            var boneNamePrefix = instanceIndex != null ? $"{skeletonName}_" : "";
            foreach (var bone in skeleton.Skeleton)
            {
                var boneName = bone.CleanedName;
                if (duplicateNameDictionary.TryGetValue(boneName, out var count))
                {
                    skeletonNodes.Add(new NodeBuilder($"{boneNamePrefix}{boneName}_{count:00}"));
                    duplicateNameDictionary[boneName] = ++count;
                }
                else
                {
                    skeletonNodes.Add(new NodeBuilder($"{boneNamePrefix}{boneName}"));
                    duplicateNameDictionary.Add(boneName, 0);
                }
            }
            if (objInstance != null)
            {
                var rootNode = GetRootSkeletonNodeTransformsFromObjectInstance(skeletonName, objInstance);
                rootNode.AddNode(skeletonNodes[0]);
            }
            for (var i = 0; i < skeletonNodes.Count; i++)
            {
                var node = skeletonNodes[i];
                var bone = skeleton.Skeleton[i];
                bone.Children.ForEach(b => node.AddNode(skeletonNodes[b]));
            }
            if (parent != null && attachBoneName != null)
            {
                if (!_skeletons.TryGetValue(parent, out var parentSkeleton))
                {
                    throw new InvalidOperationException($"Cannot attach child skeleton to parent: {parent}. It does not exist");
                }
                var attachBone = parentSkeleton
                    .Where(n => n.Name.Equals(attachBoneName, StringComparison.InvariantCultureIgnoreCase))
                    .SingleOrDefault();
                if (attachBone == null)
                {
                    throw new InvalidOperationException($"Cannot attach child skeleton to parent: {parent} at bone {attachBoneName}. Bone does not exist");
                }
                attachBone.AddNode(skeletonNodes[0]);

                if (!_skeletonChildrenAttachBones.ContainsKey(parent))
                {
                    _skeletonChildrenAttachBones.Add(parent, new List<(string, string)>());
                }
                _skeletonChildrenAttachBones[parent].Add((skeleton.ModelBase, attachBoneName));
            }
            _skeletons.Add(skeletonName, skeletonNodes);
            return skeletonNodes;
        }

        public override void ClearExportData()
        {
            _scene = null;
            _scene = new SceneBuilder();
            Materials.Clear();
            _sharedMeshes.Clear();
            _skeletons.Clear();
            _meshMaterialsToSkip.Clear();
        }

        public new int GetExportByteCount() => 0;

        private void CreateGltfMaterialFromEqMaterial(Material eqMaterial, string textureImageFolder)
        {
            var materialName = GetMaterialName(eqMaterial);

            if (Materials.ContainsKey(materialName)) return;

            if (eqMaterial.ShaderType == ShaderType.Boundary)
            {
                _meshMaterialsToSkip.Add(materialName);
                return;
            }
            if (eqMaterial.ShaderType == ShaderType.Invisible)
            {
                Materials.Add(materialName, GetInvisibleMaterial());
                return;
            }

            var imageFileNameWithoutExtension = eqMaterial.GetFirstBitmapNameWithoutExtension();
            if (string.IsNullOrEmpty(imageFileNameWithoutExtension)) return;

            var imagePath = Path.Combine(textureImageFolder, eqMaterial.GetFirstBitmapExportFilename());

            if (!File.Exists(imagePath))
            {
                if (ImagesInZoneFoundInObj.Contains(imageFileNameWithoutExtension))
                {
                    _logger.LogWarning($"Zone material: {materialName} image {imageFileNameWithoutExtension}.png not availible until _obj is processed! Manual correction required.");
                }
                else
                {
                    _logger.LogError($"Material: {materialName} image not found at '{imagePath}'!");
                }
                Materials.Add(materialName, GetBlankMaterial(materialName));
            }

            ImageBuilder imageBuilder;
            if (ShaderTypesThatNeedAlphaAddedToImage.Contains(eqMaterial.ShaderType))
            {
                // Materials with these shaders need new images with an alpha channel included to look correct
                // Not a fan of having to write new images during the generation phase, but SharpGLTF
                // needs the image bytes, and if we want to keep the original images we need to use the
                // ImageWriteCallback, and within that callback we only have access to the path the image
                // was loaded from, and that can only be set by loading an image via a path. I can't
                // even write the images to a temp folder since I won't be able to get the correct Textures
                // folder path within the callback to write the image
                var convertedImagePath = ImageAlphaConverter.AddAlphaToImage(imagePath, eqMaterial.ShaderType);
                var newImageName = Path.GetFileNameWithoutExtension(convertedImagePath);
                imageBuilder = ImageBuilder.From(new MemoryImage(convertedImagePath), newImageName);

                // No support for animated textures, but in case the user wishes to add the frames
                // somehow in post-process, add alpha to all frames of the animated texture
                if ((eqMaterial.BitmapInfoReference?.BitmapInfo.AnimationDelayMs ?? 0) > 0)
                {
                    var animationFrameBitmaps = eqMaterial.BitmapInfoReference.BitmapInfo.BitmapNames;
                    for (var i = 1; i < animationFrameBitmaps.Count(); i++)
                    {
                        var frameImageFile = animationFrameBitmaps[i].GetExportFilename();
                        var frameImageFilePath = Path.Combine(textureImageFolder, frameImageFile);
                        ImageAlphaConverter.AddAlphaToImage(frameImageFilePath, eqMaterial.ShaderType);
                    }
                }
            }
            else
            {
                var imageName = Path.GetFileNameWithoutExtension(imagePath);
                imageBuilder = ImageBuilder.From(new MemoryImage(imagePath), imageName);
            }

            var gltfMaterial = new MaterialBuilder(materialName)
                .WithDoubleSide(eqMaterial.IsTwoSided)
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.RoughnessFactor, MaterialRoughness)
                .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.MetallicFactor, 0f)
                .WithChannelParam(KnownChannel.SpecularFactor, KnownProperty.SpecularFactor, 0f); // Helps models look better in some renderers.
            // If we use the method below, the image name is not retained
            //    .WithChannelImage(KnownChannel.BaseColor, $"{textureImageFolder}{eqMaterial.GetFirstBitmapExportFilename()}");
            gltfMaterial.UseChannel(KnownChannel.BaseColor)
                .UseTexture()
                .WithPrimaryImage(imageBuilder);

            switch (eqMaterial.ShaderType)
            {
                case ShaderType.TransparentMasked:
                    gltfMaterial.WithAlpha(AlphaMode.MASK, 0.5f);
                    break;
                case ShaderType.Transparent25:
                case ShaderType.Transparent50:
                case ShaderType.Transparent75:
                case ShaderType.TransparentAdditive:
                case ShaderType.TransparentAdditiveUnlit:
                case ShaderType.TransparentSkydome:
                case ShaderType.TransparentAdditiveUnlitSkydome:
                    gltfMaterial.WithAlpha(AlphaMode.BLEND);
                    break;
                default:
                    gltfMaterial.WithAlpha(AlphaMode.OPAQUE);
                    break;
            }

            if (eqMaterial.ShaderType == ShaderType.TransparentAdditiveUnlit ||
                eqMaterial.ShaderType == ShaderType.DiffuseSkydome ||
                eqMaterial.ShaderType == ShaderType.TransparentAdditiveUnlitSkydome)
            {
                gltfMaterial.WithUnlitShader();
            }

            Materials.Add(materialName, gltfMaterial);
        }

        private string GetSkeletonName(SkeletonHierarchy skeleton, int? instanceIndex = null)
        {
            if (skeleton == null) return null;

            return GetSkeletonName(skeleton.ModelBase, instanceIndex);
        }

        private string GetSkeletonName(string skeletonModelBase, int? instanceIndex = null)
        {
            if (skeletonModelBase == null) return null;

            if (instanceIndex != null) return $"{skeletonModelBase}_{instanceIndex:000}";
 
            return skeletonModelBase;
        }

        private NodeBuilder GetRootSkeletonNodeTransformsFromObjectInstance(string name, ObjInstance instance)
        {
            var rootNode = new NodeBuilder(name);
            var zoneInstanceTransformMatrix = instance.Transform * CorrectedWorldMatrix;
            Matrix4x4.Decompose(zoneInstanceTransformMatrix, out var scale, out var rotation, out var translation);
            rotation = Quaternion.Normalize(rotation);
            rootNode.WithLocalScale(scale)
                .WithLocalRotation(rotation)
                .WithLocalTranslation(translation);

            return rootNode;
        }

        private string GetMaterialName(Material eqMaterial)
        {
            return $"{MaterialList.GetMaterialPrefix(eqMaterial.ShaderType)}{eqMaterial.GetFirstBitmapNameWithoutExtension()}";
        }

        private MaterialBuilder GetBlankMaterial(string name = null)
        {
            var materialName = name ?? MaterialBlankName;
            return new MaterialBuilder(materialName)
                .WithDoubleSide(false)
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1));
        }

        private MaterialBuilder GetInvisibleMaterial()
        {
            return new MaterialBuilder(MaterialInvisName)
                .WithDoubleSide(false)
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 0))
                .WithAlpha(AlphaMode.MASK);
        }

        private IMeshBuilder<MaterialBuilder> InstantiateMeshBuilder(string meshName, bool isSkinned = false, bool canExportVertexColors = false, bool hasAnimatedVertices = false)
        {
            var meshBuilderType = typeof(MeshBuilder<,,>).MakeGenericType(
                typeof(VertexPositionNormal),
                canExportVertexColors ? typeof(VertexColor1Texture1) : typeof(VertexTexture1),
                isSkinned ? typeof(VertexJoints4) : typeof(VertexEmpty));

            // BEGIN workaround for SharpGLTF morphing bug until fixed in nuget package - https://github.com/vpenades/SharpGLTF/issues/243

            var meshBuilder = (IMeshBuilder<MaterialBuilder>)Activator.CreateInstance(meshBuilderType, meshName);

            if (!hasAnimatedVertices) return meshBuilder;

            if (!isSkinned && !canExportVertexColors)
            {
                ((MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>)meshBuilder).VertexPreprocessor.Clear();
            }
            else if (!isSkinned && canExportVertexColors)
            {
                ((MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>)meshBuilder).VertexPreprocessor.Clear();
            }
            else if (isSkinned && !canExportVertexColors)
            {
                ((MeshBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>)meshBuilder).VertexPreprocessor.Clear();
            }
            else
            {
                ((MeshBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>)meshBuilder).VertexPreprocessor.Clear();
            }
            // END workaround

            return meshBuilder;
        }

        private IDictionary<VertexPositionNormal,int> AddTriangleToMesh<TvG, TvM, TvS>(
            IPrimitiveBuilder primitive, WldMeshHelper meshHelper,
            int polygonIndex, bool canExportVertexColors, bool isSkinned,
            int singularBoneIndex = -1, bool usesMobPieces = false, ObjInstance objectInstance = null, bool isZoneMesh = false)
                where TvG : struct, IVertexGeometry
                where TvM : struct, IVertexMaterial
                where TvS : struct, IVertexSkinning
        {
            var triangle = meshHelper.GetTriangle(polygonIndex);
            var vertexPositions = meshHelper.GetVertexPositions(triangle);
            var vertexNormals = meshHelper.GetVertexNormals(triangle);
            var vertexUvs = meshHelper.GetVertexUvs(triangle);
            var boneIndexes = meshHelper.GetBoneIndexes(triangle, isSkinned, usesMobPieces, singularBoneIndex);
            var vertexColors = meshHelper.GetVertexColorVectors(triangle, canExportVertexColors, objectInstance);

            var vertex0 = GetGltfVertex<TvG, TvM, TvS>(vertexPositions.v0, vertexNormals.v0, vertexUvs.v0, vertexColors.v0, isSkinned, boneIndexes.v0);
            var vertex1 = GetGltfVertex<TvG, TvM, TvS>(vertexPositions.v1, vertexNormals.v1, vertexUvs.v1, vertexColors.v1, isSkinned, boneIndexes.v1);
            var vertex2 = GetGltfVertex<TvG, TvM, TvS>(vertexPositions.v2, vertexNormals.v2, vertexUvs.v2, vertexColors.v2, isSkinned, boneIndexes.v2);

            primitive.AddTriangle(vertex2, vertex1, vertex0);

            var gltfVpToWldVi = new Dictionary<VertexPositionNormal, int>();

            gltfVpToWldVi[new VertexPositionNormal(vertexPositions.v0, vertexNormals.v0)] = triangle.Vertex1;
            gltfVpToWldVi[new VertexPositionNormal(vertexPositions.v1, vertexNormals.v1)] = triangle.Vertex2;
            gltfVpToWldVi[new VertexPositionNormal(vertexPositions.v2, vertexNormals.v2)] = triangle.Vertex3;

            return gltfVpToWldVi;
        }

        private IVertexBuilder GetGltfVertex<TvG, TvM, TvS>(
            Vector3 position, Vector3 normal, Vector2 uv, Vector4? color, bool isSkinned, int boneIndex)
                where TvG : struct, IVertexGeometry
                where TvM : struct, IVertexMaterial
                where TvS : struct, IVertexSkinning
        {
            var exportJoints = boneIndex > -1 && isSkinned;
            IVertexBuilder vertexBuilder;
            if (color == null && !exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>
                    ((position, normal), uv);
            }
            else if (color == null && exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>
                    ((position, normal), uv, new VertexJoints4(boneIndex));
            }
            else if (color != null && !exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>
                    ((position, normal), (color.Value, uv));
            }
            else // (color != null && exportJoints)
            {
                vertexBuilder = new VertexBuilder<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>
                    ((position, normal), (color.Value, uv), new VertexJoints4(boneIndex));
            }

            return (VertexBuilder<TvG, TvM, TvS>)vertexBuilder;
        }

        private void AddAnimatedMeshMorphTargets(Mesh mesh, IMeshBuilder<MaterialBuilder> gltfMesh,
            string meshName, Matrix4x4 transformMatrix, Dictionary<VertexPositionNormal, int> gltfVertexPositionToWldVertexIndex, bool mirrorXAxis)
        {
            var frameTimes = new List<float>();
            var weights = new List<float>();
            var animatedVertices = mesh.AnimatedVerticesReference.GetAnimatedVertices();
            var frameDelay = animatedVertices.Delay/1000f;

            for (var frame = 0; frame < animatedVertices.Frames.Count; frame++)
            {
                var vertexPositionsForFrame = animatedVertices.Frames[frame];
                var morphTarget = gltfMesh.UseMorphTarget(frame);

                foreach (var vertexGeometry in gltfVertexPositionToWldVertexIndex.Keys)
                {
                    var vertexIndex = gltfVertexPositionToWldVertexIndex[vertexGeometry];
                    var wldVertexPositionForFrame = vertexPositionsForFrame[vertexIndex];
                    var newPosition = VectorTransformer.TranslateVector(wldVertexPositionForFrame + mesh.Center, VT.SwapYandZ);
                    if (mirrorXAxis)
                    {
                        newPosition = VectorTransformer.TranslateVector(newPosition, VT.NegateX);
                    }
                    vertexGeometry.TryGetNormal(out var originalNormal);
                    morphTarget.SetVertex(vertexGeometry, new VertexPositionNormal(newPosition, originalNormal));
                }
                frameTimes.Add(frame * frameDelay);
                weights.Add(1);
            }

            var node = new NodeBuilder(meshName);
            node.LocalTransform = new AffineTransform(transformMatrix);

            var instance = _scene.AddRigidMesh(gltfMesh, node);
            instance.Content.UseMorphing().SetValue(weights.ToArray());
            var track = instance.Content.UseMorphing($"Default_{mesh.Name}");
            var morphTargetElements = new float[frameTimes.Count];

            for (var i = 0; i < frameTimes.Count; i++)
            {
                Array.Clear(morphTargetElements, 0, morphTargetElements.Length);
                morphTargetElements[i] = 1;
                track.SetPoint(frameTimes[i], true, morphTargetElements);
            }
        }

        private NodeBuilder GetDoorAnimationNodeForOpenType(int openType, string meshName, Matrix4x4 transformMatrix)
        {
            // For now, this is just windmill blades (105) and some other spinning
            // objects (100) like windmill shafts and akanon lights. The only other
            // doors that automatically move are a few traps in sol A and B
            var node = new NodeBuilder(meshName);
            node.LocalTransform = new AffineTransform(transformMatrix);
            // Rotation part of the local transform is being lost with the animation -
            // extract it out and multiply it with the animation steps
            Matrix4x4.Decompose(transformMatrix, out _, out var baseRotation, out _);
            switch (openType)
            {
                case 100:
                    node.UseRotation("Default")
                        .WithPoint(0f, baseRotation)
                        .WithPoint(4.25f, baseRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -(float)Math.PI))
                        .WithPoint(8.5f, baseRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -(float)(2 * Math.PI)));
                    return node;
                case 105:
                    node.UseRotation("Default")
                        .WithPoint(0f, baseRotation)
                        .WithPoint(4.25f, baseRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI))
                        .WithPoint(8.5f, baseRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(2 * Math.PI)));
                    return node;
                default:
                    return node;
            }
        }
        private void ApplyBoneTransformation(NodeBuilder boneNode, DataTypes.BoneTransform boneTransform, 
            string animationKey, int timeMs, bool staticPose)
        {
            var scaleVector = new Vector3(boneTransform.Scale);
            var rotationQuaternion = new Quaternion()
            {
                X = (float)(boneTransform.Rotation.x * Math.PI)/180,
                Y = (float)(boneTransform.Rotation.z * Math.PI)/180,
                Z = (float)(boneTransform.Rotation.y * Math.PI * -1)/180,
                W = (float)(boneTransform.Rotation.w * Math.PI)/180
            };
            rotationQuaternion = Quaternion.Normalize(rotationQuaternion);
            var translationVector = VectorTransformer.TranslateVector(boneTransform.Translation, VT.SwapYandZ, VT.NegateZ);
            if (!AnimationDescriptions.TryGetValue(animationKey, out var animationDescription))
            {
                animationDescription = animationKey;
            }

            if (staticPose)
            {
                boneNode
                    .WithLocalScale(scaleVector)
                    .WithLocalRotation(rotationQuaternion)
                    .WithLocalTranslation(translationVector);
            }
            else
            {
                boneNode
                    .UseScale(animationDescription)
                    .WithPoint(timeMs/1000f, scaleVector);
                boneNode
                    .UseRotation(animationDescription)
                    .WithPoint(timeMs/1000f, rotationQuaternion);
                boneNode
                    .UseTranslation(animationDescription)
                    .WithPoint(timeMs/1000f, translationVector);
            }
        }

        private string FixFilePath(string filePath)
        {
            var fixedExtension = _exportFormat == GltfExportFormat.GlTF ? ".gltf" : ".glb";
            return Path.ChangeExtension(filePath, fixedExtension);
        }
    }

    public class ObjInstance
    {
        public string Name { get; private set; }
        public ObjType Type { get; private set; }
        public Matrix4x4 Transform { get; private set; }
        public VertexColors Colors { get; private set; }

        public ObjInstance(ObjectInstance objectInstanceFragment)
        {
            Name = objectInstanceFragment.ObjectName;
            var position = VectorTransformer.TranslateVector(objectInstanceFragment.Position, VT.SwapYandZ, VT.NegateX);
            var rotation = VectorTransformer.TranslateVector(objectInstanceFragment.Rotation, VT.NegateY, VT.NegateZ);
            var scale = VectorTransformer.TranslateVector(objectInstanceFragment.Scale);
            Transform = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromYawPitchRoll(
                    (float)(rotation.Z * Math.PI) / 180f,
                    (float)(rotation.X * Math.PI) / 180f,
                    (float)(rotation.Y * Math.PI) / 180f
                )
                * Matrix4x4.CreateTranslation(position);
            Colors = objectInstanceFragment.Colors;
            Type = ObjType.ZoneInstance;
        }

        public ObjInstance(Door door)
        {
            Name = door.Name;
            var position = VectorTransformer.TranslateVector(door.Position, VT.SwapXandY, VT.SwapYandZ, VT.NegateX); // (Y, Z, X)
            Transform = Matrix4x4.CreateFromYawPitchRoll(
                    (float)(door.Heading * 360d / 512d * Math.PI) / 180f,
                    0f,
                    (float)(-door.Incline * 360f / 512f * Math.PI) / 180f
                )
                * Matrix4x4.CreateTranslation(position);

            Type = ObjType.Door;
        }

        public enum ObjType
        {
            ZoneInstance = 0,
            Door = 1
        }
    }

    public class WldMeshHelper
    {
        private readonly Mesh _wldMesh;
        private readonly bool _separateTwoFacedTriangles;
        private readonly ISet<DataTypes.Polygon> _uniqueTriangles;
        private readonly IDictionary<int, Vector3> _wldVertexIndexToDuplicatedVertexNormals;
        private readonly TriangleVertexSetComparer _triangleSetComparer;
        private readonly VectorTransformer.VectorTranslator<Vector3>[] _vectorTranslators;
        private static readonly Vector4 DefaultVertexColor = new Vector4(0f, 0f, 0f, 1f); // Black

        public WldMeshHelper(Mesh wldMesh, bool separateTwoFacedTriangles, params VectorTransformer.VectorTranslator<Vector3>[] vectorTranslators)
        {
            _wldMesh = wldMesh;
            _separateTwoFacedTriangles = separateTwoFacedTriangles;
            _triangleSetComparer = new TriangleVertexSetComparer();
            _uniqueTriangles = new HashSet<DataTypes.Polygon>(_triangleSetComparer);
            _wldVertexIndexToDuplicatedVertexNormals = new Dictionary<int, Vector3>();
            _vectorTranslators = vectorTranslators;
        }

        public DataTypes.Polygon GetTriangle(int triangleIndex)
        {
            return _wldMesh.Indices[triangleIndex];
        }

        public (Vector3 v0, Vector3 v1, Vector3 v2) GetVertexPositions(DataTypes.Polygon triangle)
        {
            (Vector3 v0, Vector3 v1, Vector3 v2) vertexPositions = (
                VectorTransformer.TranslateVector(_wldMesh.Vertices[triangle.Vertex1] + _wldMesh.Center, _vectorTranslators),
                VectorTransformer.TranslateVector(_wldMesh.Vertices[triangle.Vertex2] + _wldMesh.Center, _vectorTranslators),
                VectorTransformer.TranslateVector(_wldMesh.Vertices[triangle.Vertex3] + _wldMesh.Center, _vectorTranslators));

            return vertexPositions;
        }

        public (Vector3 v0, Vector3 v1, Vector3 v2) GetVertexNormals(DataTypes.Polygon triangle)
        {
            if (_separateTwoFacedTriangles)
            {
                if (!_uniqueTriangles.Contains(triangle, _triangleSetComparer))
                {
                    _uniqueTriangles.Add(triangle);
                }
                else
                {
                    return GetDuplicateVertexNormalsForTriangle(triangle);
                }
            }

            (Vector3 v0, Vector3 v1, Vector3 v2) vertexNormals = (
                Vector3.Normalize(VectorTransformer.TranslateVector(_wldMesh.Normals[triangle.Vertex1], _vectorTranslators)),
                Vector3.Normalize(VectorTransformer.TranslateVector(_wldMesh.Normals[triangle.Vertex2], _vectorTranslators)),
                Vector3.Normalize(VectorTransformer.TranslateVector(_wldMesh.Normals[triangle.Vertex3], _vectorTranslators)));

            return vertexNormals;
        }

        public (Vector2 v0, Vector2 v1, Vector2 v2) GetVertexUvs(DataTypes.Polygon triangle)
        {
            (Vector2 v0, Vector2 v1, Vector2 v2) vertexUvs = (
                VectorTransformer.TranslateVector(_wldMesh.TextureUvCoordinates[triangle.Vertex1], VT.NegateY),
                VectorTransformer.TranslateVector(_wldMesh.TextureUvCoordinates[triangle.Vertex2], VT.NegateY),
                VectorTransformer.TranslateVector(_wldMesh.TextureUvCoordinates[triangle.Vertex3], VT.NegateY));

            return vertexUvs;
        }

        public (int v0, int v1, int v2) GetBoneIndexes(DataTypes.Polygon triangle, bool isSkinned, bool usesMobPieces, int singularBoneIndex)
        {
            (int v0, int v1, int v2) boneIndexes = (singularBoneIndex, singularBoneIndex, singularBoneIndex);
            if (isSkinned && (usesMobPieces || singularBoneIndex == -1))
            {
                var boneOffset = singularBoneIndex == -1 ? 0 : singularBoneIndex;
                boneIndexes = (
                    GetBoneIndexForVertex(triangle.Vertex1) + boneOffset,
                    GetBoneIndexForVertex(triangle.Vertex2) + boneOffset,
                    GetBoneIndexForVertex(triangle.Vertex3) + boneOffset);
            }

            return boneIndexes;
        }

        public (Vector4? v0, Vector4? v1, Vector4? v2) GetVertexColorVectors(DataTypes.Polygon triangle, bool canExportVertexColors, ObjInstance objectInstance = null)
        {
            if (!canExportVertexColors) return (null, null, null);

            var objInstanceColors = objectInstance?.Colors?.Colors ?? new List<WldColor>();
            var meshColors = _wldMesh?.Colors ?? new List<WldColor>();

            var v0Color = CoalesceVertexColor(meshColors, objInstanceColors, triangle.Vertex1);
            var v1Color = CoalesceVertexColor(meshColors, objInstanceColors, triangle.Vertex2);
            var v2Color = CoalesceVertexColor(meshColors, objInstanceColors, triangle.Vertex3);

            return (v0Color, v1Color, v2Color);
        }

        public void Reset()
        {
            _uniqueTriangles.Clear();
            _wldVertexIndexToDuplicatedVertexNormals.Clear();
        }

        private (Vector3 v0, Vector3 v1, Vector3 v2) GetDuplicateVertexNormalsForTriangle(DataTypes.Polygon triangle)
        {
            if (!_wldVertexIndexToDuplicatedVertexNormals.TryGetValue(triangle.Vertex1, out var v0Normal))
            {
                v0Normal = Vector3.Normalize(-VectorTransformer.TranslateVector(_wldMesh.Normals[triangle.Vertex1], _vectorTranslators));
                _wldVertexIndexToDuplicatedVertexNormals.Add(triangle.Vertex1, v0Normal);
            }
            if (!_wldVertexIndexToDuplicatedVertexNormals.TryGetValue(triangle.Vertex2, out var v1Normal))
            {
                v1Normal = Vector3.Normalize(-VectorTransformer.TranslateVector(_wldMesh.Normals[triangle.Vertex2], _vectorTranslators));
                _wldVertexIndexToDuplicatedVertexNormals.Add(triangle.Vertex2, v1Normal);
            }
            if (!_wldVertexIndexToDuplicatedVertexNormals.TryGetValue(triangle.Vertex3, out var v2Normal))
            {
                v2Normal = Vector3.Normalize(-VectorTransformer.TranslateVector(_wldMesh.Normals[triangle.Vertex3], _vectorTranslators));
                _wldVertexIndexToDuplicatedVertexNormals.Add(triangle.Vertex3, v2Normal);
            }

            return (v0Normal, v1Normal, v2Normal);
        }

        private int GetBoneIndexForVertex(int vertexIndex)
        {
            foreach (var indexedMobVertexPiece in _wldMesh.MobPieces)
            {
                if (vertexIndex >= indexedMobVertexPiece.Value.Start &&
                    vertexIndex < indexedMobVertexPiece.Value.Start + indexedMobVertexPiece.Value.Count)
                {
                    return indexedMobVertexPiece.Key;
                }
            }
            return 0;
        }

        private Vector4 CoalesceVertexColor(List<WldColor> meshColors, List<WldColor> objInstanceColors, int vertexIndex)
        {
            if (vertexIndex < objInstanceColors.Count)
            {
                return VectorTransformer.TranslateColor(objInstanceColors[vertexIndex]);
            }
            else if (vertexIndex < meshColors.Count)
            {
                return VectorTransformer.TranslateColor(meshColors[vertexIndex]);
            }
            else
            {
                return DefaultVertexColor;
            }
        }
    }

    public class TriangleVertexSetComparer : IEqualityComparer<DataTypes.Polygon>
    {
        public bool Equals(DataTypes.Polygon polyX, DataTypes.Polygon polyY)
        {
            var polyXSet = new HashSet<int>() { polyX.Vertex1, polyX.Vertex2, polyX.Vertex3 };
            var polyYSet = new HashSet<int>() { polyY.Vertex1, polyY.Vertex2, polyY.Vertex3 };

            return polyXSet.SetEquals(polyYSet);
        }

        public int GetHashCode(DataTypes.Polygon poly)
        {
            var polyVertList1 = new List<int>() { poly.Vertex1, poly.Vertex2, poly.Vertex3 };
            polyVertList1.Sort();

            unchecked
            {
                return 391 + polyVertList1.GetHashCode();
            }
        }
    }

    public struct UniqueLight
    {
        public float Radius { get; private set; }
        public Vector3 Color { get; private set; }
        public UniqueLight(LightInstance lightInstance)
        {
            Radius = lightInstance.Radius;
            var color = lightInstance.LightReference?.LightSource?.Color;
            if (color != null)
            {
                Color = VectorTransformer.TranslateColorWithoutAlpha(color.Value);
            }
            else
            {
                Color = new Vector3(1, 1, 1);
            }
        }
    }

    public interface ICharacterModel
    {
        bool TryGetMaterialVariation(string imageName, out int variationIndex, out Color? color);
        bool ShouldSkipMeshGenerationForMaterial(string materialName);
    }

    static class ImageAlphaConverter
    {
        public static string AddAlphaToImage(string filePath, ShaderType shaderType)
        {
            var prefix = MaterialList.GetMaterialPrefix(shaderType);
            var newFileName = $"{prefix}{Path.GetFileNameWithoutExtension(filePath)}{Path.GetExtension(filePath)}";
            var newFilePath = Path.Combine(Path.GetDirectoryName(filePath), newFileName);

            if (File.Exists(newFilePath)) return newFilePath;

            using (var originalImage = new Bitmap(filePath))
            using (var newImage = originalImage.Clone(
                new Rectangle(0, 0, originalImage.Width, originalImage.Height),
                PixelFormat.Format32bppArgb))
            {
                for (int i = 0; i < originalImage.Width; i++)
                {
                    for (int j = 0; j < originalImage.Height; j++)
                    {
                        var pixelColor = originalImage.GetPixel(i, j);
                        switch (shaderType)
                        {
                            case ShaderType.Transparent25:
                                newImage.SetPixel(i, j, Color.FromArgb(64, pixelColor));
                                break;
                            case ShaderType.Transparent50:
                            case ShaderType.TransparentSkydome:
                                newImage.SetPixel(i, j, Color.FromArgb(128, pixelColor));
                                break;
                            case ShaderType.Transparent75:
                            case ShaderType.TransparentAdditive:
                                newImage.SetPixel(i, j, Color.FromArgb(192, pixelColor));
                                break;
                            default:
                                var maxRgb = new[] { pixelColor.R, pixelColor.G, pixelColor.B }.Max();
                                var newAlpha = maxRgb <= FullAlphaToDoubleAlphaThreshold ? maxRgb :
                                    Math.Min(maxRgb + ((maxRgb - FullAlphaToDoubleAlphaThreshold) * 2), 255);
                                newImage.SetPixel(i, j, Color.FromArgb(newAlpha, pixelColor));
                                break;
                        }
                    }
                }
                newImage.Save(newFilePath, ImageFormat.Png);
                return newFilePath;
            }
        }

        private static int FullAlphaToDoubleAlphaThreshold = 64;
    }

    public static class VectorTransformer
    {
        public static Vector2 TranslateVector(vec2 vector, params VectorTranslator<Vector2>[] translators)
        {
            return TranslateVector(ConvertGlmSharpVector2ToSystemNumeric(vector), translators);
        }
        public static Vector3 TranslateVector(vec3 vector, params VectorTranslator<Vector3>[] translators)
        {
            return TranslateVector(ConvertGlmSharpVector3ToSystemNumeric(vector), translators);
        }
        public static Vector3 Scale(Vector3 v, float scale) { return Vector3.Multiply(v, scale); }
        public static T TranslateVector<T>(T vector, params VectorTranslator<T>[] translators) where T : struct
        {
            T newVector = vector;

            foreach (var translator in translators)
            {
                newVector = translator(newVector);
            }

            return newVector;
        }

        public static Vector4 TranslateColor(WldColor color)
        {
            return new Vector4(color.R, color.G, color.B, color.A);
        }

        public static Vector4 TranslateColor(Color color)
        {
            return new Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
        }

        public static Vector3 TranslateColorWithoutAlpha(vec4 color)
        {
            return new Vector3(color.r, color.g, color.b);
        }

        public static Vector4 DecodeRgbToLinear(Vector4 color)
        {
            return new Vector4(
                (float)Math.Pow(color.X, 2.2f),
                (float)Math.Pow(color.Y, 2.2f),
                (float)Math.Pow(color.Z, 2.2f),
                color.W);
        }

        public delegate T VectorTranslator<T>(T vector) where T : struct;

        private static Vector2 ConvertGlmSharpVector2ToSystemNumeric(vec2 eqv) { return new Vector2(eqv.x, eqv.y); }
        private static Vector3 ConvertGlmSharpVector3ToSystemNumeric(vec3 eqv) { return new Vector3(eqv.x, eqv.y, eqv.z); }
    }

    class VT // VectorTranslations
    {
        public static Vector2 NegateX(Vector2 v) { return new Vector2(-v.X, v.Y); }
        public static Vector2 NegateY(Vector2 v) { return new Vector2(v.X, -v.Y); }
        public static Vector3 NegateX(Vector3 v) { return new Vector3(-v.X, v.Y, v.Z); }
        public static Vector3 NegateY(Vector3 v) { return new Vector3(v.X, -v.Y, v.Z); }
        public static Vector3 NegateZ(Vector3 v) { return new Vector3(v.X, v.Y, -v.Z); }
        public static Vector3 SwapXandY(Vector3 v) { return new Vector3(v.Y, v.X, v.Z); }
        public static Vector3 SwapYandZ(Vector3 v) { return new Vector3(v.X, v.Z, v.Y); }
    }
}
