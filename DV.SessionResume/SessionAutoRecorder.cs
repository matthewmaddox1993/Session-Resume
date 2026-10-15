using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;

namespace SessionResume
{
    internal static class SessionAutoRecorder
    {
        private static bool warnedLocomotiveFailure;
        private static bool warnedJobFailure;
        private static bool warnedParkedFailure;

        internal static void Refresh(bool logResult)
        {
            bool changed = false;
            try
            {
                changed |= CaptureOccupiedLocomotive();
            }
            catch (Exception exception)
            {
                WarnOnce(ref warnedLocomotiveFailure,
                    "Could not refresh locomotive and consist context: " + exception.Message);
            }
            try
            {
                changed |= SynchronizeJobs();
            }
            catch (Exception exception)
            {
                WarnOnce(ref warnedJobFailure,
                    "Could not refresh accepted-job context: " + exception.Message);
            }
            try
            {
                changed |= SynchronizeParkedLocomotives();
            }
            catch (Exception exception)
            {
                WarnOnce(ref warnedParkedFailure,
                    "Could not refresh parked-locomotive context: " + exception.Message);
            }

            if (changed) SessionResumePersistence.Changed();
            if (logResult) Main.Log("Automatic session context refreshed.");
        }

        private static void WarnOnce(ref bool warned, string message)
        {
            if (warned) return;
            warned = true;
            Main.LogWarning(message);
        }

        private static bool CaptureOccupiedLocomotive()
        {
            TrainCar locomotive;
            try
            {
                locomotive = PlayerManager.PlayerTransform == null ? null : PlayerManager.Car;
            }
            catch
            {
                locomotive = null;
            }

            if (locomotive == null || !locomotive.IsLoco || string.IsNullOrWhiteSpace(locomotive.CarGUID))
            {
                return false;
            }

            SessionResumeData data = SessionResumePersistence.Current;
            string locomotiveGuid = locomotive.CarGUID;
            bool sameLocomotive = data.LastLocomotive != null && data.LastLocomotive.CarGuid == locomotiveGuid;
            List<EquipmentRecord> currentConsist = GetCurrentWagons(locomotive);
            bool changed = false;

            if (sameLocomotive && data.TrackedConsist.Count > 0)
            {
                HashSet<string> currentGuids = new HashSet<string>(
                    currentConsist.Select(ItemKey).Where(value => !string.IsNullOrWhiteSpace(value)));

                foreach (EquipmentRecord formerCar in data.TrackedConsist)
                {
                    string key = ItemKey(formerCar);
                    if (string.IsNullOrWhiteSpace(key) || currentGuids.Contains(key)) continue;

                    EquipmentRecord existingInParked = FindEquipment(data.ParkedEquipment, formerCar);
                    if (existingInParked != null && existingInParked.Pinned) continue;

                    EquipmentRecord existing = FindEquipment(data.DetachedWagons, formerCar);
                    if (existing == null)
                    {
                        data.DetachedWagons.Add(new EquipmentRecord
                        {
                            EquipmentId = formerCar.EquipmentId,
                            CarGuid = formerCar.CarGuid,
                            Note = "Recently detached from " + SafeCarId(locomotive),
                            Automatic = true,
                            Pinned = false
                        });
                        changed = true;
                    }
                }

                for (int index = data.DetachedWagons.Count - 1; index >= 0; index--)
                {
                    EquipmentRecord item = data.DetachedWagons[index];
                    if (!item.Pinned && item.Automatic && currentGuids.Contains(ItemKey(item)))
                    {
                        data.DetachedWagons.RemoveAt(index);
                        changed = true;
                    }
                }
            }

            if (!EquipmentListsEqual(data.TrackedConsist, currentConsist))
            {
                data.TrackedConsist = currentConsist;
                changed = true;
            }

            string displayId = SafeCarId(locomotive);
            string type = locomotive.carType.ToString();
            if (data.LastLocomotive == null || data.LastLocomotive.CarGuid != locomotiveGuid ||
                data.LastLocomotive.DisplayId != displayId || data.LastLocomotive.Type != type)
            {
                data.LastLocomotive = new LocomotiveRecord
                {
                    CarGuid = locomotiveGuid,
                    DisplayId = displayId,
                    Type = type,
                    RecordedUtc = DateTime.UtcNow.ToString("o")
                };
                changed = true;
            }

            EquipmentRecord known = data.KnownLocomotives.FirstOrDefault(item => item.CarGuid == locomotiveGuid);
            if (known == null)
            {
                data.KnownLocomotives.Add(new EquipmentRecord
                {
                    EquipmentId = displayId,
                    CarGuid = locomotiveGuid,
                    Note = type,
                    Automatic = true
                });
                changed = true;
            }
            else if (known.EquipmentId != displayId || known.Note != type || !known.Automatic)
            {
                known.EquipmentId = displayId;
                known.Note = type;
                known.Automatic = true;
                changed = true;
            }

            return changed;
        }

