//
// IDC HVN Profile - high volume nodes per period, extended forward as reference levels until touched.
//
// How it works
//   * Chart history is split into periods (1 minute .. weekly). A volume profile is built for every period.
//   * The profile is smoothed (1-2-1 passes) so plateaus become clear, rows above "HVN threshold %" of the
//     highest smoothed row are candidates, candidates closer than the gap tolerance are merged into clusters,
//     weak cluster edges are trimmed (cluster sensitivity) and clusters smaller than the minimum are dropped.
//   * Each cluster becomes a zone that starts at its period's first bar. The period's biggest cluster is drawn
//     in the primary colour, the others in the secondary colour. A POC line marks the busiest price inside it.
//   * After the period ends a zone extends right until price trades back into it, then it stops at that bar
//     (optional alert). The label shows the cluster's POC volume and the dominant side's share of its volume
//     (green = buyers, red = sellers).
// Zones are reference levels, not buy or sell signals.
//
// Data: uses the footprint (Volumetric chart or Tick Replay) when present. Without it, each bar's volume is
// spread evenly over its range, which still gives a usable profile.
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
	/// <summary>Length of one HVN profile period.</summary>
	public enum IdcProfileTimeframe
	{
		Minute1, Minute5, Minute15, Minute30, Hour1, Hour2, Hour4, Daily, Weekly
	}

	/// <summary>Number shown on an HVN zone label.</summary>
	public enum IdcHvnLabelValue
	{
		/// <summary>Volume at the cluster's busiest single price.</summary>
		PocVolume,
		/// <summary>Total volume of the cluster.</summary>
		ClusterVolume
	}

	public class IDCHvnProfile : Indicator
	{
		private sealed class Period
		{
			public long								Key;
			public int								StartBar;
			public int								EndBar;
			public readonly Dictionary<long, double[]>	Levels = new Dictionary<long, double[]>();	// tick -> [total, buy, sell]
			public long								LowTick		= long.MaxValue;
			public long								HighTick	= long.MinValue;
			public bool								HasSides;

			public void Add(long tick, double total, double buy, double sell)
			{
				if (total <= 0)
					return;
				double[] v;
				if (!Levels.TryGetValue(tick, out v))
				{
					v = new double[3];
					Levels.Add(tick, v);
					if (tick < LowTick)		LowTick = tick;
					if (tick > HighTick)	HighTick = tick;
				}
				v[0] += total;
				v[1] += buy;
				v[2] += sell;
			}
		}

		private sealed class Zone
		{
			public int		Id;
			public int		StartBar;
			public int		PeriodEndBar;
			public int		EndBar = -1;
			public long		LowTick;
			public long		HighTick;
			public long		PocTick;
			public bool		IsPrimary;
			public bool		IsLive;
			public double	LabelValue;
			public double	BuyShare;		// 0..1, NaN when sides are unknown
		}

		private sealed class Histogram
		{
			public int		StartBar;
			public int		EndBar;
			public bool		IsLive;
			public long		LowTick;
			public int		RowTicks;
			public double[]	Rows;			// normalised 0..1
		}

		private sealed class Cluster
		{
			public int		FirstRow;
			public int		LastRow;
			public double	Volume;
			public double	Buy;
			public double	Sell;
			public long		PocTick;
			public double	PocVolume;
		}

		private readonly object				sync			= new object();
		private readonly List<Zone>			zones			= new List<Zone>();
		private readonly List<Zone>			liveZones		= new List<Zone>();
		private readonly List<Histogram>	histograms		= new List<Histogram>();
		private readonly Dictionary<int, long>	barKeys		= new Dictionary<int, long>();
		private Histogram					liveHistogram;
		private Period						current;
		private IdcFootprintEngine			engine;
		private IdcDxBrushCache				dxBrushes;
		private SessionIterator				sessionIterator;
		private DateTime					tradingDay		= DateTime.MinValue;
		private int							lastKeyedBar	= -1;
		private int							lastAnalyzedBar	= -1;
		private int							nextZoneId;
		private bool						calculateWarning;
		private int						touchBar	= -1;
		private double					touchHigh;
		private double					touchLow;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "High volume node clusters per period with POC, extended forward as support/resistance until price revisits them. Not a buy/sell signal.";
				Name						= "IDC HVN Profile";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				DrawOnPricePanel			= true;
				DisplayInDataBox			= false;
				PaintPriceMarkers			= false;
				IsSuspendedWhileInactive	= false;
				ScaleJustification			= ScaleJustification.Right;
				MaximumBarsLookBack			= MaximumBarsLookBack.TwoHundredFiftySix;

				ProfileTimeframe			= IdcProfileTimeframe.Minute30;
				RowTicks					= 0;
				TargetRows					= 60;
				HvnThresholdPercent			= 70;
				SmoothingPasses				= 2;
				ClusterSensitivity			= 50;
				MinClusterRows				= 2;
				GapToleranceRows			= 1;
				MaxClustersPerPeriod		= 3;
				TouchGraceBars				= 0;

				FootprintSource				= IdcFootprintSource.Auto;
				MaxBarsBack					= 5000;
				MaxZones					= 600;

				PrimaryZoneBrush			= Brushes.Orange;
				SecondaryZoneBrush			= new SolidColorBrush(Color.FromRgb(0x6E, 0x7D, 0xA0));
				SecondaryZoneBrush.Freeze();
				ZoneOpacity					= 20;
				TouchedOpacityPercent		= 70;
				ShowTouchedZones			= true;
				ExtendMode					= IdcExtendMode.LastBar;
				ShowPocLine					= true;
				PocOnSecondary				= false;
				ExtendPoc					= true;
				PocBrush					= Brushes.Goldenrod;
				PocWidth					= 2;
				ShowHistogram				= true;
				HistogramWidthPercent		= 35;
				HistogramBrush				= Brushes.SlateGray;
				HistogramOpacity			= 22;
				LiveProfileOpacity			= 12;
				ShowLabels					= true;
				LabelValue					= IdcHvnLabelValue.PocVolume;
				LabelFont					= new SimpleFont("Arial", 10) { Bold = true };
				LabelBrush					= Brushes.White;
				BuyPercentBrush				= new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x6E));
				BuyPercentBrush.Freeze();
				SellPercentBrush			= new SolidColorBrush(Color.FromRgb(0xC0, 0x4A, 0x44));
				SellPercentBrush.Freeze();
				ShowWarnings				= true;

				AlertOnTouch				= false;
				AlertSound					= "Alert2.wav";
				AlertRearmSeconds			= 30;
			}
			else if (State == State.DataLoaded)
			{
				engine = new IdcFootprintEngine();
				engine.MaxBarsKept = 500;
				engine.Initialize(Bars, TickSize, Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency, FootprintSource);
				sessionIterator	= new SessionIterator(Bars);
				dxBrushes		= new IdcDxBrushCache();
				lock (sync)
				{
					zones.Clear();
					liveZones.Clear();
					histograms.Clear();
					barKeys.Clear();
					liveHistogram	= null;
					current			= null;
					lastKeyedBar	= -1;
					lastAnalyzedBar	= -1;
					nextZoneId		= 0;
					tradingDay		= DateTime.MinValue;
				}
			}
			else if (State == State.Historical)
			{
				calculateWarning = Calculate != Calculate.OnEachTick;
				if (calculateWarning)
					Log(Name + ": set Calculate to 'On each tick' for correct footprint data.", LogLevel.Warning);
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
			engine.ProcessMarketData(marketDataUpdate, CurrentBar, out trade);
		}

		protected override void OnBarUpdate()
		{
			if (engine == null)
				return;

			lock (sync)
			{
				if (CurrentBar != lastKeyedBar)
				{
					lastKeyedBar = CurrentBar;
					barKeys[CurrentBar] = PeriodKey(Time[0]);
				}

				int closed = IdcBars.LastClosedBar(State, Bars.IsTickReplay, CurrentBar, Count);
				bool changed = false;
				while (lastAnalyzedBar < closed)
				{
					lastAnalyzedBar++;
					if (lastAnalyzedBar >= Count - MaxBarsBack)
					{
						AddBar(lastAnalyzedBar);
						changed = true;
					}
				}

				// the forming bar opened a new period and the old one has no open bars left: close it now so its
				// zones can be touched from the first bar of the new period on
				long formingKey;
				if (current != null && lastAnalyzedBar == CurrentBar - 1 && barKeys.TryGetValue(CurrentBar, out formingKey) && formingKey != current.Key)
				{
					FinalizePeriod(current);
					current	= null;
					changed	= true;
				}

				if (changed && (State == State.Realtime || CurrentBar >= Count - 2))
					BuildLiveProfile();

				// zones/lines only change when a new bar opened (new zones) or the forming bar made a new high/low
				if (CurrentBar != touchBar || High[0] > touchHigh || Low[0] < touchLow)
				{
					touchBar	= CurrentBar;
					touchHigh	= High[0];
					touchLow	= Low[0];
					CheckTouches(CurrentBar, High[0], Low[0]);
				}

				if (barKeys.Count > MaxBarsBack + 200)
					PruneKeys(CurrentBar - MaxBarsBack - 50);
			}
		}

		#region Periods
		private long PeriodKey(DateTime barTime)
		{
			if (ProfileTimeframe == IdcProfileTimeframe.Daily || ProfileTimeframe == IdcProfileTimeframe.Weekly)
			{
				if (Bars.BarsType != null && !Bars.BarsType.IsIntraday)
					tradingDay = barTime.Date;	// Day/Week bars are stamped with their trading day; IsFirstBarOfSession is always false on them
				else if (Bars.IsFirstBarOfSession || tradingDay == DateTime.MinValue)
				{
					sessionIterator.GetNextSession(barTime, true);
					tradingDay = sessionIterator.ActualTradingDayExchange.Date;
					if (tradingDay == DateTime.MinValue || tradingDay.Year < 1900)
						tradingDay = barTime.Date;
				}

				long day = tradingDay.Ticks / TimeSpan.TicksPerDay;
				if (ProfileTimeframe == IdcProfileTimeframe.Daily)
					return day;
				// weeks start on Monday; DateTime.MinValue (day 0) was a Monday
				return day / 7;
			}

			int minutes = PeriodMinutes();
			// bars are stamped with their close time: step back one tick so a bar ending exactly on a boundary
			// belongs to the period it covers
			long ticks = barTime.AddTicks(-1).Ticks;
			return ticks / (TimeSpan.TicksPerMinute * minutes);
		}

		private int PeriodMinutes()
		{
			switch (ProfileTimeframe)
			{
				case IdcProfileTimeframe.Minute1:	return 1;
				case IdcProfileTimeframe.Minute5:	return 5;
				case IdcProfileTimeframe.Minute15:	return 15;
				case IdcProfileTimeframe.Minute30:	return 30;
				case IdcProfileTimeframe.Hour1:		return 60;
				case IdcProfileTimeframe.Hour2:		return 120;
				case IdcProfileTimeframe.Hour4:		return 240;
				default:							return 1440;
			}
		}

		private void AddBar(int bar)
		{
			long key;
			if (!barKeys.TryGetValue(bar, out key))
				return;

			if (current != null && current.Key != key)
			{
				FinalizePeriod(current);
				current = null;
			}
			if (current == null)
				current = new Period { Key = key, StartBar = bar };
			current.EndBar = bar;

			IdcFootprintBar fp = engine.GetBar(bar, true);
			if (fp != null && !fp.IsEmpty)
			{
				int step = Math.Max(1, engine.LevelStep);
				for (long t = fp.LowTick; t <= fp.HighTick; t += step)
				{
					IdcCell cell = fp.Get(t);
					if (cell != null)
						current.Add(t, cell.Total, cell.Ask, cell.Bid);
				}
				current.HasSides = true;
				return;
			}

			// no footprint for this bar: spread its volume evenly across its range
			double volume = Bars.GetVolume(bar);
			if (Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency)
				volume = Convert.ToDouble(NinjaTrader.Core.Globals.ToCryptocurrencyVolume((long)volume));
			long lo = engine.ToTick(Bars.GetLow(bar));
			long hi = engine.ToTick(Bars.GetHigh(bar));
			if (volume <= 0 || hi < lo || hi - lo > 20000)
				return;
			double each = volume / (hi - lo + 1);
			for (long t = lo; t <= hi; t++)
				current.Add(t, each, 0, 0);
		}

		private void FinalizePeriod(Period period)
		{
			Histogram histogram;
			List<Cluster> clusters = Analyze(period, out histogram);
			if (histogram != null)
			{
				histograms.Add(histogram);
				while (histograms.Count > 400)
					histograms.RemoveAt(0);
			}

			foreach (Zone zone in MakeZones(period, clusters, false))
				zones.Add(zone);

			liveZones.Clear();
			liveHistogram = null;
			PruneZones();
		}

		private void BuildLiveProfile()
		{
			liveZones.Clear();
			liveHistogram = null;
			if (current == null || current.Levels.Count == 0)
				return;

			Histogram histogram;
			List<Cluster> clusters = Analyze(current, out histogram);
			if (histogram != null)
			{
				histogram.IsLive	= true;
				liveHistogram		= histogram;
			}
			liveZones.AddRange(MakeZones(current, clusters, true));
		}

		private List<Zone> MakeZones(Period period, List<Cluster> clusters, bool live)
		{
			List<Zone> result = new List<Zone>();
			if (clusters.Count == 0)
				return result;

			int rowTicks = RowSize(period);
			for (int i = 0; i < clusters.Count; i++)
			{
				Cluster c	= clusters[i];
				double sum	= c.Buy + c.Sell;
				result.Add(new Zone
				{
					Id				= live ? -1 : nextZoneId++,
					StartBar		= period.StartBar,
					PeriodEndBar	= period.EndBar,
					LowTick			= period.LowTick + (long)c.FirstRow * rowTicks,
					HighTick		= Math.Min(period.HighTick, period.LowTick + (long)(c.LastRow + 1) * rowTicks - 1),
					PocTick			= c.PocTick,
					IsPrimary		= i == 0,
					IsLive			= live,
					LabelValue		= LabelValue == IdcHvnLabelValue.PocVolume ? c.PocVolume : c.Volume,
					BuyShare		= period.HasSides && sum > 0 ? c.Buy / sum : double.NaN
				});
			}
			return result;
		}

		private int RowSize(Period period)
		{
			if (RowTicks > 0)
				return RowTicks;
			long range = period.HighTick - period.LowTick + 1;
			return (int)Math.Max(1, Math.Ceiling(range / (double)Math.Max(5, TargetRows)));
		}

		/// <summary>Builds the row profile, smooths it and extracts HVN clusters (largest volume first).</summary>
		private List<Cluster> Analyze(Period period, out Histogram histogram)
		{
			histogram = null;
			List<Cluster> clusters = new List<Cluster>();
			if (period.Levels.Count == 0 || period.HighTick < period.LowTick)
				return clusters;

			int		rowTicks	= RowSize(period);
			int		rowCount	= (int)((period.HighTick - period.LowTick) / rowTicks) + 1;
			double[] vol		= new double[rowCount];
			double[] buy		= new double[rowCount];
			double[] sell		= new double[rowCount];

			foreach (KeyValuePair<long, double[]> kv in period.Levels)
			{
				int r = (int)((kv.Key - period.LowTick) / rowTicks);
				vol[r]	+= kv.Value[0];
				buy[r]	+= kv.Value[1];
				sell[r]	+= kv.Value[2];
			}

			double maxRaw = vol.Max();
			if (maxRaw <= 0)
				return clusters;

			histogram = new Histogram { StartBar = period.StartBar, EndBar = period.EndBar, LowTick = period.LowTick, RowTicks = rowTicks, Rows = new double[rowCount] };
			for (int r = 0; r < rowCount; r++)
				histogram.Rows[r] = vol[r] / maxRaw;

			// smoothing: repeated 1-2-1 passes flatten single-row spikes so plateaus (nodes) stand out
			double[] smooth = (double[])vol.Clone();
			for (int pass = 0; pass < SmoothingPasses; pass++)
			{
				double[] next = new double[rowCount];
				for (int r = 0; r < rowCount; r++)
				{
					double sum = 2 * smooth[r], weight = 2;
					if (r > 0)				{ sum += smooth[r - 1]; weight += 1; }
					if (r < rowCount - 1)	{ sum += smooth[r + 1]; weight += 1; }
					next[r] = sum / weight;
				}
				smooth = next;
			}

			double maxSmooth = smooth.Max();
			double threshold = maxSmooth * HvnThresholdPercent / 100.0;

			int row = 0;
			while (row < rowCount)
			{
				if (smooth[row] < threshold)
				{
					row++;
					continue;
				}

				// grow the cluster, bridging gaps of up to GapToleranceRows
				int first = row, last = row, gap = 0;
				for (int r = row + 1; r < rowCount; r++)
				{
					if (smooth[r] >= threshold)
					{
						last	= r;
						gap		= 0;
					}
					else if (++gap > GapToleranceRows)
						break;
				}
				row = last + 1;

				// trim weak edges relative to the cluster's own peak
				double peak = 0;
				for (int r = first; r <= last; r++)
					peak = Math.Max(peak, smooth[r]);
				double edge = peak * ClusterSensitivity / 100.0;
				while (first < last && smooth[first] < edge)	first++;
				while (last > first && smooth[last] < edge)		last--;

				if (last - first + 1 < MinClusterRows)
					continue;

				Cluster cluster = new Cluster { FirstRow = first, LastRow = last };
				for (int r = first; r <= last; r++)
				{
					cluster.Volume	+= vol[r];
					cluster.Buy		+= buy[r];
					cluster.Sell	+= sell[r];
				}

				// POC: busiest single price inside the cluster
				long lo = period.LowTick + (long)first * rowTicks;
				long hi = period.LowTick + (long)(last + 1) * rowTicks - 1;
				cluster.PocTick = lo;
				for (long t = lo; t <= hi; t++)
				{
					double[] v;
					if (period.Levels.TryGetValue(t, out v) && v[0] > cluster.PocVolume)
					{
						cluster.PocVolume	= v[0];
						cluster.PocTick		= t;
					}
				}
				clusters.Add(cluster);
			}

			clusters.Sort((a, b) => b.Volume.CompareTo(a.Volume));
			if (clusters.Count > MaxClustersPerPeriod)
				clusters.RemoveRange(MaxClustersPerPeriod, clusters.Count - MaxClustersPerPeriod);
			return clusters;
		}
		#endregion

		#region Touches
		private void CheckTouches(int bar, double high, double low)
		{
			foreach (Zone zone in zones)
			{
				if (zone.EndBar >= 0 || bar <= zone.PeriodEndBar + TouchGraceBars)
					continue;

				double top		= engine.ToPrice(zone.HighTick);
				double bottom	= engine.ToPrice(zone.LowTick);
				if (engine.ToTick(high) < zone.LowTick || engine.ToTick(low) > zone.HighTick)
					continue;

				zone.EndBar = bar;
				if (AlertOnTouch && State == State.Realtime)
				{
					string message = string.Format("{0}: price touched HVN zone {1}-{2} (POC {3})", Instrument.FullName,
						Instrument.MasterInstrument.FormatPrice(bottom), Instrument.MasterInstrument.FormatPrice(top),
						Instrument.MasterInstrument.FormatPrice(engine.ToPrice(zone.PocTick)));
					Alert("IDCHvn" + zone.Id, Priority.Medium, message, SoundPath(AlertSound), AlertRearmSeconds,
						Brushes.DarkGoldenrod, Brushes.Black);
				}
			}
		}

		private void PruneZones()
		{
			if (zones.Count <= MaxZones)
				return;
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

		private void PruneKeys(int oldestBar)
		{
			List<int> stale = new List<int>();
			foreach (int key in barKeys.Keys)
				if (key < oldestBar)
					stale.Add(key);
			foreach (int key in stale)
				barKeys.Remove(key);
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

			int		fromIndex	= ChartBars.FromIndex;
			int		toIndex		= ChartBars.ToIndex;
			float	panelLeft	= ChartPanel.X;
			float	panelRight	= ChartPanel.X + ChartPanel.W;
			double	tick		= engine.TickSize;
			float	halfBar		= (float)Math.Max(1.0, chartControl.GetBarPaintWidth(ChartBars) / 2.0);
			int		lastBar		= ChartBars.Bars != null ? ChartBars.Bars.Count - 1 : toIndex;
			float	lastBarX	= chartControl.GetXByBarIndex(ChartBars, lastBar) + halfBar;
			float	activeRight	= ExtendMode == IdcExtendMode.ChartEdge ? panelRight : lastBarX;

			List<Zone>		zoneList	= new List<Zone>();
			List<Histogram>	histList	= new List<Histogram>();
			lock (sync)
			{
				foreach (Zone z in zones)
				{
					if (z.StartBar > toIndex || (z.EndBar >= 0 && (z.EndBar < fromIndex || !ShowTouchedZones)))
						continue;
					zoneList.Add(CopyZone(z));
				}
				foreach (Zone z in liveZones)
					if (z.StartBar <= toIndex)
						zoneList.Add(CopyZone(z));

				if (ShowHistogram)
				{
					foreach (Histogram h in histograms)
						if (h.StartBar <= toIndex && h.EndBar >= fromIndex)
							histList.Add(h);
					if (liveHistogram != null && liveHistogram.StartBar <= toIndex)
						histList.Add(liveHistogram);
				}
			}

			// histograms first (behind zones)
			foreach (Histogram h in histList)
			{
				float x0	= chartControl.GetXByBarIndex(ChartBars, h.StartBar) - halfBar;
				float xEnd	= h.IsLive ? lastBarX : chartControl.GetXByBarIndex(ChartBars, h.EndBar) + halfBar;
				float width	= Math.Max(4, (xEnd - x0) * HistogramWidthPercent / 100f);
				SharpDX.Direct2D1.Brush brush = dxBrushes.Get(RenderTarget, HistogramBrush, (h.IsLive ? LiveProfileOpacity : HistogramOpacity) / 100.0);

				for (int r = 0; r < h.Rows.Length; r++)
				{
					if (h.Rows[r] <= 0)
						continue;
					long	rowLow	= h.LowTick + (long)r * h.RowTicks;
					float	yTop	= chartScale.GetYByValue(engine.ToPrice(rowLow + h.RowTicks - 1) + tick * 0.5);
					float	yBottom	= chartScale.GetYByValue(engine.ToPrice(rowLow) - tick * 0.5);
					float	height	= Math.Max(1, yBottom - yTop - (yBottom - yTop >= 4 ? 1 : 0));
					RenderTarget.FillRectangle(new SharpDX.RectangleF(Math.Max(panelLeft, x0), yTop, (float)(width * h.Rows[r]), height), brush);
				}
			}

			SharpDX.DirectWrite.TextFormat labelFormat = ShowLabels && LabelFont != null ? LabelFont.ToDirectWriteTextFormat() : null;
			try
			{
				SharpDX.Direct2D1.Brush numberBrush	= dxBrushes.Get(RenderTarget, LabelBrush, 1.0);
				SharpDX.Direct2D1.Brush buyBrush	= dxBrushes.Get(RenderTarget, BuyPercentBrush, 1.0);
				SharpDX.Direct2D1.Brush sellBrush	= dxBrushes.Get(RenderTarget, SellPercentBrush, 1.0);

				foreach (Zone zone in zoneList)
				{
					float x1 = Math.Max(panelLeft, chartControl.GetXByBarIndex(ChartBars, zone.StartBar) - halfBar);
					float x2 = zone.EndBar >= 0 ? chartControl.GetXByBarIndex(ChartBars, zone.EndBar) + halfBar : activeRight;
					x2 = Math.Min(panelRight, x2);
					if (x2 - x1 < 1)
						continue;

					float yTop		= chartScale.GetYByValue(engine.ToPrice(zone.HighTick) + tick * 0.5);
					float yBottom	= chartScale.GetYByValue(engine.ToPrice(zone.LowTick) - tick * 0.5);
					if (yBottom - yTop < 2)
					{
						float mid	= (yTop + yBottom) / 2f;
						yTop		= mid - 1;
						yBottom		= mid + 1;
					}

					double opacity = ZoneOpacity / 100.0;
					if (zone.EndBar >= 0)
						opacity *= TouchedOpacityPercent / 100.0;
					if (zone.IsLive)
						opacity *= 0.7;
					System.Windows.Media.Brush color = zone.IsPrimary ? PrimaryZoneBrush : SecondaryZoneBrush;
					RenderTarget.FillRectangle(new SharpDX.RectangleF(x1, yTop, x2 - x1, yBottom - yTop), dxBrushes.Get(RenderTarget, color, opacity));

					float yPoc = chartScale.GetYByValue(engine.ToPrice(zone.PocTick));
					bool drawPoc = ShowPocLine && (zone.IsPrimary || PocOnSecondary);
					if (drawPoc)
					{
						// bright inside its own period, dimmer where it is extended forward
						// short bright segment from the period start (about 6 bars, as in the original), dimmer extension after it
						int		brightEndBar	= Math.Min(zone.PeriodEndBar, zone.StartBar + 5);
						float	periodEnd	= Math.Min(x2, chartControl.GetXByBarIndex(ChartBars, brightEndBar) + halfBar);
						double	fade		= zone.EndBar >= 0 ? 0.7 : 1.0;
						if (periodEnd > x1)
							RenderTarget.DrawLine(new SharpDX.Vector2(x1, yPoc), new SharpDX.Vector2(periodEnd, yPoc),
								dxBrushes.Get(RenderTarget, PocBrush, 0.6 * fade), PocWidth);
						if (ExtendPoc && x2 > periodEnd)
							RenderTarget.DrawLine(new SharpDX.Vector2(Math.Max(x1, periodEnd), yPoc), new SharpDX.Vector2(x2, yPoc),
								dxBrushes.Get(RenderTarget, PocBrush, 0.3 * fade), PocWidth);
					}

					// the developing period's zones are drawn without labels, as in the original
					if (labelFormat != null && !zone.IsLive)
					{
						float	yMid	= drawPoc ? yPoc : (yTop + yBottom) / 2f;
						float	right	= x2 - 8;
						string	number	= IdcDraw.Volume(zone.LabelValue);
						if (!double.IsNaN(zone.BuyShare))
						{
							bool	buyers	= zone.BuyShare >= 0.5;
							int		pct		= (int)Math.Round(100 * (buyers ? zone.BuyShare : 1 - zone.BuyShare));
							SharpDX.Size2F pctSize = IdcDraw.Text(RenderTarget, labelFormat, " | " + pct + "%", right, yMid, buyers ? buyBrush : sellBrush, IdcTextAnchor.MiddleRight);
							right -= pctSize.Width;
						}
						IdcDraw.Text(RenderTarget, labelFormat, number, right, yMid, numberBrush, IdcTextAnchor.MiddleRight);
					}
				}

				if (ShowWarnings)
					RenderWarning(numberBrush);
			}
			finally
			{
				if (labelFormat != null)
					labelFormat.Dispose();
			}
		}

		private static Zone CopyZone(Zone z)
		{
			return new Zone
			{
				Id = z.Id, StartBar = z.StartBar, PeriodEndBar = z.PeriodEndBar, EndBar = z.EndBar, LowTick = z.LowTick,
				HighTick = z.HighTick, PocTick = z.PocTick, IsPrimary = z.IsPrimary, IsLive = z.IsLive,
				LabelValue = z.LabelValue, BuyShare = z.BuyShare
			};
		}

		private void RenderWarning(SharpDX.Direct2D1.Brush brush)
		{
			string warning = null;
			if (calculateWarning)
				warning = "set Calculate = On each tick";
			else if (engine.IsMisconfigured)
				warning = engine.SourceDescription;
			else if (!engine.UsesVolumetric && !engine.HasTickReplay)
				warning = "no footprint history - profiles use bar volume spread over each bar's range (enable Tick Replay or use a Volumetric chart for exact volume at price)";
			if (warning == null)
				return;

			using (SharpDX.DirectWrite.TextFormat format = new SimpleFont("Arial", 10).ToDirectWriteTextFormat())
				IdcDraw.Text(RenderTarget, format, Name + ": " + warning, ChartPanel.X + 6, ChartPanel.Y + 34, brush, IdcTextAnchor.TopLeft);
		}
		#endregion

		#region Properties
		[Display(Name = "Profile timeframe", Description = "Length of each volume profile period.", Order = 1, GroupName = "1. Profile")]
		public IdcProfileTimeframe ProfileTimeframe { get; set; }

		[Range(0, 1000)]
		[Display(Name = "Row size (ticks)", Description = "Ticks per profile row. 0 = automatic (period range / target rows).", Order = 2, GroupName = "1. Profile")]
		public int RowTicks { get; set; }

		[Range(5, 1000)]
		[Display(Name = "Target rows (auto)", Description = "Rows per profile when row size is automatic.", Order = 3, GroupName = "1. Profile")]
		public int TargetRows { get; set; }

		[Range(1, 100)]
		[Display(Name = "HVN threshold %", Description = "Rows whose smoothed volume reaches this % of the profile's busiest row become HVN candidates.", Order = 4, GroupName = "1. Profile")]
		public int HvnThresholdPercent { get; set; }

		[Range(0, 20)]
		[Display(Name = "Smoothing passes", Description = "1-2-1 smoothing passes used for plateau detection.", Order = 5, GroupName = "1. Profile")]
		public int SmoothingPasses { get; set; }

		[Range(0, 100)]
		[Display(Name = "Cluster sensitivity %", Description = "Cluster edges below this % of the cluster's own peak are trimmed (higher = tighter zones).", Order = 6, GroupName = "1. Profile")]
		public int ClusterSensitivity { get; set; }

		[Range(1, 100)]
		[Display(Name = "Min cluster size (rows)", Order = 7, GroupName = "1. Profile")]
		public int MinClusterRows { get; set; }

		[Range(0, 50)]
		[Display(Name = "Gap tolerance (rows)", Description = "Rows below the threshold allowed inside one cluster.", Order = 8, GroupName = "1. Profile")]
		public int GapToleranceRows { get; set; }

		[Range(1, 20)]
		[Display(Name = "Max clusters per period", Order = 9, GroupName = "1. Profile")]
		public int MaxClustersPerPeriod { get; set; }

		[Range(0, 1000)]
		[Display(Name = "Touch grace bars", Description = "Bars after the period ends during which touches are ignored.", Order = 10, GroupName = "1. Profile")]
		public int TouchGraceBars { get; set; }

		[Display(Name = "Footprint source", Description = "Auto uses Volumetric bars when the chart is Volumetric, otherwise the trade stream (Tick Replay); bars without footprint use their bar volume.", Order = 1, GroupName = "2. Data")]
		public IdcFootprintSource FootprintSource { get; set; }

		[Range(100, 200000)]
		[Display(Name = "Max bars back", Description = "Only the most recent bars are profiled (performance).", Order = 2, GroupName = "2. Data")]
		public int MaxBarsBack { get; set; }

		[Range(10, 10000)]
		[Display(Name = "Max zones kept", Order = 3, GroupName = "2. Data")]
		public int MaxZones { get; set; }

		[XmlIgnore]
		[Display(Name = "Primary zone colour", Description = "The period's highest-volume cluster.", Order = 1, GroupName = "3. Zones")]
		public System.Windows.Media.Brush PrimaryZoneBrush { get; set; }

		[Browsable(false)]
		public string PrimaryZoneBrushSerialize
		{
			get { return Serialize.BrushToString(PrimaryZoneBrush); }
			set { PrimaryZoneBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Secondary zone colour", Description = "Other HVN clusters of the period.", Order = 2, GroupName = "3. Zones")]
		public System.Windows.Media.Brush SecondaryZoneBrush { get; set; }

		[Browsable(false)]
		public string SecondaryZoneBrushSerialize
		{
			get { return Serialize.BrushToString(SecondaryZoneBrush); }
			set { SecondaryZoneBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Zone opacity %", Order = 3, GroupName = "3. Zones")]
		public int ZoneOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Touched zone opacity %", Description = "Opacity of touched zones relative to active ones.", Order = 4, GroupName = "3. Zones")]
		public int TouchedOpacityPercent { get; set; }

		[Display(Name = "Show touched zones", Order = 5, GroupName = "3. Zones")]
		public bool ShowTouchedZones { get; set; }

		[Display(Name = "Extend active zones to", Order = 6, GroupName = "3. Zones")]
		public IdcExtendMode ExtendMode { get; set; }

		[Display(Name = "Show POC line", Order = 7, GroupName = "3. Zones")]
		public bool ShowPocLine { get; set; }

		[Display(Name = "Extend POC line", Description = "Continue the POC line (dimmer) with its zone after its own period.", Order = 8, GroupName = "3. Zones")]
		public bool ExtendPoc { get; set; }

		[Display(Name = "POC line on secondary zones", Description = "By default only the period's primary (orange) zone shows its POC line.", Order = 8, GroupName = "3. Zones")]
		public bool PocOnSecondary { get; set; }

		[XmlIgnore]
		[Display(Name = "POC colour", Order = 9, GroupName = "3. Zones")]
		public System.Windows.Media.Brush PocBrush { get; set; }

		[Browsable(false)]
		public string PocBrushSerialize
		{
			get { return Serialize.BrushToString(PocBrush); }
			set { PocBrush = Serialize.StringToBrush(value); }
		}

		[Range(1, 10)]
		[Display(Name = "POC width", Order = 10, GroupName = "3. Zones")]
		public int PocWidth { get; set; }

		[Display(Name = "Show labels", Order = 11, GroupName = "3. Zones")]
		public bool ShowLabels { get; set; }

		[Display(Name = "Label value", Description = "Volume at the cluster POC, or total cluster volume.", Order = 12, GroupName = "3. Zones")]
		public IdcHvnLabelValue LabelValue { get; set; }

		[Display(Name = "Label font", Order = 13, GroupName = "3. Zones")]
		public SimpleFont LabelFont { get; set; }

		[XmlIgnore]
		[Display(Name = "Label colour", Order = 14, GroupName = "3. Zones")]
		public System.Windows.Media.Brush LabelBrush { get; set; }

		[Browsable(false)]
		public string LabelBrushSerialize
		{
			get { return Serialize.BrushToString(LabelBrush); }
			set { LabelBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Buyers % colour", Order = 15, GroupName = "3. Zones")]
		public System.Windows.Media.Brush BuyPercentBrush { get; set; }

		[Browsable(false)]
		public string BuyPercentBrushSerialize
		{
			get { return Serialize.BrushToString(BuyPercentBrush); }
			set { BuyPercentBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Sellers % colour", Order = 16, GroupName = "3. Zones")]
		public System.Windows.Media.Brush SellPercentBrush { get; set; }

		[Browsable(false)]
		public string SellPercentBrushSerialize
		{
			get { return Serialize.BrushToString(SellPercentBrush); }
			set { SellPercentBrush = Serialize.StringToBrush(value); }
		}

		[Display(Name = "Show histogram", Order = 1, GroupName = "4. Histogram")]
		public bool ShowHistogram { get; set; }

		[Range(1, 100)]
		[Display(Name = "Histogram width %", Description = "Widest row as % of the period's width.", Order = 2, GroupName = "4. Histogram")]
		public int HistogramWidthPercent { get; set; }

		[XmlIgnore]
		[Display(Name = "Histogram colour", Order = 3, GroupName = "4. Histogram")]
		public System.Windows.Media.Brush HistogramBrush { get; set; }

		[Browsable(false)]
		public string HistogramBrushSerialize
		{
			get { return Serialize.BrushToString(HistogramBrush); }
			set { HistogramBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Histogram opacity %", Order = 4, GroupName = "4. Histogram")]
		public int HistogramOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Live profile opacity %", Description = "Opacity of the developing (current) period's histogram.", Order = 5, GroupName = "4. Histogram")]
		public int LiveProfileOpacity { get; set; }

		[Display(Name = "Show data warnings", Order = 6, GroupName = "4. Histogram")]
		public bool ShowWarnings { get; set; }

		[Display(Name = "Alert on HVN touch", Order = 1, GroupName = "5. Alerts")]
		public bool AlertOnTouch { get; set; }

		[Display(Name = "Alert sound", Description = "File in NinjaTrader's sounds folder or a full path.", Order = 2, GroupName = "5. Alerts")]
		public string AlertSound { get; set; }

		[Range(0, 3600)]
		[Display(Name = "Alert rearm seconds", Order = 3, GroupName = "5. Alerts")]
		public int AlertRearmSeconds { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private IDCator.IDCHvnProfile[] cacheIDCHvnProfile;
		public IDCator.IDCHvnProfile IDCHvnProfile()
		{
			return IDCHvnProfile(Input);
		}

		public IDCator.IDCHvnProfile IDCHvnProfile(ISeries<double> input)
		{
			if (cacheIDCHvnProfile != null)
				for (int idx = 0; idx < cacheIDCHvnProfile.Length; idx++)
					if (cacheIDCHvnProfile[idx] != null &&  cacheIDCHvnProfile[idx].EqualsInput(input))
						return cacheIDCHvnProfile[idx];
			return CacheIndicator<IDCator.IDCHvnProfile>(new IDCator.IDCHvnProfile(), input, ref cacheIDCHvnProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.IDCator.IDCHvnProfile IDCHvnProfile()
		{
			return indicator.IDCHvnProfile(Input);
		}

		public Indicators.IDCator.IDCHvnProfile IDCHvnProfile(ISeries<double> input )
		{
			return indicator.IDCHvnProfile(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.IDCator.IDCHvnProfile IDCHvnProfile()
		{
			return indicator.IDCHvnProfile(Input);
		}

		public Indicators.IDCator.IDCHvnProfile IDCHvnProfile(ISeries<double> input )
		{
			return indicator.IDCHvnProfile(input);
		}
	}
}

#endregion
