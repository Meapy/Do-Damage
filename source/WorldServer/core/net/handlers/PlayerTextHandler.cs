using System;
using Shared;
using WorldServer.networking;
using WorldServer.core.objects;
using WorldServer.core.worlds;

namespace WorldServer.core.net.handlers
{
    public class PlayerMessage
    {
        public Player Player { get; private set; }
        public string Message { get; private set; }
        public long Time { get; private set; }

        public PlayerMessage(Player player, TickTime time, string msg)
        {
            Player = player;
            Message = msg;
            Time = time.TotalElapsedMs;
        }
    }

    public class PlayerTextHandler : IMessageHandler
    {
        public override MessageId MessageId => MessageId.PLAYERTEXT;

        public override void Handle(Client client, NetworkReader rdr, ref TickTime tickTime)
        {
            var text = rdr.ReadUTF16();

            var player = client.Player;
            if (player?.World == null || text.Length > 512)
                return;

            var manager = player.GameServer;

            // /all and /local are chat channels, not commands: their text goes through the chat
            // checks below, as plain text does
            var channel = ChannelOf(text, out var body);

            // check for commands before other checks
            if (channel == null && text[0] == '/')
            {
                manager.CommandManager.Execute(player, tickTime, text);
                return;
            }

            if (channel != null && string.IsNullOrWhiteSpace(body))
            {
                player.SendError($"Usage: /{channel} <text>");
                return;
            }

            body ??= text;

            if (!player.NameChosen)
            {
                player.SendError("Please choose a name before chatting.");
                return;
            }

            if (player.Muted)
            {
                player.SendError("Muted. You can not talk at this time.");
                return;
            }

            if (player.CompareAndCheckSpam(body, tickTime.TotalElapsedMs))
            {
                return;
            }

            // save message for mob behaviors
            player.World.ChatReceived(player, body);

            if (channel == "all")
                manager.ChatManager.All(player, body);
            else
                manager.ChatManager.Say(player, body);
        }

        /// <summary>
        /// The channel a line is typed for: "all" or "local" for "/all text" and "/local text" (the
        /// text in <paramref name="body"/>), else null. "/all" alone is a channel with no text.
        /// </summary>
        private static string ChannelOf(string text, out string body)
        {
            body = null;
            foreach (var channel in new[] { "all", "local" })
            {
                var prefix = "/" + channel;
                if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (text.Length == prefix.Length)
                    return channel;
                if (text[prefix.Length] != ' ')
                    continue;
                body = text.Substring(prefix.Length + 1);
                return channel;
            }
            return null;
        }
    }
}
