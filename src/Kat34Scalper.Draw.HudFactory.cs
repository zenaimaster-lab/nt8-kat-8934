/*
 * Kat34Scalper.Draw.HudFactory.cs — HUD factory (partial class Kat34Scalper).
 * TradeManager pixel-perfect tokens + factory extracted from Draw.cs v0.96 audit.
 */

#region Using declarations
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		// Design tokens — single source of truth (copy TradeManager HUD Factory verbatim)
		private const double HudGap = 2;
		private const double HudPanelWidth = 250; // 250 outer => 238 inner (250-6-6) = 22+24k perfect for gap2 across 2/4/6/8 cols

		private static SolidColorBrush CreateFrozenBrush(Color c) { var b = new SolidColorBrush(c); if (b.CanFreeze) b.Freeze(); return b; }
		private readonly SolidColorBrush hudOnBrush = CreateFrozenBrush(Color.FromRgb(0, 122, 204));
		private readonly SolidColorBrush hudBotOnBrush = CreateFrozenBrush(Color.FromRgb(15, 60, 130));
		private readonly SolidColorBrush hudOffBrush = CreateFrozenBrush(Color.FromRgb(45, 50, 65));
		private readonly SolidColorBrush atmSetOffBg = CreateFrozenBrush(Color.FromRgb(45, 50, 65));
		private readonly SolidColorBrush atmSetOnBg = CreateFrozenBrush(Color.FromRgb(180, 90, 20));
		private readonly SolidColorBrush dailyOffBg = CreateFrozenBrush(Color.FromRgb(45, 50, 65));
		private readonly SolidColorBrush dailyOnBg = CreateFrozenBrush(Color.FromRgb(58, 19, 107));
		// Program + DailyRisk quick-set style — TradeManager port (transparent vs opaque)
		private readonly SolidColorBrush profileOffBg = new SolidColorBrush(Color.FromArgb(128, 45, 50, 65));
		private readonly SolidColorBrush[] profileRowOnBgs = new SolidColorBrush[] { new SolidColorBrush(Color.FromRgb(20, 110, 110)), new SolidColorBrush(Color.FromRgb(135, 35, 65)) };
		private readonly SolidColorBrush dailyRiskPresetOffBg = new SolidColorBrush(Color.FromArgb(128, 45, 50, 65));
		private readonly SolidColorBrush dailyRiskPresetOnBg = new SolidColorBrush(Color.FromArgb(51, 36, 7, 72));

		private Button CreateButton(string text, Brush bg, RoutedEventHandler handler, double height = 24, double fontSize = 10)
		{
			var tb = new TextBlock { Text = text, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0), Padding = new Thickness(0) };
			Button btn = new Button
			{
				Content = tb,
				Background = bg,
				Foreground = Brushes.White,
				FontWeight = FontWeights.Normal,
				FontSize = fontSize,
				Margin = new Thickness(0),
				Padding = new Thickness(2, 0, 2, 0),
				Height = height,
				BorderThickness = new Thickness(0),
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
				Template = GetHudButtonTemplate(),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			if (handler != null)
				btn.Click += handler;
			return btn;
		}

		private double GetQuickSetFontSize()
		{
			double sz = QuickSetFontSize;
			if (sz < 6) sz = 6;
			if (sz > 14) sz = 14;
			if (sz <= 0) sz = 8;
			return sz;
		}
		private static Brush BuildLabelBrush(Brush src, int pct, int defaultPct, byte fallbackAlpha)
		{
			try
			{
				Brush baseBrush = src ?? Brushes.White;
				Color baseColor = Colors.White;
				if (baseBrush is SolidColorBrush scb) baseColor = scb.Color;
				if (pct == 0) pct = defaultPct;
				if (pct < 10) pct = 10;
				if (pct > 100) pct = 100;
				byte alpha = (byte)(pct * 255 / 100);
				Color c = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
				var nb = new SolidColorBrush(c);
				if (nb.CanFreeze) nb.Freeze();
				return nb;
			}
			catch { var fb = new SolidColorBrush(Color.FromArgb(fallbackAlpha, 255, 255, 255)); if (fb.CanFreeze) fb.Freeze(); return fb; }
		}
		private Brush GetQuickSetLabelBrush() => BuildLabelBrush(QuickSetLabelColor, QuickSetLabelOpacityPercent, 50, 128);
		private Brush GetProgramLabelBrush() => BuildLabelBrush(ProgramLabelColor, ProgramLabelOpacityPercent, 20, 51);
		private string GetButtonLabel(Button btn)
		{
			if (btn == null) return null;
			if (btn.Content is TextBlock tb) return tb.Text;
			return btn.Content as string;
		}

		private static ControlTemplate _hudButtonTemplate;
		private static ControlTemplate _quickSetButtonTemplate;
		private static ControlTemplate GetHudButtonTemplate()
		{
			if (_hudButtonTemplate != null) return _hudButtonTemplate;
			var border = new FrameworkElementFactory(typeof(Border), "root");
			border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			border.SetValue(Border.SnapsToDevicePixelsProperty, true);
			border.SetValue(Border.UseLayoutRoundingProperty, true);
			var cp = new FrameworkElementFactory(typeof(ContentPresenter));
			cp.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			cp.SetBinding(ContentPresenter.VerticalAlignmentProperty, new System.Windows.Data.Binding("VerticalContentAlignment") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			cp.SetValue(ContentPresenter.MarginProperty, new Thickness(2, 0, 2, 0));
			border.AppendChild(cp);
			_hudButtonTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
			return _hudButtonTemplate;
		}

		private static ControlTemplate GetQuickSetButtonTemplate()
		{
			if (_quickSetButtonTemplate != null) return _quickSetButtonTemplate;
			var border = new FrameworkElementFactory(typeof(Border), "root");
			border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
			border.SetValue(Border.SnapsToDevicePixelsProperty, true);
			border.SetValue(Border.UseLayoutRoundingProperty, true);
			var tb = new FrameworkElementFactory(typeof(TextBlock), "label");
			tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Content") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			tb.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			tb.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			tb.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
			tb.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
			tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
			tb.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
			tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
			tb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
			tb.SetValue(TextBlock.MarginProperty, new Thickness(1, 0, 1, 0));
			border.AppendChild(tb);
			_quickSetButtonTemplate = new ControlTemplate(typeof(Button)) { VisualTree = border };
			return _quickSetButtonTemplate;
		}

		private void SetButtonLabel(Button btn, string text)
		{
			if (btn == null) return;
			if (btn.Content is TextBlock tb)
			{
				if (tb.Text != text) tb.Text = text;
				tb.TextAlignment = TextAlignment.Center;
				tb.HorizontalAlignment = HorizontalAlignment.Center;
				tb.VerticalAlignment = VerticalAlignment.Center;
				tb.TextTrimming = TextTrimming.CharacterEllipsis;
				tb.TextWrapping = TextWrapping.NoWrap;
				try { if (btn.Foreground != null) tb.Foreground = btn.Foreground; } catch {}
				try { if (btn.FontSize > 0) tb.FontSize = btn.FontSize; } catch {}
				tb.Margin = new Thickness(0);
				tb.Padding = new Thickness(0);
			}
			else if (btn.Content is StackPanel) { return; }
			else
			{
				var nTb = new TextBlock { Text = text, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0), Padding = new Thickness(0) };
				try { if (btn.Foreground != null) nTb.Foreground = btn.Foreground; } catch {}
				try { if (btn.FontSize > 0) nTb.FontSize = btn.FontSize; } catch {}
				btn.Content = nTb;
			}
			btn.HorizontalContentAlignment = HorizontalAlignment.Center;
			btn.VerticalContentAlignment = VerticalAlignment.Center;
			btn.Padding = new Thickness(2, 0, 2, 0);
		}

		private Grid CreateTwoColumnGrid(double bottomMargin = 2, double centerGap = 2)
		{
			Grid grid = new Grid { Margin = new Thickness(0, 0, 0, bottomMargin), HorizontalAlignment = HorizontalAlignment.Stretch, UseLayoutRounding = true, SnapsToDevicePixels = true };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(centerGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			return grid;
		}

		private Grid CreateFourColumnGrid(double bottomMargin = 2, double centerGap = 2, double subGap = 2)
		{
			Grid grid = new Grid { Margin = new Thickness(0, 0, 0, bottomMargin), HorizontalAlignment = HorizontalAlignment.Stretch, UseLayoutRounding = true, SnapsToDevicePixels = true };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(centerGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			return grid;
		}

		private Grid CreateSixColumnGrid(double bottomMargin = 2, double centerGap = 2, double subGap = 2)
		{
			Grid grid = new Grid { Margin = new Thickness(0, 0, 0, bottomMargin), HorizontalAlignment = HorizontalAlignment.Stretch, UseLayoutRounding = true, SnapsToDevicePixels = true };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(centerGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			return grid;
		}

		private Grid CreateEightColumnGrid(double bottomMargin = 2, double centerGap = 2, double subGap = 2)
		{
			Grid grid = new Grid { Margin = new Thickness(0, 0, 0, bottomMargin), HorizontalAlignment = HorizontalAlignment.Stretch, UseLayoutRounding = true, SnapsToDevicePixels = true };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(centerGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subGap) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			return grid;
		}

		private Border CreateSectionCard(FrameworkElement child, double bottomMargin = 2)
		{
			var contentHost = new Border
			{
				Padding = new Thickness(HudGap),
				Background = Brushes.Transparent,
				Child = child,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			var footer = new Border
			{
				Height = 6,
				Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
				CornerRadius = new CornerRadius(0, 0, 4, 4),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			var inner = new Grid { UseLayoutRounding = true, SnapsToDevicePixels = true };
			inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
			Grid.SetRow(contentHost, 0);
			Grid.SetRow(footer, 1);
			inner.Children.Add(contentHost);
			inner.Children.Add(footer);
			return new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(10, 12, 18)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(35, 42, 56)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(5),
				Margin = new Thickness(0, 0, 0, bottomMargin),
				Child = inner,
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
		}

		private TextBlock CreateModuleTitle(string text)
		{
			return new TextBlock
			{
				Text = text,
				Foreground = new SolidColorBrush(Color.FromRgb(110, 120, 145)),
				FontWeight = FontWeights.Bold,
				FontSize = 10,
				Margin = new Thickness(2, 0, 0, HudGap),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
		}

		private Button CreateFilterToggle(string label, Func<bool> getter, Action<bool> setter, double height = 24, double fontSize = 10, Brush activeBrush = null)
		{
			Brush onBrush = activeBrush ?? hudOnBrush;
			Button btn = CreateButton(label, getter() ? onBrush : hudOffBrush, null, height, fontSize);
			btn.Foreground = getter() ? Brushes.White : Brushes.LightGray;
			btn.Click += (s, e) =>
			{
				setter(!getter());
				bool on = getter();
				SetButtonLabel(btn, label);
				btn.Background = on ? onBrush : hudOffBrush;
				btn.Foreground = on ? Brushes.White : Brushes.LightGray;
				if (btn.Content is TextBlock tb) tb.Foreground = btn.Foreground;
			};
			return btn;
		}
	}
}
