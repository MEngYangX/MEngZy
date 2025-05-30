using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.UserMessages;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Drawing;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using System;
using System.Collections.Generic;
using System.IO;

namespace MatchZy
{
    public partial class MatchZy
    {
        public const string warmupCfgPath = "MatchZy/warmup.cfg";
        public const string knifeCfgPath = "MatchZy/knife.cfg";
        public const string liveCfgPath = "MatchZy/live.cfg";
        public const string liveWingmanCfgPath = "MatchZy/live_wingman.cfg";

        private void PrintToAllChat(string message)
        {
            Server.PrintToChatAll($"{chatPrefix} {message}");
        }

        private void PrintToPlayerChat(CCSPlayerController player, string message)
        {
            player.PrintToChat($"{chatPrefix} {message}");
        }

        private void ReplyToUserCommand(CCSPlayerController? player, string message, bool console = false)
        {
            if (player == null)
            {
                Server.PrintToConsole($"{chatPrefix} {message}");
            }
            else
            {
                if (console)
                {
                    player.PrintToConsole($"{chatPrefix} {message}");
                }
                else
                {
                    player.PrintToChat($"{chatPrefix} {message}");
                }
            }
        }

        private void LoadAdmins()
        {
            string fileName = "MatchZy/admins.json";
            string filePath = Path.Join(Server.GameDirectory + "/csgo/cfg", fileName);

            if (File.Exists(filePath))
            {
                try
                {
                    using (StreamReader fileReader = File.OpenText(filePath))
                    {
                        string jsonContent = fileReader.ReadToEnd();
                        if (!string.IsNullOrEmpty(jsonContent))
                        {
                            JsonSerializerOptions options = new()
                            {
                                AllowTrailingCommas = true,
                            };
                            loadedAdmins = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
                        }
                        else
                        {
                            // Handle the case where the JSON content is empty or null
                            loadedAdmins = new Dictionary<string, string>();
                        }
                    }
                    foreach (var kvp in loadedAdmins)
                    {
                        Log($"[ADMIN] Username: {kvp.Key}, Role: {kvp.Value}");
                    }
                }
                catch (Exception e)
                {
                    Log($"[LoadAdmins FATAL] An error occurred: {e.Message}");
                }
            }
            else
            {
                Log("[LoadAdmins] The JSON file does not exist. Creating one with default content");
                Dictionary<string, string> defaultAdmins = new()
                {
                    { "steamid", "" }
                };

                try
                {
                    JsonSerializerOptions options = new()
                    {
                        WriteIndented = true,
                    };
                    string defaultJson = JsonSerializer.Serialize(defaultAdmins, options);
                    string? directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath != null)
                    {
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                    }
                    File.WriteAllText(filePath, defaultJson);

                    Log("[LoadAdmins] Created a new JSON file with default content.");
                }
                catch (Exception e)
                {
                    Log($"[LoadAdmins FATAL] Error creating the JSON file: {e.Message}");
                }
            }
        }

        private bool IsPlayerAdmin(CCSPlayerController? player, string command = "", params string[] permissions)
        {
            if (everyoneIsAdmin.Value) return true; // Everyone is treated as admin if matchzy_everyone_is_admin is true.
            string[] updatedPermissions = permissions.Concat(new[] { "@css/root" }).ToArray();
            RequiresPermissionsOr attr = new(updatedPermissions)
            {
                Command = command
            };
            if (attr.CanExecuteCommand(player)) return true; // Admin exists in admins.json of CSSharp
            if (player == null) return true; // Sent via server, hence should be treated as an admin.
            if (loadedAdmins.ContainsKey(player.SteamID.ToString())) return true; // Admin exists in admins.json of MatchZy
            return false;
        }

        private int GetRealPlayersCount()
        {
            return playerData.Count;
        }

        private void SendUnreadyPlayersMessage()
        {
            if (!isWarmup || matchStarted) return;
            List<string> unreadyPlayers = new();

            foreach (var key in playerReadyStatus.Keys)
            {
                if (playerReadyStatus[key] == false)
                {
                    unreadyPlayers.Add(playerData[key].PlayerName);
                }
            }
            if (unreadyPlayers.Count > 0)
            {
                string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                string minimumReadyRequiredMessage = isMatchSetup ? "" : $"[Minimum ready players required: {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}]";

                // Server.PrintToChatAll($"{chatPrefix} Unready players: {unreadyPlayerList}. Please type .ready to ready up! {minimumReadyRequiredMessage}");
                if (isRoundRestorePending)
                {
                    PrintToAllChat(Localizer["matchzy.ready.readytotestorebackupinfomessage", unreadyPlayerList, minimumReadyRequiredMessage]);
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.utility.unreadyplayers", unreadyPlayerList, minimumReadyRequiredMessage]);
                }
            }
            else
            {
                int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
                if (isMatchSetup)
                {
                    // Server.PrintToChatAll($"{chatPrefix} Current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                    PrintToAllChat(Localizer["matchzy.utility.readyplayers", countOfReadyPlayers]);
                }
                else
                {
                    // Server.PrintToChatAll($"{chatPrefix} Minimum ready players required {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}, current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                    PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, countOfReadyPlayers]);
                }
            }
        }

