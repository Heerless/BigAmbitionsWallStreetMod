#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using BigAmbitions.Items;
using Helpers;

namespace WallStreet
{
    /// <summary>
    /// Runs the trading floor against the live game: finds the player's brokerages, counts
    /// the brokers actually at a desk, and books the hourly result to that business.
    /// <para>
    /// Written against the real game types rather than reflection. The game assemblies are
    /// referenced by this mod, so a wrong member name is a build error rather than a silent
    /// zero discovered an hour into a play session.
    /// </para>
    /// </summary>
    public static class FloorRunner
    {
        public const string BrokerageType = "wallstreet:businesstype_brokerage";

        private static Action<string> _log = _ => { };
        private static Action? _unsubscribe;
        private static int _hoursRun;
        private static int _lastDay = -1;
        private static bool _panelInstalled;

        /// <summary>
        /// Live figures per brokerage. These were single statics, so with two floors the
        /// panel showed whichever happened to tick last no matter which one you had open.
        /// </summary>
        private sealed class Live
        {
            public int Desks;
            public double LastPnl;
            public double TodayPnl;
        }

        private static readonly Dictionary<string, Live> Figures = new();

        private static Live FiguresFor(Address address)
        {
            var key = address.streetName + "#" + address.streetNumber;
            if (!Figures.TryGetValue(key, out var live)) Figures[key] = live = new Live();
            return live;
        }

        public static int DesksAt(Address a) => FiguresFor(a).Desks;
        public static double LastPnlAt(Address a) => FiguresFor(a).LastPnl;
        public static double TodayPnlAt(Address a) => FiguresFor(a).TodayPnl;

        public static void Start(Action<string> log)
        {
            _log = log;
            Stop();

            if (HourPump.Install(log))
            {
                HourPump.HourElapsed += OnHour;
                HourPump.Ticked += OnFrameTick;
                HourPump.OnQuitting = FloorSave.SaveAll;

                _unsubscribe = () =>
                {
                    HourPump.HourElapsed -= OnHour;
                    HourPump.Ticked -= OnFrameTick;
                };

                log("TICK: hourly trading armed.");
            }
            else
            {
                log("TICK: the floor will not trade. See CLOCK lines above.");
            }
        }

        public static void Stop()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
            HourPump.Uninstall();
        }

        private static void OnFrameTick()
        {
            if (!_panelInstalled)
                _panelInstalled = BizManPanel.Install(_log);
            else
                BizManPanel.Refresh();
        }

        private static void OnHour()
        {
            try
            {
                var hour = HourPump.HourOfDay();
                var day = HourPump.CurrentDay();

                if (day != _lastDay)
                {
                    _lastDay = day;
                    foreach (var live in Figures.Values) live.TodayPnl = 0.0;
                    FloorTick.AdvanceRegime();
                    FloorAlerts.RegimeChanged(FloorTick.RegimeLabel());
                    _log("TICK: market is " + FloorTick.RegimeLabel() + " (" + FloorTick.Regime.ToString("0.00") + ")");
                }

                foreach (var brokerage in Brokerages())
                {
                    var settings = FloorSave.For(brokerage.Address);

                    // A closed business trades nothing, but still owes its compliance retainer.
                    var figures = FiguresFor(brokerage.Address);

                    if (brokerage.temporarilyClosed)
                    {
                        figures.Desks = 0;
                        figures.LastPnl = 0.0;
                        continue;
                    }

                    var (desks, skill) = StaffedDesks(brokerage);
                    var pnl = FloorTick.RunHour(settings, desks, skill, hour, out var detail);

                    figures.Desks = desks;
                    figures.LastPnl = pnl;
                    figures.TodayPnl += pnl;

                    if (Math.Abs(pnl) < 0.01)
                    {
                        if (_hoursRun % 12 == 0) _log("TICK: idle - " + detail);
                        continue;
                    }

                    var booked = Book(brokerage.Address, pnl, pnl >= 0);
                    if (booked) RecordTrade(brokerage, pnl, desks);

                    Report(brokerage, pnl, desks);

                    _log("TICK h" + hour.ToString("00") + " " + (pnl >= 0 ? "+" : "") +
                         Math.Round(pnl).ToString("N0") + " | " + detail + (booked ? "" : " | NOT BOOKED"));
                }

                _hoursRun++;
            }
            catch (Exception e)
            {
                _log("TICK: hour failed: " + e);
            }
        }

