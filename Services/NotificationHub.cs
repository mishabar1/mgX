using MG.Server.BL;
using MG.Server.Controllers;
using MG.Server.Database;
using MG.Server.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace MG.Server.Services
{
    //public class NotificationModel
    //{
    //    public string Data { get; set; }
    //    public string NotificationType { get; set; }
    //    public NotificationModel(string t, string d)
    //    {
    //        Data = d;
    //        NotificationType = t;
    //    }
    //}

    // AUTHENTICATED. Every method here was reachable anonymously before, which is what made
    // ExecuteAction's client-supplied `playerId` a free-for-all: any connected client could act
    // as any seat in any game. The JWT already rides the SignalR handshake (Program.cs reads it
    // from the access_token query string for this path), so this only rejects clients that were
    // never signed in.
    [Authorize]
    public class NotificationHub : Hub
    {
        /// <summary>
        /// The signed-in user id for this connection, from the JWT's NameIdentifier claim
        /// (TokenService.CreateToken). This — never anything off the wire — is what identifies
        /// the caller.
        /// </summary>
        private string? CallerUserId => Context.UserIdentifier;

        readonly GameBL _gameBL;
        private readonly ILogger<NotificationHub> _logger;
        public NotificationHub(GameBL gameBL, DataRepository dataRepository, ILogger<NotificationHub> logger) :base()
        {
            _gameBL = gameBL;
            _logger = logger;
        }

        // (H1) async Task, not async void — exceptions are now observable instead of crashing the process.
        public async Task SetConnectionIDUser(string? userId)
        {
            // Prefer the AUTHENTICATED id; the argument is now only a legacy fallback and is
            // never trusted over the token. (The client re-sends this on reconnect, so the
            // connection rejoins its user group instead of silently dropping out of it.)
            userId = CallerUserId ?? userId;

            // Guard: the client can connect before a user id is available; without this,
            // userId.ToString() threw a NullReferenceException that SignalR logged as a
            // failed hub invocation.
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("Hub connected (no user yet) conn={ConnectionId}", Context.ConnectionId);
                return;
            }
            _logger.LogInformation("Hub user registered user={UserId} conn={ConnectionId}", userId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }


        public async Task ExecuteAction(ExecuteActionData s)
        {
            _logger.LogInformation(
                "ExecuteAction action={Action} item={ItemId} player={PlayerId} game={GameId}",
                s?.actionId ?? "(none)", s?.itemId ?? "-", s?.playerId ?? "-", s?.gameId ?? "-");
            // Pass the AUTHENTICATED user id alongside the payload. BaseGameFlow refuses any
            // action whose claimed seat isn't occupied by this user.
            await _gameBL.ExecuteAction(s, CallerUserId);
        }

        // ============================================================
        // Voice chat (WebRTC) signaling.
        // The hub only RELAYS the WebRTC handshake between browsers — the audio itself
        // flows peer-to-peer, never through the server. One "voice room" per game.
        //   VoiceRooms: gameId -> (connectionId -> userName)
        // ============================================================
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> VoiceRooms = new();
        private static string VoiceGroup(string gameId) => "voice:" + gameId;

        // A client joins the game's voice room. The newcomer is handed the list of peers
        // already present (it will send each of them an offer); existing peers are told a
        // new peer joined (they will answer the incoming offer). This one-way offer rule
        // avoids "glare" (both sides offering at once).
        public async Task JoinVoice(string gameId, string? userName)
        {
            if (string.IsNullOrEmpty(gameId)) return;
            var room = VoiceRooms.GetOrAdd(gameId, _ => new ConcurrentDictionary<string, string>());

            var existing = room.Select(kv => new { connectionId = kv.Key, userName = kv.Value }).ToList();
            await Clients.Caller.SendAsync("VoicePeers", existing);

            room[Context.ConnectionId] = string.IsNullOrEmpty(userName) ? "player" : userName;
            await Groups.AddToGroupAsync(Context.ConnectionId, VoiceGroup(gameId));

            await Clients.OthersInGroup(VoiceGroup(gameId)).SendAsync("VoicePeerJoined",
                new { connectionId = Context.ConnectionId, userName = room[Context.ConnectionId] });

            _logger.LogInformation("Voice join game={GameId} conn={ConnectionId} size={Size}",
                gameId, Context.ConnectionId, room.Count);
        }

        public async Task LeaveVoice(string gameId)
        {
            await RemoveFromVoiceRoom(gameId, Context.ConnectionId);
        }

        // Relay a single WebRTC signaling message (offer / answer / ICE candidate) to one peer.
        public async Task VoiceSignal(string targetConnectionId, object data)
        {
            if (string.IsNullOrEmpty(targetConnectionId)) return;
            await Clients.Client(targetConnectionId).SendAsync("VoiceSignal",
                new { fromConnectionId = Context.ConnectionId, data });
        }

        // Live transcript / captions: a speaker's browser recognized some speech; fan it out
        // to everyone (each client filters by gameId). Text is capped to keep it sane.
        public async Task SendTranscript(string gameId, string? userName, string? text)
        {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrWhiteSpace(text)) return;
            var line = text.Trim();
            if (line.Length > 300) line = line[..300];
            await Clients.All.SendAsync("Transcript",
                new { gameId, userName = string.IsNullOrEmpty(userName) ? "player" : userName, text = line });
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Drop the connection from every voice room it was in and tell the peers.
            foreach (var kv in VoiceRooms)
            {
                if (kv.Value.ContainsKey(Context.ConnectionId))
                    await RemoveFromVoiceRoom(kv.Key, Context.ConnectionId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        private async Task RemoveFromVoiceRoom(string gameId, string connectionId)
        {
            if (VoiceRooms.TryGetValue(gameId, out var room) && room.TryRemove(connectionId, out _))
            {
                await Groups.RemoveFromGroupAsync(connectionId, VoiceGroup(gameId));
                await Clients.Group(VoiceGroup(gameId)).SendAsync("VoicePeerLeft", new { connectionId });
                if (room.IsEmpty) VoiceRooms.TryRemove(gameId, out _);
            }
        }

    }
}
