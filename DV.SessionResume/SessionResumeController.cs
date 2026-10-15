using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.UI;
using UnityEngine;

namespace SessionResume
{
    internal sealed class SessionResumeController : MonoBehaviour
    {
        private const int WindowId = 997083;
        private Rect windowRect;
        private Vector2 editorScroll;
        private bool editorVisible;
        private float startupCardUntil;
        private float nextAutomaticRefresh;
        private string newJobId = string.Empty;
        private string newJobRoute = string.Empty;
        private string newDetachedId = string.Empty;
        private string newDetachedNote = string.Empty;
        private string newParkedId = string.Empty;
        private string newParkedNote = string.Empty;
        private CustomFirstPersonController playerController;
        private float resetConfirmationUntil;
        private float currentOpacity = -1f;

        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle sectionStyle;
        private GUIStyle wrappedStyle;
        private GUIStyle mutedStyle;
        private GUIStyle cardStyle;
        private GUIStyle valueStyle;
        private GUIStyle statusStyle;
        private GUIStyle buttonStyle;
        private GUIStyle pinButtonStyle;
        private GUIStyle inputStyle;
        private GUIStyle dividerStyle;
        private Texture2D panelTexture;
        private Texture2D cardTexture;
        private Texture2D fieldTexture;
        private Texture2D accentTexture;

        internal bool EditorVisible { get { return editorVisible; } }

        private void Awake()
        {
            float width = Mathf.Min(820f, Mathf.Max(620f, Screen.width - 48f));
            float height = Mathf.Min(720f, Mathf.Max(520f, Screen.height - 48f));
            windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f,
                width, height);

            if (Main.Settings.ShowOnWorldLoad)
            {
                startupCardUntil = Main.Settings.StartupUntilDismissed
                    ? float.MaxValue
                    : Time.unscaledTime + Main.Settings.StartupCardSeconds;
            }

            PlayerManager.PlayerChanged += OnPlayerChanged;
        }

        private void OnDestroy()
        {
            PlayerManager.PlayerChanged -= OnPlayerChanged;
            if (editorVisible) ResumePlayerControls();
            DestroyTexture(ref panelTexture);
            DestroyTexture(ref cardTexture);
            DestroyTexture(ref fieldTexture);
            DestroyTexture(ref accentTexture);
        }

        private void OnPlayerChanged()
        {
            playerController = null;
            if (editorVisible) SuspendPlayerControls();
        }

        private void Update()
        {
            if (Main.Settings.ToggleKey != null && Main.Settings.ToggleKey.Down())
            {
                SetEditorVisible(!editorVisible);
            }

            if (editorVisible)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
                {
                    SetEditorVisible(false);
                }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Time.unscaledTime >= nextAutomaticRefresh)
            {
                nextAutomaticRefresh = Time.unscaledTime + 1f;
                SessionAutoRecorder.Refresh(false);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (!editorVisible && Time.unscaledTime < startupCardUntil)
            {
                DrawStartupCard();
            }

            if (!editorVisible) return;
            ClampWindow();
            windowRect = GUI.Window(WindowId, windowRect, DrawEditor,
                string.Empty, panelStyle);
        }

        internal void SetEditorVisible(bool visible)
        {
            if (editorVisible == visible) return;
            editorVisible = visible;
            startupCardUntil = 0f;
            if (visible) SuspendPlayerControls();
            else ResumePlayerControls();
        }

