using System;
using System.Globalization;
using Shared;
using Shared.pass;
using WorldServer.core.objects;
using WorldServer.core.quests;
using WorldServer.core.worlds;

namespace WorldServer.core.commands
{
    public abstract partial class Command
    {
        // Testing the Realm Pass on a test server: add XP, jump to a tier, make the pass Gilded
        // without gold, start afresh. The season follows the quests' clock (/questadmin time).
        internal class PassAdminCommand : Command
        {
            public override RankingType RankRequirement => RankingType.Admin;
            public override string CommandName => "passadmin";

            protected override bool Process(Player player, TickTime time, string args)
            {
                var pass = player.Pass;
                var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (pass == null || parts.Length == 0)
                {
                    player.SendInfo("Usage: /passadmin xp <n> | tier <n> | gilded | reset [all] | time <hours> | look <aura> [skin] | petfx <id>");
                    return true;
                }
                var book = pass.Book;
                switch (parts[0].ToLowerInvariant())
                {
                    case "xp":
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var xp))
                            return false;
                        pass.Gain(PassSource.Staff, xp);
                        player.SendInfo($"Pass XP {book.State.Xp:N0}, tier {book.Tier}.");
                        break;
                    case "tier":
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var tier))
                            return false;
                        var target = Math.Clamp(tier, 0, book.Data.Tiers) * book.Data.XpPerTier;
                        if (target > book.State.Xp)
                            pass.Gain(PassSource.Staff, target - book.State.Xp);
                        else
                            book.State.Xp = target;
                        pass.Save();
                        if (pass.Subscribed)
                            pass.SendData();
                        player.SendInfo($"Pass XP {book.State.Xp:N0}, tier {book.Tier}.");
                        break;
                    case "gilded":
                        if (!book.Active)
                            return false;
                        book.MarkGilded();
                        pass.Save();
                        if (pass.Subscribed)
                            pass.SendData();
                        player.SendInfo("Your pass is Gilded (no gold taken).");
                        break;
                    case "reset":
                        pass.ResetState(parts.Length > 1 && parts[1] == "all");
                        player.SendInfo("Your pass starts afresh" + (parts.Length > 1 && parts[1] == "all" ? ", cosmetics and all." : "; your auras, name colours and pet effects are kept."));
                        break;
                    case "time":
                        if (parts.Length < 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
                            return false;
                        QuestTracker.ClockOffsetHours = hours;
                        player.SendInfo($"The quests' and pass's clock is {hours} hours ahead: {QuestTracker.Now:yyyy-MM-dd HH:mm} UTC. Rejoin a world for the new season.");
                        pass.Act(PassTracker_REQUEST, 0);
                        break;
                    case "look":
                    {
                        // an aura (granted and worn; 0 none) and, optionally, a skin by id worn
                        // now, as the Armoire would, owned or not
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var aura) || (aura != 0 && book.Data.Aura(aura) == null))
                        {
                            player.SendInfo("Usage: /passadmin look <aura id, 0 none> [skin id, as \"Winterguard\"]");
                            return true;
                        }
                        if (aura != 0 && !book.State.Auras.Contains(aura))
                            book.State.Auras.Add(aura);
                        book.WearAura(aura);
                        player.Aura = book.WornAura;
                        if (parts.Length > 2)
                        {
                            var skin = WorldServer.core.pass.PassTracker.SkinOf(player.GameServer.Resources.GameData, string.Join(' ', parts, 2, parts.Length - 2));
                            if (skin == null || skin.PlayerClassType != player.ObjectType)
                            {
                                player.SendInfo("No such skin for this class.");
                                return true;
                            }
                            player.Skin = skin.Type;
                            player.Size = skin.Size;
                        }
                        pass.Save();
                        if (pass.Subscribed)
                            pass.SendData();
                        player.SendInfo($"Aura {aura}, skin {player.Skin}.");
                        break;
                    }
                    case "petfx":
                    {
                        // a pet effect, granted and put on the pet (0 none)
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var fx) || (fx != 0 && book.Data.PetEffect(fx) == null))
                        {
                            player.SendInfo("Usage: /passadmin petfx <pet effect id, 0 none>");
                            return true;
                        }
                        if (fx != 0 && !book.State.PetEffects.Contains(fx))
                            book.State.PetEffects.Add(fx);
                        book.WearPetEffect(fx);
                        player.PetEffect = book.WornPetEffect;
                        pass.Save();
                        if (pass.Subscribed)
                            pass.SendData();
                        player.SendInfo($"Pet effect {fx}.");
                        break;
                    }
                    default:
                        return false;
                }
                return true;
            }

            private const byte PassTracker_REQUEST = WorldServer.core.pass.PassTracker.REQUEST;
        }
    }
}
