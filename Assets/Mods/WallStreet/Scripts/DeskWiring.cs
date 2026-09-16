#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;

namespace WallStreet
{
    /// <summary>
    /// Makes Brokers assignable to desks.
    /// <para>
    /// Furniture carries a <c>suitableSkills</c> list naming which employee types may work
    /// at it. Item definitions are global, so adding Broker to office workstations makes
    /// them broker-capable everywhere - which is harmless, because a job is only offered
    /// where the business type also lists the skill, and only Brokerage does.
    /// </para>
    /// </summary>
    public static class DeskWiring
    {
        // No explicit item list any more, and deliberately so.
        //
        // An empty suitableSkills array means "this is furniture"; a non-empty one means
        // "this is a workstation someone is staffed at". Naming ba:itemname_officedesk1 and
        // friends wrote skills onto items that had none, promoting ordinary desks into
        // broker-only workstations in every office the player owns - which broke schedules
        // and job-demand satisfaction for existing staff empire-wide.
        //
        // Only ever append to items that already carry skills. Never create a workstation.

        /// <summary>
        /// Office skills that mark an item as a desk a white-collar employee works at.
        /// <para>
        /// This catches laptops, computers and gaming computers as well as desks. That is
        /// correct, not over-reach: carrying a non-empty suitableSkills array is what makes
        /// an item staffable at all, and a programmer really does work at a laptop. These
        /// were cut once on a wrong hunch and the brokerage stopped seeing its desks.
        /// </para>
        /// </summary>
        private static readonly string[] MarkerSkills =
        {
            "ba:skill_programmer",
            "ba:skill_graphicdesigner",
            "ba:skill_travelagent",
            "ba:skill_lawyer"
        };

        public static void Apply(Action<string> log)
        {
            if (ItemsGetter.AllItems == null)
            {
                log("DESK: ItemsGetter.AllItems is null; cannot wire desks.");
                return;
            }

            var wired = new List<string>();
            var inspected = 0;

            foreach (var item in ItemsGetter.AllItems)
            {
                if (item == null || string.IsNullOrEmpty(item.itemName)) continue;

                var field = item.GetType().GetField("suitableSkills", GameBridge.AnyInstance);
                if (field == null) continue;

                inspected++;
                if (field.GetValue(item) is not string[] skills) continue;

                // Empty means furniture. Writing to it would invent a workstation.
                if (skills.Length == 0) continue;

                if (!skills.Any(s => MarkerSkills.Contains(s))) continue;
                if (skills.Contains(BrokerSkill.SkillName)) continue;

                try
                {
                    field.SetValue(item, skills.Concat(new[] { BrokerSkill.SkillName }).ToArray());
                    wired.Add(item.itemName);
                }
                catch (Exception e)
                {
                    log("DESK: failed on " + item.itemName + ": " + e.Message);
                }
            }

            if (inspected == 0)
            {
                log("DESK: no item carried a 'suitableSkills' field - the shape has changed.");
                return;
            }

            ReportWorkstations(log);

            log(wired.Count == 0
                ? "DESK: inspected " + inspected + " items, none matched as an office workstation."
                : "DESK: Brokers can now work at " + wired.Count + " item type(s): " + string.Join(", ", wired));
        }

        /// <summary>
        /// Lists every item the game treats as staffable, with the skills it accepts and
        /// its workstation type. The business was not recognising our desks, and guessing
        /// which item counts has cost several test cycles - this answers it outright.
        /// </summary>
        private static void ReportWorkstations(Action<string> log)
        {
            if (ItemsGetter.AllItems == null) return;

            var reported = 0;

            foreach (var item in ItemsGetter.AllItems)
            {
                if (item == null || string.IsNullOrEmpty(item.itemName)) continue;

                var type = item.GetType();
                if (type.GetField("suitableSkills", GameBridge.AnyInstance)?.GetValue(item) is not string[] skills)
                    continue;

                if (skills.Length == 0) continue;
                if (reported++ >= 40) break;

                var workstationType = type.GetField("workstationType", GameBridge.AnyInstance)?.GetValue(item);

                log("WS: " + item.itemName
                    + " | type=" + (workstationType?.ToString() ?? "-")
                    + " | skills=" + string.Join(",", skills.Select(s => s.Replace("ba:skill_", ""))));
            }

            log("WS: " + reported + " staffable item type(s) reported.");
        }
    }
}
