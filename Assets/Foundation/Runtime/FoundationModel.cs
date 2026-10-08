using System;
using System.Collections.Generic;
using System.Linq;

namespace MyGame.Foundation
{
    public enum SupplyType { Food, Water, HighValue, Ordinary }
    public enum Team { Unassigned, Red, Blue }
    public enum MatchPhase { Waiting, Preparing, Playing, Finished }

    [Serializable] public sealed class SupplyDefinition
    {
        public string id, name, desc, iconResource, specialDesc;
        public SupplyType type;
        public int value, hungerChange, thirstChange;
        public int maxStack = 10;
        public bool canGift = true;
        public bool CanUse { get { return type == SupplyType.Food || type == SupplyType.Water || !string.IsNullOrEmpty(specialDesc); } }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Supply ID/name required.");
            if (!Enum.IsDefined(typeof(SupplyType), type) || value < 0 || maxStack < 1) throw new ArgumentException("Invalid supply type/value/stack: " + id);
            if ((type == SupplyType.HighValue) != (value > 500)) throw new ArgumentException("HighValue must be >500; all other types <=500: " + id);
        }
    }
    [Serializable] public sealed class SupplyCatalog
    {
        public SupplyDefinition[] items;
        public void Validate()
        {
            if (items == null || items.Length < 2) throw new ArgumentException("At least two supplies required for mutually exclusive preferences.");
            var ids = new HashSet<string>();
            foreach (var item in items) { if (item == null) throw new ArgumentException("Null supply."); item.Validate(); if (!ids.Add(item.id)) throw new ArgumentException("Duplicate supply: " + item.id); }
        }
    }
    public sealed class ItemStack
    {
        public SupplyDefinition Item { get; private set; }
        public int Count { get; internal set; }
        public ItemStack(SupplyDefinition item, int count) { Item = item; Count = count; }
    }
    public sealed class Inventory
    {
        private readonly ItemStack[] slots;
        public int Capacity { get { return slots.Length; } }
        public event Action Changed;
        public Inventory(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException("capacity"); slots = new ItemStack[capacity]; }
        public ItemStack this[int index] { get { return slots[index]; } }
        // All-or-nothing: a failed pickup never deletes or partially moves world loot.
        public bool TryAdd(SupplyDefinition item, int count)
        {
            if (item == null || count < 1) return false;
            item.Validate();
            long room = 0;
            foreach (var slot in slots) room += slot == null ? item.maxStack : slot.Item.id == item.id ? item.maxStack - slot.Count : 0;
            if (room < count) return false;
            for (int i = 0; i < slots.Length && count > 0; i++)
                if (slots[i] != null && slots[i].Item.id == item.id) { int n = Math.Min(count, item.maxStack - slots[i].Count); slots[i].Count += n; count -= n; }
            for (int i = 0; i < slots.Length && count > 0; i++)
                if (slots[i] == null) { int n = Math.Min(count, item.maxStack); slots[i] = new ItemStack(item, n); count -= n; }
            Notify(); return true;
        }
        public bool RemoveOne(int index)
        {
            if (index < 0 || index >= slots.Length || slots[index] == null) return false;
            if (--slots[index].Count == 0) slots[index] = null;
            Notify(); return true;
        }
        public void Swap(int first, int second)
        {
            if (first < 0 || second < 0 || first >= slots.Length || second >= slots.Length) return;
            var old = slots[first]; slots[first] = slots[second]; slots[second] = old; Notify();
        }
        private void Notify() { if (Changed != null) Changed(); }
    }
    public sealed class PlayerState
    {
        public string Id { get; private set; }
        public Team Team { get; set; }
        public string Like { get; private set; }
        private readonly HashSet<string> dislikes = new HashSet<string>();
        public IEnumerable<string> Dislikes { get { return dislikes.ToArray(); } }
        public bool Confirmed { get; private set; }
        public float Hunger { get; private set; } = 100;
        public float Hydration { get; private set; } = 100;
        public PlayerState(string id) { if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Player ID required."); Id = id; }
        public bool SelectLike(string id)
        {
            if (Confirmed || string.IsNullOrEmpty(id)) return false;
            Like = id; dislikes.Remove(id); return true;
        }
        public bool ToggleDislike(string id)
        {
            if (Confirmed || string.IsNullOrEmpty(id) || id == Like) return false;
            if (dislikes.Remove(id)) return true;
            if (dislikes.Count >= 3) return false;
            dislikes.Add(id); return true;
        }
        public bool HasValidPreferences { get { return !string.IsNullOrEmpty(Like) && dislikes.Count >= 1 && dislikes.Count <= 3 && !dislikes.Contains(Like); } }
        public bool Confirm() { if (!HasValidPreferences) return false; Confirmed = true; return true; }
        internal void CompletePreferences(SupplyDefinition[] catalog, Random random)
        {
            var ids = catalog.Select(x => x.id).ToArray();
            dislikes.RemoveWhere(x => !ids.Contains(x));
            if (Like == null || !ids.Contains(Like))
            {
                var choices = ids.Where(x => !dislikes.Contains(x)).ToArray();
                if (choices.Length == 0) { dislikes.Remove(ids[0]); choices = new[] { ids[0] }; }
                Like = choices[random.Next(choices.Length)];
            }
            dislikes.Remove(Like);
            if (dislikes.Count == 0) { var choices = ids.Where(x => x != Like).ToArray(); dislikes.Add(choices[random.Next(choices.Length)]); }
            Confirm();
        }
        public void Apply(SupplyDefinition item)
        {
            Hunger = Math.Max(0, Math.Min(100, Hunger + item.hungerChange));
            Hydration = Math.Max(0, Math.Min(100, Hydration + item.thirstChange));
        }
    }
    public sealed class MatchSession
    {
        public const double PreparationSeconds = 10, MatchSeconds = 20 * 60;
        public MatchPhase Phase { get; private set; } = MatchPhase.Waiting;
        public double Remaining { get; private set; }
        public IReadOnlyList<PlayerState> Players { get; private set; }
        private readonly SupplyDefinition[] catalog;
        private readonly Random random;
        public event Action<MatchPhase> PhaseChanged;
        public MatchSession(SupplyCatalog catalog, int seed)
        { catalog.Validate(); this.catalog = catalog.items; random = new Random(seed); Players = new List<PlayerState>().AsReadOnly(); }
        // Invoke this only from the authoritative host after the room has been frozen.
        public bool Start(bool isHost, IEnumerable<PlayerState> players)
        {
            if (!isHost || Phase != MatchPhase.Waiting || players == null) return false;
            var roster = players.ToList();
            if (roster.Count < 2 || roster.Count % 2 != 0 || roster.Any(p => p == null || p.Confirmed) || roster.Select(p => p.Id).Distinct().Count() != roster.Count) return false;
            Players = roster.AsReadOnly(); Transition(MatchPhase.Preparing, PreparationSeconds); return true;
        }
        public void Tick(double delta)
        {
            if (delta < 0 || double.IsNaN(delta) || double.IsInfinity(delta)) throw new ArgumentOutOfRangeException("delta");
            if (Phase == MatchPhase.Preparing)
            {
                Remaining = Math.Max(0, Remaining - delta);
                if (Remaining <= 0 || Players.All(p => p.Confirmed))
                {
                    foreach (var p in Players) p.CompletePreferences(catalog, random);
                    BalanceTeams(); Transition(MatchPhase.Playing, MatchSeconds);
                }
            }
            else if (Phase == MatchPhase.Playing)
            { Remaining = Math.Max(0, Remaining - delta); if (Remaining <= 0) Transition(MatchPhase.Finished, 0); }
        }
        private void BalanceTeams()
        {
            // Start rejects odd rosters, so each team must have exactly half.
            var shuffled = Players.OrderBy(p => random.Next()).ToList();
            int red = shuffled.Count(p => p.Team == Team.Red), blue = shuffled.Count(p => p.Team == Team.Blue);
            int limit = shuffled.Count / 2;
            foreach (var p in shuffled)
                if ((p.Team == Team.Red && red > limit) || (p.Team == Team.Blue && blue > limit))
                { if (p.Team == Team.Red) red--; else blue--; p.Team = Team.Unassigned; }
            foreach (var p in shuffled.Where(p => p.Team == Team.Unassigned))
            { p.Team = red < blue ? Team.Red : blue < red ? Team.Blue : random.Next(2) == 0 ? Team.Red : Team.Blue; if (p.Team == Team.Red) red++; else blue++; }
        }
        private void Transition(MatchPhase phase, double seconds)
        { Phase = phase; Remaining = seconds; if (PhaseChanged != null) PhaseChanged(phase); }
    }
    [Serializable] public sealed class SpawnRule
    {
        public string id, name;
        public SupplyType[] types;
        public float chance = 1;
        public int minCount = 1, maxCount = 1;
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id) || float.IsNaN(chance) || chance < 0 || chance > 1 || minCount < 1 || maxCount < minCount || maxCount == int.MaxValue || types == null || types.Length == 0 || types.Any(x => !Enum.IsDefined(typeof(SupplyType), x))) throw new ArgumentException("Invalid spawn rule: " + id);
        }
        public ItemStack Roll(SupplyDefinition[] catalog, Random random, bool occupied)
        {
            Validate();
            if (occupied || random.NextDouble() >= chance) return null;
            var allowed = catalog.Where(x => types.Contains(x.type)).ToArray();
            if (allowed.Length == 0) return null;
            return new ItemStack(allowed[random.Next(allowed.Length)], random.Next(minCount, maxCount + 1));
        }
    }
}
