# V1 US100 Scalper

This repository is now dedicated to the V1 cTrader US100/Nasdaq scalper.

## V1 architecture
- Configurable execution timeframe, default M1
- Configurable trend timeframe, default M5
- EMA fast/slow trend and momentum filter
- RSI confirmation
- ATR-based dynamic stop loss
- Dynamic TP from SL/RR
- Risk-based volume sizing
- Spread filter
- One-position mode
- Cooldown
- Break-even
- ATR trailing
- Daily loss protection
- Consecutive-loss protection
- Daily trade cap
- UTC session controls
- Long/short controls
- Custom optimisation fitness

The execution timeframe is now obtained explicitly with MarketData.GetBars() and the strategy runs from that bars collection rather than silently depending on the chart timeframe.

## Debug fixes
- Fixed the execution-timeframe parameter so it actually controls the signal bars.
- Switched signal evaluation to the last closed execution bar.
- Preserved the original stop distance for R-multiple management so break-even/trailing changes do not corrupt the R calculation.
- Restored daily realised P/L and consecutive-loss state from history after restart.
- Uses the day-start balance for the daily-loss calculation.
- Uses the current cTrader VolumeForFixedRisk(..., RoundingMode) API.
- Uses the current ModifyPosition(position, stopLoss, takeProfit) form.
- Enforces the broker symbol's volume minimum/maximum.
- Custom fitness rejects low-trade, losing, invalid-profit-factor and 20%+ drawdown passes.

## Optimization

cTrader supports custom GetFitness(GetFitnessArgs args) optimisation. The current fitness maximises net profit and profit factor while penalising maximum equity drawdown. cTrader's current API exposes NetProfit, ProfitFactor, AverageTrade, TotalTrades and drawdown metrics through GetFitnessArgs.

Suggested initial optimisation ranges:
- Risk %: 0.25 to 1.00, step 0.25
- Max daily loss %: 1.00 to 3.00, step 0.50
- Consecutive losses: 2 to 5, step 1
- Trades/day: 10 to 40, step 5
- Fast EMA: 5 to 15, step 1
- Slow EMA: 15 to 35, step 2
- RSI period: 7 to 21, step 1
- RSI buy minimum: 50 to 60, step 2
- RSI sell maximum: 40 to 50, step 2
- ATR period: 7 to 21, step 1
- ATR SL multiplier: 0.80 to 2.00, step 0.10
- TP/SL ratio: 0.80 to 2.00, step 0.10
- Min SL: 5 to 20 pips, step 1
- Max SL: 30 to 100 pips, step 5
- Max spread: 5 to 30 pips, step 2.5
- Cooldown: 0 to 60 seconds, step 5
- Break-even trigger: 0.50R to 1.50R, step 0.10
- Trailing trigger: 0.75R to 2.00R, step 0.10
- Trailing ATR multiplier: 0.40 to 1.50, step 0.10

## Broker symbol

The default is US100. FxPro and other brokers may use a different exact CFD symbol name. Attach the cBot using the broker's actual US100/Nasdaq symbol.

## Files
- V1Scalper.cs
- README.md

No DEX/arbitrage code belongs in this repository.
