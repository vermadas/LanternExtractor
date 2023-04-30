using LanternExtractor.EQ;
using LanternExtractor.EQ.Wld.Exporters;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Numerics;
using static LanternExtractor.EQ.Wld.Exporters.PlayerCharacterModel;

namespace LanternExtractor.Infrastructure
{
    public class SqliteDatabaseConnector : IDisposable
    {
        public SqliteDatabaseConnector(Settings settings)
        {
            _connection = new SQLiteConnection(string.Format(ConnectionString, settings.ServerDbPath));
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }

        public Item QueryItemFromDatabase(string name, string slot)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(slot)) return null;

            Item item = null;
            using (var command = new SQLiteCommand(_connection))
            {
                _connection.Open();

                command.CommandText = ItemFromNameAndSlotQuery;
                command.CommandType = CommandType.Text;
                command.Parameters.Add(new SQLiteParameter("@name", $"{name}%"));
                command.Parameters.Add(new SQLiteParameter("@slot", SlotsIntFromPcEquipName(slot)));

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        item = new Item()
                        {
                            Name = ((string)reader[LanternDb.Items_NameColumn]).Trim(),
                            IdFile = ((string)reader[LanternDb.Items_IdFileColumn]).Trim(),
                            Slots = (int)reader[LanternDb.Items_SlotsColumn],
                            Material = (int)reader[LanternDb.Items_MaterialColumn],
                            Color = (int)reader[LanternDb.Items_ColorColumn]
                        };
                    }
                }

                _connection.Close();
            }
            return item;
        }

        public IEnumerable<Door> QueryDoorsInZoneFromDatabase(string zoneName)
        {
            var doors = new List<Door>();

            if (string.IsNullOrEmpty(zoneName)) return doors;

            using (var command = new SQLiteCommand(_connection))
            {
                _connection.Open();

                command.CommandText = DoorsInZoneQuery;
                command.CommandType = CommandType.Text;
                command.Parameters.Add(new SQLiteParameter("@zone", zoneName));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var door = new Door()
                        {
                            Name = ((string)reader[LanternDb.Doors_NameColumn]).Trim(),
                            Position = new Vector3
                            (
                                (float)(double)reader[LanternDb.Doors_PosXColumn],
                                (float)(double)reader[LanternDb.Doors_PosYColumn],
                                (float)(double)reader[LanternDb.Doors_PosZColumn]
                            ),
                            OpenType = (int)reader[LanternDb.Doors_OpenTypeColumn],
                            Heading = (double)reader[LanternDb.Doors_HeadingColumn],
                            Incline = (int)reader[LanternDb.Doors_InclineColumn],
                            Width = (int)reader[LanternDb.Doors_WidthColumn]
                        };

                        doors.Add(door);
                    }
                }

                _connection.Close();
            }

            return doors;
        }

        public IEnumerable<(string, PlayerCharacterModel)> QueryPlayerCharactersInZoneFromDatabase(string zoneName)
        {
            var pcModels = new List<(string, PlayerCharacterModel)>();

            if (string.IsNullOrEmpty(zoneName)) return pcModels;

			using (var command = new SQLiteCommand(_connection))
			{
				_connection.Open();

				command.CommandText = PlayerCharactersInZoneQuery;
				command.CommandType = CommandType.Text;
				command.Parameters.Add(new SQLiteParameter("@zone", zoneName));

				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						var pcModel = new PlayerCharacterModel()
						{
                            Face = (int)(long)reader[LanternDb.Npc_FaceColumn]
						};
                        var raceId = (int)reader[LanternDb.Npc_RaceColumn];
                        var gender = (int)reader[LanternDb.Npc_GenderColumn];
                        pcModel.RaceGender = GlobalReference.NpcDatabaseToClientTranslator
                            .GetClientModelForRaceIdAndGender(raceId, gender);

                        var primaryId = (int)(long)reader[LanternDb.Npc_MeleeTexture1Column];
                        if (primaryId > 0)
                        {
                            pcModel.Primary_ID = $"IT{primaryId}";
                        }
						var secondaryId = (int)(long)reader[LanternDb.Npc_MeleeTexture2Column];
						if (secondaryId > 0)
						{
							pcModel.Secondary_ID = $"IT{secondaryId}";
						}
                        var mainMaterial = 0;
                        if (reader[LanternDb.Npc_TextureColumn].GetType() != typeof(DBNull))
                        {
							mainMaterial = (int)reader[LanternDb.Npc_TextureColumn];
						}
                        pcModel.Head = new Helm()
                        {
                            Material = GetPlayerCharacterEquipmentMaterial(mainMaterial, 
                                (int)reader[LanternDb.Npc_HelmTextureColumn])
                        };
                        pcModel.Wrist = new Equipment()
                        {
                            Material = GetPlayerCharacterEquipmentMaterial(mainMaterial,
                                (int)reader[LanternDb.Npc_BracerTextureColumn])
                        };
						pcModel.Arms = new Equipment()
						{
							Material = GetPlayerCharacterEquipmentMaterial(mainMaterial,
								(int)reader[LanternDb.Npc_ArmTextureColumn])
						};
						pcModel.Hands = new Equipment()
						{
							Material = GetPlayerCharacterEquipmentMaterial(mainMaterial,
		                        (int)reader[LanternDb.Npc_HandTextureColumn])
						};
                        Color? chestColor = null;
                        if (reader[LanternDb.ChestColorAlias].GetType() != typeof(DBNull))
                        {
                            var colorInt = (int)(long)reader[LanternDb.ChestColorAlias];
                            if (colorInt > 0)
                            {
								chestColor = ColorTranslator.FromHtml($"#{colorInt:X6}");
							}      
                        }
						pcModel.Chest = new Equipment()
						{
							Material = GetPlayerCharacterEquipmentMaterial(mainMaterial,
								(int)reader[LanternDb.Npc_ChestTextureColumn], true),
                            Color = chestColor
						};
						pcModel.Legs = new Equipment()
						{
							Material = GetPlayerCharacterEquipmentMaterial(mainMaterial,
		                        (int)reader[LanternDb.Npc_LegTextureColumn])
						};
						pcModel.Feet = new Equipment()
						{
							Material = GetPlayerCharacterEquipmentMaterial(mainMaterial,
								(int)reader[LanternDb.Npc_FeetTextureColumn])
						};
                        var npcId = (int)(long)reader[LanternDb.Npc_IdColumn];
                        var npcName = ((string)reader[LanternDb.Npc_NameColumn]).Trim().Trim('#');
                        pcModels.Add(($"{npcName}_{npcId}", pcModel));
					}
				}

				_connection.Close();
			}

            return pcModels;
		}

        public IEnumerable<Npc> QueryGlobalNpcsInZone(string zoneName)
        {
            return QueryNpcs(zoneName, GlobalCharactersInZoneQuery);
        }

        public IEnumerable<Npc> QueryNpcsWithVariationsInZone(string zoneName)
        {
            return QueryNpcs(zoneName, NpcsWithVariationsQuery);
        }

        private IEnumerable<Npc> QueryNpcs(string zoneName, string query)
        {
            var npcs = new List<Npc>();

            if (string.IsNullOrEmpty(zoneName)) return npcs;

            using (var command = new SQLiteCommand(_connection))
            {
                _connection.Open();

                command.CommandText = query;
                command.CommandType = CommandType.Text;
                command.Parameters.Add(new SQLiteParameter("@zone", zoneName));

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var npc = new Npc()
                        {
                            Race = (int)reader[LanternDb.Npc_RaceColumn],
                            Gender = (NpcGender)(int)reader[LanternDb.Npc_GenderColumn],
                            Face = (int)(long)reader[LanternDb.Npc_FaceColumn],
                            Texture = (int)reader[LanternDb.Npc_TextureColumn],
                            Primary = (int)(long)reader[LanternDb.Npc_MeleeTexture1Column],
                            Secondary = (int)(long)reader[LanternDb.Npc_MeleeTexture2Column],
                            HelmTexture = (int)reader[LanternDb.Npc_HelmTextureColumn]
                        };

                        npcs.Add(npc);
                    }
                }

                _connection.Close();
            }

            return npcs;
        }

        private int SlotsIntFromPcEquipName(string slot)
        {
            if (slot == "Secondary")
            {
                return (int)LanternDb.Slots.Primary + (int)LanternDb.Slots.Secondary;
            }
            if (slot == "Wrist")
            {
                return (int)LanternDb.Slots.L_Wrist + (int)LanternDb.Slots.R_Wrist;
            }
            if (Enum.TryParse<LanternDb.Slots>(slot, out var value))
            {
                return (int) value;
            }
            return 0;
        }

        private int GetPlayerCharacterEquipmentMaterial(int mainMaterial, int equipMaterial, bool isChest = false)
        {
            if (mainMaterial == 0) return equipMaterial;
            // Robe
            if (mainMaterial >= 10 && mainMaterial <= 16)
            {
                if (isChest) return mainMaterial;
                return equipMaterial;
            }
            if (equipMaterial == 0) return mainMaterial;

            return equipMaterial;
        }

        private readonly SQLiteConnection _connection;

        private const string ConnectionString = "Data Source={0};Version=3;Read Only=True";

        private static readonly string ItemFromNameAndSlotQuery =
        $@"select 
    {LanternDb.Items_NameColumn},
    {LanternDb.Items_SlotsColumn},
    {LanternDb.Items_IdFileColumn},
    {LanternDb.Items_MaterialColumn},
    {LanternDb.Items_ColorColumn}
