using UnityEngine;
using UnityModManagerNet;

namespace SessionResume
{
    public sealed class SessionResumeSettings : UnityModManager.ModSettings
    {
        public KeyBinding ToggleKey = new KeyBinding { keyCode = KeyCode.F8 };

        // Legacy fields retained so older settings files continue to deserialize.
        // Main-menu previews were removed; save selection is now used only to prepare
        // the loading-screen context.
        public bool ShowInSaveSelection;
        public bool ShowOnContinueScreen;
        public bool ShowOnLoadingScreen = true;
        public bool ShowOnWorldLoad = true;

        // Startup duration
        public bool StartupUntilDismissed;
        public float StartupCardSeconds = 30f;

        // Layout & sizing
        public float UiScale = 1f;
        public float PanelOpacity = 0.96f;
        public int MaxDisplayedJobs = 5;
        public int MaxDisplayedVehicles = 5;
        public bool HideEmptySections = true;

        // Diagnostics
        public bool VerboseLogging;

        // Deprecated legacy field kept for backwards compatibility
        [System.Obsolete("Use MaxDisplayedJobs or MaxDisplayedVehicles instead")]
        public int MaximumVisibleItems;

        public string ToggleKeyDisplay
        {
            get { return ToggleKey == null ? "F8" : ToggleKey.ToString(); }
        }

        public void Clamp()
        {
            ShowInSaveSelection = false;
            ShowOnContinueScreen = false;

            if (ToggleKey == null)
            {
                ToggleKey = new KeyBinding { keyCode = KeyCode.F8 };
            }

#pragma warning disable 618
            // Migrate legacy setting if present
            if (MaximumVisibleItems > 0)
            {
                if (MaxDisplayedJobs <= 0) MaxDisplayedJobs = MaximumVisibleItems;
                if (MaxDisplayedVehicles <= 0) MaxDisplayedVehicles = MaximumVisibleItems;
                MaximumVisibleItems = 0;
            }
#pragma warning restore 618

            StartupCardSeconds = Mathf.Clamp(StartupCardSeconds, 5f, 120f);
            UiScale = Mathf.Clamp(UiScale, 0.75f, 1.35f);
            PanelOpacity = Mathf.Clamp(PanelOpacity, 0.50f, 1f);
            MaxDisplayedJobs = Mathf.Clamp(MaxDisplayedJobs, 1, 10);
            MaxDisplayedVehicles = Mathf.Clamp(MaxDisplayedVehicles, 1, 15);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Clamp();
            Save(this, modEntry);
        }
    }
}
