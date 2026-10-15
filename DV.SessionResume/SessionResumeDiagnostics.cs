using System;
using System.Collections.Generic;
using System.Text;
using DV;
using UnityEngine;

namespace SessionResume
{
    internal static class SessionResumeDiagnostics
    {
        private const int MaxLogEntries = 20;
        private static readonly List<string> recentIssues = new List<string>();
        private static readonly object lockObject = new object();

        // Hook status tracking
        internal static bool HookSaveLoadControllerFound;
        internal static int HookSaveLoadControllerHits;
        internal static bool HookLauncherControllerFound;
        internal static int HookLauncherControllerHits;
        internal static bool HookLoadingScreenFound;
        internal static int HookLoadingScreenHits;
        internal static bool HookWorldStreamingFound;
        internal static int HookWorldStreamingHits;
        internal static bool HookSaveGameManagerFound;
        internal static int HookSaveGameManagerHits;

        internal static void RecordIssue(string level, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            string entry = "[" + DateTime.UtcNow.ToString("HH:mm:ss") + "] [" + level + "] " + message;
            lock (lockObject)
            {
                if (recentIssues.Count >= MaxLogEntries)
                {
                    recentIssues.RemoveAt(0);
                }
                recentIssues.Add(entry);
            }
        }

        internal static List<string> GetRecentIssues()
        {
            lock (lockObject)
            {
                return new List<string>(recentIssues);
            }
        }

        internal static void ClearIssues()
        {
            lock (lockObject)
            {
                recentIssues.Clear();
            }
        }

        private static string SafeGetUnityVersion()
        {
            try
            {
                System.Reflection.PropertyInfo prop = typeof(Application).GetProperty("unityVersion",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null)
                {
                    return (string)prop.GetValue(null, null) ?? "unknown";
                }
            }
            catch { }
            return "unknown";
        }

