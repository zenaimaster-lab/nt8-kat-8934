/*
 * Kat34Scalper.Draw.cs — Draw module (partial class Kat34Scalper).
 * Everything visual: signal drawings (entry/SL/TP + ATM BE/SL1/SL2 trigger lines,
 * arrows, labels), the version/timeframe label, alert sounds, and the HUD panel.
 * HUD sections are titled by the module they control: ACCOUNT / BOT / SIGNAL / FILTER / DRAW.
 * Redesign v0.95: full TradeManager pixel-perfect port (HudGap 2, HudPanelWidth 250→238 inner, UseLayoutRounding, templates)
 * v0.98: ACCOUNT top black board (NYT time, acct/balance/Day/U/R, BOT/B1/B2/POS) — TradeManager AccountInfo port
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
		#region Signal Drawings (lines, arrows, labels, version label, alert)
		private const int MAX_SIGNAL_RECORDS = 200;
		private sealed class KatSignalRecord
		{
			public int Bar;
			public bool IsBuy;
			public string Owner; // "A1" or future "A2" etc. — enables per-signal ON/OFF cleanup
			public double ArrowY;
			public double TextY;
			public double Candidate1;
			public double Candidate2;
			public double EntryPrice;
			public double SlPrice;
			public double TpPrice;
			public double BePrice;
			public double Sl1Price;
			public double Sl2Price;
			public bool DrawLogged;
			public bool KeepAlive; // A2 pending entry: lines render while the setup is alive, ignoring the Line Length fade
		}
		private readonly List<KatSignalRecord> signalRecords = new List<KatSignalRecord>();
		private bool legacySignalDrawingsCleared;
		// Arrow/Text feature removed per request. Only lines + ATM triggers remain.

		private void PlayAlertSound()
		{
			try
			{
				string userDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds");
				string installDir = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds");
				string path = Kat34ScalperSound.ResolvePath(userDir, installDir, AlertSound);
				if (path != null) PlaySound(path);
			}
			catch { }
		}
		private int SafeLineLengthBars()
		{
			return Math.Max(1, Math.Min(LineLengthBars, 500));
		}

		private int SafeLineWidth()
		{
			return Math.Max(1, Math.Min(LineWidth, 10));
		}

		private string SignalTag(KatSignalRecord record, string suffix)
		{
			string mod = string.IsNullOrEmpty(record.Owner) ? "B1" : record.Owner;
			return "K34S_" + mod + "_" + (record.IsBuy ? "B" : "S") + "_" + suffix + "_" + record.Bar;
		}

		private void RenderSignal(KatSignalRecord record)
		{
			int age = CurrentBars[0] - record.Bar;
			if (age < 0) return;
			// Per-signal ownership: if owner disabled, skip (OFF already removed its drawings)
			string owner = record.Owner ?? "B1";
			if (owner == "B1" && !cachedB1) return;
			if (owner == "B2" && !cachedB2) return;
			if (owner == "A2" && !cachedAlertA2) return;

			// ponytail: cache brushes per frame to avoid 200* allocations per bar; fade 0.35 handled via opacity
			Brush entryBrush = record.IsBuy ? new SolidColorBrush(BuyEntryLineColor) { Opacity = 1 } : new SolidColorBrush(SellEntryLineColor) { Opacity = 1 };
			if (entryBrush.CanFreeze) entryBrush.Freeze();
			Brush slBrush = new SolidColorBrush(SLLineColor);
			if (slBrush.CanFreeze) slBrush.Freeze();
			Brush tpBrush = new SolidColorBrush(TPLineColor);
			if (tpBrush.CanFreeze) tpBrush.Freeze();
			Brush textBrush = new SolidColorBrush(record.IsBuy ? BuyTextColor : SellTextColor);
			if (textBrush.CanFreeze) textBrush.Freeze();
			int lineLength = SafeLineLengthBars();
			int width = SafeLineWidth();

			// Arrows + BUY/SELL text removed. Only lines + ATM triggers (BE/SL1/SL2) render.
			// KeepAlive (A2 pending entry): no age cap — the lines live until Cancel/Filled.
			if (age <= lineLength || record.KeepAlive)
			{
				if (record.Candidate1 != record.Candidate2)
				{
					Brush faded = new SolidColorBrush(record.IsBuy ? BuyEntryLineColor : SellEntryLineColor) { Opacity = 0.35 };
					Draw.Line(this, SignalTag(record, "C1"), false, age, record.Candidate1, 0, record.Candidate1, faded, DashStyleHelper.Dot, 1);
					Draw.Line(this, SignalTag(record, "C2"), false, age, record.Candidate2, 0, record.Candidate2, faded, DashStyleHelper.Dot, 1);
				}
				else
				{
					RemoveDrawObject(SignalTag(record, "C1"));
					RemoveDrawObject(SignalTag(record, "C2"));
				}

				Draw.Line(this, SignalTag(record, "ENTRY"), false, age, record.EntryPrice, 0, record.EntryPrice, entryBrush, DashStyleHelper.Solid, width);
				Draw.Line(this, SignalTag(record, "SL"), false, age, record.SlPrice, 0, record.SlPrice, slBrush, DashStyleHelper.Dash, width);
				Draw.Line(this, SignalTag(record, "TP"), false, age, record.TpPrice, 0, record.TpPrice, tpBrush, DashStyleHelper.Dash, width);
				if (record.BePrice != 0)
					Draw.Line(this, SignalTag(record, "BE"), false, age, record.BePrice, 0, record.BePrice, Brushes.DeepSkyBlue, DashStyleHelper.DashDot, 1);
				if (record.Sl1Price != 0)
					Draw.Line(this, SignalTag(record, "SL1"), false, age, record.Sl1Price, 0, record.Sl1Price, Brushes.Orange, DashStyleHelper.Dot, 1);
				if (record.Sl2Price != 0)
					Draw.Line(this, SignalTag(record, "SL2"), false, age, record.Sl2Price, 0, record.Sl2Price, Brushes.Magenta, DashStyleHelper.Dot, 1);

				string labelText = string.Format("{0} {1}", record.IsBuy ? "Buy" : "Sell", owner);
				Draw.Text(this, SignalTag(record, "TEXT"), labelText, age, record.TextY, textBrush);
			}
			if (!record.DrawLogged)
			{
				record.DrawLogged = true;
				Print(string.Format("[Kat34Scalper][DRAW] record bar={0}, side={1}, age={2}, entry={3:F5}, sl={4:F5}, tp={5:F5}, lineLength={6}, tags={7}_ENTRY/{7}_SL/{7}_TP",
					record.Bar, record.IsBuy ? "BUY" : "SELL", age, record.EntryPrice, record.SlPrice, record.TpPrice,
					lineLength, "K34S_" + (record.IsBuy ? "B" : "S") + "_"));
			}
		}

		private void RefreshSignalDrawings()
		{
			foreach (KatSignalRecord record in signalRecords)
				RenderSignal(record);
		}

		private void ClearLegacySignalDrawings()
		{
			if (legacySignalDrawingsCleared) return;
			legacySignalDrawingsCleared = true;
			try
			{
				var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					string tag = tool.Tag as string;
					if (name != null && name.StartsWith("K8934_", StringComparison.OrdinalIgnoreCase))
						doomed.Add(name);
					if (tag != null && tag.StartsWith("K8934_", StringComparison.OrdinalIgnoreCase))
						doomed.Add(tag);
				}
				foreach (string tag in doomed)
					RemoveDrawObject(tag);
				if (doomed.Count > 0)
					Print(string.Format("[Kat34Scalper] Removed {0} stale Kat8934 drawing(s).", doomed.Count));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Legacy drawing cleanup error: {0}", ex.Message));
			}
		}

		// replay = true during a History Days backfill pass: same drawing, no alert sound, no bot order.
		// owner = signal module id ("A1", "A2"...) for per-signal ON/OFF cleanup ownership.
		// Returns the created record so the owning signal can migrate/cancel it later (A2).
		private KatSignalRecord DrawSignal(bool isBuy, int bar, double high, double low, double c1, double c2, int offsetTicks, int stopTicks, int targetTicks, bool replay = false, string owner = "A1")
		{
			if (signalRecords.Count >= MAX_SIGNAL_RECORDS)
				signalRecords.RemoveAt(0);
			KatSignalRecord record = new KatSignalRecord { Owner = owner };
			FillSignalRecord(record, isBuy, bar, high, low, c1, c2, offsetTicks, stopTicks, targetTicks);
			signalRecords.Add(record);
			RenderSignal(record);

			if (!replay)
				PlayAlertSound();
			Print(string.Format("[Kat34Scalper][{6}][DRAW]{3} {0} signal @ bar {1} — entry {2:F5}, SL {4:F5}, TP {5:F5}", isBuy ? "BUY" : "SELL", bar, record.EntryPrice, replay ? "[replay]" : "", record.SlPrice, record.TpPrice, owner ?? "A1"));
			return record;
		}

		// Computes every price level (entry, candidates, SL/TP from ATM or settings, BE/SL1/SL2
		// triggers) and stores them on the record. Shared by DrawSignal (new signal) and the A2
		// migration (same record, new bar + better extreme — call RemoveSignalRecordDrawings first).
		private void FillSignalRecord(KatSignalRecord record, bool isBuy, int bar, double high, double low, double c1, double c2, int offsetTicks, int stopTicks, int targetTicks)
		{
			double tick = TickSize;

			// A1 dual entry: c1 = U-turn bar extreme, c2 = best later candidate (0 = none yet — fall back to the signal bar).
			double ref1 = c1 != 0 ? c1 : (isBuy ? high : low);
			double ref2 = c2 != 0 ? c2 : ref1;
			double entryPrice = Kat34ScalperLogic.EffectiveEntry(isBuy, ref1, ref2, offsetTicks, tick);
			double cand1 = isBuy ? ref1 + offsetTicks * tick : ref1 - offsetTicks * tick;
			double cand2 = isBuy ? ref2 + offsetTicks * tick : ref2 - offsetTicks * tick;

			// TradeManager-style levels: SL/TP come from the selected ATM template when it defines them,
			// otherwise from the indicator settings; BE/SL1/SL2 trailing-SL triggers exist only with an ATM.
			Kat34ScalperAtmData atm = GetAtmData();
			int slTicks = atm.StopLoss > 0 ? atm.StopLoss : stopTicks;
			int tpTicks = atm.Target > 0 ? atm.Target : targetTicks;

			// Trailing-SL trigger lines from the ATM template — same style as KatTradeManager
			// (BE DeepSkyBlue dash-dot, SL1 orange dot, SL2 magenta dot, 1 px, profit side of entry).
			int dir = isBuy ? 1 : -1;
			double bePrice = 0;
			double sl1Price = 0;
			double sl2Price = 0;
			if (atm.BETrigger > 0)
				bePrice = entryPrice + dir * atm.BETrigger * tick;
			if (atm.SL1Trigger > 0)
				sl1Price = entryPrice + dir * atm.SL1Trigger * tick;
			if (atm.SL2Trigger > 0)
				sl2Price = entryPrice + dir * atm.SL2Trigger * tick;

			record.Bar = bar;
			record.IsBuy = isBuy;
			record.ArrowY = isBuy ? low - ArrowOffsetTicks * tick : high + ArrowOffsetTicks * tick;
			record.TextY = isBuy ? entryPrice - tick : entryPrice + tick; // buy label below line, sell above
			record.Candidate1 = cand1;
			record.Candidate2 = cand2;
			record.EntryPrice = entryPrice;
			record.SlPrice = isBuy ? entryPrice - slTicks * tick : entryPrice + slTicks * tick;
			record.TpPrice = isBuy ? entryPrice + tpTicks * tick : entryPrice - tpTicks * tick;
			record.BePrice = bePrice;
			record.Sl1Price = sl1Price;
			record.Sl2Price = sl2Price;
		}

		// Removes every draw object a signal record owns (entry/SL/TP/candidates/ATM triggers).
		// Tags derive from record.Bar — call BEFORE updating the bar on a migration.
		private void RemoveSignalRecordDrawings(KatSignalRecord record)
		{
			RemoveDrawObject(SignalTag(record, "C1"));
			RemoveDrawObject(SignalTag(record, "C2"));
			RemoveDrawObject(SignalTag(record, "ENTRY"));
			RemoveDrawObject(SignalTag(record, "SL"));
			RemoveDrawObject(SignalTag(record, "TP"));
			RemoveDrawObject(SignalTag(record, "BE"));
			RemoveDrawObject(SignalTag(record, "SL1"));
			RemoveDrawObject(SignalTag(record, "SL2"));
			RemoveDrawObject(SignalTag(record, "TEXT"));
		}

		private void ClearSignalDrawings(string owner)
		{
			if (string.IsNullOrEmpty(owner)) return;
			signalRecords.RemoveAll(r => (r.Owner ?? "A1").Equals(owner, StringComparison.OrdinalIgnoreCase));
			RemoveModuleDrawings("K34S_" + owner.ToUpperInvariant() + "_");
		}

		// Removes a record's draw objects and drops it from the list (A2 cancel).
		private void RemoveSignalRecord(KatSignalRecord record)
		{
			RemoveSignalRecordDrawings(record);
			signalRecords.Remove(record);
		}

		// Removes every draw object whose tag starts with the given prefix (data thread only).
		// Used by the signal sub-modules when they are switched OFF (independence: only their own tags).
		private void RemoveModuleDrawings(string prefix)
		{
			try
			{
				var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					string tag = tool.Tag as string;
					if (name != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						doomed.Add(name);
					if (tag != null && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						doomed.Add(tag);
				}
				foreach (string tag in doomed)
					RemoveDrawObject(tag);
				if (doomed.Count > 0)
					Print(string.Format("[Kat34Scalper] Removed {0} drawing(s) with prefix {1}.", doomed.Count, prefix));
				ForceRefresh();
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Remove module drawings error ({0}): {1}", prefix, ex.Message));
			}
		}


		// Called from the data thread through TriggerCustomEvent from HUD clicks.
		private void ClearOldSignalDrawings()
		{
			try
			{
				signalRecords.Clear();

				// Reset sub-module pending drawing states so orphaned tags/records aren't retained
				b1SellRecord = null;
				b1BuyRecord = null;
				b1SellTextTag = null;
				b1BuyTextTag = null;
				b1SellState.Reset();
				b1BuyState.Reset();
				b2SellState.Reset();
				b2BuyState.Reset();
				signalInTradeMap.Clear();

				var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (IDrawingTool tool in DrawObjects)
				{
					string name = tool.Name;
					string tag = tool.Tag as string;
					if (name != null && (name.StartsWith("K34S_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("K8934_", StringComparison.OrdinalIgnoreCase)))
						doomed.Add(name);
					if (tag != null && (tag.StartsWith("K34S_", StringComparison.OrdinalIgnoreCase) || tag.StartsWith("K8934_", StringComparison.OrdinalIgnoreCase)))
						doomed.Add(tag);
				}
				foreach (string tag in doomed)
					RemoveDrawObject(tag);
				ForceRefresh();
				Print(string.Format("[Kat34Scalper] Cleared {0} old signal drawing(s).", doomed.Count));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Clear error: {0}", ex.Message));
			}
		}

		// Arrow/Text feature removed. No toggle apply needed. Lines always render.
		#endregion

		#region HUD Panel — TradeManager pixel-perfect system (HudGap 2, HudPanelWidth 250 → 238 inner)
		// HUD state — factory tokens + helpers live in Kat34Scalper.Draw.HudFactory.cs
		private Border hudBorder;
		private Canvas hudCanvas;
		private TextBlock hudStatusText;
		private System.Windows.Threading.DispatcherTimer hudStatusTimer;
		private bool isHudDragging;
		private bool hasHudDragPosition;
		private double hudDragLeft;
		private double hudDragTop;
		private double hudDragStartLeft;
		private double hudDragStartTop;
		private Point hudDragStart;

		// --- Account/ATM selectors ---
		private ComboBox atmComboBox;
		private Button[] atmSetButtons;

		private string GetAtmSetTemplate(int idx)
		{
			switch (idx)
			{
				case 0: return AtmSet1Atm;
				case 1: return AtmSet2Atm;
				case 2: return AtmSet3Atm;
				case 3: return AtmSet4Atm;
				case 4: return AtmSet5Atm;
				default: return AtmSet6Atm;
			}
		}

		private string GetAtmSetName(int idx)
		{
			switch (idx)
			{
				case 0: return AtmSet1Name;
				case 1: return AtmSet2Name;
				case 2: return AtmSet3Name;
				case 3: return AtmSet4Name;
				case 4: return AtmSet5Name;
				default: return AtmSet6Name;
			}
		}

		private void ApplyAtmSetSelection(int idx)
		{
			string tpl = GetAtmSetTemplate(idx);
			if (string.IsNullOrEmpty(tpl))
			{
				ShowHudStatus(string.Format("Set {0}: no ATM assigned (Indicator Settings)", GetAtmSetName(idx)), Brushes.OrangeRed);
				return;
			}
			if (atmComboBox != null)
			{
				bool found = false;
				for (int i = 0; i < atmComboBox.Items.Count; i++)
				{
					if (atmComboBox.Items[i].ToString().Equals(tpl, StringComparison.OrdinalIgnoreCase))
					{
						atmComboBox.SelectedIndex = i;
						found = true;
						break;
					}
				}
				if (!found)
				{
					ShowHudStatus(string.Format("Set {0}: ATM '{1}' not found on disk", GetAtmSetName(idx), tpl), Brushes.OrangeRed);
					return;
				}
			}
			UpdateAtmSetButtons();
		}

		private void UpdateAtmSetButtons()
		{
			if (atmSetButtons == null) return;
			for (int i = 0; i < atmSetButtons.Length; i++)
			{
				if (atmSetButtons[i] == null) continue;
				string tpl = GetAtmSetTemplate(i);
				bool on = !string.IsNullOrEmpty(cachedBotAtm)
					&& !cachedBotAtm.Equals("None", StringComparison.OrdinalIgnoreCase)
					&& !string.IsNullOrEmpty(tpl)
					&& tpl.Equals(cachedBotAtm, StringComparison.OrdinalIgnoreCase);
				atmSetButtons[i].Background = on ? atmSetOnBg : atmSetOffBg;
				atmSetButtons[i].Foreground = on ? Brushes.White : Brushes.LightGray;
				// sync TextBlock foreground after background flip
				if (atmSetButtons[i].Content is TextBlock tb) tb.Foreground = atmSetButtons[i].Foreground;
			}
		}

		private void ShowHudStatus(string message, Brush foreground)
		{
			if (ChartControl == null || ChartControl.Dispatcher == null) return;
			Action update = () =>
			{
				if (hudStatusText == null) return;
				hudStatusText.Text = message;
				hudStatusText.Foreground = foreground ?? Brushes.White;
				if (hudStatusTimer == null)
				{
					hudStatusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
					hudStatusTimer.Tick += (s, e) =>
					{
						if (hudStatusText != null)
						{
							hudStatusText.Text = string.Empty;
							hudStatusText.Foreground = Brushes.White;
						}
						hudStatusTimer.Stop();
					};
				}
				hudStatusTimer.Stop();
				hudStatusTimer.Start();
			};
			if (ChartControl.Dispatcher.CheckAccess()) update();
			else ChartControl.Dispatcher.BeginInvoke(update);
		}

		// --- HUD drag (TradeManager pattern: clamp ≥40px visible, skip interactive controls) ---
		private static DependencyObject GetHudParent(DependencyObject element)
		{
			if (element == null) return null;
			try { DependencyObject p = VisualTreeHelper.GetParent(element); if (p != null) return p; } catch { }
			try { return LogicalTreeHelper.GetParent(element); } catch { return null; }
		}

		private static bool IsInteractiveVisual(DependencyObject src)
		{
			while (src != null)
			{
				if (src is System.Windows.Controls.Primitives.ButtonBase
					|| src is TextBox
					|| src is ComboBox
					|| src is System.Windows.Controls.Primitives.Selector
					|| src is System.Windows.Controls.Primitives.Thumb)
					return true;
				src = GetHudParent(src);
			}
			return false;
		}

		private bool IsHudDragSource(DependencyObject source)
		{
			if (source == null || hudBorder == null) return false;
			DependencyObject current = source;
			while (current != null)
			{
				if (ReferenceEquals(current, hudBorder))
					return !IsInteractiveVisual(source);
				DependencyObject parent = GetHudParent(current);
				if (ReferenceEquals(parent, current)) break;
				current = parent;
			}
			return false;
		}

		private void OnHudPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (isHudDragging || hudBorder == null || hudCanvas == null) return;
			if (IsInteractiveVisual(e.OriginalSource as DependencyObject)) return;
			hudDragStart = e.GetPosition(hudCanvas);
			hudDragStartLeft = Canvas.GetLeft(hudBorder);
			hudDragStartTop = Canvas.GetTop(hudBorder);
			if (double.IsNaN(hudDragStartLeft)) hudDragStartLeft = 10;
			if (double.IsNaN(hudDragStartTop)) hudDragStartTop = 10;
			isHudDragging = Mouse.Capture(hudBorder, CaptureMode.SubTree);
			e.Handled = isHudDragging;
		}

		private void OnHudPreviewMouseMove(object sender, MouseEventArgs e)
		{
			if (!isHudDragging || hudBorder == null || hudCanvas == null) return;
			if (e.LeftButton != MouseButtonState.Pressed)
			{
				StopHudDrag();
				return;
			}
			Point cur = e.GetPosition(hudCanvas);
			double newLeft = hudDragStartLeft + (cur.X - hudDragStart.X);
			double newTop = hudDragStartTop + (cur.Y - hudDragStart.Y);
			const double minVisible = 40;
			double panelW = hudBorder.ActualWidth > 0 ? hudBorder.ActualWidth : HudPanelWidth;
			double panelH = hudBorder.ActualHeight > 0 ? hudBorder.ActualHeight : 40;
			newLeft = Math.Min(Math.Max(newLeft, minVisible - panelW), Math.Max(0, hudCanvas.ActualWidth - minVisible));
			newTop = Math.Min(Math.Max(newTop, minVisible - panelH), Math.Max(0, hudCanvas.ActualHeight - minVisible));
			Canvas.SetLeft(hudBorder, newLeft);
			Canvas.SetTop(hudBorder, newTop);
			hasHudDragPosition = true;
			hudDragLeft = newLeft;
			hudDragTop = newTop;
			e.Handled = true;
		}

		private void OnHudPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (!isHudDragging) return;
			StopHudDrag();
			e.Handled = true;
		}

		private void StopHudDrag()
		{
			isHudDragging = false;
			if (Mouse.Captured == hudBorder) Mouse.Capture(null);
		}

		private void OnHudLostMouseCapture(object sender, MouseEventArgs e)
		{
			isHudDragging = false;
		}

		private void AttachHudDragHandlers()
		{
			if (hudBorder == null) return;
			hudBorder.AddHandler(Border.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonDown), true);
			hudBorder.AddHandler(Border.PreviewMouseMoveEvent, new MouseEventHandler(OnHudPreviewMouseMove), true);
			hudBorder.AddHandler(Border.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonUp), true);
			hudBorder.LostMouseCapture += OnHudLostMouseCapture;
		}

		private void DetachHudDragHandlers()
		{
			if (hudBorder == null) return;
			hudBorder.RemoveHandler(Border.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonDown));
			hudBorder.RemoveHandler(Border.PreviewMouseMoveEvent, new MouseEventHandler(OnHudPreviewMouseMove));
			hudBorder.RemoveHandler(Border.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnHudPreviewMouseLeftButtonUp));
			hudBorder.LostMouseCapture -= OnHudLostMouseCapture;
		}

		private void BuildHud()
		{
			Grid host = ChartControl != null ? ChartControl.Parent as Grid : null;
			if (hudBorder != null || host == null) return;

			hudCanvas = new Canvas
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch,
				ClipToBounds = false,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			System.Windows.Controls.Panel.SetZIndex(hudCanvas, 9999);
			host.Children.Add(hudCanvas);

			hudBorder = new Border
			{
				Tag = "Kat34ScalperPanel",
				Background = new SolidColorBrush(Color.FromArgb(240, 20, 24, 33)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(HudGap),
				Margin = new Thickness(HudGap),
				Width = HudPanelWidth,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Cursor = Cursors.SizeAll,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			hudCanvas.Children.Add(hudBorder);
			Canvas.SetLeft(hudBorder, hasHudDragPosition ? hudDragLeft : 10);
			Canvas.SetTop(hudBorder, hasHudDragPosition ? hudDragTop : 10);
			hudBorder.Loaded += (s, ev) =>
			{
				if (!hasHudDragPosition && hudCanvas != null)
					Canvas.SetTop(hudBorder, Math.Max(0, hudCanvas.ActualHeight - hudBorder.ActualHeight - 10));
			};
			AttachHudDragHandlers();

			var mainPanel = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };

			// Header — TradeManager style: Normal 12, Opacity 0.3, HudGap*2 breathing
			mainPanel.Children.Add(new TextBlock
			{
				Text = string.Format("⚡ KAT 34-ScalperBot v{0}", VERSION),
				Foreground = new SolidColorBrush(Color.FromRgb(70, 130, 160)),
				FontWeight = FontWeights.Normal,
				FontSize = 12,
				Margin = new Thickness(0, HudGap * 2, 0, HudGap * 2),
				HorizontalAlignment = HorizontalAlignment.Left,
				Opacity = 0.3,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			});

			mainPanel.Children.Add(CreateAccountInfoSection());

			hudStatusText = new TextBlock
			{
				Background = Brushes.Transparent,
				Foreground = Brushes.White,
				FontSize = 10,
				Margin = new Thickness(0, 0, 0, HudGap),
				Height = 16,
				MinHeight = 16,
				MaxHeight = 16,
				TextTrimming = TextTrimming.CharacterEllipsis,
				TextWrapping = TextWrapping.NoWrap,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Text = string.Empty,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			// status lives inside mainPanel but above first card (TradeManager sec1Panel pattern isolates it inside card;
			// Scalper keeps it above secBot card for immediate visibility without extra nesting)
			// To match TradeManager exactly: create sec1-like card that hosts status + BOT controls
			// Instead we keep status as separate element above cards for cleaner HUD, but with HudGap margin
			mainPanel.Children.Add(hudStatusText);

			// --- BOT card: account, ATM template, quick-sets, BOT toggle, market/BE/Revert/Close, Daily Risk ---
			var secBot = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };

			var accCombo = new ComboBox
			{
				FontSize = 11,
				Height = 22,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(4, 0, 0, 0),
				Margin = new Thickness(0, 0, 0, HudGap),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true,
				Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
				Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1)
			};
			if (Account.All != null)
				foreach (Account acc in Account.All)
					accCombo.Items.Add(acc.Name);
			for (int i = 0; i < accCombo.Items.Count; i++)
				if (accCombo.Items[i].ToString().Equals(cachedBotAccountName, StringComparison.OrdinalIgnoreCase))
					accCombo.SelectedIndex = i;
			if (accCombo.SelectedIndex < 0 && accCombo.Items.Count > 0) accCombo.SelectedIndex = 0;
			int simIdx = -1;
			for (int i = 0; i < accCombo.Items.Count; i++)
				if (accCombo.Items[i].ToString().Equals("SIM101", StringComparison.OrdinalIgnoreCase)) { simIdx = i; break; }
			if (simIdx >= 0) accCombo.SelectedIndex = simIdx;
			else if (accCombo.SelectedIndex < 0 && accCombo.Items.Count > 0) accCombo.SelectedIndex = 0;
			if (accCombo.SelectedItem != null)
			{
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
				SyncChartTraderAccount(cachedBotAccountName);
			}
			accCombo.SelectionChanged += (s, e) =>
			{
				if (accCombo.SelectedItem == null) return;
				cachedBotAccountName = accCombo.SelectedItem.ToString();
				BotAccountName = cachedBotAccountName;
				SyncChartTraderAccount(cachedBotAccountName);
				try { UpdateAccountInfoSection(); } catch { }
			};
			secBot.Children.Add(accCombo);

			atmComboBox = new ComboBox
			{
				FontSize = 11,
				Height = 22,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 0, HudGap),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true,
				Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
				Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1)
			};
			atmComboBox.Items.Add("None");
			try
			{
				string atmDir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "templates", "AtmStrategy");
				if (Directory.Exists(atmDir))
				{
					var names = new List<string>();
					foreach (string f in Directory.GetFiles(atmDir, "*.xml"))
						names.Add(Path.GetFileNameWithoutExtension(f));
					names.Sort(StringComparer.OrdinalIgnoreCase);
					foreach (string n in names) atmComboBox.Items.Add(n);
				}
			}
			catch { }
			for (int i = 0; i < atmComboBox.Items.Count; i++)
				if (atmComboBox.Items[i].ToString().Equals(cachedBotAtm, StringComparison.OrdinalIgnoreCase))
					atmComboBox.SelectedIndex = i;
			const string mnq1ct = "mnq. 1ct. 15-be20-35move15-50triggertrail5step1";
			int mnqIdx = -1;
			for (int i = 0; i < atmComboBox.Items.Count; i++)
				if (atmComboBox.Items[i].ToString().Equals(mnq1ct, StringComparison.OrdinalIgnoreCase)) { mnqIdx = i; break; }
			if (mnqIdx >= 0) atmComboBox.SelectedIndex = mnqIdx;
			else if (atmComboBox.SelectedIndex < 0) atmComboBox.SelectedIndex = 0;
			if (atmComboBox.SelectedItem != null)
			{
				cachedBotAtm = atmComboBox.SelectedItem.ToString();
				BotAtmTemplate = cachedBotAtm;
			}
			atmComboBox.SelectionChanged += (s, e) =>
			{
				if (atmComboBox.SelectedItem == null) return;
				cachedBotAtm = atmComboBox.SelectedItem.ToString();
				BotAtmTemplate = cachedBotAtm;
				UpdateAtmSetButtons();
			};
			secBot.Children.Add(atmComboBox);

			// ATM quick-sets: 6 buttons in single row — HudGap uniform, height 22 TradeManager style
			atmSetButtons = new Button[6];
			Grid atmSetGrid = CreateSixColumnGrid(HudGap, HudGap, HudGap);
			for (int setIdx = 0; setIdx < 6; setIdx++)
			{
				int capturedIdx = setIdx;
				Button setBtn = CreateButton(GetAtmSetName(setIdx), atmSetOffBg, null, 22, 10);
				setBtn.Foreground = Brushes.LightGray;
				if (setBtn.Content is TextBlock tb) tb.Foreground = Brushes.LightGray;
				setBtn.Click += (s, ev) => ApplyAtmSetSelection(capturedIdx);
				Grid.SetColumn(setBtn, setIdx * 2);
				atmSetButtons[setIdx] = setBtn;
				atmSetGrid.Children.Add(setBtn);
			}
			secBot.Children.Add(atmSetGrid);
			UpdateAtmSetButtons();

			// BOT ON/OFF — toggle height 24 (sec4 uniform), font 11, full-width
			Button btnBot = CreateButton(cachedBotOn ? "⚡ BOT: ON" : "BOT: OFF", cachedBotOn ? hudOnBrush : hudOffBrush, null, 24, 11);
			btnBot.Foreground = cachedBotOn ? Brushes.White : Brushes.LightGray;
			if (btnBot.Content is TextBlock tbb) tbb.Foreground = btnBot.Foreground;
			btnBot.Margin = new Thickness(0, 0, 0, 0);
			btnBot.Click += (s, e) =>
			{
				cachedBotOn = !cachedBotOn;
				BotEnabled = cachedBotOn;
				SetButtonLabel(btnBot, cachedBotOn ? "⚡ BOT: ON" : "BOT: OFF");
				btnBot.Background = cachedBotOn ? hudOnBrush : hudOffBrush;
				btnBot.Foreground = cachedBotOn ? Brushes.White : Brushes.LightGray;
				if (btnBot.Content is TextBlock tb2) tb2.Foreground = btnBot.Foreground;
				try { UpdateAccountInfoSection(); } catch { }
				if (cachedBotOn)
					ShowHudStatus("BOT ON — every signal switched ON auto-submits entries", Brushes.LightGreen);
				else
				{
					ShowHudStatus("BOT OFF — pending entry cancelled", Brushes.OrangeRed);
					TriggerCustomEvent(o =>
					{
						pendingMigrate = false;
						CancelPendingBotOrder("BOT switched OFF");
					}, null);
				}
			};
			secBot.Children.Add(btnBot);

			// Market orders — 2-col grid, height 43 TradeManager primary (was 48), HudGap gaps
			Grid mktBtnGrid = CreateTwoColumnGrid(HudGap, HudGap);
			SolidColorBrush buyMktBg  = CreateFrozenBrush(Color.FromRgb(12, 48, 25));
			SolidColorBrush sellMktBg = CreateFrozenBrush(Color.FromRgb(55, 15, 18));
			Button btnSellMkt = CreateButton("SELL MARKET", sellMktBg, null, 43, 12);
			btnSellMkt.Click += (s, ev) => TriggerCustomEvent(o => { PlaceMarketOrder(OrderAction.Sell); }, null);
			Grid.SetColumn(btnSellMkt, 0);
			mktBtnGrid.Children.Add(btnSellMkt);
			Button btnBuyMkt = CreateButton("BUY MARKET", buyMktBg, null, 43, 12);
			btnBuyMkt.Click += (s, ev) => TriggerCustomEvent(o => { PlaceMarketOrder(OrderAction.Buy); }, null);
			Grid.SetColumn(btnBuyMkt, 2);
			mktBtnGrid.Children.Add(btnBuyMkt);
			secBot.Children.Add(mktBtnGrid);

			Grid beRevertGrid = CreateTwoColumnGrid(HudGap, HudGap);
			SolidColorBrush beBg     = CreateFrozenBrush(Color.FromRgb(22, 22, 22));
			SolidColorBrush revertBg = CreateFrozenBrush(Color.FromRgb(22, 22, 22));
			Button btnRevert = CreateButton("Revert", revertBg, null, 43, 12);
			btnRevert.Click += (s, ev) => TriggerCustomEvent(o => { RevertPosition(); }, null);
			Grid.SetColumn(btnRevert, 0);
			beRevertGrid.Children.Add(btnRevert);
			Button btnBE = CreateButton("Break Even", beBg, null, 43, 12);
			btnBE.Click += (s, ev) => TriggerCustomEvent(o => { SetBreakeven(); }, null);
			Grid.SetColumn(btnBE, 2);
			beRevertGrid.Children.Add(btnBE);
			secBot.Children.Add(beRevertGrid);

			SolidColorBrush closeBg = CreateFrozenBrush(Color.FromRgb(10, 10, 10));
			Button btnClose = CreateButton("Close/flatten", closeBg, null, 59, 15);
			btnClose.Margin = new Thickness(0, 0, 0, 0);
			btnClose.Click += (s, ev) => TriggerCustomEvent(o => { FlattenAllPositions(); }, null);
			secBot.Children.Add(btnClose);

			// Daily Max DD & Daily Max Profit — shared 6-col base for pixel-perfect center (TradeManager sec4 pattern)
			Grid dailyRiskGrid = CreateSixColumnGrid(0, HudGap, HudGap);
			Button btnDailyMaxDD = CreateButton(cachedIsDailyMaxDD ? "Max DD: ON" : "Max DD: OFF",
				cachedIsDailyMaxDD ? dailyOnBg : dailyOffBg, null, 24, 10);
			btnDailyMaxDD.Foreground = cachedIsDailyMaxDD ? Brushes.White : Brushes.LightGray;
			if (btnDailyMaxDD.Content is TextBlock tbd) tbd.Foreground = btnDailyMaxDD.Foreground;
			btnDailyMaxDD.Click += (s, ev) =>
			{
				cachedIsDailyMaxDD = !cachedIsDailyMaxDD;
				DailyMaxDDEnabled = cachedIsDailyMaxDD;
				SetButtonLabel(btnDailyMaxDD, cachedIsDailyMaxDD ? "Max DD: ON" : "Max DD: OFF");
				btnDailyMaxDD.Background = cachedIsDailyMaxDD ? dailyOnBg : dailyOffBg;
				btnDailyMaxDD.Foreground = cachedIsDailyMaxDD ? Brushes.White : Brushes.LightGray;
				if (btnDailyMaxDD.Content is TextBlock tbx) tbx.Foreground = btnDailyMaxDD.Foreground;
				if (IsDailyRiskBreached(out string breachReason))
				{
					ShowHudStatus(breachReason, Brushes.OrangeRed);
					TriggerCustomEvent(o => { CancelPendingBotOrder(breachReason); }, null);
				}
				else
					ShowHudStatus("Daily Max DD: " + (cachedIsDailyMaxDD ? "ON ($" + cachedDailyMaxDD + ")" : "OFF"), Brushes.LightGreen);
			};
			Grid.SetColumn(btnDailyMaxDD, 0);
			Grid.SetColumnSpan(btnDailyMaxDD, 5);
			dailyRiskGrid.Children.Add(btnDailyMaxDD);

			Button btnDailyMaxProfit = CreateButton(cachedIsDailyMaxProfit ? "Max Profit: ON" : "Max Profit: OFF",
				cachedIsDailyMaxProfit ? dailyOnBg : dailyOffBg, null, 24, 10);
			btnDailyMaxProfit.Foreground = cachedIsDailyMaxProfit ? Brushes.White : Brushes.LightGray;
			if (btnDailyMaxProfit.Content is TextBlock tbp) tbp.Foreground = btnDailyMaxProfit.Foreground;
			btnDailyMaxProfit.Click += (s, ev) =>
			{
				cachedIsDailyMaxProfit = !cachedIsDailyMaxProfit;
				DailyMaxProfitEnabled = cachedIsDailyMaxProfit;
				SetButtonLabel(btnDailyMaxProfit, cachedIsDailyMaxProfit ? "Max Profit: ON" : "Max Profit: OFF");
				btnDailyMaxProfit.Background = cachedIsDailyMaxProfit ? dailyOnBg : dailyOffBg;
				btnDailyMaxProfit.Foreground = cachedIsDailyMaxProfit ? Brushes.White : Brushes.LightGray;
				if (btnDailyMaxProfit.Content is TextBlock tbq) tbq.Foreground = btnDailyMaxProfit.Foreground;
				if (IsDailyRiskBreached(out string breachReason))
				{
					ShowHudStatus(breachReason, Brushes.OrangeRed);
					TriggerCustomEvent(o => { CancelPendingBotOrder(breachReason); }, null);
				}
				else
					ShowHudStatus("Daily Max Profit: " + (cachedIsDailyMaxProfit ? "ON ($" + cachedDailyMaxProfit + ")" : "OFF"), Brushes.LightGreen);
			};
			Grid.SetColumn(btnDailyMaxProfit, 6);
			Grid.SetColumnSpan(btnDailyMaxProfit, 5);
			dailyRiskGrid.Children.Add(btnDailyMaxProfit);
			secBot.Children.Add(dailyRiskGrid);

			mainPanel.Children.Add(CreateSectionCard(secBot, HudGap));

			// --- ALERT SIGNAL card: A2 ---
			mainPanel.Children.Add(CreateModuleTitle("ALERT SIGNAL"));
			var secAlert = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };
			Button btnAlertA2 = CreateFilterToggle("A2", () => cachedAlertA2, v => SetAlertA2Signal(v));
			btnAlertA2.Click += (s, e) => { try { UpdateAccountInfoSection(); } catch { } };
			// single toggle full-width: wrap in grid with single star centered? Use full-width button directly
			// For pixel parity with 2-col cards, use 6-col base with Span11 full-width
			Grid aGrid = CreateTwoColumnGrid(0, HudGap);
			// Span both stars + gap: replace grid with single button full-width for simplicity — stack will stretch
			secAlert.Children.Add(btnAlertA2);
			mainPanel.Children.Add(CreateSectionCard(secAlert, HudGap));

			// --- BOT SIGNAL card: B1 + B2 in 2-col grid, dark-blue ON ---
			mainPanel.Children.Add(CreateModuleTitle("BOT SIGNAL"));
			var secSignal = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };
			Grid sRow = CreateTwoColumnGrid(0, HudGap);
			Button btnB1 = CreateFilterToggle("B1 (34bounce8+)", () => cachedB1, v => SetB1Signal(v), 24, 10, hudBotOnBrush);
			btnB1.Click += (s, e) => { try { UpdateAccountInfoSection(); } catch { } };
			Grid.SetColumn(btnB1, 0);
			sRow.Children.Add(btnB1);
			Button btnB2 = CreateFilterToggle("B2 (89uturn34)", () => cachedB2, v => SetB2Signal(v), 24, 10, hudBotOnBrush);
			btnB2.Click += (s, e) => { try { UpdateAccountInfoSection(); } catch { } };
			Grid.SetColumn(btnB2, 2);
			sRow.Children.Add(btnB2);
			secSignal.Children.Add(sRow);
			mainPanel.Children.Add(CreateSectionCard(secSignal, HudGap));

			// --- BOT FILTER card: 3 rows x 2 cols, all toggle height 24 uniform ---
			mainPanel.Children.Add(CreateModuleTitle("BOT FILTER"));
			var secBotFilter = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };
			Grid bfRow1 = CreateTwoColumnGrid(HudGap, HudGap);
			Button tAdxRise = CreateFilterToggle("ADX rising", () => cachedAdxRise, v => cachedAdxRise = v);
			Grid.SetColumn(tAdxRise, 0);
			bfRow1.Children.Add(tAdxRise);
			Button tAdxMtf = CreateFilterToggle("ADX MTF", () => cachedAdxMtf, v => cachedAdxMtf = v);
			Grid.SetColumn(tAdxMtf, 2);
			bfRow1.Children.Add(tAdxMtf);
			Grid bfRow2 = CreateTwoColumnGrid(HudGap, HudGap);
			Button tEr = CreateFilterToggle("ER (trend)", () => cachedEr, v => cachedEr = v);
			Grid.SetColumn(tEr, 0);
			bfRow2.Children.Add(tEr);
			Button tCi = CreateFilterToggle("CI (chop)", () => cachedCi, v => cachedCi = v);
			Grid.SetColumn(tCi, 2);
			bfRow2.Children.Add(tCi);
			Grid bfRow3 = CreateTwoColumnGrid(0, HudGap);
			Button tVol = CreateFilterToggle("Volume", () => cachedVol, v => cachedVol = v);
			Grid.SetColumn(tVol, 0);
			bfRow3.Children.Add(tVol);
			Button tTime = CreateFilterToggle("Time window", () => cachedTime, v => cachedTime = v);
			Grid.SetColumn(tTime, 2);
			bfRow3.Children.Add(tTime);
			secBotFilter.Children.Add(bfRow1);
			secBotFilter.Children.Add(bfRow2);
			secBotFilter.Children.Add(bfRow3);
			mainPanel.Children.Add(CreateSectionCard(secBotFilter, HudGap));

			// --- DRAW card: Clear full-width, height 24 toggle size (was 24) ---
			mainPanel.Children.Add(CreateModuleTitle("DRAW"));
			var secDraw = new StackPanel { UseLayoutRounding = true, SnapsToDevicePixels = true };
			SolidColorBrush clearBg = CreateFrozenBrush(Color.FromRgb(20, 20, 20));
			Button btnClear = CreateButton("Clear", clearBg, null, 24, 10);
			btnClear.Click += (s, e) => TriggerCustomEvent(o => ClearOldSignalDrawings(), null);
			secDraw.Children.Add(btnClear);
			mainPanel.Children.Add(CreateSectionCard(secDraw, 0));

			hudBorder.Child = mainPanel;
			StartPanelWatchdog();
		}

		// Mirrors the HUD account pick into Chart Trader's own account selector
		private void SyncChartTraderAccount(string accountName)
		{
			try
			{
				if (string.IsNullOrEmpty(accountName)) return;
				DependencyObject ctControl = GetChartTraderControl();
				if (ctControl == null) return;

				var combos = new List<ComboBox>();
				FindAllVisualChildren<ComboBox>(ctControl, combos);
				foreach (ComboBox combo in combos)
					foreach (object item in combo.Items)
					{
						if (item == null) continue;
						string itemText = item.ToString();
						bool match = (item as Account)?.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase) == true
							|| itemText.Equals(accountName, StringComparison.OrdinalIgnoreCase)
							|| itemText.StartsWith(accountName + "!", StringComparison.OrdinalIgnoreCase);
						if (!match) continue;
						if (!ReferenceEquals(combo.SelectedItem, item))
							combo.SelectedItem = item;
						return;
					}
				var listed = new List<string>();
				foreach (ComboBox combo in combos)
					foreach (object item in combo.Items)
						if (item is Account listedAcc && !listed.Contains(listedAcc.Name))
							listed.Add(listedAcc.Name);
				Print(string.Format("[Kat34Scalper] Chart Trader sync skipped — '{0}' not in its account list (listed: {1})",
					accountName, listed.Count > 0 ? string.Join(", ", listed) : "none"));
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Chart Trader account sync failed: {0}", ex.Message));
			}
		}

		private DependencyObject GetChartTraderControl()
		{
			if (ChartControl == null) return null;
			if (ChartControl.OwnerChart != null && ChartControl.OwnerChart.ChartTrader != null)
			{
				var ct = ChartControl.OwnerChart.ChartTrader;
				if (ct.Visibility == Visibility.Visible) return ct;
			}
			Window window = Window.GetWindow(ChartControl);
			if (window != null)
			{
				var ct = FindVisualChildByTypeName(window, "ChartTraderControl") ?? FindVisualChildByTypeName(window, "ChartTrader");
				if (ct is FrameworkElement fe && fe.Visibility == Visibility.Visible) return ct;
			}
			return null;
		}

		private DependencyObject FindVisualChildByTypeName(DependencyObject parent, string typeName)
		{
			if (parent == null) return null;
			int count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				DependencyObject child = VisualTreeHelper.GetChild(parent, i);
				if (child != null && child.GetType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
					return child;
				DependencyObject result = FindVisualChildByTypeName(child, typeName);
				if (result != null) return result;
			}
			return null;
		}

		private void FindAllVisualChildren<T>(DependencyObject parent, List<T> results) where T : DependencyObject
		{
			if (parent == null) return;
			int count = VisualTreeHelper.GetChildrenCount(parent);
			for (int i = 0; i < count; i++)
			{
				DependencyObject child = VisualTreeHelper.GetChild(parent, i);
				if (child is T typedChild)
					results.Add(typedChild);
				FindAllVisualChildren<T>(child, results);
			}
		}

		private System.Windows.Threading.DispatcherTimer panelWatchdog;

		private void StartPanelWatchdog()
		{
			if (panelWatchdog == null)
			{
				panelWatchdog = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
				panelWatchdog.Tick += OnPanelWatchdogTick;
			}
			panelWatchdog.Start();
		}

		private void StopPanelWatchdog()
		{
			if (panelWatchdog != null)
			{
				panelWatchdog.Stop();
				panelWatchdog = null;
			}
		}

		private void OnPanelWatchdogTick(object sender, EventArgs e)
		{
			try
			{
				EnsureAccountEventSubscription();
				EvaluateDailyRiskLimits();
				TrySubmitPendingRevert();
				ScheduleAtmBracketMerge();
				try { UpdateAccountInfoSection(); } catch (Exception ex) { Print(string.Format("[Kat34Scalper] Watchdog UpdateAccountInfoSection: {0}", ex.Message)); }
			}
			catch (Exception ex)
			{
				Print(string.Format("[Kat34Scalper] Watchdog tick error: {0}", ex.Message));
			}
		}

		private void RemoveHud()
		{
			StopHudDrag();
			StopPanelWatchdog();
			RemoveAccountEventSubscription();
			if (hudStatusTimer != null)
			{
				hudStatusTimer.Stop();
				hudStatusTimer = null;
			}
			DetachHudDragHandlers();
			if (hudBorder != null && hudBorder.Parent is Panel borderHost)
				borderHost.Children.Remove(hudBorder);
			hudBorder = null;
			if (hudCanvas != null && hudCanvas.Parent is Grid host)
				host.Children.Remove(hudCanvas);
			hudCanvas = null;
			hudStatusText = null;
			atmComboBox = null;
			atmSetButtons = null;
			accountInfoCard = null;
			accountInfoDateTimeText = null;
			accountDateRun = null;
			accountTimeHmRun = null;
			accountTimeSRun = null;
			accountAmPmRun = null;
			accountNytRun = null;
			accountBalanceText = null;
			accountBalanceLabelRun = null;
			accountBalanceValueRun = null;
			accountUnrealText = null;
			accountRealText = null;
			accountUnrealLabelRun = null;
			accountUnrealValueRun = null;
			accountRealLabelRun = null;
			accountRealValueRun = null;
			accountDailyText = null;
			accountDailyLabelRun = null;
			accountDailyValueRun = null;
			accountAcctText = null;
			accountAcctLabelRun = null;
			accountAcctValueRun = null;
			accountBotText = null;
			accountBotLabelRun = null;
			accountBotValueRun = null;
			accountBotSep1 = null;
			accountB1Run = null;
			accountBotSep2 = null;
			accountB2Run = null;
			accountBotSep3 = null;
			accountPosRun = null;
		}
		#endregion
	}
}
