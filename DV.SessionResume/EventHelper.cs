using System;
using System.Reflection;

namespace SessionResume
{
    internal static class EventHelper
    {
        internal static void SubscribeStatic(Type type, string name, Delegate handler)
        {
            EventInfo eventInfo = type.GetEvent(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(null, handler);
                return;
            }

            FieldInfo field = type.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) throw new MissingMemberException(type.FullName, name);
            field.SetValue(null, Delegate.Combine(field.GetValue(null) as Delegate, handler));
        }

        internal static void UnsubscribeStatic(Type type, string name, Delegate handler)
        {
            EventInfo eventInfo = type.GetEvent(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (eventInfo != null)
            {
                eventInfo.RemoveEventHandler(null, handler);
                return;
            }

            FieldInfo field = type.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
            {
                field.SetValue(null, Delegate.Remove(field.GetValue(null) as Delegate, handler));
            }
        }

        internal static void Subscribe(object target, string name, Delegate handler)
        {
            Type type = target.GetType();
            EventInfo eventInfo = type.GetEvent(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(target, handler);
                return;
            }

            FieldInfo field = type.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new MissingMemberException(type.FullName, name);
            field.SetValue(target, Delegate.Combine(field.GetValue(target) as Delegate, handler));
        }

        internal static void Unsubscribe(object target, string name, Delegate handler)
        {
            Type type = target.GetType();
            EventInfo eventInfo = type.GetEvent(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (eventInfo != null)
            {
                eventInfo.RemoveEventHandler(target, handler);
                return;
            }

            FieldInfo field = type.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, Delegate.Remove(field.GetValue(target) as Delegate, handler));
            }
        }
    }
}
