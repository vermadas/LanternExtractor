using GlmSharp;
using LanternExtractor.EQ.Wld.Fragments;
using System.Collections.Generic;
using System.Linq;

namespace LanternExtractor.EQ.Wld.Helpers
{
    public static class MeshExportHelper
    {
        /// <summary>
        /// Transforms vertices of mesh for the given animation and frame.
        /// </summary>
        /// <param name="mesh">The mesh that will have vertices shifted</param>
        /// <param name="skeleton">The SkeletonHierarchy that contains the bone transformations</param>
        /// <param name="animName">The name of the animation used for the transform</param>
        /// <param name="frame">The frame of the animation</param>
        /// <param name="singularBoneIndex">The bone index for the mesh when there is a 1:1 relationship</param>
        /// <returns>The original vertex positions</returns>
        public static List<vec3> ShiftMeshVertices(Mesh mesh, SkeletonHierarchy skeleton, bool isCharacterAnimation, 
            string animName, int frame, int singularBoneIndex = -1, bool correctCenter = false)
        {
            var originalVertices = new List<vec3>();
            if (!skeleton.Animations.ContainsKey(animName) ||
                mesh.Vertices.Count == 0)
            {
                return originalVertices;
            }

            var centerCorrection = vec3.Zero;
            if (correctCenter && mesh.Center != vec3.Zero)
            {
                centerCorrection = new vec3(-mesh.Center.x, -mesh.Center.y, -mesh.Center.z);
            }

            var animation = skeleton.Animations[animName];
            if (frame >= animation.FrameCount) return originalVertices;
            
            var tracks = isCharacterAnimation ? animation.TracksCleanedStripped : animation.TracksCleaned;

            if (singularBoneIndex > -1)
            {
                var bone = skeleton.Skeleton[singularBoneIndex].CleanedName;
                if (!tracks.ContainsKey(bone)) return originalVertices;
                var modelMatrix = skeleton.GetBoneMatrix(singularBoneIndex, tracks, frame, centerCorrection);

                originalVertices.AddRange(ShiftMeshVerticesWithIndices(
                    0, mesh.Vertices.Count - 1, mesh, modelMatrix));

                return originalVertices;
            }

            foreach (var mobVertexPiece in mesh.MobPieces)
            {
                var boneIndex = mobVertexPiece.Key;
                var bone = skeleton.Skeleton[boneIndex].CleanedName;

                if (!tracks.ContainsKey(bone)) continue;

                var modelMatrix = skeleton.GetBoneMatrix(boneIndex, tracks, frame, centerCorrection);

                originalVertices.AddRange(ShiftMeshVerticesWithIndices(
                    mobVertexPiece.Value.Start,
                    mobVertexPiece.Value.Start + mobVertexPiece.Value.Count - 1, 
                    mesh, modelMatrix));
            }

            return originalVertices;
        }

        public static List<vec3> ShiftMeshVerticesMultipleSkeletons(Mesh mesh, List<SkeletonHierarchy> skeletons, List<bool> isCharacterAnimations,
            string animName, int frame, List<int> singularBoneIndices = null, bool correctCenter = false)
        {
            var originalVertices = new List<vec3>();
            foreach (var skeleton in skeletons)
            {
                if (!skeleton.Animations.ContainsKey(animName) ||
                    mesh.Vertices.Count == 0)
                {
                    return originalVertices;
                }
            }

            var centerCorrection = vec3.Zero;
            if (correctCenter && mesh.Center != vec3.Zero)
            {
                centerCorrection = new vec3(-mesh.Center.x, -mesh.Center.y, -mesh.Center.z);
            }

            List<Dictionary<string, TrackFragment>> tracksList = new List<Dictionary<string, TrackFragment>>();
            foreach (var skeleton in skeletons)
            {
                var animation = skeleton.Animations[animName];
                if (frame >= animation.FrameCount) return originalVertices;
                var isCharacterAnimation = isCharacterAnimations[skeletons.IndexOf(skeleton)];
                var tracks = isCharacterAnimation ? animation.TracksCleanedStripped : animation.TracksCleaned;
                tracksList.Add(tracks);
            }

            var modelMatrix = mat4.Translate(centerCorrection).Inverse * mat4.Identity;
            for (var i = 0; i < skeletons.Count; i++)
            {
                if (singularBoneIndices[i] < 0) continue;

                var bone = skeletons[i].Skeleton[singularBoneIndices[i]].CleanedName;
                if (!tracksList[i].ContainsKey(bone)) return originalVertices;
                modelMatrix = skeletons[i].GetBoneMatrix(singularBoneIndices[i], tracksList[i], frame) * modelMatrix;
            }
            modelMatrix = mat4.Translate(centerCorrection) * modelMatrix;
            originalVertices.AddRange(ShiftMeshVerticesWithIndices(
                0, mesh.Vertices.Count - 1, mesh, modelMatrix));

            return originalVertices;

            /*
            foreach (var mobVertexPiece in mesh.MobPieces)
            {
                var boneIndex = mobVertexPiece.Key;
                var bone = skeleton.Skeleton[boneIndex].CleanedName;

                if (!tracks.ContainsKey(bone)) continue;

                var modelMatrix = skeleton.GetBoneMatrix(boneIndex, tracks, frame, centerCorrection);

                originalVertices.AddRange(ShiftMeshVerticesWithIndices(
                    mobVertexPiece.Value.Start,
                    mobVertexPiece.Value.Start + mobVertexPiece.Value.Count - 1,
                    mesh, modelMatrix));
            }
            */

            return originalVertices;
        }

        private static List<vec3> ShiftMeshVerticesWithIndices(int start, int end, Mesh mesh, mat4 boneMatrix)
        {
            var originalVertices = new List<vec3>();
            for (int i = start; i <= end; i++)
            {
                if (i >= mesh.Vertices.Count) break;

                var vertex = mesh.Vertices[i];
                originalVertices.Add(vertex);
                var newVertex = boneMatrix * new vec4(vertex, 1f);
                mesh.Vertices[i] = newVertex.xyz;
            }
            return originalVertices;
        }
    }
}
