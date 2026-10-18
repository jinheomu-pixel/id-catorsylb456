//
// IDCator order-flow toolkit for NinjaTrader 8 - shared core.
//
// This file holds the pieces every IDCator indicator uses:
//   * IdcFootprintEngine - builds a per-bar bid/ask footprint either from an Order Flow+
//     Volumetric chart or directly from the trade stream (OnMarketData + Tick Replay),
//     and classifies every trade print as an aggressive buy or sell.
//   * IdcBarWindowStats  - rolling mean / standard deviation over the last N bars.
//   * IdcDxBrushCache / IdcDraw - small SharpDX helpers for custom chart rendering.
//
// Copy the whole IDCator folder into Documents\NinjaTrader 8\bin\Custom\Indicators\ and
// compile (F5 in the NinjaScript Editor). See README.md in the repository for details.
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
	/// <summary>Where footprint (bid/ask volume per price) data comes from.</summary>
	public enum IdcFootprintSource
	{
		/// <summary>Volumetric bars when the chart is Volumetric, otherwise the trade stream.</summary>
		Auto,
		/// <summary>Order Flow+ Volumetric bars only (needs an Order Flow+ license).</summary>
		Volumetric,
		/// <summary>Trade stream via OnMarketData (history needs Tick Replay enabled).</summary>
		TickData
	}

	/// <summary>How far an untouched (active) zone is drawn to the right.</summary>
	public enum IdcExtendMode
	{
		/// <summary>Up to the most recent bar.</summary>
		LastBar,
		/// <summary>Up to the right edge of the chart panel.</summary>
		ChartEdge
	}

	/// <summary>How a diagonal imbalance treats an opposite side with zero volume.</summary>
	public enum IdcZeroHandling
	{
		/// <summary>No imbalance when the opposite side traded nothing.</summary>
		Skip,
		/// <summary>Treat zero as 1 contract, so ratio = volume / 1.</summary>
		CountAsOne,
		/// <summary>Zero opposite volume counts as an imbalance at exactly the minimum ratio (matches the original's labels).</summary>
		CountAsMinRatio
	}

	/// <summary>Volume traded at one price level of one bar.</summary>
	public sealed class IdcCell
	{
		/// <summary>Aggressive buy volume (trades at the ask).</summary>
		public double Ask;
		/// <summary>Aggressive sell volume (trades at the bid).</summary>
		public double Bid;

		public double Total { get { return Ask + Bid; } }
		public double Delta { get { return Ask - Bid; } }
	}

	/// <summary>A single trade print, already classified by aggressor side.</summary>
	public struct IdcTrade
	{
		public int BarIndex;
		public long Tick;
		public double Price;
		public double Volume;
		/// <summary>+1 aggressive buy, -1 aggressive sell, 0 unknown.</summary>
		public int Side;
		public DateTime Time;
	}

	/// <summary>Footprint of one bar. Prices are stored as integer tick indices (price / TickSize).</summary>
	public sealed class IdcFootprintBar
	{
		private readonly Dictionary<long, IdcCell> cells = new Dictionary<long, IdcCell>();

		public IdcFootprintBar(int barIndex)
		{
			BarIndex	= barIndex;
			LowTick		= long.MaxValue;
			HighTick	= long.MinValue;
		}

		public int		BarIndex	{ get; private set; }
		public long		LowTick		{ get; private set; }
		public long		HighTick	{ get; private set; }
		public double	TotalAsk	{ get; private set; }
		public double	TotalBid	{ get; private set; }
		public double	TotalVolume	{ get { return TotalAsk + TotalBid; } }
		public double	Delta		{ get { return TotalAsk - TotalBid; } }
		public int		LevelCount	{ get { return cells.Count; } }
		public bool		IsEmpty		{ get { return cells.Count == 0; } }

		public void Add(long tick, double ask, double bid)
		{
			if (ask <= 0 && bid <= 0)
				return;

			IdcCell cell;
			if (!cells.TryGetValue(tick, out cell))
			{
				cell = new IdcCell();
				cells.Add(tick, cell);
				if (tick < LowTick)		LowTick = tick;
				if (tick > HighTick)	HighTick = tick;
			}

			if (ask > 0)
			{
				cell.Ask	+= ask;
				TotalAsk	+= ask;
			}
			if (bid > 0)
			{
				cell.Bid	+= bid;
				TotalBid	+= bid;
			}
		}

		public IdcCell Get(long tick)
		{
			IdcCell cell;
			return cells.TryGetValue(tick, out cell) ? cell : null;
		}

		public double Ask(long tick)	{ IdcCell c = Get(tick); return c == null ? 0 : c.Ask; }
		public double Bid(long tick)	{ IdcCell c = Get(tick); return c == null ? 0 : c.Bid; }
		public double Volume(long tick)	{ IdcCell c = Get(tick); return c == null ? 0 : c.Total; }

		/// <summary>Tick index of the level with the most volume (point of control).</summary>
		public long PocTick()
		{
			long	best	= long.MinValue;
			double	bestVol	= -1;
			for (long t = LowTick; t <= HighTick && !IsEmpty; t++)
			{
				IdcCell c = Get(t);
				if (c != null && c.Total > bestVol)
				{
					bestVol	= c.Total;
					best	= t;
				}
			}
			return best;
		}
	}

	/// <summary>
	/// Builds footprints and classifies trades. One instance per indicator.
	/// Call Initialize() in State.DataLoaded and forward every OnMarketData() event to ProcessMarketData().
	/// The owning indicator must run with Calculate.OnEachTick: NinjaTrader then calls OnBarUpdate for a
	/// new bar's opening tick BEFORE that tick's OnMarketData, so CurrentBar inside OnMarketData always
	/// names the bar the trade belongs to.
	/// </summary>
	public sealed class IdcFootprintEngine
	{
		private readonly Dictionary<int, IdcFootprintBar>	tickBars		= new Dictionary<int, IdcFootprintBar>();
		private readonly Dictionary<int, IdcFootprintBar>	volumetricCache	= new Dictionary<int, IdcFootprintBar>();
		private Bars										bars;
		private NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType volumetric;
		private int											ticksPerLevel	= 1;
		private bool										isCrypto;
		private double										lastAsk;
		private double										lastBid;
		private double										lastTradePrice;
		private int											lastTickDirection;
		private long										quoteChecks;
		private long										syntheticHits;
		private bool										realQuotesSeen;
		private int											lastPrunedBar	= -1;
		private int											firstStreamBar	= -1;
		private bool										firstStreamBarPartial;

		public double				TickSize			{ get; private set; }
		/// <summary>Price levels per footprint row in ticks (Volumetric "Ticks per level"; 1 for tick data).</summary>
		public int					LevelStep			{ get { return UsesVolumetric ? ticksPerLevel : 1; } }
		public IdcFootprintSource	RequestedSource		{ get; private set; }
		/// <summary>True when footprints are read from Order Flow+ Volumetric bars.</summary>
		public bool					UsesVolumetric		{ get; private set; }
		/// <summary>True when historical trades are replayed (Tick Replay enabled on the data series).</summary>
		public bool					HasTickReplay		{ get; private set; }
		/// <summary>True once NinjaTrader's synthetic historical bid/ask (Bid = Last, Ask = Bid + 1 tick) was detected; the up/down tick rule is used instead.</summary>
		public bool					SyntheticQuotes		{ get; private set; }
		/// <summary>Footprint bars older than this many bars behind the current bar are discarded (0 = keep all).</summary>
		public int					MaxBarsKept			{ get; set; }
		/// <summary>Trades strictly between bid and ask are classified with the up/down tick rule instead of being split 50/50.</summary>
		public bool					TickRuleForInside	{ get; set; }
		public long					TradesProcessed		{ get; private set; }

		public IdcFootprintEngine()
		{
			TickRuleForInside	= true;
			MaxBarsKept			= 0;
		}

		/// <summary>Call from State.DataLoaded.</summary>
		public void Initialize(Bars chartBars, double tickSize, bool cryptoInstrument, IdcFootprintSource source)
		{
			bars			= chartBars;
			TickSize		= tickSize > 0 ? tickSize : 0.01;
			isCrypto		= cryptoInstrument;
			RequestedSource	= source;
			HasTickReplay	= chartBars != null && chartBars.IsTickReplay;

			volumetric = chartBars == null ? null : chartBars.BarsSeries.BarsType as NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType;
			UsesVolumetric = volumetric != null && source != IdcFootprintSource.TickData;

			ticksPerLevel = 1;
			if (UsesVolumetric && chartBars.BarsPeriod != null && chartBars.BarsPeriod.BarsPeriodType == BarsPeriodType.Volumetric)
			{
				// On a Volumetric series BarsPeriod.Value carries "Ticks per level". Anything else is treated as 1.
				int value = chartBars.BarsPeriod.Value;
				if (value > 1 && value <= 1000)
					ticksPerLevel = value;
			}

			tickBars.Clear();
			volumetricCache.Clear();
			lastAsk = lastBid = lastTradePrice = 0;
			lastTickDirection = 0;
			quoteChecks = syntheticHits = 0;
			SyntheticQuotes = false;
			realQuotesSeen = false;
			TradesProcessed = 0;
			lastPrunedBar = -1;
			firstStreamBar = -1;
			firstStreamBarPartial = false;
		}

		/// <summary>True when the chosen source cannot deliver footprints (e.g. Volumetric requested on a non-volumetric chart).</summary>
		public bool IsMisconfigured
		{
			get { return RequestedSource == IdcFootprintSource.Volumetric && volumetric == null; }
		}

		/// <summary>Short human readable description of the active data source (for on-chart status text).</summary>
		public string SourceDescription
		{
			get
			{
				if (IsMisconfigured)
					return "Volumetric requested but chart is not Volumetric";
				if (UsesVolumetric)
					return HasTickReplay ? "Volumetric + Tick Replay" : "Volumetric";
				return HasTickReplay ? "Tick Replay" : "Realtime ticks only (enable Tick Replay for history)";
			}
		}

		public long ToTick(double price)	{ return (long)Math.Round(price / TickSize); }
		public double ToPrice(long tick)	{ return tick * TickSize; }

		/// <summary>
		/// Feed every OnMarketData event here. Returns true when the event was a trade; the classified
		/// trade is returned in 'trade'. Footprints are only built from trades when Volumetric bars are not used.
		/// </summary>
		/// <summary>Set in State.Realtime: realtime trades drive the feed clock (FeedNow).</summary>
		public bool TrackFeedClock { get; set; }

		private DateTime lastTradeStamp = DateTime.MinValue;
		private DateTime lastTradeSeenAt;

		/// <summary>
		/// The feed's clock: the last realtime trade's time stamp plus the platform time since it arrived, never later
		/// than the platform clock. A PC clock running ahead of the feed's stamps, or a data thread still working through
		/// queued trades, therefore never makes a bar look finished while its trades are still coming in.
		/// Before the first realtime trade it is the platform clock.
		/// </summary>
		public DateTime FeedNow()
		{
			DateTime now = IdcClock.Now;
			if (lastTradeStamp == DateTime.MinValue)
				return now;
			DateTime feed = now > lastTradeSeenAt ? lastTradeStamp + (now - lastTradeSeenAt) : lastTradeStamp;
			return feed < now ? feed : now;
		}

		public bool ProcessMarketData(MarketDataEventArgs e, int currentBar, out IdcTrade trade)
		{
			trade = new IdcTrade();
			if (e == null)
				return false;

			if (e.MarketDataType == MarketDataType.Ask)
			{
				if (e.Price > 0) lastAsk = e.Price;
				return false;
			}
			if (e.MarketDataType == MarketDataType.Bid)
			{
				if (e.Price > 0) lastBid = e.Price;
				return false;
			}
			if (e.MarketDataType != MarketDataType.Last || currentBar < 0 || e.Volume <= 0)
				return false;

			double price	= e.Price;
			double volume	= isCrypto ? Convert.ToDouble(NinjaTrader.Core.Globals.ToCryptocurrencyVolume(e.Volume)) : e.Volume;
			double ask		= e.Ask > 0 ? e.Ask : lastAsk;
			double bid		= e.Bid > 0 ? e.Bid : lastBid;

			// A print at or above a real ask is impossible with NinjaTrader's substitute quotes (Ask = Last + 1 tick),
			// so it proves the quotes are real. Until then a print matching the substitute pattern is ambiguous and is
			// classified with the tick rule, from the very first print (no 500-print warm-up, no permanent switch).
			if (ask > 0 && bid > 0 && ask > bid && price >= ask)
				realQuotesSeen = true;
			DetectSyntheticQuotes(price, ask, bid);
			bool ambiguous = !realQuotesSeen && bid > 0 && Math.Abs(bid - price) < TickSize * 0.01 && Math.Abs(ask - (bid + TickSize)) < TickSize * 0.01;

			int side = 0;
			if (ambiguous)
				side = TickRule(price);
			else if (ask > 0 && price >= ask)
				side = 1;
			else if (bid > 0 && price <= bid)
				side = -1;
			else if (TickRuleForInside)
				side = TickRule(price);

			if (price > lastTradePrice && lastTradePrice > 0)		lastTickDirection = 1;
			else if (price < lastTradePrice && lastTradePrice > 0)	lastTickDirection = -1;
			lastTradePrice = price;

			long tick = ToTick(price);
			trade.BarIndex	= currentBar;
			trade.Tick		= tick;
			trade.Price		= price;
			trade.Volume	= volume;
			trade.Side		= side;
			trade.Time		= e.Time;
			TradesProcessed++;
			if (TrackFeedClock)
			{
				// arrival first: a heartbeat that sees the new stamp also sees when it arrived
				lastTradeSeenAt	= IdcClock.Now;
				lastTradeStamp	= e.Time;
			}

			if (firstStreamBar < 0)
			{
				firstStreamBar = currentBar;
				// partial only when the bar already held volume before this first streamed trade (OnBarUpdate has already
				// added this trade to the bar); a stream that starts with a bar's opening trade observes the whole bar
				firstStreamBarPartial = bars == null || currentBar >= bars.Count || bars.GetVolume(currentBar) > e.Volume;
			}

			if (!UsesVolumetric)
			{
				IdcFootprintBar fp;
				if (!tickBars.TryGetValue(currentBar, out fp))
				{
					fp = new IdcFootprintBar(currentBar);
					tickBars.Add(currentBar, fp);
					Prune(currentBar);
				}

				if (side > 0)		fp.Add(tick, volume, 0);
				else if (side < 0)	fp.Add(tick, 0, volume);
				else				fp.Add(tick, volume * 0.5, volume * 0.5);
			}

			return true;
		}

		private int TickRule(double price)
		{
			if (lastTradePrice <= 0)
				return 0;
			if (price > lastTradePrice)
				return 1;
			if (price < lastTradePrice)
				return -1;
			return lastTickDirection;
		}

		private void DetectSyntheticQuotes(double price, double ask, double bid)
		{
			// NinjaTrader substitutes Bid = Last and Ask = Bid + 1 tick when the provider's historical ticks carry
			// no quotes. Real data shows that pattern only on sell prints (roughly half of all trades), synthetic
			// data shows it on every print. Decide once enough trades were seen.
			if (realQuotesSeen)
			{
				SyntheticQuotes = false;	// only drives the on-chart warning now
				return;
			}
			if (SyntheticQuotes || quoteChecks >= 2000)
				return;

			quoteChecks++;
			if (bid > 0 && Math.Abs(bid - price) < TickSize * 0.01 && Math.Abs(ask - (bid + TickSize)) < TickSize * 0.01)
				syntheticHits++;

			if (quoteChecks >= 500 && syntheticHits >= quoteChecks * 0.97)
				SyntheticQuotes = true;
		}

		private void Prune(int currentBar)
		{
			if (MaxBarsKept <= 0 || currentBar - lastPrunedBar < 256)
				return;

			lastPrunedBar = currentBar;
			int limit = currentBar - MaxBarsKept;
			if (limit <= 0)
				return;

			List<int> stale = null;
			foreach (int key in tickBars.Keys)
				if (key < limit)
				{
					if (stale == null) stale = new List<int>();
					stale.Add(key);
				}
			if (stale != null)
				foreach (int key in stale)
					tickBars.Remove(key);

			stale = null;
			foreach (int key in volumetricCache.Keys)
				if (key < limit)
				{
					if (stale == null) stale = new List<int>();
					stale.Add(key);
				}
			if (stale != null)
				foreach (int key in stale)
					volumetricCache.Remove(key);
		}

		/// <summary>
		/// Footprint of the bar at absolute index barIndex, or null when no data exists.
		/// For Volumetric charts pass isClosed = true for completed bars so the result can be cached.
		/// </summary>
		public IdcFootprintBar GetBar(int barIndex, bool isClosed)
		{
			if (barIndex < 0)
				return null;

			if (UsesVolumetric)
			{
				IdcFootprintBar cached;
				if (isClosed && volumetricCache.TryGetValue(barIndex, out cached))
					return cached;

				IdcFootprintBar built = BuildFromVolumetric(barIndex);
				if (built != null && isClosed)
				{
					volumetricCache[barIndex] = built;
					Prune(barIndex);
				}
				return built;
			}

			// without Tick Replay the bar that was forming when the trade stream started only holds part of its trades
			if (!HasTickReplay && firstStreamBarPartial && barIndex == firstStreamBar)
				return null;
			IdcFootprintBar fp;
			return tickBars.TryGetValue(barIndex, out fp) ? fp : null;
		}

		private IdcFootprintBar BuildFromVolumetric(int barIndex)
		{
			if (volumetric == null || bars == null || barIndex >= bars.Count)
				return null;

			IdcFootprintBar fp = new IdcFootprintBar(barIndex);
			try
			{
				var data	= volumetric.Volumes[barIndex];
				long lo		= ToTick(bars.GetLow(barIndex));
				long hi		= ToTick(bars.GetHigh(barIndex));
				// the limit is on footprint ROWS: BTC at 0.01 tick with 100 ticks per level has 250 rows over $250
				if ((hi - lo) / ticksPerLevel > 20000)
					return null;

				// Volumetric rows sit on a fixed price grid (multiples of ticks per level): start on the row that holds the
				// low, otherwise the row holding the high is skipped whenever the low is not on the grid
				long first = lo - (((lo % ticksPerLevel) + ticksPerLevel) % ticksPerLevel);
				for (long t = first; t <= hi; t += ticksPerLevel)
				{
					double price	= ToPrice(t);
					double ask		= data.GetAskVolumeForPrice(price);
					double bid		= data.GetBidVolumeForPrice(price);
					if (isCrypto)
					{
						ask = Convert.ToDouble(NinjaTrader.Core.Globals.ToCryptocurrencyVolume((long)ask));
						bid = Convert.ToDouble(NinjaTrader.Core.Globals.ToCryptocurrencyVolume((long)bid));
					}
					if (ask > 0 || bid > 0)
						fp.Add(t, ask, bid);
				}
			}
			catch (Exception)
			{
				return null;
			}
			return fp;
		}

		/// <summary>Total volume per tick for a bar when no footprint exists: spreads the bar's volume evenly over its range.</summary>
		public static void SpreadBarVolume(Dictionary<long, double> target, long lowTick, long highTick, double volume)
		{
			if (volume <= 0 || highTick < lowTick)
				return;
			long levels = highTick - lowTick + 1;
			double each = volume / levels;
			for (long t = lowTick; t <= highTick; t++)
			{
				double v;
				target.TryGetValue(t, out v);
				target[t] = v + each;
			}
		}
	}

	/// <summary>Bar bookkeeping shared by the indicators.</summary>
	public static class IdcBars
	{
		/// <summary>
		/// True when a time-based bar has certainly finished: its close timestamp lies 30 s before 'now' (the feed clock,
		/// IdcFootprintEngine.FeedNow). Used when realtime starts while the market is closed (evening, weekend) and by the
		/// heartbeat, so the last bar is not left waiting for a tick that never comes.
		/// </summary>
		public static bool IsBarComplete(Bars bars, int index, DateTime now)
		{
			if (bars == null || index < 0 || index >= bars.Count || bars.BarsPeriod == null)
				return false;
			BarsPeriodType type = bars.BarsPeriod.BarsPeriodType == BarsPeriodType.Volumetric ? bars.BarsPeriod.BaseBarsPeriodType : bars.BarsPeriod.BarsPeriodType;
			// intraday time bars only: Day / Week / Month bars carry the trading date (00:00), which lies in the past during the session
			if (type != BarsPeriodType.Second && type != BarsPeriodType.Minute)
				return false;
			// margin: a late print must not land in a bar that was already closed
			return bars.GetTime(index).AddSeconds(30) <= now;
		}

		/// <summary>
		/// True when the bar that just closed is only seen to close when the next session opens, long after it ended (daily
		/// break, weekend): its bar-close alerts would be stale. Sessions that follow each other directly (24 h forex / crypto
		/// templates) close their boundary bar on time, so those still alert.
		/// </summary>
		public static bool IsStaleSessionClose(Bars bars, int closedBar)
		{
			if (bars == null || closedBar < 0 || closedBar >= bars.Count || !bars.IsFirstBarOfSession)
				return false;
			return (IdcClock.Now - bars.GetTime(closedBar)).TotalMinutes > 5;
		}

		/// <summary>
		/// Absolute index of the most recent COMPLETED bar. With Calculate.OnEachTick the bar at CurrentBar is still
		/// forming, except on historical bars without Tick Replay, where NinjaTrader calls OnBarUpdate once per
		/// finished bar (the very last loaded bar may still be forming, so it is left for realtime).
		/// </summary>
		public static int LastClosedBar(State state, bool tickReplay, int currentBar, int count)
		{
			if (state == State.Historical && !tickReplay)
				return currentBar < count - 1 ? currentBar : currentBar - 1;
			return currentBar - 1;
		}
	}

	/// <summary>Rolling mean / standard deviation of samples collected over the last N bars.</summary>
	public sealed class IdcBarWindowStats
	{
		private sealed class Bucket
		{
			public int		Bar;
			public double	N;
			public double	Sum;
			public double	SumSq;
		}

		private readonly Queue<Bucket>	buckets = new Queue<Bucket>();
		private Bucket					current;
		private double					n;
		private double					sum;
		private double					sumSq;

		public IdcBarWindowStats(int windowBars)
		{
			WindowBars = Math.Max(1, windowBars);
		}

		public int		WindowBars	{ get; private set; }
		public double	Count		{ get { return n; } }
		public double	Mean		{ get { return n > 0 ? sum / n : 0; } }

		public double StdDev
		{
			get
			{
				if (n < 2)
					return 0;
				double mean		= sum / n;
				double variance	= sumSq / n - mean * mean;
				return variance > 0 ? Math.Sqrt(variance) : 0;
			}
		}

		/// <summary>Mean + k * standard deviation.</summary>
		public double Threshold(double k) { return Mean + k * StdDev; }

		public void Add(int bar, double value)
		{
			if (current == null || current.Bar != bar)
			{
				current = new Bucket { Bar = bar };
				buckets.Enqueue(current);
				Evict(bar);
			}
			current.N		+= 1;
			current.Sum		+= value;
			current.SumSq	+= value * value;
			n				+= 1;
			sum				+= value;
			sumSq			+= value * value;
		}

		private void Evict(int bar)
		{
			while (buckets.Count > 0 && buckets.Peek().Bar <= bar - WindowBars)
			{
				Bucket old = buckets.Dequeue();
				n		-= old.N;
				sum		-= old.Sum;
				sumSq	-= old.SumSq;
			}
			if (n <= 0)
			{
				n = sum = sumSq = 0;
			}
		}

		public void Clear()
		{
			buckets.Clear();
			current = null;
			n = sum = sumSq = 0;
		}
	}

	/// <summary>Caches SharpDX solid brushes per colour for one RenderTarget. Dispose on OnRenderTargetChanged and State.Terminated.</summary>
	public sealed class IdcDxBrushCache : IDisposable
	{
		private readonly Dictionary<uint, SharpDX.Direct2D1.SolidColorBrush>	cache = new Dictionary<uint, SharpDX.Direct2D1.SolidColorBrush>();
		private SharpDX.Direct2D1.RenderTarget										target;

		/// <summary>Brush with the colour of a WPF brush, multiplied by the extra opacity (0..1).</summary>
		public SharpDX.Direct2D1.SolidColorBrush Get(SharpDX.Direct2D1.RenderTarget renderTarget, System.Windows.Media.Brush wpfBrush, double opacity)
		{
			Color c = IdcDraw.ColorOf(wpfBrush);
			double brushOpacity = wpfBrush == null ? 1.0 : wpfBrush.Opacity;
			double alpha = c.A / 255.0 * brushOpacity * Math.Max(0, Math.Min(1, opacity));
			return Get(renderTarget, Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));
		}

		public SharpDX.Direct2D1.SolidColorBrush Get(SharpDX.Direct2D1.RenderTarget renderTarget, Color color)
		{
			if (!ReferenceEquals(renderTarget, target))
			{
				Dispose();
				target = renderTarget;
			}

			uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
			SharpDX.Direct2D1.SolidColorBrush brush;
			if (!cache.TryGetValue(key, out brush))
			{
				brush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget,
					new SharpDX.Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
				cache.Add(key, brush);
			}
			return brush;
		}

		public void Dispose()
		{
			foreach (SharpDX.Direct2D1.SolidColorBrush brush in cache.Values)
				if (brush != null)
					brush.Dispose();
			cache.Clear();
			target = null;
		}
	}

	public enum IdcTextAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

	/// <summary>Alert sound path resolution that never throws (a bad path must not disable an indicator).</summary>
	public static class IdcSound
	{
		public static string Resolve(string file)
		{
			if (string.IsNullOrWhiteSpace(file))
				return string.Empty;
			file = file.Trim().Trim('"').Trim();	// Explorer's "Copy as path" adds quotes
			try
			{
				if (System.IO.Path.IsPathRooted(file))
					return file;
				return System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", file);
			}
			catch (ArgumentException)
			{
				return string.Empty;	// alert without sound
			}
		}
	}

	/// <summary>
	/// Wall-clock heartbeat for realtime work that must not wait for the next market event: showing the last throttled
	/// update when the market goes quiet, and closing the last bar at a session end or weekend.
	/// The callback runs on a timer thread; indicators hand the work to their data thread with TriggerCustomEvent.
	/// </summary>
	public sealed class IdcHeartbeat : IDisposable
	{
		/// <summary>Test hook: no timer thread is started, the test calls Tick() itself.</summary>
		public static bool Manual;

		private System.Threading.Timer	timer;
		private Action					callback;
		private int						busy;

		public IdcHeartbeat(int periodMs, Action callback)
		{
			this.callback = callback;
			if (!Manual)
				timer = new System.Threading.Timer(OnTimer, null, periodMs, periodMs);
		}

		private void OnTimer(object state)
		{
			Tick();
		}

		public void Tick()
		{
			// a slow tick is never overlapped by the next one
			if (System.Threading.Interlocked.CompareExchange(ref busy, 1, 0) != 0)
				return;
			try
			{
				Action cb = callback;
				if (cb != null)
					cb();
			}
			catch (Exception)
			{
				// a timer thread must never take the platform down; the next tick simply tries again
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref busy, 0);
			}
		}

		public void Dispose()
		{
			callback = null;
			System.Threading.Timer t = timer;
			timer = null;
			if (t != null)
				t.Dispose();
		}
	}

	/// <summary>
	/// Market clock: replay time on the Playback connection (Core.Globals.Now stays on the PC clock there), otherwise
	/// Core.Globals.Now. Same idiom as NinjaTrader's own @BarTimer.
	/// </summary>
	public static class IdcClock
	{
		public static DateTime Now
		{
			get
			{
				NinjaTrader.Cbi.Connection playback = NinjaTrader.Cbi.Connection.PlaybackConnection;
				return playback != null ? playback.Now : NinjaTrader.Core.Globals.Now;
			}
		}
	}

	/// <summary>Monotonic wall clock in milliseconds, independent of the Playback speed. Tests may replace Source.</summary>
	public static class IdcWallClock
	{
		public static Func<long> Source;

		public static long Ms
		{
			get
			{
				Func<long> source = Source;
				if (source != null)
					return source();
				return (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1000.0 / System.Diagnostics.Stopwatch.Frequency));
			}
		}
	}

	/// <summary>An alert collected while a lock is held and raised after it is released.</summary>
	public sealed class IdcPendingAlert
	{
		public string						Id;
		public Priority						Priority;
		public string						Message;
		public string						Sound;
		public int							RearmSeconds;
		public System.Windows.Media.Brush	BackBrush;
		public System.Windows.Media.Brush	ForeBrush;
	}

	/// <summary>Static rendering and formatting helpers.</summary>
	public static class IdcDraw
	{
		public static Color ColorOf(System.Windows.Media.Brush brush)
		{
			SolidColorBrush solid = brush as SolidColorBrush;
			return solid != null ? solid.Color : Colors.Gray;
		}

		/// <summary>Blend a colour towards another (t = 0 keeps a, t = 1 gives b).</summary>
		public static Color Blend(Color a, Color b, double t)
		{
			t = Math.Max(0, Math.Min(1, t));
			return Color.FromArgb(
				(byte)Math.Round(a.A + (b.A - a.A) * t),
				(byte)Math.Round(a.R + (b.R - a.R) * t),
				(byte)Math.Round(a.G + (b.G - a.G) * t),
				(byte)Math.Round(a.B + (b.B - a.B) * t));
		}

		public static Color WithAlpha(Color c, double opacity)
		{
			opacity = Math.Max(0, Math.Min(1, opacity));
			return Color.FromArgb((byte)Math.Round(c.A * opacity), c.R, c.G, c.B);
		}

		/// <summary>Draws text anchored at (x, y). Returns the text size.</summary>
		public static SharpDX.Size2F Text(SharpDX.Direct2D1.RenderTarget rt, SharpDX.DirectWrite.TextFormat format, string text,
			float x, float y, SharpDX.Direct2D1.Brush brush, IdcTextAnchor anchor)
		{
			if (rt == null || format == null || string.IsNullOrEmpty(text) || brush == null)
				return new SharpDX.Size2F(0, 0);

			using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, text, format, 2000f, 500f))
			{
				float w = layout.Metrics.Width;
				float h = layout.Metrics.Height;
				float dx = 0, dy = 0;

				switch (anchor)
				{
					case IdcTextAnchor.TopCenter:		dx = -w / 2;					break;
					case IdcTextAnchor.TopRight:		dx = -w;						break;
					case IdcTextAnchor.MiddleLeft:		dy = -h / 2;					break;
					case IdcTextAnchor.Center:			dx = -w / 2;	dy = -h / 2;	break;
					case IdcTextAnchor.MiddleRight:		dx = -w;		dy = -h / 2;	break;
					case IdcTextAnchor.BottomLeft:		dy = -h;						break;
					case IdcTextAnchor.BottomCenter:	dx = -w / 2;	dy = -h;		break;
					case IdcTextAnchor.BottomRight:		dx = -w;		dy = -h;		break;
				}

				rt.DrawTextLayout(new SharpDX.Vector2(x + dx, y + dy), layout, brush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
				return new SharpDX.Size2F(w, h);
			}
		}

		/// <summary>Draws text anchored at (x, y) only when it fits in maxWidth x maxHeight. One TextLayout per call.</summary>
		public static bool TextIfFits(SharpDX.Direct2D1.RenderTarget rt, SharpDX.DirectWrite.TextFormat format, string text,
			float x, float y, SharpDX.Direct2D1.Brush brush, IdcTextAnchor anchor, float maxWidth, float maxHeight)
		{
			if (rt == null || format == null || string.IsNullOrEmpty(text) || brush == null)
				return false;

			using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, text, format, 2000f, 500f))
			{
				float w = layout.Metrics.Width;
				float h = layout.Metrics.Height;
				if (w > maxWidth || h > maxHeight)
					return false;

				float dx = 0, dy = 0;
				switch (anchor)
				{
					case IdcTextAnchor.TopCenter:		dx = -w / 2;					break;
					case IdcTextAnchor.TopRight:		dx = -w;						break;
					case IdcTextAnchor.MiddleLeft:		dy = -h / 2;					break;
					case IdcTextAnchor.Center:			dx = -w / 2;	dy = -h / 2;	break;
					case IdcTextAnchor.MiddleRight:		dx = -w;		dy = -h / 2;	break;
					case IdcTextAnchor.BottomLeft:		dy = -h;						break;
					case IdcTextAnchor.BottomCenter:	dx = -w / 2;	dy = -h;		break;
					case IdcTextAnchor.BottomRight:		dx = -w;		dy = -h;		break;
				}
				rt.DrawTextLayout(new SharpDX.Vector2(x + dx, y + dy), layout, brush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
				return true;
			}
		}

		/// <summary>Measures text without drawing it.</summary>
		public static SharpDX.Size2F Measure(SharpDX.DirectWrite.TextFormat format, string text)
		{
			if (format == null || string.IsNullOrEmpty(text))
				return new SharpDX.Size2F(0, 0);
			using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, text, format, 2000f, 500f))
				return new SharpDX.Size2F(layout.Metrics.Width, layout.Metrics.Height);
		}

		/// <summary>Compact volume text: 384, 1.2k, 3.4M.</summary>
		public static string Volume(double v)
		{
			return Volume(v, false);
		}

		/// <summary>Compact volume text with an upper-case thousands suffix when asked (Heavy Absorption circles: 9.1K).</summary>
		public static string Volume(double v, bool upperK)
		{
			string k = upperK ? "K" : "k";
			double a = Math.Abs(v);
			System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
			if (a >= 1000000)
				return (v / 1000000.0).ToString(a >= 10000000 ? "0" : "0.#", ci) + "M";
			if (a >= 10000)
				return (v / 1000.0).ToString("0", ci) + k;
			if (a >= 1000)
				return (v / 1000.0).ToString("0.#", ci) + k;
			if (a >= 10 || Math.Abs(v - Math.Round(v)) < 1e-9)
				return Math.Round(v).ToString("0", ci);
			return v.ToString("0.##", ci);
		}

		/// <summary>Imbalance ratio text: 5x, 6.7x, 15x.</summary>
		public static string Ratio(double r)
		{
			if (double.IsInfinity(r) || r >= 999)
				return "999x";
			return r.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "x";
		}
	}
}
