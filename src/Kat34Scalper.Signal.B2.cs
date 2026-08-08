/*
 * Kat34Scalper.Signal.B2.cs — Bot Signal sub-module B2: 89uturn34 (partial class Kat34Scalper).
 * Independent Bot Signal B2 (89uturn34 setup — 89-34 pullback setup).
 * Controls bot entry placement when Bot is ON. Spec in docs/SIGNALS.md.
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- B2 sub-module state ---
		private volatile bool cachedB2 = false;   // HUD toggle: B2 on/off (default OFF)
		private volatile bool b2BackfillPending;  // set on enable; consumed once by FlushBackfill
		private readonly KatA1State b2SellState = new KatA1State(); // sell-side sequence
		private readonly KatA1State b2BuyState = new KatA1State();  // buy-side sequence

		// HUD entry point. ON: compute + draw the History Days window immediately.
		// OFF: remove every B2 drawing — nothing else is touched.
		private void SetB2Signal(bool on)
		{
			cachedB2 = on;
			B2Enabled = on;
			if (on)
			{
				b2BackfillPending = true;
				TriggerCustomEvent(o => FlushBackfill(), null);
			}
			else
			{
				b2BackfillPending = false;
				b2SellState.Reset();
				b2BuyState.Reset();
				TriggerCustomEvent(o => { CancelSignalBotEntry("B2", "B2 switched OFF"); ClearB2Drawings(); }, null);
			}
		}

		private void EvaluateB2(double high, double low, double close, bool sellAllowed, bool buyAllowed)
		{
			if (!cachedB2 || fastEma == null || slowEma == null) return;
			if (CurrentBars[0] < Math.Max(EmaFastPeriod, EmaSlowPeriod)) return;
			Account acc = ResolveBotAccount();
			if (IsSignalInTrade("B2") || HasOpenPosition(acc)) return;

			double fast = fastEma[0];
			double slow = slowEma[0];
			int sellPhaseBefore = b2SellState.Phase;
			int buyPhaseBefore = b2BuyState.Phase;
			bool sellTouchedBefore = b2SellState.Touched89;
			bool buyTouchedBefore = b2BuyState.Touched89;
			KatSignalKind? sellSignal = null;
			KatSignalKind? buySignal = null;

			sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
				fast < slow, high, low, close, fast, slow, b2SellState);
			buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
				fast > slow, high, low, close, fast, slow, b2BuyState);

			if (sellSignal == KatSignalKind.Sell)
			{
				if (sellAllowed)
				{
					DrawSignal(false, CurrentBar, high, low, b2SellState.C1, b2SellState.C2, B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, false, "B2");
					TrySubmitBotEntry(false, b2SellState.C2, B2EntryOffsetTicks, "B2");
				}
				else
					Print(string.Format("[Kat34Scalper][B2] bar {0} SELL suppressed by filter; sellAllowed={1}",
						CurrentBar, sellAllowed));
			}
			if (buySignal == KatSignalKind.Buy)
			{
				if (buyAllowed)
				{
					DrawSignal(true, CurrentBar, high, low, b2BuyState.C1, b2BuyState.C2, B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, false, "B2");
					TrySubmitBotEntry(true, b2BuyState.C2, B2EntryOffsetTicks, "B2");
				}
				else
					Print(string.Format("[Kat34Scalper][B2] bar {0} BUY suppressed by filter; buyAllowed={1}",
						CurrentBar, buyAllowed));
			}

			if (b2SellState.Phase != sellPhaseBefore)
			{
				DrawB2PhaseMarkerAt(false, 0, high, low, b2SellState.Phase, b2SellState.Touched89);
				Print(string.Format("[Kat34Scalper][B2] bar {0} SELL phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, sellPhaseBefore, b2SellState.Phase, sellAllowed, fast < slow, close, fast, slow));
			}
			if (b2BuyState.Phase != buyPhaseBefore)
			{
				DrawB2PhaseMarkerAt(true, 0, high, low, b2BuyState.Phase, b2BuyState.Touched89);
				Print(string.Format("[Kat34Scalper][B2] bar {0} BUY phase {1}->{2}, allowed={3}, trend={4}, close={5:F5}, ema34={6:F5}, ema89={7:F5}",
					CurrentBar, buyPhaseBefore, b2BuyState.Phase, buyAllowed, fast > slow, close, fast, slow));
			}
			if (!sellTouchedBefore && b2SellState.Touched89 && b2SellState.Phase == 2)
				DrawB2PhaseMarkerAt(false, 0, high, low, 2, true);
			if (!buyTouchedBefore && b2BuyState.Touched89 && b2BuyState.Phase == 2)
				DrawB2PhaseMarkerAt(true, 0, high, low, 2, true);

			if (sellSignal.HasValue || buySignal.HasValue)
				Print(string.Format("[Kat34Scalper][B2] bar {0} result sell={1}, buy={2}",
					CurrentBar, sellSignal.HasValue ? sellSignal.Value.ToString() : "none",
					buySignal.HasValue ? buySignal.Value.ToString() : "none"));
		}

		private void DrawB2PhaseMarkerAt(bool isBuy, int barsAgo, double high, double low, int phase, bool touched)
		{
			string label;
			if (phase == 1) label = "B2-arm";
			else if (phase == 2) label = touched ? "B2-pull-T" : "B2-pull";
			else return;
			string tag = "K34S_B2ST_" + (isBuy ? "B" : "S") + "_" + (CurrentBars[0] - barsAgo);
			double y = isBuy ? low - ArrowOffsetTicks * TickSize : high + ArrowOffsetTicks * TickSize;
			Brush brush = isBuy ? Brushes.DodgerBlue : Brushes.OrangeRed;
			Draw.Text(this, tag, label, barsAgo, y, brush);
		}

		private void BackfillB2()
		{
			int warm = Math.Max(EmaFastPeriod, EmaSlowPeriod);
			int start = Math.Min(FindHistoryStartBarsAgo(B2HistoryDays), CurrentBars[0] - warm);
			if (start < 0) return;
			var tmpSell = new KatA1State();
			var tmpBuy = new KatA1State();
			for (int ago = start; ago >= 0; ago--)
			{
				double h = Highs[0][ago];
				double l = Lows[0][ago];
				double c = Closes[0][ago];
				double f = fastEma[ago];
				double sl = slowEma[ago];
				int sellPhaseBefore = tmpSell.Phase;
				int buyPhaseBefore = tmpBuy.Phase;
				bool sellTouchedBefore = tmpSell.Touched89;
				bool buyTouchedBefore = tmpBuy.Touched89;

				bool sellAllowed, buyAllowed;
				PassFiltersAt(ago, out sellAllowed, out buyAllowed);

				KatSignalKind? sellSignal = Kat34ScalperLogic.Update(KatSignalKind.Sell, MaxSequenceBars,
					f < sl, h, l, c, f, sl, tmpSell);
				KatSignalKind? buySignal = Kat34ScalperLogic.Update(KatSignalKind.Buy, MaxSequenceBars,
					f > sl, h, l, c, f, sl, tmpBuy);

				if (sellSignal == KatSignalKind.Sell && sellAllowed)
					DrawSignal(false, CurrentBars[0] - ago, h, l, tmpSell.C1, tmpSell.C2, B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, true, "B2");
				if (buySignal == KatSignalKind.Buy && buyAllowed)
					DrawSignal(true, CurrentBars[0] - ago, h, l, tmpBuy.C1, tmpBuy.C2, B2EntryOffsetTicks, B2StopDistanceTicks, B2TargetDistanceTicks, true, "B2");

				if (tmpSell.Phase != sellPhaseBefore)
					DrawB2PhaseMarkerAt(false, ago, h, l, tmpSell.Phase, tmpSell.Touched89);
				if (tmpBuy.Phase != buyPhaseBefore)
					DrawB2PhaseMarkerAt(true, ago, h, l, tmpBuy.Phase, tmpBuy.Touched89);
				if (!sellTouchedBefore && tmpSell.Touched89 && tmpSell.Phase == 2)
					DrawB2PhaseMarkerAt(false, ago, h, l, 2, true);
				if (!buyTouchedBefore && tmpBuy.Touched89 && tmpBuy.Phase == 2)
					DrawB2PhaseMarkerAt(true, ago, h, l, 2, true);
			}
			b2SellState.CopyFrom(tmpSell);
			b2BuyState.CopyFrom(tmpBuy);
			Print(string.Format("[Kat34Scalper][B2] backfill done — {0} day(s), {1} bar(s) replayed; live states synced (sell phase {2}, buy phase {3}).",
				B2HistoryDays, start + 1, b2SellState.Phase, b2BuyState.Phase));
		}

		private void ClearB2Drawings()
		{
			signalRecords.RemoveAll(r => r.Owner == "B2");
			RemoveModuleDrawings("K34S_B2_");
			RemoveModuleDrawings("K34S_B2ST_");
		}
	}
}