from {LanternDb.ItemsTable}
where
    {LanternDb.Items_NameColumn} like @name
    and {LanternDb.Items_SlotsColumn} & @slot > 0";

        private static readonly string DoorsInZoneQuery =
$@"select 
    {LanternDb.Doors_NameColumn},
    {LanternDb.Doors_PosXColumn},
    {LanternDb.Doors_PosYColumn},
    {LanternDb.Doors_PosZColumn},
    {LanternDb.Doors_OpenTypeColumn},
    {LanternDb.Doors_HeadingColumn},
    {LanternDb.Doors_InclineColumn},
    {LanternDb.Doors_WidthColumn}
from {LanternDb.DoorsTable}
where {LanternDb.Doors_ZoneColumn} = @zone
    and {LanternDb.Doors_OpenTypeColumn} not in {LanternDb.DoorsToExclude}";

        private static readonly string PlayerCharactersInZoneQuery =
$@"select distinct 
    n.{LanternDb.Npc_IdColumn}, 
    n.{LanternDb.Npc_NameColumn}, 
    n.{LanternDb.Npc_RaceColumn}, 
    n.{LanternDb.Npc_GenderColumn},
    n.{LanternDb.Npc_FaceColumn} % 255 as {LanternDb.Npc_FaceColumn},
    n.{LanternDb.Npc_TextureColumn},
    case when n.{LanternDb.Npc_MeleeTexture1Column} > 999 
        then 0 
        else n.{LanternDb.Npc_MeleeTexture1Column}
    end as {LanternDb.Npc_MeleeTexture1Column},
    case when n.{LanternDb.Npc_MeleeTexture2Column} > 999 
        then 0 
        else n.{LanternDb.Npc_MeleeTexture2Column}
    end as {LanternDb.Npc_MeleeTexture2Column},
    n.{LanternDb.Npc_HelmTextureColumn}, 
    n.{LanternDb.Npc_ChestTextureColumn}, 
    max(i.{LanternDb.Items_ColorColumn}) as {LanternDb.ChestColorAlias}, 
    n.{LanternDb.Npc_ArmTextureColumn}, 
    n.{LanternDb.Npc_BracerTextureColumn}, 
    n.{LanternDb.Npc_HandTextureColumn}, 
    n.{LanternDb.Npc_LegTextureColumn}, 
    n.{LanternDb.Npc_FeetTextureColumn}
