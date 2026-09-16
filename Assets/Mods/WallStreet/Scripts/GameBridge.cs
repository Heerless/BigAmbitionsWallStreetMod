#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WallStreet
{
    /// <summary>
    /// Reflection helpers over the game assemblies. The modding API covers business types
    /// and vehicles only, so everything else - money, the clock, employees - is reached
    /// by reflection and reported as it is found, rather than assumed.
    /// </summary>
    public static class GameBridge
    {
        public const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        public const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly Dictionary<string, Type?> TypeCache = new();

        public static Type? FindType(string simpleName)
        {
            if (TypeCache.TryGetValue(simpleName, out var cached)) return cached;

            Type? found = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).Select(t => t!).ToArray(); }
                catch { continue; }

                found = types.FirstOrDefault(t => t.Name == simpleName);
                if (found != null) break;
            }

            TypeCache[simpleName] = found;
            return found;
        }

        /// <summary>Logs the public surface of a type so the next change can be written blind-free.</summary>
        public static void Dump(string simpleName, Action<string> log, int limit = 40)
        {
            var type = FindType(simpleName);
            if (type == null) { log("DUMP " + simpleName + ": not found."); return; }

            log("DUMP " + simpleName + " = " + type.FullName);

            foreach (var m in type.GetMethods(AnyStatic).Where(m => !m.IsSpecialName).Take(limit))
                log("  static " + Sig(m));

            foreach (var p in type.GetProperties(AnyStatic).Take(limit))
                log("  static prop " + p.PropertyType.Name + " " + p.Name);

            foreach (var e in type.GetEvents(AnyStatic).Take(limit))
                log("  static event " + e.EventHandlerType?.Name + " " + e.Name);

            foreach (var f in type.GetFields(AnyStatic).Take(limit))
                log("  static field " + f.FieldType.Name + " " + f.Name);
        }

        public static void DumpInstance(object instance, Action<string> log, int limit = 40)
        {
            var type = instance.GetType();
            log("DUMP instance " + type.FullName);

            foreach (var f in type.GetFields(AnyInstance).Take(limit))
                log("  field " + f.FieldType.Name + " " + f.Name);

            foreach (var p in type.GetProperties(AnyInstance).Take(limit))
                log("  prop " + p.PropertyType.Name + " " + p.Name);

            foreach (var m in type.GetMethods(AnyInstance).Where(m => !m.IsSpecialName && m.DeclaringType == type).Take(limit))
                log("  method " + Sig(m));
        }

        public static string Sig(MethodInfo m)
        {
            var args = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            return m.ReturnType.Name + " " + m.Name + "(" + args + ")";
        }

        public static object? GetMember(object target, string name)
        {
            var type = target.GetType();

            var f = type.GetField(name, AnyInstance);
            if (f != null) { try { return f.GetValue(target); } catch { return null; } }

            var p = type.GetProperty(name, AnyInstance);
            if (p != null && p.CanRead) { try { return p.GetValue(target); } catch { return null; } }

            return null;
        }

        public static IEnumerable<object> AsEnumerable(object? value)
        {
            if (value is IEnumerable e && value is not string)
                foreach (var item in e)
                    if (item != null) yield return item;
        }

        /// <summary>
        /// Subscribes to the first static event on <paramref name="typeName"/> matching any
        /// of <paramref name="eventNames"/>. Returns an unsubscribe action, or null.
        /// </summary>
        public static Action? SubscribeStaticEvent(string typeName, string[] eventNames, Action callback, Action<string> log)
        {
            var type = FindType(typeName);
            if (type == null) { log("EVENT: type " + typeName + " not found."); return null; }

            foreach (var name in eventNames)
            {
                var evt = type.GetEvent(name, AnyStatic);
                if (evt?.EventHandlerType == null) continue;

                try
                {
                    var handler = BuildHandler(evt.EventHandlerType, callback);
                    if (handler == null) { log("EVENT: could not adapt handler for " + name); continue; }

                    evt.AddEventHandler(null, handler);
                    log("EVENT: subscribed to " + typeName + "." + name);
                    return () => { try { evt.RemoveEventHandler(null, handler); } catch { } };
                }
                catch (Exception ex)
                {
                    log("EVENT: subscribe to " + name + " failed: " + ex.Message);
                }
            }

            log("EVENT: none of [" + string.Join(", ", eventNames) + "] found on " + typeName +
                ". Available: " + string.Join(", ", type.GetEvents(AnyStatic).Select(e => e.Name)));
            return null;
        }

        /// <summary>
        /// Adapts a parameterless callback to the delegate shape an event expects.
        /// Value-type parameters rule out a single object-taking shim, so the concrete
        /// shapes the game actually uses for clock events are matched explicitly.
        /// </summary>
        private static Delegate? BuildHandler(Type delegateType, Action callback)
        {
            var invoke = delegateType.GetMethod("Invoke");
            if (invoke == null || invoke.ReturnType != typeof(void)) return null;

            var shim = new EventShim(callback);
            var parameters = invoke.GetParameters();

            string? shimName = parameters.Length switch
            {
                0 => nameof(EventShim.On0),
                1 when parameters[0].ParameterType == typeof(int) => nameof(EventShim.OnInt),
                1 when parameters[0].ParameterType == typeof(float) => nameof(EventShim.OnFloat),
                1 when !parameters[0].ParameterType.IsValueType => nameof(EventShim.OnObject),
                2 when !parameters[0].ParameterType.IsValueType
                       && !parameters[1].ParameterType.IsValueType => nameof(EventShim.OnPair),
                _ => null
            };

            if (shimName == null) return null;

            var method = typeof(EventShim).GetMethod(shimName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return null;

            return Delegate.CreateDelegate(delegateType, shim, method, false);
        }

        private sealed class EventShim
        {
            private readonly Action _callback;
            public EventShim(Action callback) => _callback = callback;

            public void On0() => _callback();
            public void OnInt(int _) => _callback();
            public void OnFloat(float _) => _callback();
            public void OnObject(object _) => _callback();
            public void OnPair(object _, object __) => _callback();
        }
    }
}
