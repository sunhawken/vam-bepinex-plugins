using System;
using System.Collections.Generic;
using UnityEngine;

namespace BepInEx
{
	[AttributeUsage(AttributeTargets.Class)]
	public class BepInPlugin : Attribute
	{
		public string GUID { get; set; } public string Name { get; set; } public Version Version { get; set; }
		public BepInPlugin(string guid, string name, string version) { GUID = guid; Name = name; Version = new Version(version); }
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public class BepInDependency : Attribute
	{
		[Flags] public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }
		public BepInDependency(string guid, DependencyFlags f) { }
		public BepInDependency(string guid) { }
	}

	[AttributeUsage(AttributeTargets.Class)] public class BepInProcess : Attribute { public BepInProcess(string n) { } }

	public static class Paths
	{
		public static string GameRootPath { get; set; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uisim_game");
		public static string BepInExRootPath => System.IO.Path.Combine(GameRootPath, "BepInEx");
		public static string ConfigPath => System.IO.Path.Combine(BepInExRootPath, "config");
		public static string PluginPath => System.IO.Path.Combine(BepInExRootPath, "plugins");
		public static string ManagedPath => System.IO.Path.Combine(GameRootPath, "VaM_Data", "Managed");
	}

	public abstract class BaseUnityPlugin : MonoBehaviour
	{
		public BepInEx.Configuration.ConfigFile Config { get; }
		protected BepInEx.Logging.ManualLogSource Logger { get; }
		protected BaseUnityPlugin()
		{
			Config = new BepInEx.Configuration.ConfigFile();
			Logger = new BepInEx.Logging.ManualLogSource(GetType().Name);
		}
	}
}

namespace BepInEx.Logging
{
	public class ManualLogSource
	{
		public string Name;
		public static List<string> Lines = new List<string>();
		public ManualLogSource(string n) { Name = n; }
		public void LogInfo(object o) { Lines.Add("[Info:" + Name + "] " + o); }
		public void LogWarning(object o) { Lines.Add("[Warn:" + Name + "] " + o); }
		public void LogError(object o) { Lines.Add("[Error:" + Name + "] " + o); }
		public void LogMessage(object o) { Lines.Add("[Msg:" + Name + "] " + o); }
		public void LogDebug(object o) { }
	}
}

namespace BepInEx.Configuration
{
	public abstract class ConfigEntryBase { public ConfigFile ConfigFile; public string Section, Key; }

	public class ConfigEntry<T> : ConfigEntryBase
	{
		private T val;
		public event EventHandler SettingChanged;
		public T DefaultValue;
		public ConfigEntry(T v) { val = v; DefaultValue = v; }
		public T Value
		{
			get => val;
			set { if (!Equals(val, value)) { val = value; SettingChanged?.Invoke(this, EventArgs.Empty); } }
		}
	}

	public class ConfigDescription { public ConfigDescription(string d) { } }

	public class ConfigFile
	{
		public static Dictionary<string, string> Overrides = new Dictionary<string, string>();   // "Section.Key" -> text, set by the simulator
		private Dictionary<string, object> entries = new Dictionary<string, object>();
		public ConfigFile() { }
		public ConfigEntry<T> Bind<T>(string section, string key, T def, string description) => Bind(section, key, def);
		public ConfigEntry<T> Bind<T>(string section, string key, T def, ConfigDescription d) => Bind(section, key, def);
		public ConfigEntry<T> Bind<T>(string section, string key, T def)
		{
			string id = section + "." + key;
			if (entries.TryGetValue(id, out var o)) return (ConfigEntry<T>)o;
			T v = def;
			if (Overrides.TryGetValue(id, out var s))
			{
				try { v = (T)Convert.ChangeType(s, typeof(T), System.Globalization.CultureInfo.InvariantCulture); } catch { }
			}
			var e = new ConfigEntry<T>(v) { ConfigFile = this, Section = section, Key = key };
			entries[id] = e;
			return e;
		}
		public void Save() { }
		public bool SaveOnConfigSet { get; set; }
	}
}