from {LanternDb.SpawnTable} s2
join {LanternDb.SpawnGroupTable} sg on s2.{LanternDb.Spawn_SpawnGroupIdColumn} = sg.{LanternDb.SpawnGroup_IdColumn}
join {LanternDb.SpawnEntryTable} se on se.{LanternDb.SpawnEntry_SpawnGroupIdColumn} = sg.{LanternDb.SpawnGroup_IdColumn}
join {LanternDb.NpcTable} n on se.{LanternDb.SpawnEntry_NpcIdColumn} = n.{LanternDb.Npc_IdColumn}
left join {LanternDb.LootTableTable} lt on n.{LanternDb.Npc_LootTableIdColumn} = lt.{LanternDb.LootTable_IdColumn}
left join {LanternDb.LootTableEntriesTable} lte on lte.{LanternDb.LootTableEntries_LootTableIdColumn} = lt.{LanternDb.LootTable_IdColumn}
left join {LanternDb.LootDropEntriesTable} lde on 
    (lte.{LanternDb.LootTableEntries_LootDropIdColumn} = lde.{LanternDb.LootDropEntries_LootDropIdColumn} and lde.{LanternDb.LootDropEntries_ChanceColumn} > 99.9)
left join {LanternDb.ItemsTable} i on 
    (lde.{LanternDb.LootDropEntries_ItemIdColumn} = i.{LanternDb.Items_IdColumn} and i.{LanternDb.Items_SlotsColumn} & {(int)LanternDb.Slots.Chest} > 0)
