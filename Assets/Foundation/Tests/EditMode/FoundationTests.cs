using System;
using System.Linq;
using NUnit.Framework;

namespace MyGame.Foundation.Tests
{
    public sealed class FoundationTests
    {
        private static SupplyDefinition Item(string id, SupplyType type = SupplyType.Food)
        { return new SupplyDefinition { id = id, name = id, type = type, value = type == SupplyType.HighValue ? 501 : 500, maxStack = 2 }; }
        private static SupplyCatalog Catalog()
        { return new SupplyCatalog { items = new[] { Item("a"), Item("b", SupplyType.Water), Item("c"), Item("d"), Item("e") } }; }
        [Test] public void PickupIsAtomicWhenInventoryIsFull()
        {
            var bag = new Inventory(2); var food = Item("food"); var water = Item("water");
            Assert.IsTrue(bag.TryAdd(food, 1)); Assert.IsTrue(bag.TryAdd(water, 2));
            Assert.IsFalse(bag.TryAdd(food, 2)); Assert.AreEqual(1, bag[0].Count); Assert.AreEqual(2, bag[1].Count);
            Assert.IsTrue(bag.TryAdd(food, 1)); Assert.AreEqual(2, bag[0].Count);
        }
        [Test] public void PreferencesAreExclusiveAndLimited()
        {
            var p = new PlayerState("p"); Assert.IsFalse(p.Confirm()); p.SelectLike("a");
            Assert.IsFalse(p.ToggleDislike("a")); p.ToggleDislike("b"); p.ToggleDislike("c"); p.ToggleDislike("d");
            Assert.IsFalse(p.ToggleDislike("e")); p.SelectLike("b"); Assert.AreEqual(2, p.Dislikes.Count());
            Assert.IsTrue(p.Confirm()); Assert.IsFalse(p.SelectLike("c")); Assert.IsFalse(p.ToggleDislike("d"));
        }
        [Test] public void TimeoutPreservesSelectionsAndFillsMissingChoices()
        {
            var p = new PlayerState("p"); p.SelectLike("a"); var s = new MatchSession(Catalog(), 123);
            Assert.IsFalse(s.Start(false, new[] { p })); Assert.IsTrue(s.Start(true, new[] { p, new PlayerState("companion") })); s.Tick(9);
            Assert.AreEqual(MatchPhase.Preparing, s.Phase); s.Tick(1);
            Assert.AreEqual(MatchPhase.Playing, s.Phase); Assert.AreEqual("a", p.Like); Assert.IsTrue(p.HasValidPreferences); Assert.IsTrue(p.Confirmed);
            Assert.AreEqual(1200, s.Remaining); s.Tick(1200); Assert.AreEqual(MatchPhase.Finished, s.Phase);
        }
        [Test] public void AllConfirmedStartsEarlyButOneConfirmationDoesNot()
        {
            var a = new PlayerState("a"); var b = new PlayerState("b"); var s = new MatchSession(Catalog(), 3); s.Start(true, new[] { a, b });
            a.SelectLike("a"); a.ToggleDislike("b"); a.Confirm(); s.Tick(0); Assert.AreEqual(MatchPhase.Preparing, s.Phase);
            b.SelectLike("a"); b.ToggleDislike("b"); b.Confirm(); s.Tick(0); Assert.AreEqual(MatchPhase.Playing, s.Phase);
        }
        [TestCase(2)] [TestCase(4)] [TestCase(6)] [TestCase(10)]
        public void TeamsBalanceEvenWhenEveryoneChoseRed(int count)
        {
            var players = Enumerable.Range(0, count).Select(i => new PlayerState(i.ToString()) { Team = Team.Red }).ToArray();
            var s = new MatchSession(Catalog(), 9); s.Start(true, players); s.Tick(10);
            int red = players.Count(p => p.Team == Team.Red), blue = players.Count(p => p.Team == Team.Blue);
            Assert.AreEqual(red, blue); Assert.AreEqual(count, red + blue);
        }
        [Test] public void SpawnHonoursOccupationTypeChanceAndCount()
        {
            var rule = new SpawnRule { id = "p", types = new[] { SupplyType.Water }, minCount = 2, maxCount = 4 };
            Assert.IsNull(rule.Roll(Catalog().items, new Random(1), true));
            for (int i = 0; i < 100; i++) { var stack = rule.Roll(Catalog().items, new Random(i), false); Assert.AreEqual(SupplyType.Water, stack.Item.type); Assert.That(stack.Count, Is.InRange(2, 4)); }
            rule.chance = 0; Assert.IsNull(rule.Roll(Catalog().items, new Random(1), false));
        }
        [Test] public void SignedEffectsClampAndDoNotConfuseHungerWithHealth()
        {
            var p = new PlayerState("p"); p.Apply(new SupplyDefinition { hungerChange = -150, thirstChange = -30 });
            Assert.AreEqual(0, p.Hunger); Assert.AreEqual(70, p.Hydration); p.Apply(new SupplyDefinition { hungerChange = 500, thirstChange = 500 }); Assert.AreEqual(100, p.Hunger); Assert.AreEqual(100, p.Hydration);
        }
        [Test] public void ValueBoundaryAndCatalogKeysAreValidated()
        {
            var high = Item("high", SupplyType.HighValue); high.value = 500; Assert.Throws<ArgumentException>(() => high.Validate());
            var ordinary = Item("normal", SupplyType.Ordinary); ordinary.value = 501; Assert.Throws<ArgumentException>(() => ordinary.Validate());
            Assert.Throws<ArgumentException>(() => new SupplyCatalog { items = new[] { Item("same"), Item("same") } }.Validate());
        }
        [Test] public void BackpackSwapAndLastConsumptionClearSlots()
        {
            var bag = new Inventory(6); bag.TryAdd(Item("a"), 1); bag.Swap(0, 5); Assert.IsNull(bag[0]); Assert.AreEqual("a", bag[5].Item.id);
            Assert.IsTrue(bag.RemoveOne(5)); Assert.IsNull(bag[5]); Assert.IsFalse(bag.RemoveOne(5));
        }
        [Test] public void InvalidRosterAndClockCannotStartOrAdvanceMatch()
        {
            var session = new MatchSession(Catalog(), 1); Assert.IsFalse(session.Start(true, new[] { new PlayerState("same"), new PlayerState("same") }));
            Assert.IsFalse(session.Start(true, new[] { new PlayerState("one") }));
            Assert.IsFalse(session.Start(true, new[] { new PlayerState("one"), new PlayerState("two"), new PlayerState("three") }));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(double.NaN)); Assert.Throws<ArgumentOutOfRangeException>(() => session.Tick(-1));
        }
    }
}