        /// <summary>
        /// Removes Sales rows naming something that is not a registered item.
        /// <para>
        /// EconoViewBusinessDetails.LoadSales calls
        /// <c>ItemsGetter.GetByName(name).HasTag(...)</c> with no null check, so one
        /// unresolvable row throws and collapses the whole statement into a single
        /// "Undefined" line. Financial summaries persist in the save, so a bad row written
        /// once keeps breaking the screen on every later load - including rows this mod
        /// wrote during development.
        /// </para>
        /// </summary>
        public static void RepairStatements(Action<string> log)
        {
            try
            {
                // 60 is the cap - the game throws if asked for more.
                var summaries = FinancialSummaryHelper.GetLastFinancialSummaries(60);
                if (summaries == null) return;

                var removed = 0;

                foreach (var summary in summaries)
                {
                    if (summary?.businessIncomeStatements == null) continue;

                    foreach (var statement in summary.businessIncomeStatements)
                    {
                        if (statement?.Sales == null) continue;

                        for (var i = statement.Sales.Count - 1; i >= 0; i--)
                        {
                            var name = statement.Sales[i].ItemName;
                            if (!string.IsNullOrEmpty(name) && ItemsGetter.GetByName(name, true) != null) continue;

                            statement.TotalSales -= statement.Sales[i].Amount;
                            statement.Sales.RemoveAt(i);
                            removed++;
                        }
                    }

                    if (removed > 0) FinancialSummaryHelper.UpdateIncomeStatementTotalProfit(summary);
                }

                log(removed == 0
                    ? "REPAIR: no broken Sales rows found."
                    : "REPAIR: removed " + removed + " unresolvable Sales row(s) that were breaking the statement.");
            }
            catch (Exception e)
            {
                log("REPAIR: failed: " + e.Message);
            }
        }

        /// <summary>
        /// Raises alerts for things worth knowing: a blowup, or an hour far outside the
        /// ordinary. Quiet hours stay quiet, or the phone becomes noise and gets ignored.
        /// </summary>
        private static void Report(BuildingRegistration brokerage, double pnl, int desks)
        {
            var name = brokerage.GetDisplayName();

            if (FloorTick.LastBlowups > 0)
            {
                FloorAlerts.DeskBlewUp(name, pnl, FloorTick.LastBlowups);
                return;
            }

            // Only flag genuine outliers, judged against what a desk can hold.
            var notable = desks * FloorTick.CapitalPerDesk * 0.02;
            if (Math.Abs(pnl) >= notable)
                FloorAlerts.NotableHour(name, pnl, pnl > 0);
        }

        /// <summary>Every brokerage the player runs.</summary>
        public static IEnumerable<BuildingRegistration> Brokerages()
        {
            var registrations = BuildingHelper.GetPlayerBuildingRegistrations(null, null);
            if (registrations == null) yield break;

            foreach (var registration in registrations)
                if (registration != null && registration.businessTypeName == BrokerageType)
                    yield return registration;
        }

        public static bool IsBrokerage(Address address)
        {
            var registration = BuildingHelper.GetBuildingRegistration(address);
            return registration != null && registration.businessTypeName == BrokerageType;
        }

        /// <summary>
        /// A desk only earns while a broker is actually at it, so this counts brokers on
        /// shift at this address and averages their skill.
        /// </summary>
        private static (int desks, double skill) StaffedDesks(BuildingRegistration brokerage)
        {
            var employees = EmployeeHelper.GetEmployeeInstances();
            if (employees == null) return (0, 0.0);

            var desks = 0;
            var total = 0.0;

            foreach (var employee in employees)
            {
                if (employee == null) continue;
                if (!employee.assignedAddress.Equals(brokerage.Address)) continue;
                if (employee.GetPrimarySkill() != BrokerSkill.SkillName) continue;
                if (!employee.IsWorking(false, WorkShiftType.Default)) continue;

                desks++;

                // Skill reads 0-100; the model wants 0-1.
                total += Math.Max(0.0, Math.Min(1.0, employee.GetSkillValue(BrokerSkill.SkillName) / 100.0));
            }

            return desks == 0 ? (0, 0.0) : (desks, total / desks);
        }

        // ---- money --------------------------------------------------------