        private void SendPausedStateMessage()
        {
            if (isPaused && matchStarted)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin")
                {
                    PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
                }
                else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedbecauserestore"]);
                }
                else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["TERRORIST"].teamName, reverseTeamSides["CT"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["CT"].teamName, reverseTeamSides["TERRORIST"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedthematch", pauseTeamName]);
                }
            }
        }

        private void ExecWarmupCfg()
        {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath)))
            {
                Log($"[StartWarmup] Starting warmup! Executing Warmup CFG from {warmupCfgPath}");
                Server.ExecuteCommand($"exec {warmupCfgPath}");
            }
            else
            {
                Log($"[StartWarmup] Starting warmup! Warmup CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("bot_kick;bot_quota 0;mp_autokick 0;mp_autoteambalance 0;mp_buy_anywhere 0;mp_buytime 15;mp_death_drop_gun 0;mp_free_armor 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_radar_showall 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_solid_teammates 0;mp_spectators_max 20;mp_maxmoney 16000;mp_startmoney 16000;mp_timelimit 0;sv_alltalk 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_deadtalk 1;sv_full_alltalk 0;sv_grenade_trajectory 0;sv_hibernate_when_empty 0;mp_weapons_allow_typecount -1;sv_infinite_ammo 0;sv_showimpacts 0;sv_voiceenable 1;sm_cvar sv_mute_players_with_social_penalties 0;sv_mute_players_with_social_penalties 0;tv_relayvoice 1;sv_cheats 0;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary;mp_maxrounds 24;mp_warmup_start;mp_warmup_pausetimer 1;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;");
            }
        }

        private void StartWarmup()
        {
            unreadyPlayerMessageTimer?.Kill();
            unreadyPlayerMessageTimer = null;
            unreadyPlayerMessageTimer ??= AddTimer(chatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);
            
            // 不在热身阶段显示指令提示
            commandHUDMessageTimer?.Kill();
            commandHUDMessageTimer = null;
            
            isWarmup = true;
            ExecWarmupCfg();
        }
        
        private CounterStrikeSharp.API.Modules.Timers.Timer? commandHUDMessageTimer = null;
        private int instructionsTimeLeft = -1; // 指令提示的剩余显示时间，-1表示不显示
        
        // C4计时器相关变量
        private CounterStrikeSharp.API.Modules.Timers.Timer? c4TimerHUDTimer = null;
        private float c4ExplosionTime = 0.0f;
        private bool c4Planted = false;
        
        // 从配置中加载C4计时器设置
        private void LoadC4TimerSettings()
        {
            try
            {
                // 直接从FakeConVar读取配置值
                showC4Timer = enableC4Timer.Value;
                
                // 添加额外的日志，帮助调试
                Log($"[C4Timer] 配置已加载: enableC4Timer.Value={enableC4Timer.Value}, showC4Timer={showC4Timer}");
                
                // 输出到控制台，方便查看当前状态
                Server.ExecuteCommand($"echo \"[MatchZy] C4计时器当前状态: {(showC4Timer ? "已启用" : "已禁用")}\"");
                
                // 如果C4计时器被禁用，确保停止任何正在显示的C4计时器
                if (!showC4Timer && c4Planted)
                {
                    c4Planted = false;
                    Log($"[C4Timer] 因为计时器被禁用，已强制停止显示C4计时器");
                }
                
                Log($"[C4Timer] C4计时器功能状态: {(showC4Timer ? "已启用" : "已禁用")}");
            }
            catch (Exception ex)
            {
                // 出现异常，记录并使用默认值
                Log($"[C4Timer] 加载C4计时器设置时发生错误: {ex.Message}");
                showC4Timer = true;
            }
        }
        
        private void ShowCommandsHUDMessage()
        {
            if (!isWarmup || isKnifeRound || isSideSelectionPhase || isMatchLive) 
            {
                commandHUDMessageTimer?.Kill();
                commandHUDMessageTimer = null;
                return;
            }
            
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player)) continue;
                
                player.PrintToCenter("游戏中可按Y键输入以下指令：\n" +
                                    ".pause/.tech/.unpause 可发起战术/技术/解除暂停\n" +
                                    ".stop 恢复上一回合的备份\n" +
                                    ".coach <T/CT>/.uncoach 可担任指定方的教练/离开教练位置");
            }
        }

        // 显示带倒计时的指令提示HUD
        private void ShowInstructionsHUD()
        {
            if (instructionsTimeLeft <= 0)
                return;
                
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player)) continue;
                
                // 显示带倒计时的指令提示
                player.PrintToCenterHtml($"<b><font color='#00FF00' size='24'>游戏指令提示 ({instructionsTimeLeft})</font></b><br><br>" +
                                      "<font color='yellow' size='20'>.pause/.tech/.unpause</font><br>" +
                                      "<font color='white' size='18'>发起战术/技术/解除暂停</font><br><br>" +
                                      "<font color='yellow' size='20'>.stop</font><br>" +
                                      "<font color='white' size='18'>恢复上一回合的备份</font><br><br>" +
                                      "<font color='yellow' size='20'>.coach &lt;T/CT&gt; / .uncoach</font><br>" +
                                      "<font color='white' size='18'>担任指定方教练/离开教练位置</font>");
            }
        }

        private void StartKnifeRound()
        {
            // Kills unready players message timer
            if (unreadyPlayerMessageTimer != null)
            {
                unreadyPlayerMessageTimer.Kill();
                unreadyPlayerMessageTimer = null;
            }
            
            // 停止指令提示定时器
            if (commandHUDMessageTimer != null)
            {
                commandHUDMessageTimer.Kill();
                commandHUDMessageTimer = null;
            }

            // Setting match phases bools
            matchStarted = true;
            isKnifeRound = true;
            readyAvailable = false;
            isWarmup = false;

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath)))
            {
                Log($"[StartKnifeRound] Starting Knife! Executing Knife CFG from {knifeCfgPath}");
                Server.ExecuteCommand($"exec {knifeCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                Log($"[StartKnifeRound] Starting Knife! Knife CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("mp_ct_default_secondary \"\";mp_free_armor 1;mp_freezetime 10;mp_give_player_c4 0;mp_maxmoney 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_t_default_secondary \"\";mp_round_restart_delay 3;mp_team_intro_time 0;mp_restartgame 1;mp_warmup_end;");
            }

            PrintToAllChat($"{ChatColors.Olive}拼刀局!");
            PrintToAllChat($"{ChatColors.Lime}拼刀局!");
            PrintToAllChat($"{ChatColors.Green}拼刀局!");
        }

        private void SendSideSelectionMessage()
        {
            if (!isSideSelectionPhase) return;
            PrintToAllChat(Localizer["matchzy.knife.sidedecisionpending", ChatColors.Green + knifeWinnerName + ChatColors.Default]);
            // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Waiting for them to type {ChatColors.Green}.stay{ChatColors.Default} or {ChatColors.Green}.switch{ChatColors.Default}");
        }

        private void StartAfterKnifeWarmup()
        {
            isWarmup = true;
            ExecWarmupCfg();
            knifeWinnerName = knifeWinner == 3 ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;
            ShowDamageInfo();
            
            // 初始化投票控制器
            VoteController = Utilities.FindAllEntitiesByDesignerName<CVoteController>("vote_controller").Last();
            if (VoteController == null) return;
            
            // 准备投票过滤器（只有胜利方可以投票）
            voteFilter.Clear();
            foreach (var playerEntry in playerData)
            {
                CCSPlayerController player = playerEntry.Value;
                if (player.TeamNum == knifeWinner && !player.IsBot && player.Connected == PlayerConnectedState.PlayerConnected)
                {
                    voteFilter.Add(player);
                }
            }
            
            if (voteFilter.Count == 0) 
            {
                StartLive();
                return;
            }

            // 重置投票控制器状态
            for (int i = 0; i < 64; i++)
            {
                VoteController.VotesCast[i] = 5; // VOTE_UNCAST
            }
            for (int i = 0; i < 5; i++)
            {
                VoteController.VoteOptionCount[i] = 0;
            }
            
            // 发送投票开始消息
            UserMessage voteStart = UserMessage.FromId(346); // CS_UM_VoteStart = 346
            voteStart.SetInt("team", -1);
            voteStart.SetInt("player_slot", 99); // VOTE_CALLER_SERVER
            voteStart.SetInt("vote_type", -1);
            voteStart.SetString("disp_str", "#SFUI_Vote_SwapCamp_PWA");
            voteStart.SetString("details_str", "");
            voteStart.SetBool("is_yes_no_vote", true);
            voteStart.Send(voteFilter);
            
            isVoteInProgress = true;
            VoteController.PotentialVotes = voteFilter.Count;
            VoteController.ActiveIssueIndex = 2;
            
            // 设置30秒后自动结束投票
            AddTimer(30.0f, () => {
                if (isVoteInProgress)
                {
                    EndVote(false);
                }
            });
        }

        private void SetLiveFlags()
        {
            // Setting match phases bools
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            readyAvailable = false;
            isKnifeRound = false;
        }

        private void SetupLiveFlagsAndCfg()
        {
            SetLiveFlags();
            KillPhaseTimers();
            ExecLiveCFG();
            // Adding timer here to make sure that CFG execution is completed till then
            AddTimer(1, () =>
            {
                HandlePlayoutConfig();
                ExecuteChangedConvars();
            });
        }

        private void StartLive()
        {
            SetupLiveFlagsAndCfg();
            StartDemoRecording();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.json";

            // This is to reload the map once it is over so that all flags are reset accordingly
            Server.ExecuteCommand("mp_match_end_restart true");

            PrintToAllChat($"{ChatColors.Olive}开始游戏！");
            PrintToAllChat($"{ChatColors.Lime}开始游戏！");
            PrintToAllChat($"{ChatColors.Green}开始游戏！");
            
            // 在比赛开始3秒后显示持续15秒的指令提示
            AddTimer(3.0f, () => {
                instructionsTimeLeft = 15; // 设置倒计时为15秒
                
                // 每秒递减倒计时并更新HUD
                commandHUDMessageTimer = AddTimer(1.0f, () => {
                    if (instructionsTimeLeft <= 0)
                    {
                        commandHUDMessageTimer?.Kill();
                        commandHUDMessageTimer = null;
                        return;
                    }
                    instructionsTimeLeft--;
                }, TimerFlags.REPEAT);
            });

            var goingLiveEvent = new GoingLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
            };

            Task.Run(async () =>
            {
                await SendEventAsync(goingLiveEvent);
            });
        }

        private void KillPhaseTimers()
        {
            unreadyPlayerMessageTimer?.Kill();
            sideSelectionMessageTimer?.Kill();
            pausedStateTimer?.Kill();
            commandHUDMessageTimer?.Kill();
            unreadyPlayerMessageTimer = null;
            sideSelectionMessageTimer = null;
            pausedStateTimer = null;
            commandHUDMessageTimer = null;
            instructionsTimeLeft = -1; // 重置指令提示计时器
        }


        private (int alivePlayers, int totalHealth) GetAlivePlayers(int team)
        {
            int count = 0;
            int totalHealth = 0;
            foreach (var key in playerData.Keys)
            {
                CCSPlayerController player = playerData[key];
                if (team == 2 && reverseTeamSides["TERRORIST"].coach.Contains(player)) continue;
                if (team == 3 && reverseTeamSides["CT"].coach.Contains(player)) continue;
                if (!IsPlayerValid(player)) continue;
                if (player.TeamNum == team)
                {
                    if (player.PlayerPawn.Value!.Health > 0) count++;
                    totalHealth += player.PlayerPawn.Value!.Health;
                }
            }
            return (count, totalHealth);
        }

        private void ResetMatch(bool warmupCfgRequired = true)
        {
            try
            {
                // We stop demo recording if a live match was restarted
                if (matchStarted && isDemoRecording)
                {
                    Server.ExecuteCommand($"tv_stoprecord");
                    isDemoRecording = false;
                }
                // Reset match data
                matchStarted = false;
                readyAvailable = true;
                isPaused = false;
                isMatchSetup = false;

                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;
                liveMatchId = -1;
                isPractice = false;
                isDryRun = false;
                isVeto = false;
                isPreVeto = false;

                lastBackupFileName = "";
                lastMatchZyBackupFileName = "";

                isRoundRestorePending = false;
                
                // 重置战术暂停计数
                tacticalPausesUsed["t"] = 0;
                tacticalPausesUsed["ct"] = 0;
                
                // 清除战术暂停计时器
                if (tacticalPauseTimer != null)
                {
                    tacticalPauseTimer.Kill();
                    tacticalPauseTimer = null;
                }
                
                playerHasTakenDamage = false;

                // Unready all players
                foreach (var key in playerReadyStatus.Keys)
                {
                    playerReadyStatus[key] = false;
                }

                teamReadyOverride = new()
                {
                    {CsTeam.Terrorist, false},
                    {CsTeam.CounterTerrorist, false},
                    {CsTeam.Spectator, false}
                };

                HandleClanTags();

                // Reset unpause data
                Dictionary<string, object> unpauseData = new()
                {
                    { "ct", false },
                    { "t", false },
                    { "pauseTeam", "" }
                };

                // Reset stop data
                stopData["ct"] = false;
                stopData["t"] = false;

                // Reset owned bots data
                pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
                noFlashList = new();
                lastGrenadesData = new();
                nadeSpecificLastGrenadeData = new();
                UnpauseMatch();

                matchzyTeam1.teamName = "COUNTER-TERRORISTS";
                matchzyTeam2.teamName = "TERRORISTS";

                matchzyTeam1.teamPlayers = null;
                matchzyTeam2.teamPlayers = null;

                HashSet<CCSPlayerController> coaches = GetAllCoaches();

                foreach (var coach in coaches)
                {
                    if (!IsPlayerValid(coach)) continue;
                    coach.Clan = "";
                    SetPlayerVisible(coach);
                }

                matchzyTeam1.coach = new();
                matchzyTeam2.coach = new();
                coachKillTimer?.Kill();
                coachKillTimer = null;

                matchzyTeam1.seriesScore = 0;
                matchzyTeam2.seriesScore = 0;

                Server.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
                Server.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");

                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                // Keeping the log URLs to avoid their reset on match start.
                matchConfig = new()
                {
                    RemoteLogURL = matchConfig.RemoteLogURL,
                    RemoteLogHeaderKey = matchConfig.RemoteLogHeaderKey,
                    RemoteLogHeaderValue = matchConfig.RemoteLogURL
                };

                KillPhaseTimers();
                UpdatePlayersMap();
                if (warmupCfgRequired)
                {
                    StartWarmup();
                }
                else
                {
                    // Since we should be already in warmup phase by this point, we are just setting up the SendUnreadyPlayersMessage timer
                    unreadyPlayerMessageTimer?.Kill();
                    unreadyPlayerMessageTimer = null;
                    unreadyPlayerMessageTimer ??= AddTimer(chatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);
                }
            }
            catch (Exception ex)
            {
                Log($"[ResetMatch - FATAL] [ERROR]: {ex.Message}");
            }
        }

        private void UpdatePlayersMap()
        {
            try
            {
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
                Log($"[UpdatePlayersMap] CCSPlayerController count: {playerEntities.Count<CCSPlayerController>()} matchModeOnly: {matchModeOnly}");
                connectedPlayers = 0;

                // Clear the playerData dictionary by creating a new instance to add fresh data.
                playerData = new Dictionary<int, CCSPlayerController>();
                foreach (var player in playerEntities)
                {
                    if (player == null) continue;
                    if (!player.IsValid || player.IsBot || player.IsHLTV) continue;

                    if (isMatchSetup || matchModeOnly)
                    {
                        CsTeam team = GetPlayerTeam(player);
                        if (team == CsTeam.None && player.UserId.HasValue)
                        {
                            Server.ExecuteCommand($"kickid {(ushort)player.UserId}");
                            continue;
                        }
                    }

                    // A player controller still exists after a player disconnects
                    // Hence checking whether the player is actually in the server or not
                    if (player.Connected != PlayerConnectedState.PlayerConnected) continue;

                    if (player.UserId.HasValue)
                    {

                        // Updating playerData and playerReadyStatus
                        playerData[player.UserId.Value] = player;

                        // Adding missing player in playerReadyStatus
                        if (!playerReadyStatus.ContainsKey(player.UserId.Value))
                        {
                            playerReadyStatus[player.UserId.Value] = false;
                        }
                    }
                    connectedPlayers++;
                }

                // Removing disconnected players from playerReadyStatus
                foreach (var key in playerReadyStatus.Keys.ToList())
                {
                    if (!playerData.ContainsKey(key))
                    {
                        // Key is not present in playerData, so remove it from playerReadyStatus
                        playerReadyStatus.Remove(key);
                    }
                }
                Log($"[UpdatePlayersMap] CCSPlayerController count: {playerEntities.Count<CCSPlayerController>()}, RealPlayersCount: {GetRealPlayersCount()}");
            }
            catch (Exception e)
            {
                Log($"[UpdatePlayersMap FATAL] An error occurred: {e.Message}");
            }
        }

        public void DetermineKnifeWinner()
        {
            // Knife Round code referred from Get5, thanks to the Get5 team for their amazing job!
            (int tAlive, int tHealth) = GetAlivePlayers(2);
            (int ctAlive, int ctHealth) = GetAlivePlayers(3);
            Log($"[KNIFE OVER] CT Alive: {ctAlive} with Total Health: {ctHealth}, T Alive: {tAlive} with Total Health: {tHealth}");
            if (ctAlive > tAlive)
            {
                knifeWinner = 3;
            }
            else if (tAlive > ctAlive)
            {
                knifeWinner = 2;
            }
            else if (ctHealth > tHealth)
            {
                knifeWinner = 3;
            }
            else if (tHealth > ctHealth)
            {
                knifeWinner = 2;
            }
            else
            {
                // Choosing a winner randomly
                Random random = new();
                knifeWinner = random.Next(2, 4);
            }
        }

        private void HandleKnifeWinner(EventCsWinPanelRound @event)
        {
            DetermineKnifeWinner();
            // Below code is working partially (Winner audio plays correctly for knife winner team, but may display round winner incorrectly)
            // Hence we restart the game with StartAfterKnifeWarmup and allow the winning team to choose side

            @event.FunfactToken = "";

            // Commenting these assignments as they were crashing the server.
            // long empty = 0;
            // @event.FunfactPlayer = null;
            // @event.FunfactData1 = empty;
            // @event.FunfactData2 = empty;
            // @event.FunfactData3 = empty;
            int finalEvent = 10;
            if (knifeWinner == 3)
            {
                finalEvent = 8;
            }
            else if (knifeWinner == 2)
            {
                finalEvent = 9;
            }
            Log($"[KNIFE WINNER] Won by: {knifeWinner}, finalEvent: {@event.FinalEvent}, newFinalEvent: {finalEvent}");
            @event.FinalEvent = finalEvent;
        }

        private void HandleMapChangeCommand(CCSPlayerController? player, string mapName)
        {
            if (!IsPlayerAdmin(player, "css_map", "@css/map"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, $"Map cannot be changed once the match is started!");
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchstarted"]);
                return;
            }

            if (!long.TryParse(mapName, out _) && !mapName.Contains('_'))
            {
                mapName = "de_" + mapName;
            }

            if (long.TryParse(mapName, out _))
            { // Check if mapName is a long for workshop map ids
                Server.ExecuteCommand($"bot_kick");
                Server.ExecuteCommand($"host_workshop_map \"{mapName}\"");
            }
            else if (Server.IsMapValid(mapName))
            {
                Server.ExecuteCommand($"bot_kick");
                Server.ExecuteCommand($"changelevel \"{mapName}\"");
            }
            else
            {
                ReplyToUserCommand(player, $"无效的地图名称！");
            }
        }

        private void HandleReadyRequiredCommand(CCSPlayerController? player, string commandArg)
        {
            if (!IsPlayerAdmin(player, "css_readyrequired", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int readyRequired) && readyRequired >= 0 && readyRequired <= 32)
                {
                    minimumReadyRequired = readyRequired;
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    // ReplyToUserCommand(player, $"Minimum ready players required to start the match are now set to: {minimumReadyRequiredFormatted}");
                    ReplyToUserCommand(player, Localizer["matchzy.utility.minreadyplayers", minimumReadyRequiredFormatted]);
                    CheckLiveRequired();
                }
                else
                {
                    // ReplyToUserCommand(player, $"Invalid value for readyrequired. Please specify a valid non-negative number. Usage: !readyrequired <number_of_ready_players_required>");
                    ReplyToUserCommand(player, Localizer["matchzy.utility.rrinvalidvalue"]);
                }
            }
            else
            {
                string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                // ReplyToUserCommand(player, $"Current Ready Required: {minimumReadyRequiredFormatted} .Usage: !readyrequired <number_of_ready_players_required>");
                ReplyToUserCommand(player, Localizer["matchzy.utility.currentreadyrequired", minimumReadyRequiredFormatted]);
            }
        }

        private void CheckLiveRequired()
        {
            if (!readyAvailable || matchStarted) return;

            // Todo: Implement a same ready system for both pug and match
            int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
            bool liveRequired = false;
            if (isMatchSetup)
            {
                if (IsTeamsReady() && IsSpectatorsReady())
                {
                    liveRequired = true;
                }
            }
            else if (minimumReadyRequired == 0)
            {
                if (countOfReadyPlayers >= connectedPlayers && connectedPlayers > 0)
                {
                    liveRequired = true;
                }
            }
            else if (countOfReadyPlayers >= minimumReadyRequired)
            {
                liveRequired = true;
            }
            if (liveRequired)
            {
                HandleMatchStart();
            }
        }

        private void HandleMatchStart()
        {
            isPractice = false;
            isDryRun = false;
            if (isRoundRestorePending)
            {
                RestoreRoundBackup(null, pendingRestoreFileName);
                isRoundRestorePending = false;
                pendingRestoreFileName = "";
                return;
            }
            // If default names, we pick a player and use their name as their team name
            if (matchzyTeam1.teamName == "COUNTER-TERRORISTS")
            {
                // matchzyTeam1.teamName = teamName;
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;
                foreach (var key in playerData.Keys)
                {
                    if (playerData[key].TeamNum == 3)
                    {
                        matchzyTeam1.teamName = "team_" + RemoveSpecialCharacters(playerData[key].PlayerName.Replace(" ", "_"));
                        foreach (var coach in matchzyTeam1.coach) {
                            coach.Clan = $"[{matchzyTeam1.teamName} COACH]";
                        }
                        break;
                    }
                }
                // Server.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
            }

            if (matchzyTeam2.teamName == "TERRORISTS")
            {
                // matchzyTeam2.teamName = teamName;
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                foreach (var key in playerData.Keys)
                {
                    if (playerData[key].TeamNum == 2)
                    {
                        matchzyTeam2.teamName = "team_" + RemoveSpecialCharacters(playerData[key].PlayerName.Replace(" ", "_"));
                        foreach (var coach in matchzyTeam2.coach) {
                            coach.Clan = $"[{matchzyTeam2.teamName} COACH]";
                        }
                        break;
                    }
                }
                // Server.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");
            }

            Server.ExecuteCommand($"mp_teamname_1 {reverseTeamSides["CT"].teamName}");
            Server.ExecuteCommand($"mp_teamname_2 {reverseTeamSides["TERRORIST"].teamName}");

            HandleClanTags();

            string seriesType = "BO" + matchConfig.NumMaps.ToString();
            liveMatchId = database.InitMatch(matchzyTeam1.teamName, matchzyTeam2.teamName, "-", isMatchSetup, liveMatchId, matchConfig.CurrentMapNumber, seriesType);
            SetupRoundBackupFile();

            GetSpawns();

            if (isPreVeto)
            {
                CreateVeto();
            }
            else if (isKnifeRequired)
            {
                StartKnifeRound();
            }
            else
            {
                StartDemoRecording();
                StartLive();
            }
            if (showCreditsOnMatchStart.Value)
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}MEngZy{ChatColors.Default}插件由{ChatColors.Green}MEngYangX{ChatColors.Default}开发");
                Server.PrintToChatAll($"{chatPrefix} 改编自MatchZy");
            }
            if (matchStartMessage.Value.Trim() != "" && matchStartMessage.Value.Trim() != "\"\"")
            {
                List<string> matchStartMessages = [.. matchStartMessage.Value.Split("$$$")];
                foreach (string message in matchStartMessages)
                {
                    PrintToAllChat(GetColorTreatedString(FormatCvarValue(message.Trim())));
                }
            }
        }

        public void HandleClanTags()
        {
            // Currently it is not possible to keep updating player tags while in warmup without restarting the match
            // Hence returning from here until we find a proper solution
            return;

            if (readyAvailable && !matchStarted)
            {
                foreach (var key in playerData.Keys)
                {
                    if (playerReadyStatus[key])
                    {
                        playerData[key].Clan = "[已准备]";
                    }
                    else
                    {
                        playerData[key].Clan = "[未准备]";
                    }
                    Server.PrintToChatAll($"PlayerName: {playerData[key].PlayerName} Clan: {playerData[key].Clan}");
                }
            }
            else if (matchStarted)
            {
                foreach (var key in playerData.Keys)
                {
                    if (playerData[key].TeamNum == 2)
                    {
                        playerData[key].Clan = reverseTeamSides["TERRORIST"].teamTag;
                    }
                    else if (playerData[key].TeamNum == 3)
                    {
                        playerData[key].Clan = reverseTeamSides["CT"].teamTag;
                    }
                    Server.PrintToChatAll($"PlayerName: {playerData[key].PlayerName} Clan: {playerData[key].Clan}");
                }
            }
        }

        private void HandleMatchEnd()
        {
            if (!isMatchLive) return;

            // This ensures that the mp_match_restart_delay is not shorter than what is required for the GOTV recording to finish.
            // Ref: Get5
            int restartDelay = ConVar.Find("mp_match_restart_delay")!.GetPrimitiveValue<int>();
            int tvDelay = GetTvDelay();
            int requiredDelay = tvDelay + 15;
            int tvFlushDelay = requiredDelay;
            if (tvDelay > 0.0)
            {
                requiredDelay += 10;
            }
            if (requiredDelay > restartDelay)
            {
                Log($"Extended mp_match_restart_delay from {restartDelay} to {requiredDelay} to ensure GOTV broadcast can finish.");
                ConVar.Find("mp_match_restart_delay")!.SetValue(requiredDelay);
                restartDelay = requiredDelay;
            }
            int currentMapNumber = matchConfig.CurrentMapNumber;
            Log($"[HandleMatchEnd] 地图已结束，isMatchSetup: {isMatchSetup} matchid: {liveMatchId} currentMapNumber: {currentMapNumber} tvFlushDelay: {tvFlushDelay}");

            StopDemoRecording(tvFlushDelay - 0.5f, activeDemoFile, liveMatchId, currentMapNumber);

            string winnerName = GetMatchWinnerName();
            (int t1score, int t2score) = GetTeamsScore();
            int team1SeriesScore = matchzyTeam1.seriesScore;
            int team2SeriesScore = matchzyTeam2.seriesScore;

            string statsPath = Server.GameDirectory + "/csgo/MatchZy_Stats/" + liveMatchId.ToString();

            var mapResultEvent = new MapResultEvent
            {
                MatchId = liveMatchId,
                MapNumber = currentMapNumber,
                Winner = new Winner(t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2", team1SeriesScore > team2SeriesScore ? "team1" : "team2"),
                StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, team1SeriesScore, t1score, 0, 0, new List<StatsPlayer>()),
                StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, team2SeriesScore, t2score, 0, 0, new List<StatsPlayer>())
            };

            Task.Run(async () =>
            {
                await SendEventAsync(mapResultEvent);
                await database.SetMapEndData(liveMatchId, currentMapNumber, winnerName, t1score, t2score, team1SeriesScore, team2SeriesScore);
                await database.WritePlayerStatsToCsv(statsPath, liveMatchId, currentMapNumber);
            });

            // If a match is not setup, it was supposed to be a pug/scrim with 1 map
            // Hence we reset the match once it is over
            // Todo: Support BO3/BO5 in pugs as well
            if (!isMatchSetup)
            {
                EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                return;
            }

            int remainingMaps = matchConfig.NumMaps - matchzyTeam1.seriesScore - matchzyTeam2.seriesScore;
            Log($"[HandleMatchEnd] 比赛已结束，remainingMaps: {remainingMaps}, NumMaps: {matchConfig.NumMaps}, Team1SeriesScore: {matchzyTeam1.seriesScore}, Team2SeriesScore: {matchzyTeam2.seriesScore}");
            if (matchzyTeam1.seriesScore == matchzyTeam2.seriesScore && remainingMaps <= 0)
            {
                EndSeries(null, restartDelay - 1, t1score, t2score);
            }
            else if (matchConfig.SeriesCanClinch)
            {
                int mapsToWinSeries = (matchConfig.NumMaps / 2) + 1;
                if (matchzyTeam1.seriesScore == mapsToWinSeries)
                {
                    EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                    return;
                }
                else if (matchzyTeam2.seriesScore == mapsToWinSeries)
                {
                    EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                    return;
                }
            }
            else if (remainingMaps <= 0)
            {
                EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                return;
            }
            if (matchzyTeam1.seriesScore > matchzyTeam2.seriesScore)
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} 赢得了系列赛 {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");

            }
            else if (matchzyTeam2.seriesScore > matchzyTeam1.seriesScore)
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} 赢得了系列赛 {ChatColors.Green}{matchzyTeam2.seriesScore}-{matchzyTeam1.seriesScore}{ChatColors.Default}");

            }
            else
            {
                Server.PrintToChatAll($"{chatPrefix} 系列赛平局 {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");
            }
            matchConfig.CurrentMapNumber += 1;
            string nextMap = matchConfig.Maplist[matchConfig.CurrentMapNumber];

            if (isPaused)
                UnpauseMatch();

            stopData["ct"] = false;
            stopData["t"] = false;

            KillPhaseTimers();

            AddTimer(restartDelay - 4, () =>
            {
                if (!isMatchSetup) return;
                ChangeMap(nextMap, 3.0f);
                matchStarted = false;
                readyAvailable = true;
                isPaused = false;

                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;
                isPractice = false;
                isDryRun = false;
                StartWarmup();
                SetMapSides();
            });
        }

        private void ChangeMap(string mapName, float delay)
        {
            Log($"[ChangeMap] 正在更改地图为 {mapName}，延迟 {delay} 秒");
            AddTimer(delay, () =>
            {
                if (long.TryParse(mapName, out _))
                {
                    Server.ExecuteCommand($"bot_kick");
                    Server.ExecuteCommand($"host_workshop_map \"{mapName}\"");
                }
                else if (Server.IsMapValid(mapName))
                {
                    Server.ExecuteCommand($"bot_kick");
                    Server.ExecuteCommand($"changelevel \"{mapName}\"");
                }
            });
        }

        private string GetMatchWinnerName()
        {
            (int t1score, int t2score) = GetTeamsScore();
            if (t1score > t2score)
            {
                matchzyTeam1.seriesScore++;
                return matchzyTeam1.teamName;
            }
            else if (t2score > t1score)
            {
                matchzyTeam2.seriesScore++;
                return matchzyTeam2.teamName;
            }
            else
            {
                return "平局";
            }
        }

        private (int t1score, int t2score) GetTeamsScore()
        {
            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            int t1score = 0;
            int t2score = 0;
            foreach (var team in teamEntities)
            {
                if (team.Teamname == teamSides[matchzyTeam1])
                {
                    t1score = team.Score;
                }
                else if (team.Teamname == teamSides[matchzyTeam2])
                {
                    t2score = team.Score;
                }
            }
            return (t1score, t2score);
        }

        private int GetRoundNumer()
        {
            (int t1score, int t2score) = GetTeamsScore();

            return t1score + t2score;
        }

        public void HandlePostRoundStartEvent(EventRoundStart @event)
        {
            if (isDryRun) RandomizeSpawns();
            if (!matchStarted) return;
            playerHasTakenDamage = false;
            HandleCoaches();
            CreateMatchZyRoundDataBackup();
            InitPlayerDamageInfo();
            UpdateHostname();
        }

        private void HandlePostRoundEndEvent(EventRoundEnd @event)
        {
            try
            {
                if (isMatchLive)
                {
                    coachKillTimer?.Kill();
                    coachKillTimer = null;
                    (int t1score, int t2score) = GetTeamsScore();
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName} [{t1score} - {t2score}] {matchzyTeam2.teamName}");

                    ShowDamageInfo();

                    (Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary, List<StatsPlayer> playerStatsListTeam1, List<StatsPlayer> playerStatsListTeam2) = GetPlayerStatsDict();

                    int currentMapNumber = matchConfig.CurrentMapNumber;
                    long matchId = liveMatchId;
                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;
                    Winner winner = new(@event.Winner.ToString(), t1score > t2score ? "team1" : "team2");

                    var roundEndEvent = new MatchZyRoundEndedEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        Reason = @event.Reason,
                        RoundTime = 0,
                        Winner = winner,
                        StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, 0, t1score, 0, 0, playerStatsListTeam1),
                        StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, 0, t2score, 0, 0, playerStatsListTeam2),
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(roundEndEvent);
                        await database.UpdatePlayerStatsAsync(matchId, currentMapNumber, playerStatsDictionary);
                        await database.UpdateMapStatsAsync(matchId, currentMapNumber, t1score, t2score);
                    });

                    string round = GetRoundNumer().ToString("D2");
                    lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                    lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    Log($"[HandlePostRoundEndEvent] 设置 lastBackupFileName 为 {lastBackupFileName} 和 lastMatchZyBackupFileName 为 {lastMatchZyBackupFileName}");

                    // One of the team did not use .stop command hence display the proper message after the round has ended.
                    if (stopData["ct"] && !stopData["t"])
                    {
                        Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{reverseTeamSides["CT"].teamName}{ChatColors.Default} 的回合恢复请求已取消，因为回合已结束");
                    }
                    else if (!stopData["ct"] && stopData["t"])
                    {
                        Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{reverseTeamSides["TERRORIST"].teamName}{ChatColors.Default} 的回合恢复请求已取消，因为回合已结束");
                    }

                    // Invalidate .stop requests after a round is completed.
                    stopData["ct"] = false;
                    stopData["t"] = false;

                    bool swapRequired = IsTeamSwapRequired();

                    // If isRoundRestoring is true, sides will be swapped from round restore if required!
                    if (swapRequired && !isRoundRestoring)
                    {
                        SwapSidesInTeamData(false);
                    }

                    isRoundRestoring = false;
                }
            }
            catch (Exception e)
            {
                Log($"[HandlePostRoundEndEvent FATAL] 发生错误: {e.Message}");
            }
        }

        public bool IsTeamSwapRequired()
        {
            // Handling OTs and side swaps (Referred from Get5)
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            int roundsPlayed = gameRules.TotalRoundsPlayed;

            int roundsPerHalf = ConVar.Find("mp_maxrounds")!.GetPrimitiveValue<int>() / 2;
            int roundsPerOTHalf = ConVar.Find("mp_overtime_maxrounds")!.GetPrimitiveValue<int>() / 2;

            bool halftimeEnabled = ConVar.Find("mp_halftime")!.GetPrimitiveValue<bool>();

            if (halftimeEnabled)
            {
                if (roundsPlayed == roundsPerHalf)
                {
                    return true;
                }
                // Now in OT.
                if (roundsPlayed >= 2 * roundsPerHalf)
                {
                    int otround = roundsPlayed - 2 * roundsPerHalf;  // round 33 -> round 3, etc.
                    // Do side swaps at OT halves (rounds 3, 9, ...)
                    if ((otround + roundsPerOTHalf) % (2 * roundsPerOTHalf) == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void PauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (isMatchLive && isPaused)
            {
                // ReplyToUserCommand(player, "Match is already paused!");
                ReplyToUserCommand(player, Localizer["matchzy.utility.paused"]);
                return;
            }
            if (IsHalfTimePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command during halftime.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.duringhalftime"]);
                return;
            }
            if (IsPostGamePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchended"]);
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.tacticaltimeout"]);
                return;
            }
            if (!techPauseEnabled.Value && player != null)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pause.techpausenotenabled"]);
                return;
            }
            if(!string.IsNullOrEmpty(techPausePermission.Value) && techPausePermission.Value != "\"\"")
            {
                if (!IsPlayerAdmin(player, "css_pause", techPausePermission.Value))
                {
                    SendPlayerNotAdminMessage(player);
                    return;
                }
            }
            if (isMatchLive && !isPaused)
            {

                string pauseTeamName = "Admin";
                unpauseData["pauseTeam"] = "Admin";
                if (player?.TeamNum == 2)
                {

                    pauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["TERRORIST"].teamName;
                }
                else if (player?.TeamNum == 3)
                {
                    pauseTeamName = reverseTeamSides["CT"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["CT"].teamName;
                }
                else
                {
                    return;
                }
                PrintToAllChat(Localizer["matchzy.pause.pausedthematch", pauseTeamName]);
                // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{pauseTeamName}{ChatColors.Default} has paused the match. Type .unpause to unpause the match");

                SetMatchPausedFlags();
            }
        }

        private void ForcePauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (!matchStarted) return;
            if (!IsPlayerAdmin(player, "css_forcepause", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (isMatchLive && isPaused)
            {
                // ReplyToUserCommand(player, "Match is already paused!");
                ReplyToUserCommand(player, Localizer["matchzy.utility.paused"]);
                return;
            }
            if (IsHalfTimePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command during halftime.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.duringhalftime"]);
                return;
            }
            if (IsPostGamePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchended"]);
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.tacticaltimeout"]);
                return;
            }
            unpauseData["pauseTeam"] = "Admin";
            PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
            // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
            if (player == null)
            {
                Server.PrintToConsole($"[MatchZy] {Localizer["matchzy.pause.adminpausedthematch"]}");
            }
            SetMatchPausedFlags();
        }

        private void ForceUnpauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (matchStarted && isPaused)
            {
                if (!IsPlayerAdmin(player, "css_forceunpause", "@css/config"))
                {
                    SendPlayerNotAdminMessage(player);
                    return;
                }
                PrintToAllChat(Localizer["matchzy.pause.adminunpausedthematch"]);
                UnpauseMatch();

                if (player == null)
                {
                    Server.PrintToConsole("[MatchZy] Admin has unpaused the match, resuming the match!");
                }
            }
        }

        private void UnpauseMatch()
        {
            Server.ExecuteCommand("mp_unpause_match;");
            isPaused = false;
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            if (!isPaused && pausedStateTimer != null)
            {
                pausedStateTimer.Kill();
                pausedStateTimer = null;
            }
        }

        private void SetMatchPausedFlags()
        {
            coachKillTimer?.Kill();
            coachKillTimer = null;

            Server.ExecuteCommand("mp_pause_match;");
            isPaused = true;

            pausedStateTimer ??= AddTimer(chatTimerDelay, SendPausedStateMessage, TimerFlags.REPEAT);
        }

        private void StartMatchMode()
        {
            if (matchStarted || (!isPractice && !isSleep)) return;
            ExecUnpracCommands();
            ResetMatch();
            RemoveSpawnBeams();
            Server.PrintToChatAll($"{chatPrefix} 比赛模式已加载！");
        }

        private void ExecLiveCFG()
        {
            int gameMode = GetGameMode();

            var cfgPath = liveCfgPath;
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", liveCfgPath);

            if (gameMode == 2)
            {
                absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", liveWingmanCfgPath);
                cfgPath = liveWingmanCfgPath;
            }

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath))
            {
                Log($"[StartLive] Starting Live! Executing Live CFG from {cfgPath}");
                Server.ExecuteCommand($"exec {cfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
                
                // 确保战术暂停设置与我们的配置一致
                Server.ExecuteCommand($"mp_team_timeout_time {tacticalPauseDuration.Value}");
                Server.ExecuteCommand($"mp_team_timeout_max {maxTacticalPausesAllowed.Value}");
                Log($"[StartLive] 已设置战术暂停时间为 {tacticalPauseDuration.Value} 秒，最大暂停次数为 {maxTacticalPausesAllowed.Value}");
            }
            else
            {
                Log($"[StartLive] Starting Live! Live CFG not found in {absolutePath}, using default CFG!");
                if (gameMode == 2)
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary;mp_maxrounds 24;mp_warmup_start;mp_warmup_pausetimer 1;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;");
                    Server.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 3;mp_team_timeout_ot_max 1;mp_team_timeout_ot_add_each 1;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
            
            // 在默认配置后也设置我们的自定义战术暂停参数
            Server.ExecuteCommand($"mp_team_timeout_time {tacticalPauseDuration.Value}");
            Server.ExecuteCommand($"mp_team_timeout_max {maxTacticalPausesAllowed.Value}");
            Log($"[StartLive] 已设置战术暂停时间为 {tacticalPauseDuration.Value} 秒，最大暂停次数为 {maxTacticalPausesAllowed.Value}");
        }

        private void SendPlayerNotAdminMessage(CCSPlayerController? player)
        {
            // ReplyToUserCommand(player, "You do not have permission to use this command!");
            ReplyToUserCommand(player, Localizer["matchzy.utility.dontpermission"]);
        }

        private string GetColorTreatedString(string message)
        {
            // Adding extra space before args if message starts with a color name
            // This is because colors cannot be applied from 1st character, hence we make first character as an empty space
            if (message.StartsWith('{')) message = " " + message;

            foreach (var field in typeof(ChatColors).GetFields())
            {
                string pattern = $"{{{field.Name}}}";
                string? replacement = field.GetValue(null)?.ToString();

                if (replacement is null) return message;

                // Create a case-insensitive regular expression pattern for the color name
                string patternIgnoreCase = Regex.Escape(pattern);
                message = Regex.Replace(message, patternIgnoreCase, replacement, RegexOptions.IgnoreCase);
            }

            return message;
        }

        private void SendAvailableCommandsMessage(CCSPlayerController? player)
        {
            if (!IsPlayerValid(player)) return;

            ReplyToUserCommand(player, "可用命令:");

            if (isPractice)
            {
                // 游戏内聊天栏显示简洁命令
                player!.PrintToChat($" {ChatColors.Green}出生点: {ChatColors.Default}.spawn, .ctspawn, .tspawn, .bestspawn, .worstspawn");
                player.PrintToChat($" {ChatColors.Green}机器人: {ChatColors.Default}.bot, .nobots, .crouchbot, .boost, .crouchboost");
                player.PrintToChat($" {ChatColors.Green}投掷物: {ChatColors.Default}.loadnade, .savenade, .importnade, .listnades");
                player.PrintToChat($" {ChatColors.Green}投掷操作: {ChatColors.Default}.rethrow, .throwindex <index>, .lastindex, .delay <number>");
                player.PrintToChat($" {ChatColors.Green}工具与开关: {ChatColors.Default}.clear, .fastforward, .last, .back, .solid, .impacts, .traj");
                player.PrintToChat($" {ChatColors.Green}阵营与其他: {ChatColors.Default}.ct, .t, .spec, .fas, .god, .dryrun, .break, .exitprac");
                
                // 向控制台输出完整指令文档 - 分段发送避免截断
                player.PrintToConsole("=== 练习模式指令列表 ===\n");
                
                // 出生点操作部分
                player.PrintToConsole("\n【出生点操作】\n" +
                    ".spawn <number>  传送到同队指定编号的竞技比赛出生点\n" +
                    ".ctspawn <number>  传送到 CT 方指定编号的竞技比赛出生点（别名：.cts）\n" +
                    ".tspawn <number>  传送到 T 方指定编号的竞技比赛出生点（别名：.ts）\n" +
                    ".bestspawn  传送到距离当前位置最近的己方出生点\n" +
                    ".worstspawn  传送到距离当前位置最远的己方出生点\n" +
                    ".bestctspawn  传送到距离当前位置最近的 CT 方出生点\n" +
                    ".worstctspawn  传送到距离当前位置最远的 CT 方出生点\n" +
                    ".besttspawn  传送到距离当前位置最近的 T 方出生点\n" +
                    ".worsttspawn  传送到距离当前位置最远的 T 方出生点\n" +
                    ".showspawns  高亮显示所有竞技比赛出生点\n" +
                    ".hidespawns  隐藏高亮显示的出生点\n");
                
                // 机器人控制部分
                player.PrintToConsole("\n【机器人控制】\n" +
                    ".bot  在玩家当前位置添加一个机器人\n" +
                    ".crouchbot  在玩家当前位置添加一个蹲着的机器人（别名：.cbot）\n" +
                    ".boost  在当前位置添加一个机器人并将玩家提升到机器人上方\n" +
                    ".crouchboost  在当前位置添加一个蹲着的机器人并将玩家提升到机器人上方\n" +
                    ".kick  移除准星所指的机器人\n" +
                    ".kickall  移除所有机器人\n");
                
                // 阵营与模式部分
                player.PrintToConsole("\n【阵营与模式】\n" +
                    ".ct,.t,.spec  将玩家更换到请求的队伍\n" +
                    ".fas /.watchme  强制所有玩家进入观察者模式，除了使用此命令的玩家\n" +
                    ".dryrun  开启空跑模式（Dryrun Mode）（别名：.dry）\n" +
                    ".god  开启上帝模式（God Mode）\n");
                
                // 投掷物管理部分
                player.PrintToConsole("\n【投掷物管理】\n" +
                    ".savenade <n> <可选描述>  保存投掷物准星（别名：.sn）\n" +
                    ".loadnade <n>  加载投掷物准星（别名：.ln）\n" +
                    ".deletenade <n>  从文件中删除投掷物准星（别名：.dn）\n" +
                    ".importnade <code>  保存投掷物准星时会在聊天框中打印代码，也可以从 savednades.cfg 中获取（别名：.in）\n" +
                    ".listnades <可选过滤器>  列出所有保存的投掷物准星，如果提供过滤器则只显示匹配的（别名：.lin）\n");
                
                // 投掷操作部分
                player.PrintToConsole("\n【投掷操作】\n" +
                    ".rethrow  重新投掷你最后投掷的手雷（别名：.rt）\n" +
                    ".last  传送回你最后投掷手雷的位置\n" +
                    ".back <number>  传送回你手雷历史记录中指定的位置\n" +
                    ".delay <delay_in_seconds>  为你最后的手雷设置延迟。这仅在使用 .rethrow 或 .throwindex 时使用\n" +
                    ".throwindex <index> <可选 index> <可选 index>  从你的手雷投掷历史记录中投掷指定位置的手雷\n" +
                    ".lastindex  打印你最后投掷的手雷的索引号\n" +
                    ".rethrowsmoke  投掷你最后投掷的烟雾弹\n" +
                    ".rethrownade  投掷你最后投掷的手雷\n" +
                    ".rethrowflash  投掷你最后投掷的闪光弹\n" +
                    ".rethrowmolotov  投掷你最后投掷的燃烧瓶\n" +
                    ".rethrowdecoy  投掷你最后投掷的诱饵弹\n");
                
                // 实用工具部分
                player.PrintToConsole("\n【实用工具】\n" +
                    ".clear  清除所有活跃的烟雾弹、燃烧弹和燃烧瓶\n" +
                    ".fastforward  将服务器时间快进到 20 秒（别名：.ff）\n" +
                    ".noflash  切换闪光弹免疫（未开启 noflash 的玩家仍会被闪，别名：.noblind）\n" +
                    ".timer  立即开始计时器，再次输入 .timer 时停止并显示持续时间\n" +
                    ".break  破坏所有可破坏的实体（玻璃窗、木门、通风口等）\n");
                
                // 显示与切换部分
                player.PrintToConsole("\n【显示与切换】\n" +
                    ".solid  切换 mp_solid_teammates（队友碰撞）- 当前值: " + ConVar.Find("mp_solid_teammates")!.GetPrimitiveValue<int>() + "\n" +
                    ".impacts  切换 sv_showimpacts（显示子弹命中点）- 当前值: " + ConVar.Find("sv_showimpacts")!.GetPrimitiveValue<int>() + "\n" +
                    ".traj  切换 sv_grenade_trajectory_prac_pipreview（显示投掷物轨迹）- 当前值: " + ConVar.Find("sv_grenade_trajectory_prac_pipreview")!.GetPrimitiveValue<bool>() + "\n");
                
                player.PrintToChat($" {ChatColors.Default}完整指令列表已输出到控制台，请查看控制台");
                
                return;
            }
            if (readyAvailable)
            {
                player!.PrintToChat($" {ChatColors.Green}准备/取消准备: {ChatColors.Default}.ready, .unready");
                return;
            }
            if (isSideSelectionPhase)
            {
                player!.PrintToChat($" {ChatColors.Green}选边: {ChatColors.Default}.stay, .switch");
                return;
            }
            if (matchStarted)
            {
                string stopCommandMessage = isStopCommandAvailable ? ", .stop" : "";
                player!.PrintToChat($" {ChatColors.Green}暂停/恢复: {ChatColors.Default}.pause, .unpause, .tac, .tech{stopCommandMessage}");
                return;
            }
        }

        public void LoadClientNames()
        {
            string namesFileName = "Match_" + liveMatchId.ToString() + ".ini";
            string namesFilePath = Server.GameDirectory + "/csgo/MatchZyPlayerNames/" + namesFileName;
            string? directoryPath = Path.GetDirectoryName(namesFilePath);
            if (directoryPath != null)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\"Names\"");
            sb.AppendLine("{");

            WriteClientNamesInFile(sb, matchzyTeam1.teamPlayers);
            WriteClientNamesInFile(sb, matchzyTeam2.teamPlayers);
            WriteClientNamesInFile(sb, matchConfig.Spectators);

            sb.AppendLine("}");
            File.WriteAllText(namesFilePath, sb.ToString());
            Server.ExecuteCommand($"sv_load_forced_client_names_file MatchZyPlayerNames/" + namesFileName);
        }

        public void WriteClientNamesInFile(StringBuilder sb, JToken? players)
        {
            if (players == null) return;
            foreach (JProperty player in players)
            {
                string steamId = player.Name;
                string escapedName = player.Value.ToString().Replace("\"", "\\\"").Trim();

                if (string.IsNullOrEmpty(escapedName)) continue;

                sb.AppendLine($"\t\"{steamId}\"\t\t\"{escapedName}\"");
            }
        }

        static bool IsValidUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? result))
            {
                return result != null && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
            }
            return false;
        }

        public string GetConvarStringValue(ConVar? cvar)
        {
            try
            {
                if (cvar == null) return "";
                string convarValue = cvar.Type switch
                {
                    ConVarType.Bool => cvar.GetPrimitiveValue<bool>().ToString(),
                    ConVarType.Float32 or ConVarType.Float64 => cvar.GetPrimitiveValue<float>().ToString(),
                    ConVarType.UInt16 => cvar.GetPrimitiveValue<ushort>().ToString(),
                    ConVarType.Int16 => cvar.GetPrimitiveValue<short>().ToString(),
                    ConVarType.UInt32 => cvar.GetPrimitiveValue<uint>().ToString(),
                    ConVarType.Int32 => cvar.GetPrimitiveValue<int>().ToString(),
                    ConVarType.Int64 => cvar.GetPrimitiveValue<long>().ToString(),
                    ConVarType.UInt64 => cvar.GetPrimitiveValue<ulong>().ToString(),
                    ConVarType.String => cvar.StringValue,
                    _ => "",
                };
                return convarValue;
            }
            catch (Exception ex)
            {
                Log($"[GetConvarStringValue - FATAL] Exception occurred: {ex.Message}");
                return "";
            }

        }

        public void SetConvarValue(ConVar? cvar, string value)
        {
            if (cvar == null) return;
            Dictionary<ConVarType, Action<string>> conversionMap = new()
            {
                { ConVarType.Bool, v => cvar.SetValue(int.TryParse(v, out int intValue) && intValue >= 1 || Convert.ToBoolean(v) ) },
                { ConVarType.Float32, v => cvar.SetValue(Convert.ToSingle(v)) },
                { ConVarType.Float64, v => cvar.SetValue(Convert.ToSingle(v)) },
                { ConVarType.UInt16, v => cvar.SetValue(Convert.ToUInt16(v)) },
                { ConVarType.Int16, v => cvar.SetValue(Convert.ToInt16(v)) },
                { ConVarType.UInt32, v => cvar.SetValue(Convert.ToUInt32(v)) },
                { ConVarType.Int32, v => cvar.SetValue(Convert.ToInt32(v)) },
                { ConVarType.Int64, v => cvar.SetValue(Convert.ToInt64(v)) },
                { ConVarType.UInt64, v => cvar.SetValue(Convert.ToUInt64(v)) },
                { ConVarType.String, v => cvar.SetValue(v) },
            };

            if (conversionMap.TryGetValue(cvar.Type, out var conversion))
            {
                try
                {
                    conversion(value);
                }
                catch (Exception ex)
                {
                    Log($"[SetConvarValue - FATAL] Exception occurred: {ex.Message}");
                }
            }
        }

        public void ExecuteChangedConvars()
        {
            foreach (string key in matchConfig.ChangedCvars.Keys)
            {
                string value = matchConfig.ChangedCvars[key];
                Log($"[ExecuteChangedConvars] Execing: {key} \"{value}\"");
                Server.ExecuteCommand($"{key} \"{value}\"");
            }
        }

        public void ResetChangedConvars()
        {
            foreach (string key in matchConfig.OriginalCvars.Keys)
            {
                string value = matchConfig.OriginalCvars[key];
                Log($"[ResetChangedConvars] Execing: {key} \"{value}\"");
                Server.ExecuteCommand($"{key} {value}");
            }
        }

        public string FormatCvarValue(string value)
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            (int team1Score, int team2Score) = GetTeamsScore();

            var formattedValue = value
                .Replace("{TIME}", formattedTime.Replace(" ", "_"))
                .Replace("{MATCH_ID}", $"{liveMatchId}")
                .Replace("{MAP}", Server.MapName)
                .Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString())
                .Replace("{TEAM1}", matchzyTeam1.teamName.Replace(" ", "_"))
                .Replace("{TEAM2}", matchzyTeam2.teamName.Replace(" ", "_"))
                .Replace("{TEAM1_SCORE}", team1Score.ToString())
                .Replace("{TEAM2_SCORE}", team2Score.ToString());
            return formattedValue;
        }

        public void UpdateHostname()
        {
            string hostname = hostnameFormat.Value.Trim();
            if (hostname == "" || hostname == "\"\"") return;
            string formattedHostname = FormatCvarValue(hostname);
            Log($"UPDATING HOSTNAME TO: {formattedHostname}");
            Server.ExecuteCommand($"hostname {formattedHostname}");
        }

        public CCSGameRules GetGameRules()
        {
            return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
        }

        public int GetGamePhase()
        {
            return GetGameRules().GamePhase;
        }

        public bool IsHalfTimePhase()
        {
            try
            {
                return GetGamePhase() == 4;
            }
            catch (Exception e)
            {
                Log($"[IsHalfTime FATAL] An error occurred: {e.Message}");
                return false;
            }

        }

        public bool IsPostGamePhase()
        {
            try
            {
                return GetGamePhase() == 5;
            }
            catch (Exception e)
            {
                Log($"[IsPostGamePhase FATAL] An error occurred: {e.Message}");
                return false;
            }

        }

        public bool IsTacticalTimeoutActive()
        {
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;

            return (gameRules.CTTimeOutActive || gameRules.TerroristTimeOutActive) && gameRules.FreezePeriod;
        }

        public (Dictionary<ulong, Dictionary<string, object>>, List<StatsPlayer>, List<StatsPlayer>) GetPlayerStatsDict()
        {
            Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary = new Dictionary<ulong, Dictionary<string, object>>();
            List<StatsPlayer> playerStatsListTeam1 = new();
            List<StatsPlayer> playerStatsListTeam2 = new();
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            int roundsPlayed = gameRules.TotalRoundsPlayed;
            try
            {
                foreach (int key in playerData.Keys)
                {
                    CCSPlayerController player = playerData[key];
                    if (!player.IsValid || player.ActionTrackingServices == null) continue;

                    var playerStats = player.ActionTrackingServices.MatchStats;
                    ulong steamid64 = player.SteamID;

                    // Create a nested dictionary to store individual stats for the player
                    Dictionary<string, object> stats = new Dictionary<string, object>
                    {
                        { "PlayerName", player.PlayerName },
                        { "Kills", playerStats.Kills },
                        { "Deaths", playerStats.Deaths },
                        { "Assists", playerStats.Assists },
                        { "Damage", playerStats.Damage },
                        { "Enemy2Ks", playerStats.Enemy2Ks },
                        { "Enemy3Ks", playerStats.Enemy3Ks },
                        { "Enemy4Ks", playerStats.Enemy4Ks },
                        { "Enemy5Ks", playerStats.Enemy5Ks },
                        { "EntryCount", playerStats.EntryCount },
                        { "EntryWins", playerStats.EntryWins },
                        { "1v1Count", playerStats.I1v1Count },
                        { "1v1Wins", playerStats.I1v1Wins },
                        { "1v2Count", playerStats.I1v2Count },
                        { "1v2Wins", playerStats.I1v2Wins },
                        { "UtilityCount", playerStats.Utility_Count },
                        { "UtilitySuccess", playerStats.Utility_Successes },
                        { "UtilityDamage", playerStats.UtilityDamage },
                        { "UtilityEnemies", playerStats.Utility_Enemies },
                        { "FlashCount", playerStats.Flash_Count },
                        { "FlashSuccess", playerStats.Flash_Successes },
                        { "HealthPointsRemovedTotal", playerStats.HealthPointsRemovedTotal },
                        { "HealthPointsDealtTotal", playerStats.HealthPointsDealtTotal },
                        { "ShotsFiredTotal", playerStats.ShotsFiredTotal },
                        { "ShotsOnTargetTotal", playerStats.ShotsOnTargetTotal },
                        { "EquipmentValue", playerStats.EquipmentValue },
                        { "MoneySaved", playerStats.MoneySaved },
                        { "KillReward", playerStats.KillReward },
                        { "LiveTime", playerStats.LiveTime },
                        { "HeadShotKills", playerStats.HeadShotKills },
                        { "CashEarned", playerStats.CashEarned },
                        { "EnemiesFlashed", playerStats.EnemiesFlashed }
                    };

                    string teamName = "Spectator";
                    if (player.TeamNum == 3)
                    {
                        teamName = reverseTeamSides["CT"].teamName;
                    }
                    else if (player.TeamNum == 2)
                    {
                        teamName = reverseTeamSides["TERRORIST"].teamName;
                    }

                    stats["TeamName"] = teamName;

                    playerStatsDictionary.Add(steamid64, stats);

                    // Populate PlayerStats instance
                    // Todo: Implement stats which are marked as 0 for now
                    PlayerStats playerStatsInstance = new()
                    {
                        Kills = playerStats.Kills,
                        Deaths = playerStats.Deaths,
                        Assists = playerStats.Assists,
                        FlashAssists = 0,
                        TeamKills = 0,
                        Suicides = 0,
                        Damage = playerStats.Damage,
                        UtilityDamage = playerStats.UtilityDamage,
                        EnemiesFlashed = playerStats.EnemiesFlashed,
                        FriendliesFlashed = 0,
                        KnifeKills = 0,
                        HeadshotKills = playerStats.HeadShotKills,
                        RoundsPlayed = roundsPlayed,
                        BombDefuses = 0,
                        BombPlants = 0,
                        Kills1 = 0,
                        Kills2 = playerStats.Enemy2Ks,
                        Kills3 = playerStats.Enemy3Ks,
                        Kills4 = playerStats.Enemy4Ks,
                        Kills5 = playerStats.Enemy5Ks,
                        OneV1s = playerStats.I1v1Wins,
                        OneV2s = playerStats.I1v2Wins,
                        OneV3s = 0,
                        OneV4s = 0,
                        OneV5s = 0,
                        FirstKillsT = 0,
                        FirstKillsCT = 0,
                        FirstDeathsT = 0,
                        FirstDeathsCT = 0,
                        TradeKills = 0,
                        Kast = 0,
                        Score = player.Score,
                        Mvps = player.MVPs,
                    };

                    StatsPlayer statsPlayer = new()
                    {
                        SteamId = steamid64.ToString(),
                        Name = player.PlayerName,
                        Stats = playerStatsInstance
                    };

                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;

                    if (player.TeamNum == 3)
                    {
                        if (ctTeamNum == 1) playerStatsListTeam1.Add(statsPlayer);
                        if (ctTeamNum == 2) playerStatsListTeam2.Add(statsPlayer);
                    }
                    else if (player.TeamNum == 2)
                    {
                        if (tTeamNum == 1) playerStatsListTeam1.Add(statsPlayer);
                        if (tTeamNum == 2) playerStatsListTeam2.Add(statsPlayer);
                    }
                }
            }
            catch (Exception e)
            {
                Log($"[GetPlayerStatsDict FATAL] An error occurred: {e.Message}");
            }

            return (playerStatsDictionary, playerStatsListTeam1, playerStatsListTeam2);
        }

        static string RemoveSpecialCharacters(string input)
        {
            Regex regex = new("[^a-zA-Z0-9 _-]");
            return regex.Replace(input, "");
        }

        private void Log(string message)
        {
            Console.WriteLine("[MatchZy] " + message);
        }

        private void AutoStart()
        {
            Log($"[AutoStart] autoStartMode: {autoStartMode}");
            if (autoStartMode == 0)
            {
                StartSleepMode();
            }
            if (autoStartMode == 1)
            {
                readyAvailable = true;
                isPractice = false;
                StartWarmup();
            }
            if (autoStartMode == 2)
            {
                StartPracticeMode();
            }
        }

        public int GetGameMode()
        {
            var convar = ConVar.Find("game_mode");
            if (convar != null)
            {
                return convar.GetPrimitiveValue<int>();
            }
            return -1;
        }

        public int GetGameType()
        {
            var convar = ConVar.Find("game_type");
            if (convar != null)
            {
                return convar.GetPrimitiveValue<int>();
            }
            return -1;
        }

        public void SetCorrectGameMode()
        {
            ConVar.Find("game_mode")!.SetValue(matchConfig.Wingman ? 2 : 1);
            ConVar.Find("game_type")!.SetValue(0); // Classic GameType
        }

        public bool IsMapReloadRequiredForGameMode(bool wingman)
        {
            int expectedMode = wingman ? 2 : 1;
            if (GetGameMode() != expectedMode || GetGameType() != 0)
            {
                return true;
            }
            return false;
        }

        public bool IsWingmanMode()
        {
            if (GetGameMode() == 2 && GetGameType() == 0) return true;
            return false;
        }

        public void KickPlayer(CCSPlayerController player)
        {
            if (player.UserId.HasValue)
            {
                Server.ExecuteCommand($"kickid {(ushort)player.UserId}");
            }
        }

        public bool IsPlayerValid(CCSPlayerController? player)
        {
            return (
                player != null &&
                player.IsValid &&
                player.PlayerPawn.IsValid &&
                player.PlayerPawn.Value != null
            );
        }

        public static Color GetPlayerTeammateColor(CCSPlayerController playerController)
        {
            return playerController.CompTeammateColor switch
            {
                1 => Color.FromArgb(50, 255, 0),
                2 => Color.FromArgb(255, 255, 0),
                3 => Color.FromArgb(255, 132, 0),
                4 => Color.FromArgb(255, 0, 255),
                0 => Color.FromArgb(0, 187, 255),
                _ => Color.Red,
            };
        }

        public static string? GetConvarValueFromCFGFile(string filePath, string convarName)
        {
            var fileContent = File.ReadAllText(filePath);

            string pattern = @$"^{convarName}\s+(.+)$";

            Regex regex = new(pattern, RegexOptions.Multiline);

            Match match = regex.Match(fileContent);
            string? value = match.Success ? match.Groups[1].Value : null;
            return value;
        }

        public async Task UploadFileAsync(string? filePath, string fileUploadURL, string headerKey, string headerValue, long matchId, int mapNumber, int roundNumber)
        {
            if (filePath == null || fileUploadURL == "")
            {
                Log($"[UploadFileAsync] 无法上传文件，filePath或fileUploadURL未设置。filePath: {filePath} fileUploadURL: {fileUploadURL}");
                return;
            }

            try
            {
                using var httpClient = new HttpClient();
                Log($"[UploadFileAsync] 准备上传文件到 {fileUploadURL}。完整路径: {filePath}");

                if (!File.Exists(filePath))
                {
                    Log($"[UploadFileAsync ERROR] 文件未找到: {filePath}");
                    return;
                }

                using FileStream fileStream = File.OpenRead(filePath);

                byte[] fileContent = new byte[fileStream.Length];
                await fileStream.ReadAsync(fileContent, 0, (int)fileStream.Length);

                using ByteArrayContent content = new(fileContent);
                content.Headers.Add("Content-Type", "application/octet-stream");

                content.Headers.Add("MatchZy-FileName", Path.GetFileName(filePath));
                content.Headers.Add("MatchZy-MatchId", matchId.ToString());
                content.Headers.Add("MatchZy-MapNumber", mapNumber.ToString());
                content.Headers.Add("MatchZy-RoundNumber", roundNumber.ToString());

                // For Get5 Panel
                content.Headers.Add("Get5-FileName", Path.GetFileName(filePath));
                content.Headers.Add("Get5-MatchId", matchId.ToString());
                content.Headers.Add("Get5-MapNumber", mapNumber.ToString());
                content.Headers.Add("Get5-RoundNumber", roundNumber.ToString());


                if (!string.IsNullOrEmpty(headerKey) && !string.IsNullOrEmpty(headerValue))
                {
                    httpClient.DefaultRequestHeaders.Add(headerKey, headerValue);
                }

                HttpResponseMessage response = await httpClient.PostAsync(fileUploadURL, content);

                if (response.IsSuccessStatusCode)
                {
                    Log($"[UploadFileAsync] 文件上传成功，比赛ID: {matchId} 地图编号: {mapNumber} 文件名: {Path.GetFileName(filePath)}。");
                }
                else
                {
                    Log($"[UploadFileAsync ERROR] 文件上传失败。状态码: {response.StatusCode} 响应: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception e)
            {
                Log($"[UploadFileAsync FATAL] 发生错误: {e.Message}");
            }
        }

        public bool HandlePlayerWhitelist(CCSPlayerController player, string steamId)
        {
            string whitelistfileName = "MatchZy/whitelist.cfg";
            string whitelistPath = Path.Join(Server.GameDirectory + "/csgo/cfg", whitelistfileName);
            string? directoryPath = Path.GetDirectoryName(whitelistPath);
            if (directoryPath != null)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            if (!File.Exists(whitelistPath)) File.WriteAllLines(whitelistPath, new[] { "Steamid1", "Steamid2" });

            var whiteList = File.ReadAllLines(whitelistPath);

            if (isWhitelistRequired == true)
            {
                if (!whiteList.Contains(steamId.ToString()))
                {
                    Log($"[EventPlayerConnectFull] 踢出玩家 STEAMID: {steamId}, 名称: {player.PlayerName} (未在白名单中！)");
                    PrintToAllChat($"踢出玩家 {player.PlayerName} - 未在白名单中。");
                    KickPlayer(player);
                    return true;
                }
            }

            return false;
        }

        public void SwitchPlayerTeam(CCSPlayerController player, CsTeam team)
        {
            if (player.Team == team) return;

            Server.NextFrame(() =>
            {
                if (team == CsTeam.Spectator)
                {
                    player.ChangeTeam(team);
                }
                else
                {
                    player.SwitchTeam(team);
                    var gameRules = GetGameRules();
                    if (gameRules.WarmupPeriod)
                    {
                        player.Respawn();
                    }
                }
            });
        }

        public void SetPlayerInvisible(CCSPlayerController player, bool setWeaponsInvisible)
        {
            if (!IsPlayerValid(player)) return;
            var playerPawnValue = player.PlayerPawn.Value;

            if (playerPawnValue != null && playerPawnValue.IsValid)
            {
                playerPawnValue.Render = Color.FromArgb(0, 0, 0, 0);
                Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_clrRender");
            }

            if (!setWeaponsInvisible) return;

            var activeWeapon = playerPawnValue!.WeaponServices?.ActiveWeapon.Value;
            if (activeWeapon != null && activeWeapon.IsValid)
            {
                activeWeapon.Render = Color.FromArgb(0, 0, 0, 0);
                activeWeapon.ShadowStrength = 0.0f;
                Utilities.SetStateChanged(activeWeapon, "CBaseModelEntity", "m_clrRender");
            }

            var myWeapons = playerPawnValue.WeaponServices?.MyWeapons;
            if (myWeapons != null)
            {
                foreach (var gun in myWeapons)
                {
                    var weapon = gun.Value;
                    if (weapon != null)
                    {
                        weapon.Render = Color.FromArgb(0, 0, 0, 0);
                        weapon.ShadowStrength = 0.0f;
                        Utilities.SetStateChanged(weapon, "CBaseModelEntity", "m_clrRender");
                    }
                }
            }
        }

        public void SetPlayerVisible(CCSPlayerController player)
        {
            if (!IsPlayerValid(player)) return;

            var playerPawnValue = player.PlayerPawn.Value;
            if (playerPawnValue == null)
                return;

            playerPawnValue.Render = Color.FromArgb(255, 255, 255, 255);
            Utilities.SetStateChanged(playerPawnValue, "CBaseModelEntity", "m_clrRender");
        }

        public void DropWeaponByDesignerName(CCSPlayerController player, string weaponName)
        {
            if (!IsPlayerValid(player) || player.PlayerPawn.Value!.WeaponServices is null) return;
            var matchedWeapon = player.PlayerPawn.Value!.WeaponServices!.MyWeapons
                .Where(weapon => weapon.Value!.DesignerName == weaponName).FirstOrDefault();

            if (matchedWeapon != null && matchedWeapon.IsValid)
            {
                player.PlayerPawn.Value.WeaponServices.ActiveWeapon.Raw = matchedWeapon.Raw;
                player.DropActiveWeapon();
            }
        }

        public void RandomizeSpawns()
        {
            List<CCSPlayerController> players = Utilities.GetPlayers();

            Dictionary<byte, List<Position>> teamSpawns = new()
            {
                { (byte)CsTeam.CounterTerrorist, spawnsData[(byte)CsTeam.CounterTerrorist].Select(position => new Position(position)).ToList() },
                { (byte)CsTeam.Terrorist, spawnsData[(byte)CsTeam.Terrorist].Select(position => new Position(position)).ToList() }
            };

            Random random = new();

            foreach (var player in players)
            {
                if (!IsPlayerValid(player)) continue;
                
                if (teamSpawns[player.TeamNum].Count == 0) break;

                int randomIndex = random.Next(teamSpawns[player.TeamNum].Count);
                Position spawnPosition = teamSpawns[player.TeamNum][randomIndex];
                teamSpawns[player.TeamNum].RemoveAt(randomIndex);

                spawnPosition.Teleport(player);
            }
        }

        // 注册OnTick事件，用于持续更新HUD显示
        public void RegisterHUDListeners()
        {
            // 删除错误的DeregisterEventHandler调用
            // 注册OnTick监听器
            RegisterListener<Listeners.OnTick>(() => {
                ShowInstructionsHUD();
                
                // 如果C4已安放且计时器启用，显示C4计时器
                if (c4Planted && showC4Timer)
                {
                    ShowC4TimerHUD();
                }
            });
            
            // 注册C4安放和拆除事件
            RegisterEventHandler<EventBombPlanted>(OnBombPlanted, HookMode.Post);
            RegisterEventHandler<EventBombDefused>(OnBombDefused, HookMode.Post);
            RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
            
            Log("[C4Timer] C4计时器事件已注册！");
        }
        
        // 显示C4计时器HUD
        private void ShowC4TimerHUD()
        {
            if (!c4Planted || !showC4Timer) return;
            
            float timeRemaining = c4ExplosionTime - Server.CurrentTime;
            
            // 添加调试日志，每5秒打印一次，避免日志过多
            if (Math.Floor(timeRemaining) % 5 == 0 && Math.Floor(timeRemaining * 10) % 10 == 0)
            {
                Log($"[C4Timer] 剩余时间: {timeRemaining:0.1}秒, c4Planted={c4Planted}, showC4Timer={showC4Timer}");
            }
            
            if (timeRemaining <= 0)
            {
                c4Planted = false;
                return;
            }
            
            // 根据剩余时间确定颜色
            string color = "white";
            if (timeRemaining <= 5)
                color = "red";
            else if (timeRemaining <= 10)
                color = "orange";
            else if (timeRemaining <= 20)
                color = "yellow";
            
            // 创建进度条
            int progressBarLength = 20; // 进度条总长度
            int filledLength = (int)Math.Ceiling(timeRemaining / 40.0f * progressBarLength); // 假设C4为40秒
            if (filledLength > progressBarLength) filledLength = progressBarLength;
            if (filledLength < 0) filledLength = 0;
            
            string progressBar = "[";
            for (int i = 0; i < progressBarLength; i++)
            {
                if (i < filledLength)
                    progressBar += "█"; // 填充部分
                else
                    progressBar += "░"; // 未填充部分
            }
            progressBar += "]";
            
            // 根据剩余时间选择显示格式
            string timeDisplay;
            if (timeRemaining <= 10)
            {
                // 小于等于10秒时显示小数点
                int seconds = (int)Math.Floor(timeRemaining);
                int tenths = (int)Math.Floor((timeRemaining - seconds) * 10);
                timeDisplay = $"{seconds}.{tenths}";
            }
            else
            {
                // 大于10秒时只显示整数
                int seconds = (int)Math.Round(timeRemaining);
                timeDisplay = $"{seconds}";
            }
                
            foreach (var player in Utilities.GetPlayers())
            {
                if (!IsPlayerValid(player)) continue;
                
                // 使用更好的格式显示C4计时器
                player.PrintToCenterHtml($"<font color='{color}' size='28'><b>C4将在 {timeDisplay} 秒后爆炸</b></font><br>" +
                                        $"<font color='{color}' size='24'>{progressBar}</font>");
            }
        }
        
        // C4安放事件处理
        public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
        {
            // 先重新加载配置，确保使用最新的设置
            LoadC4TimerSettings();
            
            // 添加日志，帮助排查问题
            Log($"[C4Timer] 炸弹已安放! showC4Timer={showC4Timer}, enableC4Timer.Value={enableC4Timer.Value}");
            
            // 检查C4计时器是否被禁用
            if (!showC4Timer)
            {
                Log($"[C4Timer] C4计时器已禁用，不显示倒计时");
                c4Planted = false;
                return HookResult.Continue;
            }
            
            try
            {
                // 获取C4爆炸时间
                ConVar? mp_c4timer = ConVar.Find("mp_c4timer");
                float c4Timer = 40.0f; // 默认40秒
                
                if (mp_c4timer != null)
                {
                    try 
                    {
                        c4Timer = mp_c4timer.GetPrimitiveValue<float>();
                    }
                    catch (Exception ex)
                    {
                        Log($"[C4Timer] 获取mp_c4timer值时出错: {ex.Message}，使用默认值40秒");
                    }
                }
                
                // 确保时间有效
                if (c4Timer <= 0 || c4Timer > 120)
                {
                    c4Timer = 40.0f; // 如果时间不合理，使用默认40秒
                }
                
                c4ExplosionTime = Server.CurrentTime + c4Timer;
                c4Planted = true;
                
                // 添加调试日志
                Log($"[C4Timer] C4将在 {c4Timer} 秒后爆炸，当前服务器时间: {Server.CurrentTime:0.1}, 爆炸时间: {c4ExplosionTime:0.1}");
                
                // 获取事件中的玩家信息
                try 
                {
                    var userid = @event.Userid;
                    if (userid != null && userid.IsValid)
                    {
                        Log($"[C4Timer] 玩家 {userid.PlayerName} 安放了C4炸弹");
                    }
                }
                catch
                {
                    // 忽略获取玩家信息时的错误
                }
            }
            catch (Exception ex)
            {
                Log($"[C4Timer] 处理C4安放事件时发生错误: {ex.Message}");
            }
            
            return HookResult.Continue;
        }
        
        // C4拆除事件处理
        public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
        {
            c4Planted = false;
            return HookResult.Continue;
        }
        
        // 回合开始事件处理
        public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            c4Planted = false;
            return HookResult.Continue;
        }
        
        // 回合结束事件处理
        public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            c4Planted = false;
            return HookResult.Continue;
        }
    }
}
