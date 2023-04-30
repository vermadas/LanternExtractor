using System;
using System.Collections.Generic;
using System.IO;

namespace LanternExtractor.EQ
{
	public class NpcDatabaseToClientTranslator
	{
		public NpcDatabaseToClientTranslator()
		{
			_npcs = new Dictionary<NpcDbInfo, string>();
		}

		public void InitFromRaceDataCsv(string pathToCsv)
		{
			if (string.IsNullOrEmpty(pathToCsv) || !File.Exists(pathToCsv))
			{
				throw new InvalidOperationException($"RaceData.csv not found at '{pathToCsv ?? ""}'");
			}

			var headerPassed = false;
			foreach (var line in File.ReadLines(pathToCsv))
			{
				if (!headerPassed)
				{
					headerPassed = true;
					continue;
				}

				// This CSV should have no quoted columns, otherwise, this won't work
				var splitRow = line.Split(',');
				if (!int.TryParse(splitRow[0], out var id))
				{
					continue;
				}

				for(var i = 2; i < 5; i++) // Male, Female, Neutral columns
				{
					if (!string.IsNullOrEmpty(splitRow[i]))
					{
						var npcInfo = new NpcDbInfo()
						{
							RaceId = id,
							Gender = (NpcGender)(i - 2) // Column index is enum value + 2
						};
						_npcs.Add(npcInfo, splitRow[i].Trim());
					}
				}
			}
		}

		public string GetClientModelForRaceIdAndGender(int raceId, int gender)
		{
			var npcDbInfo = new NpcDbInfo()
			{
				RaceId = raceId,
				Gender = (NpcGender)gender
			};

			if (_npcs.TryGetValue(npcDbInfo, out var clientModel))
			{
				return clientModel;
			}
			return null;
		}

		private readonly IDictionary<NpcDbInfo, string> _npcs;
	}

	struct NpcDbInfo
	{
		public int RaceId { get; set; }
		public NpcGender Gender { get; set; }
	}

	public enum NpcGender
	{
		Male = 0,
		Female = 1,
		Neutral = 2
	}
}
