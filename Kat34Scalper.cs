/*
 * Kat34Scalper.cs — main module (lifecycle, settings, orchestration)
 * Version: 1.00 (2026-08-08)
 * NinjaTrader 8 — EMA 34/89 rejection signal indicator (Sell / Buy).
 *
 * Co-Authored-By: Oz <oz-agent@warp.dev>
 *
 * Module layout (partial classes):
 *   Kat34Scalper.cs                    — main: state, OnStateChange, OnBarUpdate orchestration, settings
 *   src/Kat34ScalperLogic.cs           — pure signal/filter math + ATM parser (zero NT8 deps, xunit-tested)
 *   src/Kat34Scalper.AlertSignal.cs    — Alert Signal module shared helpers (alert backfill)
 *   ..\nt8-kat-A1-TradeBackground\Kat34Scalper.AlertSignal.A1.cs — KAT A1 standalone indicator (independent sibling repo; host connects via its signal)
 *   src/Kat34Scalper.AlertSignal.A2.cs — Alert Signal sub-module A2: placeholder (independent, alert-only)
 *   src/Kat34Scalper.Signal.cs         — Bot Signal module shared helpers (backfill window)
 *   src/Kat34Scalper.Signal.B1.cs      — Bot Signal sub-module B1: 34bounce8+ (34+8+Bounce ema34-touch pending entry)
 *   src/Kat34Scalper.Signal.B2.cs      — Bot Signal sub-module B2: 89uturn34 (89-34 pullback setup)
 *   src/Kat34Scalper.Filter.cs         — Filter module: ADX rising/ADX MTF/ER/CI/Volume/Time/StackEMA gates
 *   src/Kat34Scalper.StackEMA.cs       — StackEMA filter adapter (mapped secondary series, shared pure logic)
 *   ..\nt8-kat-StackEMA\nt8-kat-StackEMA.cs + StackEmaLogic.cs — standalone StackEMA indicator (independent sibling repo)
 *   src/Kat34Scalper.Bot.cs            — Bot module: order ops, stop/limit, migration, Close/Flatten
 *   src/Kat34Scalper.Bot.Risk.cs       — Bot.Risk: Daily MaxDD/MaxProfit NY session baseline + breach gate
 *   src/Kat34Scalper.Bot.AtmMerge.cs   — Bot.AtmMerge: ATM bracket MERGE reconciliation (anchor resize, duplicate/stale cancel)
 *   src/Kat34Scalper.Draw.cs           — Draw module: lines + ATM triggers + HUD assembly
 *   src/Kat34Scalper.Draw.HudFactory.cs — Draw.HudFactory: pixel-perfect tokens + factory (buttons, grids, cards, templates)
 *   src/Kat34Scalper.AccountInfo.cs     — Draw.AccountInfo: top black board (NYT time, acct/balance/Day/U/R, BOT/A2/B1/B2/POS)
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
using KatStackEMA;
#endregion

// Dropdown of the ATM strategy templates in NT8's templates\AtmStrategy folder (+ "None" = bare order).
public class Kat34ScalperAtmTemplateConverter : TypeConverter
{
	public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
	{
		var list = new List<string> { "None" };
		try
		{
			string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");
			if (Directory.Exists(dir))
			{
				var names = new List<string>();
				foreach (string f in Directory.GetFiles(dir, "*.xml"))
					names.Add(Path.GetFileNameWithoutExtension(f));
				names.Sort(StringComparer.OrdinalIgnoreCase); // filesystem order is not deterministic
				list.AddRange(names);
			}
		}
		catch { }
		return new StandardValuesCollection(list);
	}
}

// Dropdown of the .wav files in NT8's user sounds folder (Documents\NinjaTrader 8\sounds)
// plus the install sounds folder (for the Alert Sound setting). User files win on equal names.
public class Kat34ScalperSoundConverter : TypeConverter
{
	public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
	public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
	public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
	{
		var list = new List<string>();
		try
		{
			string userDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds");
			Directory.CreateDirectory(userDir); // so users can find the folder to drop custom .wav files into
			string installDir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
			list.AddRange(Kat34ScalperSound.ListSounds(userDir, installDir));
		}
		catch { }
		return new StandardValuesCollection(list);
	}
}

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper : Indicator
	{
		#region Shared State (owned by main; module-specific state lives in its own file)
		public const string VERSION = "1.00";
		public const string RELEASE_DATE = "2026-08-08";


		// Indicator series (primary chart TF)
		private EMA fastEma;
		private EMA slowEma;
		private EMA ema8;
		private EMA ema144;
		private EMA ema200;
		private ADX adxInd;
		private ADX adxMtfInd; // Bot ADX MTF regime gate on the dedicated MTF series (BarsArray[1])
		private SMA volSmaInd;

		// Time-window filter parsed values
		private TimeSpan timeStart;
		private TimeSpan timeEnd;
		private bool timeWindowDisabled;
		#endregion

		#region Indicator Lifecycle
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Kat34Scalper v" + VERSION + @" — EMA 34/89 rejection signals (Sell/Buy) with Alert Signals and Bot Signals.";
				Name						= "Kat34Scalper";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				PaintPriceMarkers			= true;
				IsSuspendedWhileInactive	= true;

				// 1. Filters defaults — every gate OFF; toggles boot OFF on every load (session-only)
				AdxPeriod					= 60; // 60x30s = 30-min regime window; 14 (7 min) whipsaws on 30s
				AdxRisingBars				= 5;
				AdxMtfMinutes				= 3;
				AdxMtfPeriod				= 14;
				AdxMtfMin					= 22;
				StackEmaFilterEnabled		= false;
				StackEmaEMA8				= 8;
				StackEmaEMA21				= 21;
				StackEmaEMA34				= 34;
				StackEmaEMA55				= 55;
				StackEmaEMA89				= 89;
				StackEmaTimeframe1			= StackEmaTimeframe.S30;
				StackEmaTimeframe2			= StackEmaTimeframe.M1;
				StackEmaTimeframe3			= StackEmaTimeframe.M3;
				StackEmaTimeframe4			= StackEmaTimeframe.M5;
				StackEmaTimeframe5			= StackEmaTimeframe.M15;
				StackEmaStack1Visible		= true;
				StackEmaStack2Visible		= true;
				StackEmaStack3Visible		= true;
				StackEmaStack4Visible		= true;
				StackEmaStack5Visible		= true;
				ErPeriod					= 40;
				ErMin						= 0.25;
				CiPeriod					= 40;
				CiMax						= 50;
				VolumeSmaPeriod				= 20;
				VolumeMinMult				= 1.0;
				TimeFilterStart				= "08:00";
				TimeFilterEnd				= "17:00";
				AlertSound					= "Alert1.wav";

				// 2.5 Alert Signal A2 defaults — OFF
				AlertA2Enabled				= false;
				AlertA2HistoryDays			= 3;

				// 3. Bot Signal B1 (34bounce8+) defaults — OFF
				B1Enabled					= false;
				B1HistoryDays				= 3;
				B1CondEma8Above34			= true;
				B1CondEma34Above89			= true;
				B1CondEma89Above144			= true;
				B1CondEma144Above200		= true;
				B1EntryOffsetTicks			= 1;
				B1StopDistanceTicks			= 60;
				B1TargetDistanceTicks		= 120;

				// 3.5 Bot Signal B2 (89uturn34) defaults — OFF
				B2Enabled					= false;
				B2HistoryDays				= 3;
				EmaFastPeriod				= 34;
				EmaSlowPeriod				= 89;
				MaxSequenceBars				= 30;
				B2EntryOffsetTicks			= 1;
				B2StopDistanceTicks			= 60;
				B2TargetDistanceTicks		= 120;

				// 5. Bot defaults
				BotEnabled					= false;
				BotOrderQuantity			= 1;
				BotAtmTemplate				= "mnq. 1ct. 15-be20-35move15-50triggertrail5step1";
				BotAccountName				= "Sim101";
				BotBufferTicks				= 2;

				DailyMaxDDEnabled			= false;
				DailyMaxDD					= 500;
				DailyMaxProfitEnabled		= false;
				DailyMaxProfit				= 1000;

				// 6. ATM Quick Sets defaults — labels A–F, no ATM assigned
				AtmSet1Name					= "A";
				AtmSet1Atm					= "";
				AtmSet2Name					= "B";
				AtmSet2Atm					= "";
				AtmSet3Name					= "C";
				AtmSet3Atm					= "";
				AtmSet4Name					= "D";
				AtmSet4Atm					= "";
				AtmSet5Name					= "E";
				AtmSet5Atm					= "";
				AtmSet6Name					= "F";
				AtmSet6Atm					= "";

				// HUD Quick Set Style defaults
				QuickSetFontSize				= 8;
				QuickSetLabelColor			= new SolidColorBrush(Color.FromRgb(255, 255, 255));
				QuickSetLabelOpacityPercent	= 50;
				ProgramLabelColor				= new SolidColorBrush(Color.FromRgb(255, 255, 255));
				ProgramLabelOpacityPercent	= 20;

				// 7. Trading Profiles (Program Quick Sets) defaults — 8 presets covering whole account
				for (int _pi = 0; _pi < 8; _pi++) InitTradingProfileDefaults(_pi);

				// 8. Daily Risk Quick Sets defaults
				DailyRiskSet1Name				= "1"; DailyRiskSet1MaxDD = 200; DailyRiskSet1MaxProfit = 500;
				DailyRiskSet2Name				= "2"; DailyRiskSet2MaxDD = 100; DailyRiskSet2MaxProfit = 300;
				DailyRiskSet3Name				= "3"; DailyRiskSet3MaxDD = 500; DailyRiskSet3MaxProfit = 1000;
				DailyRiskSet4Name				= "4"; DailyRiskSet4MaxDD = 1000; DailyRiskSet4MaxProfit = 2000;
				DailyRiskSet5Name				= "5"; DailyRiskSet5MaxDD = 1500; DailyRiskSet5MaxProfit = 3000;
				DailyRiskSet6Name				= "6"; DailyRiskSet6MaxDD = 2000; DailyRiskSet6MaxProfit = 5000;

				// 4. Lines & Text defaults
				LineLengthBars				= 7;
				LineWidth					= 2;
				ArrowOffsetTicks			= 3;
				SellEntryLineColor			= Colors.Red;
				BuyEntryLineColor			= Colors.LimeGreen;
				SLLineColor					= Colors.Red;
				TPLineColor					= Colors.Green;
				SellTextColor				= Colors.Red;
				BuyTextColor				= Colors.LimeGreen;
			}
			else if (State == State.Configure)
			{
				// ADX MTF regime timeframe (Bot ADX MTF gate) — always added so BarsArray indexes stay stable. Series 1 = BarsArray[1].
				AddDataSeries(Data.BarsPeriodType.Minute, Math.Max(1, AdxMtfMinutes));
				// StackEMA reuses A1/ADX/zone series when periods match; it adds only unique series.
				if (StackEmaFilterEnabled && HasVisibleStackEma()) ConfigureStackEma();
			}
			else if (State == State.DataLoaded)
			{
				fastEma = EMA(BarsArray[0], EmaFastPeriod);
				slowEma = EMA(BarsArray[0], EmaSlowPeriod);
				ema8 = EMA(BarsArray[0], 8);
				ema144 = EMA(BarsArray[0], 144);
				ema200 = EMA(BarsArray[0], 200);
				adxInd = ADX(BarsArray[0], AdxPeriod);
				volSmaInd = SMA(Volumes[0], VolumeSmaPeriod);

				adxMtfInd = ADX(BarsArray[1], Math.Max(1, AdxMtfPeriod));
				if (StackEmaFilterEnabled && HasVisibleStackEma()) LoadStackEma();

				timeWindowDisabled = string.Equals(TimeFilterStart, TimeFilterEnd, StringComparison.OrdinalIgnoreCase);
				if (!timeWindowDisabled)
				{
					TimeSpan.TryParse(TimeFilterStart, out timeStart);
					TimeSpan.TryParse(TimeFilterEnd, out timeEnd);
				}

				cachedAlertA2 = AlertA2Enabled;
				alertA2BackfillPending = AlertA2Enabled;

				cachedB1 = B1Enabled;
				cachedB2 = B2Enabled;
				b1BackfillPending = B1Enabled;
				b2BackfillPending = B2Enabled;

				cachedBotAtm = BotAtmTemplate ?? "";
				cachedBotAccountName = BotAccountName ?? "";
				cachedBotOn = BotEnabled;
				cachedBotBufferTicks = BotBufferTicks;
				cachedIsDailyMaxDD = DailyMaxDDEnabled;
				cachedDailyMaxDD = DailyMaxDD;
				cachedIsDailyMaxProfit = DailyMaxProfitEnabled;
				cachedDailyMaxProfit = DailyMaxProfit;

				// HUD style migration: ensure defaults for charts saved before this version
				if (QuickSetFontSize < 6 || QuickSetFontSize > 14) QuickSetFontSize = 8;
				if (QuickSetLabelColor == null) QuickSetLabelColor = new SolidColorBrush(Color.FromRgb(255, 255, 255));
				if (QuickSetLabelOpacityPercent < 10 || QuickSetLabelOpacityPercent > 100) QuickSetLabelOpacityPercent = 50;
				if (ProgramLabelColor == null) ProgramLabelColor = new SolidColorBrush(Color.FromRgb(255, 255, 255));
				if (ProgramLabelOpacityPercent < 10 || ProgramLabelOpacityPercent > 100) ProgramLabelOpacityPercent = 20;
				// Migration: pre-version profiles have quantity 0 -> seed defaults per-profile
				for (int _pi = 0; _pi < 8; _pi++)
				{
					int beforeQty = 0;
					switch (_pi) { case 0: beforeQty=TradingProfile1Quantity; break; case 1: beforeQty=TradingProfile2Quantity; break; case 2: beforeQty=TradingProfile3Quantity; break; case 3: beforeQty=TradingProfile4Quantity; break; case 4: beforeQty=TradingProfile5Quantity; break; case 5: beforeQty=TradingProfile6Quantity; break; case 6: beforeQty=TradingProfile7Quantity; break; default: beforeQty=TradingProfile8Quantity; break; }
					if (beforeQty==0) SeedTradingProfileDefaults(_pi);
				}

				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(BuildHud);
			}
			else if (State == State.Terminated)
			{
				pendingMigrate = false;
				CancelPendingBotOrder("indicator terminated");
				if (ChartControl != null)
					ChartControl.Dispatcher.InvokeAsync(RemoveHud);
			}
		}
		#endregion

		#region Orchestration (module pipeline per bar)
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0 || CurrentBars[0] < 1) return;

			ClearLegacySignalDrawings();
			RefreshSignalDrawings();

			// Backfill once per enable, at the last available bar (end of history or live bar).
			if (State == State.Realtime || CurrentBars[0] >= BarsArray[0].Count - 1)
			{
				FlushAlertBackfill();
				FlushBackfill();
			}
			if (State != State.Realtime) return;

			double high = Highs[0][0];
			double low = Lows[0][0];
			double close = Closes[0][0];

			bool sellAllowed, buyAllowed;
			PassFilters(out sellAllowed, out buyAllowed);              // Filter module (ADX rising, ADX MTF, ER, CI, Volume, Time)
			EvaluateAlertA2(high, low, close, sellAllowed, buyAllowed); // Alert Signal sub-module A2 (placeholder)
			EvaluateB1(high, low, close, sellAllowed, buyAllowed);      // Bot Signal sub-module B1 (34bounce8+)
			EvaluateB2(high, low, close, sellAllowed, buyAllowed);      // Bot Signal sub-module B2 (89uturn34)
			ManageBotEntry(high, low, close);                           // Bot module (pending entry lifecycle)
		}
		#endregion

		#region NinjaScript Properties
		// --- 1. Filters (market, time) ---
		[NinjaScriptProperty]
		[Display(Name = "ADX Period", Order = 7, GroupName = "1. Filters")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume SMA Period", Order = 9, GroupName = "1. Filters")]
		public int VolumeSmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Volume Min (x SMA)", Order = 10, GroupName = "1. Filters",
			Description = "Bar volume must be at least this multiple of its SMA — blocks dead bars.")]
		public double VolumeMinMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Time Start (HH:mm, machine local)", Order = 11, GroupName = "1. Filters",
			Description = "Trading window start. Equal start/end disables the window.")]
		public string TimeFilterStart { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Time End (HH:mm, machine local)", Order = 12, GroupName = "1. Filters",
			Description = "Trading window end. Overnight windows (start > end) wrap midnight.")]
		public string TimeFilterEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Alert Sound", Order = 13, GroupName = "1. Filters",
			Description = "Sound played on signals.")]
		[TypeConverter(typeof(Kat34ScalperSoundConverter))]
		public string AlertSound { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX Rising Lookback (bars)", Order = 15, GroupName = "1. Filters")]
		public int AdxRisingBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ER Period (bars)", Order = 16, GroupName = "1. Filters",
			Description = "Kaufman Efficiency Ratio window on the chart timeframe.")]
		public int ErPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ER Min", Order = 17, GroupName = "1. Filters",
			Description = "Minimum Efficiency Ratio (0..1) — blocks choppy windows. ~0.25+ suits 30s.")]
		public double ErMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CI Period (bars)", Order = 18, GroupName = "1. Filters",
			Description = "Choppiness Index window on the chart timeframe.")]
		public int CiPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CI Max", Order = 19, GroupName = "1. Filters",
			Description = "Maximum Choppiness Index — blocks ranging windows (>61.8 = chop, <38.2 = trend).")]
		public double CiMax { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX MTF Timeframe (minutes)", Order = 20, GroupName = "1. Filters",
			Description = "Regime ADX timeframe (dedicated secondary series) — moved from Alert A1 to Bot in v0.79.")]
		public int AdxMtfMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX MTF Period", Order = 21, GroupName = "1. Filters")]
		public int AdxMtfPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ADX MTF Min", Order = 22, GroupName = "1. Filters",
			Description = "Minimum ADX on the MTF timeframe — blocks weak-regime bars.")]
		public double AdxMtfMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Filter Enabled", Order = 23, GroupName = "1. Filters",
			Description = "Buy requires every visible pack Positive; Sell requires every visible pack Negative. No visible packs bypass.")]
		public bool StackEmaFilterEnabled { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "StackEMA EMA 8", Order = 24, GroupName = "1. Filters")]
		public int StackEmaEMA8 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "StackEMA EMA 21", Order = 25, GroupName = "1. Filters")]
		public int StackEmaEMA21 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "StackEMA EMA 34", Order = 26, GroupName = "1. Filters")]
		public int StackEmaEMA34 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "StackEMA EMA 55", Order = 27, GroupName = "1. Filters")]
		public int StackEmaEMA55 { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "StackEMA EMA 89", Order = 28, GroupName = "1. Filters")]
		public int StackEmaEMA89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 1 Timeframe", Order = 29, GroupName = "1. Filters")]
		public StackEmaTimeframe StackEmaTimeframe1 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 2 Timeframe", Order = 30, GroupName = "1. Filters")]
		public StackEmaTimeframe StackEmaTimeframe2 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 3 Timeframe", Order = 31, GroupName = "1. Filters")]
		public StackEmaTimeframe StackEmaTimeframe3 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 4 Timeframe", Order = 32, GroupName = "1. Filters")]
		public StackEmaTimeframe StackEmaTimeframe4 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 5 Timeframe", Order = 33, GroupName = "1. Filters")]
		public StackEmaTimeframe StackEmaTimeframe5 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 1 Visible", Order = 34, GroupName = "1. Filters")]
		public bool StackEmaStack1Visible { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 2 Visible", Order = 35, GroupName = "1. Filters")]
		public bool StackEmaStack2Visible { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 3 Visible", Order = 36, GroupName = "1. Filters")]
		public bool StackEmaStack3Visible { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 4 Visible", Order = 37, GroupName = "1. Filters")]
		public bool StackEmaStack4Visible { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "StackEMA Pack 5 Visible", Order = 38, GroupName = "1. Filters")]
		public bool StackEmaStack5Visible { get; set; }

		// --- 2.5 Alert Signal A2 (Placeholder sub-module) ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "2.5 Alert Signal A2",
			Description = "Default OFF. Alert Signal A2 generates sound alerts and chart drawings only.")]
		public bool AlertA2Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "2.5 Alert Signal A2",
			Description = "How many days back Alert A2 signals are replayed and drawn.")]
		public int AlertA2HistoryDays { get; set; }

		// --- 3. Bot Signal B1 — 34bounce8+ ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Default OFF. When switched ON the B1 pending entries (ema34 bounce) are computed and executed by Bot if Bot is ON.")]
		public bool B1Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "How many days back the B1 setups are computed and drawn when B1 is switched ON.")]
		public int B1HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 8 above EMA 34", Order = 3, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 8 stays above (or touches) EMA 34 — never crosses down. SELL mirrored.")]
		public bool B1CondEma8Above34 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 34 above EMA 89", Order = 4, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 34 above EMA 89. SELL mirrored.")]
		public bool B1CondEma34Above89 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 89 above EMA 144", Order = 5, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 89 above EMA 144. SELL mirrored.")]
		public bool B1CondEma89Above144 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cond: EMA 144 above EMA 200", Order = 6, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "BUY: EMA 144 above EMA 200. SELL mirrored.")]
		public bool B1CondEma144Above200 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 7, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Buy entry above the touch candle's high / Sell entry below its low.")]
		public int B1EntryOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 8, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Fallback when the selected ATM template defines no StopLoss.")]
		public int B1StopDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 9, GroupName = "3. Bot Signal B1 — 34bounce8+",
			Description = "Fallback when the selected ATM template defines no Target.")]
		public int B1TargetDistanceTicks { get; set; }

		// --- 3.5 Bot Signal B2 — 89uturn34 ---
		[NinjaScriptProperty]
		[Display(Name = "Enabled", Order = 1, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Default OFF. When switched ON the B2 signals are computed, drawn, and executed by Bot if Bot is ON.")]
		public bool B2Enabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "History Days", Order = 2, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "How many days back the B2 signals are computed and drawn when B2 is switched ON.")]
		public int B2HistoryDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fast EMA Period", Order = 3, GroupName = "3.5 Bot Signal B2 — 89uturn34")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Slow EMA Period", Order = 4, GroupName = "3.5 Bot Signal B2 — 89uturn34")]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max Sequence Bars", Order = 5, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "The whole sequence — pullback cross through the fast EMA, slow-EMA touch, U-turn close back through the fast EMA — must complete within this many bars, otherwise the setup expires.")]
		public int MaxSequenceBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Entry Offset (ticks)", Order = 7, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Sell entry below the signal low / Buy entry above the signal high.")]
		public int B2EntryOffsetTicks { get; set; }
		[Browsable(false)]
		public int EntryOffsetTicks { get { return B2EntryOffsetTicks; } set { B2EntryOffsetTicks = value; } }

		[NinjaScriptProperty]
		[Display(Name = "Stop Distance (ticks)", Order = 8, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Fallback when the selected ATM template defines no StopLoss.")]
		public int B2StopDistanceTicks { get; set; }
		[Browsable(false)]
		public int StopDistanceTicks { get { return B2StopDistanceTicks; } set { B2StopDistanceTicks = value; } }

		[NinjaScriptProperty]
		[Display(Name = "Target Distance (ticks)", Order = 9, GroupName = "3.5 Bot Signal B2 — 89uturn34",
			Description = "Fallback when the selected ATM template defines no Target.")]
		public int B2TargetDistanceTicks { get; set; }
		[Browsable(false)]
		public int TargetDistanceTicks { get { return B2TargetDistanceTicks; } set { B2TargetDistanceTicks = value; } }


		// --- 5. Bot (semi-auto — trades only while the HUD BOT button is ON) ---
		[NinjaScriptProperty]
		[Display(Name = "Bot Enabled", Order = 1, GroupName = "5. Bot",
			Description = "Master switch. The bot still trades only while the HUD BOT button is ON.")]
		public bool BotEnabled { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Order Quantity", Order = 2, GroupName = "5. Bot")]
		public int BotOrderQuantity
		{
			get { return botOrderQuantity; }
			set { botOrderQuantity = Math.Max(1, value); } // CreateOrder fails on 0/negative
		}
		private int botOrderQuantity;

		[NinjaScriptProperty]
		[Display(Name = "ATM Template", Order = 3, GroupName = "5. Bot",
			Description = "ATM strategy managing the entry (brackets). 'None' submits a bare stop order. Default: mnq 1ct bracket.")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string BotAtmTemplate { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Account Name", Order = 4, GroupName = "5. Bot",
			Description = "Account the bot trades on (also selectable on the HUD). Default: Sim101.")]
		public string BotAccountName { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Buffer Ticks", Order = 5, GroupName = "5. Bot", Description = "Buffer ticks for Breakeven (BE) stop loss offset.")]
		public int BotBufferTicks
		{
			get { return botBufferTicks; }
			set { botBufferTicks = Math.Max(0, value); cachedBotBufferTicks = botBufferTicks; }
		}
		private int botBufferTicks = 2;

		[NinjaScriptProperty]
		[Display(Name = "Daily Max DD Enabled", Order = 5, GroupName = "5. Bot", Description = "Enable Daily Max Drawdown limit protection.")]
		public bool DailyMaxDDEnabled
		{
			get { return dailyMaxDDEnabled; }
			set { dailyMaxDDEnabled = value; cachedIsDailyMaxDD = value; }
		}
		private bool dailyMaxDDEnabled;

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Daily Max DD ($)", Order = 6, GroupName = "5. Bot", Description = "Max daily drawdown limit in dollars (e.g. 500 for $500 max loss limit).")]
		public double DailyMaxDD
		{
			get { return dailyMaxDD; }
			set { dailyMaxDD = Math.Max(0, value); cachedDailyMaxDD = dailyMaxDD; }
		}
		private double dailyMaxDD;

		[NinjaScriptProperty]
		[Display(Name = "Daily Max Profit Enabled", Order = 7, GroupName = "5. Bot", Description = "Enable Daily Max Profit limit protection.")]
		public bool DailyMaxProfitEnabled
		{
			get { return dailyMaxProfitEnabled; }
			set { dailyMaxProfitEnabled = value; cachedIsDailyMaxProfit = value; }
		}
		private bool dailyMaxProfitEnabled;

		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Daily Max Profit ($)", Order = 8, GroupName = "5. Bot", Description = "Max daily profit limit in dollars (e.g. 1000 for $1000 max profit limit).")]
		public double DailyMaxProfit
		{
			get { return dailyMaxProfit; }
			set { dailyMaxProfit = Math.Max(0, value); cachedDailyMaxProfit = dailyMaxProfit; }
		}
		private double dailyMaxProfit;

		// --- 6. ATM Quick Sets (HUD: 6 buttons under the ATM dropdown; click selects the assigned ATM) ---
		private string atmSet1Name = "A";
		private string atmSet2Name = "B";
		private string atmSet3Name = "C";
		private string atmSet4Name = "D";
		private string atmSet5Name = "E";
		private string atmSet6Name = "F";

		[NinjaScriptProperty]
		[Display(Name = "Set 1 Name", Order = 1, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet1Name
		{
			get { return atmSet1Name; }
			set { atmSet1Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "A"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 1 ATM", Order = 2, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet1Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 2 Name", Order = 3, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet2Name
		{
			get { return atmSet2Name; }
			set { atmSet2Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "B"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 2 ATM", Order = 4, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet2Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 3 Name", Order = 5, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet3Name
		{
			get { return atmSet3Name; }
			set { atmSet3Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "C"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 3 ATM", Order = 6, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet3Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 4 Name", Order = 7, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet4Name
		{
			get { return atmSet4Name; }
			set { atmSet4Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "D"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 4 ATM", Order = 8, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet4Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 5 Name", Order = 9, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet5Name
		{
			get { return atmSet5Name; }
			set { atmSet5Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "E"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 5 ATM", Order = 10, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet5Atm { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 6 Name", Order = 11, GroupName = "6. ATM Quick Sets", Description = "Button label (max 3 chars)")]
		public string AtmSet6Name
		{
			get { return atmSet6Name; }
			set { atmSet6Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "F"); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Set 6 ATM", Order = 12, GroupName = "6. ATM Quick Sets")]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		public string AtmSet6Atm { get; set; }

		// --- HUD Quick Set Style ---
		[NinjaScriptProperty]
		[Range(6, 12)]
		[Display(Name = "Quick Set Font Size", Order = 1, GroupName = "HUD", Description = "Font size for quick-set/program preset buttons only (smaller = more space for custom labels).")]
		public double QuickSetFontSize { get; set; }

		private Brush quickSetLabelColor = Brushes.White;
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Quick Set Label Color", Order = 2, GroupName = "HUD", Description = "Base label color for quick-set/program buttons (combined with opacity below).")]
		public Brush QuickSetLabelColor
		{
			get { return quickSetLabelColor; }
			set
			{
				try
				{
					if (value == null) { quickSetLabelColor = Brushes.White; return; }
					if (value is SolidColorBrush scb)
					{
						var c = scb.Color;
						var nb = new SolidColorBrush(c);
						if (nb.CanFreeze) nb.Freeze();
						quickSetLabelColor = nb;
					}
					else quickSetLabelColor = value ?? Brushes.White;
				}
				catch { quickSetLabelColor = Brushes.White; }
			}
		}

		[Browsable(false)]
		public string QuickSetLabelColorSerializable
		{
			get
			{
				try
				{
					if (quickSetLabelColor is SolidColorBrush scb)
						return scb.Color.ToString();
					return Colors.White.ToString();
				}
				catch { return Colors.White.ToString(); }
			}
			set
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(value))
					{
						var c = (Color)ColorConverter.ConvertFromString(value);
						var nb = new SolidColorBrush(c);
						if (nb.CanFreeze) nb.Freeze();
						quickSetLabelColor = nb;
					}
					else quickSetLabelColor = Brushes.White;
				}
				catch { quickSetLabelColor = Brushes.White; }
			}
		}

		[NinjaScriptProperty]
		[Range(10, 100)]
		[Display(Name = "Quick Set Label Opacity %", Order = 3, GroupName = "HUD", Description = "Opacity for quick-set/program label text (100=opaque, 50=50% transparent).")]
		public int QuickSetLabelOpacityPercent { get; set; }

		private Brush programLabelColor = Brushes.White;
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Program Label Color", Order = 4, GroupName = "HUD", Description = "Base label color for Program (P1..P8) buttons (combined with opacity below). Default white 80% transparent.")]
		public Brush ProgramLabelColor
		{
			get { return programLabelColor; }
			set
			{
				try
				{
					if (value == null) { programLabelColor = Brushes.White; return; }
					if (value is SolidColorBrush scb)
					{
						var c = scb.Color;
						var nb = new SolidColorBrush(c);
						if (nb.CanFreeze) nb.Freeze();
						programLabelColor = nb;
					}
					else programLabelColor = value ?? Brushes.White;
				}
				catch { programLabelColor = Brushes.White; }
			}
		}

		[Browsable(false)]
		public string ProgramLabelColorSerializable
		{
			get
			{
				try
				{
					if (programLabelColor is SolidColorBrush scb)
						return scb.Color.ToString();
					return Colors.White.ToString();
				}
				catch { return Colors.White.ToString(); }
			}
			set
			{
				try
				{
					if (!string.IsNullOrWhiteSpace(value))
					{
						var c = (Color)ColorConverter.ConvertFromString(value);
						var nb = new SolidColorBrush(c);
						if (nb.CanFreeze) nb.Freeze();
						programLabelColor = nb;
					}
					else programLabelColor = Brushes.White;
				}
				catch { programLabelColor = Brushes.White; }
			}
		}

		[NinjaScriptProperty]
		[Range(10, 100)]
		[Display(Name = "Program Label Opacity %", Order = 5, GroupName = "HUD", Description = "Opacity for Program label text (100=opaque, 20=80% transparent). Default 20.")]
		public int ProgramLabelOpacityPercent { get; set; }

		// --- 7. Trading Profiles (Program Quick Sets — whole account: account/ATM/qty/buffer/daily risk) ---
		private string profile1Name = "P1";
		private string profile2Name = "P2";
		private string profile3Name = "P3";
		private string profile4Name = "P4";
		private string profile5Name = "P5";
		private string profile6Name = "P6";
		private string profile7Name = "P7";
		private string profile8Name = "P8";

		[NinjaScriptProperty]
		[Display(Name = "Profile 1 Name", Order = 1, GroupName = "Trading Profile 1", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile1Name
		{
			get { return profile1Name; }
			set { profile1Name = Kat34ScalperLogic.NormalizeProfileName(value, "P1"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 1 Account", Order = 2, GroupName = "Trading Profile 1")]
		public string TradingProfile1Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 1 ATM", Order = 3, GroupName = "Trading Profile 1")]
		public string TradingProfile1Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 1 Quantity", Order = 4, GroupName = "Trading Profile 1")]
		public int TradingProfile1Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 1 Buffer Ticks", Order = 5, GroupName = "Trading Profile 1")]
		public int TradingProfile1BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 1 Max DD Enabled", Order = 6, GroupName = "Trading Profile 1")]
		public bool TradingProfile1DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 1 Max DD ($)", Order = 7, GroupName = "Trading Profile 1")]
		public double TradingProfile1DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 1 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 1")]
		public bool TradingProfile1DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 1 Max Profit ($)", Order = 9, GroupName = "Trading Profile 1")]
		public double TradingProfile1DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 2 Name", Order = 1, GroupName = "Trading Profile 2", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile2Name
		{
			get { return profile2Name; }
			set { profile2Name = Kat34ScalperLogic.NormalizeProfileName(value, "P2"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 2 Account", Order = 2, GroupName = "Trading Profile 2")]
		public string TradingProfile2Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 2 ATM", Order = 3, GroupName = "Trading Profile 2")]
		public string TradingProfile2Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 2 Quantity", Order = 4, GroupName = "Trading Profile 2")]
		public int TradingProfile2Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 2 Buffer Ticks", Order = 5, GroupName = "Trading Profile 2")]
		public int TradingProfile2BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 2 Max DD Enabled", Order = 6, GroupName = "Trading Profile 2")]
		public bool TradingProfile2DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 2 Max DD ($)", Order = 7, GroupName = "Trading Profile 2")]
		public double TradingProfile2DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 2 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 2")]
		public bool TradingProfile2DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 2 Max Profit ($)", Order = 9, GroupName = "Trading Profile 2")]
		public double TradingProfile2DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 3 Name", Order = 1, GroupName = "Trading Profile 3", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile3Name
		{
			get { return profile3Name; }
			set { profile3Name = Kat34ScalperLogic.NormalizeProfileName(value, "P3"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 3 Account", Order = 2, GroupName = "Trading Profile 3")]
		public string TradingProfile3Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 3 ATM", Order = 3, GroupName = "Trading Profile 3")]
		public string TradingProfile3Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 3 Quantity", Order = 4, GroupName = "Trading Profile 3")]
		public int TradingProfile3Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 3 Buffer Ticks", Order = 5, GroupName = "Trading Profile 3")]
		public int TradingProfile3BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 3 Max DD Enabled", Order = 6, GroupName = "Trading Profile 3")]
		public bool TradingProfile3DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 3 Max DD ($)", Order = 7, GroupName = "Trading Profile 3")]
		public double TradingProfile3DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 3 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 3")]
		public bool TradingProfile3DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 3 Max Profit ($)", Order = 9, GroupName = "Trading Profile 3")]
		public double TradingProfile3DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 4 Name", Order = 1, GroupName = "Trading Profile 4", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile4Name
		{
			get { return profile4Name; }
			set { profile4Name = Kat34ScalperLogic.NormalizeProfileName(value, "P4"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 4 Account", Order = 2, GroupName = "Trading Profile 4")]
		public string TradingProfile4Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 4 ATM", Order = 3, GroupName = "Trading Profile 4")]
		public string TradingProfile4Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 4 Quantity", Order = 4, GroupName = "Trading Profile 4")]
		public int TradingProfile4Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 4 Buffer Ticks", Order = 5, GroupName = "Trading Profile 4")]
		public int TradingProfile4BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 4 Max DD Enabled", Order = 6, GroupName = "Trading Profile 4")]
		public bool TradingProfile4DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 4 Max DD ($)", Order = 7, GroupName = "Trading Profile 4")]
		public double TradingProfile4DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 4 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 4")]
		public bool TradingProfile4DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 4 Max Profit ($)", Order = 9, GroupName = "Trading Profile 4")]
		public double TradingProfile4DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 5 Name", Order = 1, GroupName = "Trading Profile 5", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile5Name
		{
			get { return profile5Name; }
			set { profile5Name = Kat34ScalperLogic.NormalizeProfileName(value, "P5"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 5 Account", Order = 2, GroupName = "Trading Profile 5")]
		public string TradingProfile5Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 5 ATM", Order = 3, GroupName = "Trading Profile 5")]
		public string TradingProfile5Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 5 Quantity", Order = 4, GroupName = "Trading Profile 5")]
		public int TradingProfile5Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 5 Buffer Ticks", Order = 5, GroupName = "Trading Profile 5")]
		public int TradingProfile5BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 5 Max DD Enabled", Order = 6, GroupName = "Trading Profile 5")]
		public bool TradingProfile5DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 5 Max DD ($)", Order = 7, GroupName = "Trading Profile 5")]
		public double TradingProfile5DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 5 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 5")]
		public bool TradingProfile5DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 5 Max Profit ($)", Order = 9, GroupName = "Trading Profile 5")]
		public double TradingProfile5DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 6 Name", Order = 1, GroupName = "Trading Profile 6", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile6Name
		{
			get { return profile6Name; }
			set { profile6Name = Kat34ScalperLogic.NormalizeProfileName(value, "P6"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 6 Account", Order = 2, GroupName = "Trading Profile 6")]
		public string TradingProfile6Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 6 ATM", Order = 3, GroupName = "Trading Profile 6")]
		public string TradingProfile6Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 6 Quantity", Order = 4, GroupName = "Trading Profile 6")]
		public int TradingProfile6Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 6 Buffer Ticks", Order = 5, GroupName = "Trading Profile 6")]
		public int TradingProfile6BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 6 Max DD Enabled", Order = 6, GroupName = "Trading Profile 6")]
		public bool TradingProfile6DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 6 Max DD ($)", Order = 7, GroupName = "Trading Profile 6")]
		public double TradingProfile6DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 6 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 6")]
		public bool TradingProfile6DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 6 Max Profit ($)", Order = 9, GroupName = "Trading Profile 6")]
		public double TradingProfile6DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 7 Name", Order = 1, GroupName = "Trading Profile 7", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile7Name
		{
			get { return profile7Name; }
			set { profile7Name = Kat34ScalperLogic.NormalizeProfileName(value, "P7"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 7 Account", Order = 2, GroupName = "Trading Profile 7")]
		public string TradingProfile7Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 7 ATM", Order = 3, GroupName = "Trading Profile 7")]
		public string TradingProfile7Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 7 Quantity", Order = 4, GroupName = "Trading Profile 7")]
		public int TradingProfile7Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 7 Buffer Ticks", Order = 5, GroupName = "Trading Profile 7")]
		public int TradingProfile7BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 7 Max DD Enabled", Order = 6, GroupName = "Trading Profile 7")]
		public bool TradingProfile7DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 7 Max DD ($)", Order = 7, GroupName = "Trading Profile 7")]
		public double TradingProfile7DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 7 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 7")]
		public bool TradingProfile7DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 7 Max Profit ($)", Order = 9, GroupName = "Trading Profile 7")]
		public double TradingProfile7DailyMaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile 8 Name", Order = 1, GroupName = "Trading Profile 8", Description = "HUD button label (max 8 chars)")]
		public string TradingProfile8Name
		{
			get { return profile8Name; }
			set { profile8Name = Kat34ScalperLogic.NormalizeProfileName(value, "P8"); }
		}
		[NinjaScriptProperty]
		[Display(Name = "Profile 8 Account", Order = 2, GroupName = "Trading Profile 8")]
		public string TradingProfile8Account { get; set; }
		[NinjaScriptProperty]
		[TypeConverter(typeof(Kat34ScalperAtmTemplateConverter))]
		[Display(Name = "Profile 8 ATM", Order = 3, GroupName = "Trading Profile 8")]
		public string TradingProfile8Atm { get; set; }
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Profile 8 Quantity", Order = 4, GroupName = "Trading Profile 8")]
		public int TradingProfile8Quantity { get; set; }
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Profile 8 Buffer Ticks", Order = 5, GroupName = "Trading Profile 8")]
		public int TradingProfile8BufferTicks { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 8 Max DD Enabled", Order = 6, GroupName = "Trading Profile 8")]
		public bool TradingProfile8DailyMaxDDEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 8 Max DD ($)", Order = 7, GroupName = "Trading Profile 8")]
		public double TradingProfile8DailyMaxDD { get; set; }
		[NinjaScriptProperty]
		[Display(Name = "Profile 8 Max Profit Enabled", Order = 8, GroupName = "Trading Profile 8")]
		public bool TradingProfile8DailyMaxProfitEnabled { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Profile 8 Max Profit ($)", Order = 9, GroupName = "Trading Profile 8")]
		public double TradingProfile8DailyMaxProfit { get; set; }

		// --- 8. Daily Risk Quick Sets (HUD: 6 buttons dưới Max DD/Profit toggles; click áp Max DD+Profit) ---
		private string dailyRiskSet1Name = "1";
		private string dailyRiskSet2Name = "2";
		private string dailyRiskSet3Name = "3";
		private string dailyRiskSet4Name = "4";
		private string dailyRiskSet5Name = "5";
		private string dailyRiskSet6Name = "6";

		[NinjaScriptProperty]
		[Display(Name = "Set 1 Name", Order = 1, GroupName = "8. Daily Risk Quick Sets", Description = "Button label (max 3 chars)")]
		public string DailyRiskSet1Name
		{
			get { return dailyRiskSet1Name; }
			set { dailyRiskSet1Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "1"); }
		}
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 1 Max DD ($)", Order = 2, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet1MaxDD { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 1 Max Profit ($)", Order = 3, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet1MaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 2 Name", Order = 4, GroupName = "8. Daily Risk Quick Sets", Description = "Button label (max 3 chars)")]
		public string DailyRiskSet2Name
		{
			get { return dailyRiskSet2Name; }
			set { dailyRiskSet2Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "2"); }
		}
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 2 Max DD ($)", Order = 5, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet2MaxDD { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 2 Max Profit ($)", Order = 6, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet2MaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 3 Name", Order = 7, GroupName = "8. Daily Risk Quick Sets", Description = "Button label (max 3 chars)")]
		public string DailyRiskSet3Name
		{
			get { return dailyRiskSet3Name; }
			set { dailyRiskSet3Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "3"); }
		}
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 3 Max DD ($)", Order = 8, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet3MaxDD { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 3 Max Profit ($)", Order = 9, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet3MaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 4 Name", Order = 10, GroupName = "8. Daily Risk Quick Sets", Description = "Button label (max 3 chars)")]
		public string DailyRiskSet4Name
		{
			get { return dailyRiskSet4Name; }
			set { dailyRiskSet4Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "4"); }
		}
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 4 Max DD ($)", Order = 11, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet4MaxDD { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 4 Max Profit ($)", Order = 12, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet4MaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 5 Name", Order = 13, GroupName = "8. Daily Risk Quick Sets", Description = "Button label (max 3 chars)")]
		public string DailyRiskSet5Name
		{
			get { return dailyRiskSet5Name; }
			set { dailyRiskSet5Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "5"); }
		}
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 5 Max DD ($)", Order = 14, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet5MaxDD { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 5 Max Profit ($)", Order = 15, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet5MaxProfit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Set 6 Name", Order = 16, GroupName = "8. Daily Risk Quick Sets", Description = "Button label (max 3 chars)")]
		public string DailyRiskSet6Name
		{
			get { return dailyRiskSet6Name; }
			set { dailyRiskSet6Name = Kat34ScalperLogic.NormalizeAtmSetName(value, "6"); }
		}
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 6 Max DD ($)", Order = 17, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet6MaxDD { get; set; }
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "Set 6 Max Profit ($)", Order = 18, GroupName = "8. Daily Risk Quick Sets")]
		public double DailyRiskSet6MaxProfit { get; set; }

		// --- 4. Lines & Text ---
		[NinjaScriptProperty]
		[Display(Name = "Line Length (bars)", Order = 1, GroupName = "4. Lines & Text",
			Description = "Entry, SL and TP lines extend this many bars forward from the signal candle.")]
		public int LineLengthBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Line Width (px)", Order = 2, GroupName = "4. Lines & Text")]
		public int LineWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Arrow Offset (ticks from candle)", Order = 3, GroupName = "4. Lines & Text",
			Description = "Distance between the signal candle and the arrow.")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Sell Entry Line Color", Order = 4, GroupName = "4. Lines & Text",
			Description = "Sell entry line (solid).")]
		[XmlIgnore]
		public Color SellEntryLineColor { get; set; }

		[Browsable(false)]
		public string SellEntryLineColorSerializable
		{
			get { return SellEntryLineColor.ToString(); }
			set { SellEntryLineColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Buy Entry Line Color", Order = 5, GroupName = "4. Lines & Text",
			Description = "Buy entry line (solid).")]
		[XmlIgnore]
		public Color BuyEntryLineColor { get; set; }

		[Browsable(false)]
		public string BuyEntryLineColorSerializable
		{
			get { return BuyEntryLineColor.ToString(); }
			set { BuyEntryLineColor = ParseColor(value, Colors.LimeGreen); }
		}

		[NinjaScriptProperty]
		[Display(Name = "SL Line Color", Order = 6, GroupName = "4. Lines & Text")]
		[XmlIgnore]
		public Color SLLineColor { get; set; }

		[Browsable(false)]
		public string SLLineColorSerializable
		{
			get { return SLLineColor.ToString(); }
			set { SLLineColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "TP Line Color", Order = 7, GroupName = "4. Lines & Text")]
		[XmlIgnore]
		public Color TPLineColor { get; set; }

		[Browsable(false)]
		public string TPLineColorSerializable
		{
			get { return TPLineColor.ToString(); }
			set { TPLineColor = ParseColor(value, Colors.Green); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Sell Text Color", Order = 8, GroupName = "4. Lines & Text",
			Description = "SELL label color.")]
		[XmlIgnore]
		public Color SellTextColor { get; set; }

		[Browsable(false)]
		public string SellTextColorSerializable
		{
			get { return SellTextColor.ToString(); }
			set { SellTextColor = ParseColor(value, Colors.Red); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Buy Text Color", Order = 9, GroupName = "4. Lines & Text",
			Description = "BUY label color.")]
		[XmlIgnore]
		public Color BuyTextColor { get; set; }

		[Browsable(false)]
		public string BuyTextColorSerializable
		{
			get { return BuyTextColor.ToString(); }
			set { BuyTextColor = ParseColor(value, Colors.LimeGreen); }
		}

		private static Color ParseColor(string value, Color fallback)
		{
			try
			{
				var c = ColorConverter.ConvertFromString(value);
				if (c != null) return (Color)c;
			}
			catch { }
			return fallback;
		}

		// ponytail: DRY profile init for SetDefaults — covers account/ATM/qty/buffer/dailyRisk (whole account)
		private void InitTradingProfileDefaults(int idx)
		{
			string n = "P" + (idx + 1);
			switch (idx)
			{
				case 0: TradingProfile1Name=n; TradingProfile1Account="Sim101"; TradingProfile1Atm=""; TradingProfile1Quantity=1; TradingProfile1BufferTicks=2; TradingProfile1DailyMaxDDEnabled=false; TradingProfile1DailyMaxDD=500; TradingProfile1DailyMaxProfitEnabled=false; TradingProfile1DailyMaxProfit=1000; break;
				case 1: TradingProfile2Name=n; TradingProfile2Account="Sim101"; TradingProfile2Atm=""; TradingProfile2Quantity=1; TradingProfile2BufferTicks=2; TradingProfile2DailyMaxDDEnabled=false; TradingProfile2DailyMaxDD=500; TradingProfile2DailyMaxProfitEnabled=false; TradingProfile2DailyMaxProfit=1000; break;
				case 2: TradingProfile3Name=n; TradingProfile3Account="Sim101"; TradingProfile3Atm=""; TradingProfile3Quantity=1; TradingProfile3BufferTicks=2; TradingProfile3DailyMaxDDEnabled=false; TradingProfile3DailyMaxDD=500; TradingProfile3DailyMaxProfitEnabled=false; TradingProfile3DailyMaxProfit=1000; break;
				case 3: TradingProfile4Name=n; TradingProfile4Account="Sim101"; TradingProfile4Atm=""; TradingProfile4Quantity=1; TradingProfile4BufferTicks=2; TradingProfile4DailyMaxDDEnabled=false; TradingProfile4DailyMaxDD=500; TradingProfile4DailyMaxProfitEnabled=false; TradingProfile4DailyMaxProfit=1000; break;
				case 4: TradingProfile5Name=n; TradingProfile5Account="Sim101"; TradingProfile5Atm=""; TradingProfile5Quantity=1; TradingProfile5BufferTicks=2; TradingProfile5DailyMaxDDEnabled=false; TradingProfile5DailyMaxDD=500; TradingProfile5DailyMaxProfitEnabled=false; TradingProfile5DailyMaxProfit=1000; break;
				case 5: TradingProfile6Name=n; TradingProfile6Account="Sim101"; TradingProfile6Atm=""; TradingProfile6Quantity=1; TradingProfile6BufferTicks=2; TradingProfile6DailyMaxDDEnabled=false; TradingProfile6DailyMaxDD=500; TradingProfile6DailyMaxProfitEnabled=false; TradingProfile6DailyMaxProfit=1000; break;
				case 6: TradingProfile7Name=n; TradingProfile7Account="Sim101"; TradingProfile7Atm=""; TradingProfile7Quantity=1; TradingProfile7BufferTicks=2; TradingProfile7DailyMaxDDEnabled=false; TradingProfile7DailyMaxDD=500; TradingProfile7DailyMaxProfitEnabled=false; TradingProfile7DailyMaxProfit=1000; break;
				default: TradingProfile8Name=n; TradingProfile8Account="Sim101"; TradingProfile8Atm=""; TradingProfile8Quantity=1; TradingProfile8BufferTicks=2; TradingProfile8DailyMaxDDEnabled=false; TradingProfile8DailyMaxDD=500; TradingProfile8DailyMaxProfitEnabled=false; TradingProfile8DailyMaxProfit=1000; break;
			}
		}

		private void SeedTradingProfileDefaults(int idx)
		{
			string defName = "P" + (idx + 1);
			switch (idx)
			{
				case 0: if (TradingProfile1Quantity!=0) return; TradingProfile1Name=defName; TradingProfile1Quantity=1; TradingProfile1BufferTicks=2; TradingProfile1DailyMaxDD=500; TradingProfile1DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile1Account)) TradingProfile1Account="Sim101"; break;
				case 1: if (TradingProfile2Quantity!=0) return; TradingProfile2Name=defName; TradingProfile2Quantity=1; TradingProfile2BufferTicks=2; TradingProfile2DailyMaxDD=500; TradingProfile2DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile2Account)) TradingProfile2Account="Sim101"; break;
				case 2: if (TradingProfile3Quantity!=0) return; TradingProfile3Name=defName; TradingProfile3Quantity=1; TradingProfile3BufferTicks=2; TradingProfile3DailyMaxDD=500; TradingProfile3DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile3Account)) TradingProfile3Account="Sim101"; break;
				case 3: if (TradingProfile4Quantity!=0) return; TradingProfile4Name=defName; TradingProfile4Quantity=1; TradingProfile4BufferTicks=2; TradingProfile4DailyMaxDD=500; TradingProfile4DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile4Account)) TradingProfile4Account="Sim101"; break;
				case 4: if (TradingProfile5Quantity!=0) return; TradingProfile5Name=defName; TradingProfile5Quantity=1; TradingProfile5BufferTicks=2; TradingProfile5DailyMaxDD=500; TradingProfile5DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile5Account)) TradingProfile5Account="Sim101"; break;
				case 5: if (TradingProfile6Quantity!=0) return; TradingProfile6Name=defName; TradingProfile6Quantity=1; TradingProfile6BufferTicks=2; TradingProfile6DailyMaxDD=500; TradingProfile6DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile6Account)) TradingProfile6Account="Sim101"; break;
				case 6: if (TradingProfile7Quantity!=0) return; TradingProfile7Name=defName; TradingProfile7Quantity=1; TradingProfile7BufferTicks=2; TradingProfile7DailyMaxDD=500; TradingProfile7DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile7Account)) TradingProfile7Account="Sim101"; break;
				default: if (TradingProfile8Quantity!=0) return; TradingProfile8Name=defName; TradingProfile8Quantity=1; TradingProfile8BufferTicks=2; TradingProfile8DailyMaxDD=500; TradingProfile8DailyMaxProfit=1000; if(string.IsNullOrWhiteSpace(TradingProfile8Account)) TradingProfile8Account="Sim101"; break;
			}
		}
		#endregion
	}
}
