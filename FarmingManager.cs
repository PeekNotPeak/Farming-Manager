using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using PRoCon.Core;
using PRoCon.Core.Players;
using PRoCon.Core.Plugin;

namespace PRoConEvents
{
    #region class FarmingManager

    public class FarmingManager : PRoConPluginAPI, IPRoConPluginInterface
    {
        #region Global Variables

        /* ===== Miscellaneous ===== */
        private const string StrPluginName = "Farming-Manager";
        private const string StrPluginVersion = "0.6.0";
        private const string StrPluginAuthor = "PeekNotPeak";
        private const string StrPluginWebsite = "github.com/PeekNotPeak/Farming-Manager";

        /* ===== 1. Farming-Manager ===== */
        private bool _boolIsPluginEnabled;
        private bool _boolDoPluginUpdateCheck;

        private const string StrPluginUpdateUrl =
            "https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/version.json";

        public List<string> LstCurrentReservedSlotPlayers;

        /* ===== 2. Global Settings ===== */
        public bool BoolSendInitialEnforcementMessage;
        public bool BoolUseAdKatsForPunishments;

        /* ===== 3. Weapon Enforcers ===== */
        private readonly Dictionary<string, WeaponEnforcer> _dictWeaponEnforcersLookup;
        private bool _boolCreateNewWeaponEnforcer;
        private int _intWeaponEnforcerDeletionId;
        private const string StrWeaponEnforcersSavePath = "Plugins/BF4/Farming-Manager_WeaponEnforcers.json";

        private Hashtable _hshTblHumanWeaponNames;

        private const string StrHumanWeaponNamesUrl =
            "https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/weapon_names.json";

        /* ===== 99. Debugging ===== */
        public readonly Logger Logger;

        #endregion Global Variables

        #region Constructor

        public FarmingManager()
        {
            /* ===== 1. FarmingManager ===== */
            _boolIsPluginEnabled = false;
            LstCurrentReservedSlotPlayers = new List<string>();

            /* ===== 2. Global Settings ===== */
            BoolSendInitialEnforcementMessage = true;
            BoolUseAdKatsForPunishments = false;

            /* ===== 3. Weapon Enforcers ===== */
            _dictWeaponEnforcersLookup = new Dictionary<string, WeaponEnforcer>();
            _boolCreateNewWeaponEnforcer = false;
            _intWeaponEnforcerDeletionId = 0;

            /* ===== 98. Plugin Update ===== */
            _boolDoPluginUpdateCheck = true;

            /* ===== 99. Debugging ===== */
            Logger = new Logger(this)
            {
                IntDebugLevel = 0, BoolDoDebugOutPut = false
            }; //Debug level is 0 by default and will not output any debug messages.
        }

        #endregion Constructor

        #region IPRoConPluginInterface

        public string GetPluginName()
        {
            return StrPluginName;
        }

        public string GetPluginVersion()
        {
            return StrPluginVersion;
        }

        public string GetPluginAuthor()
        {
            return StrPluginAuthor;
        }

        public string GetPluginWebsite()
        {
            return StrPluginWebsite;
        }

        public string GetPluginDescription()
        {
            return FarmingManagerUtilities.GetPluginDescription();
        }

        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            Logger.Debug(() => "Received OnPluginLoaded Event", 7);

            var events = new[]
            {
                /* Miscellaneous */
                "OnAccountLogin",

                /* Server Events */
                "OnServerInfo",

                /* Reserved Slots Events */
                "OnReservedSlotsList",

                /* Player Events */
                "OnPlayerKilled",
                "OnPlayerLeft",
                "OnPlayerDisconnected"
            };

            RegisterEvents(GetType().Name, events);

            LoadSavedWeaponEnforcers();

            Logger.Debug(() => "Exiting OnPluginLoaded Event", 7);
        }

        public void OnPluginEnable()
        {
            Logger.Debug(() => "Received OnPluginEnable Event", 7);

            Logger.Write($"Plugin enabled. Running on version {GetPluginVersion()}.");
            _hshTblHumanWeaponNames = FetchHumanizedWeaponNames();

            Logger.Debug(() => "Exiting OnPluginEnable Event", 7);
        }

        public void OnPluginDisable()
        {
            Logger.Debug(() => "Received OnPluginDisable Event", 7);

            Logger.Write("Plugin successfully shut down.");

            Logger.Debug(() => "Exiting OnPluginDisable Event", 7);
        }

