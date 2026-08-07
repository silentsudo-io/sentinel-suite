// ─────────────────────────────────────────────────────────────────────────────
// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain
// one at https://mozilla.org/MPL/2.0/.
//
// Copyright (c) 2026 silentsudo-io and the Sentinel Suite contributors.
//
// PROVENANCE: the author's own work (DD / GodTrades) — self-derived, original.
// A standalone indicator-only "test rig" analysis tool (feed/render lag meter).
// NOT a Council signal: no SentinelCore State seam, no Council voter, no hidden Signal plot.
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using NinjaTrader.NinjaScript.AddOns.Sentinel;
using NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors;
namespace NinjaTrader.NinjaScript.Indicators.Sentinel.Sensors
{
	public class SentinelLagCheck_v1_0_0 : Indicator
	{
		
		protected override void OnStateChange()
		{
			if (base.State == State.SetDefaults)
			{
				base.Description = "Lag Generator";
				base.Name = "Sentinel Lag Check v1.0.0";
				base.Calculate = Calculate.OnBarClose;
				base.IsOverlay = true;
				base.DisplayInDataBox = true;
				base.DrawOnPricePanel = true;
				base.PaintPriceMarkers = true;
				base.ScaleJustification = ScaleJustification.Right;
				base.IsSuspendedWhileInactive = false;
				this.ShowIndicatorLabel = false;
				this.MeasurInterv = 1;
				this.Rend_Iteration = 0;
				this.OMDt_Iteration = 0;
				this.AtrPeriod = 14;
				this.PrLines = true;
				this.ExtPrLines = true;
				this.LinesWdth = 3;
				this.LineBrush = Brushes.Yellow;
				this.LineOpacity = 100;
				this.RealLineBrush = Brushes.Magenta;
				this.RealLineOpacity = 100;
				return;
			}
			if (base.State == State.Configure)
			{
				base.Name = "Sentinel Lag Check v1.0.0";
				if (base.Calculate != Calculate.OnBarClose)
				{
					base.Calculate = Calculate.OnBarClose;
				}
				this.SmallRectBrush.Freeze();
				this.SmallFilBrush.Freeze();
				this.TxtBrush.Freeze();
				this.stopWatch2 = new Stopwatch();
				this.stopWatch3 = new Stopwatch();
				this.stopWatch4 = new Stopwatch();
				this.LastPrice = (this.GCurrentLast = 0.0);
				this.AtrValue = (this.PrevAtr = 0.0);
				this.AtrTicks = 0;
				this.cpuUsage = (this.GPUUse = 0);
				this.FirstLoad = false;
				return;
			}
			if (base.State == State.Transition)
			{
				if (!this.IsToolBarButtonAdded)
				{
					this.IndiHandler();
					return;
				}
			}
			else
			{
				if (base.State == State.DataLoaded)
				{
					if (!this.ShowIndicatorLabel) base.Name = string.Empty;
					this.TxtFormat = new TextFormat(Globals.DirectWriteFactory, "Arial", global::SharpDX.DirectWrite.FontWeight.Normal, global::SharpDX.DirectWrite.FontStyle.Normal, 11f);
					this.TxtFormat.TextAlignment = global::SharpDX.DirectWrite.TextAlignment.Leading;
					this.TxtFormat.ParagraphAlignment = ParagraphAlignment.Center;
					this.TxtFormat.WordWrapping = WordWrapping.NoWrap;
					this.ValFormat = new TextFormat(Globals.DirectWriteFactory, "Arial", global::SharpDX.DirectWrite.FontWeight.Normal, global::SharpDX.DirectWrite.FontStyle.Normal, 11f);
					this.ValFormat.TextAlignment = global::SharpDX.DirectWrite.TextAlignment.Trailing;
					this.ValFormat.ParagraphAlignment = ParagraphAlignment.Center;
					this.ValFormat.WordWrapping = WordWrapping.NoWrap;
					this.LineVec1 = default(Vector2);
					this.LineVec2 = default(Vector2);
					this.RealLineVec1 = default(Vector2);
					this.RealLineVec2 = default(Vector2);
					this.RenderLagTime = " " + this.Rend_Iteration.ToString("F1") + " ms ";
					return;
				}
				if (base.State == State.Terminated)
				{
					if (this.TxtFormat != null)
					{
						this.TxtFormat.Dispose();
					}
					if (this.ValFormat != null)
					{
						this.ValFormat.Dispose();
					}
					if (this.timer != null)
					{
						this.timer.Stop();
						this.timer.Tick -= this.Timer_Tick;
					}
					if (this.cpuCounter != null)
					{
						this.cpuCounter.Close();
						this.cpuCounter.Dispose();
					}
					if (!this.gpuCounters.IsNullOrEmpty())
					{
						foreach (PerformanceCounter performanceCounter in this.gpuCounters)
						{
							performanceCounter.Close();
							performanceCounter.Dispose();
						}
						this.gpuCounters = null;
					}
					this.DisposeCleanUp();
				}
			}
		}
		private void DisposeCleanUp()
		{
			if (base.ChartControl != null)
			{
				base.ChartControl.Dispatcher.InvokeAsync(delegate
				{
					base.ChartControl.MouseLeftButtonDown -= this.OnMouseLeftDown;
					base.ChartControl.MouseLeftButtonUp -= this.OnMouseLeftUp;
				});
			}
		}
		public void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
		{
		}
		public void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
		{
		}
		private void IndiHandler()
		{
			if (base.ChartControl == null)
			{
				return;
			}
			this.cpuCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName, true);
			int id = Process.GetCurrentProcess().Id;
			string text = "GPU Engine";
			string text2 = "Utilization Percentage";
			string text3 = "pid_" + id.ToString();
			if (PerformanceCounterCategory.Exists(text))
			{
				PerformanceCounterCategory performanceCounterCategory = new PerformanceCounterCategory(text);
				string[] instanceNames = performanceCounterCategory.GetInstanceNames();
				this.gpuCounters = new List<PerformanceCounter>();
				foreach (string text4 in instanceNames)
				{
					if (text4.Contains(text3))
					{
						try
						{
							PerformanceCounter performanceCounter = new PerformanceCounter(text, text2, text4, true);
							this.gpuCounters.Add(performanceCounter);
						}
						catch (Exception ex)
						{
							base.Print("GPU Reading error : " + ex.Message);
						}
					}
				}
			}
			Application.Current.Dispatcher.InvokeAsync(delegate
			{
				this.timer = new DispatcherTimer();
				this.timer.Interval = TimeSpan.FromSeconds((double)this.MeasurInterv);
				this.timer.Tick += this.Timer_Tick;
				this.timer.Start();
			});
			base.ChartControl.Dispatcher.InvokeAsync(delegate
			{
				base.ChartControl.MouseLeftButtonDown += this.OnMouseLeftDown;
				base.ChartControl.MouseLeftButtonUp += this.OnMouseLeftUp;
			});
			this.IsToolBarButtonAdded = true;
		}
		private async void Timer_Tick(object sender, EventArgs e)
		{
			this.cpuUsage = await Task.Run<int>(() => (int)Math.Round((double)(this.cpuCounter.NextValue() / (float)Environment.ProcessorCount), 0, MidpointRounding.AwayFromZero));
			this.UsageCP = " " + this.cpuUsage.ToString() + " % ";
			float totalGpuUsage = 0f;
			this.GPUUse = 0;
			if (!this.gpuCounters.IsNullOrEmpty())
			{
				totalGpuUsage = await this.GetTotalGpuUsageAsync(this.gpuCounters);
			}
			if (totalGpuUsage < 0.8f)
			{
				this.GPUUse = (int)totalGpuUsage;
			}
			else
			{
				this.GPUUse = (int)Math.Round((double)totalGpuUsage, 0, MidpointRounding.AwayFromZero);
			}
			this.UsageGP = " " + this.GPUUse.ToString() + " % ";
			double PriceDiff = 0.0;
			if (base.Instrument.MarketData.Last != null)
			{
				this.GCurrentLast = base.Instrument.MarketData.Last.Price;
				if (this.LastPrice != 0.0)
				{
					PriceDiff = Math.Abs(this.LastPrice - this.GCurrentLast);
				}
			}
			this.CashValue = Math.Round(base.Instrument.MasterInstrument.PointValue * PriceDiff, 1, MidpointRounding.AwayFromZero);
			this.LagCashStr = " " + this.CashValue.ToString("F1") + " $ ";
			this.OMD_Delay_Time = Math.Abs(this.OMD_TS.TotalSeconds);
			if (this.OMD_Delay_Time < 60.0)
			{
				this.OMDDelayTime = " " + this.OMD_Delay_Time.ToString("F2") + " sec ";
			}
			else
			{
				this.OMDDelayTime = " " + Math.Round(this.OMD_Delay_Time / 60.0, 1, MidpointRounding.AwayFromZero).ToString("F1") + " min ";
			}
			if (this.OMD_Delay_Time >= 1.0)
			{
				this.CriticalCPU = Math.Min(99, this.cpuUsage);
				this.CriticalCPUStr = " " + this.CriticalCPU.ToString() + " % ";
			}
			this.OMD_Time2 = this.OMD_Time;
			this.OMDTime = " " + this.OMD_Time2.ToString("F1") + " ms ";
			if (base.ChartControl != null)
			{
				base.ChartControl.Dispatcher.InvokeAsync(delegate
				{
					this.stopWatch3.Reset();
					this.stopWatch3.Start();
					base.ChartControl.InvalidateVisual();
					this.stopWatch3.Stop();
					this.Render_Time = Math.Round(this.stopWatch3.Elapsed.TotalMilliseconds, 1, MidpointRounding.AwayFromZero);
					this.RenderTime = " " + this.Render_Time.ToString("F1") + " ms ";
				});
				if (this.Render_Time >= 1.0)
				{
					this.FpsVal = Math.Round(1000.0 / Math.Max(1.0, this.Render_Time), 1, MidpointRounding.AwayFromZero);
					this.FpsStr = " " + this.FpsVal.ToString() + " ";
				}
			}
		}
		private async Task<float> GetTotalGpuUsageAsync(List<PerformanceCounter> counters)
		{
			float totalGpuUsage0 = 0f;
			List<Task<float>> tasks = new List<Task<float>>();
			using (List<PerformanceCounter>.Enumerator enumerator = counters.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					PerformanceCounter counter = enumerator.Current;
					tasks.Add(Task.Run<float>(delegate
					{
						float num;
						try
						{
							num = counter.NextValue();
						}
						catch (Exception ex)
						{
							this.Print("counter Reading error: " + ex.Message);
							num = 0f;
						}
						return num;
					}));
				}
			}
			float[] results = await Task.WhenAll<float>(tasks);
			totalGpuUsage0 = results.Sum();
			return totalGpuUsage0;
		}
		public override string DisplayName
		{
			get
			{
				return "Sentinel Lag Check v1.0.0";
			}
		}
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (base.State != State.Realtime)
			{
				return;
			}
			this.OMD_TS = new TimeSpan(Globals.Now.ToUniversalTime().Ticks - e.Time.ToUniversalTime().Ticks);
			if (e.MarketDataType == MarketDataType.Last)
			{
				this.LastPrice = e.Price;
				this.stopWatch2.Reset();
				this.stopWatch2.Start();
				if (this.OMDt_Iteration > 0)
				{
					this.TmpOmdt = 0.0;
					int i = 0;
					double num = 0.0;
					int num2 = 0;
					while (i < this.OMDt_Iteration)
					{
						num += Math.Sin((double)num2) * Math.Cos((double)num2);
						num *= 3.141592653589793;
						num /= 2.718281828459045;
						num2++;
						i = (int)this.stopWatch2.ElapsedMilliseconds;
					}
					this.TmpOmdt = num;
				}
				this.stopWatch2.Stop();
				this.OMD_Time = Math.Round(this.stopWatch2.Elapsed.TotalMilliseconds, 1, MidpointRounding.AwayFromZero);
			}
		}
		protected override void OnBarUpdate()
		{
			if (base.ChartBars == null || base.ChartControl == null || base.Bars == null || base.Instrument == null || base.ChartBars.Count < 2)
			{
				return;
			}
			double num = base.High[0];
			double num2 = base.Low[0];
			if (base.CurrentBar == 0)
			{
				this.AtrValue = num - num2;
			}
			else
			{
				double num3 = base.Close[1];
				double num4 = Math.Max(Math.Abs(num2 - num3), Math.Max(num - num2, Math.Abs(num - num3)));
				this.AtrValue = ((double)(Math.Min(base.CurrentBar + 1, this.AtrPeriod) - 1) * this.PrevAtr + num4) / (double)Math.Min(base.CurrentBar + 1, this.AtrPeriod);
			}
			if (base.CurrentBar >= base.ChartBars.Count - 2)
			{
				this.AtrTicks = (int)Math.Round(this.AtrValue / base.Instrument.MasterInstrument.TickSize, 0, MidpointRounding.AwayFromZero);
				this.AtrStr = " " + this.AtrTicks.ToString() + " ticks ";
			}
			this.PrevAtr = this.AtrValue;
		}
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (base.Bars == null || base.ChartBars == null || base.ChartControl == null || base.IsInHitTest)
			{
				return;
			}
			if (!this.FirstLoad)
			{
				base.SetZOrder(base.ChartBars.ZOrder + 659);
				this.FirstLoad = true;
			}
			const float fldW = 95f;
			const float rowH = 22f;
			float baseY = (float)base.ChartPanel.H - 10f - rowH;
			float x = 10f;
			this.Atr1Rect     = new RectangleF(x, baseY, fldW,  rowH); x += fldW;
			this.Mdt1Rect     = new RectangleF(x, baseY, fldW,  rowH); x += fldW;
			this.RendLag1Rect = new RectangleF(x, baseY, 105f,  rowH); x += 105f;
			this.GP1Rect      = new RectangleF(x, baseY, fldW,  rowH); x += fldW;
			this.CP1Rect      = new RectangleF(x, baseY, fldW,  rowH); x += fldW;
			this.CrCP1Rect    = new RectangleF(x, baseY, 115f,  rowH); x += 115f;
			this.DtLg1Rect    = new RectangleF(x, baseY, 105f,  rowH); x += 105f;
			this.DtCsh1Rect   = new RectangleF(x, baseY, fldW,  rowH); x += fldW;
			this.Rend1Rect    = new RectangleF(x, baseY, 115f,  rowH); x += 115f;
			this.Fps1Rect     = new RectangleF(x, baseY, fldW,  rowH); x += fldW;
			this.Result1Rect  = new RectangleF(x, baseY, 135f,  rowH);
				if (this.PrLines && base.ChartBars.ToIndex >= base.ChartBars.Count - 1 && this.LastPrice != 0.0 && base.Instrument.MarketData.Last != null)
			{
				double price = base.Instrument.MarketData.Last.Price;
				int ybyValue = chartScale.GetYByValue(this.LastPrice);
				int ybyValue2 = chartScale.GetYByValue(price);
				this.LineVec1.X = (float)(this.ExtPrLines ? base.ChartPanel.X : (chartControl.GetXByBarIndex(base.ChartBars, base.ChartBars.Count - 1) + 5));
				this.LineVec1.Y = (float)ybyValue;
				this.LineVec2.X = (float)(base.ChartPanel.X + base.ChartPanel.W);
				this.LineVec2.Y = (float)ybyValue;
				base.RenderTarget.DrawLine(this.LineVec1, this.LineVec2, this.LineBrushDX, (float)this.LinesWdth, null);
				if (ybyValue != ybyValue2)
				{
					this.RealLineVec1.X = this.LineVec1.X;
					this.RealLineVec1.Y = (float)ybyValue2;
					this.RealLineVec2.X = this.LineVec2.X;
					this.RealLineVec2.Y = (float)ybyValue2;
					base.RenderTarget.DrawLine(this.RealLineVec1, this.RealLineVec2, this.RealLineBrushDX, (float)this.LinesWdth, null);
				}
			}
			base.RenderTarget.FillRectangle(this.Atr1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" ATR:", this.TxtFormat, this.Atr1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.AtrStr, this.ValFormat, new RectangleF(this.Atr1Rect.X, this.Atr1Rect.Y, this.Atr1Rect.Width - 5f, this.Atr1Rect.Height), this.BlackBrushDX);
			base.RenderTarget.FillRectangle(this.Mdt1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" +Mkt:", this.TxtFormat, this.Mdt1Rect, this.TxtBrushDX);
			base.RenderTarget.DrawText(this.OMDTime, this.ValFormat, new RectangleF(this.Mdt1Rect.X, this.Mdt1Rect.Y, this.Mdt1Rect.Width - 5f, this.Mdt1Rect.Height), this.OMD_Time2 > 0.0 ? this.CrimsonBrushDX : this.TxtBrushDX);
			base.RenderTarget.FillRectangle(this.RendLag1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" \u2395 Lag:", this.TxtFormat, this.RendLag1Rect, this.TxtBrushDX);
			base.RenderTarget.DrawText(this.RenderLagTime, this.ValFormat, new RectangleF(this.RendLag1Rect.X, this.RendLag1Rect.Y, this.RendLag1Rect.Width - 5f, this.RendLag1Rect.Height), this.Rend_Iteration > 0 ? this.CrimsonBrushDX : this.TxtBrushDX);
			base.RenderTarget.FillRectangle(this.GP1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" GPU:", this.TxtFormat, this.GP1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.UsageGP, this.ValFormat, new RectangleF(this.GP1Rect.X, this.GP1Rect.Y, this.GP1Rect.Width - 5f, this.GP1Rect.Height), this.GPUUse > 20 ? this.CrimsonBrushDX : this.BlackBrushDX);
			base.RenderTarget.FillRectangle(this.CP1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" CPU:", this.TxtFormat, this.CP1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.UsageCP, this.ValFormat, new RectangleF(this.CP1Rect.X, this.CP1Rect.Y, this.CP1Rect.Width - 5f, this.CP1Rect.Height), this.cpuUsage > 20 ? this.CrimsonBrushDX : this.BlackBrushDX);
			base.RenderTarget.FillRectangle(this.CrCP1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" \u26A0 CPU:", this.TxtFormat, this.CrCP1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.CriticalCPUStr, this.ValFormat, new RectangleF(this.CrCP1Rect.X, this.CrCP1Rect.Y, this.CrCP1Rect.Width - 5f, this.CrCP1Rect.Height), this.CrimsonBrushDX);
			base.RenderTarget.FillRectangle(this.DtLg1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" \u231B Lag:", this.TxtFormat, this.DtLg1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.OMDDelayTime, this.ValFormat, new RectangleF(this.DtLg1Rect.X, this.DtLg1Rect.Y, this.DtLg1Rect.Width - 5f, this.DtLg1Rect.Height), this.OMD_Delay_Time >= 1.0 ? this.CrimsonBrushDX : this.BlackBrushDX);
			base.RenderTarget.FillRectangle(this.DtCsh1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" \u231B $:", this.TxtFormat, this.DtCsh1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.LagCashStr, this.ValFormat, new RectangleF(this.DtCsh1Rect.X, this.DtCsh1Rect.Y, this.DtCsh1Rect.Width - 5f, this.DtCsh1Rect.Height), this.CashValue > 0.0 ? this.CrimsonBrushDX : this.BlackBrushDX);
			base.RenderTarget.FillRectangle(this.Rend1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" Render:", this.TxtFormat, this.Rend1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.RenderTime, this.ValFormat, new RectangleF(this.Rend1Rect.X, this.Rend1Rect.Y, this.Rend1Rect.Width - 5f, this.Rend1Rect.Height), this.Render_Time > 100.0 ? this.CrimsonBrushDX : this.BlackBrushDX);
			base.RenderTarget.FillRectangle(this.Fps1Rect, this.SmallFilBrushDX);
			base.RenderTarget.DrawText(" FPS:", this.TxtFormat, this.Fps1Rect, this.BlackBrushDX);
			base.RenderTarget.DrawText(this.FpsStr, this.ValFormat, new RectangleF(this.Fps1Rect.X, this.Fps1Rect.Y, this.Fps1Rect.Width - 5f, this.Fps1Rect.Height), this.FpsVal < 10.0 ? this.CrimsonBrushDX : this.BlackBrushDX);
			{
				global::SharpDX.Direct2D1.Brush resFill;
				global::SharpDX.Direct2D1.Brush resTxt;
				string resVal;
				if (this.OMD_Delay_Time >= 1.0)
				{
					resFill = this.FpsVal < 5.0 ? this.CrimsonBrushDX : this.PeruBrushDX;
					resTxt  = this.WhiteBrushDX;
					resVal  = this.FpsVal < 5.0 ? this.DangerStr : this.BadStr;
				}
				else if (this.FpsVal < 5.0)  { resFill = this.CrimsonBrushDX;   resTxt = this.WhiteBrushDX; resVal = this.DangerStr; }
				else if (this.FpsVal < 10.0) { resFill = this.PeruBrushDX;      resTxt = this.WhiteBrushDX; resVal = this.BadStr; }
				else if (this.FpsVal < 20.0) { resFill = this.CadetBlueBrushDX; resTxt = this.BlackBrushDX; resVal = this.NormStr; }
				else                         { resFill = this.GreenBrushDX;     resTxt = this.WhiteBrushDX; resVal = this.ExlStr; }
				base.RenderTarget.FillRectangle(this.Result1Rect, resFill);
				base.RenderTarget.DrawText(" Result:", this.TxtFormat, this.Result1Rect, resTxt);
				base.RenderTarget.DrawText(resVal, this.ValFormat, new RectangleF(this.Result1Rect.X, this.Result1Rect.Y, this.Result1Rect.Width - 5f, this.Result1Rect.Height), resTxt);
			}
			if (this.Rend_Iteration > 0)
			{
				this.stopWatch4.Reset();
				this.stopWatch4.Start();
				this.TmpRender = 0.0;
				int i = 0;
				double num = 0.0;
				int num2 = 0;
				while (i < this.Rend_Iteration)
				{
					num += Math.Sin((double)num2) * Math.Cos((double)num2);
					num *= 3.141592653589793;
					num /= 2.718281828459045;
					num2++;
					i = (int)this.stopWatch4.ElapsedMilliseconds;
				}
				this.TmpRender = num;
				this.stopWatch4.Stop();
			}
		}
		public override void OnRenderTargetChanged()
		{
			if (this.SmallRectBrushDX != null)
			{
				this.SmallRectBrushDX.Dispose();
			}
			if (this.SmallFilBrushDX != null)
			{
				this.SmallFilBrushDX.Dispose();
			}
			if (this.TxtBrushDX != null)
			{
				this.TxtBrushDX.Dispose();
			}
			if (this.LineBrushDX != null)
			{
				this.LineBrushDX.Dispose();
			}
			if (this.RealLineBrushDX != null)
			{
				this.RealLineBrushDX.Dispose();
			}
			if (this.BlackBrushDX != null)
			{
				this.BlackBrushDX.Dispose();
			}
			if (this.WhiteBrushDX != null)
			{
				this.WhiteBrushDX.Dispose();
			}
			if (this.CrimsonBrushDX != null)
			{
				this.CrimsonBrushDX.Dispose();
			}
			if (this.GreenBrushDX != null)
			{
				this.GreenBrushDX.Dispose();
			}
			if (this.PeruBrushDX != null)
			{
				this.PeruBrushDX.Dispose();
			}
			if (this.CadetBlueBrushDX != null)
			{
				this.CadetBlueBrushDX.Dispose();
			}
			if (base.RenderTarget != null)
			{
				this.LineBrushDX = this.LineBrush.ToDxBrush(base.RenderTarget);
				this.LineBrushDX.Opacity = (float)this.LineOpacity / 100f;
				this.RealLineBrushDX = this.RealLineBrush.ToDxBrush(base.RenderTarget);
				this.RealLineBrushDX.Opacity = (float)this.RealLineOpacity / 100f;
				this.SmallRectBrushDX = this.SmallRectBrush.ToDxBrush(base.RenderTarget);
				this.SmallRectBrushDX.Opacity = 0.25f;
				this.SmallFilBrushDX = this.SmallFilBrush.ToDxBrush(base.RenderTarget);
				this.SmallFilBrushDX.Opacity = 0.25f;
				this.TxtBrushDX = this.TxtBrush.ToDxBrush(base.RenderTarget);
				this.BlackBrushDX = new global::SharpDX.Direct2D1.SolidColorBrush(base.RenderTarget, new global::SharpDX.Color4(0.96f, 0.96f, 0.96f, 1f)); // WhiteSmoke
				this.WhiteBrushDX = new global::SharpDX.Direct2D1.SolidColorBrush(base.RenderTarget, global::SharpDX.Color.White);
				this.CrimsonBrushDX = new global::SharpDX.Direct2D1.SolidColorBrush(base.RenderTarget, global::SharpDX.Color.Crimson);
				this.GreenBrushDX = new global::SharpDX.Direct2D1.SolidColorBrush(base.RenderTarget, global::SharpDX.Color.Green);
				this.PeruBrushDX = new global::SharpDX.Direct2D1.SolidColorBrush(base.RenderTarget, global::SharpDX.Color.Peru);
				this.CadetBlueBrushDX = new global::SharpDX.Direct2D1.SolidColorBrush(base.RenderTarget, global::SharpDX.Color.CadetBlue);
			}
		}
		[NinjaScriptProperty]
		[Display(Name = "Show Indicator Label", Order = 0, GroupName = "Sentinel")]
		public bool ShowIndicatorLabel
		{
			get;
			set;
		}
		[Display(Name = "measurement interval, sec", Order = 0, GroupName = "Parameters")]
		[Range(1, 5)]
		[NinjaScriptProperty]
		public int MeasurInterv
		{
			get;
			set;
		}
		[Display(Name = "Add Market Data Lag Time, ms", Order = 1, GroupName = "Parameters")]
		[Range(0, 3000)]
		[NinjaScriptProperty]
		public int OMDt_Iteration
		{
			get;
			set;
		}
		[NinjaScriptProperty]
		[Display(Name = "Add Render Lag Time, ms", Order = 2, GroupName = "Parameters")]
		[Range(0, 999)]
		public int Rend_Iteration
		{
			get;
			set;
		}
		[NinjaScriptProperty]
		[Range(1, 9999)]
		[Display(Name = "ATR Period", Order = 3, GroupName = "Parameters")]
		public int AtrPeriod
		{
			get;
			set;
		}
		[Display(Name = "Price Lines", Order = 4, GroupName = "Parameters")]
		[NinjaScriptProperty]
		public bool PrLines
		{
			get;
			set;
		}
		[NinjaScriptProperty]
		[Range(1, 30)]
		[Display(Name = "Lines Width", Order = 5, GroupName = "Parameters")]
		public int LinesWdth
		{
			get;
			set;
		}
		[NinjaScriptProperty]
		[Display(Name = "Extend Price Lines to Left", Order = 6, GroupName = "Parameters")]
		public bool ExtPrLines
		{
			get;
			set;
		}
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Market Line Color", Order = 7, GroupName = "Parameters")]
		public global::System.Windows.Media.Brush LineBrush
		{
			get;
			set;
		}
		[Browsable(false)]
		public string LineBrushSerializable
		{
			get
			{
				return Serialize.BrushToString(this.LineBrush);
			}
			set
			{
				this.LineBrush = Serialize.StringToBrush(value);
			}
		}
		[NinjaScriptProperty]
		[Display(Name = "Market Line Opacity", Order = 8, GroupName = "Parameters")]
		[Range(1, 100)]
		public int LineOpacity
		{
			get;
			set;
		}
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Real Line Color", Order = 9, GroupName = "Parameters")]
		public global::System.Windows.Media.Brush RealLineBrush
		{
			get;
			set;
		}
		[Browsable(false)]
		public string RealLineBrushSerializable
		{
			get
			{
				return Serialize.BrushToString(this.RealLineBrush);
			}
			set
			{
				this.RealLineBrush = Serialize.StringToBrush(value);
			}
		}
		[Display(Name = "Real Line Opacity", Order = 10, GroupName = "Parameters")]
		[Range(1, 100)]
		[NinjaScriptProperty]
		public int RealLineOpacity
		{
			get;
			set;
		}
		private Stopwatch stopWatch2;
		private Stopwatch stopWatch3;
		private Stopwatch stopWatch4;
		private string UsageCP = " 0 % ";
		private string UsageGP = " 0 % ";
		private string OMDTime = " 0.0 ms ";
		private string RenderLagTime = " 0.0 ms ";
		private string RenderTime = " 0.0 ms ";
		private string OMDDelayTime = " 0.00 sec ";
		private string CriticalCPUStr = " 0 % ";
		private double OMD_Time;
		private double OMD_Time2;
		private double Render_Time;
		private double TmpRender;
		private double TmpOmdt;
		private double OMD_Delay_Time;
		private TimeSpan OMD_TS;
		private DispatcherTimer timer;
		private PerformanceCounter cpuCounter;
		private bool IsToolBarButtonAdded;
		private List<PerformanceCounter> gpuCounters;
		private double LastPrice;
		private double GCurrentLast;
		private double CashValue;
		private string LagCashStr = " 0.0 $ ";
		private int CriticalCPU;
		private double AtrValue;
		private double PrevAtr;
		private int AtrTicks;
		private string AtrStr = " 0 ticks ";
		private double FpsVal;
		private string FpsStr = " 0 ";
		private string DangerStr = " \u2620 Danger! ";
		private string BadStr = " \u2717 Bad ";
		private string NormStr = " \u2713 Good ";
		private string ExlStr = " \u2605 Excellent! ";
		private global::SharpDX.Direct2D1.Brush LineBrushDX;
		private global::SharpDX.Direct2D1.Brush RealLineBrushDX;
		private Vector2 LineVec1;
		private Vector2 LineVec2;
		private Vector2 RealLineVec1;
		private Vector2 RealLineVec2;
		private global::System.Windows.Media.Brush SmallRectBrush = new global::System.Windows.Media.SolidColorBrush(global::System.Windows.Media.Color.FromRgb(20, 20, 20));
		private global::System.Windows.Media.Brush SmallFilBrush = new global::System.Windows.Media.SolidColorBrush(global::System.Windows.Media.Color.FromRgb(20, 20, 20));
		private global::System.Windows.Media.Brush TxtBrush = Brushes.WhiteSmoke;
		private global::SharpDX.Direct2D1.Brush SmallRectBrushDX;
		private global::SharpDX.Direct2D1.Brush SmallFilBrushDX;
		private global::SharpDX.Direct2D1.Brush TxtBrushDX;
		private global::SharpDX.Direct2D1.Brush BlackBrushDX;
		private global::SharpDX.Direct2D1.Brush WhiteBrushDX;
		private global::SharpDX.Direct2D1.Brush CrimsonBrushDX;
		private global::SharpDX.Direct2D1.Brush GreenBrushDX;
		private global::SharpDX.Direct2D1.Brush PeruBrushDX;
		private global::SharpDX.Direct2D1.Brush CadetBlueBrushDX;
		private RectangleF Atr1Rect;
		private RectangleF Mdt1Rect;
		private RectangleF GP1Rect;
		private RectangleF CP1Rect;
		private RectangleF CrCP1Rect;
		private RectangleF DtLg1Rect;
		private RectangleF DtCsh1Rect;
		private RectangleF Rend1Rect;
		private RectangleF Fps1Rect;
		private RectangleF Result1Rect;
		private RectangleF RendLag1Rect;
		private TextFormat TxtFormat;
		private TextFormat ValFormat;
		private int cpuUsage;
		private int GPUUse;
		private bool FirstLoad;
	}
}
