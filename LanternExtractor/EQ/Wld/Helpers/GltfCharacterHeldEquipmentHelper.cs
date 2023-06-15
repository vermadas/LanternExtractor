using LanternExtractor.EQ.Wld.DataTypes;
using LanternExtractor.EQ.Wld.Exporters;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.Infrastructure.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LanternExtractor.EQ.Wld.Exporters.GltfWriter;

namespace LanternExtractor.EQ.Wld.Helpers
{
	public static class GltfCharacterHeldEquipmentHelper
	{
		public static WldFragment GetMeshOrSkeletonForCharacterHeldEquipment(string modelId, WldFileEquipment wldEqFile, ILogger logger)
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
					logger.LogError($"Character held equipment model '{actorLookupName}' not found!");
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

		public static void AddCharacterHeldEquipmentToGltfWriter(WldFragment meshOrSkeleton, string modelId,
			SkeletonHierarchy characterSkeleton, string attachBoneKey, GltfWriter gltfWriter, ref int boneIndexOffset,
			ICharacterModel optionalFakeCharacterModel = null)
		{
			if (meshOrSkeleton == null) return;

			var attachBone = characterSkeleton.BoneMappingClean.Where(kv => kv.Value == attachBoneKey).SingleOrDefault();
			
			if (attachBone.Equals(default(KeyValuePair<int, string>))) return;

			var boneIndex = attachBone.Key;

			if (meshOrSkeleton is Mesh)
			{
				var originalVertices = MeshExportHelper.ShiftMeshVertices((Mesh)meshOrSkeleton, characterSkeleton, true, "pos", 0, boneIndex, true);
				gltfWriter.AddFragmentData(
					mesh: (Mesh)meshOrSkeleton,
					generationMode: ModelGenerationMode.Combine,
					isSkinned: true,
					singularBoneIndex: boneIndex,
					characterModel: optionalFakeCharacterModel);

				((Mesh)meshOrSkeleton).Vertices = originalVertices;

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
						var originalVertices = MeshExportHelper.ShiftMeshVerticesMultipleSkeletons(
							mesh,
							new List<SkeletonHierarchy>() { eqSkeleton, characterSkeleton },
							new List<bool>() { false, true },
							"pos",
							0,
							new List<int>() { -1, boneIndex },
							true);
						gltfWriter.AddFragmentData(mesh, eqSkeleton, boneIndexOffset, null, 
							characterSkeleton.ModelBase, attachBoneKey, null, true);
						
						mesh.Vertices = originalVertices;
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
						var originalVertices = MeshExportHelper.ShiftMeshVerticesMultipleSkeletons(
							mesh,
							new List<SkeletonHierarchy>() { eqSkeleton, characterSkeleton },
							new List<bool>() { false, true },
							"pos",
							0,
							new List<int>() { i, boneIndex },
							true);

						gltfWriter.AddFragmentData(mesh, eqSkeleton, i + boneIndexOffset, null, 
							characterSkeleton.ModelBase, attachBoneKey);

						mesh.Vertices = originalVertices;
					}
				}
			}

