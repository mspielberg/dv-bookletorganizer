using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace DvMod.BookletOrganizer
{
    [BepInPlugin(GUID, "BookletOrganizer", Version)]
    public class Main : BaseUnityPlugin
    {
        private const string GUID = "com.github.mspielberg.dv-bookletorganizer";
        private const string Version = "1.0.0";
        
        private static Main instance = null!;
        private Harmony? harmony;
        private ConfigEntry<bool> enableLogging = null!;

        private void Awake()
        {
            if (instance != null)
            {
                Logger.LogFatal($"{Info.Metadata.Name} is already loaded!");
                Destroy(this);
                return;
            }

            instance = this;

            enableLogging = Config.Bind("Debug", "Enable logging", false, "Whether to enable debug logging");

            harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        public static void DebugLog(Func<string> message)
        {
            if (instance.enableLogging.Value)
                instance.Logger.LogDebug(message());
        }
    }
}
