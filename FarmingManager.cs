/*
 * Farming-Manager - A plugin for PRoCon designed to help keep servers alive and restrict certain weapons and play-styles.
 * 
 * Copyright 2024 PeekNotPeak 
 * 
 * The Farming-Manager Frostbite Plugin is free software: You can redistribute it and/or modify it under the terms of the
 * GNU General Public License as published by the Free Software Foundation, either version 3 of the License,
 * or (at your option) any later version. Farming-Manager is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 * See the GNU General Public License for more details. To view this license, visit http://www.gnu.org/licenses/.
 * 
 * Code Credit:
 * Threading Logic, Logger Class, FServer, FWeapon, FWeaponDictionary,
 * FPlayer, FKill from AdKats by ColColonCleaner (https://github.com/adkats/adkats)
 * 
 * Type safe variables from LanguageEnforcer by maxdralle (https://myrcon.net/topic/183-language-enforcer-1030-apr-11/)
 * 
 * Development by PeekNotPeak
 * 
 * FarmingManager.cs
 * Version 1.0.0.0
 * 21-Jan-2024
 * 
 */


// ReSharper disable CheckNamespace

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using PRoCon.Core;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Plugin;

namespace PRoConEvents
{
    public class FarmingManager : PRoConPluginAPI, IPRoConPluginInterface
    {
        #region Global Variables
        
        // Miscellaneous
        private readonly Version _pluginVersion = new Version(0, 0, 1, 0);
        private const string PluginName = "Farming-Manager";
        private const string PluginAuthor = "PeekNotPeak";
        private const string PluginWebsite = "github.com/PeekNotPeak/Farming-Manager";
        private const string PluginUpdateUrl = 
            "https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/version.json";

        private readonly Logger _log;
        
        // Plugin state
        private volatile bool _pluginEnabled;
        private volatile bool _threadsReady;

        private readonly FServer _serverInfo;
        
        // Threads
        private readonly ThreadManager _threadManager;
        private Thread _activatorThread;
        private Thread _finalizerThread;
        private Thread _killProcessingThread;
        
        // Queues
        private readonly Queue<Kill> _killProcessingQueue = new Queue<Kill>();
        
        // Wait handles
        private EventWaitHandle _killProcessingWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
        
        // Players
        private readonly Dictionary<string, FPlayer> _playersDictionary = new Dictionary<string, FPlayer>();
        private readonly Dictionary<string, FPlayer> _playersLeftDictionary = new Dictionary<string, FPlayer>();
        
        // Weapons
        private readonly FWeaponDictionary _weaponDictionary;

        #endregion Global Variables
        
        #region Constructor

