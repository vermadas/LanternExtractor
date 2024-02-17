using LanternExtractor.EQ.Archive;
using LanternExtractor.EQ.Wld;
using LanternExtractor.Infrastructure;
using LanternExtractor.Infrastructure.Logger;
using System.Collections.Generic;

namespace LanternExtractor.EQ
{
    public sealed class GlobalReference
    {
        // Note: NOT thread-safe, however, outside of init, should be used
        // read-only. If app is running multi-threaded the init happens
        // before tasks are spun up.
        public static WldFileCharacters CharacterWld { get; private set; }
        public static SqliteDatabaseConnector ServerDatabaseConnector { get; private set; }
        public static NpcDatabaseToClientTranslator NpcDatabaseToClientTranslator { get; private set; }

        public static void InitCharacterWld(ArchiveBase pfsArchive, ArchiveFile wldFile, string rootFolder, string zoneName, 
            WldType type, ILogger logger, Settings settings, List<WldFile> wldFilesToInject = null)
        {
            CharacterWld = new WldFileCharacters(wldFile, zoneName, type, logger, settings, wldFilesToInject);
            CharacterWld.Initialize(rootFolder, false);
            pfsArchive.FilenameChanges = CharacterWld.FilenameChanges;
            CharacterWld.BaseS3DArchive = pfsArchive;
        }

        public static void InitServerDatabaseConnector(Settings settings)
        {
            ServerDatabaseConnector = new SqliteDatabaseConnector(settings);
        }

        public static void InitNpcDatabaseToClientTranslator(string pathToRaceDataCsv)
        {
            NpcDatabaseToClientTranslator = new NpcDatabaseToClientTranslator();
            NpcDatabaseToClientTranslator.InitFromRaceDataCsv(pathToRaceDataCsv);
        }
    }
}
