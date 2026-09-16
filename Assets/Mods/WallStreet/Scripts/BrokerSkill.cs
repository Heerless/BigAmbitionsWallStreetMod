#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WallStreet
{
    /// <summary>
    /// Standalone custom-skill injection - no third-party library.
    /// Skills are SkillData objects held by BigAmbitions.Characters.Skills.SkillHelper,
    /// populated from Addressables. We clone a vanilla skill, rename it, and add the
    /// clone back into that same collection.
    /// </summary>
    public static class BrokerSkill
    {
        public const string SkillName = "wallstreet:skill_broker";
        public const string DisplayName = "Broker";

        // Lawyer is the closest vanilla analogue: an office job hired from City Workforce Inc.
        private const string CloneFrom = "ba:skill_lawyer";
        // Tuned so a top-skill broker asks ~$300/hr: the game scales asking wage by
        // roughly (0.69 + 0.86 x skill), a fixed ~2.2x spread. Vanilla Lawyer base is $50.
        private const float HourlyWage = 193f;

        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static Action<string> _log = _ => { };

        public static bool Install(Action<string> log)
        {
            _log = log;

            var helper = FindType("SkillHelper");
            if (helper == null)
            {
                log("SKILL: SkillHelper type not found.");
                return false;
            }

            LogSkillNames(helper);

            var source = GetSkill(helper, CloneFrom);
            if (source == null)
            {
                log("SKILL: could not resolve source skill " + CloneFrom);
                return false;
            }

            var dataType = source.GetType();
            log("SKILL: SkillData type is " + dataType.FullName);
            foreach (var f in dataType.GetFields(AnyInstance))
                log("SKILL: field " + f.FieldType.Name + " " + f.Name + " = " + SafeValue(f, source));

            if (GetSkill(helper, SkillName) != null)
            {
                log("SKILL: " + SkillName + " already present.");
                return true;
            }

            var clone = Clone(source, dataType);
            if (clone == null)
            {
                log("SKILL: clone failed.");
                return false;
            }

            if (!SetMember(clone, dataType, "skillName", SkillName))
                return false;

            SetMember(clone, dataType, "displayName", DisplayName);
            SetMember(clone, dataType, "baseHourlyWage", HourlyWage);

            if (!Inject(helper, clone, dataType))
                return false;

            // A half-registered skill is worse than none: the game's daily rollup calls
            // GetTrainingCost and the wage helpers for every employee, and a null lookup
            // there throws inside GameManager.NewDay(), which silently zeroes every
            // income statement in the save. Prove the skill survives those paths, and
            // undo the injection if it does not.
            if (Validate(helper, dataType))
                return true;

            Rollback(helper, dataType);
            return false;
        }

        /// <summary>Exercises the game code paths that run during the daily rollup.</summary>
        private static bool Validate(Type helper, Type dataType)
        {
            if (GetSkill(helper, SkillName) == null)
            {
                _log("VALIDATE: GetData(" + SkillName + ") is still null after injection.");
                return false;
            }

            var employeeHelper = FindType("EmployeeHelper");
            if (employeeHelper == null)
            {
                _log("VALIDATE: EmployeeHelper not found; skipping wage checks.");
                return true;
            }

            foreach (var name in new[] { "CalculateHourlyWageForSkill", "GetWageRangeForSkill" })
            {
                var method = employeeHelper.GetMethods(PublicStatic).FirstOrDefault(m =>
                    m.Name == name &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(string));

                if (method == null) continue;

                try
                {
                    method.Invoke(null, new object[] { SkillName });
                    _log("VALIDATE: " + name + " OK.");
                }
                catch (Exception e)
                {
                    _log("VALIDATE: " + name + " threw for our skill: " + (e.InnerException?.Message ?? e.Message));
                    return false;
                }
            }

            return true;
        }

        /// <summary>Removes our clone in place, leaving every other entry untouched.</summary>
        private static void Rollback(Type helper, Type dataType)
        {
            try
            {
                foreach (var field in helper.GetFields(AnyStatic))
                {
                    var value = field.GetValue(null);

                    if (value is IDictionary map && map.Contains(SkillName))
                    {
                        map.Remove(SkillName);
                        _log("ROLLBACK: Broker removed from " + field.Name + ".");
                        return;
                    }

                    if (value is IList list)
                    {
                        for (var i = list.Count - 1; i >= 0; i--)
                        {
                            if (list[i] != null && GetMemberValue(list[i]!, "skillName") as string == SkillName)
                            {
                                list.RemoveAt(i);
                                _log("ROLLBACK: Broker removed from " + field.Name + ".");
                                return;
                            }
                        }
                    }
                }

                _log("ROLLBACK: nothing to remove.");
            }
            catch (Exception e)
            {
                _log("ROLLBACK FAILED: " + e.Message + " - restart the game to be safe.");
            }
        }

        private static object? GetMemberValue(object target, string name)
        {
            try { return target.GetType().GetField(name, AnyInstance)?.GetValue(target); }
            catch { return null; }
        }

        /// <summary>
        /// Brokers are deliberately headhunter-only.
        /// <para>
        /// Headhunters search every registered skill, so simply existing makes a Broker
        /// findable through one. Recruitment agencies instead work from their own roster,
        /// <c>Buildings.RecruitmentAgencySettings.availableEmployeeSkills</c> - we never add
        /// ourselves to it, which is what keeps Brokers off the ordinary job market and makes
        /// staffing a floor an expensive, deliberate act.
        /// </para>
        /// To reverse this decision, append <see cref="SkillName"/> to that string array on
        /// the agencies that should carry it. This method only checks that has not happened
        /// by accident - for instance if a future patch has cloned skills inherit their
        /// source's roster.
        /// </summary>
        public static void VerifyHeadhunterOnly(Action<string> log)
        {
            _log = log;

            var settingsType = FindType("RecruitmentAgencySettings");
            if (settingsType == null)
            {
                log("AGENCY: RecruitmentAgencySettings type not found; cannot verify.");
                return;
            }

            var field = settingsType.GetField("availableEmployeeSkills", AnyInstance);
            if (field == null)
            {
                log("AGENCY: availableEmployeeSkills is gone; roster shape changed. Members: " +
                    string.Join(", ", settingsType.GetFields(AnyInstance).Select(f => f.FieldType.Name + " " + f.Name)));
                return;
            }

            var leaked = 0;
            foreach (var settings in Resources.FindObjectsOfTypeAll(settingsType))
            {
                if (field.GetValue(settings) is string[] roster && roster.Contains(SkillName))
                {
                    field.SetValue(settings, roster.Where(s => s != SkillName).ToArray());
                    leaked++;
                }
            }

            log(leaked == 0
                ? "AGENCY: confirmed headhunter-only; Broker is on no agency roster."
                : "AGENCY: removed Broker from " + leaked + " agency roster(s) to keep it headhunter-only.");
        }

        /// <summary>
        /// Adds the clone to the skill roster.
        /// <para>
        /// OnSkillDataLoaded <em>replaces</em> the roster rather than appending to it, so it
        /// must always be handed the complete set. Passing a partial list silently drops
        /// every skill left out, which breaks wage and training lookups for ordinary
        /// employees and takes GameManager.NewDay() down with them.
        /// </para>
        /// </summary>
        private static bool Inject(Type helper, object clone, Type dataType)
        {
            // Add in place wherever possible. Reloading the roster rebuilds it in whatever
            // order we hand back, and SkillData carries tagIndexes - positional indices that
            // employee job demands resolve against. Reorder those and every employee reads
            // their demands off the wrong tags.
            foreach (var field in helper.GetFields(AnyStatic))
            {
                var value = field.GetValue(null);
                if (value == null) continue;

                if (value is IDictionary map)
                {
                    if (!AcceptsSkillData(map, dataType)) continue;

                    try
                    {
                        map[SkillName] = clone;
                        _log("SKILL: added to dictionary " + helper.Name + "." + field.Name +
                             " in place (" + map.Count + " skills).");
                        return true;
                    }
                    catch (Exception e)
                    {
                        _log("SKILL: dictionary insert failed: " + e.Message);
                    }

                    continue;
                }

                if (value is IList list && list.Count > 0 && dataType.IsInstanceOfType(list[0]))
                {
                    try
                    {
                        list.Add(clone);
                        _log("SKILL: appended to list " + helper.Name + "." + field.Name +
                             " in place (" + list.Count + " skills).");
                        return true;
                    }
                    catch (Exception e)
                    {
                        _log("SKILL: list append failed: " + e.Message);
                    }
                }
            }

            // Properties can hide the store too.
            foreach (var prop in helper.GetProperties(AnyStatic))
            {
                object? value;
                try { value = prop.GetValue(null); } catch { continue; }

                if (value is IDictionary pmap && AcceptsSkillData(pmap, dataType))
                {
                    try
                    {
                        pmap[SkillName] = clone;
                        _log("SKILL: added to dictionary property " + prop.Name + " in place.");
                        return true;
                    }
                    catch (Exception e) { _log("SKILL: property insert failed: " + e.Message); }
                }
            }

            ReportStores(helper, log: _log);
            _log("SKILL: refusing to reload the roster - that reorders tag indices and breaks " +
                 "employee job demands. Broker will not be installed.");
            return false;
        }

        /// <summary>True when a dictionary's values are SkillData keyed by string.</summary>
        private static bool AcceptsSkillData(IDictionary map, Type dataType)
        {
            foreach (DictionaryEntry entry in map)
                return entry.Key is string && dataType.IsInstanceOfType(entry.Value);

            return false;
        }

        /// <summary>
        /// Describes every static member holding a collection, with its real runtime type,
        /// element types and size - so the store can be targeted exactly next time instead
        /// of by another round of guesses.
        /// </summary>
        private static void ReportStores(Type helper, Action<string> log)
        {
            foreach (var field in helper.GetFields(AnyStatic))
                Describe(field.Name, Safe(() => field.GetValue(null)), log);

            foreach (var prop in helper.GetProperties(AnyStatic))
                Describe(prop.Name, Safe(() => prop.GetValue(null)), log);
        }

        private static void Describe(string name, object? value, Action<string> log)
        {
            if (value == null) { log("STORE: " + name + " = null"); return; }

            if (value is IDictionary map)
            {
                string keyType = "?", valueType = "?";
                foreach (DictionaryEntry e in map)
                {
                    keyType = e.Key?.GetType().Name ?? "null";
                    valueType = e.Value?.GetType().Name ?? "null";
                    break;
                }

                log("STORE: " + name + " = Dictionary<" + keyType + ", " + valueType + "> count=" + map.Count);
                return;
            }

            if (value is IList list)
            {
                var elem = list.Count > 0 ? list[0]?.GetType().Name ?? "null" : "empty";
                log("STORE: " + name + " = List<" + elem + "> count=" + list.Count);
                return;
            }

            log("STORE: " + name + " = " + value.GetType().Name);
        }

        private static object? Safe(Func<object?> get)
        {
            try { return get(); } catch { return null; }
        }

        private static IEnumerable<string> AllSkillNames(Type helper)
        {
            var prop = helper.GetProperty("AllSkillNames", PublicStatic);
            if (prop == null) yield break;

            object? value;
            try { value = prop.GetValue(null); }
            catch { yield break; }

            if (value is not IEnumerable names) yield break;

            foreach (var item in names)
            {
                var name = item as string;
                if (!string.IsNullOrEmpty(name) && name != SkillName) yield return name!;
            }
        }

        private static bool CallOnSkillDataLoaded(Type helper, object list)
        {
            var method = helper.GetMethods(PublicStatic)
                .FirstOrDefault(m => m.Name == "OnSkillDataLoaded" && m.GetParameters().Length == 1);

            if (method == null) return false;

            try
            {
                method.Invoke(null, new[] { list });
                _log("SKILL: injected via OnSkillDataLoaded.");
                return true;
            }
            catch (Exception e)
            {
                var inner = e.InnerException == null ? e.Message : e.InnerException.Message;
                _log("SKILL: OnSkillDataLoaded failed: " + inner);
                return false;
            }
        }

        private static object MakeTypedList(Type dataType, List<object> items)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(dataType));
            foreach (var item in items) list.Add(item);
            return list;
        }

        private static object? Clone(object source, Type dataType)
        {
            if (source is UnityEngine.Object unityObject)
            {
                var copy = UnityEngine.Object.Instantiate(unityObject);
                UnityEngine.Object.DontDestroyOnLoad(copy);
                copy.name = DisplayName;
                return copy;
            }

            var fresh = Activator.CreateInstance(dataType);
            foreach (var f in dataType.GetFields(AnyInstance))
            {
                try { f.SetValue(fresh, f.GetValue(source)); } catch { }
            }
            return fresh;
        }

        private static bool SetMember(object target, Type type, string name, object value)
        {
            var field = type.GetField(name, AnyInstance);
            if (field != null)
            {
                try
                {
                    field.SetValue(target, Convert.ChangeType(value, field.FieldType));
                    return true;
                }
                catch (Exception e)
                {
                    _log("SKILL: could not set field " + name + ": " + e.Message);
                    return false;
                }
            }

            var prop = type.GetProperty(name, AnyInstance);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    prop.SetValue(target, Convert.ChangeType(value, prop.PropertyType));
                    return true;
                }
                catch (Exception e)
                {
                    _log("SKILL: could not set property " + name + ": " + e.Message);
                    return false;
                }
            }

            _log("SKILL: no member named " + name + " on " + type.Name);
            return false;
        }

        private static object? GetSkill(Type helper, string name)
        {
            var method = helper.GetMethods(PublicStatic).FirstOrDefault(m =>
                m.Name == "GetData" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(string));

            if (method == null) return null;

            try { return method.Invoke(null, new object[] { name }); }
            catch { return null; }
        }

        private static void LogSkillNames(Type helper)
        {
            var prop = helper.GetProperty("AllSkillNames", PublicStatic);
            if (prop == null) return;

            try
            {
                if (prop.GetValue(null) is IEnumerable names)
                    _log("SKILL: existing = " + string.Join(", ", names.Cast<object>().Select(o => o == null ? "null" : o.ToString())));
            }
            catch (Exception e)
            {
                _log("SKILL: AllSkillNames threw: " + e.Message);
            }
        }

        private static string SafeValue(FieldInfo field, object target)
        {
            try
            {
                var v = field.GetValue(target);
                return v == null ? "null" : v.ToString();
            }
            catch
            {
                return "<unreadable>";
            }
        }

        private static Type? FindType(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).Select(t => t!).ToArray();
                }
                catch
                {
                    continue;
                }

                var hit = types.FirstOrDefault(t => t.Name == simpleName);
                if (hit != null) return hit;
            }

            return null;
        }
    }
}
