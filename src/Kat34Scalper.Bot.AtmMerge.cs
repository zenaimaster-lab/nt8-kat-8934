/*
 * Kat34Scalper.Bot.AtmMerge.cs — Bot ATM MERGE module (partial class Kat34Scalper).
 * Always-ON reconciliation: anchor resize to position qty, duplicate/stale cancel, flat cleanup grace.
 * Ported from TradeManager, extracted from Kat34Scalper.Bot.cs v0.96 audit.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using Kat34Scalper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		private readonly object atmScaleInLock = new object();
		private Order atmStartupOrder;
		private DateTime atmLastLifecycleActivityUtc = DateTime.MinValue;
		private bool atmPositionWasConfirmedThisEpisode;
		private const double AtmLifecycleGraceMilliseconds = 3000.0;

		private Order atmMergeStopAnchor;
		private Order atmMergeTargetAnchor;
		private int atmMergeScheduled;

		private static bool SameOrder(Order left, Order right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null) return false;
			return !string.IsNullOrEmpty(left.OrderId) && left.OrderId == right.OrderId;
		}

		private static bool IsMergeCandidateState(OrderState state)
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
				|| state == OrderState.Suspended;
		}

		private static bool HasAtmBracketName(Order order)
		{
			if (order == null || string.IsNullOrEmpty(order.Name)) return false;
			string name = order.Name;
			return name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool HasAtmEntrySignal(Order order)
		{
			return order != null
				&& !string.IsNullOrEmpty(order.FromEntrySignal)
				&& order.FromEntrySignal.IndexOf("entry", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private bool IsKnownAtmBracket(Order order)
		{
			if (order == null) return false;
			lock (atmScaleInLock)
			{
				if (ReferenceEquals(order, atmMergeStopAnchor) || ReferenceEquals(order, atmMergeTargetAnchor))
					return true;
				if (atmMergeStopAnchor != null && !string.IsNullOrEmpty(atmMergeStopAnchor.Oco)
					&& string.Equals(atmMergeStopAnchor.Oco, order.Oco, StringComparison.Ordinal))
					return true;
				if (atmMergeTargetAnchor != null && !string.IsNullOrEmpty(atmMergeTargetAnchor.Oco)
					&& string.Equals(atmMergeTargetAnchor.Oco, order.Oco, StringComparison.Ordinal))
					return true;
				return false;
			}
		}

		private bool IsAtmBracketCandidate(Order order)
		{
			if (order == null || Instrument == null || order.Instrument != Instrument) return false;
			if (IsManualExitOrder(order) || !IsMergeCandidateState(order.OrderState)) return false;
			if (order.OrderType != OrderType.StopMarket
				&& order.OrderType != OrderType.StopLimit
				&& order.OrderType != OrderType.Limit)
				return false;
			return HasAtmBracketName(order) || HasAtmEntrySignal(order) || IsKnownAtmBracket(order);
		}

		private static bool IsAtmExitAction(OrderAction action, MarketPosition position)
		{
			return position == MarketPosition.Long
				? action == OrderAction.Sell || action == OrderAction.SellShort
				: action == OrderAction.Buy || action == OrderAction.BuyToCover;
		}

		private static bool IsManualExitOrder(Order order)
		{
			return order != null
				&& !string.IsNullOrEmpty(order.Name)
				&& order.Name.StartsWith("KAT_", StringComparison.OrdinalIgnoreCase);
		}

		private void TrackAtmStartup(Order order)
		{
			if (order == null) return;
			lock (atmScaleInLock)
			{
				atmStartupOrder = order;
				atmLastLifecycleActivityUtc = DateTime.UtcNow;
				atmPositionWasConfirmedThisEpisode = false;
			}
		}

		private void ClearAtmStartup(Order expected = null)
		{
			lock (atmScaleInLock)
			{
				if (expected == null || SameOrder(atmStartupOrder, expected))
					atmStartupOrder = null;
			}
		}

		private bool IsAtmStartupPending()
		{
			Order startup;
			DateTime lastActivity;
			lock (atmScaleInLock)
			{
				startup = atmStartupOrder;
				lastActivity = atmLastLifecycleActivityUtc;
			}
			if (startup == null) return false;
			if (IsActiveOrderState(startup.OrderState))
				return true;
			if (lastActivity == DateTime.MinValue) return true;
			return (DateTime.UtcNow - lastActivity).TotalMilliseconds < AtmLifecycleGraceMilliseconds;
		}

		private void ResetAtmScaleInTracking()
		{
			lock (atmScaleInLock)
			{
				atmMergeStopAnchor = null;
				atmMergeTargetAnchor = null;
				atmStartupOrder = null;
				atmLastLifecycleActivityUtc = DateTime.MinValue;
				atmPositionWasConfirmedThisEpisode = false;
			}
		}

		private void ScheduleAtmBracketMerge()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null) return;
			if (System.Threading.Interlocked.CompareExchange(ref atmMergeScheduled, 1, 0) != 0) return;

			Action merge = () =>
			{
				try
				{
					MergeAtmBrackets();
				}
				catch (Exception ex)
				{
					Print(string.Format("[Kat34Scalper] ATM MERGE execution failed: {0}", ex.Message));
				}
				finally
				{
					System.Threading.Interlocked.Exchange(ref atmMergeScheduled, 0);
				}
			};

			try
			{
				if (ChartControl != null && ChartControl.Dispatcher != null)
					ChartControl.Dispatcher.BeginInvoke(merge);
				else
					merge();
			}
			catch
			{
				System.Threading.Interlocked.Exchange(ref atmMergeScheduled, 0);
			}
		}

		private void MergeAtmBrackets()
		{
			Account acc = ResolveBotAccount();
			if (acc == null || Instrument == null) return;

			try
			{
				Position position = GetInstrumentPosition();
				List<Order> candidates = new List<Order>();
				if (acc.Orders != null)
				{
					lock (acc.Orders)
					{
						foreach (Order o in acc.Orders)
							if (IsAtmBracketCandidate(o)) candidates.Add(o);
					}
				}

				bool positionConfirmed = position != null && position.MarketPosition != MarketPosition.Flat;
				if (positionConfirmed)
				{
					lock (atmScaleInLock)
					{
						if (!atmPositionWasConfirmedThisEpisode)
							atmLastLifecycleActivityUtc = DateTime.UtcNow;
						atmPositionWasConfirmedThisEpisode = true;
					}
					ClearAtmStartup();
				}

				if (!positionConfirmed)
				{
					bool startupPending = IsAtmStartupPending();
					bool wasPositionConfirmed;
					DateTime lastActivity;
					lock (atmScaleInLock)
					{
						wasPositionConfirmed = atmPositionWasConfirmedThisEpisode;
						lastActivity = atmLastLifecycleActivityUtc;
					}

					double activityAge = lastActivity == DateTime.MinValue
						? -1
						: (DateTime.UtcNow - lastActivity).TotalMilliseconds;

					if (Kat34ScalperLogic.ShouldDeferAtmFlatCleanup(
						startupPending,
						false,
						wasPositionConfirmed,
						activityAge,
						AtmLifecycleGraceMilliseconds))
					{
						return;
					}

					if (candidates.Count > 0)
					{
						foreach (Order c in candidates)
							try { acc.Cancel(new[] { c }); } catch { }
						Print(string.Format("[Kat34Scalper] ATM MERGE flat cleanup: cancelled {0} bracket(s).", candidates.Count));
					}
					ResetAtmScaleInTracking();
					return;
				}

				List<Order> brackets = candidates
					.Where(o => IsAtmExitAction(o.OrderAction, position.MarketPosition))
					.ToList();
				List<Order> staleOppositeBrackets = candidates
					.Where(o => !IsAtmExitAction(o.OrderAction, position.MarketPosition))
					.ToList();

				if (staleOppositeBrackets.Count > 0)
				{
					foreach (Order stale in staleOppositeBrackets)
						try { acc.Cancel(new[] { stale }); } catch { }
					Print(string.Format("[Kat34Scalper] ATM MERGE: cancelled {0} stale opposite bracket(s).", staleOppositeBrackets.Count));
				}

				List<Order> stops = brackets
					.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
					.ToList();
				List<Order> targets = brackets
					.Where(o => o.OrderType == OrderType.Limit)
					.ToList();

				Order stopAnchor;
				Order targetAnchor;
				lock (atmScaleInLock)
				{
					stopAnchor = atmMergeStopAnchor != null && stops.Contains(atmMergeStopAnchor)
						? atmMergeStopAnchor
						: stops.FirstOrDefault();
					targetAnchor = atmMergeTargetAnchor != null && targets.Contains(atmMergeTargetAnchor)
						? atmMergeTargetAnchor
						: targets.FirstOrDefault();
					atmMergeStopAnchor = stopAnchor;
					atmMergeTargetAnchor = targetAnchor;
				}

				List<Order> changes = new List<Order>();
				if (stopAnchor != null && stopAnchor.Quantity != position.Quantity)
				{
					stopAnchor.QuantityChanged = position.Quantity;
					changes.Add(stopAnchor);
				}
				if (targetAnchor != null && targetAnchor.Quantity != position.Quantity)
				{
					targetAnchor.QuantityChanged = position.Quantity;
					changes.Add(targetAnchor);
				}

				if (changes.Count > 0)
				{
					acc.Change(changes.ToArray());
					Print(string.Format("[Kat34Scalper] ATM MERGE: resized {0} anchor order(s) to canonical qty {1}.", changes.Count, position.Quantity));
				}

				List<Order> duplicates = stops
					.Where(o => o != stopAnchor)
					.Concat(targets.Where(o => o != targetAnchor))
					.ToList();

				if (duplicates.Count > 0)
				{
					foreach (Order dup in duplicates)
						try { acc.Cancel(new[] { dup }); } catch { }
				}

				int removedCount = duplicates.Count + staleOppositeBrackets.Count;
				if (changes.Count > 0 || removedCount > 0)
				{
					Print(string.Format("[Kat34Scalper] ATM MERGE reconciled: posQty={0} stop={1} target={2} changed={3} removed={4}",
						position.Quantity,
						stopAnchor != null ? stopAnchor.OrderType.ToString() : "none",
						targetAnchor != null ? targetAnchor.OrderType.ToString() : "none",
						changes.Count,
						removedCount));
				}
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] ATM MERGE reconciliation failed: {0}", ex.Message));
			}
		}
	}
}
