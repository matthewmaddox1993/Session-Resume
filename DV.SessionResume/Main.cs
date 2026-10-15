using System;
using System.Reflection;
using System.Text;
using DV;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("run_tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SessionResume.Tests")]

namespace SessionResume
{
    public static class Main
    {
        private static readonly Action WorldLoadedHandler = OnWorldLoaded;
        private static GameObject host;
        private static GameObject previewHost;
        private static SessionResumeController controller;
        private static Harmony harmony;
        private static bool harmonyPatched;
        private static float resetCurrentConfirmationUntil;
        private static float resetAllConfirmationUntil;

        internal static UnityModManager.ModEntry ModEntry { get; private set; }
        internal static SessionResumeSettings Settings { get; private set; } = new SessionResumeSettings();
        internal static bool Enabled { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Settings = UnityModManager.ModSettings.Load<SessionResumeSettings>(modEntry) ??
                       new SessionResumeSettings();
            Settings.Clamp();

            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            modEntry.OnGUI = DrawSettings;
            modEntry.OnSaveGUI = entry => Settings.Save(entry);
            harmony = new Harmony(modEntry.Info.Id);

            string version = modEntry.Info == null ? "1.0.0" : modEntry.Info.Version;
            Log("Session Resume " + version + " loaded for Derail Valley 99.7 (detected " +
                BuildInfo.BUILD_VERSION_STR + ").");
            if (BuildInfo.BUILD_VERSION_MAJOR != 99)
            {
                LogWarning("This release was tested against Derail Valley Build 99.7.");
            }

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            Enabled = enabled;
            try
            {
                if (enabled)
                {
                    if (!harmonyPatched)
                    {
                        harmony.PatchAll(Assembly.GetExecutingAssembly());
                        harmonyPatched = true;
                    }
                    EnsurePreviewHost();
                    EventHelper.SubscribeStatic(typeof(WorldStreamingInit), "LoadingFinished", WorldLoadedHandler);
                    SessionResumeDiagnostics.HookWorldStreamingFound = true;
                    UnloadWatcher.UnloadRequested += OnWorldUnloaded;
                    if (WorldStreamingInit.IsStreamingDone) OnWorldLoaded();
                }
                else
                {
                    EventHelper.UnsubscribeStatic(typeof(WorldStreamingInit), "LoadingFinished", WorldLoadedHandler);
                    UnloadWatcher.UnloadRequested -= OnWorldUnloaded;
                    Unpatch();
                    Teardown();
                    DestroyPreviewHost();
                }

                return true;
            }
            catch (Exception exception)
            {
                Enabled = false;
                modEntry.Logger.LogException("Session Resume failed to toggle:", exception);
                try
                {
                    EventHelper.UnsubscribeStatic(typeof(WorldStreamingInit), "LoadingFinished", WorldLoadedHandler);
                    UnloadWatcher.UnloadRequested -= OnWorldUnloaded;
                }
                catch
                {
                    // Preserve the original startup error.
                }
                Unpatch();
                Teardown();
                DestroyPreviewHost();
                return false;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Enabled = false;
            EventHelper.UnsubscribeStatic(typeof(WorldStreamingInit), "LoadingFinished", WorldLoadedHandler);
            UnloadWatcher.UnloadRequested -= OnWorldUnloaded;
            Unpatch();
            Teardown();
            DestroyPreviewHost();
            Settings.Save(modEntry);
            return true;
        }

        private static void OnWorldLoaded()
        {
            if (!Enabled || host != null) return;
            SessionResumeDiagnostics.HookWorldStreamingHits++;
            SaveSelectionPreview.CompleteLoadingPresentation();
            try
            {
                SessionResumePersistence.Initialize();
                host = new GameObject("[Session Resume]");
                UnityEngine.Object.DontDestroyOnLoad(host);
                controller = host.AddComponent<SessionResumeController>();
                Log("Context loaded from the active save.");
            }
            catch (Exception exception)
            {
                LogError("World initialization failed: " + exception);
                Teardown();
            }
        }

        private static void OnWorldUnloaded()
        {
            Teardown();
        }

        private static void Teardown()
        {
            if (host != null) UnityEngine.Object.Destroy(host);
            host = null;
            controller = null;
            SessionResumePersistence.Shutdown();
        }

        private static void EnsurePreviewHost()
        {
            if (previewHost != null) return;
            previewHost = new GameObject("[Session Resume Loading UI]");
            UnityEngine.Object.DontDestroyOnLoad(previewHost);
            previewHost.AddComponent<SessionResumeLoadingPresenter>();
        }

        private static void DestroyPreviewHost()
        {
            if (previewHost != null) UnityEngine.Object.Destroy(previewHost);
            previewHost = null;
        }

        private static void DrawSettings(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Session Resume - working context stored inside each Derail Valley save");
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Open dashboard:", GUILayout.Width(140f));
            UnityModManager.UI.DrawKeybindingSmart(Settings.ToggleKey, "Open dashboard");
            if (GUILayout.Button("Reset to F8", GUILayout.Width(100f)))
            {
                Settings.ToggleKey = new KeyBinding { keyCode = KeyCode.F8 };
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            Settings.ShowOnLoadingScreen = GUILayout.Toggle(Settings.ShowOnLoadingScreen,
                "Show context panel while the selected save is loading");
            Settings.ShowOnWorldLoad = GUILayout.Toggle(Settings.ShowOnWorldLoad,
                "Show in-world startup summary after loading a save");

            GUILayout.Space(6f);
            Settings.StartupUntilDismissed = GUILayout.Toggle(Settings.StartupUntilDismissed,
                "Keep startup summary visible until dismissed");

            if (!Settings.StartupUntilDismissed)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Startup card duration", GUILayout.Width(180f));
                Settings.StartupCardSeconds = GUILayout.HorizontalSlider(Settings.StartupCardSeconds, 5f, 120f,
                    GUILayout.Width(220f));
                GUILayout.Label(Mathf.RoundToInt(Settings.StartupCardSeconds) + " seconds", GUILayout.Width(90f));
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Panel scale", GUILayout.Width(180f));
            Settings.UiScale = GUILayout.HorizontalSlider(Settings.UiScale, 0.75f, 1.35f,
                GUILayout.Width(220f));
            GUILayout.Label(Mathf.RoundToInt(Settings.UiScale * 100f) + "%", GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Panel opacity", GUILayout.Width(180f));
            Settings.PanelOpacity = GUILayout.HorizontalSlider(Settings.PanelOpacity, 0.50f, 1f,
                GUILayout.Width(220f));
            GUILayout.Label(Mathf.RoundToInt(Settings.PanelOpacity * 100f) + "%", GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Maximum displayed jobs", GUILayout.Width(180f));
            Settings.MaxDisplayedJobs = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                Settings.MaxDisplayedJobs, 1f, 10f, GUILayout.Width(220f)));
            GUILayout.Label(Settings.MaxDisplayedJobs.ToString(), GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Maximum displayed vehicles", GUILayout.Width(180f));
            Settings.MaxDisplayedVehicles = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                Settings.MaxDisplayedVehicles, 1f, 15f, GUILayout.Width(220f)));
            GUILayout.Label(Settings.MaxDisplayedVehicles.ToString(), GUILayout.Width(90f));
            GUILayout.EndHorizontal();

            Settings.HideEmptySections = GUILayout.Toggle(Settings.HideEmptySections,
                "Hide empty sections (jobs, equipment, notes) when none exist");

            Settings.VerboseLogging = GUILayout.Toggle(Settings.VerboseLogging,
                "Write additional Session Resume diagnostics to the Unity Mod Manager log");

            Settings.Clamp();

            GUILayout.Space(8f);
            if (controller != null && GUILayout.Button(controller.EditorVisible ? "Close context dashboard" :
                    "Open context dashboard", GUILayout.Width(220f)))
            {
                controller.SetEditorVisible(!controller.EditorVisible);
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy diagnostic report", GUILayout.Width(200f)))
            {
                GUIUtility.systemCopyBuffer = SessionResumeDiagnostics.BuildDiagnosticReport();
                Log("Diagnostic report copied to the clipboard.");
            }

            GUI.enabled = SessionResumePersistence.IsInitialized;
            bool confirmingCurrentReset = Time.realtimeSinceStartup < resetCurrentConfirmationUntil;
            if (GUILayout.Button(confirmingCurrentReset ? "Confirm reset for this save" : "Reset this save's context",
                    GUILayout.Width(210f)))
            {
                if (!confirmingCurrentReset)
                {
                    resetCurrentConfirmationUntil = Time.realtimeSinceStartup + 8f;
                }
                else if (SessionResumePersistence.ResetCurrent())
                {
                    resetCurrentConfirmationUntil = 0f;
                    Log("Cleared Session Resume context for the active save.");
                }
            }
            GUI.enabled = true;

            bool confirmingAllReset = Time.realtimeSinceStartup < resetAllConfirmationUntil;
            if (GUILayout.Button(confirmingAllReset ? "Confirm reset ALL saves" : "Reset all Session Resume data",
                    GUILayout.Width(210f)))
            {
                if (!confirmingAllReset)
                {
                    resetAllConfirmationUntil = Time.realtimeSinceStartup + 8f;
                }
                else
                {
                    resetAllConfirmationUntil = 0f;
                    int count = SessionResumePersistence.ResetAllData();
                    Log("Reset all Session Resume data across " + count + " save file(s).");
                }
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(SessionResumePersistence.LastIssue))
            {
                GUILayout.Label("Last persistence warning: " + SessionResumePersistence.LastIssue);
            }
        }

        private static void Unpatch()
        {
            if (!harmonyPatched || harmony == null) return;
            harmony.UnpatchAll(harmony.Id);
            harmonyPatched = false;
            SaveSelectionPreview.ClearCache();
        }

        internal static void Log(string message) { if (ModEntry != null) ModEntry.Logger.Log(message); }
        internal static void LogVerbose(string message)
        {
            if (Settings.VerboseLogging) Log(message);
        }
        internal static void LogWarning(string message)
        {
            if (ModEntry != null) ModEntry.Logger.Warning(message);
            SessionResumeDiagnostics.RecordIssue("WARNING", message);
        }
        internal static void LogError(string message)
        {
            if (ModEntry != null) ModEntry.Logger.Error(message);
            SessionResumeDiagnostics.RecordIssue("ERROR", message);
        }
    }
}
