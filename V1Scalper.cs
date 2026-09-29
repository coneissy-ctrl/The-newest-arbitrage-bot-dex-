using System;
using System.Linq;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class V1Scalper : Robot
    {
        private const string Label = "V1_US100_SCALPER";

        private Bars _executionBars;
        private Bars _trendBars;
        private ExponentialMovingAverage _emaFast, _emaSlow;
        private ExponentialMovingAverage _trendEmaFast, _trendEmaSlow;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private DateTime _sessionDate;
        private double _dayStartBalance;
        private double _dailyNetProfit;
        private int _consecutiveLosses;
        private int _lastProcessedBarIndex = -1;
        private DateTime _lastTradeTime = DateTime.MinValue;
        private bool _safetyLocked;

        // Preserve the original SL risk for R-multiple calculations.
        private readonly Dictionary<int, double> _initialRiskPips = new Dictionary<int, double>();

        [Parameter("Symbol", Group = "Market", DefaultValue = "US100")]
        public string TradeSymbol { get; set; }

        [Parameter("Execution TF", Group = "Market", DefaultValue = "Minute")]
        public TimeFrame ExecutionTimeFrame { get; set; }

        [Parameter("Trend TF", Group = "Market", DefaultValue = "Minute5")]
        public TimeFrame TrendTimeFrame { get; set; }

        [Parameter("Risk % / Trade", Group = "Risk", DefaultValue = 0.50, MinValue = 0.05, MaxValue = 2.00, Step = 0.05)]
        public double RiskPercent { get; set; }

        [Parameter("Max Daily Loss %", Group = "Risk", DefaultValue = 2.00, MinValue = 0.50, MaxValue = 5.00, Step = 0.25)]
        public double MaxDailyLossPercent { get; set; }

        [Parameter("Max Consecutive Losses", Group = "Risk", DefaultValue = 3, MinValue = 1, MaxValue = 10, Step = 1)]
        public int MaxConsecutiveLosses { get; set; }

        [Parameter("Max Trades / Day", Group = "Risk", DefaultValue = 20, MinValue = 1, MaxValue = 100, Step = 1)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("Fast EMA", Group = "Signal", DefaultValue = 9, MinValue = 3, MaxValue = 30, Step = 1)]
        public int FastEmaPeriod { get; set; }

        [Parameter("Slow EMA", Group = "Signal", DefaultValue = 21, MinValue = 8, MaxValue = 80, Step = 1)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("RSI Period", Group = "Signal", DefaultValue = 14, MinValue = 5, MaxValue = 30, Step = 1)]
        public int RsiPeriod { get; set; }

        [Parameter("RSI Buy Min", Group = "Signal", DefaultValue = 52, MinValue = 45, MaxValue = 65, Step = 1)]
        public double RsiBuyMin { get; set; }

        [Parameter("RSI Sell Max", Group = "Signal", DefaultValue = 48, MinValue = 35, MaxValue = 55, Step = 1)]
        public double RsiSellMax { get; set; }

        [Parameter("ATR Period", Group = "Volatility", DefaultValue = 14, MinValue = 5, MaxValue = 30, Step = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR SL Multiplier", Group = "Volatility", DefaultValue = 1.20, MinValue = 0.50, MaxValue = 3.00, Step = 0.10)]
        public double AtrSlMultiplier { get; set; }

        [Parameter("TP / SL Ratio", Group = "Volatility", DefaultValue = 1.20, MinValue = 0.60, MaxValue = 3.00, Step = 0.10)]
        public double TpSlRatio { get; set; }

        [Parameter("Min SL Pips", Group = "Volatility", DefaultValue = 8, MinValue = 2, MaxValue = 50, Step = 1)]
        public double MinStopLossPips { get; set; }

        [Parameter("Max SL Pips", Group = "Volatility", DefaultValue = 80, MinValue = 10, MaxValue = 200, Step = 5)]
        public double MaxStopLossPips { get; set; }

        [Parameter("Max Spread Pips", Group = "Execution", DefaultValue = 20, MinValue = 1, MaxValue = 100, Step = 1)]
        public double MaxSpreadPips { get; set; }

        [Parameter("Cooldown Seconds", Group = "Execution", DefaultValue = 30, MinValue = 0, MaxValue = 300, Step = 5)]
        public int CooldownSeconds { get; set; }

        [Parameter("One Position Only", Group = "Execution", DefaultValue = true)]
        public bool OnePositionOnly { get; set; }

        [Parameter("Start Hour UTC", Group = "Session", DefaultValue = 13, MinValue = 0, MaxValue = 23, Step = 1)]
        public int StartHourUtc { get; set; }

        [Parameter("End Hour UTC", Group = "Session", DefaultValue = 21, MinValue = 0, MaxValue = 23, Step = 1)]
        public int EndHourUtc { get; set; }

        [Parameter("Trade Monday", Group = "Session", DefaultValue = true)]
        public bool TradeMonday { get; set; }

        [Parameter("Trade Friday", Group = "Session", DefaultValue = true)]
        public bool TradeFriday { get; set; }

        [Parameter("Break Even Trigger R", Group = "Management", DefaultValue = 0.70, MinValue = 0.30, MaxValue = 2.00, Step = 0.10)]
        public double BreakEvenTriggerR { get; set; }

        [Parameter("Break Even Offset Pips", Group = "Management", DefaultValue = 1.0, MinValue = 0, MaxValue = 10, Step = 0.5)]
        public double BreakEvenOffsetPips { get; set; }

        [Parameter("Trailing Trigger R", Group = "Management", DefaultValue = 1.00, MinValue = 0.50, MaxValue = 3.00, Step = 0.10)]
        public double TrailingTriggerR { get; set; }

        [Parameter("Trailing ATR Multiplier", Group = "Management", DefaultValue = 0.80, MinValue = 0.20, MaxValue = 2.00, Step = 0.10)]
        public double TrailingAtrMultiplier { get; set; }

        [Parameter("Allow Buy", Group = "Direction", DefaultValue = true)]
        public bool AllowBuy { get; set; }

        [Parameter("Allow Sell", Group = "Direction", DefaultValue = true)]
        public bool AllowSell { get; set; }

        protected override void OnStart()
        {
            if (!string.Equals(Symbol.Name, TradeSymbol, StringComparison.OrdinalIgnoreCase))
                Print("WARNING: attached symbol is {0}; configured symbol is {1}. Use the broker's exact US100 symbol.", Symbol.Name, TradeSymbol);

            _executionBars = MarketData.GetBars(ExecutionTimeFrame, Symbol.Name);
            _trendBars = MarketData.GetBars(TrendTimeFrame, Symbol.Name);

            _emaFast = Indicators.ExponentialMovingAverage(_executionBars.ClosePrices, FastEmaPeriod);
            _emaSlow = Indicators.ExponentialMovingAverage(_executionBars.ClosePrices, SlowEmaPeriod);
            _rsi = Indicators.RelativeStrengthIndex(_executionBars.ClosePrices, RsiPeriod);
            _atr = Indicators.AverageTrueRange(_executionBars, AtrPeriod, MovingAverageType.Exponential);

            _trendEmaFast = Indicators.ExponentialMovingAverage(_trendBars.ClosePrices, FastEmaPeriod);
            _trendEmaSlow = Indicators.ExponentialMovingAverage(_trendBars.ClosePrices, SlowEmaPeriod);

            _sessionDate = Server.Time.Date;
            RestoreDailyState();
            _safetyLocked = false;

            _executionBars.BarClosed += OnExecutionBarClosed;
            _lastProcessedBarIndex = -1;
            Positions.Closed += OnPositionClosed;

            foreach (var position in Positions.FindAll(Label, Symbol.Name))
                RegisterInitialRisk(position);

            Print("V1 Scalper started | Symbol={0} | Execution={1} | Trend={2}",
                Symbol.Name, ExecutionTimeFrame, TrendTimeFrame);
        }

        protected override void OnStop()
        {
            _executionBars.BarClosed -= OnExecutionBarClosed;
            Positions.Closed -= OnPositionClosed;
        }

        protected override void OnTick()
        {
            ResetDailyStateIfNeeded();
            ManageSafety();
            ManagePositions();
        }

        private void OnExecutionBarClosed(BarClosedEventArgs args)
        {
            ResetDailyStateIfNeeded();
            ManageSafety();

            if (!CanTrade() || (OnePositionOnly && HasOpenPosition()))
                return;

            if (_executionBars.Count < Math.Max(SlowEmaPeriod, AtrPeriod) + 5 ||
                _trendBars.Count < SlowEmaPeriod + 5)
                return;

            double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;

            if (spreadPips > MaxSpreadPips ||
                (Server.Time - _lastTradeTime).TotalSeconds < CooldownSeconds)
                return;

            int closedIndex = _executionBars.Count - 1;
            if (closedIndex <= _lastProcessedBarIndex)
                return;
            _lastProcessedBarIndex = closedIndex;

            // Use the closed execution bar explicitly.
            double fast = _emaFast.Result[closedIndex];
            double slow = _emaSlow.Result[closedIndex];
            double prevFast = _emaFast.Result[closedIndex - 1];
            double prevSlow = _emaSlow.Result[closedIndex - 1];
            double rsi = _rsi.Result[closedIndex];

            // Map the execution bar to its containing M5 bar, then step back
            // one bar because that M5 bar is still forming at the M1 close.
            int trendIndex = _trendBars.OpenTimes.GetIndexByTime(_executionBars.OpenTimes[closedIndex]);
            if (trendIndex < 1)
                return;

            if (_trendBars.OpenTimes[trendIndex] == _executionBars.OpenTimes[closedIndex])
            {
                trendIndex--;
            }

            if (trendIndex >= _trendBars.Count)
                return;

            bool trendBull = _trendEmaFast.Result[trendIndex] > _trendEmaSlow.Result[trendIndex];
            bool trendBear = _trendEmaFast.Result[trendIndex] < _trendEmaSlow.Result[trendIndex];

            bool bullishCross = prevFast <= prevSlow && fast > slow;
            bool bearishCross = prevFast >= prevSlow && fast < slow;

            bool buyMomentum = rsi >= RsiBuyMin && Symbol.Bid > fast;
            bool sellMomentum = rsi <= RsiSellMax && Symbol.Ask < fast;

            if (AllowBuy && trendBull && (bullishCross || buyMomentum))
                OpenPosition(TradeType.Buy);
            else if (AllowSell && trendBear && (bearishCross || sellMomentum))
                OpenPosition(TradeType.Sell);
        }

        private void OpenPosition(TradeType tradeType)
        {
            double atrPips = _atr.Result[_executionBars.Count - 1] / Symbol.PipSize;
            double stopLossPips = Clamp(
                atrPips * AtrSlMultiplier,
                MinStopLossPips,
                MaxStopLossPips);

            double takeProfitPips = stopLossPips * TpSlRatio;
            double riskMoney = Account.Balance * RiskPercent / 100.0;

            double volume = Symbol.VolumeForFixedRisk(
                riskMoney,
                stopLossPips,
                RoundingMode.Down);

            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("ENTRY SKIPPED: calculated volume {0} is below minimum {1}.",
                    volume, Symbol.VolumeInUnitsMin);
                return;
            }

            if (volume > Symbol.VolumeInUnitsMax)
                volume = Symbol.VolumeInUnitsMax;

            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
            if (volume < Symbol.VolumeInUnitsMin)
                return;

            var result = ExecuteMarketOrder(
                tradeType,
                Symbol.Name,
                volume,
                Label,
                stopLossPips,
                takeProfitPips);

            if (!result.IsSuccessful)
            {
                Print("ENTRY FAILED {0}: {1}", tradeType, result.Error);
                return;
            }

            _lastTradeTime = Server.Time;

            if (result.Position != null)
                _initialRiskPips[result.Position.Id] = stopLossPips;

            Print("ENTRY {0} | Vol={1} | SL={2:F1} | TP={3:F1} | Spread={4:F1}",
                tradeType,
                volume,
                stopLossPips,
                takeProfitPips,
                (Symbol.Ask - Symbol.Bid) / Symbol.PipSize);
        }

        private void ManagePositions()
        {
            foreach (var position in Positions.FindAll(Label, Symbol.Name))
            {
                RegisterInitialRisk(position);

                double initialRiskPips = _initialRiskPips[position.Id];

                if (initialRiskPips <= 0)
                    continue;

                double currentR = position.Pips / initialRiskPips;

                if (currentR >= BreakEvenTriggerR)
                {
                    double bePrice = position.TradeType == TradeType.Buy
                        ? position.EntryPrice + BreakEvenOffsetPips * Symbol.PipSize
                        : position.EntryPrice - BreakEvenOffsetPips * Symbol.PipSize;

                    if (ShouldImproveStop(position, bePrice))
                        ModifyPosition(position, bePrice, position.TakeProfit);
                }

                if (currentR >= TrailingTriggerR)
                {
                    double atrPips = _atr.Result.LastValue / Symbol.PipSize;
                    double trailPips = Math.Max(1.0, atrPips * TrailingAtrMultiplier);

                    double trailPrice = position.TradeType == TradeType.Buy
                        ? Symbol.Bid - trailPips * Symbol.PipSize
                        : Symbol.Ask + trailPips * Symbol.PipSize;

                    if (ShouldImproveStop(position, trailPrice))
                        ModifyPosition(position, trailPrice, position.TakeProfit);
                }
            }
        }

        private void ManageSafety()
        {
            double maxDailyLossMoney = _dayStartBalance * MaxDailyLossPercent / 100.0;

            if (_safetyLocked)
                return;

            if (_dailyNetProfit <= -maxDailyLossMoney)
            {
                _safetyLocked = true;
                CloseBotPositions("Daily loss limit reached");
                return;
            }

            if (_consecutiveLosses >= MaxConsecutiveLosses)
            {
                _safetyLocked = true;
                CloseBotPositions("Consecutive loss limit reached");
            }
        }

        private bool CanTrade()
        {
            if (_safetyLocked || !Symbol.IsTradingEnabled)
                return false;

            if (Server.Time.Hour < StartHourUtc || Server.Time.Hour > EndHourUtc)
                return false;

            if (Server.Time.DayOfWeek == DayOfWeek.Saturday ||
                Server.Time.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (!TradeMonday && Server.Time.DayOfWeek == DayOfWeek.Monday)
                return false;

            if (!TradeFriday && Server.Time.DayOfWeek == DayOfWeek.Friday)
                return false;

            double maxDailyLossMoney = _dayStartBalance * MaxDailyLossPercent / 100.0;

            if (_safetyLocked || _dailyNetProfit <= -maxDailyLossMoney ||
                _consecutiveLosses >= MaxConsecutiveLosses)
                return false;

            return CountTradesToday() < MaxTradesPerDay;
        }

        private int CountTradesToday()
        {
            int count = 0;

            foreach (var trade in History.FindAll(Label, Symbol.Name))
            {
                if (trade.EntryTime.Date == Server.Time.Date)
                    count++;
            }

            foreach (var position in Positions.FindAll(Label, Symbol.Name))
            {
                if (position.EntryTime.Date == Server.Time.Date)
                    count++;
            }

            return count;
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var position = args.Position;

            if (position.Label != Label || position.SymbolName != Symbol.Name)
                return;

            _initialRiskPips.Remove(position.Id);

            _dailyNetProfit += position.NetProfit;

            if (position.NetProfit < 0)
                _consecutiveLosses++;
            else if (position.NetProfit > 0)
                _consecutiveLosses = 0;

            Print("CLOSED | Net={0:F2} | Daily={1:F2} | ConsecutiveLosses={2}",
                position.NetProfit, _dailyNetProfit, _consecutiveLosses);
        }

        private void RestoreDailyState()
        {
            _dailyNetProfit = 0;
            _consecutiveLosses = 0;

            var trades = History.FindAll(Label, Symbol.Name);

            foreach (var trade in trades)
            {
                if (trade.ClosingTime.Date == Server.Time.Date)
                    _dailyNetProfit += trade.NetProfit;
            }

            var ordered = trades
                .Where(t => t.ClosingTime.Date == Server.Time.Date)
                .OrderByDescending(t => t.ClosingTime)
                .ToArray();

            foreach (var trade in ordered)
            {
                if (trade.NetProfit < 0)
                    _consecutiveLosses++;
                else if (trade.NetProfit > 0)
                    break;
            }

            _dayStartBalance = Account.Balance - _dailyNetProfit;

            if (_dayStartBalance <= 0)
                _dayStartBalance = Account.Balance;
        }

        private void ResetDailyStateIfNeeded()
        {
            if (Server.Time.Date == _sessionDate)
                return;

            _sessionDate = Server.Time.Date;
            RestoreDailyState();
            _safetyLocked = false;

            Print("NEW TRADING DAY | StartBalance={0:F2}", _dayStartBalance);
        }

        private void RegisterInitialRisk(Position position)
        {
            if (_initialRiskPips.ContainsKey(position.Id))
                return;

            if (position.StopLoss.HasValue)
            {
                double riskPips = Math.Abs(position.EntryPrice - position.StopLoss.Value) / Symbol.PipSize;

                if (riskPips > 0)
                    _initialRiskPips[position.Id] = riskPips;
            }
        }

        private bool HasOpenPosition()
        {
            return Positions.FindAll(Label, Symbol.Name).Length > 0;
        }

        private void CloseBotPositions(string reason)
        {
            var positions = Positions.FindAll(Label, Symbol.Name);

            foreach (var position in positions)
                ClosePosition(position);

            Print("SAFETY STOP: {0}", reason);
        }

        private bool ShouldImproveStop(Position position, double candidate)
        {
            if (position.TradeType == TradeType.Buy)
                return !position.StopLoss.HasValue ||
                       candidate > position.StopLoss.Value + Symbol.PipSize;

            return !position.StopLoss.HasValue ||
                   candidate < position.StopLoss.Value - Symbol.PipSize;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        protected override double GetFitness(GetFitnessArgs args)
        {
            if (args.TotalTrades < 50 ||
                args.MaxEquityDrawdownPercentages >= 20 ||
                args.NetProfit <= 0 ||
                args.ProfitFactor <= 0)
                return double.MinValue;

            double drawdownPenalty = 1.0 + args.MaxEquityDrawdownPercentages / 10.0;
            double fitness = args.NetProfit * args.ProfitFactor / drawdownPenalty;

            if (double.IsNaN(fitness) || double.IsInfinity(fitness))
                return double.MinValue;

            return fitness;
        }
    }
}
