using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LanternExtractor.EQ;
using LanternExtractor.EQ.Wld.Exporters;
using LanternExtractor.Infrastructure.Logger;

namespace LanternExtractor
{
    static class LanternExtractor
    {
        private static Settings _settings;
        private static ILogger _logger;
        // Switch to true to use multiple processes for processing
        private static bool _useMultiProcess = false;

        // Batch jobs n at a time
        private static int _processCount = 4;
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "PROCESS_JOB")
            {
                var zoneFiles = args.Skip(1).ToArray();
                var scrubbedZoneFiles = zoneFiles.Select(s => Regex.Match(s, "(\\w+)(?:\\.s3d)$").ToString()).ToArray();

                _logger = new TextFileLogger($"log-{Process.GetCurrentProcess().Id}.txt");
                _logger.LogInfo(string.Join("-", scrubbedZoneFiles));
                _settings = new Settings("settings.txt", _logger);

                foreach (var fileName in zoneFiles)
                {
                    Console.WriteLine($"Started extracting {fileName}");
                    ArchiveExtractor.Extract(fileName, "Exports/", _logger, _settings);
                    Console.WriteLine($"Finished extracting {fileName}");
                }
                return;
            }


            _logger = new TextFileLogger("log.txt");
            _settings = new Settings("settings.txt", _logger);
            _settings.Initialize();
            _logger.SetVerbosity((LogVerbosity)_settings.LoggerVerbosity);

            DateTime start = DateTime.Now;

//            args = new string[] { "citymist" };

            if (args.Length == 0 || args.Length > 2 || 
                (args.Length == 2 && !args[0].Equals("pc", StringComparison.InvariantCultureIgnoreCase)))
            {
                Console.WriteLine("Usage: lantern.exe <filename/shortname/pc/all> [if previous argument pc - pc equip json file]");
                return;
            }

            var archiveName = args[0];
            if (archiveName.Equals("pc", StringComparison.InvariantCultureIgnoreCase))
            {
                var pcEquipFilePath = "PcEquip.json";
                if (args.Length == 2)
                {
                    pcEquipFilePath = args[1].Trim();
                    var extension = Path.GetExtension(pcEquipFilePath);
                    if (extension == string.Empty)
                    {
                        pcEquipFilePath = Path.ChangeExtension(pcEquipFilePath, "json");
                    }
                    else if (!extension.Equals(".json", StringComparison.InvariantCultureIgnoreCase))
                    {
                        Console.WriteLine($"pc equip file: {pcEquipFilePath} does not have expected json extension");
                        return;
                    }
                }
                _settings.ModelExportFormat = ModelExportFormat.GlTF;
                var playerCharacterEquipment = ReadPlayerCharacterModel(pcEquipFilePath);
                if (playerCharacterEquipment == null) return;
                
                var pcExportFolder = pcEquipFilePath == "PcEquip.json" ? 
                    playerCharacterEquipment.RaceGender :
                    Path.GetFileNameWithoutExtension(pcEquipFilePath);

                if (IsDatabaseConnectionRequired(playerCharacterEquipment))
                {
                    GlobalReference.InitServerDatabaseConnector(_settings);
                }
                try
                {
                    ArchiveExtractor.InitializeSharedCharacterWld("Exports/", _logger, _settings);
                    PlayerCharacterGltfExporter.AddPcEquipmentClientDataFromDatabase(playerCharacterEquipment);
                    ArchiveExtractor.InitWldsForPlayerCharacterGltfExport(playerCharacterEquipment, 
                        "Exports/", pcExportFolder, _logger, _settings, out var mainWldEqFile);
                    PlayerCharacterGltfExporter.ExportPlayerCharacter(playerCharacterEquipment, 
                        GlobalReference.CharacterWld, mainWldEqFile, _logger, _settings, pcExportFolder);
                }
                finally
                {
                    GlobalReference.ServerDatabaseConnector?.Dispose();
                }

                Console.WriteLine($"Single player character export complete ({(DateTime.Now - start).TotalSeconds})s");

                return;
            }

            if (IsDatabaseConnectionRequired())
            {
                GlobalReference.InitServerDatabaseConnector(_settings);
            }

