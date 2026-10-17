//
// IDC Pressure Toolkit - multi-signal footprint tool for aggressive and passive pressure.
//
// Signals
//   * Pressure bubbles: price levels inside a bar where aggressive buying (ask) or selling (bid) volume is
//     statistically large and dominates the other side. Dim bubbles = weak, bright bubbles = strong.
//   * Big trades: an unusually large single print (mean + k standard deviations of print size) that lands on
//     or next to a pressure bubble of the same side. Drawn as a small square with the print's volume.
//   * Absorption dots: a wick (part of the bar outside the body) that holds a high-volume level, meaning
//     aggressive orders were absorbed there by passive limit orders. Dot above the bar = absorption at the
//     high, dot below = absorption at the low. Enhanced (bigger) dots mark statistically significant events.
//   * Absorption lines: one line per high-volume wick level, extended right until price comes back to test
//     it. Colour follows the aggressor that was absorbed (green = buyers, red = sellers).
//   * Psychological levels: adaptive round numbers scaled to the instrument price, with cross alerts.
// Every signal has its own alert. Markers visualise order flow - they are not buy or sell signals.
//
// Data: footprint from a Volumetric chart or from the trade stream (Tick Replay for history).
// Big trades need the trade stream (Tick Replay for history, realtime always).
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
	public class IDCPressureToolkit : Indicator
	{
		private sealed class Bubble
		{
			public int		Bar;
			public long		Tick;
			public int		Side;		// +1 buy pressure, -1 sell pressure
			public bool		Strong;
			public double	Volume;
			public double	Scale;		// volume / weak threshold
		}

		private sealed class BigTrade
		{
			public int		Bar;
			public long		Tick;
			public int		Side;
			public double	Volume;
		}

		private sealed class AbsorptionDot
		{
			public int		Bar;
			public bool		AtHigh;
			public bool		Enhanced;
		}

		private sealed class AbsorptionLine
		{
			public int		Id;
			public int		Bar;
			public long		Tick;
			public int		Side;		// aggressor that was absorbed
			public int		EndBar = -1;
		}

		private sealed class BarSignals
		{
			public readonly List<Bubble>	Bubbles		= new List<Bubble>();
			public readonly List<BigTrade>	BigTrades	= new List<BigTrade>();
			public readonly List<AbsorptionDot> Dots	= new List<AbsorptionDot>();
		}

		private readonly object							sync			= new object();
		private readonly Dictionary<int, BarSignals>	signals			= new Dictionary<int, BarSignals>();
		private readonly List<AbsorptionLine>			lines			= new List<AbsorptionLine>();
		private readonly Dictionary<int, List<IdcTrade>> largePrints	= new Dictionary<int, List<IdcTrade>>();
		private BarSignals								liveSignals;
		private int										liveBar			= -1;
		private DateTime								lastLiveUpdate	= DateTime.MinValue;
		private bool									liveDirty;
		private bool									sessionBreakClose;
		private IdcFootprintEngine						engine;
		private IdcBarWindowStats						aggressiveStats;
		private IdcBarWindowStats						levelStats;
		private IdcBarWindowStats						printStats;
		private IdcDxBrushCache							dxBrushes;
		private int										lastAnalyzedBar	= -1;
		private int										lastPruneBar	= -1;
		private int										nextLineId;
		private double									lastPrice;
		private double									psychStep;
		private bool									calculateWarning;
		private int										firstRealtimeTradeBar = -1;
		private readonly List<IdcPendingAlert>			pendingAlerts	= new List<IdcPendingAlert>();
		private int						touchBar	= -1;
		private double					touchHigh;
		private double					touchLow;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Pressure bubbles, big trades, absorption dots and lines and psychological levels from footprint data, each with its own alert. Not a buy/sell signal.";
				Name						= "IDC Pressure Toolkit";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				DrawOnPricePanel			= true;
				DisplayInDataBox			= false;
				PaintPriceMarkers			= false;
				IsSuspendedWhileInactive	= false;
				ScaleJustification			= ScaleJustification.Right;
				MaximumBarsLookBack			= MaximumBarsLookBack.TwoHundredFiftySix;

				LookbackBars				= 50;
				ShowBubbles					= true;
				BubbleStdDev				= 1.5;
				StrongBubbleStdDev			= 3.0;
				BubbleDominance				= 1.5;
				MaxBubblesPerSide			= 2;

				ShowBigTrades				= true;
				BigTradeStdDev				= 3.0;
				BigTradeNeedsBubble			= true;
				BigTradeProximityTicks		= 2;

				ShowAbsorption				= true;
				AbsorptionStdDev			= 1.0;
				EnhancedAbsorptionStdDev	= 2.5;
				MinWickVolumePercent		= 15;
				ShowAbsorptionLines			= true;
				MaxLinesPerWick				= 8;

				ShowPsychLevels				= true;
				PsychStep					= 0;
				PsychMajorEvery				= 4;
				PsychLevelsEachSide			= 6;

				FootprintSource				= IdcFootprintSource.Auto;
				MaxBarsBack					= 3000;
				MaxLines					= 600;
				LiveBar						= true;

				BuyBrush					= new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x5A));
				BuyBrush.Freeze();
				SellBrush					= new SolidColorBrush(Color.FromRgb(0xD6, 0x2E, 0x3A));
				SellBrush.Freeze();
				WeakBubbleOpacity			= 30;
				StrongBubbleOpacity			= 75;
				BubbleDiameter				= 22;
				BigTradeSize				= 12;
				DotBullBrush				= Brushes.LimeGreen;
				DotBearBrush				= new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
				DotBearBrush.Freeze();
				LineOpacity					= 80;
				PsychBrush					= Brushes.Gray;
				PsychOpacity				= 45;
				TextBrush					= Brushes.Gray;
				TextFont					= new SimpleFont("Arial", 10);
				ShowWarnings				= true;

				AlertBubbles				= false;
				AlertBigTrades				= false;
				AlertAbsorptionDots			= false;
				AlertAbsorptionLineTouch	= false;
				AlertPsychCross				= false;
				AlertSound					= "Alert3.wav";
				AlertRearmSeconds			= 10;
			}
			else if (State == State.DataLoaded)
			{
				engine = new IdcFootprintEngine();
				engine.MaxBarsKept = Math.Max(50, LookbackBars + 20);
				engine.Initialize(Bars, TickSize, Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency, FootprintSource);
				aggressiveStats	= new IdcBarWindowStats(LookbackBars);
				levelStats		= new IdcBarWindowStats(LookbackBars);
				printStats		= new IdcBarWindowStats(LookbackBars);
				dxBrushes		= new IdcDxBrushCache();
				lock (sync)
				{
					signals.Clear();
					lines.Clear();
					largePrints.Clear();
					liveSignals		= null;
					liveBar			= -1;
					lastAnalyzedBar	= -1;
					lastPruneBar	= -1;
					nextLineId		= 0;
					lastPrice		= 0;
					firstRealtimeTradeBar	= -1;
					pendingAlerts.Clear();
					psychStep		= 0;
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
			bool isTrade;
			lock (sync)		// the footprint store is also read by the analysis (OnBarUpdate, OnStateChange)
				isTrade = engine.ProcessMarketData(marketDataUpdate, CurrentBar, out trade);
			if (isTrade)
			{
				if (firstRealtimeTradeBar < 0 && State == State.Realtime)
					firstRealtimeTradeBar = trade.BarIndex;
				if (trade.Side != 0 && ShowBigTrades && CurrentBar >= Count - MaxBarsBack)
					lock (sync)
						RecordPrint(trade);
			}

			// forming-bar preview AFTER the trade reached the footprint (OnBarUpdate runs before this tick's
			// OnMarketData); a throttled update is retried on the next event, quote updates included
			if (LiveBar && State == State.Realtime)
				lock (sync)
				{
					if (isTrade)
						liveDirty = true;
					if (liveDirty)
						UpdateLiveBar();
				}
		}

		private void RecordPrint(IdcTrade trade)
		{
			bool ready		= printStats.Count >= 200;
			double limit	= printStats.Threshold(BigTradeStdDev);
			printStats.Add(trade.BarIndex, trade.Volume);
			// strictly above: with uniform print sizes (std dev 0) no print is unusually large
			if (!ready || trade.Volume <= limit)
				return;

			List<IdcTrade> list;
			if (!largePrints.TryGetValue(trade.BarIndex, out list))
			{
				list = new List<IdcTrade>();
				largePrints.Add(trade.BarIndex, list);
			}
			list.Add(trade);
		}

		protected override void OnBarUpdate()
		{
			if (engine == null || CurrentBar < 1)
				return;

			int closed = IdcBars.LastClosedBar(State, Bars.IsTickReplay, CurrentBar, Count);
			// a bar that is only seen to close when the next session opens ended long ago: no alert for it
			sessionBreakClose = Bars.IsFirstBarOfSession;
			// a gap from the previous close to this bar crosses every level in between
			double high	= Math.Max(High[0], Close[1]);
			double low	= Math.Min(Low[0], Close[1]);
			lock (sync)
			{
				AnalyzeClosedBars(closed);

				// zones/lines only change when a new bar opened (new zones) or the forming bar made a new high/low
				if (CurrentBar != touchBar || high > touchHigh || low < touchLow)
				{
					touchBar	= CurrentBar;
					touchHigh	= high;
					touchLow	= low;
					CheckLineTouches(CurrentBar, high, low);
				}
				CheckPsychCross(Close[0]);

				if (CurrentBar - lastPruneBar >= 200)
				{
					lastPruneBar = CurrentBar;
					Prune(CurrentBar - MaxBarsBack);
				}
			}
			FlushAlerts();
		}

		private void AnalyzeClosedBars(int closed)
		{
			while (lastAnalyzedBar < closed)
			{
				lastAnalyzedBar++;
				if (lastAnalyzedBar >= Count - MaxBarsBack)
					AnalyzeClosedBar(lastAnalyzedBar);
			}
		}

		private void QueueAlert(string id, Priority priority, string message, string sound, int rearmSeconds,
			System.Windows.Media.Brush backBrush, System.Windows.Media.Brush foreBrush)
		{
			pendingAlerts.Add(new IdcPendingAlert { Id = id, Priority = priority, Message = message, Sound = sound,
				RearmSeconds = rearmSeconds, BackBrush = backBrush, ForeBrush = foreBrush });
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

		#region Bar analysis
		private void AnalyzeClosedBar(int bar)
		{
			IdcFootprintBar fp = engine.GetBar(bar, true);
			if (fp == null || fp.IsEmpty)
				return;

			BarSignals result = Evaluate(fp, bar);
			if (result.Bubbles.Count > 0 || result.Dots.Count > 0 || result.BigTrades.Count > 0)
				signals[bar] = result;

			// lines use the same (previous-window) level threshold as the dot, so add them before learning from this bar
			if (ShowAbsorption && ShowAbsorptionLines)
				AddAbsorptionLines(fp, bar);

			// learn from the closed bar only after it was judged against the previous window
			int step = Math.Max(1, engine.LevelStep);
			for (long t = fp.LowTick; t <= fp.HighTick; t += step)
			{
				IdcCell cell = fp.Get(t);
				if (cell == null)
					continue;
				if (cell.Ask > 0) aggressiveStats.Add(bar, cell.Ask);
				if (cell.Bid > 0) aggressiveStats.Add(bar, cell.Bid);
				levelStats.Add(bar, cell.Total);
			}

			largePrints.Remove(bar);
			if (liveBar == bar)
			{
				liveSignals	= null;
				liveBar		= -1;
			}

			if (State == State.Realtime && !sessionBreakClose && firstRealtimeTradeBar >= 0 && bar >= firstRealtimeTradeBar)
				FireBarAlerts(result, bar);
		}

		private void UpdateLiveBar()
		{
			// platform clock: wall time in realtime, replay time in Playback
			DateTime now = NinjaTrader.Core.Globals.Now;
			if (liveBar == CurrentBar && (now - lastLiveUpdate).TotalMilliseconds < 250)
				return;

			IdcFootprintBar fp = engine.GetBar(CurrentBar, false);
			if (fp == null || fp.IsEmpty)
				return;
			lastLiveUpdate	= now;
			liveDirty		= false;
			liveSignals		= Evaluate(fp, CurrentBar);
			liveBar			= CurrentBar;
		}

		/// <summary>First tick of the footprint row that holds the tick (Volumetric "ticks per level"; the tick itself for tick data).</summary>
		private static long RowStart(long tick, int step)
		{
			return tick - (((tick % step) + step) % step);
		}

		/// <summary>Wick rows lie entirely outside the body's rows: upper = above the row holding the body top, lower = below the row holding the body bottom.</summary>
		private void WickRange(IdcFootprintBar fp, int bar, bool atHigh, int step, out long from, out long to)
		{
			double open		= Bars.GetOpen(bar);
			double close	= Bars.GetClose(bar);
			long bodyTop	= engine.ToTick(Math.Max(open, close));
			long bodyBottom	= engine.ToTick(Math.Min(open, close));
			from	= atHigh ? RowStart(bodyTop, step) + step : fp.LowTick;
			to		= atHigh ? fp.HighTick : RowStart(bodyBottom, step) - 1;
		}

		/// <summary>Bubbles, big trades and absorption dots of one bar, judged against the statistics of earlier bars.</summary>
		private BarSignals Evaluate(IdcFootprintBar fp, int bar)
		{
			BarSignals result = new BarSignals();
			if (aggressiveStats.Count < 30 || levelStats.Count < 30)
				return result;

			int		step		= Math.Max(1, engine.LevelStep);
			double	weak		= aggressiveStats.Threshold(BubbleStdDev);
			double	strong		= aggressiveStats.Threshold(StrongBubbleStdDev);

			// bubbles are always evaluated: big trades are matched against them even while they are hidden
			List<Bubble> bubbles = new List<Bubble>();
			{
				List<Bubble> buys	= new List<Bubble>();
				List<Bubble> sells	= new List<Bubble>();
				for (long t = fp.LowTick; t <= fp.HighTick; t += step)
				{
					IdcCell cell = fp.Get(t);
					if (cell == null)
						continue;
					// strictly above the mean as well: a window without variance (std dev 0) marks nothing
					if (cell.Ask >= weak && cell.Ask > aggressiveStats.Mean && cell.Ask > cell.Bid && cell.Ask >= BubbleDominance * cell.Bid)
						buys.Add(new Bubble { Bar = bar, Tick = t, Side = 1, Volume = cell.Ask, Strong = cell.Ask >= strong, Scale = cell.Ask / Math.Max(1e-9, weak) });
					if (cell.Bid >= weak && cell.Bid > aggressiveStats.Mean && cell.Bid > cell.Ask && cell.Bid >= BubbleDominance * cell.Ask)
						sells.Add(new Bubble { Bar = bar, Tick = t, Side = -1, Volume = cell.Bid, Strong = cell.Bid >= strong, Scale = cell.Bid / Math.Max(1e-9, weak) });
				}
				buys.Sort((a, b) => b.Volume.CompareTo(a.Volume));
				sells.Sort((a, b) => b.Volume.CompareTo(a.Volume));
				bubbles.AddRange(buys.Take(MaxBubblesPerSide));
				bubbles.AddRange(sells.Take(MaxBubblesPerSide));
			}
			if (ShowBubbles)
				result.Bubbles.AddRange(bubbles);

			if (ShowBigTrades)
			{
				List<IdcTrade> prints;
				if (largePrints.TryGetValue(bar, out prints))
				{
					// largest qualifying print per side
					foreach (int side in new[] { 1, -1 })
					{
						IdcTrade best = new IdcTrade();
						bool found = false;
						foreach (IdcTrade p in prints)
						{
							if (p.Side != side)
								continue;
							if (BigTradeNeedsBubble && !NearBubble(bubbles, p.Tick, side, step))
								continue;
							if (!found || p.Volume > best.Volume)
							{
								best	= p;
								found	= true;
							}
						}
						if (found)
							result.BigTrades.Add(new BigTrade { Bar = bar, Tick = best.Tick, Side = side, Volume = best.Volume });
					}
				}
			}

			if (ShowAbsorption)
			{
				double levelLimit		= levelStats.Threshold(AbsorptionStdDev);
				double enhancedLimit	= levelStats.Threshold(EnhancedAbsorptionStdDev);
				double total			= Math.Max(1e-9, fp.TotalVolume);
				long from, to;

				WickRange(fp, bar, true, step, out from, out to);
				AbsorptionDot upper = WickDot(fp, bar, from, to, true, levelLimit, enhancedLimit, total);
				WickRange(fp, bar, false, step, out from, out to);
				AbsorptionDot lower = WickDot(fp, bar, from, to, false, levelLimit, enhancedLimit, total);
				if (upper != null) result.Dots.Add(upper);
				if (lower != null) result.Dots.Add(lower);
			}
			return result;
		}

		/// <summary>True when the print lies within BigTradeProximityTicks of a same-side bubble's row (a row spans 'step' ticks).</summary>
		private bool NearBubble(List<Bubble> bubbles, long tick, int side, int step)
		{
			foreach (Bubble b in bubbles)
			{
				if (b.Side != side)
					continue;
				long rowTop		= b.Tick + step - 1;
				long distance	= tick < b.Tick ? b.Tick - tick : (tick > rowTop ? tick - rowTop : 0);
				if (distance <= BigTradeProximityTicks)
					return true;
			}
			return false;
		}

		private AbsorptionDot WickDot(IdcFootprintBar fp, int bar, long from, long to, bool atHigh, double levelLimit, double enhancedLimit, double total)
		{
			if (to < from)
				return null;

			double wickVolume = 0, maxLevel = 0;
			for (long t = from; t <= to; t++)
			{
				double v = fp.Volume(t);
				wickVolume	+= v;
				maxLevel	= Math.Max(maxLevel, v);
			}

			if (maxLevel < levelLimit || maxLevel <= levelStats.Mean || wickVolume / total * 100.0 < MinWickVolumePercent)
				return null;
			return new AbsorptionDot { Bar = bar, AtHigh = atHigh, Enhanced = maxLevel >= enhancedLimit };
		}

		private void AddAbsorptionLines(IdcFootprintBar fp, int bar)
		{
			BarSignals s;
			if (!signals.TryGetValue(bar, out s) || s.Dots.Count == 0)
				return;

			double levelLimit	= levelStats.Count > 0 ? levelStats.Threshold(AbsorptionStdDev) : double.MaxValue;
			int step			= Math.Max(1, engine.LevelStep);

			foreach (AbsorptionDot dot in s.Dots)
			{
				long from, to;
				WickRange(fp, bar, dot.AtHigh, step, out from, out to);

				List<long> levels = new List<long>();
				for (long t = from; t <= to; t++)
				{
					IdcCell cell = fp.Get(t);
					if (cell != null && cell.Total >= levelLimit)
						levels.Add(t);
				}
				if (levels.Count == 0)
					continue;

				levels.Sort((a, b) => fp.Volume(b).CompareTo(fp.Volume(a)));
				foreach (long t in levels.Take(MaxLinesPerWick))
				{
					IdcCell cell = fp.Get(t);
					lines.Add(new AbsorptionLine { Id = nextLineId++, Bar = bar, Tick = t, Side = cell.Ask >= cell.Bid ? 1 : -1 });
				}
			}

			while (lines.Count > MaxLines)
			{
				int touched = lines.FindIndex(l => l.EndBar >= 0);
				lines.RemoveAt(touched >= 0 ? touched : 0);
			}
		}
		#endregion

		#region Touches, psychological levels and alerts
		private void CheckLineTouches(int bar, double high, double low)
		{
			foreach (AbsorptionLine line in lines)
			{
				if (line.EndBar >= 0 || bar <= line.Bar)
					continue;
				double price = engine.ToPrice(line.Tick);
				if (engine.ToTick(low) > line.Tick || engine.ToTick(high) < line.Tick)
					continue;

				line.EndBar = bar;
				if (AlertAbsorptionLineTouch && State == State.Realtime)
					QueueAlert("IDCPtkLine" + line.Id, Priority.Medium,
						string.Format("{0}: price retested absorption level {1}", Instrument.FullName, Instrument.MasterInstrument.FormatPrice(price)),
						SoundPath(AlertSound), AlertRearmSeconds, line.Side > 0 ? Brushes.DarkGreen : Brushes.DarkRed, Brushes.White);
			}
		}

		private double PsychLevelStep(double price)
		{
			if (PsychStep > 0)
				return PsychStep;
			if (price <= 0)
				return 0;

			// about 0.3 % of price, rounded to a 1 / 2.5 / 5 x 10^n step that is a whole number of ticks:
			// NQ 100, ES 25, GC 10, CL 0.25, BTC 250, a 7.80 stock 0.05 (not 0.025)
			double tick			= engine != null ? engine.TickSize : TickSize;
			double target		= price * 0.003;
			double magnitude	= Math.Pow(10, Math.Floor(Math.Log10(target)));
			double[] nice		= { 1, 2.5, 5, 10, 25, 50, 100 };
			foreach (double n in nice)
			{
				double step		= magnitude * n;
				double ticks	= step / tick;
				if (step >= target * 0.75 && ticks >= 1 - 1e-9 && Math.Abs(ticks - Math.Round(ticks)) < 1e-6)
					return Math.Round(ticks) * tick;
			}
			return Math.Max(1, Math.Ceiling(target / tick - 1e-9)) * tick;
		}

		private void CheckPsychCross(double price)
		{
			if (price <= 0)
				return;

			// IsFirstBarOfSession is always false on Day/Week/Month bars: there every new bar is a new session
			if (psychStep <= 0 || (IsFirstTickOfBar && (Bars.IsFirstBarOfSession || (Bars.BarsType != null && !Bars.BarsType.IsIntraday))))
				psychStep = PsychLevelStep(price);

			if (lastPrice > 0 && psychStep > 0 && State == State.Realtime && AlertPsychCross && ShowPsychLevels)
			{
				// the level nearest to the new price on the far side of the move: up -> highest level <= price,
				// down -> lowest level >= price; it is crossed when the previous price was strictly on the other side
				bool up = price > lastPrice;
				double level = up ? Math.Floor(price / psychStep + 1e-9) * psychStep : Math.Ceiling(price / psychStep - 1e-9) * psychStep;
				if (lastPrice != price && (up ? level > lastPrice + 1e-9 : level < lastPrice - 1e-9))
					QueueAlert("IDCPtkPsych" + Math.Round(level / psychStep), Priority.Low,
						string.Format("{0}: price crossed psychological level {1}", Instrument.FullName, Instrument.MasterInstrument.FormatPrice(level)),
						SoundPath(AlertSound), Math.Max(AlertRearmSeconds, 30), Brushes.DimGray, Brushes.White);
			}
			lastPrice = price;
		}

		private void FireBarAlerts(BarSignals s, int bar)
		{
			string sound = SoundPath(AlertSound);
			if (AlertBubbles)
				foreach (Bubble b in s.Bubbles)
					QueueAlert("IDCPtkBubble" + bar + (b.Side > 0 ? "B" : "S") + b.Tick, Priority.Medium,
						string.Format("{0}: {1} {2} pressure bubble {3} @ {4}", Instrument.FullName, b.Strong ? "strong" : "weak",
							b.Side > 0 ? "buying" : "selling", IdcDraw.Volume(b.Volume), Instrument.MasterInstrument.FormatPrice(engine.ToPrice(b.Tick))),
						sound, AlertRearmSeconds, b.Side > 0 ? Brushes.DarkGreen : Brushes.DarkRed, Brushes.White);

			if (AlertBigTrades)
				foreach (BigTrade t in s.BigTrades)
					QueueAlert("IDCPtkBig" + bar + (t.Side > 0 ? "B" : "S"), Priority.High,
						string.Format("{0}: big {1} trade {2} @ {3}", Instrument.FullName, t.Side > 0 ? "BUY" : "SELL",
							IdcDraw.Volume(t.Volume), Instrument.MasterInstrument.FormatPrice(engine.ToPrice(t.Tick))),
						sound, AlertRearmSeconds, t.Side > 0 ? Brushes.DarkGreen : Brushes.DarkRed, Brushes.White);

			if (AlertAbsorptionDots)
				foreach (AbsorptionDot d in s.Dots)
					QueueAlert("IDCPtkDot" + bar + (d.AtHigh ? "H" : "L"), Priority.Medium,
						string.Format("{0}: {1}absorption at the bar {2}", Instrument.FullName, d.Enhanced ? "strong " : "", d.AtHigh ? "high" : "low"),
						sound, AlertRearmSeconds, d.AtHigh ? Brushes.DarkRed : Brushes.DarkGreen, Brushes.White);
		}

		private void Prune(int oldestBar)
		{
			if (oldestBar <= 0)
				return;
			List<int> stale = new List<int>();
			foreach (int key in signals.Keys)
				if (key < oldestBar)
					stale.Add(key);
			foreach (int key in stale)
				signals.Remove(key);

			stale.Clear();
			foreach (int key in largePrints.Keys)
				if (key < oldestBar)
					stale.Add(key);
			foreach (int key in stale)
				largePrints.Remove(key);

			lines.RemoveAll(l => l.EndBar >= 0 && l.EndBar < oldestBar);
		}

		private static string SoundPath(string file)
		{
			return IdcSound.Resolve(file);
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
			float	halfBar		= (float)Math.Max(1.0, chartControl.GetBarPaintWidth(ChartBars) / 2.0);
			int		lastBar		= ChartBars.Bars != null ? ChartBars.Bars.Count - 1 : toIndex;
			float	lastBarX	= chartControl.GetXByBarIndex(ChartBars, lastBar) + halfBar;

			List<BarSignals>		visible		= new List<BarSignals>();
			List<AbsorptionLine>	lineList	= new List<AbsorptionLine>();
			double					step;
			lock (sync)
			{
				for (int bar = fromIndex; bar <= toIndex; bar++)
				{
					BarSignals s;
					if (signals.TryGetValue(bar, out s))
						visible.Add(s);
				}
				if (liveSignals != null && liveBar >= fromIndex && liveBar <= toIndex)
					visible.Add(liveSignals);

				if (ShowAbsorption && ShowAbsorptionLines)
					foreach (AbsorptionLine l in lines)
						if (l.Bar <= toIndex && (l.EndBar < 0 || l.EndBar >= fromIndex))
							lineList.Add(new AbsorptionLine { Bar = l.Bar, Tick = l.Tick, Side = l.Side, EndBar = l.EndBar });
				step = psychStep;
			}

			SharpDX.Direct2D1.AntialiasMode oldMode = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			SharpDX.DirectWrite.TextFormat textFormat = TextFont != null ? TextFont.ToDirectWriteTextFormat() : null;
			try
			{
				SharpDX.Direct2D1.Brush textBrush = dxBrushes.Get(RenderTarget, TextBrush, 1.0);

				if (ShowPsychLevels && step > 0)
					RenderPsychLevels(chartScale, step, panelLeft, panelRight, textFormat, textBrush);

				// absorption lines
				foreach (AbsorptionLine l in lineList)
				{
					float x1 = Math.Max(panelLeft, chartControl.GetXByBarIndex(ChartBars, l.Bar));
					float x2 = l.EndBar >= 0 ? chartControl.GetXByBarIndex(ChartBars, l.EndBar) : lastBarX;
					x2 = Math.Min(panelRight, x2);
					if (x2 - x1 < 1)
						continue;
					float y = chartScale.GetYByValue(engine.ToPrice(l.Tick));
					RenderTarget.DrawLine(new SharpDX.Vector2(x1, y), new SharpDX.Vector2(x2, y),
						dxBrushes.Get(RenderTarget, l.Side > 0 ? BuyBrush : SellBrush, LineOpacity / 100.0), 1f);
				}

				foreach (BarSignals s in visible)
				{
					// pressure bubbles
					foreach (Bubble b in s.Bubbles)
					{
						float	x		= chartControl.GetXByBarIndex(ChartBars, b.Bar);
						float	y		= chartScale.GetYByValue(engine.ToPrice(b.Tick));
						double	grow	= Math.Min(1.6, Math.Max(0.8, Math.Sqrt(b.Scale) * 0.8));
						float	r		= (float)(BubbleDiameter / 2.0 * grow);
						RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), r, r),
							dxBrushes.Get(RenderTarget, b.Side > 0 ? BuyBrush : SellBrush, (b.Strong ? StrongBubbleOpacity : WeakBubbleOpacity) / 100.0));
					}

					// big trades
					foreach (BigTrade t in s.BigTrades)
					{
						float x		= chartControl.GetXByBarIndex(ChartBars, t.Bar);
						float y		= chartScale.GetYByValue(engine.ToPrice(t.Tick));
						float half	= BigTradeSize / 2f;
						SharpDX.RectangleF rect = new SharpDX.RectangleF(x - half, y - half, BigTradeSize, BigTradeSize);
						System.Windows.Media.Brush color = t.Side > 0 ? BuyBrush : SellBrush;
						RenderTarget.FillRectangle(rect, dxBrushes.Get(RenderTarget, color, 0.25));
						RenderTarget.DrawRectangle(rect, dxBrushes.Get(RenderTarget, color, 0.9), 1f);
						if (textFormat != null)
						{
							if (t.Side > 0)
								IdcDraw.Text(RenderTarget, textFormat, IdcDraw.Volume(t.Volume), x, y + half + 1, textBrush, IdcTextAnchor.TopCenter);
							else
								IdcDraw.Text(RenderTarget, textFormat, IdcDraw.Volume(t.Volume), x, y - half - 1, textBrush, IdcTextAnchor.BottomCenter);
						}
					}

					// absorption dots
					foreach (AbsorptionDot d in s.Dots)
					{
						float x		= chartControl.GetXByBarIndex(ChartBars, d.Bar);
						float r		= d.Enhanced ? 4.5f : 2.5f;
						float y		= d.AtHigh
							? chartScale.GetYByValue(Bars.GetHigh(d.Bar)) - 7 - r
							: chartScale.GetYByValue(Bars.GetLow(d.Bar)) + 7 + r;
						RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), r, r),
							dxBrushes.Get(RenderTarget, d.AtHigh ? DotBearBrush : DotBullBrush, d.Enhanced ? 1.0 : 0.85));
					}
				}

				if (ShowWarnings)
					RenderWarning(textBrush);
			}
			finally
			{
				RenderTarget.AntialiasMode = oldMode;
				if (textFormat != null)
					textFormat.Dispose();
			}
		}

		private void RenderPsychLevels(ChartScale chartScale, double step, float panelLeft, float panelRight,
			SharpDX.DirectWrite.TextFormat textFormat, SharpDX.Direct2D1.Brush textBrush)
		{
			// centre on the last price while it is in view, otherwise on the visible range (scrolled back / other price area)
			double reference = lastPrice > chartScale.MinValue && lastPrice < chartScale.MaxValue ? lastPrice : (chartScale.MaxValue + chartScale.MinValue) / 2.0;
			long center = (long)Math.Round(reference / step);
			for (long i = center - PsychLevelsEachSide; i <= center + PsychLevelsEachSide; i++)
			{
				double level = i * step;
				if (level < chartScale.MinValue || level > chartScale.MaxValue)
					continue;
				bool	major	= PsychMajorEvery > 1 && i % PsychMajorEvery == 0;
				float	y		= chartScale.GetYByValue(level);
				RenderTarget.DrawLine(new SharpDX.Vector2(panelLeft, y), new SharpDX.Vector2(panelRight, y),
					dxBrushes.Get(RenderTarget, PsychBrush, PsychOpacity / 100.0 * (major ? 1.0 : 0.6)), major ? 2f : 1f);
				if (textFormat != null)
					IdcDraw.Text(RenderTarget, textFormat, Instrument.MasterInstrument.FormatPrice(level), panelRight - 4, y - 1, textBrush, IdcTextAnchor.BottomRight);
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
			else if (ShowBigTrades && !engine.HasTickReplay)
				warning = "big trades need Tick Replay for history (realtime only)";
			else if (engine.SyntheticQuotes)
				warning = "historical ticks carry no bid/ask - using up/down tick rule";
			if (warning == null)
				return;

			using (SharpDX.DirectWrite.TextFormat format = new SimpleFont("Arial", 10).ToDirectWriteTextFormat())
				IdcDraw.Text(RenderTarget, format, Name + ": " + warning, ChartPanel.X + 6, ChartPanel.Y + 48, brush, IdcTextAnchor.TopLeft);
		}
		#endregion

		#region Properties
		[Range(5, 2000)]
		[Display(Name = "Lookback bars", Description = "Bars in the statistical window used by every signal.", Order = 1, GroupName = "1. Pressure bubbles")]
		public int LookbackBars { get; set; }

		[Display(Name = "Show pressure bubbles", Order = 2, GroupName = "1. Pressure bubbles")]
		public bool ShowBubbles { get; set; }

		[Range(0, 20)]
		[Display(Name = "Weak bubble std dev", Description = "Aggressive volume at a level must exceed mean + k * std dev of level volumes.", Order = 3, GroupName = "1. Pressure bubbles")]
		public double BubbleStdDev { get; set; }

		[Range(0, 20)]
		[Display(Name = "Strong bubble std dev", Description = "Bright (strong) bubble threshold.", Order = 4, GroupName = "1. Pressure bubbles")]
		public double StrongBubbleStdDev { get; set; }

		[Range(1, 100)]
		[Display(Name = "Dominance ratio", Description = "Aggressive side must be at least this many times the other side at the same price.", Order = 5, GroupName = "1. Pressure bubbles")]
		public double BubbleDominance { get; set; }

		[Range(1, 20)]
		[Display(Name = "Max bubbles per side per bar", Order = 6, GroupName = "1. Pressure bubbles")]
		public int MaxBubblesPerSide { get; set; }

		[Display(Name = "Show big trades", Order = 1, GroupName = "2. Big trades")]
		public bool ShowBigTrades { get; set; }

		[Range(0.5, 20)]
		[Display(Name = "Big trade std dev", Description = "A print is big at mean + k * std dev of print size.", Order = 2, GroupName = "2. Big trades")]
		public double BigTradeStdDev { get; set; }

		[Display(Name = "Only with a pressure bubble", Description = "Show big trades only when they coincide with a bubble of the same side.", Order = 3, GroupName = "2. Big trades")]
		public bool BigTradeNeedsBubble { get; set; }

		[Range(0, 50)]
		[Display(Name = "Bubble proximity (ticks)", Order = 4, GroupName = "2. Big trades")]
		public int BigTradeProximityTicks { get; set; }

		[Display(Name = "Show absorption", Order = 1, GroupName = "3. Absorption")]
		public bool ShowAbsorption { get; set; }

		[Range(0, 20)]
		[Display(Name = "Absorption std dev", Description = "A wick level is high volume at mean + k * std dev of level volumes.", Order = 2, GroupName = "3. Absorption")]
		public double AbsorptionStdDev { get; set; }

		[Range(0, 20)]
		[Display(Name = "Enhanced dot std dev", Description = "Statistically significant absorption (bigger dot).", Order = 3, GroupName = "3. Absorption")]
		public double EnhancedAbsorptionStdDev { get; set; }

		[Range(0, 100)]
		[Display(Name = "Min wick volume %", Description = "Share of the bar's volume that must trade in the wick.", Order = 4, GroupName = "3. Absorption")]
		public double MinWickVolumePercent { get; set; }

		[Display(Name = "Show absorption lines", Order = 5, GroupName = "3. Absorption")]
		public bool ShowAbsorptionLines { get; set; }

		[Range(1, 50)]
		[Display(Name = "Max lines per wick", Order = 6, GroupName = "3. Absorption")]
		public int MaxLinesPerWick { get; set; }

		[Display(Name = "Show psychological levels", Order = 1, GroupName = "4. Psychological levels")]
		public bool ShowPsychLevels { get; set; }

		[Range(0, double.MaxValue)]
		[Display(Name = "Level step (0 = auto)", Description = "Distance between round-number levels. Auto scales with price (NQ 100, ES 25, GC 10, CL 0.25).", Order = 2, GroupName = "4. Psychological levels")]
		public double PsychStep { get; set; }

		[Range(0, 100)]
		[Display(Name = "Major level every", Description = "Every Nth level is drawn thicker (0 = none).", Order = 3, GroupName = "4. Psychological levels")]
		public int PsychMajorEvery { get; set; }

		[Range(1, 50)]
		[Display(Name = "Levels each side", Order = 4, GroupName = "4. Psychological levels")]
		public int PsychLevelsEachSide { get; set; }

		[Display(Name = "Footprint source", Order = 1, GroupName = "5. Data")]
		public IdcFootprintSource FootprintSource { get; set; }

		[Range(50, 100000)]
		[Display(Name = "Max bars back", Order = 2, GroupName = "5. Data")]
		public int MaxBarsBack { get; set; }

		[Range(10, 10000)]
		[Display(Name = "Max absorption lines", Order = 3, GroupName = "5. Data")]
		public int MaxLines { get; set; }

		[Display(Name = "Show forming bar live", Description = "Draw bubbles/dots of the current bar while it forms (alerts still wait for bar close).", Order = 4, GroupName = "5. Data")]
		public bool LiveBar { get; set; }

		[XmlIgnore]
		[Display(Name = "Buy colour", Order = 1, GroupName = "6. Display")]
		public System.Windows.Media.Brush BuyBrush { get; set; }

		[Browsable(false)]
		public string BuyBrushSerialize
		{
			get { return Serialize.BrushToString(BuyBrush); }
			set { BuyBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Sell colour", Order = 2, GroupName = "6. Display")]
		public System.Windows.Media.Brush SellBrush { get; set; }

		[Browsable(false)]
		public string SellBrushSerialize
		{
			get { return Serialize.BrushToString(SellBrush); }
			set { SellBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Weak bubble opacity %", Order = 3, GroupName = "6. Display")]
		public int WeakBubbleOpacity { get; set; }

		[Range(0, 100)]
		[Display(Name = "Strong bubble opacity %", Order = 4, GroupName = "6. Display")]
		public int StrongBubbleOpacity { get; set; }

		[Range(4, 200)]
		[Display(Name = "Bubble diameter (px)", Order = 5, GroupName = "6. Display")]
		public int BubbleDiameter { get; set; }

		[Range(4, 100)]
		[Display(Name = "Big trade square (px)", Order = 6, GroupName = "6. Display")]
		public int BigTradeSize { get; set; }

		[XmlIgnore]
		[Display(Name = "Absorption dot (low) colour", Order = 7, GroupName = "6. Display")]
		public System.Windows.Media.Brush DotBullBrush { get; set; }

		[Browsable(false)]
		public string DotBullBrushSerialize
		{
			get { return Serialize.BrushToString(DotBullBrush); }
			set { DotBullBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Absorption dot (high) colour", Order = 8, GroupName = "6. Display")]
		public System.Windows.Media.Brush DotBearBrush { get; set; }

		[Browsable(false)]
		public string DotBearBrushSerialize
		{
			get { return Serialize.BrushToString(DotBearBrush); }
			set { DotBearBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Absorption line opacity %", Order = 9, GroupName = "6. Display")]
		public int LineOpacity { get; set; }

		[XmlIgnore]
		[Display(Name = "Psychological level colour", Order = 10, GroupName = "6. Display")]
		public System.Windows.Media.Brush PsychBrush { get; set; }

		[Browsable(false)]
		public string PsychBrushSerialize
		{
			get { return Serialize.BrushToString(PsychBrush); }
			set { PsychBrush = Serialize.StringToBrush(value); }
		}

		[Range(0, 100)]
		[Display(Name = "Psychological level opacity %", Order = 11, GroupName = "6. Display")]
		public int PsychOpacity { get; set; }

		[XmlIgnore]
		[Display(Name = "Text colour", Order = 12, GroupName = "6. Display")]
		public System.Windows.Media.Brush TextBrush { get; set; }

		[Browsable(false)]
		public string TextBrushSerialize
		{
			get { return Serialize.BrushToString(TextBrush); }
			set { TextBrush = Serialize.StringToBrush(value); }
		}

		[Display(Name = "Text font", Order = 13, GroupName = "6. Display")]
		public SimpleFont TextFont { get; set; }

		[Display(Name = "Show data warnings", Order = 14, GroupName = "6. Display")]
		public bool ShowWarnings { get; set; }

		[Display(Name = "Alert: pressure bubbles", Order = 1, GroupName = "7. Alerts")]
		public bool AlertBubbles { get; set; }

		[Display(Name = "Alert: big trades", Order = 2, GroupName = "7. Alerts")]
		public bool AlertBigTrades { get; set; }

		[Display(Name = "Alert: absorption dots", Order = 3, GroupName = "7. Alerts")]
		public bool AlertAbsorptionDots { get; set; }

		[Display(Name = "Alert: absorption line retest", Order = 4, GroupName = "7. Alerts")]
		public bool AlertAbsorptionLineTouch { get; set; }

		[Display(Name = "Alert: psychological level cross", Order = 5, GroupName = "7. Alerts")]
		public bool AlertPsychCross { get; set; }

		[Display(Name = "Alert sound", Description = "File in NinjaTrader's sounds folder or a full path.", Order = 6, GroupName = "7. Alerts")]
		public string AlertSound { get; set; }

		[Range(0, 3600)]
		[Display(Name = "Alert rearm seconds", Order = 7, GroupName = "7. Alerts")]
		public int AlertRearmSeconds { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private IDCator.IDCPressureToolkit[] cacheIDCPressureToolkit;
		public IDCator.IDCPressureToolkit IDCPressureToolkit()
		{
			return IDCPressureToolkit(Input);
		}

		public IDCator.IDCPressureToolkit IDCPressureToolkit(ISeries<double> input)
		{
			if (cacheIDCPressureToolkit != null)
				for (int idx = 0; idx < cacheIDCPressureToolkit.Length; idx++)
					if (cacheIDCPressureToolkit[idx] != null &&  cacheIDCPressureToolkit[idx].EqualsInput(input))
						return cacheIDCPressureToolkit[idx];
			return CacheIndicator<IDCator.IDCPressureToolkit>(new IDCator.IDCPressureToolkit(), input, ref cacheIDCPressureToolkit);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.IDCator.IDCPressureToolkit IDCPressureToolkit()
		{
			return indicator.IDCPressureToolkit(Input);
		}

		public Indicators.IDCator.IDCPressureToolkit IDCPressureToolkit(ISeries<double> input )
		{
			return indicator.IDCPressureToolkit(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.IDCator.IDCPressureToolkit IDCPressureToolkit()
		{
			return indicator.IDCPressureToolkit(Input);
		}

		public Indicators.IDCator.IDCPressureToolkit IDCPressureToolkit(ISeries<double> input )
		{
			return indicator.IDCPressureToolkit(input);
		}
	}
}

#endregion
