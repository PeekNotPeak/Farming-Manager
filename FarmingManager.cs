using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using PRoCon.Core;
using PRoCon.Core.Plugin;

namespace PRoConEvents
{
    #region class FarmingManager

    public class FarmingManager : PRoConPluginAPI, IPRoConPluginInterface
    {
        #region Global Variables

        /* ===== Miscellaneous ===== */
        private const string StrPluginName = "Farming-Manager";
        private const string StrPluginVersion = "0.1.5";
        private const string StrPluginAuthor = "PeekNotPeak";
        private const string StrPluginWebsite = "github.com/PeekNotPeak/Farming-Manager";
        
        /* ===== 1. Farming-Manager ===== */
        private bool _blnIsPluginEnabled;
        private bool _blnDoPluginUpdateCheck;
        private const string StrPluginUpdateUrl = "https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/version.json";
        
        /* ===== 2. Weapon Enforcers ===== */
        private Dictionary<string, WeaponEnforcer> _dictWeaponEnforcersLookup;
        private bool _blnSpawnNewWeaponEnforcer;
        private int _intWeaponEnforcerDeletionId;
        private const string StrWeaponEnforcersSavePath = "Plugins/BF4/Farming-Manager_WeaponEnforcers.json";

        private Hashtable _hshtblHumanWeaponNames;
        private const string StrHumanWeaponNamesUrl = "https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/weapon_names.json";

        /* ===== 99. Debugging ===== */
        public readonly Logger Logger;

        #endregion Global Variables

        #region Constructor

        public FarmingManager()
        {
            /* ===== 1. FarmingManager ===== */
            _blnIsPluginEnabled = false;
            
            /* ===== 2. Weapon Enforcers ===== */
            _dictWeaponEnforcersLookup = new Dictionary<string, WeaponEnforcer>();
            _blnSpawnNewWeaponEnforcer = false;
            _intWeaponEnforcerDeletionId = 0;
            
            /* ===== 98. Plugin Update ===== */
            _blnDoPluginUpdateCheck = true;
            
            /* ===== 99. Debugging ===== */
            Logger = new Logger(this) { IntDebugLevel = 0, BoolDoDebugOutPut = false}; //Debug level is 0 by default and will not output any debug messages.
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
            var events = new[]
            {
                /* Miscellaneous */
                "OnAccountLogin",
                
                /* Server Events */
                "OnServerInfo",
                
                /* Player Events */
                "OnPlayerKilled",
            };

            RegisterEvents(GetType().Name, events);
            
            LoadSavedWeaponEnforcers();
        }

        public void OnPluginEnable()
        {
            Logger.Write($"Plugin enabled. Running on version {GetPluginVersion()}.");

            _hshtblHumanWeaponNames = FetchHumanizedWeaponNames();
        }

