#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Dumps the real member lists of the game types the mod touches.
    /// <para>
    /// The game assemblies are imported into this project, so their API can be read here
    /// rather than probed at runtime. Writing typed code against these and letting the
    /// compiler check it beats reflecting on guessed member names.
    /// </para>
    /// </summary>
    public static class WallStreetApiDump
    {
        private static readonly string[] Wanted =
        {
            "EconoViewBusinessDetails", "EconoViewIncomeStatement", "IncomeStatementRow",
        };

        public static void Dump()
        {
            var output = new StringBuilder();

            foreach (var name in Wanted)
            {
                var types = FindTypes(name);
                if (types.Length == 0)
                {
                    output.AppendLine("### " + name + ": NOT FOUND").AppendLine();
                    continue;
                }

                foreach (var type in types.Take(2))
                    Describe(type, output);
            }

            FindMoneyMethods(output);

            var path = Path.Combine(Directory.GetCurrentDirectory(), "api-dump.txt");
            File.WriteAllText(path, output.ToString());
            Debug.Log("[CI] API dump written to " + path + " (" + output.Length + " chars)");
        }

        private static void Describe(Type type, StringBuilder output)
        {
            output.AppendLine("### " + type.FullName + (type.IsEnum ? "  [enum]" : ""));

            if (type.IsEnum)
            {
                output.AppendLine("  values: " + string.Join(", ", Enum.GetNames(type).Take(40)));
                output.AppendLine();
                return;
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance |
                                       BindingFlags.Static | BindingFlags.FlattenHierarchy;

            foreach (var f in type.GetFields(Flags).Take(60))
                output.AppendLine("  " + (f.IsStatic ? "static " : "") + "field " + Pretty(f.FieldType) + " " + f.Name);

            foreach (var p in type.GetProperties(Flags).Take(60))
                output.AppendLine("  " + "prop  " + Pretty(p.PropertyType) + " " + p.Name);

            foreach (var m in type.GetMethods(Flags).Where(m => !m.IsSpecialName).Take(70))
                output.AppendLine("  " + (m.IsStatic ? "static " : "") + "mth   " + Pretty(m.ReturnType) + " " +
                                  m.Name + "(" + string.Join(", ",
                                      m.GetParameters().Select(x => Pretty(x.ParameterType) + " " + x.Name)) + ")");

            output.AppendLine();
        }

        /// <summary>Money moves somewhere; this finds every plausible entry point.</summary>
        private static void FindMoneyMethods(StringBuilder output)
        {
            output.AppendLine("### MONEY / INCOME / SAVE ENTRY POINTS");

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.Contains("BigAmbitions") && asm.GetName().Name != "Assembly-CSharp") continue;

                foreach (var type in SafeTypes(asm))
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static |
                                                  BindingFlags.Instance | BindingFlags.NonPublic);
                    }
                    catch { continue; }

                    foreach (var m in methods)
                    {
                        var n = m.Name;
                        var interesting =
                            n.Contains("ChangeMoney") || n.Contains("AddMoney") || n.Contains("Transaction") ||
                            n.Contains("IncomeStatement") || n.Contains("SaveData") || n.Contains("ModSave") ||
                            n.Contains("PersistentData") || n.Contains("CustomData");

                        if (!interesting) continue;

                        output.AppendLine("  " + type.FullName + "." + n + "(" + string.Join(", ",
                            m.GetParameters().Select(x => Pretty(x.ParameterType))) + ") -> " + Pretty(m.ReturnType));
                    }
                }
            }
        }

        private static Type[] FindTypes(string simpleName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(SafeTypes)
                .Where(t => t.Name == simpleName)
                .ToArray();
        }

        private static Type[] SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            catch { return Array.Empty<Type>(); }
        }

        private static string Pretty(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var name = t.Name.Substring(0, t.Name.IndexOf('`'));
            return name + "<" + string.Join(",", t.GetGenericArguments().Select(Pretty)) + ">";
        }
    }
}
#endif
