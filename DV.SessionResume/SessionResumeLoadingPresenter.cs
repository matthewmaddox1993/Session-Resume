using System.Collections.Generic;
using DV;
using UnityEngine;

namespace SessionResume
{
    /// <summary>
    /// Draws the loading summary independently of Derail Valley's transient loading canvas.
    /// This avoids mutating or scanning the game's UI hierarchy while scenes are changing.
    /// </summary>
    internal sealed class SessionResumeLoadingPresenter : MonoBehaviour
    {
        private bool sawWorldStreaming;
        private float presentationStartedAt;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle valueStyle;
        private GUIStyle mutedStyle;
        private Texture2D panelTexture;
        private Texture2D accentTexture;
        private float styledScale = -1f;
        private float styledOpacity = -1f;

        private void Update()
        {
            if (!Main.Enabled || !Main.Settings.ShowOnLoadingScreen)
            {
                ResetState();
                return;
            }

            if (!SaveSelectionPreview.LoadingPresentationActive)
            {
                sawWorldStreaming = false;
                presentationStartedAt = 0f;
                return;
            }

            if (presentationStartedAt <= 0f) presentationStartedAt = Time.unscaledTime;
            if (!WorldStreamingInit.IsStreamingDone) sawWorldStreaming = true;

            bool completed = sawWorldStreaming && WorldStreamingInit.IsStreamingDone &&
                             Time.unscaledTime - presentationStartedAt > 0.75f;
            bool timedOut = Time.unscaledTime - presentationStartedAt > 180f;
            if (completed || timedOut) ResetState();
        }