        public void OnPluginDisable()
        {
            Logger.Write("Plugin successfully shut down.");
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
            CPluginVariable StringPluginVariable(string name, string value) => new CPluginVariable(name, typeof(string), value);
            CPluginVariable StringArrayPluginVariable(string name, IEnumerable<string> value) => new CPluginVariable(name, typeof(string[]), value.PrepareSafely(true));
            CPluginVariable FloatPluginVariable(string name, float value) => new CPluginVariable(name, typeof(string), value.ToString("0.00", CultureInfo.InvariantCulture.NumberFormat));
            CPluginVariable IntPluginVariable(string name, int value) => new CPluginVariable(name, typeof(int), value);
            CPluginVariable BoolPluginVariable(string name, bool value) => new CPluginVariable(name, typeof(bool), value);
            CPluginVariable BoolYesNoPluginVariable(string name, bool value) => new CPluginVariable(name, typeof(enumBoolYesNo), value ? enumBoolYesNo.Yes : enumBoolYesNo.No);

            /* ===== 1. Farming-Manager ===== */
            yield return BoolPluginVariable("1. Farming-Manager|Activate the plugin?", _blnIsPluginEnabled);
            yield return BoolPluginVariable("1. Farming-Manager|Check for plugin updates?", _blnDoPluginUpdateCheck);

            /* ===== 2. Weapon Enforcers ===== */
            yield return BoolYesNoPluginVariable("2. Weapon Enforcers|Spawn new Weapon Enforcer?", _blnSpawnNewWeaponEnforcer);
            yield return IntPluginVariable("2. Weapon Enforcers|Weapon Enforcer deletion ID", _intWeaponEnforcerDeletionId);
            
            /* ===== 2.x Weapon Enforcers ===== */
            foreach (var variable in _dictWeaponEnforcersLookup.Values.SelectMany(weaponEnforcer => weaponEnforcer.DisplayEnforcerVariables()))
            {
                yield return variable;
            }

            /* ===== 99. Debugging ===== */
            yield return BoolPluginVariable("99. Debugging|Enable debug output?", Logger.BoolDoDebugOutPut);
            if (Logger.BoolDoDebugOutPut)
            {
                yield return IntPluginVariable("99. Debugging|Debug level", Logger.IntDebugLevel);
            }
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            if (strVariable.Contains('|')) strVariable = strVariable.Substring(strVariable.IndexOf('|') + 1);
            
            Logger.Debug(() => $"Setting variable '{strVariable}' to value '{strValue}'".Trim(), 7);
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
                    case "Spawn new Weapon Enforcer?":
                        _blnSpawnNewWeaponEnforcer = strValue == "Yes";
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
                        _dictWeaponEnforcersLookup[enforcerId].EnforcerState = (WeaponEnforcer.WeaponEnforcerState)Enum.Parse(typeof(WeaponEnforcer.WeaponEnforcerState), CPluginVariable.Decode(strValue));
                        break;
                    
                    case "Enforce single instance weapon?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolEnforceSingleInstanceWeapon = strValue == "Yes";
                        break;
                    
                    case "Persist tracked players through rounds?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolPersistTrackedPlayersThroughRounds = strValue == "Yes";
                        break;
                    
                    case "Select monitored vehicle":
                        _dictWeaponEnforcersLookup[enforcerId].StrCurrentlyMonitoredWeapons = CPluginVariable.DecodeStringArray(strValue);
                        break;
                    
                    case "Set minimum required kills":
                        _dictWeaponEnforcersLookup[enforcerId].IntMinRequiredKills = int.Parse(strValue);
                        break;
                    
                    case "Allow higher required kills for reserved slot players?":
                        _dictWeaponEnforcersLookup[enforcerId].BoolAllowHigherTotalKillsForReservedSlotPlayers = strValue == "Yes";
                        break;
                    
                    case "Set minimum required kills for reserved slot players":
                        _dictWeaponEnforcersLookup[enforcerId].IntMinRequiredKillsForReservedSlotPlayers = int.Parse(strValue);
                        break;
                    
                    case "Set maximum allowed KPM":
                        _dictWeaponEnforcersLookup[enforcerId].FloatMaxAllowedKpm = Convert.ToSingle(strValue.Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat);
                        break;
                    
                    case "Set maximum allowed KDR":
                        _dictWeaponEnforcersLookup[enforcerId].FloatMaxAllowedKdr = Convert.ToSingle(strValue.Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat);
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
            if (!_blnDoPluginUpdateCheck) return;
            CheckForPluginUpdate();
        }

        public override void OnServerInfo(CServerInfo csiServerInfo)
        {
            if (_blnIsPluginEnabled) SaveCurrentWeaponEnforcers();
        }

        public override void OnPlayerKilled(Kill kKillerVictimDetails)
        {
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
            
            Logger.Debug(() => $"Player '{kKillerVictimDetails.Killer.SoldierName}' killed '{kKillerVictimDetails.Victim.SoldierName}' with '{kKillerVictimDetails.DamageType}'", 10);

            // Find the first WeaponEnforcer that monitors the weapon used by the killer
            var weaponEnforcer = _dictWeaponEnforcersLookup.Values.First(we => we.StrCurrentlyMonitoredWeapons.Contains(kKillerVictimDetails.DamageType));

            //Run all Enforcer logic in a separate thread
            var weaponEnforcerThread = new Thread(() =>
            {
                weaponEnforcer.RunEnforcementLogic(kKillerVictimDetails);
            })
            {
                IsBackground = true,
                Name = "WeaponEnforcerThread"
            };

            ThreadPool.QueueUserWorkItem(_ => weaponEnforcerThread.Start());

        }

        #endregion PRoConPluginAPI

        #region Helper Methods

        private void CheckForPluginUpdate()
        {
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
                {
                    Logger.Warn($"You are currently using version {GetPluginVersion()} of {GetPluginName()}. " +
                                $"Consider upgrading to the newest version ({latestVersion}) via GitHub.");
                }
            })
            {
                IsBackground = true,
                Name = "PluginUpdateThread"
            };
            pluginUpdateThread.Start();
            