            List<string> eqFiles = EqFileHelper.GetValidEqFilePaths(_settings.EverQuestDirectory, archiveName);
            eqFiles.Sort();

            if (eqFiles.Count == 0 && !EqFileHelper.IsClientDataFile(archiveName))
            {
                Console.WriteLine("No valid EQ files found for: '" + archiveName + "' at path: " +
                                  _settings.EverQuestDirectory);
                return;
            }

            if (_settings.UsingCombinedGlobalChr())
            {
                ArchiveExtractor.InitializeSharedCharacterWld("Exports/", _logger, _settings);
            }
            if (_settings.ExportZoneCharacterVariations)
            {
                GlobalReference.InitNpcDatabaseToClientTranslator("RaceData.csv");
            }
            try
            {
                if (_useMultiProcess && _processCount > 0)
                {
                    List<Task> tasks = new List<Task>();
                    int i = 0;

                    // Each process is responsible for n number of files to work through determined by the process count here.
                    int chunkCount = Math.Max(1, (int)Math.Ceiling((double)(eqFiles.Count / _processCount)));
                    foreach (var chunk in eqFiles.GroupBy(s => i++ / chunkCount).Select(g => g.ToArray()).ToArray())
                    {
                        Task task = Task.Factory.StartNew(() =>
                        {
                            var processJob = Process.Start("LanternExtractor.exe", string.Join(" ", chunk.Select(c => $"\"{c}\"").ToArray().Prepend("PROCESS_JOB")));
                            processJob.WaitForExit();
                        });
                        tasks.Add(task);
                    }
                    Task.WaitAll(tasks.ToArray());
                }
                else
                {
                    foreach (var file in eqFiles)
                    {
                        ArchiveExtractor.Extract(file, "Exports/", _logger, _settings);
                    }
                }
            }
            finally
            {
                GlobalReference.ServerDatabaseConnector?.Dispose();
            }

            ClientDataCopier.Copy(archiveName, "Exports/", _logger, _settings);

            Console.WriteLine($"Extraction complete ({(DateTime.Now - start).TotalSeconds:.00}s)");
        }

        private static PlayerCharacterModel ReadPlayerCharacterModel(string pcEquipJsonFilePath)
        {
            if (!File.Exists(pcEquipJsonFilePath))
            {
                Console.WriteLine($"PcEquip json file not found at: {pcEquipJsonFilePath}!");
                return null;
            }

            var pcEquipmentText = File.ReadAllText(pcEquipJsonFilePath);
            var deserializeOptions = new JsonSerializerOptions();
            deserializeOptions.Converters.Add(new ColorJsonConverter());
            var pcEquipment = JsonSerializer.Deserialize<PlayerCharacterModel>(pcEquipmentText, deserializeOptions);
            if (!pcEquipment.Validate(out var errorMessage))
            {
                Console.WriteLine($"Cannot export player character - {errorMessage}");
                return null;
            }
            return pcEquipment;
        }
        private static bool IsDatabaseConnectionRequired(PlayerCharacterModel pcModel)
        {
            return (string.IsNullOrEmpty(pcModel.Primary_ID) && !string.IsNullOrEmpty(pcModel.Primary_Name)) ||
                (string.IsNullOrEmpty(pcModel.Secondary_ID) && !string.IsNullOrEmpty(pcModel.Secondary_Name)) ||
                !string.IsNullOrEmpty(pcModel.Head.Name) ||
                !string.IsNullOrEmpty(pcModel.Wrist.Name) ||
                !string.IsNullOrEmpty(pcModel.Arms.Name) ||
                !string.IsNullOrEmpty(pcModel.Hands.Name) ||
                !string.IsNullOrEmpty(pcModel.Chest.Name) ||
                !string.IsNullOrEmpty(pcModel.Legs.Name) ||
                !string.IsNullOrEmpty(pcModel.Feet.Name);
        }

        private static bool IsDatabaseConnectionRequired()
        {
            return _settings.ExportZoneWithDoors || _settings.ExportZoneCharacterVariations;
        }
    }
}