where s2.{LanternDb.Spawn_ZoneColumn} = @zone
  and (n.{LanternDb.Npc_RaceColumn} < 13 or n.{LanternDb.Npc_RaceColumn} = 128)
group by n.{LanternDb.Npc_IdColumn}, n.{LanternDb.Npc_NameColumn}, n.{LanternDb.Npc_RaceColumn}, n.{LanternDb.Npc_GenderColumn}, n.{LanternDb.Npc_FaceColumn},
    n.{LanternDb.Npc_TextureColumn}, n.{LanternDb.Npc_MeleeTexture1Column}, n.{LanternDb.Npc_MeleeTexture2Column}, n.{LanternDb.Npc_HelmTextureColumn}, 
    n.{LanternDb.Npc_ChestTextureColumn}, n.{LanternDb.Npc_ArmTextureColumn}, n.{LanternDb.Npc_BracerTextureColumn}, n.{LanternDb.Npc_HandTextureColumn}, 
    n.{LanternDb.Npc_LegTextureColumn}, n.{LanternDb.Npc_FeetTextureColumn}
order by n.{LanternDb.Npc_IdColumn}";

        private static readonly string GlobalCharactersInZoneQuery =
$@"select distinct 
    n.{LanternDb.Npc_RaceColumn}, 
    n.{LanternDb.Npc_GenderColumn},
    n.{LanternDb.Npc_FaceColumn} % 255 as {LanternDb.Npc_FaceColumn},
    n.{LanternDb.Npc_TextureColumn},
    case when n.{LanternDb.Npc_MeleeTexture1Column} > 999 
        then 0 
        else n.{LanternDb.Npc_MeleeTexture1Column}
    end as {LanternDb.Npc_MeleeTexture1Column},
    case when n.{LanternDb.Npc_MeleeTexture2Column} > 999 
        then 0 
        else n.{LanternDb.Npc_MeleeTexture2Column}
    end as {LanternDb.Npc_MeleeTexture2Column},
    n.{LanternDb.Npc_HelmTextureColumn}
from {LanternDb.SpawnTable} s2
join {LanternDb.SpawnGroupTable} sg on s2.{LanternDb.Spawn_SpawnGroupIdColumn} = sg.{LanternDb.SpawnGroup_IdColumn}
join {LanternDb.SpawnEntryTable} se on se.{LanternDb.SpawnEntry_SpawnGroupIdColumn} = sg.{LanternDb.SpawnGroup_IdColumn}
join {LanternDb.NpcTable} n on se.{LanternDb.SpawnEntry_NpcIdColumn} = n.{LanternDb.Npc_IdColumn}
where s2.{LanternDb.Spawn_ZoneColumn} = @zone
  and n.{LanternDb.Npc_RaceColumn} in {LanternDb.GlobalRaceIds}
order by n.{LanternDb.Npc_RaceColumn}, n.{LanternDb.Npc_GenderColumn}
";

        private static readonly string NpcsWithVariationsQuery =
