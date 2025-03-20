using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Cvars;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MatchZy
{
    [MinimumApiVersion(227)]
    public partial class MatchZy : BasePlugin
    {

        public override string ModuleName => "MatchZy";
        public override string ModuleVersion => "0.0.1";

        public override string ModuleAuthor => "MEng_YangX - (https://github.com/MEngYangX)";

        public override string ModuleDescription => "A plugin for running and managing CS2 practice/pugs/scrims/matches! Adapted from MatchZy";

        public string chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        // Plugin start phase data
        public bool isPractice = false;
        public bool isSleep = false;
        public bool readyAvailable = false;
        public bool matchStarted = false;
        public bool isWarmup = false;
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public long liveMatchId = -1;
        public int autoStartMode = 1;

        public bool mapReloadRequired = false;

        // Pause Data
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object> {
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" }
        };

        // Knife Data
        public int knifeWinner = 0;
        public string knifeWinnerName = "";

        // Players Data (including admins)
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, CCSPlayerController> playerData = new Dictionary<int, CCSPlayerController>();

        // Admin Data
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();

        // Timers
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;

        // Each message is kept in chat display for ~13 seconds, hence setting default chat timer to 13 seconds.
        // Configurable using matchzy_chat_messages_timer_delay <seconds>
        public int chatTimerDelay = 13;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; // Number of ready players required start the match. If set to 0, all connected players have to ready-up to start the match.
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;

        public bool isPlayOutEnabled = false;

        public bool playerHasTakenDamage = false;

        // 存储每个玩家的无敌模式状态
        public Dictionary<int, bool> godModeEnabled = new Dictionary<int, bool>();
        
        // 战术暂停计数和计时器
        public Dictionary<string, int> tacticalPausesUsed = new Dictionary<string, int>() { { "ct", 0 }, { "t", 0 } };
        public CounterStrikeSharp.API.Modules.Timers.Timer? tacticalPauseTimer = null;

        // User command - action map
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite/MySQL Database 
        private Database database = new();
    
        // 全景投票系统
        protected CVoteController? VoteController;
        protected bool isVoteInProgress = false;
        protected RecipientFilter voteFilter = new RecipientFilter();
        protected float lastVoteTime = 0.0f;
        protected const float VOTE_COOLDOWN = 3.0f; // 设置3秒冷却时间
        protected Dictionary<int, bool> playerVotes = new Dictionary<int, bool>();
        protected int requiredVotes = 0;

        public bool showC4Timer = true;
        public FakeConVar<bool> enableC4Timer = new("matchzy_enable_c4_timer", "是否在C4安放后显示HUD计时器。设置为false禁用", true);
    
        public override void Load(bool hotReload) {
            
            // 创建插件所需的ConVars已通过FakeConVar注册，无需执行命令
            
            LoadAdmins();

            database.InitializeDatabase(ModuleDirectory);

            // This sets default config ConVars
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;
            
            // 注册HUD监听器
            RegisterHUDListeners();
            
            // 加载C4计时器设置
            LoadC4TimerSettings();

            if (!hotReload) {
                AutoStart();
            } else {
                // Pluign should not be reloaded while a match is live (this would messup with the match flags which were set)
                // Only hot-reload the plugin if you are testing something and don't want to restart the server time and again.
                UpdatePlayersMap();
                AutoStart();
            }

            commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>> {
                { ".ready", OnPlayerReady },
                { ".r", OnPlayerReady },
                { ".forceready", OnForceReadyCommandCommand },
                { ".unready", OnPlayerUnReady },
                { ".notready", OnPlayerUnReady },
                { ".ur", OnPlayerUnReady },
                { ".stay", OnTeamStay },
                { ".switch", OnTeamSwitch },
                { ".swap", OnTeamSwitch },
                { ".tech", OnTechCommand },
                { ".p", OnPauseCommand },
                { ".pause", OnPauseCommand },
                { ".unpause", OnUnpauseCommand },
                { ".up", OnUnpauseCommand },
                { ".forcepause", OnForcePauseCommand },
                { ".fp", OnForcePauseCommand },
                { ".forceunpause", OnForceUnpauseCommand },
                { ".fup", OnForceUnpauseCommand },
                { ".roundknife", OnKnifeCommand },
                { ".rk", OnKnifeCommand },
                { ".playout", OnPlayoutCommand },
                { ".start", OnStartCommand },
                { ".force", OnStartCommand },
                { ".forcestart", OnStartCommand },
                { ".skipveto", OnSkipVetoCommand },
                { ".sv", OnSkipVetoCommand },
                { ".restart", OnRestartMatchCommand },
                { ".rr", OnRestartMatchCommand },
                { ".endmatch", OnEndMatchCommand },
                { ".forceend", OnEndMatchCommand },
                { ".reloadmap", OnMapReloadCommand },
                { ".settings", OnMatchSettingsCommand },
                { ".whitelist", OnWLCommand },
                { ".globalnades", OnSaveNadesAsGlobalCommand },
                { ".reload_admins", OnReloadAdmins },
                { ".tactics", OnPracCommand },
                { ".prac", OnPracCommand },
                { ".showspawns", OnShowSpawnsCommand },
                { ".hidespawns", OnHideSpawnsCommand },
                { ".dryrun", OnDryRunCommand },
                { ".dry", OnDryRunCommand },
                { ".noflash", OnNoFlashCommand },
                { ".break", OnBreakCommand },
                { ".br", OnBreakCommand },
                { ".bot", OnBotCommand },
                { ".kick", OnKickBotCommand },
                { ".cbot", OnCrouchBotCommand },
                { ".crouchbot", OnCrouchBotCommand },
                { ".boost", OnBoostBotCommand },
                { ".crouchboost", OnCrouchBoostBotCommand },
                { ".cboost", OnCrouchBoostBotCommand },
                { ".kickall", OnNoBotsCommand },
                { ".solid", OnSolidCommand },
                { ".impacts", OnImpactsCommand },
                { ".traj", OnTrajCommand },
                { ".pip", OnTrajCommand },
                { ".god", OnGodCommand },
                { ".ff", OnFastForwardCommand },
                { ".fastforward", OnFastForwardCommand },
                { ".clear", OnClearCommand },
                { ".match", OnMatchCommand },
                { ".uncoach", OnUnCoachCommand },
                { ".exitprac", OnMatchCommand },
                { ".stop", OnStopCommand },
                { ".help", OnHelpCommand },
                { ".t", OnTCommand },
                { ".ct", OnCTCommand },
                { ".spec", OnSpecCommand },
                { ".fas", OnFASCommand },
                { ".watch", OnFASCommand },
                { ".last", OnLastCommand },
                { ".ls", OnLastCommand },
                { ".throw", OnRethrowCommand },
                { ".rethrow", OnRethrowCommand },
                { ".rt", OnRethrowCommand },
                { ".throwsmoke", OnRethrowSmokeCommand },
                { ".rethrowsmoke", OnRethrowSmokeCommand },
                { ".rethrows", OnRethrowSmokeCommand },
                { ".thrownade", OnRethrowGrenadeCommand },
                { ".rethrownade", OnRethrowGrenadeCommand },
                { ".rethrown", OnRethrowGrenadeCommand },
                { ".rethrowgrenade", OnRethrowGrenadeCommand },
                { ".throwgrenade", OnRethrowGrenadeCommand },
                { ".rethrowflash", OnRethrowFlashCommand },
                { ".rethrowf", OnRethrowFlashCommand },
                { ".throwflash", OnRethrowFlashCommand },
                { ".rethrowdecoy", OnRethrowDecoyCommand },
                { ".rethrowd", OnRethrowDecoyCommand },
                { ".throwdecoy", OnRethrowDecoyCommand },
                { ".throwmolotov", OnRethrowMolotovCommand },
                { ".rethrowmolotov", OnRethrowMolotovCommand },
                { ".rethrowm", OnRethrowMolotovCommand },
                { ".timer", OnTimerCommand },
                { ".lastindex", OnLastIndexCommand },
                { ".c4timer", OnToggleC4TimerCommand }
            };

            RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
            RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnClientDisconnectPost>(playerSlot => { 
               // May not be required, but just to be on safe side so that player data is properly updated in dictionaries
               // Update: Commenting the below function as it was being called multiple times on map change.
                // UpdatePlayersMap();
            });
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);
            RegisterEventHandler<EventPlayerTeam>((@event, info) => {
                CCSPlayerController? player = @event.Userid;
                if (!IsPlayerValid(player)) return HookResult.Continue;

                if (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!)) {
                    @event.Silent = true;
                    return HookResult.Changed;
                }
                return HookResult.Continue;
            }, HookMode.Pre);

            RegisterEventHandler<EventPlayerTeam>((@event, info) =>
            {
                if (!isMatchSetup && !isVeto) return HookResult.Continue;

                CCSPlayerController? player = @event.Userid;

                if (!IsPlayerValid(player)) return HookResult.Continue;

                if (player!.IsHLTV || player.IsBot)
                {
                    return HookResult.Continue;
                }

                CsTeam playerTeam = GetPlayerTeam(player);

                SwitchPlayerTeam(player, playerTeam);

                return HookResult.Continue;
            });

            AddCommandListener("jointeam", (player, info) =>
            {
                if ((isMatchSetup || isVeto || isKnifeRound || isSideSelectionPhase || isMatchLive) && player != null && player.IsValid) {
                    if (int.TryParse(info.ArgByIndex(1), out int joiningTeam)) {
                        int playerTeam = (int)GetPlayerTeam(player);
                        if (joiningTeam != playerTeam) {
                            PrintToPlayerChat(player, $"当前阶段不允许更换队伍");
                            return HookResult.Stop;
                        }
                    }
                }
                return HookResult.Continue;
            });

            RegisterEventHandler<EventRoundEnd>((@event, info) => 
            {
                if (!isKnifeRound) return HookResult.Continue;

                DetermineKnifeWinner();
                @event.Winner = knifeWinner;
                int finalEvent = 10;
                if (knifeWinner == 3) {
                    finalEvent = 8;
                } else if (knifeWinner == 2) {
                    finalEvent = 9;
                }
                @event.Reason = finalEvent;
                isSideSelectionPhase = true;
                isKnifeRound = false;
                StartAfterKnifeWarmup();

                return HookResult.Changed;
            }, HookMode.Pre);

           RegisterEventHandler<EventRoundEnd>((@event, info) => {
                try 
                {
                    if (isDryRun)
                    {
                        StartPracticeMode();
                        isDryRun = false;
                        return HookResult.Continue;
                    }
                    if (!isMatchLive) return HookResult.Continue;
                    HandlePostRoundEndEvent(@event);
                    return HookResult.Continue;
                }
                catch (Exception e)
                {
                    Log($"[EventRoundEnd FATAL] An error occurred: {e.Message}");
                    return HookResult.Continue;
                }

            }, HookMode.Post);

            // 注册投票事件处理器
            RegisterEventHandler<EventVoteCast>((@event, info) =>
            {
                if (!isVoteInProgress) return HookResult.Continue;

                var player = new CCSPlayerController(NativeAPI.GetEventPlayerController(@event.Handle, "userid"));
                if (player == null || !player.IsValid || !player.UserId.HasValue) 
                    return HookResult.Continue;

                int voteOption = NativeAPI.GetEventInt(@event.Handle, "vote_option");
                
                // 只有获胜队伍的玩家可以投票
                if (player.TeamNum != knifeWinner)
                {
                    PrintToPlayerChat(player, $"{chatPrefix} 只有拼刀获胜方可以参与投票");
                    return HookResult.Continue;
                }

                // 记录玩家的投票
                bool voteValue = (voteOption == 0); // 0 = 同意(F1), 1 = 反对(F2)
                playerVotes[player.UserId.Value] = voteValue;
                
                // 显示玩家的投票选择（调试信息）
                PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} 投票{(voteValue ? "同意" : "反对")}交换阵营");

                // 检查是否所有人都已投票
                int totalVotes = playerVotes.Count;
                int totalPlayers = playerData.Values.Count(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == knifeWinner);
                int yesVotes = playerVotes.Count(v => v.Value);
                int requiredVotes = GetRequiredVotes();

                PrintToAllChat($"当前投票: {yesVotes}票同意, 总共{totalVotes}/{totalPlayers}人已投票 (需要{requiredVotes}票同意)");

                if (totalVotes >= totalPlayers)
                {
                    CheckVoteResult();
                }

                return HookResult.Continue;
            });

            // RegisterEventHandler<EventMapShutdown>((@event, info) => {
            //     Log($"[EventMapShutdown] Resetting match!");
            //     ResetMatch();
            //     return HookResult.Continue;
            // });

            RegisterListener<Listeners.OnMapStart>(mapName => { 
                AddTimer(1.0f, () => {
                    if (!isMatchSetup)
                    {
                        AutoStart();
                        return;
                    }
                    if (isWarmup) StartWarmup();
                    if (isPractice) StartPracticeMode();
                });
            });

            // RegisterListener<Listeners.OnMapEnd>(() => {
            //     Log($"[Listeners.OnMapEnd] Resetting match!");
            //     ResetMatch();
            // });

            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                // Setting money back to 16000 when a player dies in warmup
                var player = @event.Userid;
                if (!isWarmup) return HookResult.Continue;
                if (!IsPlayerValid(player)) return HookResult.Continue;
                if (player!.InGameMoneyServices != null) player.InGameMoneyServices.Account = 16000;
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerHurt>((@event, info) =>
			{
				CCSPlayerController? attacker = @event.Attacker;
                CCSPlayerController? victim = @event.Userid;

                if (!IsPlayerValid(attacker) || !IsPlayerValid(victim)) return HookResult.Continue;

                // 检查玩家是否开启了无敌模式
                if (victim!.UserId.HasValue && godModeEnabled.TryGetValue(victim.UserId.Value, out bool isGodMode) && isGodMode)
                {
                    // 如果开启了无敌模式，恢复血量到100
                    if (victim.PlayerPawn?.Value != null)
                    {
                        victim.PlayerPawn.Value.Health = 100;
                    }
                    return HookResult.Continue;
                }

                if (isPractice && victim!.IsBot)
                {
                    int damage = @event.DmgHealth;
                    int postDamageHealth = @event.Health;
                    PrintToPlayerChat(attacker!, Localizer["matchzy.pracc.damage", damage, victim.PlayerName, postDamageHealth]);
                    return HookResult.Continue;
                }

				if (!attacker!.IsValid || attacker.IsBot && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))
					return HookResult.Continue;
                if (matchStarted && victim!.TeamNum != attacker.TeamNum) 
                {
                    int targetId = (int)victim.UserId!;
                    UpdatePlayerDamageInfo(@event, targetId);
                    if (attacker != victim) playerHasTakenDamage = true;
                }

				return HookResult.Continue;
			});

            RegisterEventHandler<EventPlayerChat>((@event, info) => {

                int currentVersion = Api.GetVersion();
                int index = @event.Userid + 1;
                var playerUserId = NativeAPI.GetUseridFromIndex(index);

                var originalMessage = @event.Text.Trim();
                var message = @event.Text.Trim().ToLower();

                var parts = originalMessage.Split(' ');
                var messageCommand = parts.Length > 0 ? parts[0] : string.Empty;
                var messageCommandArg = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

                CCSPlayerController? player = null;
                if (playerData.TryGetValue(playerUserId, out CCSPlayerController? value)) {
                    player = value;
                }

                if (player == null) {
                    // Somehow we did not had the player in playerData, hence updating the maps again before getting the player
                    UpdatePlayersMap();
                    player = playerData[playerUserId];
                }

                // Handling player commands
                if (commandActions.ContainsKey(message)) {
                    commandActions[message](player, null);
                }

                if (message.StartsWith(".map"))
                {
                    HandleMapChangeCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".readyrequired"))
                {
                    HandleReadyRequiredCommand(player, messageCommandArg);
                }

                if (message.StartsWith(".restore"))
                {
                    HandleRestoreCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".asay"))
                {
                    if (IsPlayerAdmin(player, "css_asay", "@css/chat"))
                    {
                        if (messageCommandArg != "")
                        {
                            Server.PrintToChatAll($"{adminChatPrefix} {messageCommandArg}");
                        }
                        else
                        {
                            // ReplyToUserCommand(player, "Usage: .asay <message>");
                            ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".asay <message>"]);
                        }
                    }
                    else
                    {
                        SendPlayerNotAdminMessage(player);
                    }
                }
                if (message.StartsWith(".savenade") || message.StartsWith(".sn"))
                {
                    HandleSaveNadeCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".delnade") || message.StartsWith(".dn"))
                {
                    HandleDeleteNadeCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".deletenade"))
                {
                    HandleDeleteNadeCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".importnade") || message.StartsWith(".in"))
                {
                    HandleImportNadeCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".listnades") || message.StartsWith(".lin"))
                {
                    HandleListNadesCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".loadnade") || message.StartsWith(".ln"))
                {
                    HandleLoadNadeCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".spawn") || message.StartsWith(".s"))
                {
                    HandleSpawnCommand(player, messageCommandArg, player.TeamNum, "spawn");
                }
                if (message.StartsWith(".ctspawn") || message.StartsWith(".cts"))
                {
                    HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
                }
                if (message.StartsWith(".tspawn") || message.StartsWith(".ts"))
                {
                    HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.Terrorist, "tspawn");
                }
                if (message.StartsWith(".bestspawn") || message.StartsWith(".bs"))
                {
                    if (!isPractice || !IsPlayerValid(player)) return HookResult.Continue;
                    TeleportPlayerToBestSpawn(player!, player!.TeamNum);
                }
                if (message.StartsWith(".worstspawn") || message.StartsWith(".ws"))
                {
                    if (!isPractice || !IsPlayerValid(player)) return HookResult.Continue;
                    TeleportPlayerToWorstSpawn(player!, player!.TeamNum);
                }
                if (message.StartsWith(".bestctspawn") || message.StartsWith(".bcts"))
                {
                    if (!isPractice || !IsPlayerValid(player)) return HookResult.Continue;
                    TeleportPlayerToBestSpawn(player!, (byte)CsTeam.CounterTerrorist);
                }
                if (message.StartsWith(".worstctspawn") || message.StartsWith(".wcts"))
                {
                    if (!isPractice || !IsPlayerValid(player)) return HookResult.Continue;
                    TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.CounterTerrorist);
                }
                if (message.StartsWith(".besttspawn") || message.StartsWith(".bts"))
                {
                    if (!isPractice || !IsPlayerValid(player)) return HookResult.Continue;
                    TeleportPlayerToBestSpawn(player!, (byte)CsTeam.Terrorist);
                }
                if (message.StartsWith(".worsttspawn") || message.StartsWith(".wts"))
                {
                    if (!isPractice || !IsPlayerValid(player)) return HookResult.Continue;
                    TeleportPlayerToWorstSpawn(player!, (byte)CsTeam.Terrorist);
                }
                if (message.StartsWith(".team1"))
                {
                    HandleTeamNameChangeCommand(player, messageCommandArg, 1);
                }
                if (message.StartsWith(".team2"))
                {
                    HandleTeamNameChangeCommand(player, messageCommandArg, 2);
                }
                if (message.StartsWith(".rcon"))
                {
                    if (IsPlayerAdmin(player, "css_rcon", "@css/rcon"))
                    {
                        Server.ExecuteCommand(messageCommandArg);
                        ReplyToUserCommand(player, "Command sent successfully!");
                    }
                    else
                    {
                        SendPlayerNotAdminMessage(player);
                    }
                }
                if (message.StartsWith(".coach"))
                {
                    HandleCoachCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".ban"))
                {
                    HandeMapBanCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".pick"))
                {
                    HandeMapPickCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".back"))
                {
                    HandleBackCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".delay"))
                {
                    HandleDelayCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".throwindex"))
                {
                    HandleThrowIndexCommand(player, messageCommandArg);
                }
                if (message.StartsWith(".throwidx"))
                {
                    HandleThrowIndexCommand(player, messageCommandArg);
                }

                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerBlind>((@event, info) =>
            {
                CCSPlayerController? player = @event.Userid;
                CCSPlayerController? attacker = @event.Attacker;
                if (!isPractice) return HookResult.Continue;

                if (!IsPlayerValid(player) || !IsPlayerValid(attacker)) return HookResult.Continue;

                if (attacker!.IsValid)
                {
                    double roundedBlindDuration = Math.Round(@event.BlindDuration, 2);
                    PrintToPlayerChat(attacker, Localizer["matchzy.pracc.blind", player!.PlayerName, roundedBlindDuration]);
                }
                var userId = player!.UserId;
                if (userId != null && noFlashList.Contains((int)userId))
                {
                    Server.NextFrame(() => KillFlashEffect(player));
                }

                return HookResult.Continue;
            });

            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyDetonate>(EventDecoyDetonateHandler);

            // 注册控制台命令
            AddCommand("matchzy_toggle_c4timer", "切换C4计时器开关状态", (player, info) => 
            {
                ToggleC4TimerConsoleCommand(player, info);
            });
            
            // 添加控制台变量命令处理器
            AddCommand("matchzy_enable_c4_timer", "设置C4计时器开关状态 (true/false)", (player, info) => 
            {
                if (info.ArgCount >= 2)
                {
                    string arg = info.ArgByIndex(1).ToLower();
                    bool newValue = arg == "true" || arg == "1";
                    
                    // 只有当值变更时才处理
                    if (showC4Timer != newValue)
                    {
                        showC4Timer = newValue;
                        enableC4Timer.Value = newValue;
                        Log($"[C4Timer] 通过控制台命令设置C4计时器状态为: {(newValue ? "启用" : "禁用")}");
                        
                        // 如果禁用计时器且C4已安放，强制停止显示
                        if (!newValue && c4Planted)
                        {
                            c4Planted = false;
                        }
                    }
                    
                    // 输出当前状态
                    if (player != null)
                    {
                        PrintToPlayerChat(player, $"{chatPrefix} C4计时器当前状态: {(showC4Timer ? "已启用" : "已禁用")}");
                    }
                    else
                    {
                        Log($"[C4Timer] C4计时器当前状态: {(showC4Timer ? "已启用" : "已禁用")}");
                        Server.ExecuteCommand($"echo \"[MatchZy] C4计时器当前状态: {(showC4Timer ? "已启用" : "已禁用")}\"");
                    }
                }
                else
                {
                    // 仅查询当前状态
                    if (player != null)
                    {
                        PrintToPlayerChat(player, $"{chatPrefix} C4计时器当前状态: {(showC4Timer ? "已启用" : "已禁用")}");
                    }
                    else
                    {
                        Log($"[C4Timer] C4计时器当前状态: {(showC4Timer ? "已启用" : "已禁用")}");
                        Server.ExecuteCommand($"echo \"matchzy_enable_c4_timer = {(showC4Timer ? "true" : "false")}\"");
                    }
                }
            });

            // 初始化战术暂停计数
            tacticalPausesUsed["t"] = 0;
            tacticalPausesUsed["ct"] = 0;
            
            // 在控制台输出战术暂停配置信息
            Log($"[TacticalPause] 已加载战术暂停配置：每队最多{maxTacticalPausesAllowed.Value}次暂停，每次{tacticalPauseDuration.Value}秒");
            Server.ExecuteCommand($"echo \"[MatchZy] 战术暂停配置：每队最多{maxTacticalPausesAllowed.Value}次暂停，每次{tacticalPauseDuration.Value}秒\"");

            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] MatchZy by MEng_YangX");
        }

        private void StartTeamVote()
        {
            float currentTime = Server.CurrentTime;
            if (currentTime - lastVoteTime < VOTE_COOLDOWN)
            {
                PrintToAllChat($"{chatPrefix} 由于投票冷却时间限制，跳过本次投票，保持当前阵营");
                StartLive();
                return;
            }

            if (isVoteInProgress) return;

            // 重置投票冷却时间
            Server.ExecuteCommand("sv_vote_cooldown 0");
            
            // 初始化VoteController
            VoteController = Utilities.FindAllEntitiesByDesignerName<CVoteController>("vote_controller").Last();
            if (VoteController == null || VoteController.Handle == IntPtr.Zero)
            {
                PrintToAllChat($"{chatPrefix} 投票系统初始化失败，跳过投票");
                StartLive();
                return;
            }

            // 重置投票状态
            for (int i = 0; i < 64; i++)
            {
                VoteController.VotesCast[i] = 0;
            }
            
            VoteController.OnlyTeamToVote = -1;
            VoteController.PotentialVotes = 1;
            VoteController.IsYesNoVote = true;
            VoteController.ActiveIssueIndex = 0;

            // 清理投票选项计数
            VoteController.VoteOptionCount[0] = 0;
            VoteController.VoteOptionCount[1] = 0;
            VoteController.VoteOptionCount[2] = 0;
            VoteController.VoteOptionCount[3] = 0;
            VoteController.VoteOptionCount[4] = 0;

            isVoteInProgress = true;
            playerVotes.Clear();
            voteFilter = new RecipientFilter();
            voteFilter.AddAllPlayers();

            // 创建投票
            UserMessage voteStart = UserMessage.FromId(349); // CS_UM_VoteStart = 349
            voteStart.SetInt("team", -1); // -1 表示所有队伍
            voteStart.SetInt("ent_idx", 99); // 实体索引
            voteStart.SetInt("vote_type", 2); // 投票类型：交换阵营
            voteStart.SetString("disp_str", "#SFUI_Vote_SwapCamp_PWA"); // 显示文本
            voteStart.SetString("details_str", "是否与对手交换阵营?"); // 详细信息
            voteStart.SetString("other_team_str", ""); // 其他队伍信息
            voteStart.SetInt("vote_initiator", 0); // 发起投票的玩家

            // 发送投票消息
            voteStart.Send(voteFilter);
            
            // 记录本次投票时间
            lastVoteTime = currentTime;

            // 设置投票超时
            AddTimer(30.0f, () => {
                if (isVoteInProgress)
                {
                    CheckVoteResult();
                }
            });

            // 发送投票状态变更事件
            EventVoteChanged voteChanged = new(true);
            voteChanged.VoteOption1 = 0;
            voteChanged.VoteOption2 = 0;
            voteChanged.VoteOption3 = 0;
            voteChanged.VoteOption4 = 0;
            voteChanged.VoteOption5 = 0;
            voteChanged.Potentialvotes = 1;
            voteChanged.FireEvent(false);
        }

        private int GetRequiredVotes()
        {
            // 计算获胜队伍的总人数
            int totalPlayers = playerData.Values.Count(p => p != null && p.IsValid && !p.IsBot && p.TeamNum == knifeWinner);
            
            // 计算所需票数：(总人数/2+1)
            int required = (totalPlayers / 2) + 1;
            
            // 调试输出
            Console.WriteLine($"[MatchZy] 总人数:{totalPlayers}, 需要票数:{required}");
            
            return required;
        }

        private void CheckVoteResult()
        {
            if (!isVoteInProgress) return;

            int yesVotes = playerVotes.Count(v => v.Value);
            int totalVotes = playerVotes.Count;
            int requiredVotes = GetRequiredVotes();
            
            PrintToAllChat($"投票结束: {yesVotes}/{totalVotes}票同意交换阵营 (需要{requiredVotes}票同意)");
            
            // 只有达到所需票数才会交换阵营
            bool shouldSwap = yesVotes >= requiredVotes;
            
            PrintToAllChat($"投票结果: {(shouldSwap ? "通过" : "未通过")}，{(shouldSwap ? "将会" : "不会")}交换阵营");
            
            EndVote(shouldSwap);
        }

        private void EndVote(bool swapTeams)
        {
            if (!isVoteInProgress) return;
            isVoteInProgress = false;

            // 发送投票结果消息
            if (swapTeams)
            {
                UserMessage votePass = UserMessage.FromId(347); // CS_UM_VotePass = 347
                votePass.SetInt("team", -1);
                votePass.SetInt("vote_type", 2);
                votePass.SetString("disp_str", "#SFUI_Vote_SwapCamp_PWA");
                votePass.SetString("details_str", "#SFUI_Vote_SwapCamp_Passed_PWA");

                RecipientFilter allPlayersFilter = new RecipientFilter();
                allPlayersFilter.AddAllPlayers();
                votePass.Send(allPlayersFilter);

                // 添加一个短暂延迟，确保投票UI消失后再交换队伍
                AddTimer(0.5f, () => {
                    Server.ExecuteCommand("mp_swapteams;");
                    SwapSidesInTeamData(true);
                });
            }
            else
            {
                // 发送投票失败消息
                UserMessage voteFailed = UserMessage.FromId(348); // CS_UM_VoteFailed = 348
                voteFailed.SetInt("team", -1);
                voteFailed.SetInt("reason", 0); // 0 = Failed (未达到足够票数)
                
                RecipientFilter allPlayersFilter = new RecipientFilter();
                allPlayersFilter.AddAllPlayers();
                voteFailed.Send(allPlayersFilter);
            }

            // 清理投票数据
            playerVotes.Clear();

            // 添加一个短暂延迟，确保投票UI消失后再开始比赛
            AddTimer(1.0f, () => {
                // 开始比赛
                Server.ExecuteCommand("mp_match_restart_delay 15");
                StartLive();
            });
        }

        // [ConsoleCommand("matchzy_toggle_c4timer", "切换C4计时器开关状态")]
        public void ToggleC4TimerConsoleCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !IsPlayerAdmin(player))
            {
                PrintToPlayerChat(player, $"{chatPrefix} 你没有权限执行此命令");
                return;
            }
            
            ToggleC4Timer();
            
            if (player != null)
            {
                PrintToPlayerChat(player, $"{chatPrefix} C4计时器已{(showC4Timer ? "启用" : "禁用")}");
            }
            else
            {
                Log($"[C4Timer] C4计时器已通过控制台命令{(showC4Timer ? "启用" : "禁用")}");
            }
        }
        
        // 聊天命令处理
        private void OnToggleC4TimerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            
            ToggleC4Timer();
            
            PrintToPlayerChat(player, $"C4计时器已{(showC4Timer ? "启用" : "禁用")}");
            PrintToAllChat($"管理员 {ChatColors.Green}{player?.PlayerName}{ChatColors.Default} 已{(showC4Timer ? "启用" : "禁用")}C4计时器");
        }
        
        // 切换C4计时器状态
        private void ToggleC4Timer()
        {
            showC4Timer = !showC4Timer;
            
            // 更新FakeConVar值
            enableC4Timer.Value = showC4Timer;
            
            // 设置配置文件中的值
            Server.ExecuteCommand($"echo \"matchzy_enable_c4_timer {(showC4Timer ? "true" : "false")}\" >> MatchZy/config.cfg");
            
            // 强制刷新设置
            LoadC4TimerSettings();
            
            Log($"[C4Timer] C4计时器状态已更改为: {(showC4Timer ? "启用" : "禁用")}");
        }
    }
}
