/*
 * Kat34Scalper.Profile.cs — Profile (Program) + DailyRisk quick-set module (partial class Kat34Scalper).
 * Whole-account package: account/ATM/qty/buffer/dailyRisk. TradeManager port v1.00 audit split.
 * Extracted from Kat34Scalper.Draw.cs (profile helpers) to keep Draw focused on rendering.
 */

#region Using declarations
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.KAT
{
	public partial class Kat34Scalper
	{
		// --- Trading Profile (Program) quick sets — whole account: account/ATM/qty/buffer/dailyRisk ---
		private string GetTradingProfileName(int idx)
		{
			switch (idx) { case 0: return TradingProfile1Name; case 1: return TradingProfile2Name; case 2: return TradingProfile3Name; case 3: return TradingProfile4Name; case 4: return TradingProfile5Name; case 5: return TradingProfile6Name; case 6: return TradingProfile7Name; default: return TradingProfile8Name; }
		}
		private string GetTradingProfileAccount(int idx)
		{
			switch (idx) { case 0: return TradingProfile1Account; case 1: return TradingProfile2Account; case 2: return TradingProfile3Account; case 3: return TradingProfile4Account; case 4: return TradingProfile5Account; case 5: return TradingProfile6Account; case 6: return TradingProfile7Account; default: return TradingProfile8Account; }
		}
		private string GetTradingProfileAtm(int idx)
		{
			switch (idx) { case 0: return TradingProfile1Atm; case 1: return TradingProfile2Atm; case 2: return TradingProfile3Atm; case 3: return TradingProfile4Atm; case 4: return TradingProfile5Atm; case 5: return TradingProfile6Atm; case 6: return TradingProfile7Atm; default: return TradingProfile8Atm; }
		}
		private int GetTradingProfileQuantity(int idx)
		{
			switch (idx) { case 0: return TradingProfile1Quantity; case 1: return TradingProfile2Quantity; case 2: return TradingProfile3Quantity; case 3: return TradingProfile4Quantity; case 4: return TradingProfile5Quantity; case 5: return TradingProfile6Quantity; case 6: return TradingProfile7Quantity; default: return TradingProfile8Quantity; }
		}
		private int GetTradingProfileBufferTicks(int idx)
		{
			switch (idx) { case 0: return TradingProfile1BufferTicks; case 1: return TradingProfile2BufferTicks; case 2: return TradingProfile3BufferTicks; case 3: return TradingProfile4BufferTicks; case 4: return TradingProfile5BufferTicks; case 5: return TradingProfile6BufferTicks; case 6: return TradingProfile7BufferTicks; default: return TradingProfile8BufferTicks; }
		}
		private bool GetTradingProfileDailyMaxDDEnabled(int idx)
		{
			switch (idx) { case 0: return TradingProfile1DailyMaxDDEnabled; case 1: return TradingProfile2DailyMaxDDEnabled; case 2: return TradingProfile3DailyMaxDDEnabled; case 3: return TradingProfile4DailyMaxDDEnabled; case 4: return TradingProfile5DailyMaxDDEnabled; case 5: return TradingProfile6DailyMaxDDEnabled; case 6: return TradingProfile7DailyMaxDDEnabled; default: return TradingProfile8DailyMaxDDEnabled; }
		}
		private double GetTradingProfileDailyMaxDD(int idx)
		{
			switch (idx) { case 0: return TradingProfile1DailyMaxDD; case 1: return TradingProfile2DailyMaxDD; case 2: return TradingProfile3DailyMaxDD; case 3: return TradingProfile4DailyMaxDD; case 4: return TradingProfile5DailyMaxDD; case 5: return TradingProfile6DailyMaxDD; case 6: return TradingProfile7DailyMaxDD; default: return TradingProfile8DailyMaxDD; }
		}
		private bool GetTradingProfileDailyMaxProfitEnabled(int idx)
		{
			switch (idx) { case 0: return TradingProfile1DailyMaxProfitEnabled; case 1: return TradingProfile2DailyMaxProfitEnabled; case 2: return TradingProfile3DailyMaxProfitEnabled; case 3: return TradingProfile4DailyMaxProfitEnabled; case 4: return TradingProfile5DailyMaxProfitEnabled; case 5: return TradingProfile6DailyMaxProfitEnabled; case 6: return TradingProfile7DailyMaxProfitEnabled; default: return TradingProfile8DailyMaxProfitEnabled; }
		}
		private double GetTradingProfileDailyMaxProfit(int idx)
		{
			switch (idx) { case 0: return TradingProfile1DailyMaxProfit; case 1: return TradingProfile2DailyMaxProfit; case 2: return TradingProfile3DailyMaxProfit; case 3: return TradingProfile4DailyMaxProfit; case 4: return TradingProfile5DailyMaxProfit; case 5: return TradingProfile6DailyMaxProfit; case 6: return TradingProfile7DailyMaxProfit; default: return TradingProfile8DailyMaxProfit; }
		}
		private bool IsTradingProfileConfigured(int idx)
		{
			string acc = GetTradingProfileAccount(idx);
			string atm = GetTradingProfileAtm(idx);
			return !string.IsNullOrWhiteSpace(acc) || !string.IsNullOrWhiteSpace(atm);
		}
		private bool IsTradingProfileActive(int idx)
		{
			if (!IsTradingProfileConfigured(idx)) return false;
			if (!string.Equals(cachedBotAccountName ?? string.Empty, GetTradingProfileAccount(idx) ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return false;
			string liveAtm = string.IsNullOrEmpty(cachedBotAtm) || cachedBotAtm.Equals("None", StringComparison.OrdinalIgnoreCase) ? string.Empty : (cachedBotAtm ?? string.Empty);
			string profAtm = string.IsNullOrEmpty(GetTradingProfileAtm(idx)) || GetTradingProfileAtm(idx).Equals("None", StringComparison.OrdinalIgnoreCase) ? string.Empty : (GetTradingProfileAtm(idx) ?? string.Empty);
			if (!string.Equals(liveAtm, profAtm, StringComparison.OrdinalIgnoreCase)) return false;
			int profQty = Math.Max(1, Math.Min(100, GetTradingProfileQuantity(idx)));
			if (BotOrderQuantity != profQty) return false;
			int profBuf = Math.Max(0, Math.Min(100, GetTradingProfileBufferTicks(idx)));
			if (BotBufferTicks != profBuf) return false;
			if (DailyMaxDDEnabled != GetTradingProfileDailyMaxDDEnabled(idx)) return false;
			if (Math.Abs(DailyMaxDD - GetTradingProfileDailyMaxDD(idx)) > 0.0001) return false;
			if (DailyMaxProfitEnabled != GetTradingProfileDailyMaxProfitEnabled(idx)) return false;
			if (Math.Abs(DailyMaxProfit - GetTradingProfileDailyMaxProfit(idx)) > 0.0001) return false;
			return true;
		}
		private void UpdateTradingProfileButtons()
		{
			if (tradingProfileButtons == null) return;
			int uniqueMatch = -1;
			{ int matches=0; for(int j=0;j<8;j++) if(IsTradingProfileActive(j)) {matches++; uniqueMatch=j;} if(matches!=1) uniqueMatch=-1; }
			Brush labelBrush = GetProgramLabelBrush();
			double fs = GetQuickSetFontSize();
			double fsProg = Math.Min(14, fs + 2);
			for (int i=0;i<tradingProfileButtons.Length;i++)
			{
				if (tradingProfileButtons[i]==null) continue;
				bool on = (uniqueMatch!=-1 && i==uniqueMatch) || (uniqueMatch==-1 && activeTradingProfile==i && IsTradingProfileActive(i));
				if (on) { int parity=i%2; tradingProfileButtons[i].Background=profileRowOnBgs[parity]; tradingProfileButtons[i].Foreground=labelBrush; }
				else { tradingProfileButtons[i].Background=profileOffBg; tradingProfileButtons[i].Foreground=labelBrush; }
				tradingProfileButtons[i].FontSize=fsProg;
				string expected=GetTradingProfileName(i);
				if (GetButtonLabel(tradingProfileButtons[i])!=expected) SetButtonLabel(tradingProfileButtons[i],expected);
				tradingProfileButtons[i].HorizontalContentAlignment=HorizontalAlignment.Left;
				tradingProfileButtons[i].Padding=new Thickness(4,0,2,0);
				if (tradingProfileButtons[i].Content is TextBlock _tbU){_tbU.TextAlignment=TextAlignment.Left;_tbU.HorizontalAlignment=HorizontalAlignment.Left;_tbU.Margin=new Thickness(4,0,0,0);_tbU.FontSize=fsProg;_tbU.Foreground=labelBrush;_tbU.Opacity=1;}
				try{
					string tAcc=GetTradingProfileAccount(i); string tAtm=GetTradingProfileAtm(i); if(string.IsNullOrWhiteSpace(tAtm)||tAtm.Equals("None",StringComparison.OrdinalIgnoreCase)) tAtm="None";
					tradingProfileButtons[i].ToolTip=string.Format("{0}: {1} / {2}  Qty {3}  DD {4}  TP {5}",expected,string.IsNullOrWhiteSpace(tAcc)?"(no acc)":tAcc,tAtm,GetTradingProfileQuantity(i),GetTradingProfileDailyMaxDD(i),GetTradingProfileDailyMaxProfit(i));
				}catch{}
			}
		}
		private void ApplyTradingProfile(int idx)
		{
			if(idx<0||idx>=8) return;
			if(activeTradingProfile==idx && (DateTime.UtcNow-lastProfileApplyUtc).TotalMilliseconds<500) return;
			lastProfileApplyUtc=DateTime.UtcNow;
			string acc=GetTradingProfileAccount(idx); string atm=GetTradingProfileAtm(idx);
			if(string.IsNullOrWhiteSpace(acc)&&string.IsNullOrWhiteSpace(atm)){ShowHudStatus(string.Format("Profile {0}: no account/ATM configured (Indicator Settings)",GetTradingProfileName(idx)),Brushes.OrangeRed);return;}
			int qty=Math.Max(1,Math.Min(100,GetTradingProfileQuantity(idx))); BotOrderQuantity=qty;
			int buf=Math.Max(0,Math.Min(100,GetTradingProfileBufferTicks(idx))); BotBufferTicks=buf; cachedBotBufferTicks=buf;
			bool ddEn=GetTradingProfileDailyMaxDDEnabled(idx); double dd=GetTradingProfileDailyMaxDD(idx); DailyMaxDDEnabled=ddEn; DailyMaxDD=dd; cachedIsDailyMaxDD=ddEn; cachedDailyMaxDD=dd;
			bool pfEn=GetTradingProfileDailyMaxProfitEnabled(idx); double pf=GetTradingProfileDailyMaxProfit(idx); DailyMaxProfitEnabled=pfEn; DailyMaxProfit=pf; cachedIsDailyMaxProfit=pfEn; cachedDailyMaxProfit=pf;
			UpdateDailyRiskPresetButtons();
			EvaluateDailyRiskLimits();
			if(!string.IsNullOrWhiteSpace(acc))
			{
				Account target=null; if(Account.All!=null) foreach(Account a in Account.All) if(a.Name.Equals(acc,StringComparison.OrdinalIgnoreCase)){target=a;break;}
				cachedBotAccountName=acc; BotAccountName=acc;
				if(accComboBox!=null)
				{
					bool accFound=false;
					for(int _ai=0;_ai<accComboBox.Items.Count;_ai++) if(accComboBox.Items[_ai].ToString().Equals(acc,StringComparison.OrdinalIgnoreCase)){accComboBox.SelectedIndex=_ai; accFound=true; break;}
					if(!accFound){ accComboBox.Items.Add(acc); accComboBox.SelectedItem=acc; }
				}
				if(target!=null)
				{
					SyncChartTraderAccount(acc);
				}
				else
				{
					ShowHudStatus(string.Format("Profile {0}: account '{1}' not connected yet",GetTradingProfileName(idx),acc),Brushes.Orange);
				}
				try{UpdateAccountInfoSection();}catch{}
			}
			if(!string.IsNullOrWhiteSpace(atm)&&!atm.Equals("None",StringComparison.OrdinalIgnoreCase))
			{
				bool found=false;
				if(atmComboBox!=null){
					for(int i=0;i<atmComboBox.Items.Count;i++) if(atmComboBox.Items[i].ToString().Equals(atm,StringComparison.OrdinalIgnoreCase)){atmComboBox.SelectedIndex=i;found=true;break;}
					if(!found){atmComboBox.Items.Add(atm); atmComboBox.SelectedItem=atm;}
				}
				cachedBotAtm=atm; BotAtmTemplate=atm;
				if(!found && !HasAtmTemplate(atm)) ShowHudStatus(string.Format("Profile {0}: ATM '{1}' not found on disk (still selected)",GetTradingProfileName(idx),atm),Brushes.Orange);
			}
			else
			{
				if(atmComboBox!=null) atmComboBox.SelectedIndex=0;
				cachedBotAtm="None"; BotAtmTemplate="None";
			}
			activeTradingProfile=idx;
			UpdateTradingProfileButtons();
			UpdateAtmSetButtons();
			UpdateDailyRiskPresetButtons();
			bool atmMissing=!string.IsNullOrWhiteSpace(atm)&&!atm.Equals("None",StringComparison.OrdinalIgnoreCase)&&!HasAtmTemplate(atm);
			if(!atmMissing) ShowHudStatus(string.Format("Profile {0} applied: {1} / {2}",GetTradingProfileName(idx),string.IsNullOrWhiteSpace(acc)?"(no acc)":acc,string.IsNullOrWhiteSpace(atm)||atm.Equals("None",StringComparison.OrdinalIgnoreCase)?"None":atm),Brushes.LightGreen);
			try{UpdateAccountInfoSection();}catch{}
		}

		// --- Daily Risk Quick Sets — 6 presets (maxDD + maxProfit) ---
		private string GetDailyRiskPresetName(int idx)
		{
			switch(idx){case 0:return string.IsNullOrWhiteSpace(DailyRiskSet1Name)?"1":DailyRiskSet1Name;case 1:return string.IsNullOrWhiteSpace(DailyRiskSet2Name)?"2":DailyRiskSet2Name;case 2:return string.IsNullOrWhiteSpace(DailyRiskSet3Name)?"3":DailyRiskSet3Name;case 3:return string.IsNullOrWhiteSpace(DailyRiskSet4Name)?"4":DailyRiskSet4Name;case 4:return string.IsNullOrWhiteSpace(DailyRiskSet5Name)?"5":DailyRiskSet5Name;default:return string.IsNullOrWhiteSpace(DailyRiskSet6Name)?"6":DailyRiskSet6Name;}
		}
		private double GetDailyRiskPresetMaxDD(int idx){switch(idx){case 0:return DailyRiskSet1MaxDD;case 1:return DailyRiskSet2MaxDD;case 2:return DailyRiskSet3MaxDD;case 3:return DailyRiskSet4MaxDD;case 4:return DailyRiskSet5MaxDD;default:return DailyRiskSet6MaxDD;}}
		private double GetDailyRiskPresetMaxProfit(int idx){switch(idx){case 0:return DailyRiskSet1MaxProfit;case 1:return DailyRiskSet2MaxProfit;case 2:return DailyRiskSet3MaxProfit;case 3:return DailyRiskSet4MaxProfit;case 4:return DailyRiskSet5MaxProfit;default:return DailyRiskSet6MaxProfit;}}
		private void ApplyDailyRiskPreset(int idx)
		{
			DailyMaxDD=GetDailyRiskPresetMaxDD(idx); DailyMaxProfit=GetDailyRiskPresetMaxProfit(idx); cachedDailyMaxDD=DailyMaxDD; cachedDailyMaxProfit=DailyMaxProfit;
			UpdateDailyRiskPresetButtons();
			try{UpdateTradingProfileButtons();}catch{}
			EvaluateDailyRiskLimits();
			ShowHudStatus(string.Format("DailyRisk {0}: DD ${1} / Profit ${2}",GetDailyRiskPresetName(idx),DailyMaxDD,DailyMaxProfit),Brushes.LightGreen);
		}
		private void UpdateDailyRiskPresetButtons()
		{
			if(dailyRiskPresetButtons==null) return;
			double fs=GetQuickSetFontSize(); double fsUse=Math.Min(14,fs+2);
			for(int i=0;i<dailyRiskPresetButtons.Length;i++){
				if(dailyRiskPresetButtons[i]==null) continue;
				bool on=DailyMaxDD==GetDailyRiskPresetMaxDD(i)&&DailyMaxProfit==GetDailyRiskPresetMaxProfit(i);
				dailyRiskPresetButtons[i].Background=on?dailyRiskPresetOnBg:dailyRiskPresetOffBg;
				dailyRiskPresetButtons[i].Foreground=Brushes.White;
				dailyRiskPresetButtons[i].FontSize=fsUse;
				dailyRiskPresetButtons[i].FontWeight=FontWeights.SemiBold;
				dailyRiskPresetButtons[i].HorizontalContentAlignment=HorizontalAlignment.Center;
				dailyRiskPresetButtons[i].VerticalContentAlignment=VerticalAlignment.Center;
				dailyRiskPresetButtons[i].Padding=new Thickness(1,0,1,0);
				dailyRiskPresetButtons[i].BorderThickness=new Thickness(0);
				string expected=GetDailyRiskPresetName(i);
				try{
					if(!string.Equals(dailyRiskPresetButtons[i].Content as string,expected,StringComparison.Ordinal)) dailyRiskPresetButtons[i].Content=expected;
					if(dailyRiskPresetButtons[i].Content is TextBlock) dailyRiskPresetButtons[i].Content=expected;
				}catch{dailyRiskPresetButtons[i].Content=expected;}
			}
		}
	}
}
