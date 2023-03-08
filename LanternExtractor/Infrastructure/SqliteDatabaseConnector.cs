using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }

    public class Item
    {
        public string Name { get; set; }
        public string IdFile { get; set; }
        public int Color { get; set; }
        public int Material { get; set; }
        public int Slots { get; set; }
    }
    internal sealed class LanternDb
    {
        public const string ItemsTable = "items";

        public const string Items_SlotsColumn = "slots";
        public const string Items_NameColumn = "name";
        public const string Items_IdFileColumn = "idfile";
        public const string Items_ColorColumn = "color";
        public const string Items_MaterialColumn = "material";

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
    }
}
