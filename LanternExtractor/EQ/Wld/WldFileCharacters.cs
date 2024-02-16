using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanternExtractor.EQ.Archive;
using LanternExtractor.EQ.Wld.DataTypes;
using LanternExtractor.EQ.Wld.Fragments;
using LanternExtractor.EQ.Wld.Helpers;
using LanternExtractor.Infrastructure;
using LanternExtractor.Infrastructure.Logger;

namespace LanternExtractor.EQ.Wld
{
    public class WldFileCharacters : WldFile
    {
        public Dictionary<string, string> AnimationSources = new Dictionary<string, string>();

        public WldFileCharacters(ArchiveFile wldFile, string zoneName, WldType type, ILogger logger, Settings settings,
            List<WldFile> wldFilesToInject = null) : base(wldFile, zoneName, type, logger, settings, wldFilesToInject)
        {
            ParseAnimationSources();
        }

        public void AddAdditionalAnimationsToSkeleton(SkeletonHierarchy skeleton, bool overwriteExisting = false)
        {
            if (!AnimationSources.TryGetValue(skeleton.ModelBase, out var alternateSkeletonModel)) return;

            var alternateSkeleton = GetFragmentsOfType<SkeletonHierarchy>()
                .Where(s => s.ModelBase == alternateSkeletonModel).SingleOrDefault();

            if (alternateSkeleton == null) return;

            foreach (var animationKey in alternateSkeleton.Animations.Keys)
            {
                if (!overwriteExisting && skeleton.Animations.ContainsKey(animationKey)) continue;

                // Want the same animation data except for the AnimModelBase name to be
                // that of the base skeleton. Renaming the existing animation without
                // making a new instance might cause problems if it's referenced later
                skeleton.Animations[animationKey] = new Animation()
                {
                    AnimModelBase = skeleton.ModelBase,
                    Tracks = alternateSkeleton.Animations[animationKey].Tracks,
                    TracksCleaned = alternateSkeleton.Animations[animationKey].TracksCleaned,
                    TracksCleanedStripped = alternateSkeleton.Animations[animationKey].TracksCleanedStripped,
                    AnimationTimeMs = alternateSkeleton.Animations[animationKey].AnimationTimeMs,
                    FrameCount = alternateSkeleton.Animations[animationKey].FrameCount
                };
            }
        }

        private void ParseAnimationSources()
        {
            string filename = "ClientData/animationsources.txt";
            if (!File.Exists(filename))
            {
                Logger.LogError("WldFileCharacters: No animationsources.txt file found.");
                return;
            }

            string fileText = File.ReadAllText(filename);
            List<List<string>> parsedText = TextParser.ParseTextByDelimitedLines(fileText, ',', '#');

            foreach (var line in parsedText)
            {
                if (line.Count != 2)
                {
                    continue;
                }

                _animationSources[line[0].ToLower()] = line[1].ToLower();
            }
        }

        private string GetAnimationModelLink(string modelName)
        {
            return !_animationSources.ContainsKey(modelName) ? modelName : _animationSources[modelName];
        }

        protected override void ProcessData()
        {
            base.ProcessData();
            FindAdditionalAnimationsAndMeshes();
            BuildSlotMapping();
            FindMaterialVariants();

            if (Settings.ExportCharactersToSingleFolder)
            {
                var characterFixer = new CharacterFixer();
                characterFixer.Fix(this);
            }

            foreach (var skeleton in GetFragmentsOfType<SkeletonHierarchy>())
            {
                skeleton.BuildSkeletonData(WldType == Wld.WldType.Characters);
            }
        }

        private void BuildSlotMapping()
        {
            var materialLists = GetFragmentsOfType<MaterialList>();

            foreach (var list in materialLists)
            {
                list.BuildSlotMapping(Logger);
            }
        }

