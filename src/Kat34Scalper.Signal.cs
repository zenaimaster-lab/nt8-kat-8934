/*
 * Kat34Scalper.Signal.cs — Bot Signal module shared helpers (partial class Kat34Scalper).
 * Standardized Bot Signals:
 *   src/Kat34Scalper.Signal.B1.cs — B1: 89-34 pullback (default OFF, backfill History Days)
 *   src/Kat34Scalper.Signal.B2.cs — B2: 34+8+Bounce ema34 touch (default OFF, backfill History Days)
 */

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// ponytail: no ': Indicator' here — NT8's codegen injects its generated region into EVERY
	// file that declares the base class, duplicating cacheKat34Scalper/wrappers across files.
	public partial class Kat34Scalper
	{
		// --- Shared signal-module diagnostics (written by Filter.PassFilters, read by B1/B2 prints) ---
		private bool diagnosticGateInitialized;
		private bool diagnosticSellAllowed;
		private bool diagnosticBuyAllowed;

		// Furthest barsAgo still inside the "last N days" window measured from the current bar.
		private int FindHistoryStartBarsAgo(int days)
		{
			if (days < 1) days = 1;
			DateTime cutoff = Times[0][0].Subtract(TimeSpan.FromDays(days));
			int max = CurrentBars[0];
			int ago = 0;
			while (ago < max && Times[0][ago] >= cutoff) ago++;
			return ago > 0 ? ago - 1 : 0;
		}

		// Runs each sub-module's one-shot backfill when it was enabled (load or HUD toggle).
		// Called from OnBarUpdate at the last available bar and from HUD clicks via TriggerCustomEvent.
		private void FlushBackfill()
		{
			if (CurrentBars == null || CurrentBars.Length == 0 || CurrentBars[0] < 1) return;
			if (b1BackfillPending)
			{
				b1BackfillPending = false;
				if (fastEma != null && slowEma != null) BackfillB1();
			}
			if (b2BackfillPending)
			{
				b2BackfillPending = false;
				if (ema8 != null && fastEma != null) BackfillB2();
			}
		}
	}
}
