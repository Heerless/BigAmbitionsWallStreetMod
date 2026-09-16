#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace WallStreet
{
    /// <summary>
    /// The trading floor's hourly profit-and-loss.
    /// <para>
    /// Customers are switched off for this business type, so no revenue arrives through the
    /// normal sales route. Instead each staffed desk puts a slice of committed capital to
    /// work once an hour and the result is posted to the books - the same approach Empire
    /// Casino takes for gaming revenue.
    /// </para>
    /// </summary>
    public static class FloorTick
    {
        // Constants, not player-facing. These are the tuning dials.
        // Tuned against play: 10M at the desk limit was returning ~1.1M a day when it should
        // return roughly half that, and 16M was returning 5M - linear scaling made big floors
        // absurd. The edge is roughly halved and large capital now tapers.
        private const double EdgePerHour = 0.00045;  // 0.045% of a desk's slice, before modifiers
        private const double VolPerHour = 0.0045;   // 0.45% standard deviation
        private const double BlowupChance = 0.002;    // per desk-hour with no compliance officer
        private const double BlowupMin = 0.15;
        private const double BlowupMax = 0.40;

        /// <summary>Tokyo through to the New York close; the London/NY overlap pays best.</summary>
        private static readonly double[] SessionWeight =
        {
            0.55, 0.50, 0.50, 0.55, 0.65, 0.75, 0.85, 0.95,
            1.05, 1.15, 1.25, 1.35, 1.40, 1.35, 1.25, 1.15,
            1.05, 0.95, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60
        };

        private static readonly Random Rng = new();

        /// <summary>
        /// Above this, returns taper rather than scale straight - a floor ten times the size
        /// should not earn ten times as much, or capital alone decides the game.
        /// </summary>
        private const double TaperFrom = 10_000_000.0;

        /// <summary>
        /// How sharply returns flatten past <see cref="TaperFrom"/>. At 0.7, five times the
        /// capital earns about three times as much rather than five.
        /// </summary>
        private const double TaperExponent = 0.7;

        /// <summary>
        /// How much capital one staffed desk can work. Committing beyond
        /// <c>desks x this</c> leaves the remainder idle, earning nothing - which is what
        /// makes a wider floor worth its wage bill.
        /// </summary>
        public const long CapitalPerDesk = 1_000_000L;

        /// <summary>Market mood, drifting over days. Bull above 1, crisis near zero.</summary>
        public static double Regime { get; private set; } = 1.0;

        /// <summary>Capital actually at work, and what is sitting idle for want of desks.</summary>
        public static double LastDeployed { get; private set; }
        public static double LastIdle { get; private set; }

        /// <summary>Desks that blew up in the hour just run, for the alert layer.</summary>
        public static int LastBlowups { get; private set; }

        private static bool _hasGaussianSpare;
        private static double _gaussianSpare;

        /// <summary>
        /// Runs one hour of trading. Returns the profit or loss, and describes what drove it.
        /// Kept free of game types so the maths can be reasoned about on its own.
        /// </summary>
        public static double RunHour(FloorState floor, int desksStaffed, double averageSkill, int hourOfDay, out string detail)
        {
            var capital = floor.committedCapital;

            if (desksStaffed <= 0 || capital <= 0)
            {
                detail = desksStaffed <= 0 ? "no desks staffed" : "no capital committed";
                return 0.0;
            }

            // A desk can only work so much money. Without this ceiling the maths collapses:
            // profit is linear in the slice, so desks x f(capital / desks) is constant and
            // one desk earns exactly what fifty do, minus forty-nine salaries. The cap is
            // what makes desks the thing that lets capital be deployed at all.
            var deployed = Math.Min(capital, desksStaffed * (double)CapitalPerDesk);
            LastDeployed = deployed;
            LastIdle = capital - deployed;

            // Returns taper past the first ten million, so a floor five times larger earns
            // about three times as much rather than five.
            var earning = deployed <= TaperFrom
                ? deployed
                : TaperFrom * Math.Pow(deployed / TaperFrom, TaperExponent);

            var slice = earning / desksStaffed;
            var risk = floor.risk;
            var session = SessionWeight[((hourOfDay % 24) + 24) % 24];

            var expected = slice * EdgePerHour * risk * Regime * session * (0.4 + 0.6 * averageSkill);
            var spread = slice * VolPerHour * risk * (1.3 - 0.5 * averageSkill);

            double total = 0.0;
            var blowups = 0;
            LastBlowups = 0;

            for (var d = 0; d < desksStaffed; d++)
            {
                var pnl = expected + spread * Gaussian();

                if (!floor.complianceRetained && Rng.NextDouble() < BlowupChance)
                {
                    pnl -= slice * (BlowupMin + Rng.NextDouble() * (BlowupMax - BlowupMin));
                    blowups++;
                }

                total += pnl;
            }

            // Charged only on a winning hour, so the fee can never deepen a loss.
            var complianceFee = 0.0;
            if (floor.complianceRetained && total > 0.0)
            {
                complianceFee = total * FloorState.ComplianceProfitShare;
                total -= complianceFee;
            }

            LastBlowups = blowups;

            detail = desksStaffed + " desks working $" + Math.Round(deployed).ToString("N0")
                     + (earning < deployed - 1 ? " (earning as $" + Math.Round(earning).ToString("N0") + ")" : "")
                     + (LastIdle > 1 ? " ($" + Math.Round(LastIdle).ToString("N0") + " idle - needs more desks)" : "")
                     + ", skill " + averageSkill.ToString("0.00")
                     + ", session " + session.ToString("0.00")
                     + ", regime " + Regime.ToString("0.00")
                     + ", risk " + risk.ToString("0.00")
                     + (complianceFee > 0.01 ? ", compliance -" + Math.Round(complianceFee).ToString("N0") : "")
                     + (blowups > 0 ? ", " + blowups + " BLOWUP" : "");

            return total;
        }

        /// <summary>
        /// Nudges the market regime. Slow mean-reverting drift, so runs of good and bad
        /// weeks both happen but neither lasts forever.
        /// </summary>
        public static void AdvanceRegime()
        {
            var drift = (1.0 - Regime) * 0.05;
            Regime = Math.Max(-0.4, Math.Min(1.6, Regime + drift + Gaussian() * 0.08));
        }

        public static string RegimeLabel()
        {
            if (Regime < 0.35) return "crisis";
            if (Regime < 0.85) return "choppy";
            return Regime > 1.25 ? "bull" : "normal";
        }

        /// <summary>Box-Muller, cached in pairs.</summary>
        private static double Gaussian()
        {
            if (_hasGaussianSpare)
            {
                _hasGaussianSpare = false;
                return _gaussianSpare;
            }

            double u, v, s;
            do
            {
                u = Rng.NextDouble() * 2.0 - 1.0;
                v = Rng.NextDouble() * 2.0 - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);

            var factor = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _gaussianSpare = v * factor;
            _hasGaussianSpare = true;
            return u * factor;
        }
    }
}
