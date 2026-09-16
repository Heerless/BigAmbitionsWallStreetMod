#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WallStreet
{
    /// <summary>
    /// A Trading Floor tab inside BizMan.
    /// <para>
    /// BizMan has no modding API, but its structure is regular: tab buttons live under
    /// <c>Business/Menu</c> and their panels are siblings under
    /// <c>Business/Layout 30-70/Right</c>. Empire Casino adds a tab the same way.
    /// </para>
    /// The panel is built explicitly rather than by cloning whole elements - an earlier
    /// version inherited a header label and rendered every line at heading size. Only the
    /// font is borrowed, so the text matches the game without inheriting its layout.
    /// </summary>
    public static class BizManPanel
    {
        private const string TabName = "WallStreetTradingFloor";
        private const string PanelName = "WallStreetTradingFloorPanel";

        private static readonly Color Ink = new(0.92f, 0.95f, 0.98f);
        private static readonly Color Muted = new(0.62f, 0.69f, 0.78f);
        private static readonly Color Positive = new(0.35f, 0.78f, 0.60f);
        private static readonly Color Negative = new(0.90f, 0.42f, 0.38f);
        private static readonly Color Accent = new(0.93f, 0.66f, 0.24f);
        private static readonly Color ButtonFill = new(0.16f, 0.22f, 0.30f, 0.95f);

        private static Action<string> _log = _ => { };
        private static GameObject? _panel;
        private static GameObject? _tab;
        private static TMP_FontAsset? _font;
        private static float _baseFontSize = 24f;
        private static int _attempts;

        private static readonly Dictionary<string, TextMeshProUGUI> Values = new();

        private static void Report(string message)
        {
            if (_attempts <= 1 || _attempts % 120 == 0) _log(message);
        }

        // ---- which brokerage is selected -----------------------------------

        private static Component? _bizManBusiness;
        private static System.Reflection.MemberInfo? _addressMember;

        /// <summary>
        /// The address BizMan is currently showing, or null.
        /// <para>
        /// The one place reflection is still warranted: BizManBusiness is a UI component,
        /// not part of the game's data API, and only the member holding the selected
        /// address is needed. It is resolved once and cached.
        /// </para>
        /// </summary>
        private static Address? CurrentAddress()
        {
            if (_bizManBusiness == null)
            {
                var type = GameBridge.FindType("BizManBusiness");
                if (type == null) return null;

                _bizManBusiness = Resources.FindObjectsOfTypeAll(type).FirstOrDefault() as Component;
                if (_bizManBusiness == null) return null;

                _addressMember =
                    (System.Reflection.MemberInfo?)type.GetProperties(GameBridge.AnyInstance)
                        .FirstOrDefault(p => p.PropertyType == typeof(Address))
                    ?? type.GetFields(GameBridge.AnyInstance)
                        .FirstOrDefault(f => f.FieldType == typeof(Address));

                _log(_addressMember == null
                    ? "PANEL: BizManBusiness exposes no Address member; the tab cannot follow the selection."
                    : "PANEL: selection read from BizManBusiness." + _addressMember.Name);
            }

            try
            {
                var value = _addressMember switch
                {
                    System.Reflection.PropertyInfo p => p.GetValue(_bizManBusiness),
                    System.Reflection.FieldInfo f => f.GetValue(_bizManBusiness),
                    _ => null
                };

                return value as Address;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Runs an action against the selected brokerage, if one is selected.</summary>
        private static void WithCurrent(Action<Address> action)
        {
            var address = CurrentAddress();
            if (address == null)
            {
                _log("PANEL: no business selected.");
                return;
            }

            if (!FloorRunner.IsBrokerage(address))
            {
                _log("PANEL: " + address + " is not a brokerage.");
                return;
            }

            action(address);
            Refresh();
        }

        public static bool Install(Action<string> log)
        {
            _log = log;
            _attempts++;

            var bizMan = FindBizManRoot();
            if (bizMan == null)
            {
                Report("PANEL: BizMan root not found (attempt " + _attempts + ").");
                return false;
            }

            var menu = bizMan.Find("Business/Menu");
            var right = bizMan.Find("Business/Layout 30-70/Right");

            if (menu == null || right == null)
            {
                Report("PANEL: layout missing (menu=" + (menu != null) + ", right=" + (right != null) + ").");
                return false;
            }

            if (right.Find(PanelName) != null) return true;

            _font = BorrowFont(right);
            _panel = BuildPanel(right);
            _tab = BuildTab(menu, right);

            if (_panel == null || _tab == null)
            {
                log("PANEL: could not build (panel=" + (_panel != null) + ", tab=" + (_tab != null) + ").");
                return false;
            }

            log("PANEL: Trading Floor tab installed.");
            return true;
        }

        public static void Uninstall()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            if (_tab != null) UnityEngine.Object.Destroy(_tab);
            _panel = null;
            _tab = null;
            Values.Clear();
        }

        private static Transform? FindBizManRoot()
        {
            var type = GameBridge.FindType("BizMan");
            if (type == null) return null;
            return Resources.FindObjectsOfTypeAll(type).FirstOrDefault() is Component c ? c.transform : null;
        }

        /// <summary>
        /// Borrow the game's font <em>and its size</em>. Hard-coded point sizes rendered
        /// almost invisibly: this canvas works in its own units, so the only reliable
        /// reference is what a vanilla label actually uses.
        /// </summary>
        private static TMP_FontAsset? BorrowFont(Transform right)
        {
            foreach (Transform panel in right)
            {
                var text = panel.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text?.font == null) continue;

                _baseFontSize = text.fontSize > 1f ? text.fontSize : 24f;
                _log("PANEL: borrowed font " + text.font.name + " at " + _baseFontSize.ToString("0.#") +
                     " from " + panel.name);
                return text.font;
            }

            return null;
        }

        /// <summary>
        /// Builds the container by cloning a real BizMan panel and emptying it.
        /// <para>
        /// Copying a sibling's RectTransform values was not enough - the result spanned the
        /// whole screen and drew over the business card on the left. Where a panel sits
        /// depends on more than its own rect, so cloning one that already sits correctly
        /// inherits all of it, including whatever the layout relies on that is not visible
        /// from the outside.
        /// </para>
        /// </summary>
        private static GameObject? CloneVanillaPanel(Transform right)
        {
            Transform? template = right.Find("Insight") ?? right.Find("Presentation");

            if (template == null)
                foreach (Transform child in right) { template = child; break; }

            if (template == null)
            {
                _log("PANEL: no vanilla panel to clone.");
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, right);
            clone.name = PanelName;

            // Strip what it inherited: its children, and its behaviours, which would keep
            // running against a business they no longer describe.
            foreach (var child in clone.transform.Cast<Transform>().ToList())
                UnityEngine.Object.DestroyImmediate(child.gameObject);

            foreach (var component in clone.GetComponents<Component>())
            {
                if (component is RectTransform || component is CanvasRenderer) continue;
                UnityEngine.Object.DestroyImmediate(component);
            }

            var rect = clone.GetComponent<RectTransform>();
            _log("PANEL: cloned " + template.name + " as container (" +
                 rect.rect.width.ToString("0") + "x" + rect.rect.height.ToString("0") + ")");

            return clone;
        }

        // ---- tab ----------------------------------------------------------

        private static GameObject? BuildTab(Transform menu, Transform right)
        {
            var template = menu.Find("Settings") ?? menu.Find("Presentation");
            if (template == null)
            {
                _log("PANEL: no menu button to clone.");
                return null;
            }

            var tab = UnityEngine.Object.Instantiate(template.gameObject, menu);
            tab.name = TabName;
            tab.transform.SetAsLastSibling();

            StripLocalization(tab);
            SetText(tab, "Trading Floor");

            var button = tab.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ShowOnly(right));
            }

            return tab;
        }

        /// <summary>Exactly one panel under Right is visible, which is how the app switches tabs.</summary>
        private static void ShowOnly(Transform right)
        {
            foreach (Transform child in right)
                child.gameObject.SetActive(child.gameObject == _panel);

            Refresh();
        }

        // ---- panel --------------------------------------------------------

        private static GameObject? BuildPanel(Transform right)
        {
            var panel = CloneVanillaPanel(right);
            if (panel == null) return null;

            var pad = Mathf.RoundToInt(_baseFontSize * 0.6f);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = _baseFontSize * 0.35f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childControlWidth = true;

            Heading(panel.transform, "TRADING FLOOR");

            Stat(panel.transform, "capital", "Committed capital");
            Stat(panel.transform, "deployed", "At work");
            Stat(panel.transform, "risk", "Risk appetite");
            Stat(panel.transform, "compliance", "Compliance officer");
            Stat(panel.transform, "market", "Market");
            Stat(panel.transform, "desks", "Desks staffed");

            Divider(panel.transform);

            Stat(panel.transform, "lasthour", "Last hour");
            Stat(panel.transform, "today", "Today");

            Divider(panel.transform);
            Heading(panel.transform, "CAPITAL", 0.85f, Muted);

            // Every control acts on the brokerage currently selected in BizMan, so two
            // floors keep their own capital and risk.
            Controls(panel.transform,
                "Withdraw $1M", () => WithCurrent(a => FloorSave.Commit(a, -1_000_000L, _log)),
                "Commit $1M", () => WithCurrent(a => FloorSave.Commit(a, 1_000_000L, _log)));

            Heading(panel.transform, "RISK", 0.85f, Muted);

            Controls(panel.transform,
                "Less", () => WithCurrent(a =>
                {
                    var f = FloorSave.For(a);
                    f.risk = Mathf.Max(0.5f, f.risk - 0.1f);
                    FloorSave.SaveAll();
                }),
                "More", () => WithCurrent(a =>
                {
                    var f = FloorSave.For(a);
                    f.risk = Mathf.Min(2.0f, f.risk + 0.1f);
                    FloorSave.SaveAll();
                }));

            Heading(panel.transform, "COMPLIANCE OFFICER", 0.85f, Muted);

            Controls(panel.transform,
                "Dismiss", () => WithCurrent(a =>
                {
                    FloorSave.For(a).complianceRetained = false;
                    FloorSave.SaveAll();
                }),
                "Retain", () => WithCurrent(a =>
                {
                    FloorSave.For(a).complianceRetained = true;
                    FloorSave.SaveAll();
                }));

            panel.SetActive(false);
            return panel;
        }

        private static GameObject Stretch(GameObject go, Transform parent)
        {
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            return go;
        }

        private static void Heading(Transform parent, string text, float scale = 1.05f, Color? color = null)
        {
            var size = _baseFontSize * scale;
            var label = Text(parent, "Heading", text, size, color ?? Accent, FontStyles.Bold);
            Height(label.gameObject, size * 1.6f);
        }

        /// <summary>A label on the left and its value on the right - no space padding.</summary>
        private static void Stat(Transform parent, string key, string caption)
        {
            var row = Row(parent, _baseFontSize * 1.5f);

            var label = Text(row.transform, "Label", caption, _baseFontSize * 0.92f, Muted);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            Flexible(label.gameObject, 1f);

            var value = Text(row.transform, "Value", "-", _baseFontSize * 0.92f, Ink, FontStyles.Bold);
            value.alignment = TextAlignmentOptions.MidlineRight;
            Flexible(value.gameObject, 1f);

            Values[key] = value;
        }

        private static void Controls(Transform parent, string leftLabel, Action left, string rightLabel, Action right)
        {
            var row = Row(parent, _baseFontSize * 1.9f);
            Button(row.transform, leftLabel, left);
            Button(row.transform, rightLabel, right);
        }

        private static GameObject Row(Transform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _baseFontSize * 0.35f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            Height(row, height);
            return row;
        }

        private static void Divider(Transform parent)
        {
            var line = new GameObject("Divider", typeof(RectTransform));
            line.transform.SetParent(parent, false);

            var image = line.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);

            Height(line, 1f);
        }

        private static void Button(Transform parent, string caption, Action onClick)
        {
            var go = new GameObject("Btn " + caption, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = ButtonFill;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.highlightedColor = new Color(0.24f, 0.32f, 0.42f, 1f);
            colors.pressedColor = new Color(0.12f, 0.17f, 0.24f, 1f);
            button.colors = colors;

            button.onClick.AddListener(() =>
            {
                onClick();
                Refresh();
            });

            var label = Text(go.transform, "Text", caption, _baseFontSize * 0.88f, Ink, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.gameObject, go.transform);
        }

        private static TextMeshProUGUI Text(Transform parent, string name, string content,
            float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) text.font = _font;

            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;

            return text;
        }

        private static void Height(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void Flexible(GameObject go, float weight)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = weight;
        }

        // ---- content ------------------------------------------------------

        public static void Refresh()
        {
            var address = CurrentAddress();
            var isBrokerage = address != null && FloorRunner.IsBrokerage(address);

            // The tab belongs to brokerages only. BizMan hides tabs it does not know about,
            // so ours has to assert itself on a brokerage and stand down everywhere else.
            if (_tab != null && _tab.activeSelf != isBrokerage) _tab.SetActive(isBrokerage);
            if (_panel != null && _panel.activeSelf && !isBrokerage) _panel.SetActive(false);

            YieldToOtherTabs();

            if (Values.Count == 0 || !isBrokerage) return;

            var floor = FloorSave.For(address!);

            Set("capital", "$" + floor.committedCapital.ToString("N0"), Ink);
            var desks = FloorRunner.DesksAt(address!);
            var capacity = (double)desks * FloorTick.CapitalPerDesk;
            var deployed = Math.Min(floor.committedCapital, capacity);
            var idle = floor.committedCapital - deployed;

            Set("deployed", "$" + Math.Round(deployed).ToString("N0") +
                (idle > 1 ? "   ($" + Math.Round(idle).ToString("N0") + " idle)" : ""),
                idle > 1 ? Negative : Positive);

            Set("risk", (floor.risk * 100f).ToString("0") + "%", Ink);
            Set("compliance",
                floor.complianceRetained ? "Retained  2% of profit" : "None",
                floor.complianceRetained ? Ink : Negative);
            Set("market", FloorTick.RegimeLabel(), FloorTick.Regime < 0.85 ? Negative : Ink);
            Set("desks", desks.ToString(), desks > 0 ? Ink : Muted);
            Set("lasthour", Money(FloorRunner.LastPnlAt(address!)), Sign(FloorRunner.LastPnlAt(address!)));
            Set("today", Money(FloorRunner.TodayPnlAt(address!)), Sign(FloorRunner.TodayPnlAt(address!)));
        }

        /// <summary>
        /// Hides our panel as soon as another one is shown.
        /// <para>
        /// A vanilla tab button activates its own panel but only deactivates the ones it
        /// knows about, and ours is not among them - so once opened it stayed on top of
        /// every other tab and every other business. Two panels being visible at once is
        /// the signal that we are no longer the selected tab.
        /// </para>
        /// </summary>
        private static void YieldToOtherTabs()
        {
            if (_panel == null || !_panel.activeSelf) return;

            var siblings = _panel.transform.parent;
            if (siblings == null) return;

            foreach (Transform sibling in siblings)
            {
                if (sibling.gameObject == _panel || !sibling.gameObject.activeSelf) continue;

                _panel.SetActive(false);
                return;
            }
        }

        private static void Set(string key, string value, Color color)
        {
            if (!Values.TryGetValue(key, out var text) || text == null) return;
            text.text = value;
            text.color = color;
        }

        private static string Money(double amount) =>
            (amount >= 0 ? "+$" : "-$") + Math.Abs(Math.Round(amount)).ToString("N0");

        private static Color Sign(double amount) =>
            Math.Abs(amount) < 0.01 ? Muted : amount > 0 ? Positive : Negative;

        /// <summary>
        /// Localisation components overwrite text on enable - which is why Empire Casino's
        /// tab has none while every vanilla one does. Without this our labels get replaced
        /// by a missing-key string.
        /// </summary>
        private static void StripLocalization(GameObject go)
        {
            var type = GameBridge.FindType("TextLocalizationComponent");
            if (type == null) return;

            foreach (var component in go.GetComponentsInChildren(type, true))
                UnityEngine.Object.Destroy(component);
        }

        private static void SetText(GameObject go, string text)
        {
            var label = go.GetComponent<TextMeshProUGUI>() ?? go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = text;
        }
    }
}
