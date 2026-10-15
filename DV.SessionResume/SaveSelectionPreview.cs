using System;
using System.Collections.Generic;
using DV.Common;
using DV.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SessionResume
{
    internal static class SaveSelectionPreview
    {
        private const string SaveLoadPreviewName = "SessionResume.SaveLoadPreview";
        private const string LauncherPreviewName = "SessionResume.LauncherPreview";

        private sealed class CacheEntry
        {
            internal DateTimeOffset Timestamp;
            internal bool Found;
            internal SessionResumeData Data;
        }

        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>();
        private static readonly List<TextMeshProUGUI> OwnedTextObjects =
            new List<TextMeshProUGUI>();
        private static readonly List<GameObject> OwnedCards = new List<GameObject>();
        private static SessionResumeData pendingLoadingData;
        private static bool pendingLoadingDataFound;
        private static bool pendingLoadingRequested;
        private static bool loadingPresentationActive;
        private static float pendingLoadingRequestedAt;

        private static float GetRealtimeSinceStartup()
        {
            try
            {
                var prop = typeof(Time).GetProperty("realtimeSinceStartup",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null) return (float)prop.GetValue(null, null);
            }
            catch { }
            return 0f;
        }

        internal static bool HasPendingLoadingRequest
        {
            get
            {
                float now = GetRealtimeSinceStartup();
                if (pendingLoadingRequested && pendingLoadingRequestedAt > 0f && now > 0f &&
                    now - pendingLoadingRequestedAt > 90f)
                {
                    CompleteLoadingPresentation();
                }
                return pendingLoadingRequested;
            }
        }

        internal static bool LoadingPresentationActive { get { return loadingPresentationActive; } }
        internal static bool PendingLoadingDataFound { get { return pendingLoadingDataFound; } }
        internal static SessionResumeData PendingLoadingData
        {
            get { return pendingLoadingData ?? new SessionResumeData(); }
        }

        internal static void ShowOnSaveLoad(SaveLoadController controller, ISaveGame save)
        {
            RemoveMainMenuPreview(controller == null ? null : controller.transform, SaveLoadPreviewName);
            if (!Main.Enabled || save == null) return;
            RememberForLoading(save);
        }

        internal static void ShowOnLauncher(LauncherController controller, ISaveGame save)
        {
            RemoveMainMenuPreview(controller == null ? null : controller.transform, LauncherPreviewName);
            if (!Main.Enabled || save == null) return;
            RememberForLoading(save);
        }

        private static void RemoveMainMenuPreview(Transform owner, string previewName)
        {
            if (owner == null) return;
            Canvas canvas = owner.GetComponentInParent<Canvas>();
            Transform root = canvas == null ? owner : canvas.transform;
            TextMeshProUGUI preview = FindPreview(root, previewName);
            if (preview == null) preview = FindPreview(owner, previewName);
            if (preview == null) return;
            GameObject card = preview.transform.parent == null
                ? preview.gameObject
                : preview.transform.parent.gameObject;
            UnityEngine.Object.Destroy(card);
        }

        internal static void ClearCache()
        {
            Cache.Clear();
            for (int index = OwnedCards.Count - 1; index >= 0; index--)
            {
                GameObject card = OwnedCards[index];
                if (card != null) UnityEngine.Object.Destroy(card);
            }
            OwnedCards.Clear();
            for (int index = OwnedTextObjects.Count - 1; index >= 0; index--)
            {
                TextMeshProUGUI text = OwnedTextObjects[index];
                if (text != null) UnityEngine.Object.Destroy(text.gameObject);
            }
            OwnedTextObjects.Clear();
            pendingLoadingData = null;
            pendingLoadingDataFound = false;
            pendingLoadingRequested = false;
            loadingPresentationActive = false;
            pendingLoadingRequestedAt = 0f;
        }

        private static TextMeshProUGUI FindPreview(Transform root, string name)
        {
            if (root == null) return null;
            Transform existing = root.Find(name);
            return existing == null ? null : existing.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        private static void PositionSaveManagerCard(TextMeshProUGUI text, Canvas canvas, RectTransform button)
        {
            if (text == null || canvas == null || button == null) return;
            RectTransform canvasRect = canvas.transform as RectTransform;
            RectTransform cardRect = text.transform.parent as RectTransform;
            if (canvasRect == null || cardRect == null) return;

            Vector3[] worldCorners = new Vector3[4];
            button.GetWorldCorners(worldCorners);
            Vector3 buttonBottomLeft = canvasRect.InverseTransformPoint(worldCorners[0]);
            Vector3 buttonTopRight = canvasRect.InverseTransformPoint(worldCorners[2]);
            float scale = Main.Settings.UiScale;
            float right = buttonTopRight.x - canvasRect.rect.xMin;
            float width = Mathf.Clamp(canvasRect.rect.width * 0.59f, 650f * scale, 1120f * scale);
            // The Save Manager reserves the first line of its lower band for the native help text.
            // Widen leftward so our inline context aligns with that band instead of the Load button.
            width = Mathf.Min(width + 124f * scale, right - 18f * scale);
            float height = 118f * scale;
            float top = buttonBottomLeft.y - canvasRect.rect.yMin - 28f * scale;

            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.zero;
            cardRect.pivot = Vector2.one;
            cardRect.sizeDelta = new Vector2(width, height);
            cardRect.anchoredPosition = ClampTopRight(canvasRect.rect, width, height, right, top);
            text.fontSize = 16f * scale;
            text.fontSizeMax = 16f * scale;
            text.fontSizeMin = 11f * scale;
            text.lineSpacing = 1.5f;
            text.rectTransform.offsetMin = new Vector2(4f * scale, 4f * scale);
            text.rectTransform.offsetMax = new Vector2(-4f * scale, -4f * scale);
        }

        private static void PositionContinueCard(TextMeshProUGUI text, Canvas canvas, LauncherController controller)
        {
            if (text == null || canvas == null || controller == null || controller.detailsTMPro == null) return;
            RectTransform canvasRect = canvas.transform as RectTransform;
            RectTransform cardRect = text.transform.parent as RectTransform;
            if (canvasRect == null || cardRect == null) return;

            float scale = Main.Settings.UiScale;

            Vector3[] detailsCorners = new Vector3[4];
            controller.detailsTMPro.rectTransform.GetWorldCorners(detailsCorners);
            Vector3 detailsTopLeft = canvasRect.InverseTransformPoint(detailsCorners[1]);

            float? buttonBottomY = null;
            float? buttonRightX = null;
            RectTransform buttonRect = controller.runButton != null ? controller.runButton.transform as RectTransform : null;
            if (buttonRect != null)
            {
                Vector3[] buttonCorners = new Vector3[4];
                buttonRect.GetWorldCorners(buttonCorners);
                Vector3 buttonBottomLeft = canvasRect.InverseTransformPoint(buttonCorners[0]);
                Vector3 buttonTopRight = canvasRect.InverseTransformPoint(buttonCorners[2]);
                buttonBottomY = buttonBottomLeft.y - canvasRect.rect.yMin;
                buttonRightX = buttonTopRight.x - canvasRect.rect.xMin;
            }

            Vector2 anchoredPos;
            Vector2 size;
            ComputeContinueFooterLayout(
                canvasRect.rect,
                detailsTopLeft.x - canvasRect.rect.xMin,
                buttonRightX,
                buttonBottomY,
                scale,
                out anchoredPos,
                out size);

            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.zero;
            cardRect.pivot = Vector2.zero;
            cardRect.sizeDelta = size;
            cardRect.anchoredPosition = anchoredPos;
            ConfigureContinueFooterText(text, scale);
        }

        internal static void ComputeContinueFooterLayout(
            Rect canvasRect,
            float detailsLeftX,
            float? rightEdgeX,
            float? buttonBottomY,
            float scale,
            out Vector2 anchoredPosition,
            out Vector2 sizeDelta)
        {
            float margin = 18f * scale;
            float leftX = Mathf.Max(detailsLeftX, margin);
            float rightX = rightEdgeX.HasValue ? rightEdgeX.Value : canvasRect.width - margin;
            rightX = Mathf.Clamp(rightX, leftX + 320f * scale, canvasRect.width - margin);
            float width = rightX - leftX;

            float topY = (buttonBottomY.HasValue ? buttonBottomY.Value : canvasRect.height * 0.28f) -
                         10f * scale;
            topY = Mathf.Clamp(topY, margin + 92f, canvasRect.height - margin);
            float desiredHeight = Mathf.Clamp(116f * scale, 92f, 138f);
            float height = Mathf.Min(desiredHeight, topY - margin);
            float cardBottomY = topY - height;

            leftX = Mathf.Clamp(leftX, margin, canvasRect.width - width - margin);

            anchoredPosition = new Vector2(leftX, cardBottomY);
            sizeDelta = new Vector2(width, height);
        }

        private static void ConfigureContinueFooterText(TextMeshProUGUI text, float scale)
        {
            text.enableAutoSizing = true;
            text.fontSize = 13f * scale;
            text.fontSizeMax = 13f * scale;
            text.fontSizeMin = 9.5f * scale;
            text.lineSpacing = 0.5f;
            text.rectTransform.offsetMin = new Vector2(18f * scale, 8f * scale);
            text.rectTransform.offsetMax = new Vector2(-16f * scale, -7f * scale);
        }

        private static Vector2 ClampTopRight(Rect canvas, float width, float height, float right, float top)
        {
            float margin = 18f * Main.Settings.UiScale;
            return new Vector2(
                Mathf.Clamp(right, width + margin, canvas.width - margin),
                Mathf.Clamp(top, height + margin, canvas.height - margin));
        }

        private static void ConfigureMenuText(TextMeshProUGUI text, float scale)
        {
            text.fontSize = 14f * scale;
            text.fontSizeMax = 14f * scale;
            text.fontSizeMin = 10f * scale;
            text.lineSpacing = 2.5f;
            text.rectTransform.offsetMin = new Vector2(18f * scale, 10f * scale);
            text.rectTransform.offsetMax = new Vector2(-16f * scale, -10f * scale);
        }

        private static TextMeshProUGUI CreateInlinePanel(Transform root, string name, TMP_Text template, int layer)
        {
            GameObject host = new GameObject(name, typeof(RectTransform));
            host.layer = layer;
            host.transform.SetParent(root, false);
            host.transform.SetAsLastSibling();

            GameObject textHost = new GameObject("Content",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textHost.layer = layer;
            textHost.transform.SetParent(host.transform, false);

            TextMeshProUGUI text = textHost.GetComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
            }

            text.color = new Color32(242, 245, 248, 255);
            text.fontStyle = FontStyles.Normal;
            text.fontSize = 16f * Main.Settings.UiScale;
            text.enableAutoSizing = true;
            text.fontSizeMin = 11f * Main.Settings.UiScale;
            text.fontSizeMax = 16f * Main.Settings.UiScale;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = true;
            text.raycastTarget = false;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f * Main.Settings.UiScale, 4f * Main.Settings.UiScale);
            rect.offsetMax = new Vector2(-4f * Main.Settings.UiScale, -4f * Main.Settings.UiScale);

            OwnedTextObjects.Add(text);
            OwnedCards.Add(host);
            return text;
        }

        private static TextMeshProUGUI CreatePopupCard(Transform root, string name, TMP_Text template,
            Vector2 anchorMin, Vector2 anchorMax, int layer)
        {
            GameObject host = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            host.layer = layer;
            host.transform.SetParent(root, false);
            host.transform.SetAsLastSibling();

            Image background = host.GetComponent<Image>();
            // This footer replaces the native one-line help band, so keep it opaque enough
            // that the original hint cannot ghost through the resume details.
            background.color = new Color32(7, 20, 35, 250);
            background.raycastTarget = false;
            Shadow shadow = host.AddComponent<Shadow>();
            shadow.effectColor = new Color32(0, 0, 0, 150);
            shadow.effectDistance = new Vector2(4f, -4f);
            shadow.useGraphicAlpha = true;
            Outline outline = host.AddComponent<Outline>();
            outline.effectColor = new Color32(95, 126, 151, 150);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            RectTransform cardRect = host.GetComponent<RectTransform>();
            cardRect.anchorMin = anchorMin;
            cardRect.anchorMax = anchorMax;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;

            GameObject accentHost = new GameObject("Accent",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accentHost.layer = layer;
            accentHost.transform.SetParent(host.transform, false);
            Image accent = accentHost.GetComponent<Image>();
            accent.color = new Color32(255, 145, 42, 255);
            accent.raycastTarget = false;
            RectTransform accentRect = accent.rectTransform;
            accentRect.anchorMin = Vector2.zero;
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.offsetMin = Vector2.zero;
            accentRect.offsetMax = new Vector2(5f * Main.Settings.UiScale, 0f);

            GameObject textHost = new GameObject("Content",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textHost.layer = layer;
            textHost.transform.SetParent(host.transform, false);
            TextMeshProUGUI text = textHost.GetComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
            }

            text.color = new Color32(235, 241, 247, 255);
            text.fontStyle = FontStyles.Normal;
            text.fontSize = template == null ? 16f : Mathf.Clamp(template.fontSize * 0.9f, 14f, 18f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 18f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = true;
            text.raycastTarget = false;
            text.parseCtrlCharacters = true;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 10f);
            rect.offsetMax = new Vector2(-16f, -10f);

            OwnedTextObjects.Add(text);
            OwnedCards.Add(host);
            return text;
        }

        private static void BindCardLifetime(GameObject ownerObject, GameObject card)
        {
            if (ownerObject == null || card == null) return;
            SessionResumePreviewOwner owner = ownerObject.GetComponent<SessionResumePreviewOwner>();
            if (owner == null) owner = ownerObject.AddComponent<SessionResumePreviewOwner>();
            owner.Card = card;
        }

        private static void SetCardActive(TextMeshProUGUI text, bool active)
        {
            if (text == null) return;
            GameObject card = text.transform.parent == null ? text.gameObject : text.transform.parent.gameObject;
            card.SetActive(active);
        }

        internal static void RememberForLoading(ISaveGame save)
        {
            if (save == null) return;
            CacheEntry entry = GetEntry(save);
            pendingLoadingData = entry.Data;
            pendingLoadingDataFound = entry.Found;
            pendingLoadingRequested = true;
            pendingLoadingRequestedAt = GetRealtimeSinceStartup();
            Main.LogVerbose("Prepared context for the loading-screen panel from save '" + save.Name + "'.");
        }

        internal static void BeginLoadingPresentation()
        {
            if (!pendingLoadingRequested || !Main.Enabled || !Main.Settings.ShowOnLoadingScreen) return;
            loadingPresentationActive = true;
            SessionResumeDiagnostics.HookLoadingScreenFound = true;
            SessionResumeDiagnostics.HookLoadingScreenHits++;
            Main.LogVerbose("Enabled the safe loading overlay.");
        }

        internal static void CompleteLoadingPresentation()
        {
            loadingPresentationActive = false;
            pendingLoadingRequested = false;
            pendingLoadingRequestedAt = 0f;
            pendingLoadingData = null;
            pendingLoadingDataFound = false;
        }

        private static string GetCardPreview(ISaveGame save)
        {
            CacheEntry entry = GetEntry(save);
            if (!entry.Found || entry.Data == null)
            {
                return "<b><color=#FF922B><size=90%>WHERE YOU LEFT OFF</size></color></b>\n" +
                       "<color=#C8D2DE>No Session Resume context has been recorded yet.</color>";
            }

            SessionResumeData data = entry.Data;
            bool hideEmpty = Main.Settings.HideEmptySections;
            List<string> lines = new List<string>();

            string locomotive = string.IsNullOrWhiteSpace(data.LastLocomotive.DisplayId)
                ? (hideEmpty ? string.Empty : "Not recorded")
                : Safe(data.LastLocomotive.DisplayId, 28);

            string jobCount = data.Jobs.Count == 1 ? "1 active" : data.Jobs.Count + " active";
            string header = "<b><color=#FF922B>WHERE YOU LEFT OFF</color></b>";
            if (!string.IsNullOrEmpty(locomotive))
            {
                header += "     <color=#9DAABA>LAST LOCO</color>  <b>" + locomotive + "</b>";
            }
            if (data.Jobs.Count > 0 || !hideEmpty)
            {
                header += "     <color=#607286>|</color>     ";
                header += "<color=#9DAABA>JOBS</color>  <b>" + jobCount + "</b>";
            }
            lines.Add(header);

            if (data.Jobs.Count > 0)
            {
                int maximum = Mathf.Min(data.Jobs.Count, Mathf.Max(1, Main.Settings.MaxDisplayedJobs));
                for (int index = 0; index < maximum; index++)
                {
                    JobRecord job = data.Jobs[index];
                    string route = string.IsNullOrWhiteSpace(job.Route) ? string.Empty : "  " + Safe(job.Route, 30);
                    string progress = string.IsNullOrWhiteSpace(job.Progress)
                        ? "Progress not recorded"
                        : Safe(job.Progress, 82);
                    lines.Add("<color=#9DAABA>JOB " + (index + 1) + "</color>  <b>" +
                              Safe(job.JobId, 24) + "</b>" + route + "  <color=#607286>|</color>  " + progress);
                }
                if (data.Jobs.Count > maximum)
                {
                    lines.Add("<color=#9DAABA>+" + (data.Jobs.Count - maximum) + " more active jobs</color>");
                }
            }
            else if (!hideEmpty)
            {
                lines.Add("<color=#9DAABA>No accepted jobs</color>");
            }

            string equipLine = string.Empty;
            int maxVehicles = Main.Settings.MaxDisplayedVehicles;
            bool hasDetached = data.DetachedWagons.Count > 0;
            bool hasParked = data.ParkedEquipment.Count > 0;

            if (hasDetached)
            {
                equipLine += "<color=#9DAABA>RECENTLY DETACHED</color>  " + FormatEquipment(data.DetachedWagons, maxVehicles);
            }
            if (hasParked)
            {
                if (equipLine.Length > 0) equipLine += "     <color=#607286>|</color>     ";
                equipLine += "<color=#9DAABA>LIKELY PARKED</color>  " + FormatEquipment(data.ParkedEquipment, maxVehicles);
            }
            if (!hasDetached && !hasParked && !hideEmpty)
            {
                equipLine = "<color=#9DAABA>No detached or parked cars</color>";
            }
            if (equipLine.Length > 0) lines.Add(equipLine);

            if (!string.IsNullOrWhiteSpace(data.Note))
            {
                lines.Add("<color=#FFB465>NOTE</color>  " + Safe(data.Note, 82));
            }
            else if (!string.IsNullOrWhiteSpace(data.AutomaticNote))
            {
                lines.Add("<color=#FFB465>NEXT</color>  " + Safe(data.AutomaticNote, 82));
            }

            return string.Join("\n", lines.ToArray());
        }

        internal static string GetCompactPreview(ISaveGame save)
        {
            CacheEntry entry = GetEntry(save);
            if (!entry.Found)
            {
                return "<b><color=#FF9D38>WHERE YOU LEFT OFF</color></b>   " +
                       "No Session Resume context recorded for this save.";
            }

            SessionResumeData data = entry.Data;
            bool hideEmpty = Main.Settings.HideEmptySections;
            string locomotive = string.IsNullOrWhiteSpace(data.LastLocomotive.DisplayId)
                ? (hideEmpty ? "None" : "Not recorded")
                : Safe(data.LastLocomotive.DisplayId, 34);

            string line1 = "<b><color=#FF9D38>WHERE YOU LEFT OFF</color></b>   Last detected loco: " + locomotive;
            string line2 = FormatJobs(data, Main.Settings.MaxDisplayedJobs);
            int maxVehicles = Main.Settings.MaxDisplayedVehicles;
            string line3 = "Recently detached: " + FormatEquipment(data.DetachedWagons, maxVehicles) +
                           "   •   Likely parked: " + FormatEquipment(data.ParkedEquipment, maxVehicles);
            if (string.IsNullOrWhiteSpace(data.Note) && !string.IsNullOrWhiteSpace(data.AutomaticNote))
            {
                line3 += "   |   Next: " + Safe(data.AutomaticNote, 64);
            }
            if (!string.IsNullOrWhiteSpace(data.Note))
            {
                line3 += "   •   Next: " + Safe(data.Note, 64);
            }

            if (!data.HasContent) line2 = "No working context recorded yet.";
            return line1 + "\n" + line2 + "\n" + line3;
        }

        private static CacheEntry GetEntry(ISaveGame save)
        {
            string cacheKey = (save.BasePath ?? string.Empty) + "|" + save.UID;
            CacheEntry cached;
            if (Cache.TryGetValue(cacheKey, out cached) && cached.Timestamp == save.Timestamp)
            {
                return cached;
            }

            SessionResumeData data;
            bool found = SessionResumePersistence.TryReadSnapshot(save, out data);
            CacheEntry entry = new CacheEntry
            {
                Timestamp = save.Timestamp,
                Found = found,
                Data = data ?? new SessionResumeData()
            };
            Cache[cacheKey] = entry;
            return entry;
        }

        private static string FormatJobs(SessionResumeData data, int maxJobs = 5)
        {
            if (data.Jobs.Count == 0) return "Jobs: none recorded";
            JobRecord first = data.Jobs[0];
            string progress = string.IsNullOrWhiteSpace(first.Progress) ? "progress not recorded" : first.Progress;
            string result = "Jobs (" + data.Jobs.Count + "): " + Safe(first.JobId, 26) + " — " +
                            Safe(progress, 68);
            if (data.Jobs.Count > 1) result += "  (and " + (data.Jobs.Count - 1) + " more)";
            return result;
        }

        private static string FormatEquipment(List<EquipmentRecord> records, int maxVehicles = 5)
        {
            if (records.Count == 0) return "none";
            int visible = Mathf.Min(records.Count, maxVehicles);
            List<string> items = new List<string>();
            for (int index = 0; index < visible; index++)
            {
                string id = Safe(records[index].EquipmentId, 20);
                if (records[index].Pinned) id += " [pinned]";
                items.Add(id);
            }
            string text = string.Join(", ", items.ToArray());
            if (records.Count > visible)
            {
                text += " +" + (records.Count - visible);
            }
            return text;
        }

        private static string Safe(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            string safe = value.Replace("\r", " ").Replace("\n", " ").Replace("<", "‹").Replace(">", "›");
            while (safe.Contains("  ")) safe = safe.Replace("  ", " ");
            safe = safe.Trim();
            return safe.Length <= maximumLength ? safe : safe.Substring(0, maximumLength - 1) + "…";
        }
    }

    [HarmonyPatch(typeof(SaveLoadController), "FormatLocalizedSaveInfo")]
    internal static class SaveLoadControllerFormatInfoPatch
    {
        private static void Postfix(ISaveGame __0, ref string __result)
        {
            try
            {
                SessionResumeDiagnostics.HookSaveLoadControllerFound = true;
                SessionResumeDiagnostics.HookSaveLoadControllerHits++;

                // Keep the selected save cached for the loading overlay without changing
                // any of Derail Valley's launcher or save-manager visuals.
                if (Main.Enabled && __0 != null)
                {
                    SaveSelectionPreview.RememberForLoading(__0);
                }
            }
            catch (Exception exception)
            {
                Main.LogWarning("Could not prepare Session Resume loading context: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SaveLoadController), "UpdateInfoTextAndThumb")]
    internal static class SaveLoadControllerUpdateInfoPatch
    {
        private static void Postfix(SaveLoadController __instance, ISaveGame __0)
        {
            try
            {
                SaveSelectionPreview.ShowOnSaveLoad(__instance, __0);
            }
            catch (Exception exception)
            {
                Main.LogWarning("Could not clean up save-selection preview: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(LauncherController), "GetSaveGameDetails")]
    internal static class LauncherControllerSaveDetailsPatch
    {
        private static void Postfix(LauncherController __instance, ISaveGame __1, ref string __result)
        {
            try
            {
                SessionResumeDiagnostics.HookLauncherControllerFound = true;
                SessionResumeDiagnostics.HookLauncherControllerHits++;

                SaveSelectionPreview.ShowOnLauncher(__instance, __1);
            }
            catch (Exception exception)
            {
                Main.LogWarning("Could not prepare the Launcher loading context: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(LauncherController), "OnRunClicked")]
    internal static class LauncherControllerRunPatch
    {
        private static void Prefix(ISaveGame ___saveGame)
        {
            try
            {
                if (___saveGame != null)
                {
                    SaveSelectionPreview.RememberForLoading(___saveGame);
                    SaveSelectionPreview.BeginLoadingPresentation();
                }
            }
            catch (Exception exception)
            {
                Main.LogWarning("Could not prepare the Continue context for loading: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SaveLoadController), "OnLoadPopupClosed")]
    internal static class SaveLoadControllerLoadPatch
    {
        private static void Prefix(ISaveGame ___saveAwaitingConfirmation)
        {
            try
            {
                if (___saveAwaitingConfirmation != null)
                {
                    SaveSelectionPreview.RememberForLoading(___saveAwaitingConfirmation);
                    SaveSelectionPreview.BeginLoadingPresentation();
                }
            }
            catch (Exception exception)
            {
                Main.LogWarning("Could not prepare the selected-save context for loading: " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(DV.Localization.Debug.LoadingScreenDebug), "Start")]
    internal static class LoadingScreenDebugStartPatch
    {
        private static void Postfix(DV.Localization.Debug.LoadingScreenDebug __instance)
        {
            SaveSelectionPreview.BeginLoadingPresentation();
        }
    }

    [HarmonyPatch(typeof(LoadingScreenManager), "StartLoading")]
    internal static class LoadingScreenManagerStartLoadingPatch
    {
        private static void Prefix()
        {
            SaveSelectionPreview.BeginLoadingPresentation();
        }
    }

    internal sealed class SessionResumePreviewOwner : MonoBehaviour
    {
        internal GameObject Card;

        private void OnDisable()
        {
            if (Card != null) Card.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Card != null) UnityEngine.Object.Destroy(Card);
            Card = null;
        }
    }
}
