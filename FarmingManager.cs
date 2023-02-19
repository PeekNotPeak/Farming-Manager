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
        private const string StrPluginVersion = "0.2.2";
        private const string StrPluginAuthor = "PeekNotPeak";
        private const string StrPluginWebsite = "github.com/PeekNotPeak/Farming-Manager";

        /* ===== 1. Farming-Manager ===== */
        private bool _blnIsPluginEnabled;
        private bool _blnDoPluginUpdateCheck;

        private const string StrPluginUpdateUrl =
            "https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/version.json";

        public List<string> LstCurrentReservedSlotPlayers;

        /* ===== 2. Global Settings ===== */
        public bool BoolAllowFirstEnforcementMessage;

        /* ===== 3. Weapon Enforcers ===== */
        private readonly Dictionary<string, WeaponEnforcer> _dictWeaponEnforcersLookup;
        private bool _blnCreateNewWeaponEnforcer;
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
            _blnIsPluginEnabled = false;
            LstCurrentReservedSlotPlayers = new List<string>();

            /* ===== 2. Weapon Enforcers ===== */
            _dictWeaponEnforcersLookup = new Dictionary<string, WeaponEnforcer>();
            _blnCreateNewWeaponEnforcer = false;
            _intWeaponEnforcerDeletionId = 0;
            BoolAllowFirstEnforcementMessage = true;

            /* ===== 98. Plugin Update ===== */
            _blnDoPluginUpdateCheck = true;

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
            yield return BoolPluginVariable("1. Farming-Manager|Activate the plugin?", _blnIsPluginEnabled);
            yield return BoolPluginVariable("1. Farming-Manager|Check for plugin updates?", _blnDoPluginUpdateCheck);

            /* ===== 2. Global Settings ===== */
            yield return BoolYesNoPluginVariable("2. Global Settings|Allow first enforcement message?",
                BoolAllowFirstEnforcementMessage);

            /* ===== 3. Weapon Enforcers ===== */
            yield return BoolYesNoPluginVariable("3. Weapon Enforcers|Create new Weapon Enforcer?",
                _blnCreateNewWeaponEnforcer);
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
                        _blnIsPluginEnabled = bool.Parse(strValue);
                        break;

                    case "Check for plugin updates?":
                        _blnDoPluginUpdateCheck = bool.Parse(strValue);
                        break;

                    /* ===== 2. Weapon Enforcers ===== */
                    case "Create new Weapon Enforcer?":
                        _blnCreateNewWeaponEnforcer = strValue == "Yes";
                        break;

                    case "Weapon Enforcer deletion ID":
                        _intWeaponEnforcerDeletionId = int.Parse(strValue);
                        break;

                    /* ===== 3. Global Settings ===== */
                    case "Allow first enforcement message?":
                        BoolAllowFirstEnforcementMessage = strValue == "Yes";
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

                    case "Log to PRoCon Chat?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolLogToPRoConChat = strValue == "Yes";
                        break;

                    case "Enforce single weapon?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolEnforceSingleWeapon = strValue == "Yes";
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
                        _dictWeaponEnforcersLookup[enforcerId].FloatMaxAllowedKpm =
                            Convert.ToSingle(strValue.Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat);
                        break;

                    case "Set maximum allowed KDR":
                        _dictWeaponEnforcersLookup[enforcerId].FloatMaxAllowedKdr =
                            Convert.ToSingle(strValue.Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat);
                        break;

                    case "Set minimum required kills":
                        _dictWeaponEnforcersLookup[enforcerId].IntMinRequiredKills = int.Parse(strValue);
                        break;

                    case "Set maximum allowed kills":
                        _dictWeaponEnforcersLookup[enforcerId].IntMaxAllowedKills = int.Parse(strValue);
                        break;

                    case "Allow higher required kills for reserved slot players?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolAllowHigherTotalKillsForReservedSlotPlayers =
                            strValue == "Yes";
                        break;

                    case "Set minimum required kills for reserved slot players":
                        _dictWeaponEnforcersLookup[enforcerId].IntMinRequiredKillsForReservedSlotPlayers =
                            int.Parse(strValue);
                        break;

                    case "Set maximum allowed kills for reserved slot players":
                        _dictWeaponEnforcersLookup[enforcerId].IntMaxAllowedKillsForReservedSlotPlayers =
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

            if (!_blnDoPluginUpdateCheck) return;
            CheckForPluginUpdate();

            Logger.Debug(() => "Exiting OnAccountLogin Event", 7);
        }

        public override void OnServerInfo(CServerInfo csiServerInfo)
        {
            Logger.Debug(() => "Received OnServerInfo Event", 7);

            if (_blnIsPluginEnabled) SaveCurrentWeaponEnforcers();

            Logger.Debug(() => "Exiting OnServerInfo Event", 7);
        }

        public override void OnReservedSlotsList(List<string> soldierNames)
        {
            Logger.Debug(() => "Received OnReservedSlotsList Event", 7);

            if (!_blnIsPluginEnabled) return;
            LstCurrentReservedSlotPlayers = soldierNames;

            Logger.Debug(() => "Exiting OnReservedSlotsList Event", 7);
        }

        public override void OnPlayerKilled(Kill kKillerVictimDetails)
        {
            Logger.Debug(() => "Received OnPlayerKilled Event", 7);

            if (!_blnIsPluginEnabled) return;

            // We don't want to count area or explosion damage nor collisions or suicides
            if (kKillerVictimDetails.DamageType == "DamageArea" ||
                kKillerVictimDetails.DamageType == "DamageExplosion" ||
                kKillerVictimDetails.DamageType == "SoldierCollision" ||
                kKillerVictimDetails.DamageType == "Suicide") return;

            //Make sure the player didn't crash or anything
            if (kKillerVictimDetails.Killer.SoldierName == string.Empty ||
                kKillerVictimDetails.Killer.SoldierName == "" || kKillerVictimDetails.Killer.SoldierName == " " ||
                kKillerVictimDetails.Killer.SoldierName == kKillerVictimDetails.Victim.SoldierName) return;

            //Humanize the weapon name
            var weaponName = GetDecodedWeaponName(kKillerVictimDetails.DamageType);

            Logger.Debug(
                () =>
                    $"Player '{kKillerVictimDetails.Killer.SoldierName}' killed '{kKillerVictimDetails.Victim.SoldierName}' with '{weaponName}'",
                4);

            // Find the first WeaponEnforcer that monitors the weapon used by the killer
            var weaponEnforcer =
                _dictWeaponEnforcersLookup.Values.First(we => we.StrCurrentlyMonitoredWeapons.Contains(weaponName));

            //Run all Enforcer logic in a separate thread
            var weaponEnforcerThread = new Thread(() => { weaponEnforcer.RunEnforcement(kKillerVictimDetails); })
            {
                IsBackground = true,
                Name = "WeaponEnforcerThread"
            };

            ThreadPool.QueueUserWorkItem(_ => weaponEnforcerThread.Start());

            Logger.Debug(() => "Exiting OnPlayerKilled Event", 7);
        }

        public override void OnPlayerLeft(CPlayerInfo playerInfo)
        {
            Logger.Debug(() => "Received OnPlayerLeft Event", 7);

            if (!_blnIsPluginEnabled) return;

            //Remove the player from the list of tracked players
            foreach (var weaponEnforcer in _dictWeaponEnforcersLookup.Values)
                weaponEnforcer.RemoveTrackedPlayer(playerInfo.SoldierName);

            Logger.Debug(() => "Exiting OnPlayerLeft Event", 7);
        }

        public override void OnPlayerDisconnected(string soldierName, string reason)
        {
            Logger.Debug(() => "Received OnPlayerDisconnected Event", 7);

            if (!_blnIsPluginEnabled) return;

            //Remove the player from the list of tracked players
            foreach (var weaponEnforcer in _dictWeaponEnforcersLookup.Values)
                weaponEnforcer.RemoveTrackedPlayer(soldierName);

            Logger.Debug(() => "Exiting OnPlayerDisconnected Event", 7);
        }

        public override void OnRoundOver(int winningTeamId)
        {
            Logger.Debug(() => "Received OnRoundOver Event", 7);

            if (!_blnIsPluginEnabled) return;

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
                        if (_blnCreateNewWeaponEnforcer)
                        {
                            CreateNewWeaponEnforcer();
                            _blnCreateNewWeaponEnforcer = false;
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

        public void SendGlobalMessage(string message)
        {
        }

        public void SendGlobalYell(string message, int duration)
        {
        }

        public void SendTeamMessage(string message, int teamId)
        {
        }

        public void SendTeamYell(string message, int duration, int teamId)
        {
        }

        public void SendSquadMessage(string message, int teamId, int squadId)
        {
        }

        public void SendSquadYell(string message, int duration, int teamId, int squadId)
        {
        }

        public void LogToPRoConChat(string message)
        {
            message = Logger.ColorOrange(GetPluginName()) + " > " + message;
            ExecuteCommand("procon.protected.chat.write", message);
        }

        public void SendPlayerMessage(string soldierName, string message)
        {
            message = "[" + GetPluginName() + "] " + message;
            ExecuteCommand("procon.protected.send", "admin.say", message, "player", soldierName);
        }

        public void SendPlayerYell(string soldierName, string message, int duration, bool doLogging = true)
        {
            message = "[" + GetPluginName() + "] " + message;
            ExecuteCommand("procon.protected.send", "admin.yell", message, duration.ToString(), "player", soldierName);
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

        public WeaponEnforcerState EnforcerState;
        public bool BoolLogToPRoConChat;
        public bool BoolEnforceSingleWeapon;
        public string[] StrCurrentlyMonitoredWeapons;
        public bool BoolPersistTrackedPlayersThroughRounds;
        public int IntMinRequiredKills;
        public int IntMaxAllowedKills;
        public bool BoolAllowHigherTotalKillsForReservedSlotPlayers;
        public int IntMinRequiredKillsForReservedSlotPlayers;
        public int IntMaxAllowedKillsForReservedSlotPlayers;
        public float FloatMaxAllowedKpm;
        public float FloatMaxAllowedKdr;

        private readonly Dictionary<string, List<Dictionary<string, int>>> _dictTrackedPlayers;

        public WeaponEnforcer(FarmingManager plugin, string enforcerId)
        {
            _plugin = plugin;
            _strEnforcerId = enforcerId;

            EnforcerState = WeaponEnforcerState.Disabled;
            BoolLogToPRoConChat = true;
            BoolEnforceSingleWeapon = false;
            StrCurrentlyMonitoredWeapons = new[]
                { "AH-1Z Viper Attack Helicopter", "Type 99 MBT", "M1 Abrams MBT", "LAV-25 APC" };
            BoolPersistTrackedPlayersThroughRounds = true;
            IntMinRequiredKills = 30;
            BoolAllowHigherTotalKillsForReservedSlotPlayers = false;
            IntMinRequiredKillsForReservedSlotPlayers = 40;
            FloatMaxAllowedKpm = 2.0F;
            FloatMaxAllowedKdr = 12.0F;

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

            var enforcerVariables = new List<CPluginVariable>
            {
                new CPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Enable Weapon Enforcer?",
                    FarmingManagerUtilities.CreateEnumString<WeaponEnforcerState>(), EnforcerState.ToString()),
                BoolYesNoPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Log to PRoCon Chat?",
                    BoolLogToPRoConChat),
                BoolYesNoPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Enforce single weapon?",
                    BoolEnforceSingleWeapon),
                StringArrayPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Select monitored weapon",
                    StrCurrentlyMonitoredWeapons),
                BoolYesNoPluginVariable(
                    GetFullName() + $"|#[4.{_strEnforcerId}] Persist tracked players through rounds?",
                    BoolPersistTrackedPlayersThroughRounds),
                FloatPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum allowed KPM",
                    FloatMaxAllowedKpm),
                FloatPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum allowed KDR",
                    FloatMaxAllowedKdr),
                IntPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set minimum required kills",
                    IntMinRequiredKills),
                IntPluginVariable(GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum allowed kills",
                    IntMaxAllowedKills),
                BoolYesNoPluginVariable(
                    GetFullName() + $"|#[4.{_strEnforcerId}] Allow higher required kills for reserved slot players?",
                    BoolAllowHigherTotalKillsForReservedSlotPlayers)
            };
            if (!BoolAllowHigherTotalKillsForReservedSlotPlayers) return enforcerVariables;

            enforcerVariables.Add(IntPluginVariable(
                GetFullName() + $"|#[4.{_strEnforcerId}] Set minimum required kills for reserved slot players",
                IntMinRequiredKillsForReservedSlotPlayers));
            enforcerVariables.Add(IntPluginVariable(
                GetFullName() + $"|#[4.{_strEnforcerId}] Set maximum allowed kills for reserved slot players",
                IntMaxAllowedKills));

            return enforcerVariables;
        }

        public void RunEnforcement(Kill kKillerVictimDetails)
        {
            _plugin.Logger.Debug(() => $"Starting up RunEnforcement for Enforcer #{_strEnforcerId}", 7);

            //If the enforcer is disabled, we want to immediately return
            //However we allow virtual mode and obviously enabled mode
            if (EnforcerState == WeaponEnforcerState.Disabled) return;

            //If current time is not within the allowed time frame, we want to return
            //TODO: Add time based enforcement
            if (!CurrentTimeWithinAllowedTimeFrameCheck()) return;

            //Local variables for readability
            var soldierName = kKillerVictimDetails.Killer.SoldierName;
            var playerWeapon = _plugin.GetDecodedWeaponName(kKillerVictimDetails.DamageType);

            //Soldier hasn't been tracked yet so we need to start the initial check
            if (!_dictTrackedPlayers.ContainsKey(kKillerVictimDetails.Killer.SoldierName))
            {
                StartInitialCheck(soldierName, playerWeapon);
                return;
            }

            //Soldier has been tracked but the weapon they're using hasn't been tracked yet
            if (!_dictTrackedPlayers[soldierName].Any(x => x.ContainsKey(playerWeapon)))
            {
                AddTrackedWeaponToSoldier(soldierName, playerWeapon);
                return;
            }

            //Soldier has been tracked and the weapon they're using has been tracked
            //So we just need to increment the weapon's usage count
            IncrementCountOnTrackedWeapon(soldierName, playerWeapon);

            //Lastly go through the tracked player again and check if they exceeded some limit
            LastCheckUpOnPlayer(kKillerVictimDetails.Killer, playerWeapon);

            _plugin.Logger.Debug(() => $"Exiting RunEnforcement for Enforcer #{_strEnforcerId}", 7);
        }

        private bool CurrentTimeWithinAllowedTimeFrameCheck()
        {
            _plugin.Logger.Debug(() => "Starting up CurrentTimeWithinAllowedTimeFrameCheck", 7);

            //TODO: Add time based enforcement
            var currentTime = DateTime.Now;
            _plugin.Logger.Debug(() => $"Current time is {currentTime}", 3);

            _plugin.Logger.Debug(() => "Exiting CurrentTimeWithinAllowedTimeFrameCheck", 7);

            return true;
        }

        private void StartInitialCheck(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up StartInitialCheck", 7);

            AddFirstTrackingEntry(soldierName, playerWeapon);

            //Send the player a message that they are being monitored if the option is enabled
            if (_plugin.BoolAllowFirstEnforcementMessage)
            {
                if (EnforcerState != WeaponEnforcerState.Virtual)
                {
                    _plugin.SendPlayerYell(soldierName,
                        $"You are now being monitored by Enforcer #{_strEnforcerId} for weapon {playerWeapon}", 10);
                    _plugin.SendPlayerMessage(soldierName,
                        $"You are now being monitored by Enforcer #{_strEnforcerId} for weapon {playerWeapon}");
                }
                else
                {
                    _plugin.Logger.Debug(
                        () =>
                            $"Enforcer #{_strEnforcerId} is in virtual mode, so no first track message will be sent to {soldierName}",
                        3);
                }
            }

            //Log the message to the console if the option is enabled
            if (BoolLogToPRoConChat && EnforcerState != WeaponEnforcerState.Virtual)
                _plugin.LogToPRoConChat(
                    $"Enforcer #{_strEnforcerId} is now monitoring '{soldierName}' for weapon '{playerWeapon}'");

            _plugin.Logger.Debug(() => "Exiting StartInitialCheck", 7);
        }

        private void LastCheckUpOnPlayer(CPlayerInfo player, string weapon)
        {
            _plugin.Logger.Debug(() => "Starting up LastCheckUpOnPlayer", 7);

            var minRequiredKills = IntMinRequiredKills;
            if (BoolAllowHigherTotalKillsForReservedSlotPlayers &&
                _plugin.LstCurrentReservedSlotPlayers.Contains(player.SoldierName))
                minRequiredKills = IntMinRequiredKillsForReservedSlotPlayers;

            //Compute the 25% of the minimum required kills and cast it to int
            var minRequiredKills25Percent = (int)Math.Round(minRequiredKills * 0.25);

            //Notify the player once they reach 25% of the minimum required kills with the specific weapon
            if (_dictTrackedPlayers[player.SoldierName]
                .Any(x => x.ContainsKey(weapon) && x[weapon] == minRequiredKills25Percent))
            {
                Do25PercentNotification(player.SoldierName, weapon);
                return;
            }

            //Notify the player once they reach 50% of the minimum required kills with the specific weapon
            if (_dictTrackedPlayers[player.SoldierName]
                .Any(x => x.ContainsKey(weapon) && x[weapon] == minRequiredKills25Percent * 2))
            {
                Do50PercentNotification(player.SoldierName, weapon);
                return;
            }

            //Notify the player once they reach 75% of the minimum required kills with the specific weapon
            if (_dictTrackedPlayers[player.SoldierName]
                .Any(x => x.ContainsKey(weapon) && x[weapon] == minRequiredKills25Percent * 3))
            {
                Do75PercentNotification(player.SoldierName, weapon);
                return;
            }

            //Everything else is technically 100% or more
            //So in this case check if we have exceeded the maximum allowed kills and maximum allowed kpm or maximum allowed kdr
            //If we have, then we need to kick the player
            if (_dictTrackedPlayers[player.SoldierName]
                .Any(x => x.ContainsKey(weapon) && x[weapon] >= minRequiredKills)) HandleEnforcementPunishment(player);

            _plugin.Logger.Debug(() => "Exiting LastCheckUpOnPlayer", 7);
        }

        private void HandleEnforcementPunishment(CPlayerInfo player)
        {
            _plugin.Logger.Debug(() => "Starting up HandleEnforcementPunishment", 7);

            //TODO: Handle the punishment

            _plugin.Logger.Debug(() => "Exiting HandleEnforcementPunishment", 7);
        }

        private void Do25PercentNotification(string soldierName, string playerWeapon)
        {
            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                _plugin.SendPlayerYell(soldierName,
                    $"You have reached 25% of the minimum required kills for weapon {playerWeapon}", 10);
                _plugin.SendPlayerMessage(soldierName,
                    $"You have reached 25% of the minimum required kills for weapon {playerWeapon}");
            }
            else
            {
                _plugin.Logger.Debug(
                    () =>
                        $"Enforcer #{_strEnforcerId} is in virtual mode, so no 25% message warning will be sent to {soldierName}",
                    3);
            }
        }

        private void Do50PercentNotification(string soldierName, string playerWeapon)
        {
            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                _plugin.SendPlayerYell(soldierName,
                    $"You have reached 50% of the minimum required kills for weapon {playerWeapon}", 10);
                _plugin.SendPlayerMessage(soldierName,
                    $"You have reached 50% of the minimum required kills for weapon {playerWeapon}");
            }
            else
            {
                _plugin.Logger.Debug(
                    () =>
                        $"Enforcer #{_strEnforcerId} is in virtual mode, so no 50% message will be sent to {soldierName}",
                    3);
            }
        }

        private void Do75PercentNotification(string soldierName, string playerWeapon)
        {
            if (EnforcerState != WeaponEnforcerState.Virtual)
            {
                _plugin.SendPlayerYell(soldierName,
                    $"You have reached 75% of the minimum required kills for weapon {playerWeapon}", 10);
                _plugin.SendPlayerMessage(soldierName,
                    $"You have reached 75% of the minimum required kills for weapon {playerWeapon}");
            }
            else
            {
                _plugin.Logger.Debug(
                    () =>
                        $"Enforcer #{_strEnforcerId} is in virtual mode, so no 75% message will be sent to {soldierName}",
                    3);
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
                () => $"Added new player '{soldierName}' to tracked players with weapon '{playerWeapon}'", 3);

            _plugin.Logger.Debug(() => "Exiting AddFirstTrackingEntry", 7);
        }

        private void AddTrackedWeaponToSoldier(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up AddTrackedWeaponToSoldier", 7);

            _dictTrackedPlayers[soldierName].Add(new Dictionary<string, int>
            {
                { playerWeapon, 1 }
            });

            _plugin.Logger.Debug(
                () => $"Added new weapon '{playerWeapon}' to tracked weapons for player '{soldierName}'", 3);

            _plugin.Logger.Debug(() => "Exiting AddTrackedWeaponToSoldier", 7);
        }

        private void IncrementCountOnTrackedWeapon(string soldierName, string playerWeapon)
        {
            _plugin.Logger.Debug(() => "Starting up IncrementCountOnTrackedWeapon", 7);

            _dictTrackedPlayers[soldierName].First(x => x.ContainsKey(playerWeapon))[playerWeapon]++;
            var weaponUsageCount =
                _dictTrackedPlayers[soldierName].First(x => x.ContainsKey(playerWeapon))[playerWeapon];
            _plugin.Logger.Debug(
                () => $"Incremented count for player {soldierName} with {playerWeapon} to {weaponUsageCount}", 3);

            _plugin.Logger.Debug(() => "Exiting IncrementCountOnTrackedWeapon", 7);
        }

        public void RemoveTrackedPlayer(string soldierName)
        {
            _plugin.Logger.Debug(() => "Starting up RemoveTrackedPlayer", 7);

            if (_dictTrackedPlayers.ContainsKey(soldierName))
            {
                _dictTrackedPlayers.Remove(soldierName);
                _plugin.Logger.Debug(() => $"Removed player '{soldierName}' from tracked players", 3);
            }

            _plugin.Logger.Debug(() => "Exiting RemoveTrackedPlayer", 7);
        }

        public void ResetTrackedPlayers()
        {
            _plugin.Logger.Debug(() => "Starting up ResetTrackedPlayer", 7);

            if (!BoolPersistTrackedPlayersThroughRounds)
            {
                _dictTrackedPlayers.Clear();
                _plugin.Logger.Debug(() => "Reset all tracked players", 3);
            }

            _plugin.Logger.Debug(() => "Exiting ResetTrackedPlayer", 7);
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