#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WallStreet
{
    /// <summary>
    /// Maps the BizMan phone app so a Brokerage panel can be attached to it.
    /// <para>
    /// BizMan has no modding API. It does have a precedent though: BizManFactory is a panel
    /// shown only for factory businesses, so a per-business-type section is clearly a shape
    /// the app already supports. This logs the app's fields and object hierarchy, plus the
    /// factory panel's structure as a template to copy.
    /// </para>
    /// Diagnostic only - it changes nothing.
    /// </summary>
    public static class BizManProbe
    {
        public static void Run(Action<string> log)
        {
            DumpComponent("BizMan", log, hierarchyDepth: 3);
            DumpComponent("BizManFactory", log, hierarchyDepth: 2);
        }

        private static void DumpComponent(string typeName, Action<string> log, int hierarchyDepth)
        {
            var type = GameBridge.FindType(typeName);
            if (type == null)
            {
                log("BIZMAN: type " + typeName + " not found.");
                return;
            }

            var instances = Resources.FindObjectsOfTypeAll(type);
            log("BIZMAN: " + typeName + " -> " + type.FullName + " (" + instances.Length + " instance(s))");

            foreach (var field in type.GetFields(GameBridge.AnyInstance).Take(45))
                log("  fld " + Pretty(field.FieldType) + " " + field.Name);

            foreach (var method in type.GetMethods(GameBridge.AnyInstance)
                         .Where(m => !m.IsSpecialName && m.DeclaringType == type).Take(25))
                log("  mth " + GameBridge.Sig(method));

            if (instances.FirstOrDefault() is not Component component) return;

            log("  --- hierarchy from " + component.gameObject.name + " ---");
            WalkUp(component.transform, log);
            Walk(component.transform, 0, hierarchyDepth, log);
        }

        /// <summary>Shows where this panel sits, so a sibling can be added alongside it.</summary>
        private static void WalkUp(Transform t, Action<string> log)
        {
            var path = t.name;
            var parent = t.parent;
            var hops = 0;

            while (parent != null && hops++ < 6)
            {
                path = parent.name + " / " + path;
                parent = parent.parent;
            }

            log("  path: " + path);
        }

        private static void Walk(Transform t, int depth, int maxDepth, Action<string> log)
        {
            if (depth > maxDepth) return;

            foreach (Transform child in t)
            {
                var components = child.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c!.GetType().Name)
                    .Where(n => n != "RectTransform" && n != "CanvasRenderer")
                    .Take(4);

                log("  " + new string(' ', (depth + 1) * 2) + child.name + "  [" + string.Join(", ", components) + "]");
                Walk(child, depth + 1, maxDepth, log);
            }
        }

        private static string Pretty(Type t)
        {
            if (!t.IsGenericType) return t.Name;

            var name = t.Name.Substring(0, t.Name.IndexOf('`'));
            return name + "<" + string.Join(",", t.GetGenericArguments().Select(a => a.Name)) + ">";
        }
    }
}
