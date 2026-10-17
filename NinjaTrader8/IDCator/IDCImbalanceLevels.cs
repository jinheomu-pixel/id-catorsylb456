//
// IDC Imbalance Levels - stacked diagonal bid/ask imbalances drawn as support / resistance zones.
//
// How it works
//   * Every completed bar's footprint is scanned with a diagonal comparison:
//       buy imbalance  at price P:  Ask(P) >= ratio * Bid(P - 1 level)
//       sell imbalance at price P:  Bid(P) >= ratio * Ask(P + 1 level)
//   * A run of at least "Min stacked levels" consecutive imbalances on the same side forms a zone.
//   * Buy stacks only count as support below the bar's close, sell stacks only as resistance above it.
//   * A zone touched by the very next bar is discarded as noise. Surviving zones extend to the right
//     until price trades back into them, then they stop at the touching bar (optional alert).
//   * Each zone shows its strongest ratio and the stack volume ("6.7x | 350"); stronger ratios are
//     shaded darker.
// Markers are a visualisation of order flow, not buy or sell signals.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.IDCator
{
	public class IDCImbalanceLevels : Indicator
	{
		private sealed class Zone
		{
			public int			Id;
			public bool			IsSupport;
			public long			LowTick;
			public long			HighTick;
			public int			Step;
			public int			StartBar;
			public int			EndBar		= -1;	// bar that touched the zone, -1 while active
			public double		Ratio;
			public double		Volume;
			public bool			Pending;			// waiting for the next bar (noise filter)
			public List<long>	Levels		= new List<long>();
		}

		private readonly object			sync			= new object();
		private readonly List<Zone>		zones			= new List<Zone>();
		private readonly List<IdcPendingAlert> pendingAlerts = new List<IdcPendingAlert>();
		private IdcFootprintEngine		engine;
		private IdcDxBrushCache			dxBrushes;
		private int						lastAnalyzedBar	= -1;
		private int						nextZoneId;
		private bool					calculateWarning;
		private int						touchBar	= -1;
		private double					touchHigh;
		private double					touchLow;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Stacked bid/ask imbalances from footprint data, drawn as support/resistance zones that extend until price returns. Not a buy/sell signal.";
				Name						= "IDC Imbalance Levels";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				DrawOnPricePanel			= true;
				DisplayInDataBox			= false;
				PaintPriceMarkers			= false;
				IsSuspendedWhileInactive	= false;
				ScaleJustification			= ScaleJustification.Right;
				MaximumBarsLookBack			= MaximumBarsLookBack.TwoHundredFiftySix;

				MinImbalanceRatio			= 5.0;
				MinStackedLevels			= 3;
				MinLevelVolume				= 0;
				MinStackVolume				= 0;
				ZeroHandling				= IdcZeroHandling.CountAsMinRatio;
				OnlyBeyondClose				= true;
				DiscardIfNextBarTouches		= false;

				FootprintSource				= IdcFootprintSource.Auto;
				MaxBarsBack					= 3000;
				MaxZones					= 400;

				SupportBrush				= Brushes.LimeGreen;
				ResistanceBrush				= Brushes.Red;
				LabelBrush					= new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
				LabelBrush.Freeze();
				MinOpacity					= 7;
				MaxOpacity					= 45;
				RatioForMaxOpacity			= 15;
				TouchedOpacityPercent		= 60;
				DrawLevelRows				= false;
				ShowLabels					= true;
				ShowTouchedZones			= true;
				ExtendMode					= IdcExtendMode.LastBar;
				LabelFont					= new SimpleFont("Arial", 10);
				ShowWarnings				= true;

				AlertOnTouch				= false;
				AlertSound					= "Alert2.wav";
				AlertRearmSeconds			= 30;
			}
			else if (State == State.DataLoaded)
			{
				engine = new IdcFootprintEngine();
				engine.MaxBarsKept = Math.Max(50, MaxBarsBack + 10);
				engine.Initialize(Bars, TickSize, Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency, FootprintSource);
				dxBrushes = new IdcDxBrushCache();
				lock (sync)
				{
					zones.Clear();
					lastAnalyzedBar	= -1;
					nextZoneId		= 0;
					pendingAlerts.Clear();
				}
			}
			else if (State == State.Historical)
			{
				calculateWarning = Calculate != Calculate.OnEachTick;
				if (calculateWarning)
					Log(Name + ": set Calculate to 'On each tick' for correct footprint data.", LogLevel.Warning);
			}
			else if (State == State.Realtime)
			{
				// market closed (evening / weekend): the last loaded bar is already complete, analyse it now
				int last = CurrentBar;		// read once: a data thread may open the next bar meanwhile
				if (engine != null && IdcBars.IsBarComplete(Bars, last))
					lock (sync)
						AnalyzeClosedBars(last);
			}
			else if (State == State.Terminated)
			{
				if (dxBrushes != null)
				{
					dxBrushes.Dispose();
					dxBrushes = null;
				}
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			if (engine == null)
				return;
			IdcTrade trade;
			lock (sync)		// the footprint store is also read by the analysis (OnBarUpdate, OnStateChange)
				engine.ProcessMarketData(marketDataUpdate, CurrentBar, out trade);
		}

		protected override void OnBarUpdate()
		{
			if (engine == null || CurrentBar < 1)
				return;

			int closed = IdcBars.LastClosedBar(State, Bars.IsTickReplay, CurrentBar, Count);

			lock (sync)
			{
				AnalyzeClosedBars(closed);

				// zones/lines only change when a new bar opened (new zones) or the forming bar made a new high/low
				if (CurrentBar != touchBar || High[0] > touchHigh || Low[0] < touchLow)
				{
					touchBar	= CurrentBar;
					touchHigh	= High[0];
					touchLow	= Low[0];
					CheckTouches(CurrentBar, High[0], Low[0]);
				}
				ConfirmPending(closed);
				Prune();
			}
			FlushAlerts();
		}

		private void AnalyzeClosedBars(int closed)
		{
			while (lastAnalyzedBar < closed)
			{
				lastAnalyzedBar++;
				if (lastAnalyzedBar >= Count - MaxBarsBack)
					AnalyzeBar(lastAnalyzedBar);
			}
		}

		/// <summary>Raises alerts collected under the lock, after it was released (OnRender takes the same lock).</summary>
		private void FlushAlerts()
		{
			List<IdcPendingAlert> copy;
			lock (sync)
			{
				if (pendingAlerts.Count == 0)
					return;
				copy = new List<IdcPendingAlert>(pendingAlerts);
				pendingAlerts.Clear();
			}
			foreach (IdcPendingAlert a in copy)
				Alert(a.Id, a.Priority, a.Message, a.Sound, a.RearmSeconds, a.BackBrush, a.ForeBrush);
		}

		#region Detection
		private void AnalyzeBar(int bar)
		{
			IdcFootprintBar fp = engine.GetBar(bar, true);
			if (fp == null || fp.IsEmpty)
				return;

			double close = Bars.GetClose(bar);
			ScanSide(fp, bar, close, true);
			ScanSide(fp, bar, close, false);
		}

		private void ScanSide(IdcFootprintBar fp, int bar, double close, bool buySide)
		{
			int			step		= Math.Max(1, engine.LevelStep);
			List<long>	run			= new List<long>();
			double		runRatio	= 0;
			double		runVolume	= 0;

			for (long t = fp.LowTick; t <= fp.HighTick + step; t += step)
			{
				double ratio = 0, volume = 0;
				bool isImbalance = t <= fp.HighTick && IsImbalance(fp, t, step, buySide, out ratio, out volume);

				if (isImbalance)
				{
					run.Add(t);
					runRatio	= Math.Max(runRatio, ratio);
					runVolume	+= volume;
					continue;
				}

				if (run.Count >= MinStackedLevels)
					CreateZone(run, runRatio, runVolume, bar, close, buySide, step);

				run.Clear();
				runRatio	= 0;
				runVolume	= 0;
			}
		}

		private bool IsImbalance(IdcFootprintBar fp, long tick, int step, bool buySide, out double ratio, out double volume)
		{
			ratio	= 0;
			volume	= buySide ? fp.Ask(tick) : fp.Bid(tick);
			if (volume <= 0 || volume < MinLevelVolume)
				return false;

			// the diagonal partner must lie inside the bar: the lowest ask / highest bid have nothing to compare with
			long partner = buySide ? tick - step : tick + step;
			if (partner < fp.LowTick || partner > fp.HighTick)
				return false;

			double opposite = buySide ? fp.Bid(partner) : fp.Ask(partner);
			if (opposite <= 0)
			{
				if (ZeroHandling == IdcZeroHandling.Skip)
					return false;
				if (ZeroHandling == IdcZeroHandling.CountAsMinRatio)
				{
					ratio = MinImbalanceRatio;
					return true;
				}
				opposite = 1;
			}

			ratio = volume / opposite;
			return ratio >= MinImbalanceRatio;
		}

		private void CreateZone(List<long> run, double ratio, double volume, int bar, double close, bool buySide, int step)
		{
			if (volume < MinStackVolume)
				return;

			long lowTick	= run[0];
			long highTick	= run[run.Count - 1];

			if (OnlyBeyondClose)
			{
				// buy stacks are support only when they sit below the close, sell stacks resistance only above it
				if (buySide && highTick + step - 1 >= engine.ToTick(close))
					return;
				if (!buySide && lowTick <= engine.ToTick(close))
					return;
			}

			Zone zone = new Zone
			{
				Id			= nextZoneId++,
				IsSupport	= buySide,
				LowTick		= lowTick,
				HighTick	= highTick,
				Step		= step,
				StartBar	= bar,
				Ratio		= ratio,
				Volume		= volume,
				Pending		= DiscardIfNextBarTouches
			};
			zone.Levels.AddRange(run);
			zones.Add(zone);
		}

		private void CheckTouches(int bar, double high, double low)
		{
			for (int i = zones.Count - 1; i >= 0; i--)
			{
				Zone zone = zones[i];
				if (zone.EndBar >= 0 || zone.StartBar >= bar)
					continue;

				double top		= engine.ToPrice(zone.HighTick + zone.Step - 1);
				double bottom	= engine.ToPrice(zone.LowTick);
				// compare in tick space: tick * TickSize can be 1 ulp above the feed price (0.1, 0.01 tick sizes)
				bool touched	= zone.IsSupport ? engine.ToTick(low) <= zone.HighTick + zone.Step - 1 : engine.ToTick(high) >= zone.LowTick;
				if (!touched)
					continue;

				if (zone.Pending && bar == zone.StartBar + 1)
				{
					zones.RemoveAt(i);
					continue;
				}

				zone.EndBar		= bar;
				zone.Pending	= false;

				if (AlertOnTouch && State == State.Realtime)
				{
					string message = string.Format("{0}: price touched {1} imbalance zone {2}-{3} ({4} | {5})",
						Instrument.FullName, zone.IsSupport ? "support" : "resistance",
						Instrument.MasterInstrument.FormatPrice(bottom), Instrument.MasterInstrument.FormatPrice(top),
						IdcDraw.Ratio(zone.Ratio), IdcDraw.Volume(zone.Volume));
					pendingAlerts.Add(new IdcPendingAlert { Id = "IDCImb" + zone.Id, Priority = Priority.Medium, Message = message,
						Sound = IdcSound.Resolve(AlertSound), RearmSeconds = AlertRearmSeconds,
						BackBrush = zone.IsSupport ? Brushes.DarkGreen : Brushes.DarkRed, ForeBrush = Brushes.White });
				}
			}
		}

		private void ConfirmPending(int closedBar)
		{
			foreach (Zone zone in zones)
				if (zone.Pending && zone.StartBar + 1 <= closedBar)
					zone.Pending = false;
		}

		private void Prune()
		{
			if (zones.Count <= MaxZones)
				return;

			// drop the oldest touched zones first, then the oldest active ones
			for (int i = 0; i < zones.Count && zones.Count > MaxZones; )
			{
				if (zones[i].EndBar >= 0)
					zones.RemoveAt(i);
				else
					i++;
			}
			while (zones.Count > MaxZones)
				zones.RemoveAt(0);
		}

		#endregion

		#region Rendering
		public override void OnRenderTargetChanged()
		{
			if (dxBrushes != null)
				dxBrushes.Dispose();
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (IsInHitTest || engine == null || ChartBars == null || dxBrushes == null || RenderTarget == null)
				return;

			int		fromIndex	= ChartBars.FromIndex;
			int		toIndex		= ChartBars.ToIndex;
			float	panelLeft	= ChartPanel.X;
			float	panelRight	= ChartPanel.X + ChartPanel.W;
			double	tick		= engine.TickSize;
			float	halfBar		= (float)Math.Max(1.0, chartControl.GetBarPaintWidth(ChartBars) / 2.0);
			int		lastBar		= ChartBars.Bars != null ? ChartBars.Bars.Count - 1 : toIndex;
			float	activeRight	= ExtendMode == IdcExtendMode.ChartEdge
				? panelRight : chartControl.GetXByBarIndex(ChartBars, lastBar) + halfBar;

			List<Zone> visible = new List<Zone>();
			lock (sync)
			{
				foreach (Zone zone in zones)
				{
					if (zone.StartBar > toIndex)
						continue;
					if (zone.EndBar >= 0 && (zone.EndBar < fromIndex || !ShowTouchedZones))
						continue;
					visible.Add(new Zone
					{
						IsSupport = zone.IsSupport, LowTick = zone.LowTick, HighTick = zone.HighTick, Step = zone.Step,
						StartBar = zone.StartBar, EndBar = zone.EndBar, Ratio = zone.Ratio, Volume = zone.Volume,
						Pending = zone.Pending, Levels = DrawLevelRows ? new List<long>(zone.Levels) : null
					});
				}
			}

			SharpDX.DirectWrite.TextFormat textFormat = ShowLabels && LabelFont != null ? LabelFont.ToDirectWriteTextFormat() : null;
			try
			{
				SharpDX.Direct2D1.Brush textBrush = dxBrushes.Get(RenderTarget, LabelBrush, 1.0);

				foreach (Zone zone in visible)
				{
					float x1 = chartControl.GetXByBarIndex(ChartBars, zone.StartBar) - halfBar;
					float x2 = zone.EndBar >= 0 ? chartControl.GetXByBarIndex(ChartBars, zone.EndBar) + halfBar : activeRight;
					x1 = Math.Max(panelLeft, x1);
					x2 = Math.Min(panelRight, x2);
					if (x2 - x1 < 1)
						continue;

					double	priceTop	= engine.ToPrice(zone.HighTick + zone.Step - 1) + tick * 0.5;
					double	priceBottom	= engine.ToPrice(zone.LowTick) - tick * 0.5;
					float	yTop		= chartScale.GetYByValue(priceTop);
					float	yBottom		= chartScale.GetYByValue(priceBottom);
					if (yBottom < ChartPanel.Y || yTop > ChartPanel.Y + ChartPanel.H)
						continue;	// vertically off-screen
					if (yBottom - yTop < 1)
						yBottom = yTop + 1;

					double strength	= RatioForMaxOpacity > MinImbalanceRatio
						? (zone.Ratio - MinImbalanceRatio) / (RatioForMaxOpacity - MinImbalanceRatio) : 1.0;
					strength		= Math.Max(0, Math.Min(1, strength));
					double opacity	= (MinOpacity + (MaxOpacity - MinOpacity) * strength) / 100.0;
					if (zone.EndBar >= 0)
						opacity *= TouchedOpacityPercent / 100.0;
					if (zone.Pending)
						opacity *= 0.6;

					System.Windows.Media.Brush baseBrush = zone.IsSupport ? SupportBrush : ResistanceBrush;
					SharpDX.Direct2D1.Brush fill	= dxBrushes.Get(RenderTarget, baseBrush, opacity);
					SharpDX.Direct2D1.Brush edge	= dxBrushes.Get(RenderTarget, baseBrush, Math.Min(1.0, opacity * 1.35 + 0.04));

					float rowHeight = Math.Abs(chartScale.GetYByValue(priceBottom) - chartScale.GetYByValue(priceBottom + tick * zone.Step));
					if (DrawLevelRows && zone.Levels != null && rowHeight >= 4 && zone.Levels.Count > 1)
					{
						foreach (long level in zone.Levels)
						{
							float ry1 = chartScale.GetYByValue(engine.ToPrice(level + zone.Step - 1) + tick * 0.5);
							float ry2 = chartScale.GetYByValue(engine.ToPrice(level) - tick * 0.5);
							RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, ry1, x2 - x1, Math.Max(1, ry2 - ry1 - 1)), fill);
						}
					}
					else
						RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, yTop, x2 - x1, yBottom - yTop), fill);

					RenderTarget.DrawRectangle(new SharpDX.RectangleF(x1, yTop, x2 - x1, yBottom - yTop), edge, 1f);

					// like the original, the label is only drawn when it fits inside the zone
					if (textFormat != null)
					{
						string label = IdcDraw.Ratio(zone.Ratio) + " | " + IdcDraw.Volume(zone.Volume);
						IdcDraw.TextIfFits(RenderTarget, textFormat, label, x2 - 5, (yTop + yBottom) / 2f, textBrush, IdcTextAnchor.MiddleRight,
							x2 - x1 - 8, (yBottom - yTop) / 0.8f);
					}
				}

				if (ShowWarnings)
					RenderWarning(textBrush);
			}
			finally
			{
				if (textFormat != null)
					textFormat.Dispose();
			}
		}

		private void RenderWarning(SharpDX.Direct2D1.Brush brush)
		{
			string warning = null;
			if (calculateWarning)
				warning = "set Calculate = On each tick";
			else if (engine.IsMisconfigured)
				warning = engine.SourceDescription;
			else if (!engine.UsesVolumetric && !engine.HasTickReplay)
				warning = "no footprint history - enable Tick Replay or use a Volumetric chart (collecting realtime data)";
			else if (engine.SyntheticQuotes)
				warning = "historical ticks carry no bid/ask - using up/down tick rule";
			if (warning == null)
				return;

			using (SharpDX.DirectWrite.TextFormat format = new SimpleFont("Arial", 10).ToDirectWriteTextFormat())
				IdcDraw.Text(RenderTarget, format, Name + ": " + warning, ChartPanel.X + 6, ChartPanel.Y + 6, brush, IdcTextAnchor.TopLeft);
		}
		#endregion

		#region Properties
		[Range(1.1, 1000)]
		[Display(Name = "Min imbalance ratio", Description = "Aggressive side must exceed the diagonal opposite side by at least this ratio.", Order = 1, GroupName = "1. Detection")]
		public double MinImbalanceRatio { get; set; }

		[Range(2, 50)]
		[Display(Name = "Min stacked levels", Description = "Consecutive imbalanced price levels required to form a zone.", Order = 2, GroupName = "1. Detection")]
		public int MinStackedLevels { get; set; }

		[Range(0, double.MaxValue)]
		[Display(Name = "Min level volume", Description = "Ignore imbalanced levels whose aggressive volume is below this.", Order = 3, GroupName = "1. Detection")]
		public double MinLevelVolume { get; set; }

		[Range(0, double.MaxValue)]
		[Display(Name = "Min stack volume", Description = "Minimum total aggressive volume of the stack (0 = off).", Order = 4, GroupName = "1. Detection")]
		public double MinStackVolume { get; set; }

		[Display(Name = "Zero-value handling", Description = "How a diagonal level with zero opposite volume is treated.", Order = 5, GroupName = "1. Detection")]
		public IdcZeroHandling ZeroHandling { get; set; }

		[Display(Name = "Support below / resistance above close only", Description = "Buy stacks only below the bar close, sell stacks only above it.", Order = 6, GroupName = "1. Detection")]
		public bool OnlyBeyondClose { get; set; }

		[Display(Name = "Discard if next bar touches", Description = "Zones retested by the very next bar are removed as noise.", Order = 7, GroupName = "1. Detection")]
		public bool DiscardIfNextBarTouches { get; set; }

		[Display(Name = "Footprint source", Description = "Auto uses Volumetric bars when the chart is Volumetric, otherwise the trade stream (needs Tick Replay for history).", Order = 1, GroupName = "2. Data")]
		public IdcFootprintSource FootprintSource { get; set; }

		[Range(50, 100000)]
		[Display(Name = "Max bars back", Description = "Only the most recent bars are scanned (performance).", Order = 2, GroupName = "2. Data")]
		public int MaxBarsBack { get; set; }

		[Range(10, 5000)]
		[Display(Name = "Max zones kept", Order = 3, GroupName = "2. Data")]
		public int MaxZones { get; set; }

		[XmlIgnore]
		[Display(Name = "Support (buy) colour", Order = 1, GroupName = "3. Display")]
		public System.Windows.Media.Brush SupportBrush { get; set; }

		[Browsable(false)]
		public string SupportBrushSerialize
		{
			get { return Serialize.BrushToString(SupportBrush); }
			set { SupportBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Resistance (sell) colour", Order = 2, GroupName = "3. Display")]
		public System.Windows.Media.Brush ResistanceBrush { get; set; }

		[Browsable(false)]
		public string ResistanceBrushSerialize
		{
			get { return Serialize.BrushToString(ResistanceBrush); }
			set { ResistanceBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Min opacity %", Description = "Opacity at the minimum ratio.", Order = 3, GroupName = "3. Display")]
		public int MinOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Max opacity %", Description = "Opacity at 'Ratio for max opacity' and above.", Order = 4, GroupName = "3. Display")]
		public int MaxOpacity { get; set; }

		[Range(1, 1000)]
		[Display(Name = "Ratio for max opacity", Order = 5, GroupName = "3. Display")]
		public double RatioForMaxOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Touched zone opacity %", Description = "Opacity of touched zones relative to active ones.", Order = 6, GroupName = "3. Display")]
		public int TouchedOpacityPercent { get; set; }

		[Display(Name = "Show touched zones", Order = 7, GroupName = "3. Display")]
		public bool ShowTouchedZones { get; set; }

		[Display(Name = "Extend active zones to", Order = 7, GroupName = "3. Display")]
		public IdcExtendMode ExtendMode { get; set; }

		[Display(Name = "Draw level rows", Description = "Draw each imbalanced price level as its own row (striped look).", Order = 8, GroupName = "3. Display")]
		public bool DrawLevelRows { get; set; }

		[Display(Name = "Show labels", Description = "Show 'ratio | volume' on each zone.", Order = 9, GroupName = "3. Display")]
		public bool ShowLabels { get; set; }

		[Display(Name = "Label font", Order = 10, GroupName = "3. Display")]
		public SimpleFont LabelFont { get; set; }

		[XmlIgnore]
		[Display(Name = "Label colour", Order = 11, GroupName = "3. Display")]
		public System.Windows.Media.Brush LabelBrush { get; set; }

		[Browsable(false)]
		public string LabelBrushSerialize
		{
			get { return Serialize.BrushToString(LabelBrush); }
			set { LabelBrush = Serialize.StringToBrush(value); }
		}

		[Display(Name = "Show data warnings", Order = 12, GroupName = "3. Display")]
		public bool ShowWarnings { get; set; }

		[Display(Name = "Alert on zone touch", Order = 1, GroupName = "4. Alerts")]
		public bool AlertOnTouch { get; set; }

		[Display(Name = "Alert sound", Description = "File in NinjaTrader's sounds folder or a full path.", Order = 2, GroupName = "4. Alerts")]
		public string AlertSound { get; set; }

		[Range(0, 3600)]
		[Display(Name = "Alert rearm seconds", Order = 3, GroupName = "4. Alerts")]
		public int AlertRearmSeconds { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private IDCator.IDCImbalanceLevels[] cacheIDCImbalanceLevels;
		public IDCator.IDCImbalanceLevels IDCImbalanceLevels()
		{
			return IDCImbalanceLevels(Input);
		}

		public IDCator.IDCImbalanceLevels IDCImbalanceLevels(ISeries<double> input)
		{
			if (cacheIDCImbalanceLevels != null)
				for (int idx = 0; idx < cacheIDCImbalanceLevels.Length; idx++)
					if (cacheIDCImbalanceLevels[idx] != null &&  cacheIDCImbalanceLevels[idx].EqualsInput(input))
						return cacheIDCImbalanceLevels[idx];
			return CacheIndicator<IDCator.IDCImbalanceLevels>(new IDCator.IDCImbalanceLevels(), input, ref cacheIDCImbalanceLevels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.IDCator.IDCImbalanceLevels IDCImbalanceLevels()
		{
			return indicator.IDCImbalanceLevels(Input);
		}

		public Indicators.IDCator.IDCImbalanceLevels IDCImbalanceLevels(ISeries<double> input )
		{
			return indicator.IDCImbalanceLevels(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.IDCator.IDCImbalanceLevels IDCImbalanceLevels()
		{
			return indicator.IDCImbalanceLevels(Input);
		}

		public Indicators.IDCator.IDCImbalanceLevels IDCImbalanceLevels(ISeries<double> input )
		{
			return indicator.IDCImbalanceLevels(input);
		}
	}
}

#endregion
