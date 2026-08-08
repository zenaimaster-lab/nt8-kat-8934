/*
 * Kat34Scalper.Bot.cs — Bot module (partial class Kat34Scalper).
 * Semi-auto: trades only while the HUD BOT button is ON and Bot Enabled is set.
 * Receives the signal's reference extreme, converts it to the right order type
 * (stop on the valid side of market, limit when price already ran past it),
 * submits through the selected ATM template (SL/TP/trailing brackets), migrates
 * the pending entry to a better extreme, cancels on trend flip / BOT OFF.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	// No ': Indicator' — see Kat34Scalper.Signal.cs (NT8 codegen duplication guard).
	public partial class Kat34Scalper
	{
		// --- Bot module state ---
		private volatile bool cachedBotOn;
		private volatile string cachedBotAtm = "";
		private volatile string cachedBotAccountName = "";
		private volatile bool cachedIsDailyMaxDD;
		private double cachedDailyMaxDD = 500;
		private volatile bool cachedIsDailyMaxProfit;
		private double cachedDailyMaxProfit = 1000;
		private volatile int cachedBotBufferTicks = 2;

		private Order pendingOrder;
		private Account pendingOrderAccount; // account that owns pendingOrder (cancel must target owner account)
		private string pendingOrderOwner = ""; // signal module that submitted pendingOrder ("B1"/"B2" — per-signal cancel)
		private int pendingOffsetTicks = 1; // entry offset of the owning signal (migration re-place must reuse it)
		private bool pendingIsBuy;
		private double pendingEntryPrice; // last submitted entry price (limit OR stop — Order.StopPrice is 0 on limits)
		private double pendingBestRef;    // best extreme used for migration (sell: highest qualifying low / buy: lowest high)
		private double pendingMigrateRef; // better extreme found; new order placed once the cancelled one is terminal
		private volatile bool pendingMigrate;
		private string atmLevelsName = "\0"; // never matches a real template name — forces first parse
		private Kat34ScalperAtmData atmLevels;
		private readonly System.Collections.Generic.Dictionary<string, bool> signalInTradeMap = new System.Collections.Generic.Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

		private bool IsSignalInTrade(string owner)
		{
			if (string.IsNullOrEmpty(owner)) return false;
			bool inTrade;
			return signalInTradeMap.TryGetValue(owner, out inTrade) && inTrade;
		}

		private void SetSignalInTrade(string owner, bool inTrade)
		{
			if (string.IsNullOrEmpty(owner)) return;
			signalInTradeMap[owner] = inTrade;
		}

		private Account ResolveBotAccount()
		{
			string name = cachedBotAccountName;
			if (string.IsNullOrEmpty(name) || Account.All == null) return null;
			foreach (Account acc in Account.All)
				if (acc.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return acc;
			return null;
		}

		private bool HasAtmTemplate(string tpl)
		{
			return !string.IsNullOrEmpty(tpl)
				&& !tpl.Equals("None", StringComparison.OrdinalIgnoreCase)
				&& File.Exists(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy", tpl + ".xml"));
		}

		// Parses the selected ATM template once; re-parses only when the template name changes (HUD or settings).
		// Draw module consumes the levels for the trigger lines.
		private Kat34ScalperAtmData GetAtmData()
		{
			string tpl = cachedBotAtm ?? "";
			if (tpl != atmLevelsName)
			{
				atmLevelsName = tpl;
				atmLevels = HasAtmTemplate(tpl)
					? Kat34ScalperAtmParser.ParseFile(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy", tpl + ".xml"))
					: new Kat34ScalperAtmData();
			}
			return atmLevels;
		}

		private int GetEffectiveBotQuantity()
		{
			Kat34ScalperAtmData atm = GetAtmData();
			return (atm != null && atm.Quantity > 0) ? atm.Quantity : Math.Max(1, BotOrderQuantity);
		}

		// BOT trades exactly the signals that are ON: an owner switched OFF never submits
		// (and its pending order was already cancelled by Set*Signal(false)).
		private bool SignalOwnerEnabled(string owner)
		{
			if (owner == "B1") return cachedB1;
			if (owner == "B2") return cachedB2;
			return false;
		}

		private bool HasOpenPosition(Account acc)
		{
			if (acc == null || acc.Positions == null) return false;
			try
			{
				foreach (Position pos in acc.Positions)
				{
					if (pos != null && pos.Instrument != null && Instrument != null
						&& pos.Instrument.FullName == Instrument.FullName
						&& pos.MarketPosition != MarketPosition.Flat)
						return true;
				}
			}
			catch { }
			return false;
		}

		// Called from the Signal module after a signal fires. refExtreme = best candidate extreme (sell: c2 low / buy: c2 high).
		// offsetTicks = the calling signal's own Entry Offset (order price must match its drawn entry line).
		// owner = signal module id ("B1"/"B2") — a signal cancels only its own pending order.
		private void TrySubmitBotEntry(bool isBuy, double refExtreme, int offsetTicks, string owner = "B1")
		{
			if (!cachedBotOn || !BotEnabled || refExtreme == 0) return;
			if (!SignalOwnerEnabled(owner)) return;
			Account acc = ResolveBotAccount();
			if (acc == null) return;
			if (IsDailyRiskBreached(out string breachReason))
			{
				ShowHudStatus(breachReason, Brushes.OrangeRed);
				Print(string.Format("[Kat34Scalper] BOT [{0}] entry blocked: {1}", owner, breachReason));
				return;
			}
			if (IsSignalInTrade(owner) || HasOpenPosition(acc)) return;
			if (pendingOrder != null || pendingMigrate) return; // one bot order at a time
			SubmitBotOrder(isBuy, refExtreme, offsetTicks, owner);
		}

		// Cancels the bot's pending entry only when it belongs to the given signal (any side).
		// Used when the signal is switched OFF — OFF must also kill its working order and
		// stop any in-flight migration re-place.
		private void CancelSignalBotEntry(string owner, string reason)
		{
			SetSignalInTrade(owner, false);
			if (pendingOrder != null && pendingOrderOwner == owner)
			{
				pendingMigrate = false;
				CancelPendingBotOrder(reason);
			}
		}

		private void SubmitBotOrder(bool isBuy, double refExtreme, int offsetTicks, string owner = "B1")
		{
			Account acc = ResolveBotAccount();
			if (acc == null)
			{
				Print("[Kat34Scalper] BOT: no account selected — pick one on the HUD or in settings.");
				return;
			}
			double entryPrice = isBuy
				? refExtreme + offsetTicks * TickSize
				: refExtreme - offsetTicks * TickSize;
			bool useStop = Kat34ScalperLogic.UseStopOrder(isBuy, entryPrice, Closes[0][0]);
			int qty = GetEffectiveBotQuantity();
			try
			{
				Order order = acc.CreateOrder(Instrument,
					isBuy ? OrderAction.Buy : OrderAction.Sell,
					useStop ? OrderType.StopMarket : OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc,
					qty, useStop ? 0 : entryPrice, useStop ? entryPrice : 0, "", "Entry", NinjaTrader.Core.Globals.MaxDate, null);

				pendingOrder = order;
				pendingOrderOwner = owner;
				pendingOffsetTicks = offsetTicks;
				pendingIsBuy = isBuy;
				pendingBestRef = refExtreme;
				pendingEntryPrice = entryPrice;

				pendingOrderAccount = acc;

				TrackAtmStartup(order);
				string tpl = cachedBotAtm;
				bool hasAtm = HasAtmTemplate(tpl);
				if (hasAtm)
					NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, order);
				else
				{
					if (!string.IsNullOrEmpty(tpl) && !tpl.Equals("None", StringComparison.OrdinalIgnoreCase))
						Print(string.Format("[Kat34Scalper] BOT: ATM template '{0}' not found — bare stop order.", tpl));
					acc.Submit(new[] { order });
				}
				ScheduleAtmBracketMerge();
				Print(string.Format("[Kat34Scalper] BOT [{5}]: {0} {1} {6}ct @ {2:F5} submitted (account {3}, ATM {4}).",
					isBuy ? "BUY" : "SELL", useStop ? "stop" : "limit", entryPrice, acc.Name, hasAtm ? tpl : "none", owner, qty));
				ShowHudStatus(string.Format("BOT [{4}]: {0} {1} {5}ct @ {2:F2} ({3})", isBuy ? "BUY" : "SELL", useStop ? "stop" : "limit", entryPrice, hasAtm ? tpl : "no ATM", owner, qty), Brushes.LightGreen);
			}
			catch (Exception ex)
			{
				pendingOrder = null;
				pendingOrderAccount = null;
				Print(string.Format("[Kat34Scalper] BOT submit error: {0}", ex.Message));
				ShowHudStatus("BOT submit error: " + ex.Message, Brushes.OrangeRed);
			}
		}

		// Polls the pending order on the data thread: terminal cleanup, trend-flip cancel, migrate to a better extreme.
		private void ManageBotEntry(double high, double low, double close)
		{
			EvaluateDailyRiskLimits();
			TrySubmitPendingRevert();
			CleanupFlatOrphans();

			Account acc = ResolveBotAccount();
			if (acc != null && !HasOpenPosition(acc))
			{
				if (signalInTradeMap.Count > 0)
				{
					signalInTradeMap.Clear();
				}
			}

			if (pendingOrder == null)
			{

				pendingOrderAccount = null;
				// A cancelled order left a better entry behind — re-place it while the setup still holds.
				// Owner gate: a signal switched OFF mid-migration must not see its order re-placed.
				if (pendingMigrate && cachedBotOn && BotEnabled && SignalOwnerEnabled(pendingOrderOwner))
				{
					pendingMigrate = false;
					if (fastEma != null && slowEma != null
						&& (pendingIsBuy ? fastEma[0] > slowEma[0] && close > fastEma[0] : fastEma[0] < slowEma[0] && close < fastEma[0]))
						SubmitBotOrder(pendingIsBuy, pendingMigrateRef, pendingOffsetTicks, pendingOrderOwner);
				}
				return;
			}

			OrderState state = pendingOrder.OrderState;
			if (state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected)
			{
				Print(string.Format("[Kat34Scalper] BOT: entry order {0} @ {1:F5}.", state, pendingEntryPrice));
				if (state == OrderState.Filled)
				{
					SetSignalInTrade(pendingOrderOwner, true);
					ShowHudStatus(string.Format("BOT [{0}]: entry FILLED @ {1:F2} — ATM manages brackets", pendingOrderOwner, pendingEntryPrice), Brushes.LightGreen);
					ClearSignalDrawings(pendingOrderOwner);
				}
				pendingOrder = null;
				pendingOrderAccount = null;
				return; // filled: ATM owns the brackets from here
			}
			if (state != OrderState.Working && state != OrderState.Accepted) return;
			if (fastEma == null || slowEma == null) return;

			// Trend flipped — cancel the pending entry.
			if (pendingIsBuy ? fastEma[0] < slowEma[0] : fastEma[0] > slowEma[0])
			{
				CancelPendingBotOrder("trend flip");
				return;
			}

			// Migration: a newer bar closed on the setup side of ema34 with a better extreme.
			if (!pendingIsBuy && close < fastEma[0] && low > pendingBestRef)
			{
				pendingBestRef = low;
				pendingMigrateRef = low;
				pendingMigrate = true;
				CancelPendingBotOrder("migrate to higher sell stop");
			}
			else if (pendingIsBuy && close > fastEma[0] && high < pendingBestRef)
			{
				pendingBestRef = high;
				pendingMigrateRef = high;
				pendingMigrate = true;
				CancelPendingBotOrder("migrate to lower buy stop");
			}
		}

		private void CancelPendingBotOrder(string reason)
		{
			if (pendingOrder == null) return;
			try
			{
				Account acc = pendingOrderAccount ?? ResolveBotAccount();
				if (acc != null)
				{
					acc.Cancel(new[] { pendingOrder });
					Print(string.Format("[Kat34Scalper] BOT: entry cancel requested ({0}).", reason));
					ShowHudStatus("BOT: entry cancel — " + reason, Brushes.OrangeRed);
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] BOT cancel error: {0}", ex.Message));
			}
		}

		private static bool IsActiveOrderState(OrderState state)
		{
			return state == OrderState.Initialized
				|| state == OrderState.Submitted
				|| state == OrderState.Accepted
				|| state == OrderState.AcceptedByRisk
				|| state == OrderState.Working
				|| state == OrderState.TriggerPending
				|| state == OrderState.ChangePending
				|| state == OrderState.ChangeSubmitted
				|| state == OrderState.PartFilled
				|| state == OrderState.Suspended
				|| state == OrderState.CancelPending
				|| state == OrderState.CancelSubmitted;
		}

		#region Market Order / BE / Revert (ported from KatTradeManager)
		private DateTime lastEntrySubmitTime = DateTime.MinValue;
		private const double EntryDebounceMs = 500;
		private enum RevertAction { None = 0, Buy = 1, Sell = 2 }
		private int pendingRevertAction;   // 0 = none, 1 = Buy, 2 = Sell
		private int pendingRevertQuantity;
		private int pendingRevertSubmitInFlight;
		private int closeInFlight;

		// ponytail: simplified from TradeManager's QueueAccountOperation — scalper uses direct submit
		private bool IsEntryDebounced()
		{
			if ((DateTime.Now - lastEntrySubmitTime).TotalMilliseconds < EntryDebounceMs) return true;
			lastEntrySubmitTime = DateTime.Now;
			return false;
		}

		private Position GetInstrumentPosition()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null) return null;
			var positions = acc.Positions;
			try
			{
				lock (positions)
				{
					foreach (Position p in positions)
						if (p != null && p.Instrument != null && p.Instrument.FullName == Instrument.FullName)
							return p;
				}
			}
			catch { }
			return null;
		}

		private bool IsCloseInFlight()
		{
			return System.Threading.Volatile.Read(ref closeInFlight) != 0;
		}

		private void CancelWorkingOrdersForInstrument(Account acc)
		{
			if (acc == null || Instrument == null || acc.Orders == null) return;
			System.Collections.Generic.List<Order> toCancel = new System.Collections.Generic.List<Order>();
			try
			{
				lock (acc.Orders)
				{
					foreach (Order o in acc.Orders)
					{
						if (o == null || o.Instrument == null || o.Instrument.FullName != Instrument.FullName) continue;
						if (IsActiveOrderState(o.OrderState))
							toCancel.Add(o);
					}
				}
				if (toCancel.Count > 0)
				{
					foreach (Order o in toCancel)
					{
						try { acc.Cancel(new[] { o }); } catch { }
					}
					Print(string.Format("[Kat34Scalper] Cancelled {0} working order(s) for {1}.", toCancel.Count, Instrument.FullName));
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error cancelling working orders: {0}", ex.Message));
			}
		}

		private void CleanupFlatOrphans()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null || acc.Orders == null) return;

			Position pos = GetInstrumentPosition();
			if (pos != null && pos.MarketPosition != MarketPosition.Flat) return;

			System.Collections.Generic.List<Order> orphans = new System.Collections.Generic.List<Order>();
			try
			{
				lock (acc.Orders)
				{
					foreach (Order o in acc.Orders)
					{
						if (o == null || o.Instrument == null || o.Instrument.FullName != Instrument.FullName) continue;
						if (!IsActiveOrderState(o.OrderState)) continue;
						if (pendingOrder != null && o == pendingOrder) continue;
						orphans.Add(o);
					}
				}

				if (orphans.Count > 0)
				{
					foreach (Order orphan in orphans)
					{
						try { acc.Cancel(new[] { orphan }); } catch { }
					}
					Print(string.Format("[Kat34Scalper] Flat cleanup: cancelled {0} orphan working order(s).", orphans.Count));
				}
			}
			catch { }
		}

		private Account subscribedAccount;

		private void EnsureAccountEventSubscription()
		{
			Account acc = ResolveBotAccount();
			if (ReferenceEquals(subscribedAccount, acc)) return;
			RemoveAccountEventSubscription();
			subscribedAccount = acc;
			if (subscribedAccount != null)
			{
				try { subscribedAccount.OrderUpdate += OnAccountOrderUpdate; } catch { }
			}
		}

		private void RemoveAccountEventSubscription()
		{
			if (subscribedAccount != null)
			{
				try { subscribedAccount.OrderUpdate -= OnAccountOrderUpdate; } catch { }
			}
			subscribedAccount = null;
			ClearAtmStartup();
			ResetAtmScaleInTracking();
		}

		private void ProcessAtmStartupUpdate(Order observed)
		{
			if (observed == null) return;
			lock (atmScaleInLock)
			{
				if (SameOrder(atmStartupOrder, observed))
					atmLastLifecycleActivityUtc = DateTime.UtcNow;
			}
		}

		private void OnAccountOrderUpdate(object sender, OrderEventArgs e)
		{
			try
			{
				Order observed = e != null ? e.Order : null;
				if (observed == null) return;
				ProcessAtmStartupUpdate(observed);
				if (Instrument == null || observed.Instrument == null || observed.Instrument.FullName != Instrument.FullName) return;
				ScheduleAtmBracketMerge();
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Account order update error: {0}", ex.Message));
			}
		}

		private bool PlaceMarketOrder(OrderAction action)
		{
			return PlaceMarketOrder(action, 0);
		}

		private bool PlaceMarketOrder(OrderAction action, int quantityOverride)
		{
			Print(string.Format("[Kat34Scalper] PlaceMarketOrder click: {0}", action));
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null)
			{
				ShowHudStatus("Market order: no account", Brushes.OrangeRed);
				return false;
			}

			if (IsDailyRiskBreached(out string breachReason))
			{
				Print(string.Format("[Kat34Scalper] Market Order REJECTED by Daily Risk: {0}", breachReason));
				ShowHudStatus(breachReason, Brushes.OrangeRed);
				return false;
			}

			if (IsEntryDebounced())
			{
				Print("[Kat34Scalper] Duplicate market order ignored (debounce).");
				return false;
			}

			try
			{
				int qty = quantityOverride > 0 ? quantityOverride : GetEffectiveBotQuantity();
				string tpl = cachedBotAtm;
				bool hasAtm = HasAtmTemplate(tpl);
				string entryName = hasAtm ? "Entry" : (action == OrderAction.Buy ? "MarketBuy" : "MarketSell");

				Order order = acc.CreateOrder(Instrument, action, OrderType.Market, OrderEntry.Manual,
					TimeInForce.Gtc, qty, 0, 0, "", entryName, NinjaTrader.Core.Globals.MaxDate, null);
				if (order != null)
				{
					TrackAtmStartup(order);
					if (hasAtm)
						NinjaTrader.NinjaScript.AtmStrategy.StartAtmStrategy(tpl, order);
					else
						acc.Submit(new[] { order });
					ScheduleAtmBracketMerge();
					Print(string.Format("[Kat34Scalper] Market order submitted: {0} qty={1} atm={2}", action, qty, hasAtm ? tpl : "none"));
					ShowHudStatus(string.Format("{0} market order submitted", action), Brushes.LightGreen);
					return true;
				}
				Print(string.Format("[Kat34Scalper] Market order creation returned null: {0} qty={1}", action, qty));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error placing market order: {0}", ex.Message));
			}
			return false;
		}

		private void SetBreakeven()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null)
			{
				ShowHudStatus("BE: no account", Brushes.OrangeRed);
				return;
			}
			try
			{
				Position pos = GetInstrumentPosition();
				if (pos == null || pos.MarketPosition == MarketPosition.Flat)
				{
					Print("[Kat34Scalper] BE: No active position.");
					ShowHudStatus("BE: no active position", Brushes.OrangeRed);
					return;
				}

				double tickSize = Instrument.MasterInstrument.TickSize;
				bool isLong = pos.MarketPosition == MarketPosition.Long;
				double bePrice = Kat34ScalperLogic.CalculateBreakevenPrice(isLong, pos.AveragePrice, cachedBotBufferTicks, tickSize);

				// Underwater check: BE stop on wrong side of market → broker rejection
				double livePrice = 0;
				try { livePrice = Closes[0][0]; } catch { }
				if (livePrice > 0 && !Kat34ScalperLogic.IsStopOnValidSide(isLong, bePrice, livePrice))
				{
					Print(string.Format("[Kat34Scalper] BE skipped: stop {0} invalid vs market {1}.", bePrice, livePrice));
					ShowHudStatus(string.Format("BE skipped: stop {0} invalid", bePrice), Brushes.OrangeRed);
					return;
				}

				// Find existing stop orders to move
				System.Collections.Generic.List<Order> workingStops = new System.Collections.Generic.List<Order>();
				if (acc.Orders != null)
				{
					foreach (Order o in acc.Orders)
					{
						if (o == null || o.Instrument != Instrument || !IsActiveOrderState(o.OrderState)) continue;
						if (o.OrderType != OrderType.StopMarket && o.OrderType != OrderType.StopLimit) continue;
						bool isProtective = isLong
							? (o.OrderAction == OrderAction.Sell || o.OrderAction == OrderAction.SellShort)
							: (o.OrderAction == OrderAction.Buy || o.OrderAction == OrderAction.BuyToCover);
						if (isProtective) workingStops.Add(o);
					}
				}

				if (workingStops.Count > 0)
				{
					foreach (Order stop in workingStops)
						stop.StopPriceChanged = bePrice;
					acc.Change(workingStops.ToArray());
					Print(string.Format("[Kat34Scalper] Moved {0} stop(s) to BE @ {1} (buffer {2} ticks)", workingStops.Count, bePrice, cachedBotBufferTicks));
					ShowHudStatus(string.Format("BE stop moved @ {0}", bePrice), Brushes.LightGreen);
				}
				else
				{
					OrderAction slAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
					Order slOrder = acc.CreateOrder(Instrument, slAction, OrderType.StopMarket, OrderEntry.Manual,
						TimeInForce.Gtc, pos.Quantity, 0, bePrice, "", "KAT_SL_BE", NinjaTrader.Core.Globals.MaxDate, null);
					if (slOrder != null)
					{
						acc.Submit(new[] { slOrder });
						Print(string.Format("[Kat34Scalper] BE stop submitted @ {0} (buffer {1} ticks)", bePrice, cachedBotBufferTicks));
						ShowHudStatus(string.Format("BE stop submitted @ {0}", bePrice), Brushes.LightGreen);
					}
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error setting BE: {0}", ex.Message));
			}
		}

		private void RevertPosition()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null)
			{
				ShowHudStatus("Revert: no account", Brushes.OrangeRed);
				return;
			}
			try
			{
				if (IsCloseInFlight())
				{
					Print("[Kat34Scalper] Revert: close already in flight.");
					return;
				}

				Position pos = GetInstrumentPosition();
				if (pos == null || pos.MarketPosition == MarketPosition.Flat)
				{
					Print("[Kat34Scalper] Revert: no active position.");
					ShowHudStatus("Revert: no active position", Brushes.OrangeRed);
					return;
				}

				OrderAction oppositeAction = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
				int revertQty = pos.Quantity;
				System.Threading.Interlocked.Exchange(ref pendingRevertAction, (int)(oppositeAction == OrderAction.Buy ? RevertAction.Buy : RevertAction.Sell));
				System.Threading.Interlocked.Exchange(ref pendingRevertQuantity, revertQty);

				// Close current position first
				System.Threading.Interlocked.Exchange(ref closeInFlight, 1);
				OrderAction closeAction = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
				try
				{
					// Cancel existing orders first
					if (acc.Orders != null)
						foreach (Order o in acc.Orders)
							if (o != null && o.Instrument == Instrument && IsActiveOrderState(o.OrderState))
								try { acc.Cancel(new[] { o }); } catch { }

					Order closeOrder = acc.CreateOrder(Instrument, closeAction, OrderType.Market, OrderEntry.Manual,
						TimeInForce.Gtc, pos.Quantity, 0, 0, "", "KAT_REVERT_CLOSE", NinjaTrader.Core.Globals.MaxDate, null);
					if (closeOrder != null)
						acc.Submit(new[] { closeOrder });
				}
				catch (Exception ex)
				{
					System.Threading.Interlocked.Exchange(ref closeInFlight, 0);
					Print(string.Format("[Kat34Scalper] Revert close error: {0}", ex.Message));
					return;
				}

				Print(string.Format("[Kat34Scalper] Revert queued: close qty={0}, then enter {1} qty={0}.", revertQty, oppositeAction));
				ShowHudStatus(string.Format("Revert: closing → {0}", oppositeAction), Brushes.LightGreen);
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error reverting: {0}", ex.Message));
			}
		}

		// Called from ManageBotEntry on each bar update — completes the revert after the close fills
		private void TrySubmitPendingRevert()
		{
			int reqAction = System.Threading.Volatile.Read(ref pendingRevertAction);
			int reqQty = System.Threading.Volatile.Read(ref pendingRevertQuantity);
			if (reqAction == 0) return;

			// Check if close is done
			Position pos = GetInstrumentPosition();
			if (pos != null && pos.MarketPosition != MarketPosition.Flat)
			{
				// Still closing — check if it's the revert close
				Account acc = ResolveBotAccount();
				if (acc != null && acc.Orders != null)
				{
					bool hasRevertClose = false;
					foreach (Order o in acc.Orders)
						if (o != null && o.Name == "KAT_REVERT_CLOSE" && IsActiveOrderState(o.OrderState))
						{ hasRevertClose = true; break; }
					if (!hasRevertClose)
						System.Threading.Interlocked.Exchange(ref closeInFlight, 0);
				}
				return;
			}

			System.Threading.Interlocked.Exchange(ref closeInFlight, 0);
			if (reqQty <= 0)
			{
				System.Threading.Interlocked.Exchange(ref pendingRevertAction, 0);
				return;
			}

			if (System.Threading.Interlocked.CompareExchange(ref pendingRevertSubmitInFlight, 1, 0) != 0)
				return;
			try
			{
				OrderAction action = reqAction == (int)RevertAction.Buy ? OrderAction.Buy : OrderAction.Sell;
				if (PlaceMarketOrder(action, reqQty))
				{
					System.Threading.Interlocked.Exchange(ref pendingRevertAction, 0);
					System.Threading.Interlocked.Exchange(ref pendingRevertQuantity, 0);
					ShowHudStatus(string.Format("Revert: {0} {1}ct submitted", action, reqQty), Brushes.LightGreen);
				}
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref pendingRevertSubmitInFlight, 0);
			}
		}
		#endregion

		public void FlattenAllPositions()
		{
			Account acc = ResolveBotAccount();
			if (acc == null)
			{
				ShowHudStatus("Flatten: no account selected", Brushes.OrangeRed);
				Print("[Kat34Scalper] Flatten: no account selected.");
				return;
			}

			try
			{
				// Cancel pending bot entries
				CancelPendingBotOrder("Close/flatten clicked");

				// Cancel all active working orders on the selected account for this instrument
				if (acc.Orders != null)
				{
					foreach (Order order in acc.Orders)
					{
						if (order == null || order.Instrument == null || Instrument == null) continue;
						if (order.Instrument.FullName != Instrument.FullName) continue;
						if (IsActiveOrderState(order.OrderState))
						{
							try { acc.Cancel(new[] { order }); } catch { }
						}
					}
				}

				// Market close all non-flat positions on the account
				if (acc.Positions != null)
				{
					foreach (Position pos in acc.Positions)
					{
						if (pos != null && pos.MarketPosition != MarketPosition.Flat)
						{
							OrderAction action = pos.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
							try
							{
								Order closeOrder = acc.CreateOrder(pos.Instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Gtc, pos.Quantity, 0, 0, "", "KAT_CLOSE", NinjaTrader.Core.Globals.MaxDate, null);
								if (closeOrder != null)
									acc.Submit(new[] { closeOrder });
							}
							catch (Exception ex)
							{
								Print(string.Format("[Kat34Scalper] Error submitting close for {0}: {1}", pos.Instrument != null ? pos.Instrument.FullName : "unknown", ex.Message));
							}
						}
					}
				}

				ShowHudStatus("Close/flatten executed", Brushes.OrangeRed);
				Print("[Kat34Scalper] Close/flatten executed: cancelled orders & closed positions.");
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Error flattening account: {0}", ex.Message));
			}
		}
	}
}