        private static string SafeGetScreenResolution()
        {
            try
            {
                System.Reflection.PropertyInfo wProp = typeof(Screen).GetProperty("width",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                System.Reflection.PropertyInfo hProp = typeof(Screen).GetProperty("height",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                System.Reflection.PropertyInfo dpiProp = typeof(Screen).GetProperty("dpi",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (wProp != null && hProp != null)
                {
                    int w = (int)wProp.GetValue(null, null);
                    int h = (int)hProp.GetValue(null, null);
                    float dpi = dpiProp != null ? (float)dpiProp.GetValue(null, null) : 0f;
                    float aspect = (float)w / Mathf.Max(1, h);
                    return w + "x" + h + " (aspect " + aspect.ToString("0.00") +
                           ", dpi " + (dpi > 0 ? dpi.ToString("0") : "unknown") + ")";
                }
            }
            catch { }
            return "unknown";
        }

        internal static string BuildDiagnosticReport()
        {
            SessionResumeData data = SessionResumePersistence.Current ?? new SessionResumeData();
            SessionResumeSettings settings = Main.Settings ?? new SessionResumeSettings();
            StringBuilder sb = new StringBuilder();

            string modVersion = Main.ModEntry == null || Main.ModEntry.Info == null
                ? "1.0.0"
                : Main.ModEntry.Info.Version;

            string unityVersion = SafeGetUnityVersion();
            string resDisplay = SafeGetScreenResolution();

            sb.AppendLine("=== Session Resume Diagnostic Report ===");
            sb.AppendLine("Mod Version: " + modVersion);
            sb.AppendLine("Game Build: " + BuildInfo.BUILD_VERSION_STR);
            sb.AppendLine("Unity Version: " + unityVersion);
            sb.AppendLine("Screen Resolution: " + resDisplay);
            sb.AppendLine("Mod Enabled: " + Main.Enabled);
            sb.AppendLine("Data Schema Version: v" + SessionResumeData.CurrentVersion);
            sb.AppendLine("World Persistence Initialized: " + SessionResumePersistence.IsInitialized);
            sb.AppendLine("Loading Context Pending: " + SaveSelectionPreview.HasPendingLoadingRequest);

            sb.AppendLine();
            sb.AppendLine("--- Settings ---");
            sb.AppendLine("Toggle Key: " + settings.ToggleKeyDisplay);
            sb.AppendLine("UI Scale: " + (settings.UiScale * 100f).ToString("0") + "%");
            sb.AppendLine("Panel Opacity: " + (settings.PanelOpacity * 100f).ToString("0") + "%");
            sb.AppendLine("Main Menu Context Panels: Disabled");
            sb.AppendLine("Show on Loading Screen: " + settings.ShowOnLoadingScreen);
            sb.AppendLine("Show on World Load: " + settings.ShowOnWorldLoad);
            sb.AppendLine("Startup Duration: " + (settings.StartupUntilDismissed
                ? "Until dismissed"
                : Mathf.RoundToInt(settings.StartupCardSeconds) + " seconds"));
            sb.AppendLine("Max Displayed Jobs: " + settings.MaxDisplayedJobs);
            sb.AppendLine("Max Displayed Vehicles: " + settings.MaxDisplayedVehicles);
            sb.AppendLine("Hide Empty Sections: " + settings.HideEmptySections);
            sb.AppendLine("Verbose Logging: " + settings.VerboseLogging);

            sb.AppendLine();
            sb.AppendLine("--- Menu & Loading Hooks ---");
            sb.AppendLine("SaveLoadController (Sessions Menu): " +
                          (HookSaveLoadControllerFound ? "Active (calls: " + HookSaveLoadControllerHits + ")" : "Not detected"));
            sb.AppendLine("LauncherController (Continue Menu): " +
                          (HookLauncherControllerFound ? "Active (calls: " + HookLauncherControllerHits + ")" : "Not detected"));
            sb.AppendLine("LoadingScreenDebug (Loading UI): " +
                          (HookLoadingScreenFound ? "Active (calls: " + HookLoadingScreenHits + ")" : "Not detected"));
            sb.AppendLine("WorldStreamingInit (World Finished): " +
                          (HookWorldStreamingFound ? "Active (calls: " + HookWorldStreamingHits + ")" : "Not detected"));
            sb.AppendLine("SaveGameManager (Save Sync): " +
                          (HookSaveGameManagerFound ? "Active (calls: " + HookSaveGameManagerHits + ")" : "Not detected"));

            sb.AppendLine();
            sb.AppendLine("--- Recorded Context Counts ---");
            string locoDisplay = string.IsNullOrWhiteSpace(data.LastLocomotive.DisplayId)
                ? "None recorded"
                : data.LastLocomotive.DisplayId + " (" + (data.LastLocomotive.Type ?? "unknown") + ")";
            sb.AppendLine("Last Locomotive: " + locoDisplay);
            sb.AppendLine("Accepted Jobs: " + data.Jobs.Count);
            sb.AppendLine("Recently Detached Wagons: " + data.DetachedWagons.Count +
                          " (" + data.DetachedWagons.FindAll(x => x.Pinned).Count + " pinned)");
            sb.AppendLine("Parked Equipment: " + data.ParkedEquipment.Count +
                          " (" + data.ParkedEquipment.FindAll(x => x.Pinned).Count + " pinned)");
            sb.AppendLine("Tracked Consist: " + data.TrackedConsist.Count);
            sb.AppendLine("Known Locomotives: " + data.KnownLocomotives.Count);
            sb.AppendLine("Manual Note Override: " + (!string.IsNullOrWhiteSpace(data.Note) ? "Yes" : "No"));
            sb.AppendLine("Auto Suggestion: " + (!string.IsNullOrWhiteSpace(data.AutomaticNote) ? "Yes" : "No"));
            sb.AppendLine("Last Updated (UTC): " + (string.IsNullOrWhiteSpace(data.UpdatedUtc) ? "Never" : data.UpdatedUtc));

            sb.AppendLine();
            sb.AppendLine("--- Recent Issues & Warnings ---");
            List<string> issues = GetRecentIssues();
            if (issues.Count == 0 && string.IsNullOrWhiteSpace(SessionResumePersistence.LastIssue))
            {
                sb.AppendLine("No issues or warnings recorded.");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(SessionResumePersistence.LastIssue))
                {
                    sb.AppendLine("Last Persistence Issue: " + SessionResumePersistence.LastIssue);
                }
                foreach (string issue in issues)
                {
                    sb.AppendLine(issue);
                }
            }

            return sb.ToString();
        }
    }
}