        private void FindMaterialVariants()
        {
            var materialLists = GetFragmentsOfType<MaterialList>();

            foreach (var list in materialLists)
            {
                string materialListModelName = FragmentNameCleaner.CleanName(list);
                var materialListContainsRobe = list.Materials.Where(
                    m => m.Name.StartsWith("clk", StringComparison.InvariantCultureIgnoreCase))
                    .Any();

                foreach (var material in GetFragmentsOfTypeIncludingInjectedWlds<Material>())
                {
                    string materialName = FragmentNameCleaner.CleanName(material);
                    var materialIsRobe = materialName.StartsWith("clk", StringComparison.InvariantCultureIgnoreCase);
                    
                    if (material.IsHandled && !materialIsRobe)
                    {
                        continue;
                    }

                    if ((materialListContainsRobe && materialIsRobe)
                        || materialName.StartsWith(materialListModelName))
                    {
                        list.AddVariant(material, Logger);
                    }
                }
            }

            foreach (var material in GetFragmentsOfType<Material>())
            {
                if (material.IsHandled)
                {
                    continue;
                }

                Logger.LogWarning("WldFileCharacters: Material not assigned: " + material.Name);
            }
        }

        private void FindAdditionalAnimationsAndMeshes()
        {
            if (GetFragmentsOfType<TrackFragment>().Count == 0)
            {
                return;
            }

            var skeletons = GetFragmentsOfType<SkeletonHierarchy>();

            if (skeletons.Count == 0)
            {
                if (WldFilesToInject == null)
                {
                    return;
                }
                
                foreach (var wldFile in WldFilesToInject)
                {
                    skeletons.AddRange(wldFile.GetFragmentsOfType<SkeletonHierarchy>());
                }
            }

            if (skeletons.Count == 0)
            {
                return;
            }

            var allTracks = GetFragmentsOfType<TrackFragment>();

            _wldFilesToInject?.ForEach(w =>
                allTracks.AddRange(w?.GetFragmentsOfType<TrackFragment>() 
                ?? Enumerable.Empty<TrackFragment>()));

            allTracks.Where(t => !t.IsPoseAnimation && !t.IsNameParsed)
                .ToList()
                .ForEach(t => t.ParseTrackData(_logger));

            foreach (var skeletonFragment in skeletons)
            {
                SkeletonHierarchy skeleton = skeletonFragment as SkeletonHierarchy;

                if (skeleton == null)
                {
                    continue;
                }

                string modelBase = skeleton.ModelBase;
                string alternateModel = GetAnimationModelLink(modelBase);

                // TODO: Alternate model bases
                allTracks
                    .Where(t => t.ModelName == modelBase || t.ModelName == alternateModel)
                    .ToList()
                    .ForEach(t => skeleton.AddTrackData(t));
                
                // TODO: Split to another function
                if(GetFragmentsOfType<Mesh>().Count != 0)
                {
                    foreach (var mesh in GetFragmentsOfType<Mesh>())
                    {
                        if (mesh.IsHandled)
                        {
                            continue;
                        }

                        string cleanedName = FragmentNameCleaner.CleanName(mesh);

                        string basename = cleanedName;

                        bool endsWithNumber = char.IsDigit(cleanedName[cleanedName.Length - 1]);

                        if (endsWithNumber)
                        {
                            int id = Convert.ToInt32(cleanedName.Substring(cleanedName.Length - 2));
                            cleanedName = cleanedName.Substring(0, cleanedName.Length - 2);

                            if (cleanedName.Length != 3)
                            {
                                string modelType = cleanedName.Substring(cleanedName.Length - 3);
                                cleanedName = cleanedName.Substring(0, cleanedName.Length - 2);
                            }

                            basename = cleanedName;
                        }

                        if (basename == modelBase)
                        {
                            skeleton.AddAdditionalMesh(mesh);
                        }
                    }
                }
            }

            foreach (var track in GetFragmentsOfType<TrackFragment>())
            {
                if (track.IsPoseAnimation || track.IsProcessed)
                {
                    continue;
                }

                Logger.LogWarning("WldFileCharacters: Track not assigned: " + track.Name);
            }
        }
    }
}