        public FarmingManager()
        {
            _log = new Logger(this)
            {
                DebugLevel = 0,
                DoDebugOutPut = false
            };
            _threadManager = new ThreadManager(_log);
            _serverInfo = new FServer(this);

            try
            {
                _weaponDictionary = new FWeaponDictionary(this);
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }
        
        #endregion Constructor
        
        #region Threads
        
        private void InitializeWaitHandles()
        {
            _threadManager.Init();
            
            _killProcessingWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
        }

        private void OpenAllHandles()
        {
            _threadManager.Set();
            
            _killProcessingWaitHandle.Set();
        }

        private void InitializeThreads()
        {
            try
            {
                _killProcessingThread = new Thread(KillProcessingThreadLoop)
                {
                    IsBackground = true
                };
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }

        private void StartThreads()
        {
            try
            {   
                // Reset the master wait handle
                _threadManager.Reset();
                
                // Start the threads
                _threadManager.StartWatchdog(_killProcessingThread);

                _threadsReady = true;
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }

        #endregion Threads
        
        #region ThreadQueues
        
        private void QueueKillForProcessing(Kill kKillerVictimDetails)
        {
            _log.Debug(() => "Entering QueueKillForProcessing", 7);
            
            try
            {
                if (!_pluginEnabled || !_threadsReady) return;
                
                _log.Debug(() => "Preparing to lock kill processing queue to add new kill.", 6);
                lock (_killProcessingQueue)
                {
                    _log.Debug(() => "Kill has been queued for processing", 6);
                    _killProcessingQueue.Enqueue(kKillerVictimDetails);
                }
                
                _killProcessingWaitHandle.Set();

            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
            
            _log.Debug(() => "Exiting QueueKillForProcessing", 7);
        }
        
        #endregion ThreadQueues
        
        #region ThreadLoops
        
        private void KillProcessingThreadLoop()
        {
            try
            {
                _log.Debug(() => "Starting KillProcessing thread", 1);
                
                Thread.CurrentThread.Name = "KillProcessing";

                while (true)
                {
                    var loopStart = DateTime.UtcNow;

                    try
                    {
                        _log.Debug(() => "Entering KillProcessingThreadLoop", 7);
                        if (!_pluginEnabled)
                        {
                            _log.Debug(() => $"Detected Farming-Manager not enabled. Exiting thread {Thread.CurrentThread.Name}", 6);
                            break;
                        }
                        
                        // Get all unprocessed kills
                        Queue<Kill> toProcessPlayerKills;
                        
                        lock (_killProcessingQueue)
                        {
                            if (_killProcessingQueue.Count > 0)
                            {
                                _log.Debug(() => "Preparing to lock kill processing queue to retrieve new kills.", 7);
                                lock (_killProcessingQueue)
                                {
                                    _log.Debug(() => "Unprocessed kills found. Retrieving...", 7);
                                
                                    // Copy the queue to a new queue to process
                                    toProcessPlayerKills = new Queue<Kill>(_killProcessingQueue.ToArray());
                                
                                    // Clear the old queue
                                    _killProcessingQueue.Clear();
                                }
                            }
                            else
                            {
                                _log.Debug(() => "No unprocessed kills found. Waiting for input.", 6);

                                if ((DateTime.UtcNow - loopStart).TotalMilliseconds > 1000)
                                {
                                    var start = loopStart;
                                    _log.Debug(() => $"Warning. {Thread.CurrentThread.Name} thread processing completed in {(int)(DateTime.UtcNow - start).TotalMilliseconds}ms.", 4);
                                }

                                _killProcessingWaitHandle.Reset();
                                _killProcessingWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                            
                                loopStart = DateTime.UtcNow;
                                continue;
                            }
                        }
                        
                        // Process all kills in the order they were received
                        while (toProcessPlayerKills.Count > 0)
                        {
                            if (!_pluginEnabled)
                            {
                                break;
                            }
                            
                            _log.Debug(() => "Beginning reading player kills.", 6);
                            
                            // Get the next kill to process
                            var currentKill = toProcessPlayerKills.Dequeue();
                            var damageTypeCategory = DamageTypes.None;
                            
                            if (currentKill != null && !string.IsNullOrEmpty(currentKill.DamageType))
                            {
                                damageTypeCategory = _weaponDictionary.GetDamageTypeByWeaponCode(currentKill.DamageType);
                            }

                            FPlayer victim;
                            _playersDictionary.TryGetValue(currentKill.Victim.SoldierName, out victim);
                            FPlayer killer;
                            _playersDictionary.TryGetValue(currentKill.Killer.SoldierName, out killer);
                            
                            if (victim == null || killer == null)
                            {
                                continue;
                            }

                            ProcessPlayerKill(new FKill(this)
                            {
                                Killer = killer,
                                KillerCPI = currentKill.Killer,
                                Victim = victim,
                                VictimCPI = currentKill.Victim,
                                WeaponCode = string.IsNullOrEmpty(currentKill.DamageType) ? "NoDamageType" : currentKill.DamageType,
                                WeaponDamage = damageTypeCategory,
                                TimeStamp = currentKill.TimeOfDeath,
                                UTCTimeStamp = currentKill.TimeOfDeath.ToUniversalTime(),
                                IsSuicide = currentKill.IsSuicide,
                                IsHeadshot = currentKill.Headshot,
                                IsTeamkill = currentKill.Killer.TeamID == currentKill.Victim.TeamID,
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is ThreadAbortException)
                        {
                            _log.Warn("KillProcessingThreadLoop was force aborted. Exiting thread.");
                            break;
                        }
                        _log.Exception(e);
                    }
                }
                _log.Debug(() => "Ending KillProcessing thread", 1);
                _threadManager.StopWatchdog();
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }

        #endregion ThreadLoops
        
        #region IPRoConPluginInterface
        
        public string GetPluginName()
        {
            return PluginName;
        }
        
        public string GetPluginVersion()
        {
            return _pluginVersion.ToString();
        }
        
        public string GetPluginAuthor()
        {
            return PluginAuthor;
        }
        
        public string GetPluginWebsite()
        {
            return PluginWebsite;
        }
        
        public string GetPluginDescription()
        {
            return "";
        }
        
        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            try
            {
                _serverInfo.ServerIP = strHostName + ":" + strPort;
            
                var events = new[]
                {
                    //Miscellaneous
                    "OnAccountLogin",

                    //Server
                    "OnServerInfo",
                    "OnServerType",
                    "OnRoundOver",
                    "OnRoundOverTeamScores",
                    "OnReservedSlotsList",
                    "OnVersion",

                    //Level
                    "OnLevelLoaded",

                    //Player
                    "OnPlayerSpawned",
                    "OnPlayerKilled",
                    "OnPlayerLeft",
                    "OnPlayerDisconnected"
                };
            
                RegisterEvents(GetType().Name, events);
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }
        
        public void OnPluginEnable()
        {
            try
            {
                _activatorThread = new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        Thread.CurrentThread.Name = "Activator";
                        Thread.Sleep(250);

                        // Fetch all necessary weapon names and information
                        if (_weaponDictionary.PopulateDictionaries())
                        {
                            _log.Success("Fetched engine weapon names.");
                        }
                        else
                        {
                            _log.Error("Failed to fetch engine weapon names. Farming-Manager could not be started.");
                            Disable();
                            _threadManager.StopWatchdog();
                            return;
                        }
                        
                        _pluginEnabled = true;
                        
                        InitializeWaitHandles();
                        OpenAllHandles();
                        InitializeThreads();
                        StartThreads();

                    }
                    catch (Exception e)
                    {
                        _log.Exception(e);
                    }
                    
                    _threadManager.StopWatchdog();
                }));
                
                _log.Write("^b^2ENABLED!^n^0 Starting Farming-Manager...");
                _threadManager.StartWatchdog(_activatorThread);
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }
        
        public void OnPluginDisable()
        {
            if (_finalizerThread != null && _finalizerThread.IsAlive)
            {
                return;
            }

            try
            {
                _finalizerThread = new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        Thread.CurrentThread.Name = "Finalizer";
                        _log.Write("Shutting down Farming-Manager.");
                        
                        //Disable settings
                        _pluginEnabled = false;
                        _threadsReady = false;
                        
                        //Open all handles to allow threads to exit
                        OpenAllHandles();
                        _threadManager.MonitorShutdown();

                        _log.Write("^b^1DISABLED!^n^0 Farming-Manager has been disabled.");
                    }
                    catch (Exception e)
                    {
                        _log.Exception(e);
                    }
                }));
                
                //Start the finalizer thread
                _finalizerThread.Start();
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }
        
        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            return new List<CPluginVariable>(GeneratePluginVariables());
        }