        private void OnGUI()
        {
            if (!Main.Enabled || !Main.Settings.ShowOnLoadingScreen ||
                !SaveSelectionPreview.LoadingPresentationActive) return;

            EnsureStyles();
            float scale = Main.Settings.UiScale;
            float margin = 34f * scale;
            float width = Mathf.Clamp(640f * scale, 380f, Screen.width - margin * 2f);
            SessionResumeData data = SaveSelectionPreview.PendingLoadingData;
            bool found = SaveSelectionPreview.PendingLoadingDataFound;
            int contentLines = CountContentLines(data, found);
            float height = Mathf.Clamp((78f + contentLines * 24f) * scale,
                132f * scale, Mathf.Min(390f * scale, Screen.height - margin * 2f));
            Rect panel = new Rect(Screen.width - width - margin, margin, width, height);

            int previousDepth = GUI.depth;
            GUI.depth = -900;
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.DrawTexture(new Rect(panel.x, panel.y, 5f * scale, panel.height), accentTexture);

            Rect content = new Rect(panel.x + 22f * scale, panel.y + 16f * scale,
                panel.width - 40f * scale, panel.height - 28f * scale);
            GUILayout.BeginArea(content);
            GUILayout.BeginHorizontal();
            GUILayout.Label("WHERE YOU LEFT OFF", titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("SESSION RESUME", mutedStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(7f * scale);

            if (!found || data == null || !data.HasContent)
            {
                GUILayout.Label("No context was recorded in this save.", valueStyle);
                GUILayout.Label("Tracking will begin automatically after the world loads.", mutedStyle);
                GUILayout.EndArea();
                GUI.depth = previousDepth;
                return;
            }

            string locomotive = data.LastLocomotive == null ||
                                string.IsNullOrWhiteSpace(data.LastLocomotive.DisplayId)
                ? "Not recorded"
                : data.LastLocomotive.DisplayId;
            GUILayout.Label("LAST LOCOMOTIVE", labelStyle);
            GUILayout.Label(locomotive, valueStyle);

            if (data.Jobs.Count > 0)
            {
                GUILayout.Space(5f * scale);
                GUILayout.Label(data.Jobs.Count == 1 ? "ACTIVE JOB" : "ACTIVE JOBS  " + data.Jobs.Count,
                    labelStyle);
                int maximum = Mathf.Min(data.Jobs.Count, Mathf.Max(1, Main.Settings.MaxDisplayedJobs));
                for (int index = 0; index < maximum; index++)
                {
                    JobRecord job = data.Jobs[index];
                    string route = string.IsNullOrWhiteSpace(job.Route) ? string.Empty : "  " + job.Route;
                    GUILayout.Label((index + 1) + ".  " + job.JobId + route, valueStyle);
                    GUILayout.Label(string.IsNullOrWhiteSpace(job.Progress)
                        ? "Progress not recorded"
                        : job.Progress, mutedStyle);
                }
                if (data.Jobs.Count > maximum)
                {
                    GUILayout.Label("+" + (data.Jobs.Count - maximum) + " more active jobs", mutedStyle);
                }
            }

            string equipment = BuildEquipmentLine(data);
            if (!string.IsNullOrEmpty(equipment))
            {
                GUILayout.Space(5f * scale);
                GUILayout.Label(equipment, mutedStyle);
            }

            string note = !string.IsNullOrWhiteSpace(data.Note) ? data.Note : data.AutomaticNote;
            if (!string.IsNullOrWhiteSpace(note))
            {
                GUILayout.Space(5f * scale);
                GUILayout.Label("NEXT  " + note, valueStyle);
            }

            GUILayout.EndArea();
            GUI.depth = previousDepth;
        }

        private static int CountContentLines(SessionResumeData data, bool found)
        {
            if (!found || data == null || !data.HasContent) return 2;
            int count = 2;
            if (data.Jobs.Count > 0)
            {
                int maximum = Mathf.Min(data.Jobs.Count, Mathf.Max(1, Main.Settings.MaxDisplayedJobs));
                count += 1 + maximum * 2;
                if (data.Jobs.Count > maximum) count++;
            }
            if (data.DetachedWagons.Count > 0 || data.ParkedEquipment.Count > 0) count++;
            if (!string.IsNullOrWhiteSpace(data.Note) || !string.IsNullOrWhiteSpace(data.AutomaticNote)) count++;
            return count;
        }

        private static string BuildEquipmentLine(SessionResumeData data)
        {
            List<string> parts = new List<string>();
            if (data.DetachedWagons.Count > 0)
            {
                parts.Add("Recently detached: " + FormatEquipment(data.DetachedWagons));
            }
            if (data.ParkedEquipment.Count > 0)
            {
                parts.Add("Likely parked: " + FormatEquipment(data.ParkedEquipment));
            }
            return string.Join("    |    ", parts.ToArray());
        }

        private static string FormatEquipment(List<EquipmentRecord> records)
        {
            int maximum = Mathf.Min(records.Count, Mathf.Max(1, Main.Settings.MaxDisplayedVehicles));
            List<string> ids = new List<string>();
            for (int index = 0; index < maximum; index++)
            {
                string id = records[index] == null ? string.Empty : records[index].EquipmentId;
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
            if (records.Count > maximum) ids.Add("+" + (records.Count - maximum));
            return ids.Count == 0 ? "none" : string.Join(", ", ids.ToArray());
        }

        private void EnsureStyles()
        {
            float scale = Main.Settings.UiScale;
            float opacity = Main.Settings.PanelOpacity;
            if (panelStyle != null && Mathf.Approximately(styledScale, scale) &&
                Mathf.Approximately(styledOpacity, opacity)) return;

            DestroyTextures();
            styledScale = scale;
            styledOpacity = opacity;
            panelTexture = CreateSolidTexture(new Color32(7, 20, 35,
                (byte)Mathf.RoundToInt(opacity * 255f)));
            accentTexture = CreateSolidTexture(new Color32(255, 145, 42, 255));

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelTexture;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(18f * scale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(255, 155, 61, 255) }
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(10f * scale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(154, 174, 194, 255) }
            };
            valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(14f * scale),
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color32(240, 244, 248, 255) }
            };
            mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(12f * scale),
                wordWrap = true,
                normal = { textColor = new Color32(180, 194, 207, 255) }
            };
        }

        private static Texture2D CreateSolidTexture(Color32 color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void ResetState()
        {
            sawWorldStreaming = false;
            presentationStartedAt = 0f;
            SaveSelectionPreview.CompleteLoadingPresentation();
        }

        private void OnDestroy()
        {
            DestroyTextures();
        }

        private void DestroyTextures()
        {
            if (panelTexture != null) Destroy(panelTexture);
            if (accentTexture != null) Destroy(accentTexture);
            panelTexture = null;
            accentTexture = null;
            panelStyle = null;
        }
    }
}
