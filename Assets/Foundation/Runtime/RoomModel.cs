using System;
using System.Collections.Generic;
using System.Linq;

namespace MyGame.Foundation
{
    public enum FrontendStage { Lobby, Room, InGame }
    public sealed class RoomMember
    {
        public PlayerState Player { get; private set; }
        public string Name { get; private set; }
        public bool Ready { get; internal set; }
        public bool MicrophoneOpen { get; set; }
        public bool Speaking { get; set; }
        public RoomMember(string id, string name) { Player = new PlayerState(id); Name = RoomModel.ValidName(name); }
    }
    public sealed class RoomModel
    {
        public string Name { get; private set; }
        public string Code { get; private set; }
        public string HostId { get; private set; }
        public const int Capacity = 10;
        public bool Started { get; private set; }
        public bool Closed { get; private set; }
        // v2 allows host start during waiting; v1 required all other members ready.
        public bool RequireAllReady { get; set; }
        private readonly List<RoomMember> members = new List<RoomMember>();
        private readonly Queue<string> chat = new Queue<string>();
        public IReadOnlyList<RoomMember> Members { get { return members.AsReadOnly(); } }
        public IEnumerable<string> Chat { get { return chat.ToArray(); } }
        public RoomModel(string name, string code, RoomMember host)
        {
            if (host == null || string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Host and room code required.");
            Name = ValidName(name); Code = code; HostId = host.Player.Id; members.Add(host);
        }
        public static string ValidName(string name)
        {
            if (name == null) throw new ArgumentException("Name required.");
            name = name.Trim();
            if (name.Length < 1 || name.Length > 32 || name.Any(char.IsControl)) throw new ArgumentException("Name must contain 1-32 visible characters.");
            return name;
        }
        public bool Join(RoomMember member)
        {
            if (Closed || Started || member == null || members.Count >= Capacity || members.Any(x => x.Player.Id == member.Player.Id)) return false;
            members.Add(member); return true;
        }
        public bool ToggleReady(string playerId)
        {
            var member = members.FirstOrDefault(x => x.Player.Id == playerId);
            if (Closed || Started || member == null) return false;
            member.Ready = !member.Ready; return true;
        }
        public bool Kick(string actorId, string targetId)
        {
            if (Closed || Started || actorId != HostId || targetId == HostId) return false;
            return members.RemoveAll(x => x.Player.Id == targetId) > 0;
        }
        public bool Leave(string playerId)
        {
            if (Closed || Started || !members.Any(x => x.Player.Id == playerId)) return false;
            if (playerId == HostId) { Closed = true; members.Clear(); return true; }
            members.RemoveAll(x => x.Player.Id == playerId); return true;
        }
        public bool CanStart(string actorId)
        {
            return !Closed && !Started && actorId == HostId && members.Count >= 2 && members.Count % 2 == 0 &&
                (!RequireAllReady || members.Where(x => x.Player.Id != HostId).All(x => x.Ready));
        }
        public bool TryStart(string actorId, MatchSession session)
        {
            if (!CanStart(actorId) || session == null || !session.Start(true, members.Select(x => x.Player))) return false;
            Started = true; return true;
        }
        public bool SendChat(string senderId, string text)
        {
            var sender = members.FirstOrDefault(x => x.Player.Id == senderId);
            if (Closed || Started || sender == null || string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim(); if (text.Length > 200 || text.Any(char.IsControl)) return false;
            chat.Enqueue(sender.Name + ": " + text); while (chat.Count > 6) chat.Dequeue(); return true;
        }
    }
    // Local demo directory. Replace with an authenticated network/Steam adapter in production.
    public sealed class LocalRoomDirectory
    {
        private readonly List<RoomModel> rooms = new List<RoomModel>();
        public RoomModel Create(string name, RoomMember host)
        {
            string code;
            do { code = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant(); } while (rooms.Any(x => x.Code == code));
            var room = new RoomModel(name, code, host); rooms.Add(room); return room;
        }
        public IEnumerable<RoomModel> Search(string code)
        {
            return rooms.Where(x => !x.Closed && !x.Started && x.Members.Count < RoomModel.Capacity &&
                (string.IsNullOrWhiteSpace(code) || x.Code.IndexOf(code.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        }
    }
}
