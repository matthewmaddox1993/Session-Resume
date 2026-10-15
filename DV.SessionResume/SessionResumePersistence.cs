using System;
using System.Collections.Generic;
using System.IO;
using DV.Common;
using DV.Utils;
using Newtonsoft.Json.Linq;

namespace SessionResume
{
    internal static class SessionResumePersistence
    {
        private const string SaveKey = "SessionResume_Data";
        private static readonly Action<SaveGameData> SaveHandler = OnGameSave;
        private static bool subscribed;
        private static bool initialized;
        private static bool warnedAboutNewerData;

        internal static SessionResumeData Current { get; private set; } = new SessionResumeData();
        internal static bool IsInitialized { get { return initialized; } }
        internal static string LastIssue { get; private set; } = string.Empty;

        internal static void Initialize()
        {
            SaveGameManager manager = SingletonBehaviour<SaveGameManager>.Instance;
            if (manager == null)
            {
                Main.LogWarning("SaveGameManager was unavailable; session context cannot be loaded yet.");
                LastIssue = "SaveGameManager was unavailable.";
                SessionResumeDiagnostics.RecordIssue("WARNING", LastIssue);
                Current = new SessionResumeData();
                initialized = false;
                return;
            }

            if (!subscribed)
            {
                EventHelper.Subscribe(manager, "OnInternalDataUpdate", SaveHandler);
                subscribed = true;
                SessionResumeDiagnostics.HookSaveGameManagerFound = true;
            }

            Current = Deserialize(manager.data);
            Current.Normalize();
            initialized = true;
            if (Current.Version <= SessionResumeData.CurrentVersion) LastIssue = string.Empty;
        }

        internal static void Shutdown()
        {
            if (initialized) StageCurrent();
            if (subscribed)
            {
                SaveGameManager manager = SingletonBehaviour<SaveGameManager>.Instance;
                if (manager != null)
                {
                    EventHelper.Unsubscribe(manager, "OnInternalDataUpdate", SaveHandler);
                }
            }

            subscribed = false;
            initialized = false;
            Current = new SessionResumeData();
        }

        internal static bool ResetCurrent()
        {
            if (!initialized) return false;
            Current = new SessionResumeData();
            Current.Touch();
            warnedAboutNewerData = false;
            LastIssue = string.Empty;
            StageCurrent();
            return true;
        }

