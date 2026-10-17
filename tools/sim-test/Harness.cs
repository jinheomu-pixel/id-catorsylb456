// Driver that runs a NinjaScript indicator the way NinjaTrader 8 does:
//   SetDefaults -> (properties) -> Configure -> DataLoaded -> Historical -> bars/ticks -> Transition -> Realtime -> Terminated
// Data modes
//   * Tick Replay / realtime: for every trade, open a new bar first when needed (IsFirstTickOfBar = true) or update
//     the forming bar, call OnBarUpdate, THEN OnMarketData(Last with Ask/Bid). Realtime also sends separate
//     Ask/Bid events before the trade when the quote changed.
//   * Historical without Tick Replay: all bars are loaded first, then OnBarUpdate runs once per bar; no
//     OnMarketData. Footprints come from VolumetricBarsType.Volumes when the chart is volumetric.
// Protected NinjaScript methods are invoked through reflection (delegates bound to the overrides).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.BarsTypes;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Gui.Chart;

namespace SimTest
{
	/// <summary>One trade print as the data feed would deliver it.</summary>
	public sealed class Trade
	{
		public DateTime	Time;
		public double	Price;
		public long		Volume;
		/// <summary>True aggressor (+1 buy at ask, -1 sell at bid); used for Volumetric data and for checks.</summary>
		public int		Side;
		/// <summary>Best ask / bid at trade time as stamped on the Last event (0 = not stamped).</summary>
		public double	Ask;
		public double	Bid;
		public override string ToString() { return string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} {1} x{2} side={3} a={4} b={5}", Time, Price, Volume, Side, Ask, Bid); }
	}

	public sealed class SimOptions
	{
		public string			InstrumentName	= "ES 12-26";
		public double			TickSize		= 0.25;
		public InstrumentType	InstrumentType	= InstrumentType.Future;
		public BarsPeriodType	PeriodType		= BarsPeriodType.Second;
		public int				PeriodValue		= 15;
		public bool				TickReplay		= true;
		public bool				Volumetric		= false;
		public int				TicksPerLevel	= 1;
		public TimeSpan			SessionStart	= TimeSpan.FromHours(17);	// CME ETH: 17:00 -> 16:00, trading day = next day
		public TimeSpan			SessionLength	= TimeSpan.FromHours(23);
		public int				Capacity		= 30000;
		/// <summary>Realtime Last events carry Ask/Bid (false: only separate Ask/Bid events).</summary>
		public bool				RealtimeLastHasQuotes	= true;
		public bool				RealtimeQuoteEvents		= true;

		public SimOptions Clone() { return (SimOptions)MemberwiseClone(); }
	}

	/// <summary>Runs one indicator instance through NinjaTrader's lifecycle.</summary>
	public sealed class Sim<T> where T : Indicator, new()
	{
		public readonly T				I;
		public readonly Bars			Bars;
		public readonly SimOptions		O;
		public readonly VolumetricBarsType Vol;
		private readonly Action			onStateChange;
		private readonly Action			onBarUpdate;
		private readonly Action<MarketDataEventArgs> onMarketData;
		private readonly MethodInfo		onRender;
		private int						ticksInBar;
		private DateTime				currentTradingDay = DateTime.MinValue;
		private double					lastAsk, lastBid;
		public long						Events;
		public long						BarUpdates;

		/// <summary>Called after every trade was fully processed (OnBarUpdate + OnMarketData).</summary>
		public Action<Trade>			AfterTrade;

