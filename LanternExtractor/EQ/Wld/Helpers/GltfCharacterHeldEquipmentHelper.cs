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
			SkeletonHierarchy characterSkeleton, string attachBoneKey, GltfWriter gltfWriter, ref int boneIndexOffset)
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
					singularBoneIndex: boneIndex);

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
	}
}
