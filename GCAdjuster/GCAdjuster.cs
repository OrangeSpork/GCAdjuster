using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace GCAdjuster
{
    [BepInPlugin(GUID, PluginName, Version)]
    public class GCAdjuster : BaseUnityPlugin
    {
        public const string GUID = "orange.spork.gcadjuster";
        public const string PluginName = "GCAdjuster";
        public const string Version = "1.0.0";

        public static ConfigEntry<bool> GCAdjusterEnabled { get; set; }
        public static ConfigEntry<int> GCFullCollectionMark { get; set; }
        public static ConfigEntry<int> GCFullCollectionCooldown { get; set; }
        public static ConfigEntry<int> GCZeroGenMark { get; set; }
        public static ConfigEntry<int> GCZeroGenMaxWait { get; set; }
        public static ConfigEntry<int> GCFullGenMaxWait { get; set; }

        ManualLogSource Log => Logger;

        public GCAdjuster()
        {
            GCAdjusterEnabled = Config.Bind("Options", "GC Adjuster Enabled", true);
            GCFullCollectionMark = Config.Bind("Options", "Full GC Collection Mark (MB)", 12000, new ConfigDescription("Full GC when used memory hits this"));
            GCFullCollectionCooldown = Config.Bind("Options", "Full GC Cooldown (S)", 10, new ConfigDescription("Pause between Full GC runs"));
            GCZeroGenMark = Config.Bind("Options", "Zero Gen GC Collection Mark (MB)", 4000, new ConfigDescription("Zero Gen GC when used memory allocates this above last collection amount"));
            GCZeroGenMaxWait = Config.Bind("Options", "Max Wait Between Zero Gen Collections (Secs)", 300, new ConfigDescription("Maximum wait for Zero Gen GC Collection, -1 for no max wait."));
            GCFullGenMaxWait = Config.Bind("Options", "Max Wait Between Full Gen Collections (Secs)", 900, new ConfigDescription("Maximum wait for Full Gen GC Collection, -1 for no max wait."));
            Config.SettingChanged += SettingsChanged;
        }

        void SettingsChanged(object sender, SettingChangedEventArgs args)
        {
            fullGCMark = GCFullCollectionMark.Value * 1024L * 1024L;
            zgGCMark = GCZeroGenMark.Value * 1024L * 1024L;
            Log.LogInfo($"Current GC Mode: {GarbageCollector.GCMode} Full GC Point: {fullGCMark / 1024L / 1024L } ZG Amount: {zgGCMark / 1024L / 1024L}");
        }

        void OnDestroy()
        {
            GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
            Log.LogInfo($"Terminating GCAdjuster, restoring automated control");
        }

        void Start()
        {
            fullGCMark = GCFullCollectionMark.Value * 1024L * 1024L;
            zgGCMark = GCZeroGenMark.Value * 1024L * 1024L;
            Log.LogInfo($"Current GC Mode: {GarbageCollector.GCMode} Full GC Point: {fullGCMark / 1024L / 1024L } ZG Amount: {zgGCMark / 1024L / 1024L}");
            GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
            lastGCRun = Time.realtimeSinceStartup;
        }

        long fullGCMark = 0;
        long zgGCMark = 0;

        long nextCollectAt = 0;

        float lastGCRun = 0;
        float lastFullGCRun = 0;

        void Update()
        {
            long mem = Profiler.GetMonoUsedSizeLong();

            if (!GCAdjusterEnabled.Value && GarbageCollector.GCMode == GarbageCollector.Mode.Disabled)
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                return;
            }

            if (nextCollectAt == 0)
            {
                nextCollectAt = mem + zgGCMark;
                Log.LogInfo($"Next ZeroGen Collection at {nextCollectAt / 1024L / 1024L}");
            }

            if (mem > fullGCMark && (lastFullGCRun + GCFullCollectionCooldown.Value) < Time.realtimeSinceStartup)
            {
                Log.LogInfo($"GC Full {mem / 1024L / 1024L} > {fullGCMark / 1024L / 1024L}");
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                System.GC.Collect(2, GCCollectionMode.Forced, false, false);
                lastFullGCRun = Time.realtimeSinceStartup;
                nextCollectAt = 0;
            }
            else if (mem > nextCollectAt)
            {
                Log.LogInfo($"GC ZeroGen {mem / 1024L / 1024L } > {nextCollectAt / 1024L / 1024L}");
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                System.GC.Collect(0, GCCollectionMode.Forced, false, false);
                lastGCRun = Time.realtimeSinceStartup;
                nextCollectAt = 0;
            }
            else if (GCFullGenMaxWait.Value >= 0 && (lastGCRun + GCFullGenMaxWait.Value) < Time.realtimeSinceStartup)
            {
                Log.LogInfo($"Full GC ZeroGen");
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                System.GC.Collect(2, GCCollectionMode.Forced, false, false);
                lastFullGCRun = Time.realtimeSinceStartup;
                nextCollectAt = 0;
            }
            else if (GCZeroGenMaxWait.Value >= 0 && (lastGCRun + GCZeroGenMaxWait.Value) < Time.realtimeSinceStartup )
            {
                Log.LogInfo($"Periodic GC ZeroGen");
                GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
                System.GC.Collect(0, GCCollectionMode.Forced, false, false);
                lastGCRun = Time.realtimeSinceStartup;
                nextCollectAt = 0;
            }            
            else if (GarbageCollector.GCMode == GarbageCollector.Mode.Enabled)
            {
                GarbageCollector.GCMode = GarbageCollector.Mode.Disabled;
                Log.LogInfo("GC Offline");
            }
        }
    }
}
