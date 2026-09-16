#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WallStreet
{
    /// <summary>
    /// One trading floor's configuration. Settings belong to a brokerage, not to the save,
    /// so two floors can run different capital and risk.
    /// </summary>
    [Serializable]
    public class FloorState
    {
        public string address = "";

        /// <summary>Capital committed to this floor, in dollars. Real money, held out of the balance.</summary>
        public long committedCapital;

        /// <summary>0.5 conservative through 2.0 aggressive.</summary>
        public float risk = 1.0f;

        /// <summary>
        /// Without a compliance officer, desks can blow up. The officer is insurance against
        /// the tail, not a smoothing filter.
        /// </summary>
        public bool complianceRetained = true;

        /// <summary>
        /// The compliance officer takes a share of what the floor makes rather than a flat
        /// retainer. A retainer could push a quiet floor into the red on its own; a cut
        /// never can, so retaining one can only cost you upside, never solvency.
        /// </summary>
        public const float ComplianceProfitShare = 0.02f;

        public string Describe() =>
            "$" + committedCapital.ToString("N0") + " at " + (risk * 100f).ToString("0") + "%" +
            (complianceRetained ? " with compliance" : " uninsured");
    }

    /// <summary>
    /// Persists floor settings across sessions.
    /// <para>
    /// There is no general save API for mods - only <c>IPersistableOption</c> - so this
    /// takes the same route Empire Casino does for its VIP and blacklist data: JSON in
    /// PlayerPrefs, keyed per save so slots do not bleed into each other.
    /// </para>
    /// </summary>
    public static class FloorSave
    {
        private const string KeyPrefix = "WallStreet.Floors.";

        private static readonly Dictionary<string, FloorState> Floors = new();
        private static string _saveKey = KeyPrefix + "default";
        private static Action<string> _log = _ => { };
        private static bool _loaded;

        /// <summary>
        /// Binds to a save.
        /// <para>
        /// The key must be identical every launch. An earlier version hashed
        /// <c>Application.persistentDataPath</c>, and string hash codes are not stable
        /// across processes - so every launch wrote under a fresh key, read back nothing,
        /// and silently lost whatever capital was committed. Only stable identifiers here.
        /// </para>
        /// </summary>
        public static void Bind(string saveName, Action<string> log)
        {
            _log = log;
            _saveKey = KeyPrefix + Sanitise(saveName);
            _loaded = false;
            Floors.Clear();
            log("SAVE: bound to key " + _saveKey);
            Load();
        }

        private static string Sanitise(string name)
        {
            if (string.IsNullOrEmpty(name)) return "shared";

            var clean = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                clean.Append(char.IsLetterOrDigit(c) ? c : '_');

            return clean.ToString();
        }

        /// <summary>Settings for one brokerage, created on first use.</summary>
        public static FloorState For(Address address)
        {
            if (!_loaded) Load();

            var key = Key(address);
            if (Floors.TryGetValue(key, out var existing)) return existing;

            var state = new FloorState { address = key };
            Floors[key] = state;
            return state;
        }

        public static IEnumerable<FloorState> All()
        {
            if (!_loaded) Load();
            return Floors.Values;
        }

        private static string Key(Address address) => address.streetName + "#" + address.streetNumber;

        // Records are written by hand rather than with JsonUtility, which serialised the
        // whole bag to "{}" and reported success - losing committed capital silently. The
        // format is one floor per line: address | capital | risk | compliance.
        private const char FieldSeparator = '|';
        private const char RecordSeparator = '\n';

        private static void Load()
        {
            _loaded = true;

            try
            {
                var raw = UnityEngine.PlayerPrefs.GetString(_saveKey);
                if (string.IsNullOrEmpty(raw))
                {
                    _log("SAVE: nothing stored yet under " + _saveKey + ".");
                    return;
                }

                foreach (var line in raw.Split(RecordSeparator))
                {
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split(FieldSeparator);
                    if (parts.Length < 4) continue;

                    var state = new FloorState { address = parts[0] };

                    long.TryParse(parts[1], out var capital);
                    state.committedCapital = capital;

                    state.risk = float.TryParse(parts[2], out var risk) ? risk : 1.0f;
                    state.complianceRetained = parts[3] == "1";

                    Floors[state.address] = state;
                }

                _log("SAVE: restored " + Floors.Count + " floor(s) from " + _saveKey + ".");
            }
            catch (Exception e)
            {
                _log("SAVE: could not read stored floors: " + e.Message);
            }
        }

        public static void SaveAll()
        {
            try
            {
                var text = new System.Text.StringBuilder();

                foreach (var floor in Floors.Values)
                {
                    if (string.IsNullOrEmpty(floor.address)) continue;

                    text.Append(floor.address).Append(FieldSeparator)
                        .Append(floor.committedCapital).Append(FieldSeparator)
                        .Append(floor.risk.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(FieldSeparator)
                        .Append(floor.complianceRetained ? '1' : '0')
                        .Append(RecordSeparator);
                }

                var payload = text.ToString();
                UnityEngine.PlayerPrefs.SetString(_saveKey, payload);
                UnityEngine.PlayerPrefs.Save();

                // Read straight back: a silent write failure is what cost the last attempt.
                var check = UnityEngine.PlayerPrefs.GetString(_saveKey);
                _log(check == payload
                    ? "SAVE: stored " + Floors.Count + " floor(s), verified."
                    : "SAVE: WROTE BUT READ BACK DIFFERENT - stored " + check.Length +
                      " chars, expected " + payload.Length + ".");
            }
            catch (Exception e)
            {
                _log("SAVE: could not store floors: " + e.Message);
            }
        }

        /// <summary>
        /// Moves money between the player's balance and a floor, booked against that
        /// brokerage. Returns what actually moved: less than asked if it cannot be afforded,
        /// or if more was withdrawn than is committed.
        /// </summary>
        public static long Commit(Address address, long amount, Action<string> log)
        {
            var state = For(address);

            if (amount < 0)
                amount = -Math.Min(-amount, state.committedCapital);

            if (amount == 0) return 0;

            if (!FloorRunner.ChangeMoney(address, -amount))
            {
                log("CAPITAL: $" + Math.Abs(amount).ToString("N0") + " refused.");
                return 0;
            }

            state.committedCapital += amount;
            SaveAll();

            log("CAPITAL: " + (amount > 0 ? "committed $" : "withdrew $") +
                Math.Abs(amount).ToString("N0") + "; floor holds $" + state.committedCapital.ToString("N0"));

            return amount;
        }
    }
}