$@"select distinct 
    n.{LanternDb.Npc_RaceColumn}, 
    n.{LanternDb.Npc_GenderColumn},
    n.{LanternDb.Npc_FaceColumn} % 255 as {LanternDb.Npc_FaceColumn},
    n.{LanternDb.Npc_TextureColumn},
    case when n.{LanternDb.Npc_MeleeTexture1Column} > 999 
        then 0 
        else n.{LanternDb.Npc_MeleeTexture1Column}
    end as {LanternDb.Npc_MeleeTexture1Column},
    case when n.{LanternDb.Npc_MeleeTexture2Column} > 999 
        then 0 
        else n.{LanternDb.Npc_MeleeTexture2Column}
    end as {LanternDb.Npc_MeleeTexture2Column},
    n.{LanternDb.Npc_HelmTextureColumn}
from {LanternDb.SpawnTable} s2
join {LanternDb.SpawnGroupTable} sg on s2.{LanternDb.Spawn_SpawnGroupIdColumn} = sg.{LanternDb.SpawnGroup_IdColumn}
join {LanternDb.SpawnEntryTable} se on se.{LanternDb.SpawnEntry_SpawnGroupIdColumn} = sg.{LanternDb.SpawnGroup_IdColumn}
join {LanternDb.NpcTable} n on se.{LanternDb.SpawnEntry_NpcIdColumn} = n.{LanternDb.Npc_IdColumn}
where s2.{LanternDb.Spawn_ZoneColumn} = @zone
  and not (n.{LanternDb.Npc_RaceColumn} < 13 or n.{LanternDb.Npc_RaceColumn} = 127 or n.{LanternDb.Npc_RaceColumn} = 128)
  and not n.{LanternDb.Npc_RaceColumn} in {LanternDb.GlobalRaceIds}
  and (n.{LanternDb.Npc_TextureColumn} > 0 or n.{LanternDb.Npc_HelmTextureColumn} > 0 or
    (n.{LanternDb.Npc_MeleeTexture1Column} > 0 and n.{LanternDb.Npc_MeleeTexture1Column} < 1000) or 
    (n.{LanternDb.Npc_MeleeTexture2Column} > 0 and n.{LanternDb.Npc_MeleeTexture2Column} < 1000) or
    (n.{LanternDb.Npc_FaceColumn} > 0 and n.{LanternDb.Npc_FaceColumn} <> 255))
