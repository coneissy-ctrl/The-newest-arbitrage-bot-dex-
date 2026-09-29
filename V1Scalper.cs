using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class V1Scalper : Robot
    {
        private const string Label = "V1_US100_SCALPER";
        private Bars _trendBars;
        private ExponentialMovingAverage _emaFast, _emaSlow, _trendEmaFast, _trendEmaSlow;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;
        private DateTime _sessionDate;
        private double _dailyNetProfit;
        private int _consecutiveLosses;
        private DateTime _lastTradeTime = DateTime.MinValue;

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

            _trendBars = MarketData.GetBars(TrendTimeFrame, Symbol.Name);
            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, FastEmaPeriod);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, SlowEmaPeriod);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _atr = Indicators.AverageTrueRange(Bars, AtrPeriod, MovingAverageType.Exponential);
            _trendEmaFast = Indicators.ExponentialMovingAverage(_trendBars.ClosePrices, FastEmaPeriod);
            _trendEmaSlow = Indicators.ExponentialMovingAverage(_trendBars.ClosePrices, SlowEmaPeriod);

            _sessionDate = Server.Time.Date;
            Positions.Closed += OnPositionClosed;
            Print("V1 Scalper started | Symbol={0} | Execution={1} | Trend={2}", Symbol.Name, ExecutionTimeFrame, TrendTimeFrame);
        }

        protected override void OnBar()
        {
            ResetDailyStateIfNeeded();
            ManageSafety();

            if (!CanTrade() || (OnePositionOnly && HasOpenPosition()))
                return;

            if (Bars.Count < Math.Max(SlowEmaPeriod, AtrPeriod) + 10 || _trendBars.Count < SlowEmaPeriod + 10)
                return;

            double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
            if (spreadPips > MaxSpreadPips || (Server.Time - _lastTradeTime).TotalSeconds < CooldownSeconds)
                return;

            bool trendBull = _trendEmaFast.Result.LastValue > _trendEmaSlow.Result.LastValue;
            bool trendBear = _trendEmaFast.Result.LastValue < _trendEmaSlow.Result.LastValue;
            double fast = _emaFast.Result.LastValue;
            double slow = _emaSlow.Result.LastValue;
            double rsi = _rsi.Result.LastValue;
            double prevFast = _emaFast.Result.Last(1);
            double prevSlow = _emaSlow.Result.Last(1);

            bool bullishCross = prevFast <= prevSlow && fast > slow;
            bool bearishCross = prevFast >= prevSlow && fast < slow;
            bool buyMomentum = rsi >= RsiBuyMin && Symbol.Bid > fast;
            bool sellMomentum = rsi <= RsiSellMax && Symbol.Ask < fast;

            if (AllowBuy && trendBull && (bullishCross || buyMomentum))
                OpenPosition(TradeType.Buy);
            else if (AllowSell && trendBear && (bearishCross || sellMomentum))
                OpenPosition(TradeType.Sell);
        }

        protected override void OnTick()
        {
            ResetDailyStateIfNeeded();
            ManagePositions();
        }

        private void OpenPosition(TradeType tradeType)
        {
            double atrPips = _atr.Result.LastValue / Symbol.PipSize;
            double stopLossPips = Clamp(atrPips * AtrSlMultiplier, MinStopLossPips, MaxStopLossPips);
            double takeProfitPips = stopLossPips * TpSlRatio;
            double riskMoney = Account.Balance * RiskPercent / 100.0;
            double volume = Symbol.VolumeForFixedRisk(riskMoney, stopLossPips, RoundingMode.Down);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                return;

            var result = ExecuteMarketOrder(tradeType, Symbol.Name, volume, Label, stopLossPips, takeProfitPips);

            if (result.IsSuccessful)
            {
                _lastTradeTime = Server.Time;
                Print("ENTRY {0} | Vol={1} | SL={2:F1} | TP={3:F1} | Spread={4:F1}",
                    tradeType, volume, stopLossPips, takeProfitPips,
                    (Symbol.Ask - Symbol.Bid) / Symbol.PipSize);
            }
            else
                Print("ENTRY FAILED {0}: {1}", tradeType, result.Error);
        }

        private void ManagePositions()
        {
            foreach (var position in Positions.FindAll(Label, Symbol.Name))
            {
                double initialRiskPips = GetInitialRiskPips(position);
                if (initialRiskPips <= 0)
                    continue;

                double currentR = position.Pips / initialRiskPips;

                if (currentR >= BreakEvenTriggerR)
                {
                    double bePrice = position.TradeType == TradeType.Buy
                        ? position.EntryPrice + BreakEvenOffsetPips * Symbol.PipSize
                        : position.EntryPrice - BreakEvenOffsetPips * Symbol.PipSize;

                    if (ShouldImproveStop(position, bePrice))
                        ModifyPosition(position, bePrice, position.TakeProfit, ProtectionType.Absolute);
                }

                if (currentR >= TrailingTriggerR)
                {
                    double atrPips = _atr.Result.LastValue / Symbol.PipSize;
                    double trailPips = Math.Max(1.0, atrPips * TrailingAtrMultiplier);
                    double trailPrice = position.TradeType == TradeType.Buy
                        ? Symbol.Bid - trailPips * Symbol.PipSize
                        : Symbol.Ask + trailPips * Symbol.PipSize;

                    if (ShouldImproveStop(position, trailPrice))
                        ModifyPosition(position, trailPrice, position.TakeProfit, ProtectionType.Absolute);
                }
            }
        }

        private void ManageSafety()
        {
            if (_dailyNetProfit <= -(Account.Balance * MaxDailyLossPercent / 100.0))
                CloseBotPositions("Daily loss limit reached");

            if (_consecutiveLosses >= MaxConsecutiveLosses)
                CloseBotPositions("Consecutive loss limit reached");
        }

        private bool CanTrade()
        {
            if (Server.Time.Hour < StartHourUtc || Server.Time.Hour > EndHourUtc)
                return false;

            if (Server.Time.DayOfWeek == DayOfWeek.Saturday || Server.Time.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (!TradeMonday && Server.Time.DayOfWeek == DayOfWeek.Monday)
                return false;

            if (!TradeFriday && Server.Time.DayOfWeek == DayOfWeek.Friday)
                return false;

            if (_dailyNetProfit <= -(Account.Balance * MaxDailyLossPercent / 100.0) ||
                _consecutiveLosses >= MaxConsecutiveLosses)
                return false;

            return CountTradesToday() < MaxTradesPerDay;
        }

        private int CountTradesToday()
        {
            int count = 0;
            foreach (var trade in History.FindAll(Label, Symbol.Name))
                if (trade.EntryTime.Date == Server.Time.Date) count++;

            foreach (var position in Positions.FindAll(Label, Symbol.Name))
                if (position.EntryTime.Date == Server.Time.Date) count++;

            return count;
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (args.Position.Label != Label || args.Position.SymbolName != Symbol.Name)
                return;

            _dailyNetProfit += args.Position.NetProfit;

            if (args.Position.NetProfit < 0)
                _consecutiveLosses++;
            else if (args.Position.NetProfit > 0)
                _consecutiveLosses = 0;
        }

        private void ResetDailyStateIfNeeded()
        {
            if (Server.Time.Date == _sessionDate)
                return;

            _sessionDate = Server.Time.Date;
            _dailyNetProfit = 0;
            _consecutiveLosses = 0;
        }

        private bool HasOpenPosition()
        {
            return Positions.FindAll(Label, Symbol.Name).Length > 0;
        }

        private void CloseBotPositions(string reason)
        {
            foreach (var position in Positions.FindAll(Label, Symbol.Name))
                ClosePosition(position);

            Print("SAFETY STOP: {0}", reason);
        }

        private double GetInitialRiskPips(Position position)
        {
            if (!position.StopLoss.HasValue)
                return 0;

            return Math.Abs(position.EntryPrice - position.StopLoss.Value) / Symbol.PipSize;
        }

        private bool ShouldImproveStop(Position position, double candidate)
        {
            if (position.TradeType == TradeType.Buy)
                return !position.StopLoss.HasValue || candidate > position.StopLoss.Value + Symbol.PipSize;

            return !position.StopLoss.HasValue || candidate < position.StopLoss.Value - Symbol.PipSize;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        protected override double GetFitness(GetFitnessArgs args)
        {
            if (args.TotalTrades < 50 || args.MaxEquityDrawdownPercentages >= 20)
                return double.MinValue;

            double profitFactor = Math.Max(0.01, args.ProfitFactor);
            double drawdownPenalty = 1.0 + args.MaxEquityDrawdownPercentages / 10.0;
            double tradeQuality = Math.Max(0.5, Math.Min(2.0, args.AverageTrade));
            double fitness = args.NetProfit * profitFactor * tradeQuality / drawdownPenalty;

            if (double.IsNaN(fitness) || double.IsInfinity(fitness))
                return double.MinValue;

            return fitness;
        }
    }
}
