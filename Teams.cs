using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Newtonsoft.Json.Linq;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;

namespace MatchZy
{

    public class Team 
    {
        [JsonPropertyName("id")]
        public string id = "";

        [JsonPropertyName("teamname")]
        public required string teamName;

        [JsonPropertyName("teamflag")]
        public string teamFlag = "";

        [JsonPropertyName("teamtag")]
        public string teamTag = "";

        [JsonPropertyName("teamplayers")]
        public JToken? teamPlayers;

        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public HashSet<CCSPlayerController> coach = [];

        [JsonPropertyName("seriesscore")]
        public int seriesScore = 0;
    }

    public partial class MatchZy
    {
        [ConsoleCommand("css_coach", "Sets coach for the requested team")]
        public void OnCoachCommand(CCSPlayerController? player, CommandInfo command) 
        {
            HandleCoachCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_uncoach", "Sets coach for the requested team")]
        public void OnUnCoachCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !player.PlayerPawn.IsValid) return;
            if (isPractice) {
                ReplyToUserCommand(player, "取消教练命令只能在比赛模式下使用！");
                return;
            }

            if (matchzyTeam1.coach.Contains(player)) {
                player.Clan = "";
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else if (matchzyTeam2.coach.Contains(player)) {
                player.Clan = "";
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else {
                ReplyToUserCommand(player, "你没有担任任何队伍的教练！");
                return;
            }

            if (player.InGameMoneyServices != null) player.InGameMoneyServices.Account = 0;

            ReplyToUserCommand(player, "你已不再担任任何队伍的教练！");
        }

        [ConsoleCommand("matchzy_addplayer", "Adds player to the provided team")]
        [ConsoleCommand("get5_addplayer", "Adds player to the provided team")]
        public void OnAddPlayerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player != null || command == null) return;
            if (!isMatchSetup) {
                command.ReplyToCommand("尚未设置比赛！");
                return;
            }
            if (IsHalfTimePhase())
            {
                command.ReplyToCommand("半场休息时无法添加玩家，请等到下一回合开始。");
                return;
            }
            if (command.ArgCount < 3)
            {
                command.ReplyToCommand("用法: matchzy_addplayer <steam64> <team> \"<名称>\"");
                return; 
            }

            string playerSteamId = command.ArgByIndex(1);
            string playerTeam = command.ArgByIndex(2);
            string playerName = command.ArgByIndex(3);
            bool success;
            if (playerTeam == "team1")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchzyTeam1.teamPlayers);
            } else if (playerTeam == "team2")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchzyTeam2.teamPlayers);
            } else if (playerTeam == "spec")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchConfig.Spectators);
            } else 
            {
                command.ReplyToCommand("未知队伍：必须是team1、team2或spec之一");
                return; 
            }
            if (!success)
            {
                command.ReplyToCommand($"无法添加玩家 {playerName} 到 {playerTeam}。玩家可能已在其他队伍中或您提供了无效的Steam ID。");
                return;
            }
            command.ReplyToCommand($"玩家 {playerName} 已成功添加到 {playerTeam}！");
        }

        [ConsoleCommand("matchzy_removeplayer", "Removes the player from all the teams")]
        [ConsoleCommand("get5_removeplayer", "Removes the player from all the teams")]
        [CommandHelper(minArgs: 1, usage: "<steam64>")]
        public void OnRemovePlayerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player != null || command == null) return;
            if (!isMatchSetup) {
                command.ReplyToCommand("尚未设置比赛！");
                return;
            }
            if (IsHalfTimePhase())
            {
                command.ReplyToCommand("半场休息时无法移除玩家，请等到下一回合开始。");
                return;
            }

            string arg = command.GetArg(1);

            if (!ulong.TryParse(arg, out ulong steamId))
            {
                command.ReplyToCommand($"无效的Steam64 ID");
            }

            bool success = RemovePlayerFromTeam(steamId.ToString());
            if (success)
            {
                command.ReplyToCommand($"成功移除玩家 {steamId}");
                CCSPlayerController? removedPlayer = Utilities.GetPlayerFromSteamId(steamId);
                if (IsPlayerValid(removedPlayer))
                {
                    Log($"Kicking player {removedPlayer!.PlayerName} - Not a player in this game (removed).");
                    PrintToAllChat($"踢出玩家 {removedPlayer!.PlayerName} - 不在当前游戏中。");
                    KickPlayer(removedPlayer);
                }
            }
            else
            {
                command.ReplyToCommand($"未在任何队伍中找到玩家 {steamId}，或Steam ID无效。");
            }
        }

        public bool AddPlayerToTeam(string steamId, string name, JToken? team)
        {
            if (matchzyTeam1.teamPlayers != null && matchzyTeam1.teamPlayers[steamId] != null) return false;
            if (matchzyTeam2.teamPlayers != null && matchzyTeam2.teamPlayers[steamId] != null) return false;
            if (matchConfig.Spectators != null && matchConfig.Spectators[steamId] != null) return false;

            if (team is JObject jObjectTeam)
            {
                jObjectTeam.Add(steamId, name);
                LoadClientNames();
                return true;
            }
            else if (team is JArray jArrayTeam)
            {
                jArrayTeam.Add(name);
                LoadClientNames();
                return true;
            }
            return false;
        }

        public bool RemovePlayerFromTeam(string steamId)
        {
            List<JToken?> teams = [matchzyTeam1.teamPlayers, matchzyTeam2.teamPlayers, matchConfig.Spectators];

            foreach (var team in teams)
            {
                if (team is null) continue;
                if (team is JObject jObjectTeam)
                {
                    jObjectTeam.Remove(steamId);
                    return true;
                }
                else if (team is JArray jArrayTeam)
                {
                    jArrayTeam.Remove(steamId);
                    return true;
                }
            }
            return false;
        }
    }
}