        public List<CPluginVariable> GetPluginVariables()
        {
            return new List<CPluginVariable>(GeneratePluginVariables());
        }

        private IEnumerable<CPluginVariable> GeneratePluginVariables()
        {
            // Type safe variables
            CPluginVariable StringPluginVariable(string name, string value) => new CPluginVariable(name, typeof(string), value);
            CPluginVariable StringArrayPluginVariable(string name, IEnumerable<string> value) => new CPluginVariable(name, typeof(string[]), value);
            CPluginVariable FloatPluginVariable(string name, float value) => new CPluginVariable(name, typeof(string), value.ToString("0.00", CultureInfo.InvariantCulture.NumberFormat));
            CPluginVariable IntPluginVariable(string name, int value) => new CPluginVariable(name, typeof(int), value);
            CPluginVariable BoolPluginVariable(string name, bool value) => new CPluginVariable(name, typeof(bool), value);
            CPluginVariable EnumBoolYesNoPluginVariable(string name, bool value) => new CPluginVariable(name, typeof(enumBoolYesNo), value ? enumBoolYesNo.Yes : enumBoolYesNo.No);
            
            // 1. Farming-Manager
            yield return StringPluginVariable("1. Farming-Manager|Server IP (Display)", _serverInfo.ServerIP);
            yield return StringPluginVariable("1. Farming-Manager|Plugin Version (Display)", _pluginVersion.ToString());

            // 99. Debugging
            yield return BoolPluginVariable("99. Debugging|Enable Debugging", _log.DoDebugOutPut);
            if (_log.DoDebugOutPut)
            {
                yield return IntPluginVariable("99. Debugging|Debug Level", _log.DebugLevel);
            }
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            if (strVariable == "UpdateSettings")
            {
                return;
            }
                
            if (strVariable.Contains('|')) strVariable = strVariable.Substring(strVariable.IndexOf('|') + 1);

            try
            {
                switch (strVariable)
                {
                    // 99. Debugging
                    case "Enable Debugging":
                        _log.DoDebugOutPut = bool.Parse(strValue);
                        break;
                    
                    case "Debug Level":
                        _log.DebugLevel = int.Parse(strValue);
                        break;
                }
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }
        
        #endregion IPRoConPluginInterface
        
        #region ProConPluginAPI
        
        public override void OnServerInfo(CServerInfo serverInfo)
        {
            try
            {
                if (!_pluginEnabled) return;
                
                lock (serverInfo)
                {
                    _serverInfo.SetServerInfoObject(serverInfo);
                }
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }

        public override void OnServerType(string value)
        {
            _serverInfo.ServerType = value;
        }
        
        public override void OnPlayerKilled(Kill kKillerVictimDetails)
        {
            try
            {
                if (!_pluginEnabled || !_threadsReady)
                    return;

                QueueKillForProcessing(kKillerVictimDetails);
            }
            catch (Exception e)
            {
                _log.Exception(e);
            }
        }
        
        public override void OnVersion(string serverType, string version)
        {
            //ServerInfo.ServerType = serverType;
            _serverInfo.GamePatchVersion = version;
        }
        
        #endregion ProConPluginAPI

        #region Internal Methods
        
        private void Enable()
        {
            if (Thread.CurrentThread.Name == "Finalizer")
            {
                var pluginRebootThread = new Thread(new ThreadStart(delegate
                {
                    _log.Debug(() => "Starting a reboot thread.", 5);
                    try
                    {
                        Thread.CurrentThread.Name = "PluginReboot";
                        Thread.Sleep(1000);

                        ExecuteCommand("procon.protected.plugins.enable", "FarmingManager", "True");
                    }
                    catch (Exception e)
                    {
                        _log.Exception(e);
                    }
                    _log.Debug(() => "Exiting reboot thread.", 5);
                    _threadManager.StopWatchdog();
                }));
                _threadManager.StartWatchdog(pluginRebootThread);
            }
            else
            {
                ExecuteCommand("procon.protected.plugins.enable", "FarmingManager", "True");
            }
        }
        
        private void Disable()
        {
            ExecuteCommand("procon.protected.plugins.enable", "FarmingManager", "False");
            
            // Stop threads
            _pluginEnabled = false;
            _threadsReady = false;
        }

        private void ProcessPlayerKill(FKill toProcess)
        {
            var killerName = toProcess.Killer.Name;
            var victimName = toProcess.Victim.Name;
            var weapon = _weaponDictionary.GetShortWeaponNameByCode(toProcess.WeaponCode);
            
            _log.Warn($"Player {killerName} killed {victimName} with {weapon}.");
        }

        #endregion Internal Methods
        
        #region Internal Classes
        
        public class FServer
        {
            private readonly FarmingManager _plugin;
            
            public string ServerIP;
            public string ServerName;
            public string ServerType = "UNKNOWN";
            public string GamePatchVersion;

            public CServerInfo ServerInfoObject { get; private set; }
            public DateTime ServerInfoObjectLastUpdated = DateTime.UtcNow;

            public FServer(FarmingManager plugin)
            {
                _plugin = plugin;
            }

            public void SetServerInfoObject(CServerInfo infoObject)
            {
                ServerInfoObject = infoObject;
                ServerName = infoObject.ServerName;
                ServerInfoObjectLastUpdated = DateTime.UtcNow;
            }
        }
        
        public class FPlayer
        {
            private readonly FarmingManager _plugin;
            
            public CPunkbusterInfo PBPlayerInfo;
            public List<FKill> LiveKills;
            
            public CPlayerInfo Info;
            public string Name;
            public string ClanTag;
            public string GUID;
            
            public DateTime LastKill;
            public DateTime LastDeath;
            public DateTime LastSpawn;
            
            public bool Online;
            public bool SpawnedOnce;
            
            public FPlayer(FarmingManager plugin)
            {
                _plugin = plugin;
            }

            public string GetVerboseName()
            {
                return (string.IsNullOrEmpty(ClanTag) ? "" : "[" + ClanTag + "]") + Name;
            }
        }
        
        public class FWeaponDictionary
        {
            private readonly FarmingManager _plugin;
            
            public readonly Dictionary<string, FWeapon> Weapons = new Dictionary<string, FWeapon>();
            public string AllDamageTypesEnumString;
            public string InfantryDamageTypesEnumString;
            public string AllWeaponNamesEnumString;
            public string InfantryWeaponNamesEnumString;
            
            public FWeaponDictionary(FarmingManager plugin)
            {
                _plugin = plugin;

                try
                {
                    var random = new Random(Environment.TickCount);
                    
                    //Infantry Damage Types
                    InfantryDamageTypesEnumString = string.Empty;
                    foreach (var damageType in Enum.GetValues(typeof(DamageTypes))
                                 .Cast<DamageTypes>()
                                 .Where(type => type != DamageTypes.Nonlethal &&
                                                type != DamageTypes.Suicide &&
                                                type != DamageTypes.VehicleAir &&
                                                type != DamageTypes.VehicleHeavy &&
                                                type != DamageTypes.VehicleLight &&
                                                type != DamageTypes.VehiclePersonal &&
                                                type != DamageTypes.VehicleStationary &&
                                                type != DamageTypes.VehicleTransport &&
                                                type != DamageTypes.VehicleWater)
                                 .OrderBy(type => type.ToString()))
                    {
                        if (string.IsNullOrEmpty(InfantryDamageTypesEnumString))
                        {
                            InfantryDamageTypesEnumString +=
                                "enum.InfantryDamageTypesEnum_" + random.Next(100000, 999999) + "(";
                        }
                        else
                        {
                            InfantryDamageTypesEnumString += "|";
                        }
                        
                        InfantryDamageTypesEnumString += damageType;
                    }
                    
                    InfantryDamageTypesEnumString += ")";
                    
                    //All Damage Types
                    AllDamageTypesEnumString = string.Empty;
                    foreach (var damageType in Enum.GetValues(typeof(DamageTypes))
                                 .Cast<DamageTypes>()
                                 .Where(type => type != DamageTypes.Nonlethal &&
                                                type != DamageTypes.Suicide)
                                 .OrderBy(type => type.ToString()))
                    {
                        if (string.IsNullOrEmpty(AllDamageTypesEnumString))
                        {
                            AllDamageTypesEnumString += "enum.AllDamageTypeEnum_" + random.Next(100000, 999999) + "(";
                        }
                        else
                        {
                            AllDamageTypesEnumString += "|";
                        }
                        AllDamageTypesEnumString += damageType;
                    }

                    AllDamageTypesEnumString += ")";
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
            }

            public bool PopulateDictionaries()
            {
                try
                {
                    // Damage Type and Weapon Code
                    foreach (var weapon in _plugin.GetWeaponDefines())
                    {
                        FWeapon newWeaponToAdd;
                        if (!Weapons.TryGetValue(weapon.Name, out newWeaponToAdd))
                        {
                            newWeaponToAdd = new FWeapon() 
                            {
                                Code = weapon.Name
                            };
                            Weapons[newWeaponToAdd.Code] = newWeaponToAdd;
                        }
                        newWeaponToAdd.Damage = weapon.Damage;

                        switch (newWeaponToAdd.Code)
                        {
                            //Fix invalid or wrong damage types
                            case "dlSHTR":
                                //Phantom Bow is considered a sniper rifle
                                newWeaponToAdd.Damage = DamageTypes.SniperRifle;
                                break;
                            case "U_SR338":
                                // SR338 is considered a DMR
                                newWeaponToAdd.Damage = DamageTypes.DMR;
                                break;
                            case "U_BallisticShield":
                                // Ballistic Shield is considered a melee weapon
                                newWeaponToAdd.Damage = DamageTypes.Melee;
                                break;
                        }

                        if (newWeaponToAdd.Code.ToLower() == "roadkill")
                        {
                            // Roadkill is considered as impact damage
                            newWeaponToAdd.Damage = DamageTypes.Impact;
                        }
                    }
                    
                    //Weapon Names (short and long)
                    var weaponNames = FetchFWeaponNames();
                    if (weaponNames == null)
                    {
                        return false;
                    }

                    //Loop through all weapons
                    foreach (DictionaryEntry currentWeapon in weaponNames)
                    {
                        var weaponCode = currentWeapon.Key.ToString();
                        var shortName = (string)((Hashtable)currentWeapon.Value)["readable_short"];
                        var longName = (string)((Hashtable)currentWeapon.Value)["readable_long"];
                        
                        FWeapon newWeaponToAdd;
                        if (!Weapons.TryGetValue(weaponCode, out newWeaponToAdd))
                        {
                            newWeaponToAdd = new FWeapon()
                            {
                                Code = weaponCode
                            };
                            Weapons[newWeaponToAdd.Code] = newWeaponToAdd;
                        }
                        
                        //Set the weapon names
                        newWeaponToAdd.Code = weaponCode;
                        newWeaponToAdd.ShortName = shortName;
                        newWeaponToAdd.LongName = longName;
                    }
                    
                    //Fill the weapon name enum string
                    var random = new Random(Environment.TickCount);
                    
                    InfantryWeaponNamesEnumString = string.Empty;
                    foreach (var weaponName in Weapons.Values.Where(weapon => !string.IsNullOrEmpty(weapon.ShortName) &&
                                                                              weapon.Damage != DamageTypes.None &&
                                                                              weapon.Damage != DamageTypes.Nonlethal &&
                                                                              weapon.Damage != DamageTypes.Suicide &&
                                                                              weapon.Damage != DamageTypes.VehicleAir &&
                                                                              weapon.Damage != DamageTypes.VehicleHeavy &&
                                                                              weapon.Damage != DamageTypes.VehicleLight &&
                                                                              weapon.Damage != DamageTypes.VehiclePersonal &&
                                                                              weapon.Damage != DamageTypes.VehicleStationary &&
                                                                              weapon.Damage != DamageTypes.VehicleTransport &&
                                                                              weapon.Damage != DamageTypes.VehicleWater)
                                                             .OrderBy(weapon => weapon.Damage)
                                                             .ThenBy(weapon => weapon.ShortName)
                                                             .Select(weapon => weapon.Damage.ToString() + "\\" + weapon.ShortName)
                                                             .Distinct())
                    {
                        if (string.IsNullOrEmpty(InfantryWeaponNamesEnumString))
                        {
                            InfantryWeaponNamesEnumString += "enum.InfantryWeaponNamesEnum_" + random.Next(100000, 999999) + "(None|";
                        }
                        else
                        {
                            InfantryWeaponNamesEnumString += "|";
                        }
                        InfantryWeaponNamesEnumString += weaponName;
                    }
                    InfantryWeaponNamesEnumString += ")";

                    AllWeaponNamesEnumString = string.Empty;
                    foreach (var weaponName in Weapons.Values.Where(weapon => !string.IsNullOrEmpty(weapon.ShortName) &&
                                                                              weapon.Damage != DamageTypes.None &&
                                                                              weapon.Damage != DamageTypes.Nonlethal &&
                                                                              weapon.Damage != DamageTypes.Suicide)
                                                             .OrderBy(weapon => weapon.Damage)
                                                             .ThenBy(weapon => weapon.ShortName)
                                                             .Select(weapon => weapon.Damage.ToString() + "\\" + weapon.ShortName)
                                                             .Distinct())
                    {
                        if (string.IsNullOrEmpty(AllWeaponNamesEnumString))
                        {
                            AllWeaponNamesEnumString += "enum.AllWeaponNamesEnum_" + random.Next(100000, 999999) + "(None|";
                        }
                        else
                        {
                            AllWeaponNamesEnumString += "|";
                        }
                        AllWeaponNamesEnumString += weaponName;
                    }
                    AllWeaponNamesEnumString += ")";
                    
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }

                return true;
            }
            
            private Hashtable FetchFWeaponNames()
            {
                _plugin._log.Debug(() => "Entering FetchFWeaponNames", 7);

                Hashtable weaponNames = null;
                string response;
                
                using (var webClient = new WebClient())
                {
                    _plugin._log.Debug(() => "Fetching weapon names...", 2);
                    
                    // Fetch the weapon names from the GitHub repository
                    try
                    {
                        response = webClient.DownloadString("https://raw.githubusercontent.com/PeekNotPeak/Farming-Manager/master/weapon_names.json");
                        _plugin._log.Debug(() => "Weapon names fetched successfully.", 1);
                    }
                    catch (WebException)
                    {
                        return null;
                    }
                }
                
                // Parse the JSON response
                try
                {
                    weaponNames = (Hashtable)JSON.JsonDecode(response);
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
                
                _plugin._log.Debug(() => "Exiting FetchFWeaponNames", 7);
                return weaponNames;
            }
            
            public List<string> GetWeaponCodesOfDamageType(DamageTypes damage)
            {
                try
                {
                    if (damage != DamageTypes.None)
                        return Weapons.Values.Where(weapon => weapon.Damage == damage).Select(weapon => weapon.Code)
                            .ToList();
                    
                    _plugin._log.Error("Damage type was None when fetching weapons of damage type.");
                    return new List<string>();
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
                return new List<string>();
            }
            
            public DamageTypes GetDamageTypeByWeaponCode(string weaponCode)
            {
                try
                {
                    if (string.IsNullOrEmpty(weaponCode))
                    {
                        _plugin._log.Error("WeaponCode was empty/null when fetching weapon damage type.");
                        return DamageTypes.None;
                    }
                    
                    var weapon = Weapons.Values.FirstOrDefault(dWeapon => dWeapon.Code == weaponCode);
                    if (weapon != null) return weapon.Damage;
                    
                    _plugin._log.Error($"No weapon defined for code {weaponCode} when fetching damage type. Is your DEF file updated?");
                    return DamageTypes.None;
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
                return DamageTypes.None;
            }
            
            public string GetWeaponCodeByShortName(string weaponShortName)
            {
                try
                {
                    if (!string.IsNullOrEmpty(weaponShortName))
                    {
                        foreach (var weapon in Weapons.Values.Where(weapon => weapon != null &&
                                                                              weapon.ShortName == weaponShortName))
                        {
                            return weapon.Code;
                        }
                    }
                    _plugin._log.Error($"Unable to get weapon CODE for short NAME '{weaponShortName}', in {Weapons.Count}x weapons.");
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
                return null;
            }
            
            public string GetShortWeaponNameByCode(string weaponCode)
            {
                try
                {
                    FWeapon weaponName;
                    if (string.IsNullOrEmpty(weaponCode))
                    {
                        _plugin._log.Error("WeaponCode was null when fetching weapon name");
                        return null;
                    }
                    
                    Weapons.TryGetValue(weaponCode, out weaponName);
                    if (weaponName != null) return weaponName.ShortName;
                    
                    _plugin._log.Error($"Unable to get weapon short name for code '{weaponCode}', in {Weapons.Count}x weapons.");
                    return weaponCode;
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
                return null;
            }
            
            public string GetLongWeaponNameByCode(string weaponCode)
            {
                try
                {
                    FWeapon weaponName;
                    if (string.IsNullOrEmpty(weaponCode))
                    {
                        _plugin._log.Error("WeaponCode was null when fetching weapon name");
                        return null;
                    }
                    Weapons.TryGetValue(weaponCode, out weaponName);
                    if (weaponName != null) return weaponName.ShortName;
                    
                    _plugin._log.Error($"Unable to get weapon long name for code '{weaponCode}', in {Weapons.Count}x weapons.");
                    return weaponCode;
                }
                catch (Exception e)
                {
                    _plugin._log.Exception(e);
                }
                return null;
            }
        }
        
        public class FWeapon
        {
            public DamageTypes Damage = DamageTypes.None;
            public string Code;
            public string ShortName;
            public string LongName;
        }
        
        public class FKill
        {
            private readonly FarmingManager _plugin;

            public string WeaponCode;
            public DamageTypes WeaponDamage = DamageTypes.None;
            public FPlayer Killer;
            public FPlayer Victim;
            public CPlayerInfo KillerCPI;
            public CPlayerInfo VictimCPI;
            public bool IsSuicide;
            public bool IsHeadshot;
            public bool IsTeamkill;
            public DateTime TimeStamp;
            public DateTime UTCTimeStamp;

            public FKill(FarmingManager plugin)
            {
                _plugin = plugin;
            }

            public override string ToString()
            {
                // Default values in case any are null;
                var killerString = Killer != null ? Killer.GetVerboseName() : "UnknownKiller";
                var methodString = "UnknownMethod";
                
                if (!string.IsNullOrEmpty(WeaponCode))
                {
                    methodString = _plugin._weaponDictionary.GetShortWeaponNameByCode(WeaponCode);
                }
                var victimString = Victim != null ? Victim.GetVerboseName() : "UnknownVictim";
                return killerString + " [" + methodString + "] " + victimString;
            }
        }
        
        #endregion Internal Classes
        
        #region Additional Classes
        
        public class Logger
        {
            private readonly FarmingManager _plugin;

            public Logger(FarmingManager plugin)
            {
                _plugin = plugin;
            }

            public int DebugLevel { get; set; }
            public bool DoDebugOutPut { get; set; }

            private string FormatMessage(string message, LogMessageType type)
            {
                var prefix = "[^b" + _plugin.GetPluginName() + "^n] ";
                switch (type)
                {
                    case LogMessageType.Verbose:
                        prefix += ColorGray("[VERBOSE] ");
                        break;
                    case LogMessageType.Info:
                        prefix += ColorGray("[INFO] ");
                        break;
                    case LogMessageType.Warning:
                        prefix += ColorOrange("[WARNING] ");
                        break;
                    case LogMessageType.Error:
                        prefix += ColorRed("[ERROR] ");
                        break;
                    case LogMessageType.Exception:
                        prefix += ColorRed("[EXCEPTION] ");
                        break;
                    case LogMessageType.Debug:
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

            private void ConsoleWrite(string message, LogMessageType type)
            {
                ConsoleWrite(FormatMessage(message, type));
            }

            public void Success(string message)
            {
                Write("^b^2SUCCESS^n^0: " + message);
            }

            public void Write(string message)
            {
                ConsoleWrite(message, LogMessageType.Info);
            }

            public void Warn(string message)
            {
                ConsoleWrite(message, LogMessageType.Warning);
            }

            public void Error(string message)
            {
                ConsoleWrite(message, LogMessageType.Error);
            }

            public void Exception(Exception e)
            {
                ConsoleWrite(e.ToString(), LogMessageType.Exception);
            }

            public void Debug(Func<string> messageFunction, int level)
            {
                try
                {
                    if (!DoDebugOutPut || DebugLevel < level) return;

                    if (DebugLevel >= 8)
                        ConsoleWrite("[" + level + "-" + new StackFrame(1).GetMethod().Name + "-" +
                                     (string.IsNullOrEmpty(Thread.CurrentThread.Name)
                                         ? "Main"
                                         : Thread.CurrentThread.Name) + "-" + Thread.CurrentThread.ManagedThreadId + "] " +
                                     messageFunction(), LogMessageType.Debug);

                    else ConsoleWrite(messageFunction(), LogMessageType.Debug);
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

            private enum LogMessageType
            {
                Verbose = 0,
                Info = 1,
                Warning = 2,
                Error = 3,
                Exception = 4,
                Debug = 5
            }
        }

        public class ThreadManager
        {
            private readonly Logger _log;

            public ThreadManager(Logger log)
            {
                _log = log;
            }

            private readonly Dictionary<int, Thread> _watchdogThreads = new Dictionary<int, Thread>();
            private EventWaitHandle _masterWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

            public int Count()
            {
                lock (_watchdogThreads)
                {
                    return _watchdogThreads.Count;
                }
            }

            public void Init()
            {
                _masterWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
            }

            public void Set()
            {
                _masterWaitHandle.Set();
            }

            public void Reset()
            {
                _masterWaitHandle.Reset();
            }

            public bool Wait(int duration)
            {
                return _masterWaitHandle.WaitOne(duration);
            }

            public bool Wait(TimeSpan duration)
            {
                return _masterWaitHandle.WaitOne(duration);
            }

            public void StartWatchdog(Thread fThread)
            {
                try
                {
                    fThread.Start();
                    lock (_watchdogThreads)
                    {
                        if (_watchdogThreads.ContainsKey(fThread.ManagedThreadId)) return;
                        
                        _watchdogThreads.Add(fThread.ManagedThreadId, fThread);
                        _masterWaitHandle.WaitOne(100);
                    }
                }
                catch (Exception e)
                {
                    _log.Exception(e);
                }
            }

            public void StopWatchdog()
            {
                try
                {
                    lock (_watchdogThreads)
                    {
                        _watchdogThreads.Remove(Thread.CurrentThread.ManagedThreadId);
                    }
                }
                catch (Exception e)
                {
                    _log.Exception(e);
                }
            }

            public void Prune()
            {
                try
                {
                    lock (_watchdogThreads)
                    {
                        var threads = _watchdogThreads.ToList();
                        foreach (var deadThreadId in threads.Where(threadPair =>
                                         threadPair.Value == null || !threadPair.Value.IsAlive)
                                     .Select(threadPair =>
                                         threadPair.Value == null ? threadPair.Key : threadPair.Value.ManagedThreadId))
                            _watchdogThreads.Remove(deadThreadId);
                    }
                }
                catch (Exception e)
                {
                    _log.Exception(e);
                }
            }

            public void Monitor()
            {
                try
                {
                    lock (_watchdogThreads)
                    {
                        if (_watchdogThreads.Count < 20) return;

                        var aliveThreads = _watchdogThreads.Values.ToList().Aggregate("",
                            (current, value) => current + value.Name + "[" + value.ManagedThreadId + "] ");
                        _log.Warn("Thread warning: " + aliveThreads);
                    }
                }
                catch (Exception e)
                {
                    _log.Exception(e);
                }
            }

            public bool IsAlive(string threadName)
            {
                try
                {
                    lock (_watchdogThreads)
                    {
                        return _watchdogThreads.Values.ToList().Any(aThread => aThread != null &&
                                                                               aThread.IsAlive &&
                                                                               aThread.Name == threadName);
                    }
                }
                catch (Exception e)
                {
                    _log.Exception(e);
                }

                return false;
            }

            public void MonitorShutdown()
            {
                try
                {
                    //Check to make sure all threads have completed and stopped
                    var attempts = 0;
                    var alive = false;
                    do
                    {
                        attempts++;
                        Thread.Sleep(500);
                        alive = false;
                        var aliveThreads = "";
                        lock (_watchdogThreads)
                        {
                            foreach (var deadThreadId in _watchdogThreads.Values.ToList()
                                         .Where(thread => !thread.IsAlive).Select(thread => thread.ManagedThreadId)
                                         .ToList()) _watchdogThreads.Remove(deadThreadId);
                            foreach (var aliveThread in _watchdogThreads.Values.ToList())
                            {
                                alive = true;
                                aliveThreads += aliveThread.Name + "[" + aliveThread.ManagedThreadId + "] ";
                            }
                        }

                        if (aliveThreads.Length <= 0) continue;
                        
                        if (attempts > 20)
                            _log.Warn("Threads still exiting: " + aliveThreads);
                        else
                            _log.Debug(() => "Threads still exiting: " + aliveThreads, 2);
                    } while (alive);
                }
                catch (Exception e)
                {
                    _log.Exception(e);
                }
            }
        }
        
        #endregion Additional Classes
    }
}