		public Sim(SimOptions options, Action<T> configure)
		{
			O = options;
			I = new T();
			onStateChange	= Bind<Action>("OnStateChange");
			onBarUpdate		= Bind<Action>("OnBarUpdate");
			onMarketData	= Bind<Action<MarketDataEventArgs>>("OnMarketData");
			onRender		= FindMethod("OnRender");

			SetState(State.SetDefaults);
			if (configure != null)
				configure(I);

			MasterInstrument mi = new MasterInstrument { TickSize = O.TickSize, Name = O.InstrumentName.Split(' ')[0], InstrumentType = O.InstrumentType, PointValue = 50 };
			Instrument inst		= new Instrument { MasterInstrument = mi, FullName = O.InstrumentName };
			Bars = new Bars(O.Capacity)
			{
				Instrument		= inst,
				IsTickReplay	= O.TickReplay,
				TradingHours	= new TradingHours { TimeZoneInfo = TimeZoneInfo.Utc, SessionStart = O.SessionStart, SessionLength = O.SessionLength },
				ProcessingBar	= -1,
			};
			bool intraday = O.PeriodType != BarsPeriodType.Day && O.PeriodType != BarsPeriodType.Week && O.PeriodType != BarsPeriodType.Month && O.PeriodType != BarsPeriodType.Year;
			if (O.Volumetric)
			{
				Vol = new VolumetricBarsType { IsIntraday = intraday, Volumes = new VolumetricData[O.Capacity] };
				Bars.BarsSeries = new BarsSeries { BarsType = Vol };
				Bars.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Volumetric, Value = O.TicksPerLevel, BaseBarsPeriodType = O.PeriodType, BaseBarsPeriodValue = O.PeriodValue };
			}
			else
			{
				Bars.BarsSeries = new BarsSeries { BarsType = new SimBarsType { IsIntraday = intraday } };
				Bars.BarsPeriod = new BarsPeriod { BarsPeriodType = O.PeriodType, Value = O.PeriodValue };
			}

			I.SimAttach(Bars);
			I.CurrentBar = -1;
			SetState(State.Configure);
			SetState(State.DataLoaded);
		}

		private TDelegate Bind<TDelegate>(string name) where TDelegate : class
		{
			MethodInfo m = FindMethod(name);
			return (TDelegate)(object)Delegate.CreateDelegate(typeof(TDelegate), I, m);
		}

		private static MethodInfo FindMethod(string name)
		{
			for (Type t = typeof(T); t != null; t = t.BaseType)
			{
				MethodInfo m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
				if (m != null)
					return m;
			}
			throw new MissingMethodException(typeof(T).Name, name);
		}

		public void SetState(State s)
		{
			I.State = s;
			onStateChange();
		}

		public State State { get { return I.State; } }
		public int CurrentBar { get { return I.CurrentBar; } }

		// ---------------- bar building ----------------
		private bool Intraday { get { return Bars.BarsType.IsIntraday; } }

		private DateTime TradingDay(DateTime t)
		{
			DateTime b, e, d;
			Bars.TradingHours.GetSession(t, true, out b, out e, out d);
			return d;
		}

		/// <summary>Bar time stamp for a trade, and whether it opens a new bar.</summary>
		private bool OpensNewBar(Trade t, out DateTime barTime)
		{
			int n = Bars.Count;
			switch (O.PeriodType)
			{
				case BarsPeriodType.Second:
				case BarsPeriodType.Minute:
				{
					long len = (O.PeriodType == BarsPeriodType.Second ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute) * O.PeriodValue;
					// half-open [start, end): a trade exactly on a boundary opens the next bar; bars are stamped with their END time
					barTime = new DateTime((t.Time.Ticks / len) * len + len, t.Time.Kind);
					return n == 0 || Bars.GetTime(n - 1) != barTime;
				}
				case BarsPeriodType.Tick:
					barTime = t.Time;
					return n == 0 || ticksInBar >= O.PeriodValue;
				case BarsPeriodType.Day:
					barTime = TradingDay(t.Time);
					return n == 0 || Bars.GetTime(n - 1) != barTime;
				default:
					throw new NotSupportedException(O.PeriodType.ToString());
			}
		}

		/// <summary>Applies a trade to the bars (and volumetric data). Returns true when a new bar was opened.</summary>
		private bool ApplyTrade(Trade t)
		{
			DateTime barTime;
			bool isNew = OpensNewBar(t, out barTime);
			if (isNew)
			{
				bool firstOfSession = false;
				if (Intraday)
				{
					DateTime day = TradingDay(barTime);
					firstOfSession = Bars.Count == 0 || day != currentTradingDay;
					currentTradingDay = day;
				}
				Bars.SimAppend(barTime, t.Price, t.Volume, firstOfSession);
				ticksInBar = 1;
				if (Vol != null)
					Vol.Volumes[Bars.Count - 1] = new VolumetricData(O.TickSize, O.TicksPerLevel);
			}
			else
			{
				Bars.SimUpdate(t.Price, t.Volume, O.PeriodType == BarsPeriodType.Tick ? t.Time : barTime);
				ticksInBar++;
			}
			if (Vol != null)
				Vol.Volumes[Bars.Count - 1].SimAdd(t.Price, t.Volume, t.Side);
			return isNew;
		}

