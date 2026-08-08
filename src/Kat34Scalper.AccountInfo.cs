/*
 * Kat34Scalper.AccountInfo.cs — Account Info header (partial class Kat34Scalper).
 * Top black board: realtime NY time + account/balance/PnL + bot indicators (BOT/B1/B2/position).
 * Style ported verbatim from TradeManager KatTradeManager.AccountInfo.cs, adapted to Scalper domain.
 * HudGap 2, HudPanelWidth 250 → inner 238, UseLayoutRounding true, footer 10.
 */

#region Using declarations
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private Border accountInfoCard;
		private TextBlock accountInfoDateTimeText;
		private Run accountDateRun;
		private Run accountTimeHmRun;
		private Run accountTimeSRun;
		private Run accountAmPmRun;
		private Run accountNytRun;
		private TextBlock accountBalanceText;
		private Run accountBalanceLabelRun;
		private Run accountBalanceValueRun;
		private TextBlock accountUnrealText;
		private TextBlock accountRealText;
		private Run accountUnrealLabelRun;
		private Run accountUnrealValueRun;
		private Run accountRealLabelRun;
		private Run accountRealValueRun;
		private TextBlock accountDailyText;
		private Run accountDailyLabelRun;
		private Run accountDailyValueRun;
		private TextBlock accountAcctText;
		private Run accountAcctLabelRun;
		private Run accountAcctValueRun;
		private TextBlock accountBotText;
		private Run accountBotLabelRun;
		private Run accountBotValueRun;
		private Run accountBotSep1;
		private Run accountB1Run;
		private Run accountBotSep2;
		private Run accountB2Run;
		private Run accountBotSep3;
		private Run accountPosRun;

		private readonly SolidColorBrush accountDateBrush = CreateFrozenBrush(Color.FromRgb(180, 100, 255));
		private readonly SolidColorBrush accountTimeBrush = CreateFrozenBrush(Color.FromRgb(255, 165, 0));
		private readonly SolidColorBrush accountGrayBrush = CreateFrozenBrush(Color.FromRgb(160, 160, 160));
		private readonly SolidColorBrush pnlPositiveBrush = CreateFrozenBrush(Color.FromRgb(40, 200, 80));
		private readonly SolidColorBrush pnlNegativeBrush = CreateFrozenBrush(Color.FromRgb(220, 50, 50));
		private readonly SolidColorBrush botOnBrush2 = CreateFrozenBrush(Color.FromRgb(40, 200, 80));
		private readonly SolidColorBrush bOnBrush = CreateFrozenBrush(Color.FromRgb(15, 60, 130));

		private static DateTime GetNyTime(DateTime utc)
		{
			TimeZoneInfo nyZone;
			try { nyZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
			catch { try { nyZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); } catch { nyZone = TimeZoneInfo.Local; } }
			return TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime(), nyZone);
		}

		private Border CreateAccountInfoSection()
		{
			StackPanel inner = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };

			accountDateRun = new Run("") { Foreground = accountDateBrush };
			accountTimeHmRun = new Run("") { Foreground = accountTimeBrush, FontWeight = FontWeights.Bold };
			accountTimeSRun = new Run("") { Foreground = accountTimeBrush, FontWeight = FontWeights.Normal };
			accountAmPmRun = new Run("") { Foreground = accountGrayBrush };
			accountNytRun = new Run(" (NYT)") { Foreground = accountGrayBrush };
			accountInfoDateTimeText = new TextBlock
			{
				FontSize = 11,
				Margin = new Thickness(0, 0, 0, HudGap),
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountInfoDateTimeText.Inlines.Add(accountDateRun);
			accountInfoDateTimeText.Inlines.Add(new Run("   ") { Foreground = accountGrayBrush });
			accountInfoDateTimeText.Inlines.Add(accountTimeHmRun);
			accountInfoDateTimeText.Inlines.Add(accountTimeSRun);
			accountInfoDateTimeText.Inlines.Add(accountAmPmRun);
			accountInfoDateTimeText.Inlines.Add(accountNytRun);
			inner.Children.Add(accountInfoDateTimeText);

			accountAcctLabelRun = new Run("Acct: ") { Foreground = accountGrayBrush };
			accountAcctValueRun = new Run("--") { Foreground = accountGrayBrush };
			accountAcctText = new TextBlock
			{
				FontSize = 11,
				Margin = new Thickness(0, 0, 0, HudGap),
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				TextTrimming = TextTrimming.CharacterEllipsis,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountAcctText.Inlines.Add(accountAcctLabelRun);
			accountAcctText.Inlines.Add(accountAcctValueRun);
			inner.Children.Add(accountAcctText);

			accountBalanceLabelRun = new Run("Balance: ") { Foreground = accountGrayBrush };
			accountBalanceValueRun = new Run("--") { Foreground = accountGrayBrush };
			accountBalanceText = new TextBlock
			{
				FontSize = 11,
				Margin = new Thickness(0, 0, 0, HudGap),
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountBalanceText.Inlines.Add(accountBalanceLabelRun);
			accountBalanceText.Inlines.Add(accountBalanceValueRun);
			inner.Children.Add(accountBalanceText);

			accountDailyLabelRun = new Run("Day: ") { Foreground = accountGrayBrush };
			accountDailyValueRun = new Run("--") { Foreground = accountGrayBrush };
			accountDailyText = new TextBlock
			{
				FontSize = 11,
				Margin = new Thickness(0, 0, 0, HudGap),
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountDailyText.Inlines.Add(accountDailyLabelRun);
			accountDailyText.Inlines.Add(accountDailyValueRun);
			inner.Children.Add(accountDailyText);

			accountUnrealLabelRun = new Run("U: ") { Foreground = accountGrayBrush };
			accountUnrealValueRun = new Run("--") { Foreground = accountGrayBrush };
			accountUnrealText = new TextBlock
			{
				FontSize = 11,
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountUnrealText.Inlines.Add(accountUnrealLabelRun);
			accountUnrealText.Inlines.Add(accountUnrealValueRun);

			accountRealLabelRun = new Run("R: ") { Foreground = accountGrayBrush };
			accountRealValueRun = new Run("--") { Foreground = accountGrayBrush };
			accountRealText = new TextBlock
			{
				FontSize = 11,
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountRealText.Inlines.Add(accountRealLabelRun);
			accountRealText.Inlines.Add(accountRealValueRun);

			Grid pnlGrid = CreateTwoColumnGrid(0, HudGap);
			Grid.SetColumn(accountUnrealText, 0);
			Grid.SetColumn(accountRealText, 2);
			pnlGrid.Children.Add(accountUnrealText);
			pnlGrid.Children.Add(accountRealText);
			inner.Children.Add(pnlGrid);

			accountBotLabelRun = new Run("Bots: ") { Foreground = accountGrayBrush };
			accountBotValueRun = new Run("BOT OFF") { Foreground = accountGrayBrush };
			accountBotSep1 = new Run("  ") { Foreground = accountGrayBrush };
			accountB1Run = new Run("B1 OFF") { Foreground = accountGrayBrush };
			accountBotSep2 = new Run("  ") { Foreground = accountGrayBrush };
			accountB2Run = new Run("B2 OFF") { Foreground = accountGrayBrush };
			accountBotSep3 = new Run("  ") { Foreground = accountGrayBrush };
			accountPosRun = new Run("") { Foreground = accountGrayBrush };
			accountBotText = new TextBlock
			{
				FontSize = 10,
				FontWeight = FontWeights.SemiBold,
				Margin = new Thickness(0, HudGap, 0, 0),
				HorizontalAlignment = HorizontalAlignment.Left,
				TextWrapping = TextWrapping.NoWrap,
				TextTrimming = TextTrimming.CharacterEllipsis,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			accountBotText.Inlines.Add(accountBotLabelRun);
			accountBotText.Inlines.Add(accountBotValueRun);
			accountBotText.Inlines.Add(accountBotSep1);
			accountBotText.Inlines.Add(accountB1Run);
			accountBotText.Inlines.Add(accountBotSep2);
			accountBotText.Inlines.Add(accountB2Run);
			accountBotText.Inlines.Add(accountBotSep3);
			accountBotText.Inlines.Add(accountPosRun);
			inner.Children.Add(accountBotText);

			var accContentHost = new Border
			{
				Padding = new Thickness(HudGap, HudGap + 4, HudGap, HudGap + 4),
				Background = Brushes.Transparent,
				Child = inner,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			var accFooter = new Border
			{
				Height = 10,
				Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
				CornerRadius = new CornerRadius(0, 0, 4, 4),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			var accInner = new Grid { UseLayoutRounding = true, SnapsToDevicePixels = true };
			accInner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			accInner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
			Grid.SetRow(accContentHost, 0);
			Grid.SetRow(accFooter, 1);
			accInner.Children.Add(accContentHost);
			accInner.Children.Add(accFooter);
			accountInfoCard = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(5),
				Margin = new Thickness(0, 0, 0, HudGap),
				Child = accInner,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			UpdateAccountInfoSection();
			return accountInfoCard;
		}

		private void UpdateAccountInfoSection()
		{
			if (accountInfoDateTimeText == null || accountDateRun == null) return;
			try
			{
				DateTime nyTime = GetNyTime(DateTime.UtcNow);
				string dateStr = nyTime.ToString("dddd dd, MMM", CultureInfo.InvariantCulture);
				string timeStr = nyTime.ToString("hh:mm:ss", CultureInfo.InvariantCulture);
				string amPmStr = nyTime.ToString("tt", CultureInfo.InvariantCulture).ToLowerInvariant();
				string hmStr = timeStr.Length >= 5 ? timeStr.Substring(0, 5) : timeStr;
				string sStr = timeStr.Length > 5 ? timeStr.Substring(5) : "";
				if (accountDateRun.Text != dateStr) accountDateRun.Text = dateStr;
				if (accountTimeHmRun.Text != hmStr) accountTimeHmRun.Text = hmStr;
				if (accountTimeSRun.Text != sStr) accountTimeSRun.Text = sStr;
				string amPmWithSpace = " " + amPmStr;
				if (accountAmPmRun.Text != amPmWithSpace) accountAmPmRun.Text = amPmWithSpace;
			}
			catch { }

			Account acc = null;
			try { acc = ResolveBotAccount(); } catch { }
			string acctName = acc != null ? acc.Name : (!string.IsNullOrEmpty(cachedBotAccountName) ? cachedBotAccountName : "--");
			string instrName = "--";
			try
			{
				if (Instrument != null && Instrument.MasterInstrument != null)
					instrName = Instrument.MasterInstrument.Name;
			}
			catch { }
			string acctLine = acctName + "  •  " + instrName;
			try { if (accountAcctValueRun.Text != acctLine) accountAcctValueRun.Text = acctLine; } catch { }

			if (acc == null)
			{
				try
				{
					if (accountBalanceValueRun.Text != "--") accountBalanceValueRun.Text = "--";
					accountBalanceValueRun.Foreground = accountGrayBrush;
					if (accountDailyValueRun.Text != "--") accountDailyValueRun.Text = "--";
					accountDailyValueRun.Foreground = accountGrayBrush;
					if (accountUnrealValueRun.Text != "--") accountUnrealValueRun.Text = "--";
					accountUnrealValueRun.Foreground = accountGrayBrush;
					if (accountRealValueRun.Text != "--") accountRealValueRun.Text = "--";
					accountRealValueRun.Foreground = accountGrayBrush;
				}
				catch { }
			}
			else
			{
				double balance = double.NaN;
				try { balance = acc.Get(AccountItem.CashValue, Currency.UsDollar); } catch { }
				if (double.IsNaN(balance) || double.IsInfinity(balance)) try { balance = acc.Get(AccountItem.TotalCashBalance, Currency.UsDollar); } catch { }
				if (double.IsNaN(balance) || double.IsInfinity(balance)) try { balance = acc.Get(AccountItem.NetLiquidation, Currency.UsDollar); } catch { }
				if (double.IsNaN(balance) || double.IsInfinity(balance)) balance = 0;
				double unreal = 0;
				try { unreal = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar); } catch { }
				double realized = double.NaN;
				try { realized = acc.Get(AccountItem.GrossRealizedProfitLoss, Currency.UsDollar); } catch { }
				if (double.IsNaN(realized) || double.IsInfinity(realized)) try { realized = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar); } catch { }
				if (double.IsNaN(realized) || double.IsInfinity(realized)) realized = 0;
				double daily = 0;
				bool dailyOk = false;
				try { daily = CalculateDailyPnL(); dailyOk = true; } catch { }

				try
				{
					string balStr = balance.ToString("N0", CultureInfo.InvariantCulture);
					if (accountBalanceValueRun.Text != balStr) accountBalanceValueRun.Text = balStr;
					accountBalanceValueRun.Foreground = accountGrayBrush;
				}
				catch { }
				try
				{
					if (dailyOk)
					{
						string dStr; Brush dBrush;
						if (daily > 0.005) { dStr = "+" + daily.ToString("N0", CultureInfo.InvariantCulture); dBrush = pnlPositiveBrush; }
						else if (daily < -0.005) { dStr = daily.ToString("N0", CultureInfo.InvariantCulture); dBrush = pnlNegativeBrush; }
						else { dStr = "0"; dBrush = accountGrayBrush; }
						if (accountDailyValueRun.Text != dStr) accountDailyValueRun.Text = dStr;
						accountDailyValueRun.Foreground = dBrush;
					}
				}
				catch { }
				try
				{
					string uStr; Brush uBrush;
					if (unreal > 0.005) { uStr = "+" + unreal.ToString("N0", CultureInfo.InvariantCulture); uBrush = pnlPositiveBrush; }
					else if (unreal < -0.005) { uStr = unreal.ToString("N0", CultureInfo.InvariantCulture); uBrush = pnlNegativeBrush; }
					else { uStr = "0"; uBrush = accountGrayBrush; }
					if (accountUnrealValueRun.Text != uStr) accountUnrealValueRun.Text = uStr;
					accountUnrealValueRun.Foreground = uBrush;
				}
				catch { }
				try
				{
					string rStr; Brush rBrush;
					if (realized > 0.005) { rStr = "+" + realized.ToString("N0", CultureInfo.InvariantCulture); rBrush = pnlPositiveBrush; }
					else if (realized < -0.005) { rStr = realized.ToString("N0", CultureInfo.InvariantCulture); rBrush = pnlNegativeBrush; }
					else { rStr = "0"; rBrush = accountGrayBrush; }
					if (accountRealValueRun.Text != rStr) accountRealValueRun.Text = rStr;
					accountRealValueRun.Foreground = rBrush;
				}
				catch { }
			}

			try
			{
				bool botOn = false; try { botOn = cachedBotOn && BotEnabled; } catch { botOn = cachedBotOn; }
				bool b1On = false; try { b1On = cachedB1; } catch { }
				bool b2On = false; try { b2On = cachedB2; } catch { }
				string botTxt = botOn ? "BOT ON" : "BOT OFF";
				Brush botBrush = botOn ? botOnBrush2 : accountGrayBrush;
				if (accountBotValueRun.Text != botTxt) accountBotValueRun.Text = botTxt;
				accountBotValueRun.Foreground = botBrush;

				string b1Txt = b1On ? "B1 ON" : "B1 OFF";
				Brush b1Brush = b1On ? bOnBrush : accountGrayBrush;
				if (accountB1Run.Text != b1Txt) accountB1Run.Text = b1Txt;
				accountB1Run.Foreground = b1Brush;

				string b2Txt = b2On ? "B2 ON" : "B2 OFF";
				Brush b2Brush = b2On ? bOnBrush : accountGrayBrush;
				if (accountB2Run.Text != b2Txt) accountB2Run.Text = b2Txt;
				accountB2Run.Foreground = b2Brush;

				string posTxt = "";
				try
				{
					Position pos = GetInstrumentPosition();
					if (pos != null && pos.MarketPosition != MarketPosition.Flat)
					{
						string side = pos.MarketPosition == MarketPosition.Long ? "Long" : "Short";
						posTxt = "POS " + side + " " + pos.Quantity;
					}
					else if (pendingOrder != null && IsActiveOrderState(pendingOrder.OrderState))
					{
						posTxt = "PENDING " + pendingOrderOwner + " " + (pendingIsBuy ? "Buy" : "Sell");
					}
					else posTxt = "Flat";
				}
				catch { posTxt = ""; }
				if (accountPosRun.Text != posTxt) accountPosRun.Text = posTxt;
				accountPosRun.Foreground = accountGrayBrush;
			}
			catch { }
		}
	}
}
