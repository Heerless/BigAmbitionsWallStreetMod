#nullable enable
using System;
using System.Collections.Generic;
using Entities;
using UI.Notification;
using UI.Smartphone.Apps.Contacts;

namespace WallStreet
{
    /// <summary>
    /// Tells the player what the floor is doing.
    /// <para>
    /// Regimes turning and desks blowing up were both silent - the mechanics ran and the
    /// only evidence was a number moving. Market shifts arrive as notifications; individual
    /// desk results come from a contact, because a message from your floor manager reads
    /// better than a toast and leaves a history you can scroll back through.
    /// </para>
    /// </summary>
    public static class FloorAlerts
    {
        public const string ContactName = "wallstreet-floormanager";

        private static Action<string> _log = _ => { };
        private static Contact? _contact;
        private static string _lastRegime = "";

        public static void Install(Action<string> log)
        {
            _log = log;

            try
            {
                _contact = Contact.GetContact(ContactName, ContactCategoryName.Finance, "wallstreet:contact_description");
                log("ALERTS: floor manager contact ready.");
            }
            catch (Exception e)
            {
                _contact = null;
                log("ALERTS: could not create the contact: " + e.Message);
            }
        }

        /// <summary>Announces a change of market regime, and only a change.</summary>
        public static void RegimeChanged(string regime)
        {
            if (regime == _lastRegime) return;

            var first = _lastRegime == "";
            _lastRegime = regime;
            if (first) return;

            var type = regime switch
            {
                "crisis" => NotificationType.Error,
                "choppy" => NotificationType.Warning,
                "bull" => NotificationType.Success,
                _ => NotificationType.Info,
            };

            Show(type, "wallstreet:alert_market_" + regime);
        }

        /// <summary>A desk blew up. Loud, because it costs real money.</summary>
        public static void DeskBlewUp(string businessName, double loss, int desks)
        {
            Show(NotificationType.Error, "wallstreet:alert_blowup");

            Message(desks + (desks == 1 ? " desk at " : " desks at ") + businessName +
                    " blew up, costing $" + Math.Round(Math.Abs(loss)).ToString("N0") +
                    ". We have no compliance officer to cap this.");
        }

        /// <summary>An unusually good or bad hour, worth a word from the floor.</summary>
        public static void NotableHour(string businessName, double pnl, bool excellent)
        {
            Message(excellent
                ? businessName + " had a strong hour: +$" + Math.Round(pnl).ToString("N0") + " across the floor."
                : businessName + " had a rough hour: -$" + Math.Round(Math.Abs(pnl)).ToString("N0") + " on the desks.");
        }

        private static void Show(NotificationType type, string headerKey)
        {
            try
            {
                Notifications.Show(type, headerKey, null, 5f, headerKey);
            }
            catch (Exception e)
            {
                _log("ALERTS: notification failed: " + e.Message);
            }
        }

        private static void Message(string text)
        {
            if (_contact == null) return;

            try
            {
                _contact.SendMessage(new TextMessage(text), sendNotificationInstantly: false);
            }
            catch (Exception e)
            {
                _log("ALERTS: message failed: " + e.Message);
            }
        }
    }
}
