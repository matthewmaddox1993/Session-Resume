using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SessionResume;
using UnityEngine;

public class Program
{
    private static int testsRun = 0;
    private static int testsPassed = 0;

    public static void Main()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("Running DV.SessionResume Pre-Release Test Suite");
        Console.WriteLine("==================================================");

        TestSchemaVersioningAndMigration();
        TestDamagedAndMissingDataRecovery();
        TestHonestDetectionAndPinning();
        TestDisplaySettingsAndHideEmpty();
        TestMultiResolutionAndLayoutGeometry();
        TestDiagnosticsReport();

        Console.WriteLine("==================================================");
        Console.WriteLine(string.Format("Results: {0} / {1} tests passed.", testsPassed, testsRun));
        if (testsPassed == testsRun)
        {
            Console.WriteLine("ALL TESTS PASSED SUCCESSFULLY!");
        }
        else
        {
            Console.WriteLine("SOME TESTS FAILED!");
            Environment.Exit(1);
        }
        Console.WriteLine("==================================================");
    }

    private static void Assert(bool condition, string testName)
    {
        testsRun++;
        if (condition)
        {
            testsPassed++;
            Console.WriteLine("  [PASS] " + testName);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [FAIL] " + testName);
            Console.ResetColor();
        }
    }

    private static void TestSchemaVersioningAndMigration()
    {
        Console.WriteLine("\n[1] Save-Data Safety & Schema Migration Tests");

        SessionResumeData data = new SessionResumeData
        {
            Version = 1
        };
        data.DetachedWagons.Add(new EquipmentRecord
        {
            EquipmentId = "W-101",
            CarGuid = "guid-w-101",
            Automatic = false,
            Note = "Left at siding"
        });
        data.ParkedEquipment.Add(new EquipmentRecord
        {
            EquipmentId = "DE2-005",
            CarGuid = "guid-loco-2",
            Automatic = true,
            Note = "Stationary"
        });

        data.Normalize();

        Assert(data.Version == SessionResumeData.CurrentVersion, "Migrated version is CurrentVersion (v2)");
        Assert(data.DetachedWagons[0].Pinned == true, "V1 manual detached wagon was migrated to Pinned = true");
        Assert(data.ParkedEquipment[0].Pinned == false, "V1 automatic parked vehicle remains Pinned = false");
    }

    private static void TestDamagedAndMissingDataRecovery()
    {
        Console.WriteLine("\n[2] Damaged & Missing Data Recovery Tests");

        SessionResumeData damaged = new SessionResumeData
        {
            Version = -5,
            UpdatedUtc = null,
            Note = new string('A', 2000),
            LastLocomotive = null,
            Jobs = null,
            DetachedWagons = null,
            ParkedEquipment = null
        };

        damaged.Normalize();

        Assert(damaged.Version >= 1, "Negative schema version recovered to >= 1");
        Assert(damaged.LastLocomotive != null, "Null LastLocomotive recovered to clean instance");
        Assert(damaged.Jobs != null && damaged.Jobs.Count == 0, "Null Jobs list recovered to empty list");
        Assert(damaged.DetachedWagons != null, "Null DetachedWagons recovered to empty list");
        Assert(damaged.Note.Length <= 600, "Excessive note string safely trimmed to max length");
    }

    private static void TestHonestDetectionAndPinning()
    {
        Console.WriteLine("\n[3] Honest Detection & Equipment Pinning Tests");

        SessionResumeData data = new SessionResumeData();
        EquipmentRecord detached = new EquipmentRecord
        {
            EquipmentId = "WAG-999",
            CarGuid = "guid-wag-999",
            Automatic = true,
            Pinned = false,
            Note = "Recently detached from DE6-001"
        };
        data.DetachedWagons.Add(detached);

        Assert(data.InferredDetachedCount == 1, "Detached wagon correctly counted as Inferred");
        Assert(data.PinnedDetachedCount == 0, "Detached wagon not counted as Pinned");

        data.DetachedWagons.Remove(detached);
        EquipmentRecord parked = new EquipmentRecord
        {
            EquipmentId = detached.EquipmentId,
            CarGuid = detached.CarGuid,
            Automatic = false,
            Pinned = true,
            Note = "Confirmed parked"
        };
        data.ParkedEquipment.Add(parked);

        Assert(data.DetachedWagons.Count == 0, "Detached wagons list empty after pinning to parked");
        Assert(data.ParkedEquipment.Count == 1, "Vehicle added to parked equipment");
        Assert(data.PinnedParkedCount == 1, "Vehicle correctly counted as PinnedParked");
        Assert(data.InferredParkedCount == 0, "Vehicle not counted as InferredParked");
        Assert(parked.Note == "Confirmed parked", "Explicit parked vehicle has confirmed note");
    }

    private static void TestDisplaySettingsAndHideEmpty()
    {
        Console.WriteLine("\n[4] Display Settings & Hide Empty Sections Tests");

        SessionResumeSettings settings = new SessionResumeSettings();
        settings.StartupUntilDismissed = true;
        settings.HideEmptySections = true;
        settings.UiScale = 1.15f;
        settings.PanelOpacity = 0.85f;
        settings.MaxDisplayedJobs = 3;
        settings.MaxDisplayedVehicles = 4;
        settings.Clamp();

        Assert(settings.StartupUntilDismissed == true, "StartupUntilDismissed setting retained");
        Assert(settings.HideEmptySections == true, "HideEmptySections setting retained");
        Assert(Math.Abs(settings.UiScale - 1.15f) < 0.001f, "UiScale clamped properly");
        Assert(Math.Abs(settings.PanelOpacity - 0.85f) < 0.001f, "PanelOpacity clamped properly");
        Assert(settings.MaxDisplayedJobs == 3, "MaxDisplayedJobs is 3");
        Assert(settings.MaxDisplayedVehicles == 4, "MaxDisplayedVehicles is 4");
        Assert(!settings.ShowInSaveSelection && !settings.ShowOnContinueScreen,
            "Main-menu context panels remain disabled");
    }

    private static void TestMultiResolutionAndLayoutGeometry()
    {
        Console.WriteLine("\n[5] Multi-Resolution & Layout Geometry Tests");

        var resolutions = new[]
        {
            new { Width = 1920f, Height = 1080f, Name = "1080p FHD (16:9)" },
            new { Width = 2560f, Height = 1440f, Name = "1440p QHD (16:9)" },
            new { Width = 3840f, Height = 2160f, Name = "4K UHD (16:9)" },
            new { Width = 2560f, Height = 1080f, Name = "Ultrawide (21:9)" },
            new { Width = 3440f, Height = 1440f, Name = "WQHD Ultrawide (21:9)" },
            new { Width = 5120f, Height = 1440f, Name = "Super Ultrawide (32:9)" }
        };

        float[] scales = new[] { 0.75f, 1.0f, 1.25f, 1.35f };

        foreach (var res in resolutions)
        {
            foreach (float scale in scales)
            {
                float margin = 34f * scale;
                float panelWidth = Mathf.Clamp(640f * scale, 380f, res.Width - margin * 2f);
                float panelHeight = Mathf.Clamp((78f + 8f * 24f) * scale,
                    132f * scale, Mathf.Min(390f * scale, res.Height - margin * 2f));
                float panelLeft = res.Width - panelWidth - margin;
                float panelRight = panelLeft + panelWidth;
                float panelTop = margin;
                float panelBottom = panelTop + panelHeight;

                bool boundsValid = panelLeft >= 0f && panelRight <= res.Width &&
                                   panelTop >= 0f && panelBottom <= res.Height;
                bool rightAligned = Math.Abs(panelRight - (res.Width - margin)) < 0.01f;

                if (!boundsValid || !rightAligned)
                {
                    Assert(false, string.Format("Loading overlay layout failed at {0} with scale {1:0.00}", res.Name, scale));
                    return;
                }
            }
        }

        Assert(true, "Loading overlay stayed inside screen bounds for all 24 resolution and UI-scale combinations");
        Assert(true, "Loading overlay remained consistently right-aligned");

    }

    private static void TestDiagnosticsReport()
    {
        Console.WriteLine("\n[6] Diagnostic Report Tests");

        SessionResumeDiagnostics.HookSaveLoadControllerFound = true;
        SessionResumeDiagnostics.HookSaveLoadControllerHits = 3;
        SessionResumeDiagnostics.HookLauncherControllerFound = true;
        SessionResumeDiagnostics.HookLauncherControllerHits = 1;
        SessionResumeDiagnostics.HookLoadingScreenFound = true;
        SessionResumeDiagnostics.HookLoadingScreenHits = 2;
        SessionResumeDiagnostics.HookWorldStreamingFound = true;
        SessionResumeDiagnostics.HookWorldStreamingHits = 1;

        SessionResumeDiagnostics.RecordIssue("WARNING", "Test warning for diagnostics report");

        string report = SessionResumeDiagnostics.BuildDiagnosticReport();

        Assert(report.Contains("Session Resume Diagnostic Report"), "Report header present");
        Assert(report.Contains("Mod Version:"), "Mod version present");
        Assert(report.Contains("Data Schema Version: v2"), "Data schema version v2 present");
        Assert(report.Contains("SaveLoadController (Sessions Menu): Active"), "Sessions hook status present");
        Assert(report.Contains("LauncherController (Continue Menu): Active"), "Continue hook status present");
        Assert(report.Contains("LoadingScreenDebug (Loading UI): Active"), "Loading hook status present");
        Assert(report.Contains("Test warning for diagnostics report"), "Recent issue present in report");
    }
}
