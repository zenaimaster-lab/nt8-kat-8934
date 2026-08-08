using Kat34Scalper;
using KatStackEMA;
using Xunit;

namespace Kat34Scalper.Tests;

public class Kat34ScalperLogicTests
{
	#region Stack EMA
	[Fact]
	public void StackEmaDirection_RequiresPriceAboveEveryEma()
	{
		Assert.Equal(1, StackEmaLogic.Direction(100, 99, 98, 97, 96, 95));
		Assert.Equal(0, StackEmaLogic.Direction(100, 99, 101, 97, 96, 95));
	}

	[Fact]
	public void StackEmaDirection_RequiresPriceBelowEveryEma()
	{
		Assert.Equal(-1, StackEmaLogic.Direction(90, 91, 92, 93, 94, 95));
		Assert.Equal(0, StackEmaLogic.Direction(90, 91, 89, 93, 94, 95));
	}

	[Fact]
	public void StackEmaFilter_UsesEnabledPacks_AndBypassesWhenNoneSelected()
	{
		Assert.True(StackEmaLogic.FilterPass(true, true, new[] { 1, 1, 0 }, new[] { true, true, false }));
		Assert.False(StackEmaLogic.FilterPass(true, true, new[] { 1, -1, 1 }, new[] { true, true, true }));
		Assert.True(StackEmaLogic.FilterPass(true, false, new[] { 1, 0, 1 }, new[] { false, false, false }));
	}

	[Fact]
	public void StackEmaClosedBarCutoff_LeavesTargetBarClosed()
	{
		var cutoff = StackEmaLogic.ClosedBarCutoff(new System.DateTime(2026, 8, 6, 10, 0, 0), 30, 180);
		Assert.Equal(new System.DateTime(2026, 8, 6, 9, 57, 30), cutoff);
	}

	[Fact]
	public void StackEmaTimeframeLabel_UsesCompactNames()
	{
		Assert.Equal("30s", StackEmaLogic.TimeframeLabel(StackEmaTimeframe.S30));
		Assert.Equal("15m", StackEmaLogic.TimeframeLabel(StackEmaTimeframe.M15));
	}

	[Fact]
	public void StackEmaSeriesMap_ReusesExistingAndDeduplicatesRequests()
	{
		var map = StackEmaLogic.MapRequestedSeries(new[] { 30, 180, 180, 300 }, new[] { 1, 3, 4, 5 }, new[] { 30, 60, 180, 300, 900 }, 6);
		Assert.Equal(new[] { 1, 6, 3, 5, 7 }, map);
	}
	#endregion

	// --- A1 sequence: pullback from beyond ema34 -> ema89 touch -> U-turn close through ema34 ---
	// Sell trend: ema34 below ema89 (e.g. 100.5 / 101.5). Buy trend mirrored (100.5 / 99.5).