        private static bool SynchronizeJobs()
        {
            JobsManager manager = JobsManager.Instance;
            if (manager == null || manager.currentJobs == null) return false;

            SessionResumeData data = SessionResumePersistence.Current;
            HashSet<string> activeIds = new HashSet<string>();
            JobRecord firstActive = null;
            bool changed = false;

            foreach (Job job in manager.currentJobs)
            {
                if (job == null || job.State != JobState.InProgress || string.IsNullOrWhiteSpace(job.ID)) continue;
                activeIds.Add(job.ID);
                string route = GetRoute(job);
                string progress = GetProgress(job);
                JobRecord saved = data.Jobs.FirstOrDefault(item => item.JobId == job.ID);
                if (saved == null)
                {
                    saved = new JobRecord { JobId = job.ID };
                    data.Jobs.Add(saved);
                    changed = true;
                }

                if (saved.Route != route || saved.Progress != progress || !saved.Automatic)
                {
                    saved.Route = route;
                    saved.Progress = progress;
                    saved.Automatic = true;
                    changed = true;
                }

                if (firstActive == null) firstActive = saved;
            }

            for (int index = data.Jobs.Count - 1; index >= 0; index--)
            {
                JobRecord saved = data.Jobs[index];
                if (saved.Automatic && !activeIds.Contains(saved.JobId))
                {
                    data.Jobs.RemoveAt(index);
                    changed = true;
                }
            }

            string automaticNote = firstActive == null
                ? string.Empty
                : BuildAutomaticNote(firstActive);
            if (data.AutomaticNote != automaticNote)
            {
                data.AutomaticNote = automaticNote;
                changed = true;
            }

            return changed;
        }