		// ---------------- historical without Tick Replay ----------------
		/// <summary>Loads all bars first, then calls OnBarUpdate once per bar (NinjaTrader historical, no Tick Replay).</summary>
		public void RunHistoricalBars(IEnumerable<Trade> trades)
		{
			if (O.TickReplay)
				throw new InvalidOperationException("RunHistoricalBars needs TickReplay = false");
			foreach (Trade t in trades)
				ApplyTrade(t);
			SetState(State.Historical);
			int total = Bars.Count;
			I.SimCount = total;
			for (int i = 0; i < total; i++)
			{
				Bars.ProcessingBar	= i;
				I.CurrentBar		= i;
				I.IsFirstTickOfBar	= true;
				NinjaTrader.Core.Globals.SimNow = Bars.GetTime(i);
				onBarUpdate();
				BarUpdates++;
			}
			I.SimCount = -1;
		}

		// ---------------- Tick Replay / realtime ----------------
		public void StartHistorical() { SetState(State.Historical); }

		public void GoRealtime()
		{
			SetState(State.Transition);
			SetState(State.Realtime);
		}

		public void Terminate() { SetState(State.Terminated); }

		/// <summary>Feeds trades tick by tick in the current state (Historical = Tick Replay, Realtime = live).</summary>
		public void Feed(IEnumerable<Trade> trades)
		{
			foreach (Trade t in trades)
				Feed(t);
		}

		public void Feed(Trade t)
		{
			if (I.State == State.Historical && !O.TickReplay)
				throw new InvalidOperationException("Tick feeding during Historical requires Tick Replay");
			NinjaTrader.Core.Globals.SimNow = t.Time;
			bool realtime = I.State == State.Realtime;

			if (realtime && O.RealtimeQuoteEvents)
			{
				// level-1 quote updates arrive as separate events in realtime (never in Tick Replay)
				if (t.Ask > 0 && t.Ask != lastAsk)
				{
					lastAsk = t.Ask;
					SendMarketData(new MarketDataEventArgs { MarketDataType = MarketDataType.Ask, Price = t.Ask, Volume = 10, Time = t.Time, Instrument = Bars.Instrument });
				}
				if (t.Bid > 0 && t.Bid != lastBid)
				{
					lastBid = t.Bid;
					SendMarketData(new MarketDataEventArgs { MarketDataType = MarketDataType.Bid, Price = t.Bid, Volume = 10, Time = t.Time, Instrument = Bars.Instrument });
				}
			}

			bool isNew = ApplyTrade(t);
			I.CurrentBar		= Bars.Count - 1;
			Bars.ProcessingBar	= I.CurrentBar;
			I.IsFirstTickOfBar	= isNew;
			onBarUpdate();
			BarUpdates++;

			bool stamp = !realtime || O.RealtimeLastHasQuotes;
			SendMarketData(new MarketDataEventArgs
			{
				MarketDataType	= MarketDataType.Last,
				Price			= t.Price,
				Volume			= t.Volume,
				Ask				= stamp ? t.Ask : 0,
				Bid				= stamp ? t.Bid : 0,
				Time			= t.Time,
				Instrument		= Bars.Instrument
			});

			if (AfterTrade != null)
				AfterTrade(t);
		}

		private void SendMarketData(MarketDataEventArgs e)
		{
			Events++;
			onMarketData(e);
		}

		// ---------------- rendering ----------------
		public FakeChart Chart;

		/// <summary>Calls OnRender with a fake chart showing bars [from, to] and prices [minPrice, maxPrice].</summary>
		public FakeChart Render(int from, int to, double minPrice, double maxPrice)
		{
			if (Chart == null)
				Chart = new FakeChart(Bars);
			Chart.Prepare(I, from, to, minPrice, maxPrice);
			onRender.Invoke(I, new object[] { Chart.Control, Chart.Scale });
			return Chart;
		}

