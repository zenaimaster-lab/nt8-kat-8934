/* Kat34ScalperLogic.cs - pure signal state machine + ATM template parser, zero NT8 dependencies (unit-testable). */

using System;
using System.IO;
using System.Xml;

namespace Kat34Scalper
{
	public enum KatSignalKind
	{
		Sell,
		Buy
	}

	// A1 EMA34 zone timeframes (value = period seconds; NT8 renders enum names as the dropdown).
	public enum KatEmaZoneTf
	{
		S90 = 90,
		M1 = 60,
		M2 = 120,
		M3 = 180,
		M5 = 300,
		M15 = 900,
		M30 = 1800
	}

	/// <summary>
	/// Per-side A1 sequence state — the caller owns one instance per side (sell/buy).
	/// Phase: 0 = idle (waiting for price beyond the fast EMA), 1 = armed (price beyond the fast
	/// EMA, waiting for the cross back through it), 2 = pullback running (crossed, watching for the
	/// slow-EMA touch and the U-turn close — the signal fires on that close).
	/// </summary>
	public sealed class KatA1State
	{
		public int Phase;
		public bool Touched89;
		public int SeqBars;   // sequence lifetime in bars, counted from the ema34 cross bar
		public double C1;     // U-turn bar extreme (sell: its low / buy: its high)
		public double C2;     // best later candidate extreme

		public void Reset()
		{
			Phase = 0;
			Touched89 = false;
			SeqBars = 0;
			C1 = 0;
			C2 = 0;
		}

		// Backfill handoff: a replayed temp state replaces the live state so realtime
		// evaluation continues the in-flight sequence instead of re-arming from idle.
		public void CopyFrom(KatA1State other)
		{
			Phase = other.Phase;
			Touched89 = other.Touched89;
			SeqBars = other.SeqBars;
			C1 = other.C1;
			C2 = other.C2;
		}
	}

	/// <summary>Result of one A2 bar step — what the pending ema34-bounce entry did on this bar.</summary>
	public enum KatA2Action
	{
		None,     // nothing changed
		NewEntry, // first valid touch candle — place the pending stop at its extreme
		Migrate,  // later touch candle with a better extreme — move the pending entry
		Cancel,   // close beyond ema34 (or trend lost) — kill the pending entry
		Filled    // bar reached the pending entry — assume filled, setup done
	}

	/// <summary>
	/// Per-side A2 (34+8+Bounce) pending-entry state. Active = a touch candle already placed
	/// a pending stop entry; RefExtreme ratchets to better extremes only (buy: lowest touch
	/// high / sell: highest touch low). The caller owns one instance per side.
	/// </summary>
	public sealed class KatA2State
	{
		public bool Active;
		public double RefExtreme;

		public void Reset()
		{
			Active = false;
			RefExtreme = 0;
		}

		// Backfill handoff: replayed temp state replaces the live state (same contract as KatA1State).
		public void CopyFrom(KatA2State other)
		{
			Active = other.Active;
			RefExtreme = other.RefExtreme;
		}
	}

	public static class Kat34ScalperLogic
	{
		/// <summary>Close/flatten has work to do only if the account has working orders or an open position.</summary>
		public static bool ShouldFlattenAccount(bool hasWorkingOrders, bool hasOpenPosition)
		{
			return hasWorkingOrders || hasOpenPosition;
		}

		/// <summary>
		/// A1 effective entry from the two candidates: sell takes the higher stop (max), buy the lower (min).
		/// Sell stops sit below the candidate lows; buy stops above the candidate highs.
		/// </summary>
		public static double EffectiveEntry(bool isBuy, double c1, double c2, int offsetTicks, double tickSize)
		{
			if (isBuy)
				return Math.Min(c1, c2) + offsetTicks * tickSize;
			return Math.Max(c1, c2) - offsetTicks * tickSize;
		}

		/// <summary>
		/// Bot entry order type: a stop entry is only valid on the correct side of the market
		/// (sell stop BELOW / buy stop ABOVE current price). Price already past the trigger → limit.
		/// Same rule as KatTradeManager.DetermineOrderType.
		/// </summary>
		public static bool UseStopOrder(bool isBuy, double triggerPrice, double currentPrice)
		{
			return isBuy ? triggerPrice > currentPrice : triggerPrice < currentPrice;
		}

