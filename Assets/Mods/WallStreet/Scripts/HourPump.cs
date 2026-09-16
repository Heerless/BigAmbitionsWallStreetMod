#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WallStreet
{
    /// <summary>
    /// Fires once per in-game hour.
    /// <para>
    /// The game exposes no static clock event - GameManager declares none - so rather than
    /// guess at another event name this finds whatever member actually holds the hour and
    /// watches it for changes. Reading one number per frame is cheap; being wrong about an
    /// event name costs a whole test cycle.
    /// </para>
    /// </summary>
    public class HourPump : MonoBehaviour
    {
        public static event Action? HourElapsed;

        /// <summary>
        /// Fired roughly twice a second regardless of the clock. UI work needs this:
        /// the phone is built long after mods load, and in-game time does not advance
        /// while the player sits in a menu - so anything waiting on an hour may never run.
        /// </summary>
        public static event Action? Ticked;

        private static float _nextTick;

        private static HourPump? _instance;
        private static Func<int>? _read;
        private static Action<string> _log = _ => { };

        private int _last = int.MinValue;

        /// <summary>
        /// Committed capital leaves the player's balance but there is no mod save API to
        /// record it, so it must come home before the process ends or the money is lost.
        /// </summary>
        public static Action? OnQuitting;

        public static bool Install(Action<string> log)
        {
            _log = log;
            _read = Discover(log);

            Application.quitting -= HandleQuitting;
            Application.quitting += HandleQuitting;

            if (_read == null)
            {
                log("CLOCK: no readable hour found; the floor cannot trade.");
                return false;
            }

            if (_instance == null)
            {
                var host = new GameObject("WallStreet.HourPump");
                DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;
                _instance = host.AddComponent<HourPump>();
            }

            log("CLOCK: watching the game clock; currently reads " + _read() + ".");
            return true;
        }

        private static void HandleQuitting()
        {
            try { OnQuitting?.Invoke(); }
            catch (Exception e) { _log("CLOCK: quit handler threw: " + e.Message); }
        }

        /// <summary>Returns capital if the player leaves the city without quitting.</summary>
        private void OnDestroy()
        {
            HandleQuitting();
        }

        public static void Uninstall()
        {
            Application.quitting -= HandleQuitting;

            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }

            _read = null;
            HourElapsed = null;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 0.5f;
                try { Ticked?.Invoke(); }
                catch (Exception e) { _log("CLOCK: tick handler threw: " + e.Message); }
            }

            if (_read == null) return;

            int now;
            try { now = _read(); }
            catch { return; }

            if (now == _last) return;

            var first = _last == int.MinValue;
            _last = now;
            if (first) return;

            try { HourElapsed?.Invoke(); }
            catch (Exception e) { _log("CLOCK: hour handler threw: " + e.Message); }
        }

        /// <summary>Day number, used to roll the market regime and reset the daily total.</summary>
        public static int CurrentDay()
        {
            try { return _read == null ? 0 : _read() / 24; }
            catch { return 0; }
        }

        /// <summary>Current hour of day, 0-23, for session weighting.</summary>
        public static int HourOfDay()
        {
            try { return _read == null ? 12 : ((_read() % 24) + 24) % 24; }
            catch { return 12; }
        }

        // ---- discovery ----------------------------------------------------

        private static readonly string[] Preferred = { "totalHours", "TotalHours" };
        private static readonly string[] Fallback = { "currentHour", "CurrentHour", "hour", "Hour" };

        private static Func<int>? Discover(Action<string> log)
        {
            var found = new List<string>();

            // A monotonic total is best: it also ticks over midnight.
            var accessor = Scan(Preferred, found) ?? Scan(Fallback, found);

            log(found.Count == 0
                ? "CLOCK: found no hour-like members anywhere."
                : "CLOCK: candidates -> " + string.Join(", ", found.Take(12)));

            return accessor;
        }

        private static Func<int>? Scan(string[] names, List<string> found)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).Select(t => t!).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    foreach (var name in names)
                    {
                        var statics = TryStatic(type, name, found);
                        if (statics != null) return statics;

                        var viaSingleton = TrySingleton(type, name, found);
                        if (viaSingleton != null) return viaSingleton;
                    }
                }
            }

            return null;
        }

        private static Func<int>? TryStatic(Type type, string name, List<string> found)
        {
            var p = type.GetProperty(name, GameBridge.AnyStatic);
            if (p != null && IsNumeric(p.PropertyType))
            {
                found.Add(type.Name + "." + name + " (static prop)");
                return () => Convert.ToInt32(p.GetValue(null));
            }

            var f = type.GetField(name, GameBridge.AnyStatic);
            if (f != null && IsNumeric(f.FieldType))
            {
                found.Add(type.Name + "." + name + " (static field)");
                return () => Convert.ToInt32(f.GetValue(null));
            }

            return null;
        }

        /// <summary>Most managers here are singletons, so check Instance.member too.</summary>
        private static Func<int>? TrySingleton(Type type, string name, List<string> found)
        {
            MemberInfo? holder = type.GetProperty("Instance", GameBridge.AnyStatic);
            holder ??= type.GetField("Instance", GameBridge.AnyStatic);
            holder ??= type.GetProperty("instance", GameBridge.AnyStatic);
            holder ??= type.GetField("instance", GameBridge.AnyStatic);

            if (holder == null) return null;

            Func<object?> getInstance = holder is PropertyInfo prop
                ? () => prop.GetValue(null)
                : () => ((FieldInfo)holder).GetValue(null);

            object? instance;
            try { instance = getInstance(); }
            catch { return null; }

            if (instance == null) return null;

            var instanceType = instance.GetType();

            var p = instanceType.GetProperty(name, GameBridge.AnyInstance);
            if (p != null && IsNumeric(p.PropertyType))
            {
                found.Add(type.Name + ".Instance." + name + " (prop)");
                return () => Convert.ToInt32(p.GetValue(getInstance()));
            }

            var f = instanceType.GetField(name, GameBridge.AnyInstance);
            if (f != null && IsNumeric(f.FieldType))
            {
                found.Add(type.Name + ".Instance." + name + " (field)");
                return () => Convert.ToInt32(f.GetValue(getInstance()));
            }

            return null;
        }

        private static bool IsNumeric(Type t) =>
            t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long);
    }
}