        private void DrawStartupCard()
        {
            SessionResumeData data = SessionResumePersistence.Current;
            float scale = Main.Settings.UiScale;
            float width = Mathf.Min(470f * scale, Screen.width - 24f);
            string nextAction = !string.IsNullOrWhiteSpace(data.Note) ? data.Note : data.AutomaticNote;
            bool hideEmpty = Main.Settings.HideEmptySections;

            float height = 130f * scale;
            if (data.HasContent)
            {
                height += 46f * scale;
                if (!hideEmpty || data.Jobs.Count > 0) height += 46f * scale;
                if (!hideEmpty || data.DetachedWagons.Count > 0 || data.ParkedEquipment.Count > 0) height += 32f * scale;
                if (!string.IsNullOrWhiteSpace(nextAction)) height += 46f * scale;
            }
            height = Mathf.Min(height, Screen.height - 24f);

            Rect area = new Rect(Screen.width - width - 18f, 18f, width, height);
            GUILayout.BeginArea(area, panelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("WHERE YOU LEFT OFF", titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Main.Settings.ToggleKeyDisplay + "  DETAILS", statusStyle);
            GUILayout.Space(6f);
            if (GUILayout.Button("✕", buttonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
            {
                startupCardUntil = 0f;
            }
            GUILayout.EndHorizontal();
            GUILayout.Box(GUIContent.none, dividerStyle, GUILayout.Height(2f), GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);

            if (!data.HasContent)
            {
                GUILayout.Label("No working context has been recorded for this save yet.", wrappedStyle);
                GUILayout.Label("Enter a locomotive or accept a job to begin automatic tracking.", mutedStyle);
                GUILayout.EndArea();
                return;
            }

            string locoDisplay = string.IsNullOrWhiteSpace(data.LastLocomotive.DisplayId)
                ? (hideEmpty ? string.Empty : "Not recorded")
                : data.LastLocomotive.DisplayId + (string.IsNullOrWhiteSpace(data.LastLocomotive.Type) ? "" : "  (" + data.LastLocomotive.Type + ")");

            if (!string.IsNullOrEmpty(locoDisplay))
            {
                GUILayout.Label("LAST DETECTED LOCOMOTIVE", sectionStyle);
                GUILayout.Label(locoDisplay, valueStyle);
                GUILayout.Space(5f);
            }

            if (data.Jobs.Count > 0 || !hideEmpty)
            {
                GUILayout.Label("ACTIVE JOBS", sectionStyle);
                if (data.Jobs.Count == 0)
                {
                    GUILayout.Label("No accepted jobs", mutedStyle);
                }
                else
                {
                    JobRecord job = data.Jobs[0];
                    string route = string.IsNullOrWhiteSpace(job.Route) ? string.Empty : "  " + job.Route;
                    GUILayout.Label(job.JobId + route, wrappedStyle);
                    GUILayout.Label(string.IsNullOrWhiteSpace(job.Progress)
                        ? "Progress not recorded"
                        : job.Progress, mutedStyle);
                    if (data.Jobs.Count > 1) GUILayout.Label("+ " + (data.Jobs.Count - 1) + " more", statusStyle);
                }
                GUILayout.Space(5f);
            }

            bool hasEquip = data.DetachedWagons.Count > 0 || data.ParkedEquipment.Count > 0;
            if (hasEquip || !hideEmpty)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("RECENTLY DETACHED  " + data.DetachedWagons.Count, wrappedStyle);
                GUILayout.Space(18f);
                GUILayout.Label("LIKELY PARKED  " + data.ParkedEquipment.Count, wrappedStyle);
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrWhiteSpace(nextAction))
            {
                GUILayout.Space(5f);
                GUILayout.Label("NEXT ACTION", sectionStyle);
                GUILayout.Label(nextAction, wrappedStyle);
            }
            GUILayout.EndArea();
        }

        private void DrawEditor(int id)
        {
            SessionResumeData data = SessionResumePersistence.Current;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("WHERE YOU LEFT OFF", titleStyle);
            GUILayout.Label("AUTOMATIC SAVE CONTEXT", statusStyle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Width(92f)))
            {
                SessionAutoRecorder.Refresh(true);
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("Close  " + Main.Settings.ToggleKeyDisplay, buttonStyle, GUILayout.Width(112f)))
            {
                SetEditorVisible(false);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);
            GUILayout.Box(GUIContent.none, dividerStyle, GUILayout.Height(2f), GUILayout.ExpandWidth(true));
            GUILayout.Space(8f);

            editorScroll = GUILayout.BeginScrollView(editorScroll);
            DrawDashboardLocomotive(data);
            GUILayout.Space(10f);
            DrawDashboardJobs(data);
            GUILayout.Space(10f);

            GUILayout.BeginHorizontal();
            float columnWidth = Mathf.Max(260f, (windowRect.width - 80f) * 0.5f);
            DrawDashboardEquipment("RECENTLY DETACHED", data.DetachedWagons,
                "No detached wagons detected.", columnWidth, true);
            GUILayout.Space(10f);
            DrawDashboardEquipment("LIKELY PARKED", data.ParkedEquipment,
                "No parked vehicles detected.", columnWidth, false);
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            DrawDashboardNote(data);
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Saved inside this Derail Valley save", mutedStyle);
            GUILayout.FlexibleSpace();

            bool confirmingReset = Time.realtimeSinceStartup < resetConfirmationUntil;
            if (GUILayout.Button(confirmingReset ? "Confirm reset" : "Reset this save", buttonStyle,
                    GUILayout.Width(125f), GUILayout.Height(24f)))
            {
                if (!confirmingReset)
                {
                    resetConfirmationUntil = Time.realtimeSinceStartup + 8f;
                }
                else if (SessionResumePersistence.ResetCurrent())
                {
                    resetConfirmationUntil = 0f;
                    Main.Log("Cleared Session Resume context for the active save.");
                }
            }
            GUILayout.Space(10f);
            GUILayout.Label("Updates automatically", mutedStyle);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 220f, 78f));
        }

        private void DrawDashboardLocomotive(SessionResumeData data)
        {
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("LAST DETECTED LOCOMOTIVE", sectionStyle);
            GUILayout.BeginHorizontal();
            LocomotiveRecord locomotive = data.LastLocomotive;
            string displayId = locomotive == null || string.IsNullOrWhiteSpace(locomotive.DisplayId)
                ? "Not detected yet"
                : locomotive.DisplayId;
            GUILayout.Label(displayId, valueStyle);
            GUILayout.FlexibleSpace();
            if (locomotive != null && !string.IsNullOrWhiteSpace(locomotive.Type))
            {
                GUILayout.Label(locomotive.Type, mutedStyle);
            }
            GUILayout.EndHorizontal();
            if (locomotive == null || string.IsNullOrWhiteSpace(locomotive.DisplayId))
            {
                GUILayout.Label("Enter a locomotive cab and it will be recorded automatically.", mutedStyle);
            }
            GUILayout.EndVertical();
        }

        private void DrawDashboardJobs(SessionResumeData data)
        {
            GUILayout.Label("ACTIVE JOBS", sectionStyle);
            if (data.Jobs.Count == 0)
            {
                if (!Main.Settings.HideEmptySections)
                {
                    GUILayout.BeginVertical(cardStyle);
                    GUILayout.Label("No accepted jobs", valueStyle);
                    GUILayout.Label("Newly accepted jobs and their task stages appear here automatically.", mutedStyle);
                    GUILayout.EndVertical();
                }
                return;
            }

            int maxJobs = Main.Settings.MaxDisplayedJobs;
            int visibleCount = Mathf.Min(data.Jobs.Count, maxJobs);
            for (int i = 0; i < visibleCount; i++)
            {
                JobRecord job = data.Jobs[i];
                GUILayout.BeginVertical(cardStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.IsNullOrWhiteSpace(job.JobId) ? "Unnamed job" : job.JobId, valueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(job.Automatic ? "LIVE" : "MANUAL", statusStyle);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrWhiteSpace(job.Route)) GUILayout.Label(job.Route, wrappedStyle);
                GUILayout.Space(3f);
                GUILayout.Label(string.IsNullOrWhiteSpace(job.Progress)
                    ? "Progress has not been recorded yet."
                    : job.Progress, mutedStyle);
                GUILayout.EndVertical();
                GUILayout.Space(6f);
            }

            if (data.Jobs.Count > visibleCount)
            {
                GUILayout.Label("+ " + (data.Jobs.Count - visibleCount) + " more accepted jobs", mutedStyle);
            }
        }

        private void DrawDashboardEquipment(string heading, List<EquipmentRecord> records, string emptyMessage,
            float width, bool isDetachedList)
        {
            if (Main.Settings.HideEmptySections && records.Count == 0) return;

            GUILayout.BeginVertical(cardStyle, GUILayout.Width(width), GUILayout.MinHeight(120f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(heading, sectionStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(records.Count.ToString(), statusStyle);
            GUILayout.EndHorizontal();

            if (records.Count == 0)
            {
                GUILayout.Label(emptyMessage, mutedStyle);
            }
            else
            {
                int maxVehicles = Main.Settings.MaxDisplayedVehicles;
                int visible = Mathf.Min(records.Count, maxVehicles);
                for (int index = 0; index < visible; index++)
                {
                    EquipmentRecord item = records[index];
                    GUILayout.BeginHorizontal();
                    string badge = item.Pinned
                        ? "<color=#FFB465>[Pinned]</color> "
                        : (item.Automatic ? "<color=#89ABCB>[Inferred]</color> " : "");
                    string note = string.IsNullOrWhiteSpace(item.Note) ? string.Empty : "  " + item.Note;
                    GUILayout.Label("• " + badge + item.EquipmentId + note, wrappedStyle);
                    GUILayout.FlexibleSpace();

                    if (!item.Pinned)
                    {
                        if (GUILayout.Button(isDetachedList ? "Park" : "Pin", pinButtonStyle,
                                GUILayout.Width(46f), GUILayout.Height(22f)))
                        {
                            PinEquipment(item, isDetachedList);
                            break;
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Unpin", pinButtonStyle,
                                GUILayout.Width(50f), GUILayout.Height(22f)))
                        {
                            UnpinEquipment(item);
                            break;
                        }
                    }

                    if (GUILayout.Button("✕", buttonStyle, GUILayout.Width(24f), GUILayout.Height(22f)))
                    {
                        records.RemoveAt(index);
                        Changed();
                        break;
                    }

                    GUILayout.EndHorizontal();
                }

                if (records.Count > visible)
                {
                    GUILayout.Label("+ " + (records.Count - visible) + " more", mutedStyle);
                }
            }

            GUILayout.EndVertical();
        }

        private void PinEquipment(EquipmentRecord item, bool fromDetached)
        {
            SessionResumeData data = SessionResumePersistence.Current;
            if (fromDetached)
            {
                data.DetachedWagons.Remove(item);
                EquipmentRecord existing = data.ParkedEquipment.FirstOrDefault(x => x.CarGuid == item.CarGuid);
                if (existing != null)
                {
                    existing.Pinned = true;
                    existing.Automatic = false;
                    existing.Note = "Confirmed parked";
                }
                else
                {
                    data.ParkedEquipment.Add(new EquipmentRecord
                    {
                        EquipmentId = item.EquipmentId,
                        CarGuid = item.CarGuid,
                        Note = "Confirmed parked",
                        Automatic = false,
                        Pinned = true
                    });
                }
            }
            else
            {
                item.Pinned = true;
                item.Automatic = false;
                item.Note = "Confirmed parked";
            }
            Changed();
        }

        private void UnpinEquipment(EquipmentRecord item)
        {
            item.Pinned = false;
            item.Automatic = true;
            item.Note = "Likely parked";
            Changed();
        }

        private void DrawDashboardNote(SessionResumeData data)
        {
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("NEXT ACTION", sectionStyle);
            string detected = string.IsNullOrWhiteSpace(data.AutomaticNote)
                ? "No next action has been detected."
                : data.AutomaticNote;
            GUILayout.Label(detected, wrappedStyle);
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("OPTIONAL NOTE OVERRIDE", statusStyle);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrWhiteSpace(data.Note) &&
                GUILayout.Button("Clear", buttonStyle, GUILayout.Width(62f), GUILayout.Height(24f)))
            {
                data.Note = string.Empty;
                Changed();
            }
            GUILayout.EndHorizontal();
            string note = GUILayout.TextArea(data.Note ?? string.Empty, inputStyle, GUILayout.MinHeight(54f));
            if (note != data.Note)
            {
                data.Note = note;
                Changed();
            }
            GUILayout.EndVertical();
        }

        private static void Changed()
        {
            SessionResumePersistence.Changed();
        }

        private CustomFirstPersonController GetPlayerController()
        {
            if (playerController != null) return playerController;
            try
            {
                Transform player = PlayerManager.PlayerTransform;
                if (player != null) playerController = player.GetComponent<CustomFirstPersonController>();
            }
            catch { }
            return playerController;
        }

        private void SuspendPlayerControls()
        {
            CustomFirstPersonController controller = GetPlayerController();
            if (controller != null)
            {
                controller.FreeLookAllowed = false;
                if (controller.m_MouseLook != null)
                {
                    controller.m_MouseLook.RequestMouseSensitivityState(this, MouseSensitivityState.Locked, 100);
                }
                if (controller.Locomotion != null) controller.Locomotion.inputEnabled = false;
            }

            try { if (CursorManager.Instance != null) CursorManager.Instance.RequestCursor(this, true, 100); }
            catch { }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ResumePlayerControls()
        {
            CustomFirstPersonController controller = GetPlayerController();
            if (controller != null)
            {
                controller.FreeLookAllowed = true;
                if (controller.m_MouseLook != null) controller.m_MouseLook.RemoveRequest(this);
                if (controller.Locomotion != null) controller.Locomotion.inputEnabled = true;
            }

            try { if (CursorManager.Instance != null) CursorManager.Instance.RemoveRequest(this); }
            catch { }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void EnsureStyles()
        {
            if (panelStyle != null && Mathf.Approximately(currentOpacity, Main.Settings.PanelOpacity)) return;

            currentOpacity = Main.Settings.PanelOpacity;
            byte alpha = (byte)Mathf.RoundToInt(currentOpacity * 255f);

            DestroyTexture(ref panelTexture);
            DestroyTexture(ref cardTexture);
            DestroyTexture(ref fieldTexture);
            DestroyTexture(ref accentTexture);

            panelTexture = CreateSolidTexture(new Color32(7, 16, 27, alpha));
            cardTexture = CreateSolidTexture(new Color32(16, 31, 48, (byte)Mathf.Min(255, alpha + 6)));
            fieldTexture = CreateSolidTexture(new Color32(4, 12, 21, alpha));
            accentTexture = CreateSolidTexture(new Color32(255, 145, 42, 255));

            panelStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(20, 20, 18, 14),
                fontSize = 13
            };
            panelStyle.normal.background = panelTexture;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(255, 151, 54, 255) }
            };
            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(137, 171, 203, 255) }
            };
            wrappedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                richText = true,
                normal = { textColor = new Color32(235, 241, 247, 255) }
            };
            mutedStyle = new GUIStyle(wrappedStyle)
            {
                fontSize = 11,
                normal = { textColor = new Color32(158, 174, 190, 255) }
            };
            valueStyle = new GUIStyle(wrappedStyle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color32(255, 174, 91, 255) }
            };
            cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(13, 13, 11, 11),
                margin = new RectOffset(0, 0, 0, 0)
            };
            cardStyle.normal.background = cardTexture;
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 30f,
                padding = new RectOffset(10, 10, 5, 5),
                normal = { textColor = new Color32(226, 235, 243, 255) },
                hover = { textColor = Color.white },
                active = { textColor = Color.white }
            };
            buttonStyle.normal.background = fieldTexture;
            buttonStyle.hover.background = cardTexture;
            buttonStyle.active.background = accentTexture;

            pinButtonStyle = new GUIStyle(buttonStyle)
            {
                fontSize = 11,
                fixedHeight = 22f,
                padding = new RectOffset(6, 6, 2, 2)
            };

            inputStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 13,
                wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8),
                normal = { textColor = new Color32(235, 241, 247, 255) },
                focused = { textColor = Color.white }
            };
            inputStyle.normal.background = fieldTexture;
            inputStyle.focused.background = fieldTexture;
            dividerStyle = new GUIStyle(GUI.skin.box);
            dividerStyle.normal.background = accentTexture;
        }

        private static Texture2D CreateSolidTexture(Color32 color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private void ClampWindow()
        {
            windowRect.width = Mathf.Min(windowRect.width, Mathf.Max(600f, Screen.width - 20f));
            windowRect.height = Mathf.Min(windowRect.height, Mathf.Max(480f, Screen.height - 20f));
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));
        }
    }
}
