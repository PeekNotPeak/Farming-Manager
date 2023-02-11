using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using PRoCon.Core;
using PRoCon.Core.Plugin;

#region namespace PRoConEvents

namespace PRoConEvents
{
    #region class FarmingManager

    public class FarmingManager : PRoConPluginAPI, IPRoConPluginInterface
    {
        #region Global Variables

        /* ===== Miscellaneous ===== */
        private const string StrPluginName = "Farming-Manager";
        private const string StrPluginVersion = "0.1.0";
        private const string StrPluginAuthor = "PeekNotPeak";
        private const string StrPluginWebsite = "github.com/PeekNotPeak/Farming-Manager";
        
        /* ===== 1. Farming-Manager ===== */
        private bool _blnIsPluginEnabled;
        
        /* ===== 99. Debugging ===== */
        public readonly Logger Logger;
        
        #endregion Global Variables

        #region Constructor

        public FarmingManager()
        {
            /* ===== 1. FarmingManager ===== */
            _blnIsPluginEnabled = false;
            
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
                "OnServerInfo"
            };

            RegisterEvents(GetType().Name, events);
        }

        public void OnPluginEnable()
        {
            Logger.Write($"Plugin enabled. Running on version {GetPluginVersion()}.");
        }

        public void OnPluginDisable()
        {
            Logger.Write("Plugin successfully shut down.");
        }
        
        /* ==== Variable Handling ==== */

        private IEnumerable<CPluginVariable> PluginVariables()
        {
            //Type safe plugin variable creation
            Func<string, string, CPluginVariable> stringPluginVariable = (name, value) => new CPluginVariable(name, typeof(string), value);
            Func<string, IEnumerable<string>, CPluginVariable> stringArrayPluginVariable = (name, value) => new CPluginVariable(name, typeof(string[]), value);
            Func<string, float, CPluginVariable> floatPluginVariable = (name, value) => new CPluginVariable(name, typeof(string), value.ToString("0.00", CultureInfo.InvariantCulture.NumberFormat));
            Func<string, int, CPluginVariable> intPluginVariable = (name, value) => new CPluginVariable(name, typeof(int), value);
            Func<string, bool, CPluginVariable> boolPluginVariable = (name, value) => new CPluginVariable(name, typeof(bool), value);
            
            /* ===== 1. Farming-Manager ===== */
            yield return boolPluginVariable("1. Farming-Manager|Activate the plugin?", _blnIsPluginEnabled);
            
            /* ===== 99. Debugging ===== */
            yield return boolPluginVariable("99. Debugging|Enable debug output?", Logger.BoolDoDebugOutPut);
            if (Logger.BoolDoDebugOutPut)
            {
                yield return intPluginVariable("99. Debugging|Debug level", Logger.IntDebugLevel);
            }
        }

        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            return new List<CPluginVariable>(PluginVariables());
        }

        public List<CPluginVariable> GetPluginVariables()
        {
            return new List<CPluginVariable>(PluginVariables());
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            Logger.Debug(() => $"Setting variable '{strVariable}' to value '{strValue}'", 7);
            try
            {
                switch (strVariable)
                {
                    /* ===== 1. Farming-Manager ===== */
                    case "Activate the plugin?":
                        _blnIsPluginEnabled = bool.Parse(strValue);
                        break;
                    
                    /* ===== 99. Debugging ===== */
                    case "Enable debug output?":
                        Logger.BoolDoDebugOutPut = bool.Parse(strValue);
                        break;
                    case "Debug level":
                        Logger.IntDebugLevel = int.Parse(strValue);
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Exception(e);
            }
            finally
            {
                //TODO: Add variable validation
            }
        }

        #endregion IPRoConPluginInterface
    }

    #endregion class FarmingManager

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
}

#endregion namespace PRoConEvents