        private static bool SynchronizeParkedLocomotives()
        {
            SessionResumeData data = SessionResumePersistence.Current;
            Dictionary<string, TrainCar> loadedCars = GetLoadedCars();
            HashSet<string> activeTrainset = new HashSet<string>();
            TrainCar activeLoco;
            loadedCars.TryGetValue(data.LastLocomotive == null ? string.Empty : data.LastLocomotive.CarGuid,
                out activeLoco);
            if (activeLoco != null && activeLoco.trainset != null && activeLoco.trainset.cars != null)
            {
                foreach (TrainCar car in activeLoco.trainset.cars)
                {
                    if (car != null && !string.IsNullOrWhiteSpace(car.CarGUID)) activeTrainset.Add(car.CarGUID);
                }
            }

            bool changed = false;
            foreach (EquipmentRecord known in data.KnownLocomotives)
            {
                if (string.IsNullOrWhiteSpace(known.CarGuid)) continue;
                EquipmentRecord parked = data.ParkedEquipment.FirstOrDefault(item =>
                    item.CarGuid == known.CarGuid);

                if (parked != null && parked.Pinned)
                {
                    // Confirmed pinned by player, keep pinned!
                    continue;
                }

                TrainCar car;
                bool loaded = loadedCars.TryGetValue(known.CarGuid, out car);
                bool shouldBeParked = !activeTrainset.Contains(known.CarGuid) && (!loaded || car.isStationary);
                string note = loaded ? "Stationary; likely parked" : "Last detected parked";

                if (shouldBeParked && parked == null)
                {
                    data.ParkedEquipment.Add(new EquipmentRecord
                    {
                        EquipmentId = known.EquipmentId,
                        CarGuid = known.CarGuid,
                        Note = note,
                        Automatic = true,
                        Pinned = false
                    });
                    changed = true;
                }
                else if (!shouldBeParked && parked != null && parked.Automatic)
                {
                    data.ParkedEquipment.Remove(parked);
                    changed = true;
                }
                else if (shouldBeParked && parked != null && parked.Automatic)
                {
                    if (parked.EquipmentId != known.EquipmentId || parked.Note != note)
                    {
                        parked.EquipmentId = known.EquipmentId;
                        parked.Note = note;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private static List<EquipmentRecord> GetCurrentWagons(TrainCar locomotive)
        {
            List<EquipmentRecord> result = new List<EquipmentRecord>();
            Trainset trainset = locomotive.trainset;
            if (trainset == null || trainset.cars == null) return result;
            foreach (TrainCar car in trainset.cars)
            {
                if (car == null || car.IsLoco) continue;
                result.Add(new EquipmentRecord
                {
                    EquipmentId = SafeCarId(car),
                    CarGuid = car.CarGUID ?? string.Empty,
                    Note = "Coupled to " + SafeCarId(locomotive),
                    Automatic = true
                });
            }
            return result;
        }

        private static Dictionary<string, TrainCar> GetLoadedCars()
        {
            Dictionary<string, TrainCar> result = new Dictionary<string, TrainCar>();
            if (Trainset.allSets == null) return result;
            foreach (Trainset set in Trainset.allSets)
            {
                if (set == null || set.cars == null) continue;
                foreach (TrainCar car in set.cars)
                {
                    if (car != null && !string.IsNullOrWhiteSpace(car.CarGUID)) result[car.CarGUID] = car;
                }
            }
            return result;
        }

        private static string GetRoute(Job job)
        {
            if (job.chainData == null) return string.Empty;
            string origin = job.chainData.chainOriginYardId ?? string.Empty;
            string destination = job.chainData.chainDestinationYardId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(origin)) return destination;
            if (string.IsNullOrWhiteSpace(destination)) return origin;
            return origin + " -> " + destination;
        }

        private static string GetProgress(Job job)
        {
            List<TaskData> leaves = new List<TaskData>();
            if (job.tasks != null)
            {
                foreach (DV.Logic.Job.Task task in job.tasks) CollectLeafTasks(task, leaves);
            }

            if (leaves.Count == 0) return "Accepted; waiting for task data";
            int done = leaves.Count(task => task.state == TaskState.Done);
            TaskData current = leaves.FirstOrDefault(task => task.state == TaskState.InProgress);
            string stage = current == null ? "Ready to complete" : DescribeTask(current);
            return done + "/" + leaves.Count + " stages - " + stage;
        }

        private static void CollectLeafTasks(DV.Logic.Job.Task task, List<TaskData> leaves)
        {
            if (task == null) return;
            TaskData data = task.GetTaskData();
            if (data.nestedTasks != null && data.nestedTasks.Count > 0)
            {
                foreach (DV.Logic.Job.Task nested in data.nestedTasks) CollectLeafTasks(nested, leaves);
                return;
            }
            leaves.Add(data);
        }

        private static string DescribeTask(TaskData task)
        {
            if (task.type == TaskType.Transport)
            {
                if (task.couplingRequiredAndNotDone) return "Couple the assigned cars";
                string destination = task.destinationTrack == null || task.destinationTrack.ID == null
                    ? string.Empty
                    : task.destinationTrack.ID.FullDisplayID;
                if (task.anyHandbrakeRequiredAndNotDone)
                {
                    return string.IsNullOrWhiteSpace(destination)
                        ? "Secure the cars"
                        : "Secure the cars at " + destination;
                }
                return string.IsNullOrWhiteSpace(destination)
                    ? "Move the assigned cars"
                    : "Move the cars to " + destination;
            }

            if (task.type == TaskType.Warehouse)
            {
                if (task.warehouseTaskType == WarehouseTaskType.Loading) return "Load the assigned cargo";
                if (task.warehouseTaskType == WarehouseTaskType.Unloading) return "Unload the assigned cargo";
                return "Complete the warehouse operation";
            }

            return "Continue the active stage";
        }

        private static string BuildAutomaticNote(JobRecord job)
        {
            if (job == null) return string.Empty;
            string route = string.IsNullOrWhiteSpace(job.Route) ? string.Empty : " (" + job.Route + ")";
            return job.JobId + route + ": " + job.Progress;
        }

        private static EquipmentRecord FindEquipment(IEnumerable<EquipmentRecord> records, EquipmentRecord target)
        {
            string key = ItemKey(target);
            return records.FirstOrDefault(item => ItemKey(item) == key);
        }

        private static bool EquipmentListsEqual(IList<EquipmentRecord> left, IList<EquipmentRecord> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                if (left[index].EquipmentId != right[index].EquipmentId ||
                    left[index].CarGuid != right[index].CarGuid) return false;
            }
            return true;
        }

        private static string ItemKey(EquipmentRecord item)
        {
            if (item == null) return string.Empty;
            return !string.IsNullOrWhiteSpace(item.CarGuid) ? item.CarGuid : item.EquipmentId;
        }

        internal static string SafeCarId(TrainCar car)
        {
            if (car == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(car.ID)) return car.ID;
            if (!string.IsNullOrWhiteSpace(car.CarGUID)) return car.CarGUID;
            return car.name;
        }
    }
}
