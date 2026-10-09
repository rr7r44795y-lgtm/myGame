using System;
using System.Collections.Generic;
using System.Linq;

namespace MyGame.Foundation
{
    [Serializable] public sealed class SupplyMasterRow
    {
        public string id, name, desc, iconResource;
        public SupplyType type;
        public int value, maxStack = 10;
        public bool canGift = true;
    }
    [Serializable] public sealed class FoodRow { public string id, key, name, specialDesc; public int HPchange; }
    [Serializable] public sealed class WaterRow { public string id, key, name, specialDesc; public int MPchange; }
    [Serializable] public sealed class HighValueRow { public string id, key, name, specialDesc; }
    [Serializable] public sealed class OrdinaryRow { public string id, key, name; }
    [Serializable] public sealed class SpawnAddress { public float x, y, z; }
    [Serializable] public sealed class DropLocationRow
    {
        public string id, name;
        public SupplyType[] type;
        public int number;
        public SpawnAddress address;
        // Optional configuration beyond the six wiki columns.
        public float chance = 1;
    }
    [Serializable] public sealed class SupplyTables
    {
        public SupplyMasterRow[] supplies;
        public FoodRow[] foods;
        public WaterRow[] waters;
        public HighValueRow[] highValues;
        public OrdinaryRow[] ordinary;
        public DropLocationRow[] dropLocations;
        public SupplyCatalog ToCatalog()
        {
            if (supplies == null || foods == null || waters == null || highValues == null || ordinary == null || dropLocations == null) throw new ArgumentException("All six supply tables are required.");
            var definitions = new List<SupplyDefinition>(); var linked = new HashSet<string>();
            var master = supplies.ToDictionary(x => x.id);
            ValidateRows(foods.Select(x => Tuple.Create(x.id, x.key)), master, SupplyType.Food, linked);
            ValidateRows(waters.Select(x => Tuple.Create(x.id, x.key)), master, SupplyType.Water, linked);
            ValidateRows(highValues.Select(x => Tuple.Create(x.id, x.key)), master, SupplyType.HighValue, linked);
            ValidateRows(ordinary.Select(x => Tuple.Create(x.id, x.key)), master, SupplyType.Ordinary, linked);
            foreach (var row in supplies)
            {
                if (!linked.Contains(row.id)) throw new ArgumentException("Missing subtype row: " + row.id);
                var item = new SupplyDefinition { id = row.id, name = row.name, desc = row.desc, type = row.type, value = row.value, maxStack = row.maxStack, canGift = row.canGift, iconResource = row.iconResource };
                if (row.type == SupplyType.Food) { var food = foods.Single(x => x.key == row.id); item.hungerChange = food.HPchange; item.specialDesc = food.specialDesc; }
                if (row.type == SupplyType.Water) { var water = waters.Single(x => x.key == row.id); item.thirstChange = water.MPchange; item.specialDesc = water.specialDesc; }
                if (row.type == SupplyType.HighValue) item.specialDesc = highValues.Single(x => x.key == row.id).specialDesc;
                definitions.Add(item);
            }
            var pointIds = new HashSet<string>();
            foreach (var row in dropLocations)
            {
                if (row.address == null || !pointIds.Add(row.id)) throw new ArgumentException("Missing/duplicate drop address: " + row.id);
                float[] coordinates = { row.address.x, row.address.y, row.address.z };
                if (coordinates.Any(x => float.IsNaN(x) || float.IsInfinity(x))) throw new ArgumentException("Invalid drop address: " + row.id);
                var rule = new SpawnRule { id = row.id, name = row.name, types = row.type, minCount = row.number, maxCount = row.number, chance = row.chance }; rule.Validate();
                if (!definitions.Any(x => row.type.Contains(x.type))) throw new ArgumentException("Drop point has no matching supply: " + row.id);
            }
            var catalog = new SupplyCatalog { items = definitions.ToArray() }; catalog.Validate(); return catalog;
        }
        private static void ValidateRows(IEnumerable<Tuple<string, string>> rows, Dictionary<string, SupplyMasterRow> master, SupplyType expected, HashSet<string> linked)
        {
            var ids = new HashSet<string>();
            foreach (var pair in rows)
            {
                SupplyMasterRow row;
                if (string.IsNullOrWhiteSpace(pair.Item1) || !ids.Add(pair.Item1) || string.IsNullOrWhiteSpace(pair.Item2) || !master.TryGetValue(pair.Item2, out row) || row.type != expected || !linked.Add(pair.Item2)) throw new ArgumentException("Invalid subtype ID/key/type: " + pair.Item1);
            }
        }
    }
}