		/// <summary>
		/// Normalizes an ATM quick-set button label: trimmed, at most 3 characters, falling back to
		/// the default letter when empty/whitespace. Same contract as KatTradeManager.
		/// </summary>
		public static string NormalizeAtmSetName(string value, string fallback)
		{
			string trimmed = (value ?? string.Empty).Trim();
			if (trimmed.Length == 0) return fallback;
			return trimmed.Length > 3 ? trimmed.Substring(0, 3) : trimmed;
		}

		/// <summary>Market filter: ADX strength + relative volume. volumeSma 0 disables the volume leg.</summary>
		public static bool PassMarketFilter(double adx, double adxMin, double volume, double volumeSma, double volumeMult)
		{
			if (adx < adxMin) return false;
			if (volumeSma > 0 && volume < volumeSma * volumeMult) return false;
			return true;
		}

		/// <summary>
		/// Kaufman Efficiency Ratio over the window (oldest -&gt; newest): |net change| / sum of
		/// per-bar |changes|. 1 = perfectly trending, near 0 = choppy. Degenerate window
		/// (&lt;2 bars or zero movement) reads 0.
		/// </summary>
		public static double EfficiencyRatio(double[] closes)
		{
			if (closes == null || closes.Length < 2) return 0;
			double sum = 0;
			for (int i = 1; i < closes.Length; i++) sum += Math.Abs(closes[i] - closes[i - 1]);
			if (sum <= 0) return 0;
			return Math.Abs(closes[closes.Length - 1] - closes[0]) / sum;
		}

