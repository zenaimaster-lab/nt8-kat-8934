/*
 * Kat34Scalper.Filter.cs — Filter module (partial class Kat34Scalper).
 * Gates that decide whether a signal may fire on a bar. Every gate has a *At(barsAgo)
 * variant so the signal backfill replays evaluate the same gates on historical bars.
 * New filters (MACD, RSI, ...) plug in as a new method here + one clause in PassFiltersAt.
 *   BOT gates (ADX rising, ADX MTF, ER, CI, Volume, Time window) feed B1+B2 and the A2 alert
 *   placeholder. Since v0.79 there is NO alert-side filter: A1 is a pure EMA fan. The old
 *   A1-only legs (ADX rising, ADX MTF) moved here; the alert-side ER/CI duplicates were dropped.
 * Every gate is OFF by default (session-only toggles boot OFF on every load).
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- Filter module state (HUD toggles — default OFF: every gate open until user enables) ---
		private volatile bool cachedAdxRise; // moved from the alert side v0.79 (was "ADX rising (A1)")
		private volatile bool cachedAdxMtf;  // moved from the alert side v0.79 (was "ADX MTF (A1)")
		private volatile bool cachedEr;
		private volatile bool cachedCi;
		private volatile bool cachedVol;
		private volatile bool cachedTime;

		// Live entry point (current bar) with the gate-transition diagnostic print.
		private void PassFilters(out bool sellAllowed, out bool buyAllowed)
		{
			PassFiltersAt(0, out sellAllowed, out buyAllowed);
			if (!diagnosticGateInitialized ||
				diagnosticSellAllowed != sellAllowed || diagnosticBuyAllowed != buyAllowed)
			{
				diagnosticGateInitialized = true;
				diagnosticSellAllowed = sellAllowed;
				diagnosticBuyAllowed = buyAllowed;
				Print(string.Format("[Kat34Scalper][GATE] bar {0} sellAllowed={1}, buyAllowed={2}, vol={3}, time={4}",
					CurrentBar, sellAllowed, buyAllowed, cachedVol, cachedTime));
			}
		}

		// Market + time gates at any bar (barsAgo 0 = live, >0 = backfill replay).
		private void PassFiltersAt(int barsAgo, out bool sellAllowed, out bool buyAllowed)
		{
			bool pass = MarketPassAt(barsAgo) && TimePassAt(barsAgo);
			sellAllowed = pass && StackEmaFilterPassAt(barsAgo, false);
			buyAllowed  = pass && StackEmaFilterPassAt(barsAgo, true);
		}

		private bool MarketPassAt(int barsAgo)
		{
			if (cachedEr && !ErPassAt(barsAgo)) return false;
			if (cachedCi && !CiPassAt(barsAgo)) return false;
			int riseBars = Math.Max(1, AdxRisingBars); // 0 would compare adx against itself — gate permanently closed
			if (cachedAdxRise && (adxInd == null || CurrentBars[0] < barsAgo + riseBars || adxInd[barsAgo] <= adxInd[barsAgo + riseBars])) return false;
			if (cachedAdxMtf && !AdxMtfPassAt(barsAgo)) return false;
			// Volume leg only: ADX plain gate removed v0.77, so skip dummy adx params
			if (cachedVol && volSmaInd != null)
			{
				double volSma = volSmaInd[barsAgo];
				if (volSma > 0 && Volumes[0][barsAgo] < volSma * VolumeMinMult) return false;
			}
			return true;
		}

		// Kaufman Efficiency Ratio over the last ErPeriod bars ending at barsAgo (oldest -> newest).
		private bool ErPassAt(int barsAgo)
		{
			int n = Math.Max(2, ErPeriod);
			if (CurrentBars[0] < barsAgo + n) return false;
			double[] closes = new double[n];
			for (int i = 0; i < n; i++) closes[i] = Closes[0][barsAgo + n - 1 - i];
			return Kat34ScalperLogic.EfficiencyRatio(closes) >= ErMin;
		}

		// Choppiness Index over the last CiPeriod bars ending at barsAgo (closes carry one extra prior bar).
		private bool CiPassAt(int barsAgo)
		{
			int n = Math.Max(2, CiPeriod);
			if (CurrentBars[0] < barsAgo + n) return false;
			double[] highs = new double[n];
			double[] lows = new double[n];
			double[] closes = new double[n + 1];
			closes[0] = Closes[0][barsAgo + n];
			for (int i = 0; i < n; i++)
			{
				highs[i] = Highs[0][barsAgo + n - 1 - i];
				lows[i] = Lows[0][barsAgo + n - 1 - i];
				closes[i + 1] = Closes[0][barsAgo + n - 1 - i];
			}
			return Kat34ScalperLogic.ChoppinessIndex(highs, lows, closes) <= CiMax;
		}

		private bool TimePassAt(int barsAgo)
		{
			if (!cachedTime || timeWindowDisabled) return true;
			return Kat34ScalperLogic.IsInTimeWindow(Times[0][barsAgo].TimeOfDay, timeStart, timeEnd);
		}

		// ADX regime gate on the dedicated MTF series (BarsArray[1]): the most recent MTF bar CLOSED
		// at or before the series-0 bar's close must have ADX >= AdxMtfMin (no lookahead, backfill-aware).
		private bool AdxMtfPassAt(int barsAgo)
		{
			if (adxMtfInd == null || CurrentBars == null || CurrentBars.Length < 2 || CurrentBars[1] < 1) return true;
			DateTime cutoff = Kat34ScalperLogic.ClosedBarCutoff(Times[0][barsAgo], SeriesPeriodSeconds(0), SeriesPeriodSeconds(1));
			int idx = Kat34ScalperLogic.BarsAgoAtOrBefore(i => Times[1][i], CurrentBars[1], cutoff);
			if (idx < 0) return true; // MTF series starts after the bar — warmup, gate open
			return adxMtfInd[idx] >= AdxMtfMin;
		}

		// Bar period in seconds for time-based series; 0 for non-time-based (tick/volume/range —
		// their completion time is unknowable from timestamps, so cutoffs stay conservative).
		private double SeriesPeriodSeconds(int series)
		{
			var bp = BarsArray[series].BarsPeriod;
			if (bp.BarsPeriodType == Data.BarsPeriodType.Second) return Math.Max(1, bp.Value);
			if (bp.BarsPeriodType == Data.BarsPeriodType.Minute) return Math.Max(1, bp.Value) * 60.0;
			return 0;
		}
	}
}