            Logger.Debug(() => "Received version info from GitHub: " + latestVersion, 10);
        }

        private Hashtable FetchHumanizedWeaponNames()
        {
            Logger.Debug(() => "Starting up FetchHumanizedWeaponNames", 7);
            
            Hashtable humanizedWeaponNames;

            using (var webClient = new WebClient())
            {
                Logger.Debug(() => "Downloading humanized weapon names from GitHub.", 2);
                
                var response = webClient.DownloadString(StrHumanWeaponNamesUrl);
                humanizedWeaponNames = (Hashtable)JSON.JsonDecode(response);
            }
            
            Logger.Debug(() => "Exiting FetchHumanizedWeaponNames", 7);
            
            return humanizedWeaponNames;
        }

        private void ValidateParsedValues(string strVariable, string strValue)
        {
            try
            {
                switch (strVariable)
                {
                    /* ===== 2. Weapon Enforcers ===== */
                    case "Spawn new Weapon Enforcer?":
                        if (_blnSpawnNewWeaponEnforcer)
                        {
                            CreateNewWeaponEnforcer();
                            _blnSpawnNewWeaponEnforcer = false;
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
                        else Logger.IntDebugLevel = int.Parse(strValue);
                        break;
                }   
            }
            catch (Exception e)
            {
                Logger.Exception(e);
            }
        }

        #endregion

        #region Weapon Enforcers Helper Methods

        private void CreateNewWeaponEnforcer()
        {
            var id = GetNextWeaponEnforcerId();
            _dictWeaponEnforcersLookup.Add(id, new WeaponEnforcer(this, id));
            
            Logger.Debug(() => $"Created new Weapon Enforcer with ID {id}", 8);
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
            if (handlerId == 0 || !_dictWeaponEnforcersLookup.ContainsKey(handlerId.ToString())) return;

            _dictWeaponEnforcersLookup.Remove(handlerId.ToString());
            
            Logger.Debug(() => $"Deleted Weapon Enforcer with ID {handlerId}", 8);
        }
        
        private void SaveCurrentWeaponEnforcers()
        {
            if (_dictWeaponEnforcersLookup.Count >= 0) return;
            Logger.Debug((() => $"Saving current Weapon Enforcers to {StrWeaponEnforcersSavePath}..."), 10);

            //TODO: Add saving of current weapon enforcers
        }
        
        private void LoadSavedWeaponEnforcers()
        {
            if (!File.Exists(StrWeaponEnforcersSavePath)) return;
            Logger.Debug((() => $"Loading saved Weapon Enforcers from {StrWeaponEnforcersSavePath}..."), 10);
            
            //TODO: Add loading of saved weapon enforcers
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
        public bool BoolEnforceSingleInstanceWeapon;
        public string[] StrCurrentlyMonitoredWeapons;
        public bool BoolPersistTrackedPlayersThroughRounds;
        public int IntMinRequiredKills;
        public bool BoolAllowHigherTotalKillsForReservedSlotPlayers;
        public int IntMinRequiredKillsForReservedSlotPlayers;
        public float FloatMaxAllowedKpm;
        public float FloatMaxAllowedKdr;

        private readonly Dictionary<string, List<Dictionary<string, int>>> _dictTrackedPlayers;

        public WeaponEnforcer(FarmingManager plugin, string enforcerId)
        {
            _plugin = plugin;
            _strEnforcerId = enforcerId;

            EnforcerState = WeaponEnforcerState.Disabled;
            BoolEnforceSingleInstanceWeapon = true;
            StrCurrentlyMonitoredWeapons = new string[] { "Gameplay/Vehicles/AH1Z/AH1Z", "Gameplay/Vehicles/AH6/AH6_Littlebird", "Gameplay/Vehicles/F35/F35B", "Gameplay/Vehicles/M1A2/M1Abrams", "Gameplay/Vehicles/CH_MBT_Type99/CH_MBT_Type99" };
            BoolPersistTrackedPlayersThroughRounds = true;
            IntMinRequiredKills = 0;
            BoolAllowHigherTotalKillsForReservedSlotPlayers = true;
            IntMinRequiredKillsForReservedSlotPlayers = 0;
            FloatMaxAllowedKpm = 2.0F;
            FloatMaxAllowedKdr = 12.0F;
            
            _dictTrackedPlayers = new Dictionary<string, List<Dictionary<string, int>>>();
        }

        public IEnumerable<CPluginVariable> DisplayEnforcerVariables()
        {
            //Type safe plugin variable creation
            CPluginVariable StringPluginVariable(string name, string value) => new CPluginVariable(name, typeof(string), value);
            CPluginVariable StringArrayPluginVariable(string name, IEnumerable<string> value) => new CPluginVariable(name, typeof(string[]), value.PrepareSafely(true));
            CPluginVariable FloatPluginVariable(string name, float value) => new CPluginVariable(name, typeof(string), value.ToString("0.00", CultureInfo.InvariantCulture.NumberFormat));
            CPluginVariable IntPluginVariable(string name, int value) => new CPluginVariable(name, typeof(int), value);
            CPluginVariable BoolPluginVariable(string name, bool value) => new CPluginVariable(name, typeof(bool), value);
            CPluginVariable BoolYesNoPluginVariable(string name, bool value) => new CPluginVariable(name, typeof(enumBoolYesNo), value ? enumBoolYesNo.Yes : enumBoolYesNo.No);

            var enforcerVariables = new List<CPluginVariable>
            {
                new CPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Enable Weapon Enforcer?", FarmingManagerUtilities.CreateEnumString<WeaponEnforcerState>(), EnforcerState.ToString()),
                BoolYesNoPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Enforce single instance weapon?", BoolEnforceSingleInstanceWeapon),
                StringArrayPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Select monitored weapon", StrCurrentlyMonitoredWeapons),
                BoolYesNoPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Persist tracked players through rounds?", BoolPersistTrackedPlayersThroughRounds),
                IntPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Set minimum required kills", IntMinRequiredKills),
                BoolYesNoPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Allow higher required kills for reserved slot players?", BoolAllowHigherTotalKillsForReservedSlotPlayers)
            };
            if (BoolAllowHigherTotalKillsForReservedSlotPlayers)
                enforcerVariables.Add(IntPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Set minimum required kills for reserved slot players", IntMinRequiredKillsForReservedSlotPlayers));
            
            enforcerVariables.Add(FloatPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Set maximum allowed KPM", FloatMaxAllowedKpm));
            enforcerVariables.Add(FloatPluginVariable(GetFullName() + $"|#[2.{_strEnforcerId}] Set maximum allowed KDR", FloatMaxAllowedKdr));

            return enforcerVariables;
        }

        public void RunEnforcementLogic(Kill kKillerVictimDetails)
        {
            //If the enforcer is disabled, we want to immediately return
            //However we allow virtual mode and obviously enabled mode
            if (EnforcerState == WeaponEnforcerState.Disabled) return;
            
            //Track the player's weapon and how often they've used it
            var playerWeapon = kKillerVictimDetails.DamageType;
            var killerSoldierName = kKillerVictimDetails.Killer.SoldierName;
            var victimSoldierName = kKillerVictimDetails.Victim.SoldierName;
            
            _plugin.Logger.Debug(() => $"Currently tracking player '{killerSoldierName}' with '{playerWeapon}' on Enforcer #{_strEnforcerId}", 10);

            //Soldier hasn't been tracked yet so every weapon is new
            if (!_dictTrackedPlayers.ContainsKey(kKillerVictimDetails.Killer.SoldierName))
            {
                _plugin.Logger.Debug(() => $"Adding new player '{killerSoldierName}' to tracked players", 10);
                _dictTrackedPlayers.Add(killerSoldierName, new List<Dictionary<string, int>>()
                {
                    new Dictionary<string, int>()
                    {
                        { playerWeapon, 1 }
                    }
                });
            }

            //Soldier has been tracked but the weapon they're using hasn't been tracked yet
            else if (!_dictTrackedPlayers[killerSoldierName].Any(x => x.ContainsKey(playerWeapon)))
            {
                _plugin.Logger.Debug(() => $"Adding new weapon '{playerWeapon}' to tracked weapons for player '{killerSoldierName}'", 10);
                _dictTrackedPlayers[killerSoldierName].Add(new Dictionary<string, int>()
                {
                    { playerWeapon, 1 }
                });
            }

            //Soldier has been tracked and the weapon they're using has been tracked
            //So we just need to increment the weapon's usage count
            else
            {
                _dictTrackedPlayers[killerSoldierName].First(x => x.ContainsKey(playerWeapon))[playerWeapon]++;
                var weaponUsageCount = _dictTrackedPlayers[killerSoldierName].First(x => x.ContainsKey(playerWeapon))[playerWeapon];
                _plugin.Logger.Debug(() => $"Incrementing weapon {playerWeapon} usage count for player {killerSoldierName} to {weaponUsageCount}", 10);
            }
        }

        private string GetFullName()
        {
            return $"2.{_strEnforcerId} Weapon Enforcer with ID #{_strEnforcerId}";
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
            return prepare ? data.Select(CPluginVariable.Encode).ToArray() : (data as string[]) ?? data.ToArray();
        }

        public static string HumanizeWeaponName(string engineName)
        {
            string humanized;
            
            switch (engineName)
            {
                case "Gameplay/Vehicles/AH1Z/AH1Z":
                    humanized = "AH1Z";
                    break; 
                
                case "Gameplay/Vehicles/AH6/AH6_Littlebird":
                    humanized = "AH6J";
                    break;
                
                default:
                    humanized = "Unknown";
                    break;
            }

            return humanized;
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