		/// <summary>
		/// Choppiness Index over an n-bar window: 100*log10(sumTR/(HH-LL))/log10(n), TR using the
		/// prior close. Arrays oldest -&gt; newest: highs/lows hold the n window bars; closes holds
		/// n+1 with closes[0] = the close BEFORE the window and closes[i+1] = close of window bar i.
		/// &gt;61.8 = ranging, &lt;38.2 = trending. Flat window (HH == LL) reads 100.
		/// </summary>
		public static double ChoppinessIndex(double[] highs, double[] lows, double[] closes)
		{
			int n = highs == null ? 0 : highs.Length;
			if (n < 2 || lows == null || lows.Length < n || closes == null || closes.Length < n + 1) return 0;
			double sumTr = 0, hh = double.MinValue, ll = double.MaxValue;
			for (int i = 0; i < n; i++)
			{
				double prevClose = closes[i];
				double tr = Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - prevClose), Math.Abs(lows[i] - prevClose)));
				sumTr += tr;
				if (highs[i] > hh) hh = highs[i];
				if (lows[i] < ll) ll = lows[i];
			}
			double range = hh - ll;
			if (range <= 0) return 100;
			return 100 * Math.Log10(sumTr / range) / Math.Log10(n);
		}

		/// <summary>Time window in machine-local time. start == end disables the window (always true). Overnight (start &gt; end) wraps midnight.</summary>
		public static bool IsInTimeWindow(TimeSpan time, TimeSpan start, TimeSpan end)
		{
			if (start == end) return true;
			if (start < end) return time >= start && time < end;
			return time >= start || time < end;
		}

		/// <summary>
		/// Advances the per-side state machine by one bar. Caller owns the KatA1State instance.
		/// Sell (downtrend: ema34 below ema89): price pulls back from BELOW ema34, crosses UP
		/// through ema34, touches/crosses ema89, reverses and closes back below ema34 (U-turn) —
		/// the signal fires immediately on that U-turn close. The whole sequence (cross bar
		/// included) must complete within maxSeqBars, otherwise the setup expires and rearms.
		/// Buy mirrors the same sequence.
		/// C1/C2 are kept (not cleared) when a signal fires so the caller can price the entry.
		/// </summary>
		public static KatSignalKind? Update(
			KatSignalKind kind, int maxSeqBars,
			bool trendOk,
			double high, double low, double close,
			double ema34, double ema89,
			KatA1State s)
		{
			if (!trendOk)
			{
				s.Reset();
				return null;
			}
			if (maxSeqBars < 1) maxSeqBars = 1;

			// Sequence lifetime: counted from the ema34 cross bar. Expired setups rearm from scratch.
			if (s.Phase >= 2)
			{
				s.SeqBars++;
				if (s.SeqBars > maxSeqBars) s.Reset();
			}

			if (kind == KatSignalKind.Sell)
			{
				// 0: idle — the pullback must start from BELOW ema34
				if (s.Phase == 0 && close < ema34) s.Phase = 1;

				// 1: armed below — the cross UP through ema34 (close basis) starts the sequence
				if (s.Phase == 1 && close > ema34)
				{
					s.Phase = 2;
					s.SeqBars = 1;
				}

			// 2: pullback running — watch the ema89 touch and the U-turn close back below ema34
			if (s.Phase == 2)
			{
				if (high >= ema89) s.Touched89 = true;
				if (close < ema34)
				{
					if (s.Touched89)
					{
						s.C1 = low;
						s.C2 = low;
						s.Phase = 1; // back below ema34 already — armed for the next pullback
						s.Touched89 = false;
						s.SeqBars = 0;
						return KatSignalKind.Sell;
					}
					// reversed below ema34 before ever touching ema89 — failed pullback, rearmed
					s.Phase = 1;
					s.SeqBars = 0;
				}
			}
		}
		else
		{
			// Buy mirrors Sell: armed ABOVE ema34, cross DOWN through it, touch ema89 from above,
			// U-turn close back above ema34 fires the signal.
			if (s.Phase == 0 && close > ema34) s.Phase = 1;

			if (s.Phase == 1 && close < ema34)
			{
				s.Phase = 2;
				s.SeqBars = 1;
			}

			if (s.Phase == 2)
			{
				if (low <= ema89) s.Touched89 = true;
				if (close > ema34)
				{
					if (s.Touched89)
					{
						s.C1 = high;
						s.C2 = high;
						s.Phase = 1;
						s.Touched89 = false;
						s.SeqBars = 0;
						return KatSignalKind.Buy;
					}
					s.Phase = 1;
					s.SeqBars = 0;
				}
			}
		}

			return null;
		}

		/// <summary>
		/// A1 (fan) — normalized EMA slope in degrees: slope per bar (price units) divided by the
		/// normalization unit (the price-per-bar move that reads as 45 degrees), then atan. Rising
		/// EMA yields positive degrees, falling negative. Zoom-independent and backfillable.
		/// </summary>
		public static double SlopeAngleDeg(double emaNow, double emaPrev, double normUnit)
		{
			if (normUnit <= 0) normUnit = 1;
			return Math.Atan((emaNow - emaPrev) / normUnit) * 180.0 / Math.PI;
		}

		/// <summary>
		/// A1 (fan) environment direction: +1 LONG when the EMA fan is 8 &gt; 34 &gt; 89 &gt; 144 &gt; 200
		/// (per enabled condition) and the EMA34 slope angle is at least +minAngleDeg (rising);
		/// -1 SHORT when the fan mirrors and the angle is at most -minAngleDeg (falling); 0 otherwise.
		/// Each fan/angle condition can be disabled via its own toggle.
		/// </summary>
		public static int A1Direction(
			bool cond8Above34, bool cond34Above89, bool cond89Above144, bool cond144Above200, bool condAngle,
			double e8, double e34, double e89, double e144, double e200,
			double angleDeg, double minAngleDeg)
		{
			bool buy = (!cond8Above34 || e8 > e34)
				&& (!cond34Above89 || e34 > e89)
				&& (!cond89Above144 || e89 > e144)
				&& (!cond144Above200 || e144 > e200)
				&& (!condAngle || angleDeg >= minAngleDeg);
			if (buy) return 1;

			bool sell = (!cond8Above34 || e8 < e34)
				&& (!cond34Above89 || e34 < e89)
				&& (!cond89Above144 || e89 < e144)
				&& (!cond144Above200 || e144 < e200)
				&& (!condAngle || angleDeg <= -minAngleDeg);
			return sell ? -1 : 0;
		}

		/// <summary>
		/// A1 (fan) edge-trigger step with break debounce: a fired environment stays armed until it
		/// has been invalid for breakBars consecutive bars ("điều kiện phá vỡ"), then the next valid
		/// environment fires again. Returns true when this bar fires a new alert line; a direction
		/// flip (1 -> -1) always fires.
		/// </summary>
		public static bool A1EdgeStep(int dir, int lastDir, int invalidStreak, int breakBars,
			out int newLastDir, out int newStreak)
		{
			if (breakBars < 1) breakBars = 1;
			if (dir != 0)
			{
				newStreak = 0;
				bool fired = dir != lastDir;
				newLastDir = dir;
				return fired;
			}
			newStreak = invalidStreak + 1;
			newLastDir = newStreak >= breakBars ? 0 : lastDir;
			return false;
		}

		/// <summary>
		/// A1 (fan) debounced environment direction for episode/band rendering: an armed environment
		/// (lastDir != 0) keeps counting as that environment until it has been invalid for breakBars
		/// consecutive bars — same "điều kiện phá vỡ" rule as <see cref="A1EdgeStep"/>. Use the state
		/// BEFORE feeding the bar through A1EdgeStep; feed the raw direction to the edge step itself.
		/// </summary>
		public static int A1DebouncedDir(int dir, int lastDir, int invalidStreak, int breakBars)
		{
			if (dir != 0) return dir;
			if (breakBars < 1) breakBars = 1;
			return invalidStreak + 1 >= breakBars ? 0 : lastDir;
		}

		/// <summary>
		/// A1 EMA34 zone condition (v0.84), mirrored by direction: LONG needs the zone bar's close
		/// above EMA34, SHORT below; direction 0 is neutral and passes.
		/// </summary>
		public static bool EmaZonePass(int dir, double close, double ema)
		{
			if (dir > 0) return close > ema;
			if (dir < 0) return close < ema;
			return true;
		}

		/// <summary>
		/// No-lookahead cutoff for cross-series gate reads: the latest bar-open timestamp a
		/// target-series bar may have and still be CLOSED by the time the source bar (opened at
		/// sourceBarOpen, period sourcePeriodSeconds) closes: sourceClose - targetPeriod.
		/// Pass targetPeriodSeconds = sourcePeriodSeconds for non-time-based target series (their
		/// completion time is unknowable from timestamps alone) so the cutoff stays at the source
		/// bar's open — conservative, never peeks.
		/// </summary>
		public static DateTime ClosedBarCutoff(DateTime sourceBarOpen, double sourcePeriodSeconds, double targetPeriodSeconds)
		{
			return sourceBarOpen.AddSeconds(sourcePeriodSeconds - targetPeriodSeconds);
		}

		/// <summary>
		/// Binary search over a descending time series addressed by barsAgo (index 0 = newest):
		/// the smallest barsAgo whose bar time is at or before t; -1 when t precedes every bar.
		/// Shared by the A1 module's cross-series time mappings (no lookahead by construction).
		/// </summary>
		public static int BarsAgoAtOrBefore(Func<int, DateTime> timeAt, int maxBarsAgo, DateTime t)
		{
			if (maxBarsAgo < 1) return -1;
			int lo = 0, hi = maxBarsAgo;
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (timeAt(mid) <= t) hi = mid; else lo = mid + 1;
			}
			return timeAt(lo) <= t ? lo : -1;
		}

		/// <summary>
		/// Environment band draw decision: a LONG/SHORT episode band is drawn only for a real
		/// direction over a real span (episode start bar index strictly before the end bar index)
		/// with a real price extent (hi &gt; lo). Args are ABSOLUTE bar indexes on the episode series
		/// (not barsAgo); the returned anchors ARE barsAgo (agoStart = episode start, agoEnd =
		/// episode end) for a time-anchored rectangle. False when nothing may be drawn.
		/// </summary>
		public static bool EnvBandAnchors(int dir, int startIdx, int endIdx, double hi, double lo,
			int currentBarIdx, out int agoStart, out int agoEnd)
		{
			agoStart = currentBarIdx - startIdx;
			agoEnd = currentBarIdx - endIdx;
			if (dir == 0 || startIdx >= endIdx || hi <= lo) return false;
			if (agoEnd < 0 || agoStart > currentBarIdx) return false;
			return true;
		}

		/// <summary>
		/// A2 (34+8+Bounce) — advances one pending-entry state machine by one bar.
		/// Buy: trend stack valid (caller evaluates the enabled ema conditions), price pulls back
		/// and TOUCHES ema34 (wick low &lt;= ema34) while CLOSING above it → pending stop LONG at the
		/// touch candle's high (+ offset). A later touch candle with a lower high migrates the entry
		/// down; a higher high means the stop would already have filled. A close below ema34 (or trend
		/// loss) cancels the entry. Sell mirrors: touch = high &gt;= ema34, close below; entry at the
		/// touch candle's low (- offset); migrate up to a higher low; close above ema34 cancels.
		/// Fill check runs first (entry = RefExtreme ± offset): once price reaches the trigger the
		/// setup is done regardless of what else the bar did.
		/// </summary>
		public static KatA2Action UpdateA2(
			KatSignalKind kind, bool trendOk,
			double high, double low, double close, double ema34,
			int offsetTicks, double tickSize,
			KatA2State s)
		{
			if (kind == KatSignalKind.Buy)
			{
				double trigger = s.RefExtreme + offsetTicks * tickSize;
				if (s.Active && high >= trigger) { s.Reset(); return KatA2Action.Filled; }
				if (!trendOk || close < ema34)
				{
					if (s.Active) { s.Reset(); return KatA2Action.Cancel; }
					return KatA2Action.None;
				}
				if (low <= ema34) // wick touched ema34 and the bar closed above it
				{
					if (!s.Active) { s.Active = true; s.RefExtreme = high; return KatA2Action.NewEntry; }
					if (high < s.RefExtreme) { s.RefExtreme = high; return KatA2Action.Migrate; }
				}
				return KatA2Action.None;
			}
			else
			{
				double trigger = s.RefExtreme - offsetTicks * tickSize;
				if (s.Active && low <= trigger) { s.Reset(); return KatA2Action.Filled; }
				if (!trendOk || close > ema34)
				{
					if (s.Active) { s.Reset(); return KatA2Action.Cancel; }
					return KatA2Action.None;
				}
				if (high >= ema34) // wick touched ema34 and the bar closed below it
				{
					if (!s.Active) { s.Active = true; s.RefExtreme = low; return KatA2Action.NewEntry; }
					if (low > s.RefExtreme) { s.RefExtreme = low; return KatA2Action.Migrate; }
				}
				return KatA2Action.None;
			}
		}

		/// <summary>
		/// A4 (OCO) price prioritization rules.
		/// Buy: always select the lowest buy level (closer/better entry).
		/// Sell: always select the highest sell level (closer/better entry).
		/// </summary>
		public static double SelectA4BuyPrice(double existingBuy, double candidateBuy)
		{
			if (existingBuy <= 0) return candidateBuy;
			return Math.Min(existingBuy, candidateBuy);
		}

		public static double SelectA4SellPrice(double existingSell, double candidateSell)
		{
			if (existingSell <= 0) return candidateSell;
			return Math.Max(existingSell, candidateSell);
		}

		/// <summary>
		/// Pure daily-risk gate: a limit can only breach while its toggle is ON and the configured
		/// limit is positive. OFF means never breached, regardless of PnL.
		/// </summary>
		public static bool EvaluateDailyRiskBreach(
			bool isMaxDDEnabled,
			double maxDD,
			bool isMaxProfitEnabled,
			double maxProfit,
			double dailyPnL,
			out string breachReason)
		{
			breachReason = string.Empty;

			if (isMaxDDEnabled && maxDD > 0 && dailyPnL <= -Math.Abs(maxDD))
			{
				breachReason = string.Format("Daily Max DD breached (Current Daily PnL: ${0:F2} <= Max DD limit: -${1:F2})", dailyPnL, Math.Abs(maxDD));
				return true;
			}

			if (isMaxProfitEnabled && maxProfit > 0 && dailyPnL >= maxProfit)
			{
				breachReason = string.Format("Daily Max Profit reached (Current Daily PnL: ${0:F2} >= Max Profit limit: ${1:F2})", dailyPnL, maxProfit);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Session baseline capture gate. The baseline (realized PnL at session start) must only be
		/// captured when the account read actually succeeded — capturing 0 after a failed read poisons
		/// the baseline and produces a phantom daily PnL (and a phantom risk breach) on the next read.
		/// </summary>
		public static bool ShouldCaptureSessionBaseline(bool isCaptured, DateTime currentSessionStartUtc, DateTime lastSessionStartUtc, bool readSucceeded)
		{
			if (!readSucceeded) return false;
			return !isCaptured || currentSessionStartUtc > lastSessionStartUtc;
		}

		/// <summary>
		/// Calculates UTC timestamp corresponding to 6:00 PM NY time (Eastern Time) of active trading session.
		/// </summary>
		public static DateTime GetNySessionStartUtc(DateTime nowUtc)
		{
			TimeZoneInfo nyZone;
			try
			{
				nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
			}
			catch (TimeZoneNotFoundException)
			{
				// Non-Windows host (e.g. CI Linux): EST maps to America/New_York
				nyZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("NY timezone not found: " + ex.Message, ex);
			}

			DateTime nowNy = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, nyZone);
			DateTime sessionStartNy;
			if (nowNy.TimeOfDay >= new TimeSpan(18, 0, 0))
			{
				sessionStartNy = nowNy.Date.AddHours(18);
			}
			else
			{
				sessionStartNy = nowNy.Date.AddDays(-1).AddHours(18);
			}

			return TimeZoneInfo.ConvertTimeToUtc(sessionStartNy, nyZone);
		}

		/// <summary>BE stop = entry ± bufferTicks * tickSize (Buy adds, Sell subtracts).</summary>
		public static double CalculateBreakevenPrice(bool isBuy, double entryPrice, int bufferTicks, double tickSize)
		{
			if (tickSize <= 0) return entryPrice;
			if (bufferTicks < 0) bufferTicks = 0;
			double raw = isBuy ? entryPrice + bufferTicks * tickSize : entryPrice - bufferTicks * tickSize;
			return Math.Round(raw / tickSize) * tickSize;
		}

		/// <summary>Long stop must sit below market; short stop above. Prevents broker rejection.</summary>
		public static bool IsStopOnValidSide(bool isLong, double stopPrice, double currentPrice)
		{
			if (stopPrice <= 0 || currentPrice <= 0) return false;
			return isLong ? stopPrice < currentPrice : stopPrice > currentPrice;
		}

		/// <summary>Returns true if position is Flat and active working orders exist that should be cleaned up.</summary>
		public static bool ShouldCancelFlatOrphans(bool isFlat, bool hasWorkingOrders, bool hasPendingEntry)
		{
			return isFlat && hasWorkingOrders && !hasPendingEntry;
		}

		/// <summary>Prevents ATM flat cleanup while entry startup is pending within grace period.</summary>
		public static bool ShouldDeferAtmFlatCleanup(
			bool atmEntryStartupPending,
			bool positionConfirmed,
			bool positionWasConfirmedThisEpisode,
			double millisecondsSinceLastAtmActivity,
			double graceMilliseconds)
		{
			if (positionConfirmed) return false;
			if (!positionWasConfirmedThisEpisode && atmEntryStartupPending) return true;
			if (double.IsNaN(millisecondsSinceLastAtmActivity)
				|| double.IsInfinity(millisecondsSinceLastAtmActivity)
				|| millisecondsSinceLastAtmActivity < 0)
				return true;

			return millisecondsSinceLastAtmActivity < Math.Max(0, graceMilliseconds);
		}

		/// <summary>Returns true if order action is an exit action for the given position side.</summary>
		public static bool IsAtmExitAction(bool isLongPosition, bool isSellAction)
		{
			return isLongPosition ? isSellAction : !isSellAction;
		}
	}



	/// <summary>Parsed SL/TP and trailing-SL trigger levels (ticks) from an NT8 ATM strategy template.</summary>
	public sealed class Kat34ScalperAtmData
	{
		public int StopLoss;
		public int Target;
		public int BETrigger;
		public int SL1Trigger;
		public int SL2Trigger;
		public int Quantity;
	}

	/// <summary>
	/// Reads StopLoss/Target/AutoBreakEven/AutoTrail profit triggers and EntryQuantity from an ATM template .xml.
	/// Any parse failure yields zeroed data (callers fall back to indicator settings).
	/// Named Kat34Scalper* on purpose: NT8 compiles every Custom indicator into ONE assembly —
	/// reusing KatTradeManager's type names would collide.
	/// </summary>
	public static class Kat34ScalperAtmParser
	{
		public static Kat34ScalperAtmData ParseFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return new Kat34ScalperAtmData();
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(filePath);
				return ParseDocument(doc);
			}
			catch
			{
				return new Kat34ScalperAtmData();
			}
		}

		public static Kat34ScalperAtmData ParseXml(string xmlContent)
		{
			if (string.IsNullOrWhiteSpace(xmlContent)) return new Kat34ScalperAtmData();
			try
			{
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(xmlContent);
				return ParseDocument(doc);
			}
			catch
			{
				return new Kat34ScalperAtmData();
			}
		}

		private static Kat34ScalperAtmData ParseDocument(XmlDocument doc)
		{
			Kat34ScalperAtmData result = new Kat34ScalperAtmData();
			if (doc == null) return result;

			result.StopLoss = ReadInt(doc, "//AtmStrategy/Brackets/Bracket/StopLoss");
			result.Target = ReadInt(doc, "//AtmStrategy/Brackets/Bracket/Target");
			result.BETrigger = ReadInt(doc, "//AtmStrategy/Brackets/Bracket/StopStrategy/AutoBreakEvenProfitTrigger");

			int entryQty = ReadInt(doc, "//AtmStrategy/EntryQuantity");
			if (entryQty <= 0)
			{
				XmlNodeList qtyNodes = doc.SelectNodes("//AtmStrategy/Brackets/Bracket/Quantity");
				if (qtyNodes != null)
				{
					foreach (XmlNode n in qtyNodes)
					{
						int q;
						if (n != null && int.TryParse(n.InnerText, out q))
							entryQty += q;
					}
				}
			}
			result.Quantity = entryQty > 0 ? entryQty : 0;

			XmlNodeList trailSteps = doc.SelectNodes("//AtmStrategy/Brackets/Bracket/StopStrategy/AutoTrailSteps/AutoTrailStep");
			if (trailSteps != null)
			{
				if (trailSteps.Count > 0) result.SL1Trigger = ReadInt(trailSteps[0], "ProfitTrigger");
				if (trailSteps.Count > 1) result.SL2Trigger = ReadInt(trailSteps[1], "ProfitTrigger");
			}
			return result;
		}

		private static int ReadInt(XmlDocument doc, string xpath)
		{
			XmlNode node = doc.SelectSingleNode(xpath);
			int value;
			return node != null && int.TryParse(node.InnerText, out value) ? value : 0;
		}

		private static int ReadInt(XmlNode parent, string name)
		{
			XmlNode node = parent == null ? null : parent.SelectSingleNode(name);
			int value;
			return node != null && int.TryParse(node.InnerText, out value) ? value : 0;
		}
	}

	// Alert sound file resolution — user sounds folder (Documents\NinjaTrader 8\sounds)
	// overlays NT8's install sounds folder; on equal names the user file wins.
	public static class Kat34ScalperSound
	{
		public static string ResolvePath(string userDir, string installDir, string fileName)
		{
			if (string.IsNullOrEmpty(fileName)) return null;
			string user = string.IsNullOrEmpty(userDir) ? null : Path.Combine(userDir, fileName);
			if (user != null && File.Exists(user)) return user;
			string install = string.IsNullOrEmpty(installDir) ? null : Path.Combine(installDir, fileName);
			if (install != null && File.Exists(install)) return install;
			return null;
		}

		public static System.Collections.Generic.List<string> ListSounds(string userDir, string installDir)
		{
			var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var list = new System.Collections.Generic.List<string>();
			string[] dirs = { userDir, installDir };
			foreach (string dir in dirs)
			{
				if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
				foreach (string f in Directory.GetFiles(dir, "*.wav"))
				{
					string name = Path.GetFileName(f);
					if (seen.Add(name)) list.Add(name);
				}
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return list;
		}
	}
}
