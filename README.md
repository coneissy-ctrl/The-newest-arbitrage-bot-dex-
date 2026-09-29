# V1 US100 Scalper

This repository is now dedicated to the V1 cTrader US100/Nasdaq scalper.

## Strategy
- M1 execution
- M5 trend filter
- EMA fast/slow
- RSI momentum confirmation
- ATR dynamic SL
- Dynamic TP from SL/RR
- Risk-based sizing
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
- Custom optimization fitness

## Optimization
Use cTrader Custom optimization criteria so GetFitness() is used.

The fitness function combines net profit, profit factor and average trade, while penalizing maximum equity drawdown. It rejects passes with fewer than 50 trades or 20%+ maximum equity drawdown.

Suggested optimization ranges:
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
The default is US100. FxPro and other brokers may use a different exact CFD symbol name. Attach the cBot to the broker's actual US100/Nasdaq symbol.

## Files
- V1Scalper.cs
- README.md

No DEX/arbitrage code belongs in this repository.