	[Fact]
	public void Sell_Breakdown_FullSequence_FiresOnUturnClose()
	{
		var s = new KatA1State();
		// bar 1: close below ema34 -> armed
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s));
		// bar 2: cross UP through ema34 (close basis) -> sequence starts, no touch yet
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.0, 100.2, 101.0, 100.5, 101.5, s));
		// bar 3: high touches ema89, still closing above ema34
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.0, 100.8, 101.2, 100.5, 101.5, s));
		Assert.True(s.Touched89);
		// bar 4: U-turn close back below ema34 -> Breakdown fires immediately
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.8, 99.9, 100.5, 101.5, s);
		Assert.Equal(KatSignalKind.Sell, signal);
		Assert.Equal(99.8, s.C1);
		Assert.Equal(99.8, s.C2);
	}

	[Fact]
	public void Sell_RequiresPullbackFromBelowEma34_NoArmNoSignal()
	{
		var s = new KatA1State();
		// price was never below ema34: a touch of ema89 from ABOVE followed by a close below ema34 is NOT a setup
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 103.0, 101.0, 102.0, 100.5, 101.5, s));
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.5, 101.2, 102.2, 100.5, 101.5, s));
		// this bar only arms (close < ema34) — it must NOT fire, the pullback never happened
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.6, 99.6, 99.8, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
	}

	[Fact]
	public void Sell_WickAboveEma34_DoesNotCountAsCross()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		// wick pokes above ema34 but close stays below -> still armed, sequence not started
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.5, 99.8, 100.4, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
		// real cross on close basis
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.0, 100.2, 101.0, 100.5, 101.5, s));
		Assert.Equal(2, s.Phase);
	}

	[Fact]
	public void Sell_ExpiresAfterMaxSequenceBars()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.2, 101.5, 100.5, 101.5, s)); // cross+touch, seq=1
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.8, 101.4, 100.5, 101.5, s)); // seq=2
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.8, 101.4, 100.5, 101.5, s)); // seq=3
		// seq would be 4 > 3 -> expired and reset (close > ema34 -> no re-arm)
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 102.0, 100.8, 101.4, 100.5, 101.5, s));
		Assert.Equal(0, s.Phase);
		// late U-turn close below ema34: only re-arms, must NOT fire on the stale touch
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 3, true, 100.8, 99.8, 99.9, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
	}

	[Fact]
	public void Sell_UturnOnLastAllowedBar_StillFires()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 2, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 2, true, 102.0, 100.2, 101.5, 100.5, 101.5, s)); // cross+touch, seq=1
		// seq=2 == max -> still alive, U-turn fires
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Sell, 2, true, 100.8, 99.8, 99.9, 100.5, 101.5, s);
		Assert.Equal(KatSignalKind.Sell, signal);
	}

	[Fact]
	public void Sell_FailedPullback_ReArmsWithoutStaleTouch()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		// cross up but high stays below ema89 (no touch), then close back below ema34 -> failed pullback, rearmed
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 101.2, 100.2, 101.0, 100.5, 101.5, s));
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.7, 99.9, 100.5, 101.5, s));
		Assert.Equal(1, s.Phase);
		Assert.False(s.Touched89);
		// new pullback: cross + touch + U-turn -> fires
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.0, 100.2, 101.5, 100.5, 101.5, s));
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.8, 99.9, 100.5, 101.5, s);
		Assert.Equal(KatSignalKind.Sell, signal);
	}

	[Fact]
	public void Sell_TrendLoss_ResetsSequence()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.2, 99.3, 99.5, 100.5, 101.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 102.0, 100.2, 101.5, 100.5, 101.5, s)); // cross+touch
		// trend flips (ema34 no longer below ema89) -> full reset
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, false, 100.8, 99.8, 99.9, 100.5, 101.5, s));
		Assert.Equal(0, s.Phase);
		Assert.False(s.Touched89);
		// U-turn-like bar after reset: no fire
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Sell, 30, true, 100.8, 99.8, 99.9, 100.5, 101.5, s));
	}

	[Fact]
	public void Buy_Breakdown_FullSequence_FiresOnUturnClose()
	{
		var s = new KatA1State();
		// uptrend: ema34 100.5 above ema89 99.5
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 101.8, 101.0, 101.5, 100.5, 99.5, s)); // arm above ema34
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.8, 99.8, 100.0, 100.5, 99.5, s)); // cross DOWN through ema34
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.2, 99.4, 99.9, 100.5, 99.5, s)); // low touches ema89
		Assert.True(s.Touched89);
		var signal = Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.9, 99.9, 100.8, 100.5, 99.5, s); // U-turn close above ema34
		Assert.Equal(KatSignalKind.Buy, signal);
		Assert.Equal(100.9, s.C1);
	}

	[Fact]
	public void Buy_ExpiresAfterMaxSequenceBars()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 101.8, 101.0, 101.5, 100.5, 99.5, s)); // arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.8, 99.4, 99.9, 100.5, 99.5, s)); // cross+touch, seq=1
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.2, 99.6, 100.0, 100.5, 99.5, s)); // seq=2
		// seq=3 > 2 -> expired; this bar closes above ema34 so it only rearms (phase 1)
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.8, 99.6, 100.6, 100.5, 99.5, s));
		Assert.Equal(1, s.Phase);
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 2, true, 100.9, 100.1, 100.8, 100.5, 99.5, s)); // no fire on stale touch
	}

	[Fact]
	public void Buy_RequiresPullbackFromAboveEma34_NoArmNoSignal()
	{
		var s = new KatA1State();
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 99.0, 98.0, 98.5, 100.5, 99.5, s)); // below both, no arm
		Assert.Null(Kat34ScalperLogic.Update(KatSignalKind.Buy, 30, true, 100.9, 99.8, 100.6, 100.5, 99.5, s)); // arm (close > ema34)
		Assert.Equal(1, s.Phase);
	}

	// --- Market filter ---
	[Fact]
	public void Market_AdxTooLow_Blocked()
	{
		Assert.False(Kat34ScalperLogic.PassMarketFilter(15, 20, 1000, 500, 1.0));
	}

	[Fact]
	public void Market_VolumeTooLow_Blocked()
	{
		Assert.False(Kat34ScalperLogic.PassMarketFilter(25, 20, 400, 500, 1.0));
	}

	[Fact]
	public void Market_BothOk_Passes()
	{
		Assert.True(Kat34ScalperLogic.PassMarketFilter(25, 20, 1000, 500, 1.0));
	}

	[Fact]
	public void Market_ZeroVolSma_DisablesVolumeLeg()
	{
		Assert.True(Kat34ScalperLogic.PassMarketFilter(25, 20, 0, 0, 1.0));
	}

	[Fact]
	public void Market_AdxExactlyAtMin_Passes()
	{
		Assert.True(Kat34ScalperLogic.PassMarketFilter(20, 20, 1000, 500, 1.0));
	}

	// --- Range filters: EfficiencyRatio / ChoppinessIndex ---
	[Fact]
	public void Er_FlatCloses_IsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.EfficiencyRatio(new double[] { 100, 100, 100, 100 }), 10);
	}

	[Fact]
	public void Er_PerfectTrend_IsOne()
	{
		Assert.Equal(1, Kat34ScalperLogic.EfficiencyRatio(new double[] { 100, 101, 102, 103, 104 }), 10);
	}

	[Fact]
	public void Er_Sawtooth_IsChoppy()
	{
		Assert.True(Kat34ScalperLogic.EfficiencyRatio(new double[] { 0, 1, 0, 1, 0, 1, 0, 1 }) < 0.25);
	}

	[Fact]
	public void Er_DegenerateWindow_IsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.EfficiencyRatio(new double[] { 100 }), 10);
		Assert.Equal(0, Kat34ScalperLogic.EfficiencyRatio(null), 10);
	}

	[Fact]
	public void Ci_StrongTrend_ReadsBelow38()
	{
		int n = 40;
		var h = new double[n]; var l = new double[n]; var c = new double[n + 1];
		c[0] = -1;
		for (int i = 0; i < n; i++) { h[i] = i + 0.5; l[i] = i - 0.5; c[i + 1] = i; }
		Assert.True(Kat34ScalperLogic.ChoppinessIndex(h, l, c) < 38.2);
	}

	[Fact]
	public void Ci_Sawtooth_ReadsAbove61()
	{
		int n = 40;
		var h = new double[n]; var l = new double[n]; var c = new double[n + 1];
		c[0] = 1;
		for (int i = 0; i < n; i++) { double close = i % 2; h[i] = close + 0.5; l[i] = close - 0.5; c[i + 1] = close; }
		Assert.True(Kat34ScalperLogic.ChoppinessIndex(h, l, c) > 61.8);
	}

	[Fact]
	public void Ci_FlatWindow_Is100()
	{
		Assert.Equal(100, Kat34ScalperLogic.ChoppinessIndex(new double[] { 5, 5, 5 }, new double[] { 5, 5, 5 }, new double[] { 5, 5, 5, 5 }), 10);
	}

	// --- BarsAgoAtOrBefore (cross-series time mapping) ---
	[Fact]
	public void BarsAgo_ExactMatch_ReturnsThatAgo()
	{
		var times = new[] { T(10, 0), T(9, 30), T(9, 0), T(8, 30) }; // index 0 = newest
		Assert.Equal(2, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times[i], times.Length - 1, T(9, 0)));
	}

	[Fact]
	public void BarsAgo_BetweenBars_ReturnsNewerClosedBar()
	{
		var times = new[] { T(10, 0), T(9, 30), T(9, 0), T(8, 30) };
		Assert.Equal(1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times[i], times.Length - 1, T(9, 45)));
	}

	[Fact]
	public void BarsAgo_OlderThanAll_ReturnsMinusOne()
	{
		var times = new[] { T(10, 0), T(9, 30), T(9, 0) };
		Assert.Equal(-1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times[i], times.Length - 1, T(8, 0)));
	}

	[Fact]
	public void BarsAgo_NewerThanAll_ReturnsZero()
	{
		var times = new[] { T(10, 0), T(9, 30), T(9, 0) };
		Assert.Equal(0, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times[i], times.Length - 1, T(12, 0)));
	}

	private static DateTime T(int h, int m) => new DateTime(2026, 8, 4, h, m, 0);

	// --- ClosedBarCutoff (no-lookahead cross-series gate reads) ---
	[Fact]
	public void Cutoff_SlowerTimeTarget_ExcludesBarNotClosedBySourceClose()
	{
		// A1 30s bar opened 10:04:30 closes 10:05:00; a 3m MTF bar opened 10:03 closes 10:06 — NOT readable.
		var cutoff = Kat34ScalperLogic.ClosedBarCutoff(T(10, 4).AddSeconds(30), 30, 180);
		var mtfOpens = new[] { T(10, 3), T(10, 0) }; // index 0 = newest
		Assert.Equal(1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => mtfOpens[i], mtfOpens.Length - 1, cutoff));
	}

	[Fact]
	public void Cutoff_SlowerTimeTarget_AdmitsBarOnceClosed()
	{
		// Same MTF bar IS readable by the A1 bar opened 10:05:30 (closes 10:06:00 = MTF close).
		var cutoff = Kat34ScalperLogic.ClosedBarCutoff(T(10, 5).AddSeconds(30), 30, 180);
		var mtfOpens = new[] { T(10, 3), T(10, 0) };
		Assert.Equal(0, Kat34ScalperLogic.BarsAgoAtOrBefore(i => mtfOpens[i], mtfOpens.Length - 1, cutoff));
	}

	[Fact]
	public void Cutoff_FasterTimeTarget_IncludesBarsClosedBySourceClose()
	{
		// Chart 10s, A1 30s: series-0 bar opened 10:04:50 closes 10:05:00 — readable by the A1 bar opened 10:04:30.
		var cutoff = Kat34ScalperLogic.ClosedBarCutoff(T(10, 4).AddSeconds(30), 30, 10);
		Assert.Equal(T(10, 4).AddSeconds(50), cutoff);
	}

	[Fact]
	public void Cutoff_NonTimeTarget_FallsBackToSourceOpen()
	{
		// Tick/volume target: completion time unknowable — cutoff stays at the source open (conservative).
		var t = T(10, 4).AddSeconds(30);
		Assert.Equal(t, Kat34ScalperLogic.ClosedBarCutoff(t, 30, 30));
	}

	// --- EnvBandAnchors (environment band draw decision; args are absolute bar INDEXES) ---
	[Fact]
	public void EnvBand_ValidEpisode_DrawsAndConvertsToBarsAgo()
	{
		// episode bars 100..150 on a series whose current index is 200
		Assert.True(Kat34ScalperLogic.EnvBandAnchors(1, 100, 150, 4500, 4400, 200, out int agoStart, out int agoEnd));
		Assert.Equal(100, agoStart); // 200 - 100: episode start is the older bar
		Assert.Equal(50, agoEnd);    // 200 - 150: episode end is the newer bar
	}

	[Fact]
	public void EnvBand_Ranging_NotDrawn()
	{
		Assert.False(Kat34ScalperLogic.EnvBandAnchors(0, 100, 150, 4500, 4400, 200, out _, out _));
	}

	[Fact]
	public void EnvBand_SameBarEpisode_NotDrawn()
	{
		// dir changed THIS bar — the closed episode would be zero-length (startIdx == endIdx)
		Assert.False(Kat34ScalperLogic.EnvBandAnchors(1, 150, 150, 4500, 4400, 200, out _, out _));
	}

	[Fact]
	public void EnvBand_FlatExtent_NotDrawn()
	{
		Assert.False(Kat34ScalperLogic.EnvBandAnchors(1, 100, 150, 4400, 4400, 200, out _, out _));
	}

	// --- Time window ---
	[Fact]
	public void Time_InsideWindow_True()
	{
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(10, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
	}

	[Fact]
	public void Time_OutsideWindow_False()
	{
		Assert.False(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(18, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0)));
	}

	[Fact]
	public void Time_OvernightWindow_WrapsMidnight()
	{
		var start = new TimeSpan(22, 0, 0);
		var end = new TimeSpan(6, 0, 0);
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(23, 30, 0), start, end));
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(2, 0, 0), start, end));
		Assert.False(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(12, 0, 0), start, end));
	}

	[Fact]
	public void Time_StartEqualsEnd_Disabled_AlwaysTrue()
	{
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(new TimeSpan(3, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(8, 0, 0)));
	}

	[Fact]
	public void Time_StartInclusive_EndExclusive_Boundaries()
	{
		var start = new TimeSpan(8, 0, 0);
		var end = new TimeSpan(17, 0, 0);
		Assert.True(Kat34ScalperLogic.IsInTimeWindow(start, start, end));
		Assert.False(Kat34ScalperLogic.IsInTimeWindow(end, start, end));
	}

	// --- Bot entry order type (stop only on the valid side of market, else limit) ---
	[Fact]
	public void BotEntry_SellStopBelowMarket_UsesStop_AboveMarket_UsesLimit()
	{
		Assert.True(Kat34ScalperLogic.UseStopOrder(false, 99.5, 100.0));  // sell stop below current -> valid stop
		Assert.False(Kat34ScalperLogic.UseStopOrder(false, 100.5, 100.0)); // price ran past -> limit
	}

	[Fact]
	public void BotEntry_BuyStopAboveMarket_UsesStop_BelowMarket_UsesLimit()
	{
		Assert.True(Kat34ScalperLogic.UseStopOrder(true, 100.5, 100.0));  // buy stop above current -> valid stop
		Assert.False(Kat34ScalperLogic.UseStopOrder(true, 99.5, 100.0));   // price ran past -> limit
	}

	[Fact]
	public void BotEntry_TriggerEqualsMarket_UsesLimit_BothSides()
	{
		Assert.False(Kat34ScalperLogic.UseStopOrder(false, 100.0, 100.0)); // sell stop must be below market
		Assert.False(Kat34ScalperLogic.UseStopOrder(true, 100.0, 100.0));  // buy stop must be above market
	}

	// --- EffectiveEntry ---
	[Fact]
	public void EffectiveEntry_Sell_TakesHigherStop()
	{
		// sell: below candidate lows; c2 higher -> better
		Assert.Equal(99.9 - 0.25, Kat34ScalperLogic.EffectiveEntry(false, 99.5, 99.9, 1, 0.25));
	}

	[Fact]
	public void EffectiveEntry_Buy_TakesLowerStop()
	{
		// buy: above candidate highs; c2 lower -> better
		Assert.Equal(100.1 + 0.25, Kat34ScalperLogic.EffectiveEntry(true, 100.5, 100.1, 1, 0.25));
	}

	// --- ATM template parser ---
	private const string SampleAtmXml =
		"<AtmStrategy><Brackets><Bracket>" +
		"<StopLoss>60</StopLoss><Target>120</Target>" +
		"<StopStrategy><AutoBreakEvenProfitTrigger>30</AutoBreakEvenProfitTrigger>" +
		"<AutoTrailSteps>" +
		"<AutoTrailStep><ProfitTrigger>45</ProfitTrigger></AutoTrailStep>" +
		"<AutoTrailStep><ProfitTrigger>80</ProfitTrigger></AutoTrailStep>" +
		"</AutoTrailSteps></StopStrategy>" +
		"</Bracket></Brackets></AtmStrategy>";

	[Fact]
	public void Atm_FullTemplate_ParsesAllLevels()
	{
		var d = Kat34ScalperAtmParser.ParseXml(SampleAtmXml);
		Assert.Equal(60, d.StopLoss);
		Assert.Equal(120, d.Target);
		Assert.Equal(30, d.BETrigger);
		Assert.Equal(45, d.SL1Trigger);
		Assert.Equal(80, d.SL2Trigger);
	}

	[Fact]
	public void Atm_MissingNodes_StayZero()
	{
		var d = Kat34ScalperAtmParser.ParseXml("<AtmStrategy><Brackets><Bracket><StopLoss>40</StopLoss></Bracket></Brackets></AtmStrategy>");
		Assert.Equal(40, d.StopLoss);
		Assert.Equal(0, d.Target);
		Assert.Equal(0, d.BETrigger);
		Assert.Equal(0, d.SL1Trigger);
	}

	[Fact]
	public void Atm_GarbageOrEmpty_ReturnsZeros()
	{
		Assert.Equal(0, Kat34ScalperAtmParser.ParseXml("not xml at all").StopLoss);
		Assert.Equal(0, Kat34ScalperAtmParser.ParseXml("").Target);
		Assert.Equal(0, Kat34ScalperAtmParser.ParseFile(@"C:\no\such\file.xml").StopLoss);
	}

	[Fact]
	public void Atm_ParseQuantity_ReadsEntryQuantityOrSumOfBrackets()
	{
		var xml1 = "<AtmStrategy><EntryQuantity>2</EntryQuantity><Brackets><Bracket><Quantity>2</Quantity></Bracket></Brackets></AtmStrategy>";
		Assert.Equal(2, Kat34ScalperAtmParser.ParseXml(xml1).Quantity);

		var xml2 = "<AtmStrategy><Brackets><Bracket><Quantity>1</Quantity></Bracket><Bracket><Quantity>2</Quantity></Bracket></Brackets></AtmStrategy>";
		Assert.Equal(3, Kat34ScalperAtmParser.ParseXml(xml2).Quantity);
	}

	// --- A2 (34+8+Bounce): pending stop entry at the touch candle's extreme ---
	// ema34 = 100.5. Buy: touch = low <= 100.5 with close > 100.5; entry trigger = RefExtreme + offset*tick.
	// Sell mirrors: touch = high >= 100.5 with close < 100.5; trigger = RefExtreme - offset*tick.

	[Fact]
	public void A2_Buy_TouchCloseAbove_NewEntryAtHigh()
	{
		var s = new KatA2State();
		// running above ema34, no touch yet -> nothing
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.5, 100.8, 101.2, 100.5, 4, 0.25, s));
		// wick dips to ema34, closes above -> NewEntry at the high
		Assert.Equal(KatA2Action.NewEntry, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s));
		Assert.True(s.Active);
		Assert.Equal(101.0, s.RefExtreme);
	}

	[Fact]
	public void A2_Buy_LowerHighTouch_MigratesEntryDown()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s); // NewEntry @101.0
		// next touch candle with a lower high -> migrate down
		Assert.Equal(KatA2Action.Migrate, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 100.8, 100.3, 100.7, 100.5, 4, 0.25, s));
		Assert.Equal(100.8, s.RefExtreme);
	}

	[Fact]
	public void A2_Buy_HigherHighBelowTrigger_KeepsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s); // trigger = 101.0 + 1.0 = 102.0
		// touch candle with a HIGHER high (101.5) but still below the trigger: not filled, not better -> no change
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.5, 100.4, 100.9, 100.5, 4, 0.25, s));
		Assert.True(s.Active);
		Assert.Equal(101.0, s.RefExtreme);
	}

	[Fact]
	public void A2_Buy_ReachingTrigger_Filled()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s); // trigger 102.0
		Assert.Equal(KatA2Action.Filled, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 102.1, 100.9, 101.8, 100.5, 4, 0.25, s));
		Assert.False(s.Active); // setup done — next touch starts fresh
	}

	[Fact]
	public void A2_Buy_CloseBelowEma34_CancelsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s);
		Assert.Equal(KatA2Action.Cancel, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 100.6, 100.0, 100.2, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
		// no entry active and a close below ema34 -> no signal at all (touch candle must CLOSE above)
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 100.6, 100.3, 100.4, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
	}

	[Fact]
	public void A2_Buy_TrendLoss_CancelsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s);
		Assert.Equal(KatA2Action.Cancel, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, false, 101.2, 100.7, 101.0, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
		// trend dead + no entry -> silent
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, false, 101.0, 100.4, 100.8, 100.5, 4, 0.25, s));
	}

	[Fact]
	public void A2_Sell_TouchCloseBelow_NewEntryAtLow_ThenMigratesUp()
	{
		var s = new KatA2State();
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.2, 99.4, 99.8, 100.5, 4, 0.25, s)); // no touch
		Assert.Equal(KatA2Action.NewEntry, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.6, 99.8, 100.2, 100.5, 4, 0.25, s));
		Assert.Equal(99.8, s.RefExtreme);
		// next touch candle with a higher low -> migrate the sell stop up
		Assert.Equal(KatA2Action.Migrate, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.7, 99.9, 100.1, 100.5, 4, 0.25, s));
		Assert.Equal(99.9, s.RefExtreme);
	}

	[Fact]
	public void A2_Sell_ReachingTrigger_Filled()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.6, 99.8, 100.2, 100.5, 4, 0.25, s); // trigger = 99.8 - 1.0 = 98.8
		Assert.Equal(KatA2Action.Filled, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.1, 98.7, 99.0, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
	}

	[Fact]
	public void A2_Sell_CloseAboveEma34_CancelsEntry()
	{
		var s = new KatA2State();
		Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.6, 99.8, 100.2, 100.5, 4, 0.25, s);
		Assert.Equal(KatA2Action.Cancel, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.9, 100.3, 100.7, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
		// touch candle that closes ABOVE ema34 never places a sell entry
		Assert.Equal(KatA2Action.None, Kat34ScalperLogic.UpdateA2(KatSignalKind.Sell, true, 100.7, 100.1, 100.6, 100.5, 4, 0.25, s));
		Assert.False(s.Active);
	}

	// --- A2 backtest: full synthetic bar series replayed through the state machine ---
	// ema34 flat at 100.0, offset 4 ticks × 0.25 = 1.0. Each bar = (high, low, close, trendOk).

	private static List<KatA2Action> ReplayA2(KatSignalKind kind, double ema34, double[][] bars)
	{
		var s = new KatA2State();
		var actions = new List<KatA2Action>();
		foreach (double[] b in bars)
			actions.Add(Kat34ScalperLogic.UpdateA2(kind, b[3] > 0.5, b[0], b[1], b[2], ema34, 4, 0.25, s));
		return actions;
	}

	[Fact]
	public void A2_Backtest_BuyRun_Touch_Migrate_Fill()
	{
		// 5 bars running above ema34 (no touch), pullback touches ema34 (NewEntry @101.0, trigger 102.0),
		// next touch with a lower high (Migrate to 100.7, trigger 101.7), rally reaches it (Filled).
		var bars = new[]
		{
			new[] { 101.5, 100.8, 101.2, 1.0 },
			new[] { 101.6, 100.9, 101.3, 1.0 },
			new[] { 101.4, 100.7, 101.1, 1.0 },
			new[] { 101.5, 100.8, 101.2, 1.0 },
			new[] { 101.3, 100.6, 101.0, 1.0 },
			new[] { 101.0,  99.9, 100.3, 1.0 }, // touch + close above -> NewEntry
			new[] { 100.7,  99.8, 100.2, 1.0 }, // touch, lower high -> Migrate
			new[] { 101.8, 100.5, 101.5, 1.0 }, // high 101.8 >= trigger 101.7 -> Filled
		};
		var actions = ReplayA2(KatSignalKind.Buy, 100.0, bars);
		Assert.Equal(
			new[] { KatA2Action.None, KatA2Action.None, KatA2Action.None, KatA2Action.None, KatA2Action.None,
				KatA2Action.NewEntry, KatA2Action.Migrate, KatA2Action.Filled },
			actions);
	}

	[Fact]
	public void A2_Backtest_BuyTouch_CloseBelow_Cancels()
	{
		// NewEntry then a close below ema34 kills the pending entry; later re-touch starts a fresh one.
		var bars = new[]
		{
			new[] { 101.0,  99.9, 100.3, 1.0 }, // NewEntry @101.0
			new[] { 100.4,  99.0,  99.5, 1.0 }, // close < 100 -> Cancel
			new[] { 100.8,  99.9, 100.4, 1.0 }, // touch + close above -> NewEntry again
			new[] { 101.5, 100.2, 101.2, 1.0 }, // trigger = 100.8+1.0=101.8 not reached -> None
			new[] { 102.0, 100.9, 101.9, 1.0 }, // high 102.0 >= 101.8 -> Filled
		};
		var actions = ReplayA2(KatSignalKind.Buy, 100.0, bars);
		Assert.Equal(
			new[] { KatA2Action.NewEntry, KatA2Action.Cancel, KatA2Action.NewEntry, KatA2Action.None, KatA2Action.Filled },
			actions);
	}

	[Fact]
	public void A2_Backtest_SellRun_TrendLossCancels_ThenSellFills()
	{
		// Sell entry placed, trend stack breaks (Cancel); trend returns, new touch, migrate up, fill.
		var bars = new[]
		{
			new[] { 99.2, 98.5, 99.0, 1.0 },  // running below, no touch
			new[] { 100.1, 99.0, 99.7, 1.0 }, // touch + close below -> NewEntry @99.0 (trigger 98.0)
			new[] { 100.3, 99.5, 99.8, 0.0 }, // trend lost -> Cancel
			new[] { 100.2, 99.1, 99.6, 1.0 }, // trend back, touch -> NewEntry @99.1 (trigger 98.1)
			new[] { 100.4, 99.3, 99.5, 1.0 }, // touch, higher low -> Migrate to 99.3 (trigger 98.3)
			new[] { 99.8, 98.2, 98.5, 1.0 },  // low 98.2 <= 98.3 -> Filled
		};
		var actions = ReplayA2(KatSignalKind.Sell, 100.0, bars);
		Assert.Equal(
			new[] { KatA2Action.None, KatA2Action.NewEntry, KatA2Action.Cancel,
				KatA2Action.NewEntry, KatA2Action.Migrate, KatA2Action.Filled },
			actions);
	}

	// --- ATM quick-set button labels ---
	[Fact]
	public void AtmSetName_WithinLimit_Kept()
	{
		Assert.Equal("A", Kat34ScalperLogic.NormalizeAtmSetName("A", "F"));
		Assert.Equal("ABC", Kat34ScalperLogic.NormalizeAtmSetName("ABC", "F"));
		Assert.Equal("1x", Kat34ScalperLogic.NormalizeAtmSetName("1x", "F"));
	}

	[Fact]
	public void AtmSetName_OverThreeChars_Truncated()
	{
		Assert.Equal("SCA", Kat34ScalperLogic.NormalizeAtmSetName("SCALP", "F"));
		Assert.Equal("ABC", Kat34ScalperLogic.NormalizeAtmSetName("ABCDE", "F"));
	}

	[Fact]
	public void AtmSetName_EmptyOrWhitespace_FallsBack()
	{
		Assert.Equal("B", Kat34ScalperLogic.NormalizeAtmSetName("", "B"));
		Assert.Equal("C", Kat34ScalperLogic.NormalizeAtmSetName("   ", "C"));
		Assert.Equal("D", Kat34ScalperLogic.NormalizeAtmSetName(null, "D"));
	}

	[Fact]
	public void AtmSetName_SurroundingWhitespace_Trimmed()
	{
		Assert.Equal("TP", Kat34ScalperLogic.NormalizeAtmSetName("  TP ", "F"));
		Assert.Equal("ABC", Kat34ScalperLogic.NormalizeAtmSetName(" ABCD ", "F"));
	}

	// --- Daily Risk (Max DD & Max Profit) tests ---
	[Fact]
	public void EvaluateDailyRiskBreach_OffToggles_NeverBreach()
	{
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, false, 1000.0, -50000.0, out _));
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, false, 1000.0, 99999.0, out _));
	}

	[Fact]
	public void EvaluateDailyRiskBreach_MaxDDBreach_WhenEnabledAndBeyondLimit()
	{
		Assert.True(Kat34ScalperLogic.EvaluateDailyRiskBreach(true, 500.0, false, 1000.0, -500.0, out string reason));
		Assert.Contains("Max DD", reason);
		Assert.True(Kat34ScalperLogic.EvaluateDailyRiskBreach(true, 500.0, false, 1000.0, -750.25, out _));
	}

	[Fact]
	public void EvaluateDailyRiskBreach_MaxProfitBreach_WhenEnabledAndReached()
	{
		Assert.True(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, true, 1000.0, 1000.0, out string reason));
		Assert.Contains("Max Profit", reason);
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(false, 500.0, true, 1000.0, 999.99, out _));
	}

	[Fact]
	public void ShouldCaptureSessionBaseline_Behavior()
	{
		DateTime session = new DateTime(2026, 7, 30, 22, 0, 0, DateTimeKind.Utc);
		Assert.False(Kat34ScalperLogic.ShouldCaptureSessionBaseline(false, session, DateTime.MinValue, false));
		Assert.True(Kat34ScalperLogic.ShouldCaptureSessionBaseline(false, session, DateTime.MinValue, true));
	}

	[Fact]
	public void ShouldFlattenAccount_Behavior()
	{
		Assert.True(Kat34ScalperLogic.ShouldFlattenAccount(true, false));
		Assert.True(Kat34ScalperLogic.ShouldFlattenAccount(false, true));
		Assert.True(Kat34ScalperLogic.ShouldFlattenAccount(true, true));
		Assert.False(Kat34ScalperLogic.ShouldFlattenAccount(false, false));
	}

	#region CalculateBreakevenPrice
	[Fact]
	public void CalculateBreakevenPrice_Long_AddsBuffer()
	{
		double be = Kat34ScalperLogic.CalculateBreakevenPrice(true, 20000.0, 2, 0.25);
		Assert.Equal(20000.50, be, 4);
	}

	[Fact]
	public void CalculateBreakevenPrice_Short_SubtractsBuffer()
	{
		double be = Kat34ScalperLogic.CalculateBreakevenPrice(false, 20000.0, 2, 0.25);
		Assert.Equal(19999.50, be, 4);
	}

	[Fact]
	public void CalculateBreakevenPrice_ZeroBuffer_ReturnsEntry()
	{
		Assert.Equal(5000.0, Kat34ScalperLogic.CalculateBreakevenPrice(true, 5000.0, 0, 0.25), 4);
		Assert.Equal(5000.0, Kat34ScalperLogic.CalculateBreakevenPrice(false, 5000.0, 0, 0.25), 4);
	}
	#endregion

	#region IsStopOnValidSide
	[Fact]
	public void IsStopOnValidSide_LongStopBelow_True()
	{
		Assert.True(Kat34ScalperLogic.IsStopOnValidSide(true, 19990.0, 20000.0));
	}

	[Fact]
	public void IsStopOnValidSide_LongStopAbove_False()
	{
		Assert.False(Kat34ScalperLogic.IsStopOnValidSide(true, 20010.0, 20000.0));
	}

	[Fact]
	public void IsStopOnValidSide_ShortStopAbove_True()
	{
		Assert.True(Kat34ScalperLogic.IsStopOnValidSide(false, 20010.0, 20000.0));
	}

	[Fact]
	public void IsStopOnValidSide_ShortStopBelow_False()
	{
		Assert.False(Kat34ScalperLogic.IsStopOnValidSide(false, 19990.0, 20000.0));
	}

	[Fact]
	public void IsStopOnValidSide_ZeroPrice_False()
	{
		Assert.False(Kat34ScalperLogic.IsStopOnValidSide(true, 0, 20000.0));
		Assert.False(Kat34ScalperLogic.IsStopOnValidSide(true, 19990.0, 0));
	}
	#endregion

	#region ShouldCancelFlatOrphans
	[Fact]
	public void ShouldCancelFlatOrphans_Behavior()
	{
		Assert.True(Kat34ScalperLogic.ShouldCancelFlatOrphans(true, true, false));
		Assert.False(Kat34ScalperLogic.ShouldCancelFlatOrphans(false, true, false));
		Assert.False(Kat34ScalperLogic.ShouldCancelFlatOrphans(true, false, false));
		Assert.False(Kat34ScalperLogic.ShouldCancelFlatOrphans(true, true, true));
	}
	#endregion

	#region ATM MERGE Logic
	[Fact]
	public void ShouldDeferAtmFlatCleanup_Behavior()
	{
		Assert.False(Kat34ScalperLogic.ShouldDeferAtmFlatCleanup(true, true, true, 1000, 3000));
		Assert.True(Kat34ScalperLogic.ShouldDeferAtmFlatCleanup(true, false, false, 1000, 3000));
		Assert.True(Kat34ScalperLogic.ShouldDeferAtmFlatCleanup(false, false, true, 1000, 3000));
		Assert.False(Kat34ScalperLogic.ShouldDeferAtmFlatCleanup(false, false, true, 5000, 3000));
	}

	[Fact]
	public void IsAtmExitAction_Behavior()
	{
		Assert.True(Kat34ScalperLogic.IsAtmExitAction(true, true));   // Long -> Sell
		Assert.False(Kat34ScalperLogic.IsAtmExitAction(true, false)); // Long -> Buy
		Assert.True(Kat34ScalperLogic.IsAtmExitAction(false, false)); // Short -> Buy
		Assert.False(Kat34ScalperLogic.IsAtmExitAction(false, true)); // Short -> Sell
	}
	#endregion

	#region Alert Signal A1 (fan) — SlopeAngleDeg
	[Fact]
	public void SlopeAngleDeg_FlatEma_IsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.SlopeAngleDeg(100.0, 100.0, 1.0), 10);
	}

	[Fact]
	public void SlopeAngleDeg_SlopeEqualsNorm_IsPlusMinus45()
	{
		Assert.Equal(45, Kat34ScalperLogic.SlopeAngleDeg(101.0, 100.0, 1.0), 10);
		Assert.Equal(-45, Kat34ScalperLogic.SlopeAngleDeg(99.0, 100.0, 1.0), 10);
	}

	[Fact]
	public void SlopeAngleDeg_NonPositiveNorm_FallsBackToOne()
	{
		Assert.Equal(45, Kat34ScalperLogic.SlopeAngleDeg(101.0, 100.0, 0), 10);
		Assert.Equal(45, Kat34ScalperLogic.SlopeAngleDeg(101.0, 100.0, -5), 10);
	}

	[Fact]
	public void SlopeAngleDeg_SlopeHalfNorm_ReadsAbout26_6_Deg()
	{
		// why the 30deg gate rarely passes on 30s bars: slope must reach ~0.58 x ATR/bar
		Assert.Equal(26.57, Kat34ScalperLogic.SlopeAngleDeg(100.5, 100.0, 1.0), 2);
	}
	#endregion

	#region Alert Signal A1 (fan) — A1Direction
	// Buy fan: 105 > 104 > 103 > 102 > 101 (e8 > e34 > e89 > e144 > e200). Sell fan mirrored.
	private const bool C = true;  // condition enabled

	[Fact]
	public void A1Direction_BuyFan_AndRisingAngle_ReturnsLong()
	{
		Assert.Equal(1, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 102, 101, 35, 30));
		Assert.Equal(1, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 102, 101, 30, 30)); // boundary
	}

	[Fact]
	public void A1Direction_SellFan_AndFallingAngle_ReturnsShort()
	{
		Assert.Equal(-1, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 101, 102, 103, 104, 105, -35, 30));
		Assert.Equal(-1, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 101, 102, 103, 104, 105, -30, 30)); // boundary
	}

	[Fact]
	public void A1Direction_AngleTooShallow_ReturnsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 102, 101, 20, 30));  // long fan, weak slope
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 101, 102, 103, 104, 105, -20, 30)); // short fan, weak slope
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 102, 101, -35, 30)); // long fan but falling
	}

	[Fact]
	public void A1Direction_BrokenFan_ReturnsZero()
	{
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 104, 104, 103, 102, 101, 35, 30)); // ema8 not above ema34
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 104, 102, 101, 35, 30)); // ema34 not above ema89
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 103, 101, 35, 30)); // ema89 not above ema144
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 102, 102, 35, 30)); // ema144 not above ema200
	}

	[Fact]
	public void A1Direction_TransitionalFan_34Below89_BlocksBothSides()
	{
		// chart case user caught: fast EMAs turned up but 34 still below 89 -> no signal either side
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 106, 102, 101, 35, 30)); // 8>34 but 34<89: long blocked
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 101, 102, 100, 104, 105, -35, 30)); // 8<34 but 34>89: short blocked
	}

	[Fact]
	public void A1Direction_ToggleOff_SkipsThatCondition()
	{
		// ema8-below-34 tolerated when its toggle is off
		Assert.Equal(1, Kat34ScalperLogic.A1Direction(false, C, C, C, C, 104, 105, 103, 102, 101, 35, 30));
		// ema34-below-89 tolerated when its toggle is off
		Assert.Equal(1, Kat34ScalperLogic.A1Direction(C, false, C, C, C, 105, 104, 106, 102, 101, 35, 30));
		// angle tolerated when its toggle is off
		Assert.Equal(1, Kat34ScalperLogic.A1Direction(C, C, C, C, false, 105, 104, 103, 102, 101, -10, 30));
		Assert.Equal(-1, Kat34ScalperLogic.A1Direction(C, C, C, false, C, 101, 102, 103, 104, 104, -35, 30)); // 144>200 broken but disabled
	}

	[Fact]
	public void A1Direction_TinySlopeVsNorm_AngleGateBlocks_FanAloneFiresWhenAngleOff()
	{
		// 30s reality: slope ~0.2 of the normalization unit -> ~11 degrees, below 30
		double angle = Kat34ScalperLogic.SlopeAngleDeg(100.2, 100.0, 1.0);
		Assert.True(angle < 30);
		Assert.Equal(0, Kat34ScalperLogic.A1Direction(C, C, C, C, C, 105, 104, 103, 102, 101, angle, 30));  // angle gate ON blocks
		Assert.Equal(1, Kat34ScalperLogic.A1Direction(C, C, C, C, false, 105, 104, 103, 102, 101, angle, 30)); // angle gate OFF -> fan fires
		Assert.Equal(-1, Kat34ScalperLogic.A1Direction(C, C, C, C, false, 101, 102, 103, 104, 105, -angle, 30));
	}
	#endregion

	#region Alert Signal A1 (fan) — A1EdgeStep (break debounce)
	[Fact]
	public void A1EdgeStep_FiresOnNewValidEnvironment()
	{
		bool fired = Kat34ScalperLogic.A1EdgeStep(1, 0, 0, 3, out int lastDir, out int streak);
		Assert.True(fired);
		Assert.Equal(1, lastDir);
		Assert.Equal(0, streak);
	}

	[Fact]
	public void A1EdgeStep_NoRefireWhileEnvironmentHolds()
	{
		bool fired = Kat34ScalperLogic.A1EdgeStep(1, 1, 0, 3, out int lastDir, out _);
		Assert.False(fired);
		Assert.Equal(1, lastDir);
	}

	[Fact]
	public void A1EdgeStep_ShortWobble_DoesNotRearm()
	{
		// armed LONG, 2 invalid bars (< break 3) -> still armed, no re-fire on return
		Kat34ScalperLogic.A1EdgeStep(0, 1, 0, 3, out int lastDir, out int streak);
		Assert.Equal(1, lastDir); Assert.Equal(1, streak);
		Kat34ScalperLogic.A1EdgeStep(0, lastDir, streak, 3, out lastDir, out streak);
		Assert.Equal(1, lastDir); Assert.Equal(2, streak);
		bool fired = Kat34ScalperLogic.A1EdgeStep(1, lastDir, streak, 3, out lastDir, out _);
		Assert.False(fired);
		Assert.Equal(1, lastDir);
	}

	[Fact]
	public void A1EdgeStep_SustainedBreak_RearmsAndRefires()
	{
		int lastDir = 1, streak = 0;
		for (int i = 0; i < 3; i++)
			Kat34ScalperLogic.A1EdgeStep(0, lastDir, streak, 3, out lastDir, out streak);
		Assert.Equal(0, lastDir); // broken
		bool fired = Kat34ScalperLogic.A1EdgeStep(1, lastDir, streak, 3, out _, out _);
		Assert.True(fired);
	}

	[Fact]
	public void A1EdgeStep_DirectionFlip_FiresImmediately()
	{
		bool fired = Kat34ScalperLogic.A1EdgeStep(-1, 1, 0, 3, out int lastDir, out _);
		Assert.True(fired);
		Assert.Equal(-1, lastDir);
	}

	[Fact]
	public void A1EdgeStep_NonPositiveBreakBars_FallsBackToOne()
	{
		Kat34ScalperLogic.A1EdgeStep(0, 1, 0, 0, out int lastDir, out _);
		Assert.Equal(0, lastDir); // one invalid bar already breaks
	}

	[Fact]
	public void A1EdgeStep_FlipDuringDebounceStreak_FiresWithoutFullBreak()
	{
		// armed LONG, 1 invalid bar (< break 3) -> still armed LONG; SHORT arrives mid-streak -> fires immediately
		Kat34ScalperLogic.A1EdgeStep(0, 1, 0, 3, out int lastDir, out int streak);
		Assert.Equal(1, lastDir);
		bool fired = Kat34ScalperLogic.A1EdgeStep(-1, lastDir, streak, 3, out lastDir, out streak);
		Assert.True(fired);
		Assert.Equal(-1, lastDir);
		Assert.Equal(0, streak);
	}
	#endregion

	#region Alert Signal A1 (fan) — A1DebouncedDir (episode invalid decision)
	[Fact]
	public void A1DebouncedDir_WobbleKeepsArmedEnvironment()
	{
		// armed LONG, 2 invalid bars (< break 3) -> episode still LONG
		Assert.Equal(1, Kat34ScalperLogic.A1DebouncedDir(0, 1, 0, 3));
		Assert.Equal(1, Kat34ScalperLogic.A1DebouncedDir(0, 1, 1, 3));
	}

	[Fact]
	public void A1DebouncedDir_FullBreakGoesRanging()
	{
		Assert.Equal(0, Kat34ScalperLogic.A1DebouncedDir(0, 1, 2, 3)); // 3rd consecutive invalid bar
	}

	[Fact]
	public void A1DebouncedDir_FlipPassesThroughImmediately()
	{
		Assert.Equal(-1, Kat34ScalperLogic.A1DebouncedDir(-1, 1, 0, 3));
	}

	[Fact]
	public void A1DebouncedDir_RangingStaysRanging()
	{
		Assert.Equal(0, Kat34ScalperLogic.A1DebouncedDir(0, 0, 5, 3));
	}

	[Fact]
	public void A1DebouncedDir_MatchesEdgeStepDisarmBar()
	{
		// episode end (debounced dir -> 0) lands on the same bar the edge step disarms
		int lastDir = 1, streak = 0;
		int effDir = 1;
		for (int i = 0; i < 3; i++)
		{
			effDir = Kat34ScalperLogic.A1DebouncedDir(0, lastDir, streak, 3);
			Kat34ScalperLogic.A1EdgeStep(0, lastDir, streak, 3, out lastDir, out streak);
		}
		Assert.Equal(0, effDir);
		Assert.Equal(0, lastDir);
	}

	[Fact]
	public void EmaZonePass_MirrorsDirection()
	{
		Assert.True(Kat34ScalperLogic.EmaZonePass(1, 101, 100));   // LONG above EMA34
		Assert.False(Kat34ScalperLogic.EmaZonePass(1, 99, 100));   // LONG below EMA34
		Assert.True(Kat34ScalperLogic.EmaZonePass(-1, 99, 100));   // SHORT below EMA34
		Assert.False(Kat34ScalperLogic.EmaZonePass(-1, 101, 100)); // SHORT above EMA34
		Assert.True(Kat34ScalperLogic.EmaZonePass(0, 99, 100));     // neutral passes
	}
	#endregion

	#region Alert sound resolution
	private static string NewTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kat34snd_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	[Fact]
	public void ResolvePath_UserFolderWinsOverInstallFolder()
	{
		string user = NewTempDir(), install = NewTempDir();
		try
		{
			File.WriteAllBytes(Path.Combine(user, "My.wav"), new byte[1]);
			File.WriteAllBytes(Path.Combine(install, "My.wav"), new byte[1]);
			File.WriteAllBytes(Path.Combine(install, "Alert1.wav"), new byte[1]);
			Assert.Equal(Path.Combine(user, "My.wav"), Kat34ScalperSound.ResolvePath(user, install, "My.wav"));
			Assert.Equal(Path.Combine(install, "Alert1.wav"), Kat34ScalperSound.ResolvePath(user, install, "Alert1.wav"));
			Assert.Null(Kat34ScalperSound.ResolvePath(user, install, "Nope.wav"));
			Assert.Null(Kat34ScalperSound.ResolvePath(user, install, ""));
		}
		finally { Directory.Delete(user, true); Directory.Delete(install, true); }
	}

	[Fact]
	public void ListSounds_MergesBothFoldersDedupesAndSorts()
	{
		string user = NewTempDir(), install = NewTempDir();
		try
		{
			File.WriteAllBytes(Path.Combine(user, "zeta.wav"), new byte[1]);
			File.WriteAllBytes(Path.Combine(user, "My.wav"), new byte[1]);
			File.WriteAllBytes(Path.Combine(install, "My.wav"), new byte[1]);
			File.WriteAllBytes(Path.Combine(install, "Alert1.wav"), new byte[1]);
			File.WriteAllBytes(Path.Combine(install, "readme.txt"), new byte[1]);
			var list = Kat34ScalperSound.ListSounds(user, install);
			Assert.Equal(new[] { "Alert1.wav", "My.wav", "zeta.wav" }, list);
		}
		finally { Directory.Delete(user, true); Directory.Delete(install, true); }
	}
	#endregion

	#region Audit-fix coverage — edge, boundary, regression (v0.96)
	[Fact]
	public void StackEmaDirection_PriceEqualsEma_NeutralZero()
	{
		// price exactly on EMA => strictly-above/below fails => Neutral
		Assert.Equal(0, StackEmaLogic.Direction(100, 100, 99, 98, 97, 96));
		Assert.Equal(0, StackEmaLogic.Direction(100, 99, 100, 98, 97, 96)); // one EMA equals price => not strictly above
		Assert.Equal(-1, StackEmaLogic.Direction(90, 91, 92, 93, 94, 95)); // strictly below => -1
	}

	[Fact]
	public void StackEmaFilter_Boundary_DirectionsCovered()
	{
		// enabled packs only; disabled packs ignored even if negative
		Assert.True(StackEmaLogic.FilterPass(true, true, new[] { 1, -1 }, new[] { true, false }));
		Assert.False(StackEmaLogic.FilterPass(true, true, new[] { 1, -1 }, new[] { true, true })); // second pack -1 breaks buy
		Assert.True(StackEmaLogic.FilterPass(true, false, new[] { -1, -1 }, new[] { true, true }));
	}

	[Fact]
	public void BarsAgoAtOrBefore_SingleElement_AndZeroMax()
	{
		var times = new[] { T(10, 0) };
		// maxBarsAgo <1 => contract returns -1 regardless of time
		Assert.Equal(-1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times[i], 0, T(10, 0)));
		Assert.Equal(-1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times[i], 0, T(9, 0)));
		// single element valid max = 0 -> use separate array with 2 elements for max=1 case
		var times2 = new[] { T(10, 0), T(9, 0) };
		Assert.Equal(0, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times2[i], 1, T(10, 0)));
		Assert.Equal(1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times2[i], 1, T(9, 0)));
		Assert.Equal(-1, Kat34ScalperLogic.BarsAgoAtOrBefore(i => times2[i], 1, T(8, 0)));
	}

	[Fact]
	public void ClosedBarCutoff_TickTarget_ConservativeCutoff()
	{
		var open = new DateTime(2026, 8, 4, 10, 0, 0);
		// Kat34ScalperLogic: source 30s, target 0 (non-time) => cutoff = open +30s (source close)
		// StackEmaLogic same formula; tick target 0 means add full source period
		Assert.Equal(open.AddSeconds(30), Kat34ScalperLogic.ClosedBarCutoff(open, 30, 0));
		Assert.Equal(open.AddSeconds(30), StackEmaLogic.ClosedBarCutoff(open, 30, 0));
	}

	[Fact]
	public void GetNySessionStartUtc_BeforeAndAfter18Ny()
	{
		// Find NY zone (Windows vs Linux)
		TimeZoneInfo ny;
		try { ny = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
		catch { ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
		// Pick a summer day (EDT UTC-4): 2026-07-01 16:00 UTC = 12:00 NY (<18) => session prev day 18:00 NY
		var utcBefore = new DateTime(2026, 7, 1, 16, 0, 0, DateTimeKind.Utc);
		var sessBefore = Kat34ScalperLogic.GetNySessionStartUtc(utcBefore);
		var sessBeforeNy = TimeZoneInfo.ConvertTimeFromUtc(sessBefore, ny);
		Assert.Equal(new TimeSpan(18, 0, 0), sessBeforeNy.TimeOfDay);
		Assert.Equal(new DateTime(2026, 6, 30), sessBeforeNy.Date);
		// After 18 NY: 2026-07-01 23:00 UTC = 19:00 NY (>18) => session same day 18:00 NY
		var utcAfter = new DateTime(2026, 7, 1, 23, 0, 0, DateTimeKind.Utc);
		var sessAfter = Kat34ScalperLogic.GetNySessionStartUtc(utcAfter);
		var sessAfterNy = TimeZoneInfo.ConvertTimeFromUtc(sessAfter, ny);
		Assert.Equal(new TimeSpan(18, 0, 0), sessAfterNy.TimeOfDay);
		Assert.Equal(new DateTime(2026, 7, 1), sessAfterNy.Date);
	}

	[Fact]
	public void CalculateBreakevenPrice_NegativeBufferAndZeroTick_Edge()
	{
		Assert.Equal(5000.0, Kat34ScalperLogic.CalculateBreakevenPrice(true, 5000.0, -5, 0.25), 4); // negative clamped 0
		Assert.Equal(5000.0, Kat34ScalperLogic.CalculateBreakevenPrice(true, 5000.0, 2, 0), 4); // zero tick => entry
		Assert.Equal(5000.0, Kat34ScalperLogic.CalculateBreakevenPrice(true, 5000.0, 2, -1), 4); // negative tick => entry
	}

	[Fact]
	public void EvaluateDailyRiskBreach_ZeroLimit_NeverBreach()
	{
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(true, 0, true, 0, -100000, out _));
		Assert.False(Kat34ScalperLogic.EvaluateDailyRiskBreach(true, 0, false, 1000, -100000, out _));
	}

	[Fact]
	public void ShouldCaptureSessionBaseline_SessionAdvance_AndStaleRead()
	{
		var s1 = new DateTime(2026, 7, 30, 22, 0, 0, DateTimeKind.Utc);
		var s2 = s1.AddDays(1);
		// first capture ok
		Assert.True(Kat34ScalperLogic.ShouldCaptureSessionBaseline(false, s1, DateTime.MinValue, true));
		// same session, already captured => false
		Assert.False(Kat34ScalperLogic.ShouldCaptureSessionBaseline(true, s1, s1, true));
		// session advanced => true
		Assert.True(Kat34ScalperLogic.ShouldCaptureSessionBaseline(true, s2, s1, true));
		// read failed => never capture
		Assert.False(Kat34ScalperLogic.ShouldCaptureSessionBaseline(false, s2, s1, false));
	}

	[Fact]
	public void NormalizeAtmSetName_UnicodeAndTrim_Edge()
	{
		Assert.Equal("€AB", Kat34ScalperLogic.NormalizeAtmSetName(" €AB ", "X"));
		Assert.Equal("AB", Kat34ScalperLogic.NormalizeAtmSetName(" AB\n", "X"));
	}

	[Fact]
	public void IsStopOnValidSide_Equality_False()
	{
		Assert.False(Kat34ScalperLogic.IsStopOnValidSide(true, 100.0, 100.0));
		Assert.False(Kat34ScalperLogic.IsStopOnValidSide(false, 100.0, 100.0));
	}

	[Fact]
	public void ShouldCancelFlatOrphans_PendingEntryBlocks()
	{
		Assert.False(Kat34ScalperLogic.ShouldCancelFlatOrphans(true, true, true));
		Assert.True(Kat34ScalperLogic.ShouldCancelFlatOrphans(true, true, false));
	}

	[Fact]
	public void A2_Buy_ExactLowOnEma34_TouchCounts()
	{
		var s = new KatA2State();
		// low exactly == ema34 with close above => touch valid
		Assert.Equal(KatA2Action.NewEntry, Kat34ScalperLogic.UpdateA2(KatSignalKind.Buy, true, 101.0, 100.5, 100.8, 100.5, 1, 0.25, s));
	}
	#endregion
}
