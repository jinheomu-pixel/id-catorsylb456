//
// IDC Heavy Absorption - exceptionally large aggressive prints absorbed by passive liquidity.
//
// How it works
//   * Every trade print is classified as an aggressive buy (at the ask) or sell (at the bid).
//   * The detection threshold is statistical: mean + k * standard deviation of all print sizes inside the
//     lookback window (default 100 bars, k = 2.0). It rises in busy sessions and falls in quiet ones.
//   * A print at or above the threshold marks the bar: one bull circle (buy prints) and/or one bear circle
//     (sell prints) per bar, placed at the price of the largest qualifying print. If a larger print arrives
//     later in the bar the circle moves to that price.
//   * The number inside the circle is the aggressive volume traded at that price in that bar (it keeps
//     counting live), or optionally the sum of the qualifying prints.
//   * Circle diameter is square-root scaled relative to the other markers on screen.
// Markers visualise large trades - they are not buy or sell signals.
//
// Data: needs the trade stream. History requires Tick Replay (Tools > Options > Market data > Show Tick
// Replay, then tick "Tick Replay" in the chart's Data Series). Without it markers appear from realtime on.
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
	/// <summary>How consecutive trade prints are grouped before the size test.</summary>
	public enum IdcPrintGrouping
	{
		/// <summary>Every print reported by the data feed is tested on its own.</summary>
		Individual,
		/// <summary>Consecutive prints with the same timestamp and side are summed (approximates one aggressive order sweeping the book).</summary>
		SameTimestamp
	}

	/// <summary>Number shown inside a Heavy Absorption circle.</summary>
	public enum IdcAbsorptionLabel
	{
		/// <summary>Aggressive volume traded at the marker price in that bar (counts up live).</summary>
		LevelVolume,
		/// <summary>Sum of the qualifying (large) prints of that bar and side.</summary>
		QualifyingVolume
	}

	/// <summary>How Heavy Absorption circle sizes are derived from volume.</summary>
	public enum IdcCircleSizing
	{
		/// <summary>Diameter = min + factor * sqrt(volume), capped at max (stable size, as seen in the original's videos).</summary>
		Absolute,
		/// <summary>Diameter scales by sqrt(volume / largest visible marker) between min and max.</summary>
		RelativeToVisible
	}

	public class IDCHeavyAbsorption : Indicator
	{
		private sealed class Marker
		{
			public int		Bar;
			public int		Side;			// +1 buy, -1 sell
			public long		Tick;
			public double	LargestPrint;
			public double	QualifyingVolume;
			public double	DisplayValue;
		}

		private sealed class BarData
		{
			public readonly Dictionary<long, double>	BuyVolume	= new Dictionary<long, double>();
			public readonly Dictionary<long, double>	SellVolume	= new Dictionary<long, double>();
			public Marker								Buy;
			public Marker								Sell;
		}

		private readonly object						sync		= new object();
		private readonly Dictionary<int, BarData>	barData		= new Dictionary<int, BarData>();
		private IdcFootprintEngine					engine;
		private IdcBarWindowStats					stats;
		private IdcDxBrushCache						dxBrushes;
		private IdcTrade							pending;
		private double								pendingLargestPart;
		private bool								hasPending;
		private int									lastClosedBar	= -1;
		private int									lastPruneBar	= -1;
		private bool								calculateWarning;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Marks exceptionally large aggressive prints (mean + k standard deviations) absorbed by passive liquidity. Circle size grows with volume. Not a buy/sell signal.";
				Name						= "IDC Heavy Absorption";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				DrawOnPricePanel			= true;
				DisplayInDataBox			= false;
				PaintPriceMarkers			= false;
				IsSuspendedWhileInactive	= false;
				ScaleJustification			= ScaleJustification.Right;
				MaximumBarsLookBack			= MaximumBarsLookBack.TwoHundredFiftySix;

				StdDevMultiplier			= 2.0;
				LookbackBars				= 100;
				MinPopulation				= 200;
				MinVolume					= 0;
				PrintGrouping				= IdcPrintGrouping.Individual;
				LabelMode					= IdcAbsorptionLabel.LevelVolume;

				MaxBarsBack					= 3000;

				BullBrush					= new SolidColorBrush(Color.FromRgb(0x1E, 0x8F, 0x4E));
				BullBrush.Freeze();
				BearBrush					= new SolidColorBrush(Color.FromRgb(0xDC, 0x1C, 0x1C));
				BearBrush.Freeze();
				TextBrush					= Brushes.White;
				FillOpacity					= 30;
				RingOpacity					= 60;
				CircleSizing				= IdcCircleSizing.Absolute;
				MinDiameter					= 16;
				SizeFactor					= 1.5;
				MaxDiameter					= 64;
				ShowText					= true;
				TextFont					= new SimpleFont("Arial", 10) { Bold = true };
				ShowWarnings				= true;

				AlertOnBarClose				= false;
				AlertSound					= "Alert4.wav";
			}
			else if (State == State.DataLoaded)
			{
				engine = new IdcFootprintEngine();
				// footprints are not needed here, only classified prints: keep the trade-stream store tiny
				engine.MaxBarsKept = 10;
				engine.Initialize(Bars, TickSize, Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency, IdcFootprintSource.TickData);
				stats		= new IdcBarWindowStats(LookbackBars);
				dxBrushes	= new IdcDxBrushCache();
				lock (sync)
				{
					barData.Clear();
					hasPending		= false;
					lastClosedBar	= -1;
					lastPruneBar	= -1;
				}
			}
			else if (State == State.Historical)
			{
				calculateWarning = Calculate != Calculate.OnEachTick;
				if (calculateWarning)
					Log(Name + ": set Calculate to 'On each tick' so prints are assigned to the right bar.", LogLevel.Warning);
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
			if (!engine.ProcessMarketData(marketDataUpdate, CurrentBar, out trade) || trade.Side == 0)
				return;
			if (CurrentBar < Count - MaxBarsBack)
				return;

			lock (sync)
			{
				if (PrintGrouping == IdcPrintGrouping.Individual)
				{
					ProcessPrint(trade);
					return;
				}

				if (hasPending && pending.Time == trade.Time && pending.Side == trade.Side && pending.BarIndex == trade.BarIndex)
				{
					// same aggressive order sweeping several prices: sum it, remember the price where most volume traded
					AddLevelVolume(trade);
					pending.Volume += trade.Volume;
					if (trade.Volume > pendingLargestPart)
					{
						pendingLargestPart	= trade.Volume;
						pending.Tick		= trade.Tick;
						pending.Price		= trade.Price;
					}
					return;
				}

				if (hasPending)
					EvaluatePrint(pending);
				pending				= trade;
				pendingLargestPart	= trade.Volume;
				hasPending			= true;
				AddLevelVolume(trade);
			}
		}

		protected override void OnBarUpdate()
		{
			if (engine == null || CurrentBar < 1)
				return;

			int closed = IdcBars.LastClosedBar(State, Bars.IsTickReplay, CurrentBar, Count);
			lock (sync)
			{
				while (lastClosedBar < closed)
				{
					lastClosedBar++;
					OnBarClosed(lastClosedBar);
				}

				if (CurrentBar - lastPruneBar >= 200)
				{
					lastPruneBar = CurrentBar;
					Prune(CurrentBar - MaxBarsBack);
				}
			}
		}

		#region Detection
		private BarData GetBarData(int bar)
		{
			BarData data;
			if (!barData.TryGetValue(bar, out data))
			{
				data = new BarData();
				barData.Add(bar, data);
			}
			return data;
		}

		private void AddLevelVolume(IdcTrade trade)
		{
			BarData data = GetBarData(trade.BarIndex);
			Dictionary<long, double> side = trade.Side > 0 ? data.BuyVolume : data.SellVolume;
			double v;
			side.TryGetValue(trade.Tick, out v);
			side[trade.Tick] = v + trade.Volume;
			RefreshDisplay(data);
		}

		private void ProcessPrint(IdcTrade trade)
		{
			AddLevelVolume(trade);
			EvaluatePrint(trade);
		}

		/// <summary>Tests one (possibly grouped) print against the adaptive threshold, then adds it to the statistics.</summary>
		private void EvaluatePrint(IdcTrade print)
		{
			bool ready		= stats.Count >= MinPopulation;
			double limit	= stats.Threshold(StdDevMultiplier);
			bool qualifies	= ready && print.Volume >= limit && print.Volume >= MinVolume;

			stats.Add(print.BarIndex, print.Volume);

			if (!qualifies)
				return;

			BarData data	= GetBarData(print.BarIndex);
			Marker marker	= print.Side > 0 ? data.Buy : data.Sell;
			if (marker == null)
			{
				marker = new Marker { Bar = print.BarIndex, Side = print.Side, Tick = print.Tick, LargestPrint = print.Volume };
				if (print.Side > 0)
					data.Buy = marker;
				else
					data.Sell = marker;
			}
			else if (print.Volume > marker.LargestPrint)
			{
				marker.Tick			= print.Tick;
				marker.LargestPrint	= print.Volume;
			}

			marker.QualifyingVolume += print.Volume;
			RefreshDisplay(data);
		}

		private void RefreshDisplay(BarData data)
		{
			if (data.Buy != null)
				data.Buy.DisplayValue = DisplayValue(data, data.Buy);
			if (data.Sell != null)
				data.Sell.DisplayValue = DisplayValue(data, data.Sell);
		}

		private double DisplayValue(BarData data, Marker marker)
		{
			if (LabelMode == IdcAbsorptionLabel.QualifyingVolume)
				return marker.QualifyingVolume;
			double v;
			(marker.Side > 0 ? data.BuyVolume : data.SellVolume).TryGetValue(marker.Tick, out v);
			return Math.Max(v, marker.LargestPrint);
		}

		private void OnBarClosed(int bar)
		{
			if (hasPending && pending.BarIndex <= bar && PrintGrouping != IdcPrintGrouping.Individual)
			{
				EvaluatePrint(pending);
				hasPending = false;
			}

			if (!AlertOnBarClose || State != State.Realtime)
				return;

			BarData data;
			if (!barData.TryGetValue(bar, out data))
				return;

			foreach (Marker marker in new[] { data.Buy, data.Sell })
			{
				if (marker == null)
					continue;
				string message = string.Format("{0}: heavy {1} absorption {2} @ {3}", Instrument.FullName,
					marker.Side > 0 ? "BUY" : "SELL", IdcDraw.Volume(marker.DisplayValue),
					Instrument.MasterInstrument.FormatPrice(engine.ToPrice(marker.Tick)));
				Alert("IDCHeavyAbs" + (marker.Side > 0 ? "B" : "S") + bar, Priority.Medium, message, SoundPath(AlertSound), 0,
					marker.Side > 0 ? Brushes.DarkGreen : Brushes.DarkRed, Brushes.White);
			}
		}

		private void Prune(int oldestBar)
		{
			if (oldestBar <= 0)
				return;
			List<int> stale = null;
			foreach (int key in barData.Keys)
				if (key < oldestBar)
				{
					if (stale == null) stale = new List<int>();
					stale.Add(key);
				}
			if (stale != null)
				foreach (int key in stale)
					barData.Remove(key);
		}

		private static string SoundPath(string file)
		{
			if (string.IsNullOrWhiteSpace(file))
				return string.Empty;
			if (System.IO.Path.IsPathRooted(file))
				return file;
			return System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", file);
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

			int fromIndex	= ChartBars.FromIndex;
			int toIndex		= ChartBars.ToIndex;

			List<Marker> visible = new List<Marker>();
			lock (sync)
			{
				for (int bar = fromIndex; bar <= toIndex; bar++)
				{
					BarData data;
					if (!barData.TryGetValue(bar, out data))
						continue;
					if (data.Buy != null)
						visible.Add(new Marker { Bar = bar, Side = 1, Tick = data.Buy.Tick, DisplayValue = data.Buy.DisplayValue });
					if (data.Sell != null)
						visible.Add(new Marker { Bar = bar, Side = -1, Tick = data.Sell.Tick, DisplayValue = data.Sell.DisplayValue });
				}
			}

			SharpDX.Direct2D1.Brush textBrush = dxBrushes.Get(RenderTarget, TextBrush, 1.0);
			if (visible.Count > 0)
			{
				double maxValue = 0;
				foreach (Marker m in visible)
					maxValue = Math.Max(maxValue, m.DisplayValue);

				// big circles first so smaller ones stay visible on top
				visible.Sort((a, b) => b.DisplayValue.CompareTo(a.DisplayValue));

				SharpDX.Direct2D1.AntialiasMode oldMode = RenderTarget.AntialiasMode;
				RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
				SharpDX.DirectWrite.TextFormat textFormat = ShowText && TextFont != null ? TextFont.ToDirectWriteTextFormat() : null;
				try
				{
					foreach (Marker m in visible)
					{
						double	diameter;
						if (CircleSizing == IdcCircleSizing.Absolute)
							diameter = Math.Min(MaxDiameter, MinDiameter + SizeFactor * Math.Sqrt(Math.Max(0, m.DisplayValue)));
						else
							diameter = MinDiameter + (MaxDiameter - MinDiameter) * (maxValue > 0 ? Math.Sqrt(Math.Max(0, m.DisplayValue) / maxValue) : 1);
						float	radius		= (float)(Math.Max(MinDiameter, diameter) / 2.0);
						float	x			= chartControl.GetXByBarIndex(ChartBars, m.Bar);
						float	y			= chartScale.GetYByValue(engine.ToPrice(m.Tick));
						System.Windows.Media.Brush color = m.Side > 0 ? BullBrush : BearBrush;

						SharpDX.Direct2D1.Ellipse ellipse = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), radius, radius);
						RenderTarget.FillEllipse(ellipse, dxBrushes.Get(RenderTarget, color, FillOpacity / 100.0));
						RenderTarget.DrawEllipse(ellipse, dxBrushes.Get(RenderTarget, color, RingOpacity / 100.0), 1.5f);

						if (textFormat != null)
							IdcDraw.Text(RenderTarget, textFormat, IdcDraw.Volume(m.DisplayValue), x, y, textBrush, IdcTextAnchor.Center);
					}
				}
				finally
				{
					RenderTarget.AntialiasMode = oldMode;
					if (textFormat != null)
						textFormat.Dispose();
				}
			}

			if (ShowWarnings)
				RenderWarning(textBrush);
		}

		private void RenderWarning(SharpDX.Direct2D1.Brush brush)
		{
			string warning = null;
			if (calculateWarning)
				warning = "set Calculate = On each tick";
			else if (!engine.HasTickReplay)
				warning = "no print history - enable Tick Replay on the data series (collecting realtime prints)";
			else if (engine.SyntheticQuotes)
				warning = "historical ticks carry no bid/ask - using up/down tick rule";
			if (warning == null)
				return;

			using (SharpDX.DirectWrite.TextFormat format = new SimpleFont("Arial", 10).ToDirectWriteTextFormat())
				IdcDraw.Text(RenderTarget, format, Name + ": " + warning, ChartPanel.X + 6, ChartPanel.Y + 20, brush, IdcTextAnchor.TopLeft);
		}
		#endregion

		#region Properties
		[Range(0.1, 20)]
		[Display(Name = "Std dev multiplier", Description = "Sensitivity: threshold = mean + k * standard deviation of print size. Higher = fewer, stronger markers.", Order = 1, GroupName = "1. Detection")]
		public double StdDevMultiplier { get; set; }

		[Range(5, 5000)]
		[Display(Name = "Lookback bars", Description = "Bars in the statistical window.", Order = 2, GroupName = "1. Detection")]
		public int LookbackBars { get; set; }

		[Range(10, 1000000)]
		[Display(Name = "Min prints before marking", Description = "Prints needed in the window before markers are allowed (avoids noise right after loading).", Order = 3, GroupName = "1. Detection")]
		public int MinPopulation { get; set; }

		[Range(0, double.MaxValue)]
		[Display(Name = "Min volume filter", Description = "Optional floor: prints smaller than this never qualify (0 = off).", Order = 4, GroupName = "1. Detection")]
		public double MinVolume { get; set; }

		[Display(Name = "Print grouping", Description = "Individual prints, or prints with the same timestamp and side summed into one order.", Order = 5, GroupName = "1. Detection")]
		public IdcPrintGrouping PrintGrouping { get; set; }

		[Display(Name = "Circle number", Description = "Volume at the marker price (live) or sum of qualifying prints.", Order = 6, GroupName = "1. Detection")]
		public IdcAbsorptionLabel LabelMode { get; set; }

		[Range(50, 100000)]
		[Display(Name = "Max bars back", Description = "Prints older than this are ignored (performance).", Order = 1, GroupName = "2. Data")]
		public int MaxBarsBack { get; set; }

		[XmlIgnore]
		[Display(Name = "Bull (buy) colour", Order = 1, GroupName = "3. Display")]
		public System.Windows.Media.Brush BullBrush { get; set; }

		[Browsable(false)]
		public string BullBrushSerialize
		{
			get { return Serialize.BrushToString(BullBrush); }
			set { BullBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Bear (sell) colour", Order = 2, GroupName = "3. Display")]
		public System.Windows.Media.Brush BearBrush { get; set; }

		[Browsable(false)]
		public string BearBrushSerialize
		{
			get { return Serialize.BrushToString(BearBrush); }
			set { BearBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Text colour", Order = 3, GroupName = "3. Display")]
		public System.Windows.Media.Brush TextBrush { get; set; }

		[Browsable(false)]
		public string TextBrushSerialize
		{
			get { return Serialize.BrushToString(TextBrush); }
			set { TextBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Fill opacity %", Order = 4, GroupName = "3. Display")]
		public int FillOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Ring opacity %", Order = 5, GroupName = "3. Display")]
		public int RingOpacity { get; set; }

		[Display(Name = "Circle sizing", Description = "Absolute: min + factor * sqrt(volume). Relative: sqrt(volume / largest visible marker) between min and max.", Order = 6, GroupName = "3. Display")]
		public IdcCircleSizing CircleSizing { get; set; }

		[Range(4, 200)]
		[Display(Name = "Min diameter (px)", Order = 7, GroupName = "3. Display")]
		public int MinDiameter { get; set; }

		[Range(0, 20)]
		[Display(Name = "Size factor (absolute)", Description = "Pixels added per square root of volume.", Order = 7, GroupName = "3. Display")]
		public double SizeFactor { get; set; }

		[Range(4, 300)]
		[Display(Name = "Max diameter (px)", Order = 7, GroupName = "3. Display")]
		public int MaxDiameter { get; set; }

		[Display(Name = "Show volume text", Order = 8, GroupName = "3. Display")]
		public bool ShowText { get; set; }

		[Display(Name = "Text font", Order = 9, GroupName = "3. Display")]
		public SimpleFont TextFont { get; set; }

		[Display(Name = "Show data warnings", Order = 10, GroupName = "3. Display")]
		public bool ShowWarnings { get; set; }

		[Display(Name = "Alert on bar close", Description = "Alert when a closed bar carries a heavy absorption marker.", Order = 1, GroupName = "4. Alerts")]
		public bool AlertOnBarClose { get; set; }

		[Display(Name = "Alert sound", Description = "File in NinjaTrader's sounds folder or a full path.", Order = 2, GroupName = "4. Alerts")]
		public string AlertSound { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private IDCator.IDCHeavyAbsorption[] cacheIDCHeavyAbsorption;
		public IDCator.IDCHeavyAbsorption IDCHeavyAbsorption()
		{
			return IDCHeavyAbsorption(Input);
		}

		public IDCator.IDCHeavyAbsorption IDCHeavyAbsorption(ISeries<double> input)
		{
			if (cacheIDCHeavyAbsorption != null)
				for (int idx = 0; idx < cacheIDCHeavyAbsorption.Length; idx++)
					if (cacheIDCHeavyAbsorption[idx] != null &&  cacheIDCHeavyAbsorption[idx].EqualsInput(input))
						return cacheIDCHeavyAbsorption[idx];
			return CacheIndicator<IDCator.IDCHeavyAbsorption>(new IDCator.IDCHeavyAbsorption(), input, ref cacheIDCHeavyAbsorption);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.IDCator.IDCHeavyAbsorption IDCHeavyAbsorption()
		{
			return indicator.IDCHeavyAbsorption(Input);
		}

		public Indicators.IDCator.IDCHeavyAbsorption IDCHeavyAbsorption(ISeries<double> input )
		{
			return indicator.IDCHeavyAbsorption(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.IDCator.IDCHeavyAbsorption IDCHeavyAbsorption()
		{
			return indicator.IDCHeavyAbsorption(Input);
		}

		public Indicators.IDCator.IDCHeavyAbsorption IDCHeavyAbsorption(ISeries<double> input )
		{
			return indicator.IDCHeavyAbsorption(input);
		}
	}
}

#endregion
