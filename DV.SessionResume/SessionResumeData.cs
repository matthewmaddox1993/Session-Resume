using System;
using System.Collections.Generic;

namespace SessionResume
{
    internal sealed class SessionResumeData
    {
        internal const int CurrentVersion = 2;
        private const int MaximumJobs = 50;
        private const int MaximumEquipmentRecords = 250;

        public int Version = CurrentVersion;
        public string UpdatedUtc = string.Empty;
        public LocomotiveRecord LastLocomotive = new LocomotiveRecord();
        public List<JobRecord> Jobs = new List<JobRecord>();
        public List<EquipmentRecord> DetachedWagons = new List<EquipmentRecord>();
        public List<EquipmentRecord> ParkedEquipment = new List<EquipmentRecord>();
        public List<EquipmentRecord> TrackedConsist = new List<EquipmentRecord>();
        public List<EquipmentRecord> KnownLocomotives = new List<EquipmentRecord>();
        public string Note = string.Empty;
        public string AutomaticNote = string.Empty;

        public void Normalize()
        {
            if (Version < 1) Version = 1;
            if (Version < CurrentVersion)
            {
                MigrateFrom(Version);
            }

            if (LastLocomotive == null) LastLocomotive = new LocomotiveRecord();
            if (Jobs == null) Jobs = new List<JobRecord>();
            if (DetachedWagons == null) DetachedWagons = new List<EquipmentRecord>();
            if (ParkedEquipment == null) ParkedEquipment = new List<EquipmentRecord>();
            if (TrackedConsist == null) TrackedConsist = new List<EquipmentRecord>();
            if (KnownLocomotives == null) KnownLocomotives = new List<EquipmentRecord>();
            if (Note == null) Note = string.Empty;
            if (AutomaticNote == null) AutomaticNote = string.Empty;
            UpdatedUtc = Clean(UpdatedUtc, 64);
            Note = Clean(Note, 600);
            AutomaticNote = Clean(AutomaticNote, 600);
            LastLocomotive.Normalize();

            Jobs.RemoveAll(item => item == null);
            DetachedWagons.RemoveAll(item => item == null);
            ParkedEquipment.RemoveAll(item => item == null);
            TrackedConsist.RemoveAll(item => item == null);
            KnownLocomotives.RemoveAll(item => item == null);
            foreach (JobRecord item in Jobs) item.Normalize();
            foreach (EquipmentRecord item in DetachedWagons) item.Normalize();
            foreach (EquipmentRecord item in ParkedEquipment) item.Normalize();
            foreach (EquipmentRecord item in TrackedConsist) item.Normalize();
            foreach (EquipmentRecord item in KnownLocomotives) item.Normalize();

            TrimTo(Jobs, MaximumJobs);
            TrimTo(DetachedWagons, MaximumEquipmentRecords);
            TrimTo(ParkedEquipment, MaximumEquipmentRecords);
            TrimTo(TrackedConsist, MaximumEquipmentRecords);
            TrimTo(KnownLocomotives, MaximumEquipmentRecords);
        }

        public void MigrateFrom(int oldVersion)
        {
            if (oldVersion < 2)
            {
                // Version 1 to 2 migration:
                // In v1, records only had Automatic flag. Non-automatic equipment was manually placed,
                // so mark it as Pinned (confirmed).
                if (ParkedEquipment != null)
                {
                    foreach (EquipmentRecord item in ParkedEquipment)
                    {
                        if (item != null && !item.Automatic)
                        {
                            item.Pinned = true;
                        }
                    }
                }
                if (DetachedWagons != null)
                {
                    foreach (EquipmentRecord item in DetachedWagons)
                    {
                        if (item != null && !item.Automatic)
                        {
                            item.Pinned = true;
                        }
                    }
                }
            }

            Version = CurrentVersion;
            Main.LogVerbose("Migrated session context data from schema v" + oldVersion + " to v" + CurrentVersion + ".");
        }

        public void Touch()
        {
            UpdatedUtc = DateTime.UtcNow.ToString("o");
        }

        public bool HasContent
        {
            get
            {
                return !string.IsNullOrWhiteSpace(LastLocomotive.DisplayId) || Jobs.Count > 0 ||
                       DetachedWagons.Count > 0 || ParkedEquipment.Count > 0 ||
                       !string.IsNullOrWhiteSpace(Note) || !string.IsNullOrWhiteSpace(AutomaticNote);
            }
        }

        public int InferredDetachedCount
        {
            get { return DetachedWagons.FindAll(item => !item.Pinned).Count; }
        }

        public int PinnedDetachedCount
        {
            get { return DetachedWagons.FindAll(item => item.Pinned).Count; }
        }

        public int InferredParkedCount
        {
            get { return ParkedEquipment.FindAll(item => !item.Pinned).Count; }
        }

        public int PinnedParkedCount
        {
            get { return ParkedEquipment.FindAll(item => item.Pinned).Count; }
        }

        internal static string Clean(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string result = value.Replace("\0", string.Empty).Trim();
            return result.Length <= maximumLength ? result : result.Substring(0, maximumLength);
        }

        private static void TrimTo<T>(List<T> items, int maximumCount)
        {
            if (items.Count > maximumCount) items.RemoveRange(maximumCount, items.Count - maximumCount);
        }
    }

    internal sealed class LocomotiveRecord
    {
        public string CarGuid = string.Empty;
        public string DisplayId = string.Empty;
        public string Type = string.Empty;
        public string RecordedUtc = string.Empty;

        public void Normalize()
        {
            CarGuid = SessionResumeData.Clean(CarGuid, 128);
            DisplayId = SessionResumeData.Clean(DisplayId, 96);
            Type = SessionResumeData.Clean(Type, 96);
            RecordedUtc = SessionResumeData.Clean(RecordedUtc, 64);
        }
    }

    internal sealed class JobRecord
    {
        public string JobId = string.Empty;
        public string Route = string.Empty;
        public string Progress = string.Empty;
        public bool Automatic;

        public void Normalize()
        {
            JobId = SessionResumeData.Clean(JobId, 96);
            Route = SessionResumeData.Clean(Route, 200);
            Progress = SessionResumeData.Clean(Progress, 300);
        }
    }

    internal sealed class EquipmentRecord
    {
        public string EquipmentId = string.Empty;
        public string CarGuid = string.Empty;
        public string Note = string.Empty;
        public bool Automatic;
        public bool Pinned;

        public void Normalize()
        {
            EquipmentId = SessionResumeData.Clean(EquipmentId, 96);
            CarGuid = SessionResumeData.Clean(CarGuid, 128);
            Note = SessionResumeData.Clean(Note, 240);
        }
    }
}
