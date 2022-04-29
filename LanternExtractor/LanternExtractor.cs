using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LanternExtractor.EQ;
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
                    Console.WriteLine($"Startined to extract {fileName}");
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

            if (false /*DEBUG*/ && args.Length != 1)
            {
                Console.WriteLine("Usage: lantern.exe <filename/shortname/pc/all>");
                return;
            }

            string archiveName = "pc"; // args[0]; // DEBUG

            if (archiveName.Equals("pc", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!File.Exists(Path.Combine(_settings.EverQuestDirectory, "global_chr.s3d")))
                {
                    Console.WriteLine("No valid EQ files found at path: " + _settings.EverQuestDirectory);
                    return;
                }
                var pcEquipJsonFilePath = "PcEquip.json";
                if (!File.Exists(pcEquipJsonFilePath))
                {
                    Console.WriteLine("PcEquip.json file not found!");
                    return;
                }

                ArchiveExtractor.ExportSinglePlayerCharacterGltf(pcEquipJsonFilePath, "Exports/", _logger, _settings);
                Console.WriteLine($"Single player character export complete ({(DateTime.Now - start).TotalSeconds})s");

                return;
            }

            List<string> eqFiles = EqFileHelper.GetValidEqFilePaths(_settings.EverQuestDirectory, archiveName);

            if (eqFiles.Count == 0)
            {
                Console.WriteLine("No valid EQ files found for: '" + archiveName + "' at path: " +
                                  _settings.EverQuestDirectory);
                return;
            }

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

            Console.WriteLine($"Extraction complete ({(DateTime.Now - start).TotalSeconds})s");
        }
    }
}