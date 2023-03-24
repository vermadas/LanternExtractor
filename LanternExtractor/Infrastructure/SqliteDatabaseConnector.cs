using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Numerics;

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
                            OpenType = (int)reader[LanternDb.Doors_OpenType],
                            Heading = (double)reader[LanternDb.Doors_Heading],
                            Incline = (int)reader[LanternDb.Doors_Incline],
                            Width = (int)reader[LanternDb.Doors_Width]
                        };

                        doors.Add(door);
                    }
                }

                _connection.Close();
            }

            return doors;
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
    {LanternDb.Doors_OpenType},
    {LanternDb.Doors_Heading},
    {LanternDb.Doors_Incline},
    {LanternDb.Doors_Width}
from {LanternDb.DoorsTable}
where {LanternDb.Doors_ZoneColumn} = @zone
    and {LanternDb.Doors_OpenType} not in {LanternDb.DoorsToExclude}";
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

    internal sealed class LanternDb
    {
        public const string ItemsTable = "items";

        public const string Items_SlotsColumn = "slots";
        public const string Items_NameColumn = "name";
        public const string Items_IdFileColumn = "idfile";
        public const string Items_ColorColumn = "color";
        public const string Items_MaterialColumn = "material";

        public const string DoorsTable = "doors";

        public const string Doors_NameColumn = "name";
        public const string Doors_ZoneColumn = "zone";
        public const string Doors_PosXColumn = "pos_x";
        public const string Doors_PosYColumn = "pos_y";
        public const string Doors_PosZColumn = "pos_z";
        public const string Doors_OpenType = "opentype";
        public const string Doors_Heading = "heading";
        public const string Doors_Incline = "incline";
        public const string Doors_Width = "width";

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
    }
}