        /// <summary>
        /// Books an amount against a business so it lands on that business's income
        /// statement rather than only moving the player's balance.
        /// </summary>
        public static bool Book(Address address, double amount, bool asRevenue)
        {
            try
            {
                var registration = BuildingHelper.GetBuildingRegistration(address);
                var name = registration != null ? registration.GetDisplayName() : address.ToString();

                // Booked as ba:transaction_revenue so it lands in the Sales section of the
                // income statement. Our own transaction type kept the nicer label but left
                // Sales at zero, which made the brokerage look like pure cost.
                //
                // A losing hour is negative revenue rather than an expense - the floor's
                // result is a net figure, the way a casino reports gross gaming revenue.
                var info = new TransactionInfo(
                    "ba:transaction_revenue",
                    "wallstreet:category_trading",
                    new Dictionary<string, string> { { "businessName", name } },
                    false);

                return GameManager.ChangeMoneySafe((float)amount, info, null, address, false, false);
            }
            catch (Exception e)
            {
                _log("MONEY: booking $" + Math.Round(amount).ToString("N0") + " failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Records the hour as a sale in the business's order history.
        /// <para>
        /// Writing into the income statement directly was always going to lose: the game
        /// rebuilds those Sales rows from order history, so our row survived only until the
        /// next rebuild - which is exactly the intermittent "Undefined" that came back after
        /// a while. Order history is the source, so writing there lets the statement, the
        /// inventory screen and the sales figures all derive themselves.
        /// </para>
        /// </summary>
        private static void RecordTrade(BuildingRegistration registration, double amount, int desks)
        {
            try
            {
                if (ItemsGetter.GetByName(TradingLine, true) == null)
                {
                    if (!_warnedNoCommission)
                    {
                        _warnedNoCommission = true;
                        _log("SALES: " + TradingLine + " is not registered; not recording trades.");
                    }
                    return;
                }

                registration.orderHistory ??= new List<OrderHistoryEntry>();

                var day = SaveGameManager.Current.Day;
                OrderHistoryEntry? today = null;

                foreach (var entry in registration.orderHistory)
                    if (entry != null && entry.dayNumber == day) { today = entry; break; }

                if (today == null)
                {
                    today = new OrderHistoryEntry
                    {
                        dayNumber = day,
                        itemSales = new List<OrderHistoryEntry.ItemReport>(),
                        hourReports = new List<OrderHistoryEntry.HourReport>(),
                    };
                    registration.orderHistory.Add(today);
                }

                today.itemSales ??= new List<OrderHistoryEntry.ItemReport>();

                OrderHistoryEntry.ItemReport? report = null;
                foreach (var sale in today.itemSales)
                    if (sale != null && sale.itemName == TradingLine) { report = sale; break; }

                if (report == null)
                {
                    report = new OrderHistoryEntry.ItemReport(TradingLine, 0, 0f, 0f,
                        Array.Empty<ItemSoldPerPriceEntry>());
                    today.itemSales.Add(report);
                }

                // One trade per staffed desk per hour gives the volume figure some meaning.
                report.amountSold += Math.Max(1, desks);
                report.totalPrice += (float)amount;
                today.totalRevenue += (float)amount;
            }
            catch (Exception e)
            {
                _log("SALES: could not record the trade: " + e.Message);
            }
        }

        private const string TradingLine = "wallstreet:itemname_commission";
        private static bool _warnedNoCommission;

        private static System.Reflection.ConstructorInfo? _transactionCtor;
        private static bool _ctorSearched;

        /// <summary>
        /// Builds a TransactionInfo. The type is immutable with no parameterless
        /// constructor, and its constructor signature could not be read while the project
        /// was failing to compile - so it is resolved once here and logged, ready to be
        /// written as a plain call.
        /// </summary>
        private static TransactionInfo? MakeTransactionInfo(string type)
        {
            if (!_ctorSearched)
            {
                _ctorSearched = true;

                var ctors = typeof(TransactionInfo).GetConstructors();
                foreach (var c in ctors)
                    _log("MONEY: TransactionInfo(" + string.Join(", ",
                        c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");

                // Prefer the simplest constructor that starts with the transaction type.
                _transactionCtor = ctors
                    .Where(c => c.GetParameters().Length > 0 &&
                                c.GetParameters()[0].ParameterType == typeof(string))
                    .OrderBy(c => c.GetParameters().Length)
                    .FirstOrDefault() ?? ctors.OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            }

            if (_transactionCtor == null) return null;

            try
            {
                var parameters = _transactionCtor.GetParameters();
                var args = new object?[parameters.Length];

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (i == 0 && parameters[i].ParameterType == typeof(string))
                        args[i] = type;
                    else if (parameters[i].HasDefaultValue)
                        args[i] = parameters[i].DefaultValue;
                    else if (parameters[i].ParameterType == typeof(string))
                        args[i] = type;
                    else
                        args[i] = parameters[i].ParameterType.IsValueType
                            ? Activator.CreateInstance(parameters[i].ParameterType)
                            : null;
                }

                return _transactionCtor.Invoke(args) as TransactionInfo;
            }
            catch (Exception e)
            {
                _log("MONEY: could not build TransactionInfo: " + (e.InnerException?.Message ?? e.Message));
                return null;
            }
        }

        /// <summary>Capital transfers are booked to the brokerage too, so they show up in its ledger.</summary>
        /// <summary>
        /// Moves capital between the player and a floor.
        /// <para>
        /// Deliberately booked with <em>no address</em>. Committing capital is moving your
        /// own money, not a business expense - charging it to the brokerage made its income
        /// statement permanently negative and, worse, had the IRS treat it as a deduction.
        /// </para>
        /// </summary>
        public static bool ChangeMoney(Address address, double amount)
        {
            try
            {
                var registration = BuildingHelper.GetBuildingRegistration(address);
                var name = registration != null ? registration.GetDisplayName() : address.ToString();

                var info = new TransactionInfo(
                    "wallstreet:transaction_capital_commit",
                    new Dictionary<string, string> { { "businessName", name } },
                    false);

                return GameManager.ChangeMoneySafe((float)amount, info, null, null, false, false);
            }
            catch (Exception e)
            {
                _log("MONEY: capital transfer failed: " + e.Message);
                return false;
            }
        }
    }
}