        internal static int ResetAllData()
        {
            int filesModified = 0;
            try
            {
                if (initialized)
                {
                    ResetCurrent();
                }

                SaveSelectionPreview.ClearCache();

                string localLow = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Altfuture", "Derail Valley", "Users");
                string usersDir = Path.GetFullPath(localLow);

                if (Directory.Exists(usersDir))
                {
                    string[] sessionFiles = Directory.GetFiles(usersDir, "sessionData.json", SearchOption.AllDirectories);
                    foreach (string filePath in sessionFiles)
                    {
                        try
                        {
                            if (!File.Exists(filePath)) continue;
                            string jsonText = File.ReadAllText(filePath);
                            if (!jsonText.Contains(SaveKey)) continue;

                            JObject root = JObject.Parse(jsonText);
                            bool modified = false;

                            JToken gameDataToken = root["GameData"];
                            if (gameDataToken != null)
                            {
                                if (gameDataToken.Type == JTokenType.String)
                                {
                                    string gdString = gameDataToken.ToString();
                                    if (gdString.Contains(SaveKey))
                                    {
                                        JObject gdObj = JObject.Parse(gdString);
                                        if (gdObj.Remove(SaveKey))
                                        {
                                            root["GameData"] = gdObj.ToString(Newtonsoft.Json.Formatting.None);
                                            modified = true;
                                        }
                                    }
                                }
                                else if (gameDataToken is JObject gdObj)
                                {
                                    if (gdObj.Remove(SaveKey))
                                    {
                                        modified = true;
                                    }
                                }
                            }

                            if (modified)
                            {
                                File.WriteAllText(filePath, root.ToString(Newtonsoft.Json.Formatting.Indented));
                                filesModified++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Main.LogWarning("Could not clean Session Resume data from " + filePath + ": " + ex.Message);
                            SessionResumeDiagnostics.RecordIssue("WARNING", "Reset failed on " + Path.GetFileName(filePath) + ": " + ex.Message);
                        }
                    }
                }

                Main.Log("Reset all Session Resume data. Cleared cache and cleaned " + filesModified + " save file(s).");
            }
            catch (Exception exception)
            {
                Main.LogError("Failed during ResetAllData: " + exception.Message);
                SessionResumeDiagnostics.RecordIssue("ERROR", "ResetAllData failed: " + exception.Message);
            }

            return filesModified;
        }

        internal static void Changed()
        {
            if (!initialized) return;
            Current.Normalize();
            Current.Touch();
            StageCurrent();
        }

        internal static void StageCurrent()
        {
            if (!initialized) return;
            SaveGameManager manager = SingletonBehaviour<SaveGameManager>.Instance;
            if (manager == null || manager.data == null || Current == null) return;

            try
            {
                Write(manager.data, Current);
            }
            catch (Exception exception)
            {
                LastIssue = exception.Message;
                SessionResumeDiagnostics.RecordIssue("ERROR", "Failed to stage session context: " + exception.Message);
                Main.LogError("Failed to stage session context in the current save: " + exception);
            }
        }

        internal static bool TryReadSnapshot(ISaveGame save, out SessionResumeData data)
        {
            data = new SessionResumeData();
            if (save == null) return false;

            try
            {
                save.LoadData();
                if (save.Data == null) return false;

                SaveGameData gameData = SaveGameData.LoadFromJson(save.Data, save.CustomChunkData);
                JObject root = gameData == null ? null : gameData.GetJObject(SaveKey);
                if (root == null) return false;

                data = DeserializeRoot(root);
                return true;
            }
            catch (Exception exception)
            {
                string msg = "Could not read Session Resume data from save '" + save.Name + "': " + exception.Message;
                Main.LogWarning(msg);
                SessionResumeDiagnostics.RecordIssue("WARNING", msg);
                data = new SessionResumeData();
                return false;
            }
        }

        internal static bool TryReadGameData(SaveGameData gameData, out SessionResumeData data)
        {
            data = new SessionResumeData();
            if (gameData == null) return false;
            try
            {
                JObject root = gameData.GetJObject(SaveKey);
                if (root == null) return false;
                data = DeserializeRoot(root);
                return true;
            }
            catch (Exception exception)
            {
                string msg = "Could not read Session Resume data for the loading screen: " + exception.Message;
                Main.LogWarning(msg);
                SessionResumeDiagnostics.RecordIssue("WARNING", msg);
                return false;
            }
        }

        private static void OnGameSave(SaveGameData gameData)
        {
            SessionResumeDiagnostics.HookSaveGameManagerHits++;
            try
            {
                Write(gameData, Current);
            }
            catch (Exception exception)
            {
                LastIssue = exception.Message;
                SessionResumeDiagnostics.RecordIssue("ERROR", "Failed to write session context during save: " + exception.Message);
                Main.LogError("Failed to write session context: " + exception);
            }
        }

        private static SessionResumeData Deserialize(SaveGameData gameData)
        {
            if (gameData == null) return new SessionResumeData();
            JObject root = null;
            try
            {
                root = gameData.GetJObject(SaveKey);
            }
            catch (Exception ex)
            {
                LastIssue = "Corrupted save JSON: " + ex.Message;
                SessionResumeDiagnostics.RecordIssue("ERROR", LastIssue);
                Main.LogError("Failed reading Session Resume JObject from save data: " + ex);
                return new SessionResumeData();
            }

            if (root == null) return new SessionResumeData();

            try
            {
                return DeserializeRoot(root);
            }
            catch (Exception exception)
            {
                LastIssue = exception.Message;
                SessionResumeDiagnostics.RecordIssue("ERROR", "Session context was invalid: " + exception.Message);
                Main.LogError("Session context was invalid and could not be loaded: " + exception);
                return new SessionResumeData();
            }
        }

        private static SessionResumeData DeserializeRoot(JObject root)
        {
            int schemaVersion = SafeInt(root, "version", 1);
            SessionResumeData data = new SessionResumeData
            {
                Version = schemaVersion,
                UpdatedUtc = SafeString(root, "updatedUtc"),
                Note = SafeString(root, "note"),
                AutomaticNote = SafeString(root, "automaticNote"),
                LastLocomotive = ReadLocomotive(root["lastLocomotive"] as JObject)
            };

            ReadJobs(root["jobs"] as JArray, data);
            ReadEquipment(root["detachedWagons"] as JArray, data.DetachedWagons);
            ReadEquipment(root["parkedEquipment"] as JArray, data.ParkedEquipment);
            ReadEquipment(root["trackedConsist"] as JArray, data.TrackedConsist);
            ReadEquipment(root["knownLocomotives"] as JArray, data.KnownLocomotives);
            data.Normalize();
            return data;
        }

        private static void Write(SaveGameData gameData, SessionResumeData data)
        {
            if (gameData == null || data == null) return;
            data.Normalize();
            if (data.Version > SessionResumeData.CurrentVersion)
            {
                LastIssue = "This save contains Session Resume data from a newer mod version (v" + data.Version + ").";
                if (!warnedAboutNewerData)
                {
                    warnedAboutNewerData = true;
                    Main.LogWarning(LastIssue + " It was left unchanged to protect the save context.");
                    SessionResumeDiagnostics.RecordIssue("WARNING", LastIssue);
                }
                return;
            }

            JArray jobs = new JArray();
            foreach (JobRecord job in data.Jobs)
            {
                if (job == null) continue;
                jobs.Add(new JObject
                {
                    ["jobId"] = job.JobId,
                    ["route"] = job.Route,
                    ["progress"] = job.Progress,
                    ["automatic"] = job.Automatic
                });
            }

            JArray detached = WriteEquipment(data.DetachedWagons);
            JArray parked = WriteEquipment(data.ParkedEquipment);
            LocomotiveRecord locomotive = data.LastLocomotive ?? new LocomotiveRecord();

            gameData.SetJObject(SaveKey, new JObject
            {
                ["version"] = SessionResumeData.CurrentVersion,
                ["updatedUtc"] = data.UpdatedUtc,
                ["lastLocomotive"] = new JObject
                {
                    ["carGuid"] = locomotive.CarGuid,
                    ["displayId"] = locomotive.DisplayId,
                    ["type"] = locomotive.Type,
                    ["recordedUtc"] = locomotive.RecordedUtc
                },
                ["jobs"] = jobs,
                ["detachedWagons"] = detached,
                ["parkedEquipment"] = parked,
                ["trackedConsist"] = WriteEquipment(data.TrackedConsist),
                ["knownLocomotives"] = WriteEquipment(data.KnownLocomotives),
                ["note"] = data.Note,
                ["automaticNote"] = data.AutomaticNote
            });
        }

        private static LocomotiveRecord ReadLocomotive(JObject item)
        {
            if (item == null) return new LocomotiveRecord();
            return new LocomotiveRecord
            {
                CarGuid = SafeString(item, "carGuid"),
                DisplayId = SafeString(item, "displayId"),
                Type = SafeString(item, "type"),
                RecordedUtc = SafeString(item, "recordedUtc")
            };
        }

        private static void ReadJobs(JArray items, SessionResumeData data)
        {
            if (items == null) return;
            foreach (JToken token in items)
            {
                try
                {
                    JObject item = token as JObject;
                    if (item == null) continue;
                    data.Jobs.Add(new JobRecord
                    {
                        JobId = SafeString(item, "jobId"),
                        Route = SafeString(item, "route"),
                        Progress = SafeString(item, "progress"),
                        Automatic = SafeBool(item, "automatic", false)
                    });
                }
                catch (Exception ex)
                {
                    SessionResumeDiagnostics.RecordIssue("WARNING", "Skipped damaged job record: " + ex.Message);
                }
            }
        }

        private static void ReadEquipment(JArray items, List<EquipmentRecord> target)
        {
            if (items == null) return;
            foreach (JToken token in items)
            {
                try
                {
                    JObject item = token as JObject;
                    if (item == null) continue;
                    target.Add(new EquipmentRecord
                    {
                        EquipmentId = SafeString(item, "equipmentId"),
                        CarGuid = SafeString(item, "carGuid"),
                        Note = SafeString(item, "note"),
                        Automatic = SafeBool(item, "automatic", false),
                        Pinned = SafeBool(item, "pinned", false)
                    });
                }
                catch (Exception ex)
                {
                    SessionResumeDiagnostics.RecordIssue("WARNING", "Skipped damaged equipment record: " + ex.Message);
                }
            }
        }

        private static JArray WriteEquipment(List<EquipmentRecord> items)
        {
            JArray result = new JArray();
            if (items == null) return result;
            foreach (EquipmentRecord item in items)
            {
                if (item == null) continue;
                result.Add(new JObject
                {
                    ["equipmentId"] = item.EquipmentId,
                    ["carGuid"] = item.CarGuid,
                    ["note"] = item.Note,
                    ["automatic"] = item.Automatic,
                    ["pinned"] = item.Pinned
                });
            }

            return result;
        }

        private static string SafeString(JObject obj, string key, string fallback = "")
        {
            if (obj == null) return fallback;
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            return token.ToString();
        }

        private static int SafeInt(JObject obj, string key, int fallback = 0)
        {
            if (obj == null) return fallback;
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Integer) return (int)token;
            int parsed;
            if (int.TryParse(token.ToString(), out parsed)) return parsed;
            return fallback;
        }

        private static bool SafeBool(JObject obj, string key, bool fallback = false)
        {
            if (obj == null) return fallback;
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Boolean) return (bool)token;
            bool parsed;
            if (bool.TryParse(token.ToString(), out parsed)) return parsed;
            return fallback;
        }
    }
}