			gltfWriter.ApplyAnimationToSkeleton(eqSkeleton, "pos", false, true);
			boneIndexOffset += eqSkeleton.Skeleton.Count;
		}

		public static bool IsShield(string itemId)
		{
			if (string.IsNullOrEmpty(itemId)) return false;

			var numericItem = int.Parse(itemId.Substring(2));
			return numericItem >= 200 && numericItem < 300;
		}

		public static bool CanWield(int raceId, NpcGender gender, int itemId)
		{
			if (itemId >= 200 && itemId < 300) // shield
			{
				return RaceIdGendersThatCanHoldShield.Contains((raceId, gender));
			}
			else
			{
				return RaceIdGendersThatCanHoldEquipment.Contains((raceId, gender));
			}
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

		private static readonly ISet<string> SkeletalActorsNotUsingBoneMeshes = new HashSet<string>() { "IT4", "IT61", "IT153", "IT154", "IT157", "IT198" };
		private static readonly ISet<string> MissingSkeletalActors = new HashSet<string>() { "IT145" };

		private static readonly ISet<(int, NpcGender)> RaceIdGendersThatCanHoldEquipment = new HashSet<(int, NpcGender)>()
		{
			(13, NpcGender.Neutral), (17, NpcGender.Neutral), (18, NpcGender.Neutral), (23, NpcGender.Male),
			(23, NpcGender.Female), (26, NpcGender.Neutral), (27, NpcGender.Neutral), (28, NpcGender.Neutral),
			(33, NpcGender.Neutral), (39, NpcGender.Neutral), (40, NpcGender.Neutral), (44, NpcGender.Male),
			(45, NpcGender.Neutral), (51, NpcGender.Neutral), (53, NpcGender.Neutral), (54, NpcGender.Neutral),
			(55, NpcGender.Male), (57, NpcGender.Male), (57, NpcGender.Female), (60, NpcGender.Neutral),
			(64, NpcGender.Neutral), (67, NpcGender.Male), (70, NpcGender.Male), (70, NpcGender.Female),
			(71, NpcGender.Male), (71, NpcGender.Female), (75, NpcGender.Neutral), (77, NpcGender.Male),
			(77, NpcGender.Female), (78, NpcGender.Male), (81, NpcGender.Male), (81, NpcGender.Female),
			(88, NpcGender.Male), (88, NpcGender.Female), (90, NpcGender.Male), (90, NpcGender.Female),
			(92, NpcGender.Male), (92, NpcGender.Female), (93, NpcGender.Male), (93, NpcGender.Female),
			(94, NpcGender.Male), (94, NpcGender.Female), (98, NpcGender.Male), (98, NpcGender.Female),
			(101, NpcGender.Neutral), (106, NpcGender.Male), (106, NpcGender.Female), (110, NpcGender.Neutral),
			(112, NpcGender.Male), (112, NpcGender.Female), (117, NpcGender.Male), (118, NpcGender.Male),
			(118, NpcGender.Female), (126, NpcGender.Neutral), (131, NpcGender.Neutral), (136, NpcGender.Neutral),
			(137, NpcGender.Neutral), (139, NpcGender.Male), (139, NpcGender.Female), (139, NpcGender.Neutral),
			(144, NpcGender.Neutral), (146, NpcGender.Neutral), (147, NpcGender.Male), (155, NpcGender.Neutral),
			(156, NpcGender.Neutral), (161, NpcGender.Neutral), (181, NpcGender.Neutral), (183, NpcGender.Male),
			(183, NpcGender.Female), (183, NpcGender.Neutral), (188, NpcGender.Neutral), (189, NpcGender.Male)
		};

		private static readonly ISet<(int, NpcGender)> RaceIdGendersThatCanHoldShield = new HashSet<(int, NpcGender)>()
		{
			(23, NpcGender.Male), (23, NpcGender.Female), (28, NpcGender.Neutral), (44, NpcGender.Male),
			(55, NpcGender.Male), (60, NpcGender.Neutral), (67, NpcGender.Male), (70, NpcGender.Male),
			(70, NpcGender.Female), (71, NpcGender.Male), (71, NpcGender.Female), (77, NpcGender.Male),
			(77, NpcGender.Female), (78, NpcGender.Male), (81, NpcGender.Male), (88, NpcGender.Male),
			(88, NpcGender.Female), (90, NpcGender.Male), (90, NpcGender.Female), (92, NpcGender.Male), 
			(92, NpcGender.Female), (93, NpcGender.Male), (93, NpcGender.Female), (94, NpcGender.Male), 
			(94, NpcGender.Female), (98, NpcGender.Male), (98, NpcGender.Female), (106, NpcGender.Male),
			(106, NpcGender.Female), (112, NpcGender.Male), (112, NpcGender.Female), (117, NpcGender.Male),
			(118, NpcGender.Male), (118, NpcGender.Female), (131, NpcGender.Neutral), (139, NpcGender.Male),
			(139, NpcGender.Female), (139, NpcGender.Neutral), (147, NpcGender.Male), (155, NpcGender.Neutral),
			(156, NpcGender.Neutral), (161, NpcGender.Neutral), (183, NpcGender.Male), (183, NpcGender.Female), 
			(183, NpcGender.Neutral), (189, NpcGender.Male)
		};
	}
}