        /* ==== Variable Handling ==== */

        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            return new List<CPluginVariable>(PluginVariables());
        }

        public List<CPluginVariable> GetPluginVariables()
        {
            return new List<CPluginVariable>(PluginVariables());
        }

        private IEnumerable<CPluginVariable> PluginVariables()
        {
            //Type safe plugin variable creation
            CPluginVariable StringPluginVariable(string name, string value)
            {
                return new CPluginVariable(name, typeof(string), value);
            }

            CPluginVariable StringArrayPluginVariable(string name, IEnumerable<string> value)
            {
                return new CPluginVariable(name, typeof(string[]), value);
            }

            CPluginVariable FloatPluginVariable(string name, float value)
            {
                return new CPluginVariable(name, typeof(string),
                    value.ToString("0.00", CultureInfo.InvariantCulture.NumberFormat));
            }

            CPluginVariable IntPluginVariable(string name, int value)
            {
                return new CPluginVariable(name, typeof(int), value);
            }

            CPluginVariable BoolPluginVariable(string name, bool value)
            {
                return new CPluginVariable(name, typeof(bool), value);
            }

            CPluginVariable BoolYesNoPluginVariable(string name, bool value)
            {
                return new CPluginVariable(name, typeof(enumBoolYesNo), value ? enumBoolYesNo.Yes : enumBoolYesNo.No);
            }

            /* ===== 1. Farming-Manager ===== */
            yield return BoolPluginVariable("1. Farming-Manager|Activate the plugin?", _boolIsPluginEnabled);
            yield return BoolPluginVariable("1. Farming-Manager|Check for plugin updates?", _boolDoPluginUpdateCheck);

            /* ===== 2. Global Settings ===== */
            yield return BoolYesNoPluginVariable("2. Global Settings|Send initial enforcement message?",
                BoolSendInitialEnforcementMessage);
            yield return BoolYesNoPluginVariable("2. Global Settings|Use AdKats for punishments?",
                BoolUseAdKatsForPunishments);

            /* ===== 3. Weapon Enforcers ===== */
            yield return BoolYesNoPluginVariable("3. Weapon Enforcers|Create new Weapon Enforcer?",
                _boolCreateNewWeaponEnforcer);
            yield return IntPluginVariable("3. Weapon Enforcers|Weapon Enforcer deletion ID",
                _intWeaponEnforcerDeletionId);

            /* ===== 4.x Weapon Enforcers ===== */
            foreach (var variable in _dictWeaponEnforcersLookup.Values.SelectMany(weaponEnforcer =>
                         weaponEnforcer.DisplayEnforcerVariables())) yield return variable;

            /* ===== 99. Debugging ===== */
            yield return BoolPluginVariable("99. Debugging|Enable debug output?", Logger.BoolDoDebugOutPut);
            if (Logger.BoolDoDebugOutPut)
                yield return IntPluginVariable("99. Debugging|Debug level", Logger.IntDebugLevel);
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            if (strVariable.Contains('|')) strVariable = strVariable.Substring(strVariable.IndexOf('|') + 1);

            try
            {
                switch (strVariable)
                {
                    /* ===== 1. Farming-Manager ===== */
                    case "Activate the plugin?":
                        _boolIsPluginEnabled = bool.Parse(strValue);
                        break;

                    case "Check for plugin updates?":
                        _boolDoPluginUpdateCheck = bool.Parse(strValue);
                        break;

                    /* ===== 2. Global Settings ===== */
                    case "Send initial enforcement message?":
                        BoolSendInitialEnforcementMessage = strValue == "Yes";
                        break;

                    case "Use AdKats for punishments?":
                        BoolUseAdKatsForPunishments = strValue == "Yes";
                        break;

                    /* ===== 3. Weapon Enforcers ===== */
                    case "Create new Weapon Enforcer?":
                        _boolCreateNewWeaponEnforcer = strValue == "Yes";
                        break;

                    case "Weapon Enforcer deletion ID":
                        _intWeaponEnforcerDeletionId = int.Parse(strValue);
                        break;

                    /* ===== 99. Debugging ===== */
                    case "Enable debug output?":
                        Logger.BoolDoDebugOutPut = bool.Parse(strValue);
                        break;

                    case "Debug level":
                        Logger.IntDebugLevel = int.Parse(strValue);
                        break;
                }

                /* ===== Weapon Enforcers ===== */

                var match = Regex.Match(strVariable, @"\#\[(.*?)\]");
                if (!match.Success) return;

                //Extract the Enforcer ID from the variable name
                var enforcerId = Regex.Match(strVariable, @"\d+(?=\])").Value;

                //Remove Enforcer ID and brackets from the variable name
                var rawVariableName = Regex.Replace(strVariable, @"\#\[(.*?)\]\s", string.Empty).Trim();

                //Set the variable in the Enforcer
                switch (rawVariableName)
                {
                    case "Enable Weapon Enforcer?":
                        _dictWeaponEnforcersLookup[enforcerId].EnforcerState =
                            (WeaponEnforcer.WeaponEnforcerState)Enum.Parse(typeof(WeaponEnforcer.WeaponEnforcerState),
                                CPluginVariable.Decode(strValue));
                        break;

                    case "Select punishment type":
                        _dictWeaponEnforcersLookup[enforcerId].Punishment =
                            (WeaponEnforcer.PunishmentTypes)Enum.Parse(typeof(WeaponEnforcer.PunishmentTypes),
                                CPluginVariable.Decode(strValue));
                        break;

                    case "Log to PRoCon Chat?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolLogToPRoConChat = strValue == "Yes";
                        break;

                    case "Send 70% and 90% warning messages?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolSendPercentWarningMessages = strValue == "Yes";
                        break;

                    case "Select monitored weapon":
                        _dictWeaponEnforcersLookup[enforcerId].StrCurrentlyMonitoredWeapons =
                            CPluginVariable.DecodeStringArray(strValue);
                        break;

                    case "Persist tracked players through rounds?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolPersistTrackedPlayersThroughRounds =
                            strValue == "Yes";
                        break;

                    case "Set maximum allowed KPM":
                        _dictWeaponEnforcersLookup[enforcerId].DoubleMaxAllowedKpm =
                            Convert.ToSingle(strValue.Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat);
                        break;

                    case "Set maximum allowed KDR":
                        _dictWeaponEnforcersLookup[enforcerId].DoubleMaxAllowedKdr =
                            Convert.ToSingle(strValue.Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat);
                        break;

                    case "Set minimum required kills":
                        _dictWeaponEnforcersLookup[enforcerId].IntMinRequiredKills = int.Parse(strValue);
                        break;

                    case "Set maximum warnings":
                        _dictWeaponEnforcersLookup[enforcerId].IntMaximumWarnings = int.Parse(strValue);
                        break;

                    case "Allow higher required kills for reserved slot players?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolAllowHigherTotalKillsForReservedSlotPlayers =
                            strValue == "Yes";
                        break;

                    case "Set minimum required kills for reserved slot players":
                        _dictWeaponEnforcersLookup[enforcerId].IntMinRequiredKillsForReservedSlotPlayers =
                            int.Parse(strValue);
                        break;

                    case "Set maximum warnings for reserved slot players":
                        _dictWeaponEnforcersLookup[enforcerId].IntMaximumWarningsReservedSlotPlayers =
                            int.Parse(strValue);
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Exception(e);
            }
            finally
            {
                ValidateParsedValues(strVariable, strValue);
            }
        }

        #endregion IPRoConPluginInterface

        #region PRoConPluginAPI

        public override void OnAccountLogin(string strSoldierName, string ip, CPrivileges privileges)
        {
            Logger.Debug(() => "Received OnAccountLogin Event", 7);

            if (_boolDoPluginUpdateCheck) CheckForPluginUpdate();

            Logger.Debug(() => "Exiting OnAccountLogin Event", 7);
        }

        public override void OnServerInfo(CServerInfo csiServerInfo)
        {
            Logger.Debug(() => "Received OnServerInfo Event", 7);

            if (_boolIsPluginEnabled) SaveCurrentWeaponEnforcers();

            Logger.Debug(() => "Exiting OnServerInfo Event", 7);
        }

        public override void OnReservedSlotsList(List<string> soldierNames)
        {
            Logger.Debug(() => "Received OnReservedSlotsList Event", 7);

            if (_boolIsPluginEnabled) LstCurrentReservedSlotPlayers = soldierNames;

            Logger.Debug(() => "Exiting OnReservedSlotsList Event", 7);
        }

        public override void OnPlayerKilled(Kill kKillerVictimDetails)
        {
            if (!_boolIsPluginEnabled) return;

            var damageType = kKillerVictimDetails.DamageType;
            var killerSoldierName = kKillerVictimDetails.Killer.SoldierName;
            var victimSoldierName = kKillerVictimDetails.Victim.SoldierName;

            // We don't want to count area or explosion damage nor collisions or suicides
            if (damageType == "DamageArea" ||
                damageType == "DamageExplosion" ||
                damageType == "SoldierCollision" ||
                damageType == "Suicide" ||
                killerSoldierName == victimSoldierName) return;

            //Make sure the player didn't crash or anything
            if (kKillerVictimDetails.Killer.SoldierName == string.Empty) return;

            Logger.Debug(() => "Received OnPlayerKilled Event", 7);

            //Humanize the weapon name
            var weaponName = GetDecodedWeaponName(damageType);

            Logger.Debug(
                () =>
                    $"Player '{killerSoldierName}' killed '{victimSoldierName}' with '{weaponName}'",
                4);

            // Find the first WeaponEnforcer that monitors the weapon used by the killer
            var weaponEnforcer =
                _dictWeaponEnforcersLookup.Values.First(we => we.StrCurrentlyMonitoredWeapons.Contains(weaponName));

            ThreadPool.QueueUserWorkItem(callBack =>
            {
                try
                {
                    weaponEnforcer.ProcessWeaponKill(kKillerVictimDetails);
                }
                catch (Exception e)
                {
                    Logger.Exception(e);
                }
            });

            Logger.Debug(() => "Exiting OnPlayerKilled Event", 7);
        }

        public override void OnPlayerLeft(CPlayerInfo playerInfo)
        {
            if (!_boolIsPluginEnabled) return;

            Logger.Debug(() => "Received OnPlayerLeft Event", 7);

            //Remove the player from the list of tracked players
            foreach (var weaponEnforcer in _dictWeaponEnforcersLookup.Values)
                weaponEnforcer.RemoveTrackedPlayer(playerInfo.SoldierName);

            Logger.Debug(() => "Exiting OnPlayerLeft Event", 7);
        }

        public override void OnPlayerDisconnected(string soldierName, string reason)
        {
            if (!_boolIsPluginEnabled) return;

            Logger.Debug(() => "Received OnPlayerDisconnected Event", 7);

            //Remove the player from the list of tracked players
            foreach (var weaponEnforcer in _dictWeaponEnforcersLookup.Values)
                weaponEnforcer.RemoveTrackedPlayer(soldierName);

            Logger.Debug(() => "Exiting OnPlayerDisconnected Event", 7);
        }

        public override void OnRoundOver(int winningTeamId)
        {
            if (!_boolIsPluginEnabled) return;

            Logger.Debug(() => "Received OnRoundOver Event", 7);

            //Reset all tracked players
            foreach (var weaponEnforcer in _dictWeaponEnforcersLookup.Values) weaponEnforcer.ResetTrackedPlayers();

            Logger.Debug(() => "Exiting OnRoundOver Event", 7);
        }

        #endregion PRoConPluginAPI

        #region Helper Methods

        private void ValidateParsedValues(string strVariable, string strValue)
        {
            try
            {
                switch (strVariable)
                {
                    /* ===== 2. Weapon Enforcers ===== */
                    case "Create new Weapon Enforcer?":
                        if (_boolCreateNewWeaponEnforcer)
                        {
                            CreateNewWeaponEnforcer();
                            _boolCreateNewWeaponEnforcer = false;
                        }

                        break;

                    case "Weapon Enforcer deletion ID":
                        DeleteWeaponEnforcerById(int.Parse(strValue));
                        _intWeaponEnforcerDeletionId = 0;
                        break;

                    /* ===== 99. Debugging ===== */
                    case "Debug level":
                        if (int.Parse(strValue) < 0 || int.Parse(strValue) > 10)
                        {
                            Logger.Warn($"Debug level must be between 0 and 10. Value '{strValue}' is invalid.");
                            Logger.IntDebugLevel = 0;
                        }
                        else
                        {
                            Logger.IntDebugLevel = int.Parse(strValue);
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Exception(e);
            }
        }

        private void CheckForPluginUpdate()
        {
            Logger.Debug(() => "Starting up CheckForPluginUpdate", 7);

            var latestVersion = "Unknown";

            var pluginUpdateThread = new Thread(() =>
            {
                using (var webClient = new WebClient())
                {
                    var response = webClient.DownloadString(StrPluginUpdateUrl);
                    var data = (Hashtable)JSON.JsonDecode(response);

                    if (data != null && data.ContainsKey("version")) latestVersion = data["version"].ToString();
                }

                if (!Regex.Match(latestVersion, GetPluginVersion()).Success)
                    Logger.Warn($"You are currently using version {GetPluginVersion()} of {GetPluginName()}. " +
                                $"Consider upgrading to the newest version ({latestVersion}) via GitHub.");
            })
            {
                IsBackground = true,
                Name = "PluginUpdateThread"
            };
            pluginUpdateThread.Start();

            Logger.Debug(() => "Exiting CheckForPluginUpdate", 7);
        }

        private string ClientDownloadTimer(GZipWebClient webClient, string url)
        {
            Logger.Debug(() => "Preparing to download from " + GetDomainName(url), 7);

            var timer = new Stopwatch();
            timer.Start();
            var returnString = webClient.GZipDownloadString(url);
            timer.Stop();

            Logger.Debug(() => "Downloaded from " + GetDomainName(url) + " in " + timer.ElapsedMilliseconds + "ms", 7);

            return returnString;
        }

        private static string GetDomainName(string url)
        {
            var domain = new Uri(url).DnsSafeHost.ToLower();

            var tokens = domain.Split('.');

            if (tokens.Length <= 2) return domain;

            //Add only second level exceptions to the < 3 rule here
            string[] exceptions = { "info", "firm", "name", "com", "biz", "gen", "ltd", "web", "net", "pro", "org" };

            var validTokens =
                2 + (tokens[tokens.Length - 2].Length < 3 || exceptions.Contains(tokens[tokens.Length - 2]) ? 1 : 0);

            domain = string.Join(".", tokens, tokens.Length - validTokens, validTokens);
            return domain;
        }

        public void LogToPRoConChat(string message)
        {
            message = Logger.ColorOrange(GetPluginName()) + " > " + message;
            ExecuteCommand("procon.protected.chat.write", message);
        }

        public void SendPlayerMessage(string soldierName, string message)
        {
            message = "[" + GetPluginName().ToUpper() + "] " + message;
            ExecuteCommand("procon.protected.send", "admin.say", message, "player", soldierName);
        }

        public void SendPlayerYell(string soldierName, string message, int duration)
        {
            message = "[" + GetPluginName().ToUpper() + "] " + message;
            ExecuteCommand("procon.protected.send", "admin.yell", message, duration.ToString(), "player", soldierName);
        }

        public void SendPlayerTell(string soldierName, string message, int duration = 5)
        {
            message = "[" + GetPluginName().ToUpper() + "] " + message;
            ExecuteCommand("procon.protected.send", "admin.yell", message, duration.ToString(), "player", soldierName);
            ExecuteCommand("procon.protected.send", "admin.say", message, "player", soldierName);
        }

        public void DoAdKatsPunishment(string playerName, WeaponEnforcer.PunishmentTypes punishmentType, string reason)
        {
            Logger.Debug(() => "Starting up DoAdKatsPunishment", 7);

            string commandType;

            switch (punishmentType)
            {
                case WeaponEnforcer.PunishmentTypes.Punish:
                    commandType = "player_punish";
                    break;

                case WeaponEnforcer.PunishmentTypes.Kill:
                    commandType = "player_kill";
                    break;

                case WeaponEnforcer.PunishmentTypes.Kick:
                    commandType = "player_kick";
                    break;

                case WeaponEnforcer.PunishmentTypes.TempBan:
                    commandType = "player_ban_temp";
                    break;

                case WeaponEnforcer.PunishmentTypes.PermanentBan:
                    commandType = "player_ban";
                    break;

                default:
                    return;
            }

            ThreadPool.QueueUserWorkItem(callBack =>
            {
                try
                {
                    Thread.Sleep(2000);

                    var requestHashtable = new Hashtable
                    {
                        { "caller_identity", GetType().Name },
                        { "response_requested", false },
                        { "command_type", commandType },
                        { "source_name", GetType().Name },
                        { "target_name", playerName },
                        { "record_message", reason }
                    };
                    if (punishmentType == WeaponEnforcer.PunishmentTypes.TempBan)
                        requestHashtable.Add("command_numeric", 60); //1 hour or 60 minutes

                    ExecuteCommand("procon.protected.plugins.call", "AdKats", "IssueCommand", GetType().Name,
                        JSON.JsonEncode(requestHashtable));

                    Logger.Debug(
                        () => $"Issued AdKats punishment [{punishmentType.ToString()}] to {playerName} for: {reason}",
                        3);
                }
                catch (Exception e)
                {
                    Logger.Exception(e);
                }
            });

            Logger.Debug(() => "Exiting DoAdKatsPunishment", 7);
        }

        public void DoPRoConPunishment(string playerName, WeaponEnforcer.PunishmentTypes punishmentType, string reason)
        {
            string commandType;

            switch (punishmentType)
            {
                case WeaponEnforcer.PunishmentTypes.Punish:
                    commandType = "admin.punishPlayer";
                    break;

                case WeaponEnforcer.PunishmentTypes.Kill:
                    commandType = "admin.killPlayer";
                    break;

                case WeaponEnforcer.PunishmentTypes.Kick:
                    commandType = "admin.kickPlayer";
                    break;

                case WeaponEnforcer.PunishmentTypes.TempBan:
                    commandType = "admin.tempBanPlayer";
                    break;

                case WeaponEnforcer.PunishmentTypes.PermanentBan:
                    commandType = "admin.banPlayer";
                    break;

                default:
                    return;
            }

            ThreadPool.QueueUserWorkItem(callBack =>
            {
                try
                {
                    Thread.Sleep(2000); //3 Seconds

                    if (punishmentType != WeaponEnforcer.PunishmentTypes.Punish ||
                        punishmentType != WeaponEnforcer.PunishmentTypes.TempBan ||
                        punishmentType != WeaponEnforcer.PunishmentTypes.PermanentBan)
                    {
                        ExecuteCommand("procon.protected.send", commandType, playerName, reason);
                    }
                    else
                    {
                        const int time = 60 * 60; //1 Hour or 60 minutes in seconds

                        //Add the player to the ban list either with a time or permanent
                        if (commandType == "admin.tempBanPlayer")
                            ExecuteCommand("procon.protected.send", "banList.add", "name", playerName, "seconds",
                                time.ToString(CultureInfo.InvariantCulture), reason);
                        else
                            ExecuteCommand("procon.protected.send", "banList.add", "name", playerName, "perm", reason);

                        //Save the ban list and kick him
                        ExecuteCommand("procon.protected.send", "banList.save");
                        ExecuteCommand("procon.protected.send", "admin.KickPlayer", playerName, reason);
                    }

                    Logger.Debug(
                        () => $"Issued PRoCon punishment [{punishmentType.ToString()}] to {playerName} for: {reason}",
                        3);
                }
                catch (Exception e)
                {
                    Logger.Exception(e);
                }
            });
        }

        #endregion

        #region Weapon Enforcers Helper Methods

        private void CreateNewWeaponEnforcer()
        {
            Logger.Debug(() => "Starting up CreateNewWeaponEnforcer", 7);

            var id = GetNextWeaponEnforcerId();
            _dictWeaponEnforcersLookup.Add(id, new WeaponEnforcer(this, id));
            Logger.Debug(() => $"Created new Weapon Enforcer with ID {id}", 2);

            Logger.Debug(() => "Exiting CreateNewWeaponEnforcer", 7);
        }

        private List<int> GetSortedWeaponEnforcerIds()
        {
            var lookup = _dictWeaponEnforcersLookup.Keys.ToDictionary(int.Parse,
                weaponEnforcerId => _dictWeaponEnforcersLookup[weaponEnforcerId]);

            var sortedWeaponEnforcerIds = lookup.Keys.ToList();
            sortedWeaponEnforcerIds.Sort((a, b) => a.CompareTo(b));

            return sortedWeaponEnforcerIds;
        }

        private string GetNextWeaponEnforcerId()
        {
            if (_dictWeaponEnforcersLookup.Count == 0) return "1";

            var sortedWeaponEnforcerIds = GetSortedWeaponEnforcerIds();

            if (sortedWeaponEnforcerIds.Count == sortedWeaponEnforcerIds[sortedWeaponEnforcerIds.Count - 1])
                return (sortedWeaponEnforcerIds.Count + 1).ToString();

            var i = 1;
            for (; i <= sortedWeaponEnforcerIds.Count; i++)
                if (sortedWeaponEnforcerIds[i - 1] != i)
                    break;

            return i.ToString();
        }

        private void DeleteWeaponEnforcerById(int handlerId)
        {
            Logger.Debug(() => "Starting up DeleteWeaponEnforcerById", 7);

            if (handlerId == 0 || !_dictWeaponEnforcersLookup.ContainsKey(handlerId.ToString())) return;

            _dictWeaponEnforcersLookup[handlerId.ToString()] = null;
            _dictWeaponEnforcersLookup.Remove(handlerId.ToString());
            GC.Collect();
            Logger.Debug(() => $"Deleted Weapon Enforcer with ID {handlerId}", 2);

            Logger.Debug(() => "Exiting DeleteWeaponEnforcerById", 7);
        }

        private Hashtable FetchHumanizedWeaponNames()
        {
            Logger.Debug(() => "Starting up FetchHumanizedWeaponNames", 7);

            Hashtable humanizedWeaponNames;

            using (var client = new GZipWebClient(compress: false))
            {
                string downloadString;
                try
                {
                    downloadString = ClientDownloadTimer(client,
                        StrHumanWeaponNamesUrl + "?cacherand=" + Environment.TickCount);
                    Logger.Debug(() => "Weapon names downloaded successfully", 1);
                }
                catch (Exception e)
                {
                    Logger.Exception(e);
                    return null;
                }

                humanizedWeaponNames = (Hashtable)JSON.JsonDecode(downloadString);
            }

            Logger.Debug(() => "Exiting FetchHumanizedWeaponNames", 7);

            return humanizedWeaponNames;
        }

        public string GetDecodedWeaponName(string engineWeaponName, bool longName = true)
        {
            var readableWeaponNames = (Hashtable)_hshTblHumanWeaponNames[engineWeaponName];
            var playerWeapon = string.Empty;
            var weaponKey = longName ? "readable_long" : "readable_short";

            foreach (var weaponName in readableWeaponNames.Cast<DictionaryEntry>()
                         .Where(weaponName => weaponName.Key.ToString() == weaponKey))
                playerWeapon = weaponName.Value.ToString();

            return playerWeapon;
        }

        private void SaveCurrentWeaponEnforcers()
        {
            Logger.Debug(() => "Starting up SaveCurrentWeaponEnforcers", 7);

            if (_dictWeaponEnforcersLookup.Count >= 0) return;
            //TODO: Add saving of current weapon enforcers

            Logger.Debug(() => "Exiting SaveCurrentWeaponEnforcers", 7);
        }

        private void LoadSavedWeaponEnforcers()
        {
            Logger.Debug(() => "Starting up LoadSavedWeaponEnforcers", 7);

            if (!File.Exists(StrWeaponEnforcersSavePath)) return;
            //TODO: Add loading of saved weapon enforcers

            Logger.Debug(() => "Exiting LoadSavedWeaponEnforcers", 7);
        }

        #endregion Weapon Enforcers Helper Methods
    }

    #endregion class FarmingManager

    #region class WeaponEnforcer

    public class WeaponEnforcer
    {
        private readonly FarmingManager _plugin;
        private readonly string _strEnforcerId;

        public enum WeaponEnforcerState
        {
            Disabled,
            Enabled,
            Virtual
        }

        public enum PunishmentTypes
        {
            Punish,
            Kill,
            Kick,
            TempBan,
            PermanentBan
        }

        public WeaponEnforcerState EnforcerState;
        public PunishmentTypes Punishment;
        public bool BoolLogToPRoConChat;
        public bool BoolSendPercentWarningMessages;
        public string[] StrCurrentlyMonitoredWeapons;
        public bool BoolPersistTrackedPlayersThroughRounds;
        public int IntMinRequiredKills;
        public int IntMaximumWarnings;
        public bool BoolAllowHigherTotalKillsForReservedSlotPlayers;
        public int IntMinRequiredKillsForReservedSlotPlayers;
        public int IntMaximumWarningsReservedSlotPlayers;
        private readonly Dictionary<string, int> _dictTotalPlayerWarnings;
        public double DoubleMaxAllowedKdr;
        public double DoubleMaxAllowedKpm;

        private readonly Dictionary<string, List<Dictionary<string, int>>> _dictTrackedPlayers;

        public WeaponEnforcer(FarmingManager plugin, string enforcerId)
        {
            _plugin = plugin;
            _strEnforcerId = enforcerId;

            EnforcerState = WeaponEnforcerState.Disabled;
            BoolLogToPRoConChat = true;
            BoolSendPercentWarningMessages = true;
            StrCurrentlyMonitoredWeapons = new[]
                { "AH-1Z Viper Attack Helicopter", "Type 99 MBT", "M1 Abrams MBT", "LAV-25 APC" };
            BoolPersistTrackedPlayersThroughRounds = true;
            IntMinRequiredKills = 35;
            IntMaximumWarnings = 10;
            BoolAllowHigherTotalKillsForReservedSlotPlayers = false;
            IntMinRequiredKillsForReservedSlotPlayers = 40;
            IntMaximumWarningsReservedSlotPlayers = 15;
            _dictTotalPlayerWarnings = new Dictionary<string, int>();
            DoubleMaxAllowedKdr = 6.50;
            DoubleMaxAllowedKpm = 2.50;

            _dictTrackedPlayers = new Dictionary<string, List<Dictionary<string, int>>>();
        }

        public IEnumerable<CPluginVariable> DisplayEnforcerVariables()
        {
            //Type safe plugin variable creation
            CPluginVariable StringPluginVariable(string name, string value)
            {
                return new CPluginVariable(name, typeof(string), value);
            }

            CPluginVariable StringArrayPluginVariable(string name, IEnumerable<string> value)
            {
                return new CPluginVariable(name, typeof(string[]), value);
            }

            CPluginVariable DoublePluginVariable(string name, double value)
            {
                return new CPluginVariable(name, typeof(string),
                    value.ToString("0.00", CultureInfo.InvariantCulture.NumberFormat));
            }

            CPluginVariable IntPluginVariable(string name, int value)
            {
                return new CPluginVariable(name, typeof(int), value);
            }

            CPluginVariable BoolPluginVariable(string name, bool value)
            {
                return new CPluginVariable(name, typeof(bool), value);
            }

            CPluginVariable BoolYesNoPluginVariable(string name, bool value)
            {
                return new CPluginVariable(name, typeof(enumBoolYesNo), value ? enumBoolYesNo.Yes : enumBoolYesNo.No);
            }

            var enforcerVariables = new List<CPluginVariable>
            {
                new CPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Enable Weapon Enforcer?",
                    FarmingManagerUtilities.CreateEnumString<WeaponEnforcerState>(), EnforcerState.ToString()),
                new CPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Select punishment type",
                    FarmingManagerUtilities.CreateEnumString<PunishmentTypes>(), Punishment.ToString()),
                BoolYesNoPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Log to PRoCon Chat?",
                    BoolLogToPRoConChat),
                BoolYesNoPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Send 70% and 90% warning messages?",
                    BoolSendPercentWarningMessages),
                StringArrayPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Select monitored weapon",
                    StrCurrentlyMonitoredWeapons),
                BoolYesNoPluginVariable(
                    GetFullName() + $"|#[4.{_strEnforcerId}] Persist tracked players through rounds?",
                    BoolPersistTrackedPlayersThroughRounds),
                DoublePluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum allowed KDR",
                    DoubleMaxAllowedKdr),
                DoublePluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum allowed KPM",
                    DoubleMaxAllowedKpm),
                IntPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set minimum required kills",
                    IntMinRequiredKills),
                IntPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum warnings",
                    IntMaximumWarnings),
                BoolYesNoPluginVariable(
                    GetFullName() + $"|#[4.{_strEnforcerId}] Allow higher required kills for reserved slot players?",
                    BoolAllowHigherTotalKillsForReservedSlotPlayers)
            };
            if (!BoolAllowHigherTotalKillsForReservedSlotPlayers) return enforcerVariables;

            enforcerVariables.Add(IntPluginVariable(
                GetFullName() + $"|#[4.{_strEnforcerId}] Set minimum required kills for reserved slot players",
                IntMinRequiredKillsForReservedSlotPlayers));
            enforcerVariables.Add(IntPluginVariable(
                GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum warnings for reserved slot players",
                IntMaximumWarnings));

            return enforcerVariables;
        }

        public void ProcessWeaponKill(Kill kKillerVictimDetails)
        {
            if (EnforcerState == WeaponEnforcerState.Disabled) return;

            if (!CurrentTimeWithinAllowedTimeFrameCheck()) return;

            _plugin.Logger.Debug(() => $"Starting up ProcessWeaponKill for Enforcer #{_strEnforcerId}", 7);

            var soldierName = kKillerVictimDetails.Killer.SoldierName;
            var playerWeapon = _plugin.GetDecodedWeaponName(kKillerVictimDetails.DamageType);

            if (EnforcerState != WeaponEnforcerState.Virtual)
                switch (true)
                {
                    //Soldier hasn't been tracked yet so we need to start the initial check
                    case true when !_dictTrackedPlayers.ContainsKey(kKillerVictimDetails.Killer.SoldierName):
                        StartInitialCheck(soldierName, playerWeapon);
                        break;

                    //Soldier has been tracked but the weapon they're using hasn't been tracked yet
                    case true when !_dictTrackedPlayers[soldierName].Any(x => x.ContainsKey(playerWeapon)):
                        AddTrackedWeaponToSoldier(soldierName, playerWeapon);
                        break;

                    //Soldier has been tracked and the weapon they're using has been tracked
                    //So we just need to increment the weapon's usage count
                    //Lastly go through the tracked player again and check if they exceeded some limit
                    default:
                        IncrementCountOnTrackedWeapon(soldierName, playerWeapon);
                        LastCheckUpOnPlayer(kKillerVictimDetails.Killer, playerWeapon);
                        break;
                }
            else
                _plugin.Logger.Warn(
                    $"Enforcer #{_strEnforcerId}: Currently in virtual mode, not executing ProcessWeaponKill for '{soldierName}'");

            _plugin.Logger.Debug(() => $"Exiting ProcessWeaponKill for Enforcer #{_strEnforcerId}", 7);
        }

        private bool CurrentTimeWithinAllowedTimeFrameCheck()
        {
            _plugin.Logger.Debug(() => "Starting up CurrentTimeWithinAllowedTimeFrameCheck", 7);

            //TODO: Add time based enforcement
            var currentTime = DateTime.Now;
            _plugin.Logger.Warn("SIMULATING TIME LOGIC - CURRENT TIME: " + currentTime);

            _plugin.Logger.Debug(() => "Exiting CurrentTimeWithinAllowedTimeFrameCheck", 7);

            return true;
        }

        private void StartInitialCheck(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up StartInitialCheck", 7);

            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                AddFirstTrackingEntry(soldierName, playerWeapon);

                if (BoolLogToPRoConChat)
                    _plugin.LogToPRoConChat(
                        $"Enforcer #{_strEnforcerId}: Now monitoring '{soldierName}' for weapon '{playerWeapon}'");

                if (_plugin.BoolSendInitialEnforcementMessage)
                    _plugin.SendPlayerTell(soldierName,
                        $"You are now being monitored by Enforcer #{_strEnforcerId} for weapon {playerWeapon}", 10);

                _plugin.Logger.Debug(
                    () =>
                        $"Enforcer #{_strEnforcerId}: Now monitoring '{soldierName}' for weapon '{playerWeapon}'",
                    3);
            }
            else
            {
                _plugin.Logger.Warn(
                    $"Enforcer #{_strEnforcerId}: Currently in virtual mode, not executing InitialCheck for '{soldierName}'");
            }

            _plugin.Logger.Debug(() => "Exiting StartInitialCheck", 7);
        }

        private void AddTrackedWeaponToSoldier(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up AddTrackedWeaponToSoldier", 7);

            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                _dictTrackedPlayers[soldierName].Add(new Dictionary<string, int>
                {
                    { playerWeapon, 1 }
                });

                if (BoolLogToPRoConChat)
                    _plugin.LogToPRoConChat(
                        $"Enforcer #{_strEnforcerId}: Added weapon '{playerWeapon}' to tracked weapons for player '{soldierName}'");

                _plugin.Logger.Debug(
                    () =>
                        $"Enforcer #{_strEnforcerId}: Added weapon '{playerWeapon}' to tracked weapons for player '{soldierName}'",
                    3);
            }
            else
            {
                _plugin.Logger.Warn(
                    $"Enforcer #{_strEnforcerId}: Currently in virtual mode, not executing AddTrackedWeaponToSoldier for '{soldierName}'");
            }

            _plugin.Logger.Debug(() => "Exiting AddTrackedWeaponToSoldier", 7);
        }

        private void IncrementCountOnTrackedWeapon(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up IncrementCountOnTrackedWeapon", 7);

            _dictTrackedPlayers[soldierName].First(x => x.ContainsKey(playerWeapon))[playerWeapon]++;
            var weaponUsageCount =
                _dictTrackedPlayers[soldierName].First(x => x.ContainsKey(playerWeapon))[playerWeapon];
            _plugin.Logger.Debug(
                () =>
                    $"Enforcer #{_strEnforcerId}: Counted up on {soldierName} with {playerWeapon} to {weaponUsageCount}",
                3);

            _plugin.Logger.Debug(() => "Exiting IncrementCountOnTrackedWeapon", 7);
        }

        private void LastCheckUpOnPlayer(CPlayerInfo player, string weapon)
        {
            _plugin.Logger.Debug(() => "Starting up LastCheckUpOnPlayer", 7);

            var minRequiredKills = GetMinimumRequiredKillsForPlayer(player.SoldierName);
            var weaponUsageCount = _dictTrackedPlayers[player.SoldierName]
                .First(x => x.ContainsKey(weapon))[weapon];

            //Compute the 25% of the minimum required kills and cast it to int
            var minRequiredKills70Percent = (int)Math.Round(minRequiredKills * 0.70);
            var minRequiredKills90Percent = (int)Math.Round(minRequiredKills * 0.90);

            switch (true)
            {
                //Notify the player once they reach 70% of the minimum required kills with the specific weapon
                case true when weaponUsageCount == minRequiredKills70Percent:
                    DoPercentNotification(player.SoldierName, weapon, "70%");
                    return;

                //Notify the player once they reach 90% of the minimum required kills with the specific weapon
                case true when weaponUsageCount == minRequiredKills90Percent:
                    DoPercentNotification(player.SoldierName, weapon, "90%");
                    return;

                //Notify the player once they reach 100% of the minimum required kills with the specific weapon
                case true when weaponUsageCount == minRequiredKills:
                    DoPercentNotification(player.SoldierName, weapon, "100%");
                    return;

                //At this point the player is beyond the minimum required kills
                case true when weaponUsageCount > minRequiredKills:
                    HandleEnforcementPunishment(player, weapon);
                    return;
            }

            _plugin.Logger.Debug(() => "Exiting LastCheckUpOnPlayer", 7);
        }

        private void HandleEnforcementPunishment(CPlayerInfo player, string weapon)
        {
            _plugin.Logger.Debug(() => "Starting up HandleEnforcementPunishment", 7);

            var maximumWarnings = GetMaximumWarningsForPlayer(player.SoldierName);

            AddOrIncrementPlayerWarnings(player.SoldierName);

            var currentWarnings = _dictTotalPlayerWarnings[player.SoldierName];

            var playerKdr = Math.Round(player.Kdr, 2);
            var maxAllowedKdr = Math.Round(DoubleMaxAllowedKdr, 2);

            var infoMessage = string.Empty;

            switch (true)
            {
                case true when currentWarnings == 1 && playerKdr >= maxAllowedKdr:
                    infoMessage =
                        $"Your current KDR is too high [{playerKdr}/{maxAllowedKdr}] | This is the first warning!";
                    break;

                case true when currentWarnings < maximumWarnings - 1 && playerKdr >= maxAllowedKdr:
                    infoMessage =
                        $"Your current KDR is too high [{playerKdr}/{maxAllowedKdr}] | " +
                        $"Warning [{currentWarnings}/{maximumWarnings}]";
                    break;

                case true when currentWarnings == maximumWarnings - 3 && playerKdr >= maxAllowedKdr:
                    infoMessage =
                        $"Your current KDR is too high [{playerKdr}/{maxAllowedKdr}] " +
                        "Please change your play-style or use a different weapon | " +
                        $"Warning [{currentWarnings}/{maximumWarnings}]";
                    break;

                case true when currentWarnings == maximumWarnings - 1 && playerKdr >= maxAllowedKdr:
                    infoMessage =
                        $"Your current KDR is too high [{playerKdr}/{maxAllowedKdr}] | " +
                        "THIS IS THE LAST WARNING BEFORE BEING PUNISHED!";
                    break;

                //When we're here just punish em
                case true when currentWarnings >= maximumWarnings && playerKdr >= maxAllowedKdr:
                    var reason = infoMessage = $"Exceeded KDR Limit with '{weapon}' [{playerKdr}/{maxAllowedKdr}]";
                    InitializePunishment(player, reason);
                    break;
            }

            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                if (infoMessage != string.Empty) _plugin.SendPlayerTell(player.SoldierName, infoMessage, 15);

                if (BoolLogToPRoConChat)
                {
                    var message = $"Enforcer #{_strEnforcerId}:" + player.SoldierName + " > " + infoMessage;
                    _plugin.LogToPRoConChat(message);
                }
            }
            else
            {
                _plugin.Logger.Warn(
                    $"Enforcer #{_strEnforcerId}: Currently in virtual mode, not executing HandleEnforcementPunishment for '{player.SoldierName}'");
            }

            _plugin.Logger.Debug(() => "Exiting HandleEnforcementPunishment", 7);
        }

        private void InitializePunishment(CPlayerInfo player, string reason)
        {
            _plugin.Logger.Debug(() => "Starting up InitializePunishment", 7);

            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                if (_plugin.BoolUseAdKatsForPunishments)
                    _plugin.DoAdKatsPunishment(player.SoldierName, Punishment, reason);
                else
                    _plugin.DoPRoConPunishment(player.SoldierName, Punishment, reason);

                if (BoolLogToPRoConChat)
                    _plugin.LogToPRoConChat(
                        $"Enforcer #{_strEnforcerId}: Issued punishment [{Punishment.ToString()}] to {player.SoldierName} for: {reason}");
            }
            else
            {
                _plugin.Logger.Warn(
                    $"Enforcer #{_strEnforcerId}: Currently in virtual mode, not executing InitializePunishment for '{player.SoldierName}'");
            }

            _plugin.Logger.Debug(() => "Exiting InitializePunishment", 7);
        }

        private void DoPercentNotification(string soldierName, string playerWeapon, string percent)
        {
            var minRequiredKills = GetMinimumRequiredKillsForPlayer(soldierName);
            var weaponUsageCount = _dictTrackedPlayers[soldierName]
                .First(x => x.ContainsKey(playerWeapon))[playerWeapon];

            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                if (BoolSendPercentWarningMessages)
                {
                    var message =
                        $"You have reached {percent} [{weaponUsageCount}/{minRequiredKills}] of minimum required kills for weapon {playerWeapon} to be punished";

                    if (percent == "100%")
                        message =
                            $"You have exceeded the minimum required kills with {playerWeapon}. Further kills with this weapon will result in a punishment";


                    _plugin.SendPlayerYell(soldierName, message, 10);
                    _plugin.SendPlayerMessage(soldierName, message);

                    if (BoolLogToPRoConChat)
                        _plugin.LogToPRoConChat(
                            $"Enforcer #{_strEnforcerId}: Sent {percent} [{weaponUsageCount}/{minRequiredKills}] " +
                            $"warning message to '{soldierName}' for weapon '{playerWeapon}'");
                }
                else
                {
                    _plugin.Logger.Warn($"Enforcer #{_strEnforcerId}: BoolSendPercentWarningMessages is turned off. " +
                                        $"Player '{soldierName}' will not receive {percent} warnings.");
                }
            }
            else
            {
                _plugin.Logger.Warn(
                    $"Enforcer #{_strEnforcerId}: Currently in virtual mode, not executing DoPercentNotification for '{soldierName}'");
            }
        }


        private void AddFirstTrackingEntry(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up AddFirstTrackingEntry", 7);

            _dictTrackedPlayers.Add(soldierName, new List<Dictionary<string, int>>
            {
                new Dictionary<string, int>
                {
                    { playerWeapon, 1 }
                }
            });
            _plugin.Logger.Debug(
                () => $"Enforcer #{_strEnforcerId}: Added new player '{soldierName}' with weapon '{playerWeapon}'", 3);

            _plugin.Logger.Debug(() => "Exiting AddFirstTrackingEntry", 7);
        }

        public void RemoveTrackedPlayer(string soldierName)
        {
            _plugin.Logger.Debug(() => "Starting up RemoveTrackedPlayer", 7);

            switch (true)
            {
                case true when _dictTrackedPlayers.ContainsKey(soldierName):
                    _dictTrackedPlayers.Remove(soldierName);
                    _plugin.Logger.Debug(
                        () => $"Enforcer #{_strEnforcerId}: Removed player '{soldierName}' from tracked players", 3);
                    break;

                case true when _dictTotalPlayerWarnings.ContainsKey(soldierName):
                    _dictTotalPlayerWarnings.Remove(soldierName);
                    _plugin.Logger.Debug(
                        () => $"Enforcer #{_strEnforcerId}: Removed player '{soldierName}' from total warnings", 3);
                    break;
            }

            _plugin.Logger.Debug(() => "Exiting RemoveTrackedPlayer", 7);
        }

        public void ResetTrackedPlayers()
        {
            _plugin.Logger.Debug(() => "Starting up ResetTrackedPlayer", 7);

            if (!BoolPersistTrackedPlayersThroughRounds)
            {
                _dictTrackedPlayers.Clear();
                _dictTotalPlayerWarnings.Clear();
                _plugin.Logger.Debug(() => $"Enforcer #{_strEnforcerId}: Reset all tracked players and warnings", 3);
            }

            _plugin.Logger.Debug(() => "Exiting ResetTrackedPlayer", 7);
        }

        private void AddOrIncrementPlayerWarnings(string soldierName)
        {
            _plugin.Logger.Debug(() => "Starting up AddOrIncrementPlayerWarnings", 7);

            //If its the first time the player has exceeded the minimum required kills start the warning counter
            if (!_dictTotalPlayerWarnings.ContainsKey(soldierName)) _dictTotalPlayerWarnings.Add(soldierName, 1);

            //If the player has already exceeded the minimum required kills, increment the warning counter
            else _dictTotalPlayerWarnings[soldierName] += 1;

            _plugin.Logger.Debug(
                () =>
                    $"Enforcer #{_strEnforcerId}: Counted up total warnings on '{soldierName}' to {_dictTotalPlayerWarnings[soldierName]}",
                3);

            _plugin.Logger.Debug(() => "Exiting AddOrIncrementPlayerWarnings", 7);
        }

        private int GetMinimumRequiredKillsForPlayer(string soldierName)
        {
            _plugin.Logger.Debug(() => "Starting up GetMinimumRequiredKillsForPlayer", 7);

            var minRequiredKills = IntMinRequiredKills;
            if (BoolAllowHigherTotalKillsForReservedSlotPlayers &&
                _plugin.LstCurrentReservedSlotPlayers.Contains(soldierName))
                minRequiredKills = IntMinRequiredKillsForReservedSlotPlayers;

            _plugin.Logger.Debug(
                () =>
                    $"Enforcer #{_strEnforcerId}: Minimum required kills for player '{soldierName}' are {minRequiredKills}",
                3);

            _plugin.Logger.Debug(() => "Exiting GetMinimumRequiredKillsForPlayer", 7);

            return minRequiredKills;
        }

        private int GetMaximumWarningsForPlayer(string soldierName)
        {
            _plugin.Logger.Debug(() => "Starting up GetMaximumWarningsForPlayer", 7);

            var maximumWarnings = IntMaximumWarnings;
            if (BoolAllowHigherTotalKillsForReservedSlotPlayers &&
                _plugin.LstCurrentReservedSlotPlayers.Contains(soldierName))
                maximumWarnings = IntMaximumWarningsReservedSlotPlayers;

            _plugin.Logger.Debug(
                () => $"Enforcer #{_strEnforcerId}: Maximum warnings for player '{soldierName}' are {maximumWarnings}",
                3);

            _plugin.Logger.Debug(() => "Exiting GetMaximumWarningsForPlayer", 7);

            return maximumWarnings;
        }

        private string GetFullName()
        {
            return $"4.{_strEnforcerId} Weapon Enforcer with ID #{_strEnforcerId}";
        }
    }

    #endregion

    #region class FarmingManagerUtilities

    public static class FarmingManagerUtilities
    {
        private static string CreateEnumString(string name, string[] valueArray)
        {
            //return string.Format("enum.{0}_{1}({2})", "ProconEvents.FarmingManager", name, string.Join("|", valueArray));
            return $"enum.ProconEvents.FarmingManager_{name}({string.Join("|", valueArray)})";
        }

        internal static string CreateEnumString<T>()
        {
            return CreateEnumString(typeof(T).Name, Enum.GetNames(typeof(T)));
        }

        public static string PrepareSafely(this string data, bool prepare)
        {
            return prepare ? CPluginVariable.Encode(data) : data;
        }

        public static string[] PrepareSafely(this IEnumerable<string> data, bool prepare)
        {
            if (data == null) data = Array.Empty<string>();
            return prepare ? data.Select(CPluginVariable.Encode).ToArray() : data as string[] ?? data.ToArray();
        }

        public static string GetPluginDescription()
        {
            return @"This plugin is designed to help you manage farming players on your server.
                    It will automatically detect and act upon players who are farming.";
        }
    }

    #endregion class FarmingManagerUtilities

    #region class Logger

    public class Logger
    {
        private readonly FarmingManager _plugin;
        public int IntDebugLevel { get; set; }
        public bool BoolDoDebugOutPut { get; set; }

        private enum DebugMessageType
        {
            Verbose = 0,
            Info = 1,
            Warning = 2,
            Error = 3,
            Exception = 4,
            Debug = 5
        }

        public Logger(FarmingManager plugin)
        {
            _plugin = plugin;
        }

        private string FormatMessage(string message, DebugMessageType type)
        {
            var prefix = "[^b" + _plugin.GetPluginName() + "^n] ";
            switch (type)
            {
                case DebugMessageType.Verbose:
                    prefix += ColorGray("[VERBOSE] ");
                    break;
                case DebugMessageType.Info:
                    prefix += ColorGray("[INFO] ");
                    break;
                case DebugMessageType.Warning:
                    prefix += ColorOrange("[WARNING] ");
                    break;
                case DebugMessageType.Error:
                    prefix += ColorRed("[ERROR] ");
                    break;
                case DebugMessageType.Exception:
                    prefix += ColorRed("[EXCEPTION] ");
                    break;
                case DebugMessageType.Debug:
                    prefix += ColorLightBlue("[DEBUG] ");
                    break;
                default:
                    return prefix;
            }

            return prefix + message;
        }

        private void ConsoleWrite(string message)
        {
            _plugin.ExecuteCommand("procon.protected.pluginconsole.write", message);
        }

        private void ConsoleWrite(string message, DebugMessageType type)
        {
            ConsoleWrite(FormatMessage(message, type));
        }

        public void Write(string message)
        {
            ConsoleWrite(message, DebugMessageType.Info);
        }

        public void Warn(string message)
        {
            ConsoleWrite(message, DebugMessageType.Warning);
        }

        public void Error(string message)
        {
            ConsoleWrite(message, DebugMessageType.Error);
        }

        public void Exception(Exception e)
        {
            ConsoleWrite(e.ToString(), DebugMessageType.Exception);
        }

        public void Debug(Func<string> messageFunction, int level)
        {
            try
            {
                if (!BoolDoDebugOutPut || IntDebugLevel < level) return;

                if (IntDebugLevel >= 8)
                    ConsoleWrite("[" + level + "-" + new StackFrame(1).GetMethod().Name + "-" +
                                 (string.IsNullOrEmpty(Thread.CurrentThread.Name)
                                     ? "Main"
                                     : Thread.CurrentThread.Name) + "-" + Thread.CurrentThread.ManagedThreadId + "] " +
                                 messageFunction(), DebugMessageType.Debug);

                else ConsoleWrite(messageFunction(), DebugMessageType.Debug);
            }
            catch (Exception e)
            {
                Exception(e);
            }
        }

        public string FontClear(string message)
        {
            return message
                .Replace("^b", "")
                .Replace("^n", "")
                .Replace("^i", "")
                .Replace("^0", "")
                .Replace("^1", "")
                .Replace("^2", "")
                .Replace("^3", "")
                .Replace("^4", "")
                .Replace("^5", "")
                .Replace("^6", "")
                .Replace("^7", "")
                .Replace("^8", "")
                .Replace("^9", "");
        }

        public string FontBold(string message)
        {
            return "^b" + message + "^n";
        }

        public string FontItalic(string message)
        {
            return "^i" + message + "^n";
        }

        public string ColorMaroon(string message)
        {
            return "^1" + message + "^0";
        }

        public string ColorGreen(string message)
        {
            return "^2" + message + "^0";
        }

        public string ColorOrange(string message)
        {
            return "^3" + message + "^0";
        }

        public string ColorBlue(string message)
        {
            return "^4" + message + "^0";
        }

        public string ColorLightBlue(string message)
        {
            return "^5" + message + "^0";
        }

        public string ColorPurple(string message)
        {
            return "^6" + message + "^0";
        }

        public string ColorPink(string message)
        {
            return "^7" + message + "^0";
        }

        public string ColorRed(string message)
        {
            return "^8" + message + "^0";
        }

        public string ColorGray(string message)
        {
            return "^9" + message + "^0";
        }
    }

    #endregion class Logger

    #region class GZipWebClient

    public class GZipWebClient : WebClient
    {
        private readonly string _userAgent;
        private readonly bool _compress;

        public GZipWebClient(string userAgent = "Mozilla/5.0 (compatible; PRoCon 1; Farming-Manager",
            bool compress = true)
        {
            _userAgent = userAgent;
            _compress = compress;
            Headers["User-Agent"] = _userAgent;
        }

        public string GZipDownloadString(string address)
        {
            return GZipDownloadString(new Uri(address));
        }

        private string GZipDownloadString(Uri address)
        {
            Headers[HttpRequestHeader.UserAgent] = _userAgent;

            if (_compress == false)
                return DownloadString(address);

            Headers[HttpRequestHeader.AcceptEncoding] = "gzip";
            var stream = OpenRead(address);
            if (stream == null)
                return "";

            var contentEncoding = ResponseHeaders[HttpResponseHeader.ContentEncoding];
            Headers.Remove(HttpRequestHeader.AcceptEncoding);

            Stream decompressedStream = null;
            StreamReader reader = null;
            if (!string.IsNullOrEmpty(contentEncoding) && contentEncoding.ToLower().Contains("gzip"))
            {
                decompressedStream = new GZipStream(stream, CompressionMode.Decompress);
                reader = new StreamReader(decompressedStream);
            }
            else
            {
                reader = new StreamReader(stream);
            }

            var data = reader.ReadToEnd();
            reader.Close();
            decompressedStream?.Close();
            stream.Close();
            return data;
        }
    }

    #endregion class GZipWebClient

    #region class DiscordWebhook

    public class DiscordWebhook
    {
        private readonly Logger _logger;
        private readonly string _url;
        private readonly string _username;
        private readonly string _avatar;
        private readonly int _color;

        public DiscordWebhook(Logger logger, string url, string username, string avatar, int color)
        {
            _logger = logger;
            _url = url;
            _username = username;
            _avatar = avatar;
            _color = color;
        }

        public void SendNotification(string title, string content)
        {
            if (content == null)
            {
                _logger.Error("[DiscordWebhook] Webhook content cannot be empty. Please provide an input.");
                return;
            }

            var embed = new Hashtable
            {
                { "title", title },
                { "description", content },
                { "color", _color },
                { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
            };

            var embeds = new ArrayList { embed };

            var payload = new Hashtable
            {
                { "username", _username },
                { "avatar_url", _avatar },
                { "embeds", embeds }
            };

            var body = JSON.JsonEncode(payload);
            DoRequest(body);
        }

        private void DoRequest(string body)
        {
            try
            {
                var request = WebRequest.Create(_url);
                request.Method = "POST";
                request.ContentType = "application/json";
                var byteArray = Encoding.UTF8.GetBytes(body);
                request.ContentLength = byteArray.Length;

                using (var dataStream = request.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }
            }
            catch (Exception e)
            {
                _logger.Exception(e);
            }
        }
    }

    #endregion class DiscordWebhook
}