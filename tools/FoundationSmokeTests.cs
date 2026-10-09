using System;
using System.Linq;
using MyGame.Foundation;
class FoundationSmokeTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static SupplyDefinition Item(string id) { return new SupplyDefinition { id = id, name = id, maxStack = 2 }; }
    static void Main()
    {
        var catalog = new SupplyCatalog { items = new[] { Item("a"), Item("b"), Item("c"), Item("d"), Item("e") } };
        var bag = new Inventory(2); Check(bag.TryAdd(catalog.items[0], 1), "add a"); Check(bag.TryAdd(catalog.items[1], 2), "add b"); Check(!bag.TryAdd(catalog.items[0], 2), "reject overflow"); Check(bag[0].Count == 1, "atomic pickup");
        var p = new PlayerState("p"); Check(!p.Confirm(), "reject missing prefs"); p.SelectLike("a"); Check(!p.ToggleDislike("a"), "exclusive"); p.ToggleDislike("b"); p.ToggleDislike("c"); p.ToggleDislike("d"); Check(!p.ToggleDislike("e"), "max dislikes");
        for (int count = 2; count <= 20; count += 2)
        {
            var players = Enumerable.Range(0, count).Select(i => new PlayerState(i.ToString()) { Team = Team.Red }).ToArray();
            var session = new MatchSession(catalog, count); Check(!session.Start(false, players), "host only"); Check(session.Start(true, players), "host starts"); session.Tick(9); Check(session.Phase == MatchPhase.Preparing, "10s preparation"); session.Tick(1);
            Check(session.Phase == MatchPhase.Playing && session.Remaining == 1200, "20min match"); Check(players.All(x => x.HasValidPreferences && x.Confirmed), "timeout preferences"); Check(Math.Abs(players.Count(x => x.Team == Team.Red) - players.Count(x => x.Team == Team.Blue)) == 0, "balanced teams"); session.Tick(1200); Check(session.Phase == MatchPhase.Finished, "ends once");
        }
        var early = new MatchSession(catalog, 1); p.Confirm(); var q = new PlayerState("q"); q.SelectLike("a"); q.ToggleDislike("b"); var r = new PlayerState("r"); r.SelectLike("a"); r.ToggleDislike("b"); early.Start(true, new[] { q, r }); q.Confirm(); r.Confirm(); early.Tick(0); Check(early.Phase == MatchPhase.Playing, "early confirmation");
        var odd = new MatchSession(catalog, 1); Check(!odd.Start(true, new[] { new PlayerState("solo") }), "reject solo"); Check(!odd.Start(true, new[] { new PlayerState("1"), new PlayerState("2"), new PlayerState("3") }), "reject odd roster");
        var rule = new SpawnRule { id = "point", types = new[] { SupplyType.Food }, minCount = 1, maxCount = 3 };
        Check(rule.Roll(catalog.items, new Random(1), true) == null, "occupied point");
        for (int seed = 0; seed < 100; seed++) { var stack = rule.Roll(catalog.items, new Random(seed), false); Check(stack != null && stack.Count >= 1 && stack.Count <= 3, "count range"); }
        q.Apply(new SupplyDefinition { hungerChange = -150, thirstChange = -30 }); Check(q.Hunger == 0 && q.Hydration == 70, "signed/clamped effects");
        var directory = new LocalRoomDirectory(); var room = directory.Create("Test room", new RoomMember("host", "Host"));
        Check(!room.CanStart("host"), "odd room cannot start"); Check(room.Join(new RoomMember("member", "Member")), "join waiting room");
        Check(room.CanStart("host") && !room.CanStart("member"), "host-only even start"); room.RequireAllReady = true; Check(!room.CanStart("host"), "v1 ready gate"); room.ToggleReady("member"); Check(room.CanStart("host"), "ready room");
        Check(!room.Kick("member", "host"), "member cannot kick"); Check(!room.Kick("host", "host"), "cannot kick host");
        for(int i=0;i<8;i++) Check(room.SendChat("member", "line"+i), "chat accepted"); Check(room.Chat.Count()==6, "bounded chat");
        var roomSession = new MatchSession(catalog, 3); Check(room.TryStart("host", roomSession), "room starts session"); Check(!room.Join(new RoomMember("late", "Late")), "no late joins"); Check(!directory.Search(room.Code).Any(), "started room hidden");
        var other = directory.Create("Other", new RoomMember("h2", "Host2")); other.Join(new RoomMember("m2", "Member2")); Check(other.Leave("m2") && !other.Closed, "member leaves without dissolving"); Check(other.Leave("h2") && other.Closed, "host dissolves");
        var tables = new SupplyTables {
            supplies = new[] { new SupplyMasterRow { id="f", name="Food", type=SupplyType.Food, value=20, maxStack=2 }, new SupplyMasterRow { id="w", name="Water", type=SupplyType.Water, value=30, maxStack=2 } },
            foods = new[] { new FoodRow { id="1", key="f", name="Food", HPchange=-15 } }, waters = new[] { new WaterRow { id="1", key="w", name="Water", MPchange=20 } },
            highValues = new HighValueRow[0], ordinary = new OrdinaryRow[0], dropLocations = new[] { new DropLocationRow { id="point", type=new[] { SupplyType.Water }, number=1, address=new SpawnAddress() } }
        };
        var loaded=tables.ToCatalog(); Check(loaded.items[0].hungerChange==-15 && loaded.items[1].thirstChange==20,"six-table signed mapping");
        tables.waters[0].key="f"; bool rejected=false; try { tables.ToCatalog(); } catch(ArgumentException) { rejected=true; } Check(rejected,"foreign-key type validation");
        tables.waters[0].key="w"; tables.dropLocations[0].number=0; rejected=false; try { tables.ToCatalog(); } catch(ArgumentException) { rejected=true; } Check(rejected,"invalid drop count rejected");
        Console.WriteLine("PASS: " + checks + " core assertions");
    }
}