order by n.{LanternDb.Npc_RaceColumn}, n.{LanternDb.Npc_GenderColumn}
";
	}

    public class Item
    {
        public string Name { get; set; }
        public string IdFile { get; set; }
        public int Color { get; set; }
        public int Material { get; set; }
        public int Slots { get; set; }
    }

    public class Door
    {
        public string Name { get; set; }
        public Vector3 Position { get; set; }
        public int OpenType { get; set; } // Maybe should be an enum
        public double Heading { get; set; }
        public int Incline { get; set; }
        public int Width { get; set; }
    }

    public class Npc : ICharacterModel
	{
        public int Race { get; set; }
        public NpcGender Gender { get; set; }
        public int Face { get; set; }
        public int Texture { get; set; }
        public int Primary { get; set; }
        public int Secondary { get; set; }
        public int HelmTexture { get; set; }

		public bool TryGetMaterialVariation(string imageName, out int variationIndex, out Color? color)
		{
            color = null;
			if (imageName.Contains("he00") && (imageName.EndsWith("1") || imageName.EndsWith("2"))
                && NpcsWithFaceVariations.Contains(Race))
			{
                variationIndex = Face;
                return true;
			}
            variationIndex = Texture;

            if (variationIndex == 0) return false;

            variationIndex -= 1;
            return true;
		}

        public bool ShouldSkipMeshGenerationForMaterial(string materialName) => false;

        private static readonly HashSet<int> NpcsWithFaceVariations = new HashSet<int>()
        { 13, 60, 71, 183, 188 };
	}

    internal sealed class LanternDb
    {
        // ==== ITEMS ====
        public const string ItemsTable = "items";

        public const string Items_IdColumn = "id";
        public const string Items_SlotsColumn = "slots";
        public const string Items_NameColumn = "name";
        public const string Items_IdFileColumn = "idfile";
        public const string Items_ColorColumn = "color";
        public const string Items_MaterialColumn = "material";

		// ==== DOORS ====
		public const string DoorsTable = "doors";

        public const string Doors_NameColumn = "name";
        public const string Doors_ZoneColumn = "zone";
        public const string Doors_PosXColumn = "pos_x";
        public const string Doors_PosYColumn = "pos_y";
        public const string Doors_PosZColumn = "pos_z";
        public const string Doors_OpenTypeColumn = "opentype";
        public const string Doors_HeadingColumn = "heading";
        public const string Doors_InclineColumn = "incline";
        public const string Doors_WidthColumn = "width";

		// ==== SPAWN ====
		public const string SpawnTable = "alkabor_spawn2";

        public const string Spawn_SpawnGroupIdColumn = "spawngroupID";
        public const string Spawn_ZoneColumn = "zone";

		// ==== SPAWNGROUP ====
		public const string SpawnGroupTable = "alkabor_spawngroup";

        public const string SpawnGroup_IdColumn = "id";

		// ==== SPAWNENTRY ====
		public const string SpawnEntryTable = "alkabor_spawnentry";

        public const string SpawnEntry_SpawnGroupIdColumn = "spawngroupID";
        public const string SpawnEntry_NpcIdColumn = "npcID";

        // ==== NPC ====
        public const string NpcTable = "alkabor_npc_types";

        public const string Npc_IdColumn = "id";
        public const string Npc_NameColumn = "name";
        public const string Npc_RaceColumn = "race";
        public const string Npc_GenderColumn = "gender";
        public const string Npc_FaceColumn = "face";
        public const string Npc_TextureColumn = "texture";
        public const string Npc_MeleeTexture1Column = "d_melee_texture1";
        public const string Npc_MeleeTexture2Column = "d_melee_texture2";
        public const string Npc_HelmTextureColumn = "helmtexture";
        public const string Npc_ChestTextureColumn = "chesttexture";
        public const string Npc_ArmTextureColumn = "armtexture";
        public const string Npc_BracerTextureColumn = "bracertexture";
        public const string Npc_HandTextureColumn = "handtexture";
        public const string Npc_LegTextureColumn = "legtexture";
        public const string Npc_FeetTextureColumn = "feettexture";
        public const string Npc_LootTableIdColumn = "loottable_id";

		// ==== LOOTTABLE ====
		public const string LootTableTable = "alkabor_loottable";

        public const string LootTable_IdColumn = "id";

		// ==== LOOTTABLEENTRIES ====
		public const string LootTableEntriesTable = "alkabor_loottable_entries";

        public const string LootTableEntries_LootTableIdColumn = "loottable_id";
        public const string LootTableEntries_LootDropIdColumn = "lootdrop_id";

        // ==== LOOTDROPENTRIES ====
        public const string LootDropEntriesTable = "alkabor_lootdrop_entries";

        public const string LootDropEntries_LootDropIdColumn = "lootdrop_id";
        public const string LootDropEntries_ChanceColumn = "chance";
        public const string LootDropEntries_ItemIdColumn = "item_id";

		public const string ChestColorAlias = "chestcolor";

		[Flags]
        public enum Slots : int
        {
            None = 0,
            Charm = 1,
            L_Ear = 2,
            Head = 4,
            Face = 8,
            R_Ear = 16,
            Neck = 32,
            Shoulder = 64,
            Arms = 128,
            Back = 256,
            L_Wrist = 512,
            R_Wrist = 1024,
            Ranged = 2048,
            Hands = 4096,
            Primary = 8192,
            Secondary = 16384,
            L_Ring = 32768,
            R_Ring = 65536,
            Chest = 131072,
            Legs = 262144,
            Feet = 524288,
            Waist = 1048576,
            Ammo = 2097152
        }

        public const string DoorsToExclude = "(50, 53, 54, 55)"; // 50, 53, 54 are invis, 55 is not classic
        public const string GlobalRaceIds = "(14, 60, 75, 108, 120, 141, 161, 209, 210, 211, 212)";
    }
}