		/// <summary>Render the last n bars with an automatic price range.</summary>
		public FakeChart RenderLast(int n)
		{
			int to = Bars.Count - 1;
			int from = Math.Max(0, to - n + 1);
			double lo = double.MaxValue, hi = double.MinValue;
			for (int i = from; i <= to; i++) { lo = Math.Min(lo, Bars.GetLow(i)); hi = Math.Max(hi, Bars.GetHigh(i)); }
			double pad = Math.Max(O.TickSize * 10, (hi - lo) * 0.1);
			return Render(from, to, lo - pad, hi + pad);
		}

		public void RenderTargetChanged()
		{
			if (Chart != null)
				Chart.NewRenderTarget(I);
			I.OnRenderTargetChanged();
		}

		// ---------------- helpers ----------------
		public long Tick(double price) { return (long)Math.Round(price / O.TickSize); }
		public double Px(long tick) { return Math.Round(tick * O.TickSize, 10); }
	}

	/// <summary>A linear fake chart around a recording RenderTarget.</summary>
	public sealed class FakeChart
	{
		public readonly ChartControl					Control;
		public readonly ChartScale						Scale;
		public readonly ChartPanel						Panel;
		public readonly ChartBars						ChartBars;
		public SharpDX.Direct2D1.RenderTarget			RT;

		public FakeChart(Bars bars)
		{
			Panel		= new ChartPanel { X = 0, Y = 0, W = 1600, H = 800 };
			ChartBars	= new ChartBars { Bars = bars };
			Control		= new ChartControl { PanelX = 0, BarDistance = 20, Properties = new ChartControlProperties { BarDistance = 20 } };
			Control.BarsArray = new[] { ChartBars };
			Scale		= new ChartScale { Height = 800, PanelY = 0 };
			RT			= new SharpDX.Direct2D1.RenderTarget();
		}

		public void NewRenderTarget(Indicator ind)
		{
			RT = new SharpDX.Direct2D1.RenderTarget();
			ind.RenderTarget = RT;
		}

		public void Prepare(Indicator ind, int from, int to, double minPrice, double maxPrice)
		{
			ChartBars.FromIndex	= from;
			ChartBars.ToIndex	= to;
			// fit the visible bars plus a right margin of 5 bars into the panel
			Control.BarDistance	= Math.Max(4, Math.Min(40, Panel.W / (double)Math.Max(1, to - from + 6)));
			Scale.MinValue		= minPrice;
			Scale.MaxValue		= maxPrice;
			RT.Ops.Clear();
			ind.ChartControl	= Control;
			ind.ChartBars		= ChartBars;
			ind.ChartPanel		= Panel;
			ind.RenderTarget	= RT;
			ind.IsInHitTest		= false;
		}

		public List<SharpDX.Direct2D1.DrawOp> Ops { get { return RT.Ops; } }
		public IEnumerable<string> Texts { get { return RT.Ops.Where(o => o.Kind == "Text").Select(o => o.Text); } }
		public int BarX(int bar) { return Control.GetXByBarIndex(ChartBars, bar); }
		public int Y(double price) { return Scale.GetYByValue(price); }
	}

	/// <summary>Builds trade tapes.</summary>
	public sealed class Tape
	{
		public readonly List<Trade>	Trades = new List<Trade>();
		public readonly double		TickSize;
		public DateTime				Now;
		/// <summary>When true every print is stamped Bid = Last, Ask = Bid + 1 tick (NinjaTrader's substitute for providers without historical quotes).</summary>
		public bool					SyntheticQuotes;
		public int					StepMs = 50;

		public Tape(double tickSize, DateTime start) { TickSize = tickSize; Now = start; }

		public double P(double price) { return Math.Round(Math.Round(price / TickSize) * TickSize, 10); }

		public Tape At(DateTime t) { Now = t; return this; }
		public Tape Wait(double seconds) { Now = Now.AddSeconds(seconds); return this; }

		public Trade Print(double price, long volume, int side, int stepMs = -1)
		{
			price = P(price);
			Trade t = new Trade { Time = Now, Price = price, Volume = volume, Side = side };
			if (SyntheticQuotes)
			{
				t.Bid = price;
				t.Ask = P(price + TickSize);
			}
			else if (side > 0)
			{
				t.Ask = price;
				t.Bid = P(price - TickSize);
			}
			else if (side < 0)
			{
				t.Bid = price;
				t.Ask = P(price + TickSize);
			}
			else
			{
				t.Bid = P(price - TickSize);
				t.Ask = P(price + TickSize);
			}
			Trades.Add(t);
			Now = Now.AddMilliseconds(stepMs >= 0 ? stepMs : StepMs);
			return t;
		}

		public Trade Buy(double price, long volume, int stepMs = -1) { return Print(price, volume, 1, stepMs); }
		public Trade Sell(double price, long volume, int stepMs = -1) { return Print(price, volume, -1, stepMs); }

		/// <summary>
		/// Emits one bar's trades inside [start, start + length): the path goes open -> first extreme -> second extreme -> close
		/// (down first for an up bar). Each level's ask/bid volume is split into prints of at most maxPrint and emitted at
		/// the level's first visit; one print of the close level is kept for the very end so the bar closes at 'close'.
		/// </summary>
		public void Bar(DateTime start, TimeSpan length, double open, double close, IList<Level> levels, int maxPrint = 5)
		{
			open = P(open); close = P(close);
			double lo = levels.Min(l => P(l.Price)), hi = levels.Max(l => P(l.Price));
			lo = Math.Min(lo, Math.Min(open, close)); hi = Math.Max(hi, Math.Max(open, close));
			Dictionary<long, Level> byTick = new Dictionary<long, Level>();
			foreach (Level l in levels)
				byTick[(long)Math.Round(l.Price / TickSize)] = l;

			List<long> path = new List<long>();
			long o = (long)Math.Round(open / TickSize), c = (long)Math.Round(close / TickSize);
			long lt = (long)Math.Round(lo / TickSize), ht = (long)Math.Round(hi / TickSize);
			bool up = close >= open;
			if (up)
			{
				for (long t = o; t >= lt; t--) path.Add(t);
				for (long t = lt + 1; t <= ht; t++) path.Add(t);
				for (long t = ht - 1; t >= c; t--) path.Add(t);
			}
			else
			{
				for (long t = o; t <= ht; t++) path.Add(t);
				for (long t = ht - 1; t >= lt; t--) path.Add(t);
				for (long t = lt + 1; t <= c; t++) path.Add(t);
			}

			// build print list in path order
			List<Trade> prints = new List<Trade>();
			HashSet<long> visited = new HashSet<long>();
			Trade reservedClose = null;
			foreach (long t in path)
			{
				if (!visited.Add(t)) { continue; }
				Level l;
				if (!byTick.TryGetValue(t, out l)) continue;
				List<Trade> lp = new List<Trade>();
				long a = l.Ask, b = l.Bid;
				while (a > 0 || b > 0)
				{
					if (a > 0) { long v = Math.Min(maxPrint, a); a -= v; lp.Add(MakeTrade(t, v, 1)); }
					if (b > 0) { long v = Math.Min(maxPrint, b); b -= v; lp.Add(MakeTrade(t, v, -1)); }
				}
				if (t == c && lp.Count > 0)
				{
					reservedClose = lp[lp.Count - 1];
					lp.RemoveAt(lp.Count - 1);
				}
				prints.AddRange(lp);
			}
			if (reservedClose != null)
				prints.Add(reservedClose);
			// make sure the first print is at the open (needed when the open level has no volume listed)
			if (prints.Count == 0 || (long)Math.Round(prints[0].Price / TickSize) != o)
				prints.Insert(0, MakeTrade(o, 1, up ? -1 : 1));
			if ((long)Math.Round(prints[prints.Count - 1].Price / TickSize) != c)
				prints.Add(MakeTrade(c, 1, up ? 1 : -1));

			double span = length.TotalMilliseconds - 2;
			for (int i = 0; i < prints.Count; i++)
			{
				prints[i].Time = start.AddMilliseconds(1 + span * i / Math.Max(1, prints.Count));
				Trades.Add(prints[i]);
			}
			Now = start + length;
		}

		private Trade MakeTrade(long tick, long vol, int side)
		{
			double price = Math.Round(tick * TickSize, 10);
			price = P(price);
			Trade t = new Trade { Price = price, Volume = vol, Side = side };
			if (SyntheticQuotes) { t.Bid = price; t.Ask = P(price + TickSize); }
			else if (side > 0) { t.Ask = price; t.Bid = P(price - TickSize); }
			else { t.Bid = price; t.Ask = P(price + TickSize); }
			return t;
		}
	}

	public sealed class Level
	{
		public double	Price;
		public long		Ask;
		public long		Bid;
		public Level(double price, long ask, long bid) { Price = price; Ask = ask; Bid = bid; }
	}

	/// <summary>Reflection access to private indicator state.</summary>
	public static class R
	{
		private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		public static object Get(object o, string name)
		{
			if (o == null) throw new ArgumentNullException("o", "reading " + name + " from null");
			for (Type t = o.GetType(); t != null; t = t.BaseType)
			{
				FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
				if (f != null) return f.GetValue(o);
				PropertyInfo p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
				if (p != null) return p.GetValue(o, null);
			}
			throw new MissingMemberException(o.GetType().Name, name);
		}

		public static TV Get<TV>(object o, string name) { return (TV)Get(o, name); }

		public static List<object> List(object o, string name)
		{
			object v = Get(o, name);
			List<object> result = new List<object>();
			if (v == null) return result;
			foreach (object x in (IEnumerable)v) result.Add(x);
			return result;
		}

		public static object Call(object o, string name, params object[] args)
		{
			for (Type t = o.GetType(); t != null; t = t.BaseType)
			{
				MethodInfo m = t.GetMethod(name, All | BindingFlags.DeclaredOnly);
				if (m != null)
				{
					try { return m.Invoke(o, args); }
					catch (TargetInvocationException e) { throw e.InnerException; }
				}
			}
			throw new MissingMethodException(o.GetType().Name, name);
		}

		/// <summary>Values of a Dictionary&lt;int, X&gt; field.</summary>
		public static Dictionary<int, object> IntDict(object o, string name)
		{
			Dictionary<int, object> result = new Dictionary<int, object>();
			object v = Get(o, name);
			foreach (object kv in (IEnumerable)v)
			{
				int key = (int)kv.GetType().GetProperty("Key").GetValue(kv, null);
				result[key] = kv.GetType().GetProperty("Value").GetValue(kv, null);
			}
			return result;
		}
	}

	/// <summary>Scenario context: checks and notes.</summary>
	public sealed class Ctx
	{
		public readonly List<string>	Failures	= new List<string>();
		public readonly List<string>	Notes		= new List<string>();
		public bool						Quick;

		public bool Check(bool condition, string what)
		{
			if (!condition) Failures.Add(what);
			return condition;
		}

		public void Eq<TV>(TV expected, TV actual, string what)
		{
			if (!EqualityComparer<TV>.Default.Equals(expected, actual))
				Failures.Add(string.Format(CultureInfo.InvariantCulture, "{0}: expected {1}, got {2}", what, expected, actual));
		}

		public void Near(double expected, double actual, double tol, string what)
		{
			if (double.IsNaN(actual) || Math.Abs(expected - actual) > tol)
				Failures.Add(string.Format(CultureInfo.InvariantCulture, "{0}: expected {1} (+/-{2}), got {3}", what, expected, tol, actual));
		}

		public void Note(string format, params object[] args) { Notes.Add(string.Format(CultureInfo.InvariantCulture, format, args)); }
	}

	public sealed class Scenario
	{
		public string		Name;
		public string		Description;
		public Action<Ctx>	Body;
		public Scenario(string name, string description, Action<Ctx> body) { Name = name; Description = description; Body = body; }
	}

	public static class F
	{
		public static string S(double v) { return v.ToString("0.########", CultureInfo.InvariantCulture); }
	}
}
