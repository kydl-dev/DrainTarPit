using System.IO;
using System.Reflection;
using BepInEx;
using DrainTarPit.Building;
using DrainTarPit.Debug;
using DrainTarPit.Effects;
using Kydl.Common;
using DrainTarPit.TextLocal;
using DrainTarPit.Visuals;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace DrainTarPit
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.kydldev.DrainTarPit";
        public const string ModName = "DrainTarPit";
        public const string ModVersion = "1.0.0";
        public const string BundleName = "draintarpit";

        internal static Plugin Instance;
        internal static AssetBundle Bundle;

        void Awake()
        {
            Instance = this;
            ModLog.Init(Logger, ModName, () => ModSettings.DebugLog != null && ModSettings.DebugLog.Value);                    // first line of the session: "{date&time} - NEW LOG SESSION STARTS HERE"
            ModLog.Info(ModName + " " + ModVersion + " loaded (dll built " + File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm:ss") + ")");
            ModSettings.Bind(Config);
            WatchConfigFile();
            Loc.Register();

            Bundle = LoadBundle();
            if (Bundle == null) { ModLog.Error("Could not load asset bundle '" + BundleName + "'."); return; }
            foreach (var n in Bundle.GetAllAssetNames()) ModLog.Verbose("bundle asset: " + n);

            PumpFx.Preload();                       // pump_up / oil_pumped sounds (no-op on a dedicated server)
            PrefabManager.OnVanillaPrefabsAvailable += PieceRegistrar.RegisterAll;
            CommandManager.Instance.AddConsoleCommand(new DtpCommands());
            new Harmony(ModGuid).PatchAll(Assembly.GetExecutingAssembly());
            Ui.UiLog.VerboseSink = s => ModLog.Verbose(s);     // the shared UI code (Unity project) logs through the mod's log, debug lines only with DebugLog
            Ui.UiLog.WarnSink = s => ModLog.Warn(s);
            Ui.UiLog.ErrorSink = s => ModLog.Error(s);
            Ui.UiInputLock.Install();               // T-023: the character does not move while a menu of the user's mods (UI Toolkit) is open
            ModLog.Info("WaterAndTarPools " + (PoolHooks.PoolsInstalled ? "is loaded (" + PoolHooks.PoolsMod + ")" : "is loaded after this mod or not installed (pools / water come with it)"));
        }

        // 1) embedded resource (read fully into memory: LoadFromStream needs the stream to stay open, which breaks
        //    once the stream is disposed -> "ManagedStream object must be readable"), 2) file next to the DLL
        static AssetBundle LoadBundle()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (res != BundleName && !res.EndsWith("." + BundleName)) continue;
                using (var stream = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream())
                {
                    if (stream != null) stream.CopyTo(ms);
                    var b = AssetBundle.LoadFromMemory(ms.ToArray());
                    if (b != null) return b;
                }
            }
            var path = Path.Combine(Path.GetDirectoryName(asm.Location)!, BundleName);
            return File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
        }

        // --- config hot reload ---------------------------------------------------------------------------------------------
        // BepInEx does not watch the .cfg file: editing it on disk changed nothing until the next start (only the in-game config manager raises
        // SettingChanged). The watcher notices the save, the reload itself runs on the main thread a moment later (editors save in several writes).
        FileSystemWatcher _cfgWatcher;
        volatile bool _cfgFileChanged;
        float _cfgReloadAt;
        int _cfgRetries;

        void WatchConfigFile()
        {
            try
            {
                var path = Config.ConfigFilePath;
                _cfgWatcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                _cfgWatcher.Changed += (s, e) => _cfgFileChanged = true;
                _cfgWatcher.Created += (s, e) => _cfgFileChanged = true;
                _cfgWatcher.Renamed += (s, e) => _cfgFileChanged = true;     // some editors save to a temp file and rename it over the config
                _cfgWatcher.EnableRaisingEvents = true;
            }
            catch (System.Exception e) { ModLog.Warn("Config file watcher not started (edits to the .cfg need a restart): " + e.Message); }
        }

        void TickConfigReload()
        {
            if (_cfgFileChanged) { _cfgFileChanged = false; _cfgReloadAt = Time.realtimeSinceStartup + 0.5f; _cfgRetries = 0; }
            if (_cfgReloadAt <= 0f || Time.realtimeSinceStartup < _cfgReloadAt) return;
            _cfgReloadAt = 0f;
            try
            {
                if (!File.Exists(Config.ConfigFilePath)) return;
                Config.Reload();                      // raises SettingChanged for every entry whose value differs (HoseStyle / HoseOpacity -> MaterialFixer.RequestRefresh)
                ModLog.Info("Config file changed on disk - reloaded.");
            }
            catch (System.Exception e)
            {
                if (++_cfgRetries <= 3) _cfgReloadAt = Time.realtimeSinceStartup + 1f;     // file still locked by the editor: try again
                else ModLog.Warn("Config reload failed: " + e.Message);
            }
        }

        void OnDestroy()
        {
            if (_cfgWatcher != null) { _cfgWatcher.EnableRaisingEvents = false; _cfgWatcher.Dispose(); _cfgWatcher = null; }
        }

        void Update()
        {
            TickConfigReload();
            MaterialFixer.Tick();                    // applies a changed [Visuals] HoseStyle / HoseOpacity (to hoses built from now on) on the main thread
        }

        internal void RunCoroutine(System.Collections.IEnumerator routine) { StartCoroutine(routine); }
    }
}