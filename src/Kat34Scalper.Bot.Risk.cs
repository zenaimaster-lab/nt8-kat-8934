/*
 * Kat34Scalper.Bot.Risk.cs — Bot Risk module (partial class Kat34Scalper).
 * Daily MaxDD / MaxProfit session baseline (NY 18:00) + breach gate.
 * Extracted from Kat34Scalper.Bot.cs for module clarity (v0.96 audit).
 */

#region Using declarations
using System;
using System.Windows.Media;
using NinjaTrader.Cbi;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private DateTime lastSessionStartUtc;
		private double sessionStartRealizedPnL;
		private bool isSessionStartCaptured;
		private int dailyRiskFlattened;

		private double CalculateDailyPnL()
		{
			Account acc = ResolveBotAccount();
			if (acc == null) return 0;

			DateTime currentSessionStartUtc;
			try { currentSessionStartUtc = Kat34ScalperLogic.GetNySessionStartUtc(DateTime.UtcNow); }
			catch (Exception ex) { Print("[Kat34Scalper] NY session calc failed: " + ex.Message); return 0; }
			double currentRealizedPnL = 0;
			bool realizedReadOk;
			try
			{
				currentRealizedPnL = acc.Get(AccountItem.GrossRealizedProfitLoss, Currency.UsDollar);
				realizedReadOk = true;
			}
			catch
			{
				realizedReadOk = false;
			}

			if (Kat34ScalperLogic.ShouldCaptureSessionBaseline(isSessionStartCaptured, currentSessionStartUtc, lastSessionStartUtc, realizedReadOk))
			{
				lastSessionStartUtc = currentSessionStartUtc;
				sessionStartRealizedPnL = currentRealizedPnL;
				isSessionStartCaptured = true;
			}

			if (!realizedReadOk) return 0; // poisoned baseline guard
			double dailyRealized = currentRealizedPnL - sessionStartRealizedPnL;
			double dailyUnrealized = 0;
			try
			{
				dailyUnrealized = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
			}
			catch { }

			return dailyRealized + dailyUnrealized;
		}

		private bool IsDailyRiskBreached(out string breachReason)
		{
			breachReason = string.Empty;
			Account acc = ResolveBotAccount();
			if (acc == null) return false;

			double dailyPnL = CalculateDailyPnL();

			return Kat34ScalperLogic.EvaluateDailyRiskBreach(
				cachedIsDailyMaxDD, cachedDailyMaxDD,
				cachedIsDailyMaxProfit, cachedDailyMaxProfit,
				dailyPnL, out breachReason);
		}

		private void EvaluateDailyRiskLimits()
		{
			Account acc = ResolveBotAccount();
			if (acc == null) return;

			if (IsDailyRiskBreached(out string breachReason))
			{
				if (System.Threading.Interlocked.CompareExchange(ref dailyRiskFlattened, 1, 0) == 0)
				{
					Print(string.Format("[Kat34Scalper] EMERGENCY CANCEL triggered by Daily Risk Protection: {0}", breachReason));
					ShowHudStatus(breachReason, Brushes.OrangeRed);
					CancelPendingBotOrder(breachReason);
				}
			}
			else
			{
				System.Threading.Interlocked.Exchange(ref dailyRiskFlattened, 0);
			}
		}
	}
}
