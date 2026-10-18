// RUNNABLE fake of the NinjaTrader 8 API surface used by the IDCator indicators, for the behaviour
// simulation harness in tools/sim-test. Started as a copy of tools/compile-check/NinjaTraderApiStubs.cs
// (signatures from the official NinjaScript docs) and extended so that the members actually work:
// price/time series backed by arrays, Bars.Get* accessors, IsFirstBarOfSession, a SessionIterator that
// returns a trading day, VolumetricBarsType with real per-price data, and Alert/Log/Print recording.
//
// It also contains minimal fakes of System.Windows(.Media/.Input) and SharpDX, so the indicators run on
// Linux / net8.0. The SharpDX RenderTarget records every draw call, which lets the harness call OnRender
// and inspect what would be painted (labels, circles, rectangles) and catch disposed / foreign brushes.
//
// Nothing here may be copied into NinjaTrader - the real platform provides all of these types.
#pragma warning disable 0067, 0649, 0169, 1591
using System;
using System.Collections.Generic;
using System.Globalization;

// --------------------------------------------------------------------------------------------------
// WPF fakes
// --------------------------------------------------------------------------------------------------
namespace System.Windows
{
	public struct Point
	{
		public double X;
		public double Y;
		public Point(double x, double y) { X = x; Y = y; }
	}

	public class PresentationSource { }
}

namespace System.Windows.Input
{
	internal static class FakeInputNamespaceMarker { }
}

namespace System.Windows.Media
{
	public struct Color : IEquatable<Color>
	{
		public byte A { get; set; }
		public byte R { get; set; }
		public byte G { get; set; }
		public byte B { get; set; }

		public static Color FromArgb(byte a, byte r, byte g, byte b) { return new Color { A = a, R = r, G = g, B = b }; }
		public static Color FromRgb(byte r, byte g, byte b) { return FromArgb(255, r, g, b); }
		public bool Equals(Color o) { return A == o.A && R == o.R && G == o.G && B == o.B; }
		public override bool Equals(object obj) { return obj is Color && Equals((Color)obj); }
		public override int GetHashCode() { return (A << 24) | (R << 16) | (G << 8) | B; }
		public static bool operator ==(Color a, Color b) { return a.Equals(b); }
		public static bool operator !=(Color a, Color b) { return !a.Equals(b); }
		public override string ToString() { return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", A, R, G, B); }
	}

	public static class Colors
	{
		public static Color Gray		{ get { return Color.FromRgb(0x80, 0x80, 0x80); } }
		public static Color White		{ get { return Color.FromRgb(0xFF, 0xFF, 0xFF); } }
		public static Color Black		{ get { return Color.FromRgb(0x00, 0x00, 0x00); } }
		public static Color Transparent	{ get { return Color.FromArgb(0, 0xFF, 0xFF, 0xFF); } }
	}

	public abstract class Brush
	{
		private double opacity = 1.0;
		public bool IsFrozen { get; private set; }
		public double Opacity
		{
			get { return opacity; }
			set { if (IsFrozen) throw new InvalidOperationException("Cannot set a property on object '" + GetType().Name + "' because it is in a read-only state (frozen)."); opacity = value; }
		}
		public void Freeze() { IsFrozen = true; }
	}

	public sealed class SolidColorBrush : Brush
	{
		private Color color;
		public SolidColorBrush() { }
		public SolidColorBrush(Color c) { color = c; }
		public Color Color
		{
			get { return color; }
			set { if (IsFrozen) throw new InvalidOperationException("Cannot set Color on a frozen SolidColorBrush."); color = value; }
		}
		public override string ToString() { return color.ToString(); }
	}

	public static class Brushes
	{
		private static SolidColorBrush Make(byte r, byte g, byte b) { SolidColorBrush s = new SolidColorBrush(Color.FromRgb(r, g, b)); s.Freeze(); return s; }
		public static SolidColorBrush Black			{ get { return Make(0x00, 0x00, 0x00); } }
		public static SolidColorBrush White			{ get { return Make(0xFF, 0xFF, 0xFF); } }
		public static SolidColorBrush Gray			{ get { return Make(0x80, 0x80, 0x80); } }
		public static SolidColorBrush DimGray		{ get { return Make(0x69, 0x69, 0x69); } }
		public static SolidColorBrush SlateGray		{ get { return Make(0x70, 0x80, 0x90); } }
		public static SolidColorBrush Red			{ get { return Make(0xFF, 0x00, 0x00); } }
		public static SolidColorBrush DarkRed		{ get { return Make(0x8B, 0x00, 0x00); } }
		public static SolidColorBrush LimeGreen		{ get { return Make(0x32, 0xCD, 0x32); } }
		public static SolidColorBrush Green			{ get { return Make(0x00, 0x80, 0x00); } }
		public static SolidColorBrush DarkGreen		{ get { return Make(0x00, 0x64, 0x00); } }
		public static SolidColorBrush Orange		{ get { return Make(0xFF, 0xA5, 0x00); } }
		public static SolidColorBrush Goldenrod		{ get { return Make(0xDA, 0xA5, 0x20); } }
		public static SolidColorBrush DarkGoldenrod	{ get { return Make(0xB8, 0x86, 0x0B); } }
		public static SolidColorBrush CornflowerBlue{ get { return Make(0x64, 0x95, 0xED); } }
		public static SolidColorBrush Transparent	{ get { SolidColorBrush s = new SolidColorBrush(Color.FromArgb(0, 0xFF, 0xFF, 0xFF)); s.Freeze(); return s; } }
	}
}

// --------------------------------------------------------------------------------------------------
// SharpDX fakes (recording render target)
// --------------------------------------------------------------------------------------------------
namespace SharpDX
{
	public struct Vector2
	{
		public float X;
		public float Y;
		public Vector2(float x, float y) { X = x; Y = y; }
	}

	public struct Size2F
	{
		public float Width;
		public float Height;
		public Size2F(float w, float h) { Width = w; Height = h; }
	}

	public struct RectangleF
	{
		public float X;
		public float Y;
		public float Width;
		public float Height;
		public RectangleF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; }
		public float Left	{ get { return X; } }
		public float Top	{ get { return Y; } }
		public float Right	{ get { return X + Width; } }
		public float Bottom	{ get { return Y + Height; } }
	}

	public struct Color4
	{
		public float Red;
		public float Green;
		public float Blue;
		public float Alpha;
		public Color4(float r, float g, float b, float a) { Red = r; Green = g; Blue = b; Alpha = a; }
	}
}

namespace SharpDX.Direct2D1
{
	public enum AntialiasMode { PerPrimitive = 0, Aliased = 1 }
	public enum DrawTextOptions { None = 0, NoSnap = 1, Clip = 2 }

	public struct Ellipse
	{
		public SharpDX.Vector2	Point;
		public float			RadiusX;
		public float			RadiusY;
		public Ellipse(SharpDX.Vector2 center, float radiusX, float radiusY) { Point = center; RadiusX = radiusX; RadiusY = radiusY; }
	}

	public abstract class Brush : IDisposable
	{
		public bool IsDisposed { get; private set; }
		public RenderTarget Owner { get; protected set; }
		public void Dispose() { IsDisposed = true; }
	}

	public class SolidColorBrush : Brush
	{
		public SharpDX.Color4 Color { get; set; }
		public SolidColorBrush(RenderTarget renderTarget, SharpDX.Color4 color)
		{
			if (renderTarget == null) throw new ArgumentNullException("renderTarget");
			Owner = renderTarget;
			Color = color;
			renderTarget.BrushesCreated++;
		}
	}

	/// <summary>One recorded draw call.</summary>
	public sealed class DrawOp
	{
		public string			Kind;		// FillRectangle, DrawRectangle, DrawLine, FillEllipse, DrawEllipse, Text
		public float			X, Y, W, H;	// rect / ellipse centre+radii / line endpoints (X,Y)-(W,H) / text origin + size
		public float			Stroke;
		public SharpDX.Color4	Color;
		public string			Text;
		public override string ToString()
		{
			return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:0.#},{2:0.#},{3:0.#},{4:0.#}) a={5:0.00} {6}", Kind, X, Y, W, H, Color.Alpha, Text);
		}
	}

	/// <summary>Records draw calls instead of drawing; validates brushes like Direct2D would (disposed / other target).</summary>
	public class RenderTarget
	{
		public readonly List<DrawOp>	Ops			= new List<DrawOp>();
		public readonly List<string>	Errors		= new List<string>();
		public int						BrushesCreated;
		public AntialiasMode			AntialiasMode { get; set; }

		private SharpDX.Color4 Check(Brush brush, string op, params float[] values)
		{
			if (brush == null)
				throw new ArgumentNullException("brush", op + " called with a null brush");
			if (brush.IsDisposed)
				throw new ObjectDisposedException("SolidColorBrush", op + " used a disposed SharpDX brush");
			if (!ReferenceEquals(brush.Owner, this))
				throw new InvalidOperationException(op + " used a brush created for another RenderTarget (D2DERR_WRONG_RESOURCE_DOMAIN)");
			foreach (float v in values)
				if (float.IsNaN(v) || float.IsInfinity(v))
					Errors.Add(op + " got a NaN/Infinity coordinate");
			SolidColorBrush s = brush as SolidColorBrush;
			return s != null ? s.Color : new SharpDX.Color4(0, 0, 0, 1);
		}

		public void FillRectangle(SharpDX.RectangleF r, Brush brush)
		{
			SharpDX.Color4 c = Check(brush, "FillRectangle", r.X, r.Y, r.Width, r.Height);
			Ops.Add(new DrawOp { Kind = "FillRectangle", X = r.X, Y = r.Y, W = r.Width, H = r.Height, Color = c });
		}

		public void DrawRectangle(SharpDX.RectangleF r, Brush brush, float strokeWidth)
		{
			SharpDX.Color4 c = Check(brush, "DrawRectangle", r.X, r.Y, r.Width, r.Height, strokeWidth);
			Ops.Add(new DrawOp { Kind = "DrawRectangle", X = r.X, Y = r.Y, W = r.Width, H = r.Height, Stroke = strokeWidth, Color = c });
		}

		public void DrawRectangle(SharpDX.RectangleF r, Brush brush) { DrawRectangle(r, brush, 1f); }

		public void DrawLine(SharpDX.Vector2 p0, SharpDX.Vector2 p1, Brush brush, float strokeWidth)
		{
			SharpDX.Color4 c = Check(brush, "DrawLine", p0.X, p0.Y, p1.X, p1.Y, strokeWidth);
			Ops.Add(new DrawOp { Kind = "DrawLine", X = p0.X, Y = p0.Y, W = p1.X, H = p1.Y, Stroke = strokeWidth, Color = c });
		}

		public void DrawLine(SharpDX.Vector2 p0, SharpDX.Vector2 p1, Brush brush) { DrawLine(p0, p1, brush, 1f); }

		public void FillEllipse(Ellipse e, Brush brush)
		{
			SharpDX.Color4 c = Check(brush, "FillEllipse", e.Point.X, e.Point.Y, e.RadiusX, e.RadiusY);
			Ops.Add(new DrawOp { Kind = "FillEllipse", X = e.Point.X, Y = e.Point.Y, W = e.RadiusX, H = e.RadiusY, Color = c });
		}

		public void DrawEllipse(Ellipse e, Brush brush, float strokeWidth)
		{
			SharpDX.Color4 c = Check(brush, "DrawEllipse", e.Point.X, e.Point.Y, e.RadiusX, e.RadiusY, strokeWidth);
			Ops.Add(new DrawOp { Kind = "DrawEllipse", X = e.Point.X, Y = e.Point.Y, W = e.RadiusX, H = e.RadiusY, Stroke = strokeWidth, Color = c });
		}

		public void DrawTextLayout(SharpDX.Vector2 origin, SharpDX.DirectWrite.TextLayout layout, Brush brush, DrawTextOptions options)
		{
			if (layout == null) throw new ArgumentNullException("layout");
			if (layout.IsDisposed) throw new ObjectDisposedException("TextLayout");
			if (layout.Format == null || layout.Format.IsDisposed) throw new ObjectDisposedException("TextFormat", "DrawTextLayout with a disposed TextFormat");
			SharpDX.Color4 c = Check(brush, "DrawTextLayout", origin.X, origin.Y);
			Ops.Add(new DrawOp { Kind = "Text", X = origin.X, Y = origin.Y, W = layout.Metrics.Width, H = layout.Metrics.Height, Color = c, Text = layout.Text });
		}

		public void DrawTextLayout(SharpDX.Vector2 origin, SharpDX.DirectWrite.TextLayout layout, Brush brush) { DrawTextLayout(origin, layout, brush, DrawTextOptions.None); }
	}
}

namespace SharpDX.DirectWrite
{
	public class Factory { }

	public class TextFormat : IDisposable
	{
		public float	FontSize	{ get; set; }
		public bool		Bold		{ get; set; }
		public bool		IsDisposed	{ get; private set; }
		public void Dispose() { IsDisposed = true; }
	}

	public struct TextMetrics
	{
		public float Width;
		public float Height;
	}

	public class TextLayout : IDisposable
	{
		public TextLayout(Factory factory, string text, TextFormat format, float maxWidth, float maxHeight)
		{
			if (format == null) throw new ArgumentNullException("format");
			if (format.IsDisposed) throw new ObjectDisposedException("TextFormat", "TextLayout created from a disposed TextFormat");
			Text	= text ?? string.Empty;
			Format	= format;
			float size = format.FontSize > 0 ? format.FontSize : 12f;
			Metrics = new TextMetrics { Width = Text.Length * size * 0.55f, Height = size * 1.2f };
		}
		public string		Text		{ get; private set; }
		public TextFormat	Format		{ get; private set; }
		public TextMetrics	Metrics		{ get; private set; }
		public bool			IsDisposed	{ get; private set; }
		public void Dispose() { IsDisposed = true; }
	}
}

// --------------------------------------------------------------------------------------------------
// NinjaTrader fakes
// --------------------------------------------------------------------------------------------------
namespace NinjaTrader.Core
{
	public static class Globals
	{
		private static readonly SharpDX.DirectWrite.Factory factory = new SharpDX.DirectWrite.Factory();
		public static SharpDX.DirectWrite.Factory	DirectWriteFactory	{ get { return factory; } }
		public static string						InstallDir			{ get { return "C:\\Program Files\\NinjaTrader 8\\"; } }
		public static string						UserDataDir			{ get { return "C:\\Users\\sim\\Documents\\NinjaTrader 8\\"; } }
		/// <summary>Simulation clock (set by the harness to the time of the event being processed).</summary>
		public static DateTime						SimNow				= new DateTime(2026, 1, 1);
		public static DateTime						Now					{ get { return SimNow; } }
		public static DateTime						MinDate				{ get { return new DateTime(1800, 1, 1); } }
		public static double ToCryptocurrencyVolume(long volume) { return volume; }
		public static NinjaTrader.Core.GeneralOptions GeneralOptions { get { return new GeneralOptions(); } }
	}

	public class GeneralOptions
	{
		public TimeZoneInfo TimeZoneInfo { get { return TimeZoneInfo.Utc; } }
	}
}

namespace NinjaTrader.Core.FloatingPoint
{
	public static class DoubleExtensions
	{
		public static int ApproxCompare(this double a, double b)
		{
			if (Math.Abs(a - b) < 1e-10) return 0;
			return a < b ? -1 : 1;
		}
	}
}

namespace NinjaTrader.Cbi
{
	public enum InstrumentType { Cfd, CryptoCurrency, Forex, Future, Index, Option, Stock }

	public class MasterInstrument
	{
		public double			TickSize		{ get; set; }
		public double			PointValue		{ get; set; }
		public string			Name			{ get; set; }
		public InstrumentType	InstrumentType	{ get; set; }
		public double RoundToTickSize(double price) { return TickSize > 0 ? Math.Round(price / TickSize) * TickSize : price; }
		public double RoundDownToTickSize(double price) { return TickSize > 0 ? Math.Floor(price / TickSize + 1e-9) * TickSize : price; }
		public int Compare(double price1, double price2)
		{
			double d = price1 - price2;
			if (Math.Abs(d) < TickSize * 1e-6) return 0;
			return d < 0 ? -1 : 1;
		}
		public string FormatPrice(double price, bool round = true)
		{
			int decimals = 0;
			double t = TickSize;
			while (decimals < 8 && Math.Abs(t - Math.Round(t)) > 1e-9) { t *= 10; decimals++; }
			return (round ? RoundToTickSize(price) : price).ToString("F" + decimals, CultureInfo.InvariantCulture);
		}
	}

	public class Instrument
	{
		public MasterInstrument	MasterInstrument	{ get; set; }
		public string			FullName			{ get; set; }
	}
}

namespace NinjaTrader.Data
{
	public enum MarketDataType { Ask, Bid, DailyHigh, DailyLow, DailyVolume, Last, LastClose, Opening, OpenInterest, Settlement }

	public enum BarsPeriodType { Tick = 0, Volume = 1, Range = 2, Second = 3, Minute = 4, Day = 5, Week = 6, Month = 7, Year = 8, Volumetric = 14 }

	public enum VolumetricDeltaType { BidAsk, UpDownTick }

	public class MarketDataEventArgs : EventArgs
	{
		public double							Ask				{ get; set; }
		public double							Bid				{ get; set; }
		public NinjaTrader.Cbi.Instrument		Instrument		{ get; set; }
		public bool								IsReset			{ get; set; }
		public MarketDataType					MarketDataType	{ get; set; }
		public double							Price			{ get; set; }
		public DateTime							Time			{ get; set; }
		public long								Volume			{ get; set; }
		public override string ToString() { return string.Format(CultureInfo.InvariantCulture, "{0} {1} x{2} @{3:HH:mm:ss.fff} a={4} b={5}", MarketDataType, Price, Volume, Time, Ask, Bid); }
	}

	public class BarsPeriod
	{
		public BarsPeriodType	BarsPeriodType		{ get; set; }
		public int				Value				{ get; set; }
		public int				Value2				{ get; set; }
		public BarsPeriodType	BaseBarsPeriodType	{ get; set; }
		public int				BaseBarsPeriodValue	{ get; set; }
	}

	public abstract class BarsType
	{
		public bool IsIntraday { get; set; }
		public bool IsTimeBased { get; set; }
	}

	/// <summary>Plain (non volumetric) bars type used by the harness.</summary>
	public class SimBarsType : BarsType { }

	public class BarsSeries
	{
		public BarsType BarsType { get; set; }
	}

	public class TradingHours
	{
		public TimeZoneInfo TimeZoneInfo	{ get; set; }
		/// <summary>Harness session model: each session starts at SessionStart (local) and lasts SessionLength.</summary>
		public TimeSpan		SessionStart	= TimeSpan.FromHours(17);
		public TimeSpan		SessionLength	= TimeSpan.FromHours(23);

		/// <summary>Session that contains (or, inside a break, follows) the given bar time.</summary>
		public void GetSession(DateTime time, bool includesEndTimeStamp, out DateTime begin, out DateTime end, out DateTime tradingDay)
		{
			DateTime t = includesEndTimeStamp ? time.AddTicks(-1) : time;
			DateTime shifted = t - SessionStart;
			begin	= shifted.Date + SessionStart;
			end		= begin + SessionLength;
			if (t >= end)
			{
				begin	= begin.AddDays(1);
				end		= begin + SessionLength;
			}
			tradingDay = SessionStart.TotalHours >= 12 ? begin.Date.AddDays(1) : begin.Date;
			// rev2: optional intra-day break -> two sessions with the same trading day (e.g. CBOT grains)
			if (BreakLength > TimeSpan.Zero)
			{
				DateTime breakBegin = begin + BreakOffset, breakEnd = breakBegin + BreakLength;
				if (t < breakBegin) end = breakBegin;
				else begin = breakEnd;
			}
		}
		public TimeSpan		BreakOffset		= TimeSpan.Zero;
		public TimeSpan		BreakLength		= TimeSpan.Zero;
	}

	/// <summary>Bars backed by arrays. The harness appends/updates bars; indicators only read.</summary>
	public class Bars
	{
		private readonly object		gate = new object();
		private double[]			open, high, low, close;
		private long[]				volume;
		private DateTime[]			time;
		private bool[]				firstOfSession;
		private int					count;

		public Bars(int capacity)
		{
			open = new double[capacity]; high = new double[capacity]; low = new double[capacity]; close = new double[capacity];
			volume = new long[capacity]; time = new DateTime[capacity]; firstOfSession = new bool[capacity];
		}

		/// <summary>Index of the bar NinjaTrader is processing (drives IsFirstBarOfSession). Set by the harness.</summary>
		public int							ProcessingBar			{ get; set; }
		public int							Count					{ get { return count; } }
		public int							Capacity				{ get { return open.Length; } }
		public bool							IsTickReplay			{ get; set; }
		public bool							IsFirstBarOfSession
		{
			get
			{
				if (BarsType != null && !BarsType.IsIntraday) return false;	// docs: always false on non-intraday periods
				return ProcessingBar >= 0 && ProcessingBar < count && firstOfSession[ProcessingBar];
			}
		}
		public bool							IsResetOnNewTradingDay	{ get; set; }
		public BarsPeriod					BarsPeriod				{ get; set; }
		public BarsSeries					BarsSeries				{ get; set; }
		public BarsType						BarsType				{ get { return BarsSeries != null ? BarsSeries.BarsType : null; } }
		public NinjaTrader.Cbi.Instrument	Instrument				{ get; set; }
		public TradingHours					TradingHours			{ get; set; }
		public long							TickCount				{ get; set; }

		private void Check(int index, string member)
		{
			if (index < 0 || index >= count)
				throw new ArgumentOutOfRangeException("index", string.Format("Bars.{0}({1}) out of range (Count = {2})", member, index, count));
		}

		public double	GetOpen(int index)		{ Check(index, "GetOpen");	return open[index]; }
		public double	GetHigh(int index)		{ Check(index, "GetHigh");	return high[index]; }
		public double	GetLow(int index)		{ Check(index, "GetLow");	return low[index]; }
		public double	GetClose(int index)		{ Check(index, "GetClose");	return close[index]; }
		public long		GetVolume(int index)	{ Check(index, "GetVolume");	return volume[index]; }
		public DateTime	GetTime(int index)		{ Check(index, "GetTime");	return time[index]; }
		public bool		IsFirstBarOfSessionByIndex(int index)
		{
			if (BarsType != null && !BarsType.IsIntraday) return false;
			Check(index, "IsFirstBarOfSessionByIndex");
			return firstOfSession[index];
		}
		public int		GetBar(DateTime t)
		{
			for (int i = 0; i < count; i++)
				if (time[i] >= t) return i;
			return count - 1;
		}
		public DateTime	GetSessionEndTime(int index)
		{
			DateTime b, e, d;
			TradingHours.GetSession(GetTime(index), true, out b, out e, out d);
			return e;
		}

		// ---- harness side ----
		public void SimAppend(DateTime barTime, double price, long vol, bool isFirstOfSession)
		{
			lock (gate)
			{
				if (count >= open.Length)
					throw new InvalidOperationException("Bars capacity exceeded; raise SimOptions.Capacity");
				open[count] = high[count] = low[count] = close[count] = price;
				volume[count] = vol;
				time[count] = barTime;
				firstOfSession[count] = isFirstOfSession;
				count++;
			}
		}

		public void SimUpdate(double price, long vol, DateTime barTime)
		{
			int i = count - 1;
			if (price > high[i]) high[i] = price;
			if (price < low[i]) low[i] = price;
			close[i] = price;
			volume[i] += vol;
			time[i] = barTime;
		}
	}

	public class SessionIterator
	{
		private readonly Bars bars;
		public static int GetNextSessionCalls;

		public SessionIterator(Bars bars)
		{
			if (bars == null) throw new ArgumentNullException("bars");
			this.bars = bars;
		}
		public DateTime ActualSessionBegin			{ get; set; }
		public DateTime ActualSessionEnd			{ get; set; }
		public DateTime ActualTradingDayExchange	{ get; set; }
		public DateTime ActualTradingDayEndLocal	{ get; set; }

		public bool GetNextSession(DateTime timeLocal, bool includesEndTimeStamp)
		{
			GetNextSessionCalls++;
			DateTime b, e, d;
			bars.TradingHours.GetSession(timeLocal, includesEndTimeStamp, out b, out e, out d);
			ActualSessionBegin			= b;
			ActualSessionEnd			= e;
			ActualTradingDayExchange	= d;
			// end of the whole trading day (all its sessions)
			DateTime b2, e2, d2;
			DateTime probe = e;
			ActualTradingDayEndLocal = e;
			for (int i = 0; i < 8; i++)
			{
				bars.TradingHours.GetSession(probe, false, out b2, out e2, out d2);
				if (d2 != d) break;
				ActualTradingDayEndLocal = e2;
				probe = e2;
			}
			return true;
		}

		public DateTime GetTradingDay(DateTime timeLocal)
		{
			DateTime b, e, d;
			bars.TradingHours.GetSession(timeLocal, true, out b, out e, out d);
			return d;
		}

		public bool IsInSession(DateTime timeLocal, bool includesEndTimeStamp, bool isIntraDay)
		{
			GetNextSession(timeLocal, includesEndTimeStamp);
			return timeLocal >= ActualSessionBegin && timeLocal <= ActualSessionEnd;
		}

		public bool IsNewSession(DateTime time, bool includesEndTimeStamp) { return false; }
	}
}

namespace NinjaTrader.Gui
{
	public static class Serialize
	{
		public static string BrushToString(System.Windows.Media.Brush brush)
		{
			System.Windows.Media.SolidColorBrush s = brush as System.Windows.Media.SolidColorBrush;
			return s == null ? "" : s.Color.ToString();
		}
		public static System.Windows.Media.Brush StringToBrush(string value)
		{
			if (string.IsNullOrEmpty(value) || value[0] != '#' || value.Length != 9) return null;
			uint argb = uint.Parse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			System.Windows.Media.SolidColorBrush s = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
				(byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
			s.Freeze();
			return s;
		}
	}

	public static class DxExtensions
	{
		public static SharpDX.Direct2D1.Brush ToDxBrush(this System.Windows.Media.Brush brush, SharpDX.Direct2D1.RenderTarget renderTarget)
		{
			System.Windows.Media.SolidColorBrush s = brush as System.Windows.Media.SolidColorBrush;
			System.Windows.Media.Color c = s != null ? s.Color : System.Windows.Media.Colors.Gray;
			return new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f * (float)brush.Opacity));
		}
		public static SharpDX.Vector2 ToVector2(this System.Windows.Point point) { return new SharpDX.Vector2((float)point.X, (float)point.Y); }
	}

	public class Stroke
	{
		public System.Windows.Media.Brush	Brush		{ get; set; }
		public float						Width		{ get; set; }
		public DashStyleHelper				DashStyleHelper	{ get; set; }
		public Stroke() { }
		public Stroke(System.Windows.Media.Brush brush, DashStyleHelper dashStyleHelper, float width) { Brush = brush; DashStyleHelper = dashStyleHelper; Width = width; }
	}

	public enum DashStyleHelper { Dash, DashDot, DashDotDot, Dot, Solid }
}

namespace NinjaTrader.Gui.Tools
{
	public class SimpleFont
	{
		public SimpleFont() { Family = "Arial"; Size = 12; }
		public SimpleFont(string familyName, int size) { Family = familyName; Size = size; }
		public string	Family	{ get; set; }
		public bool		Bold	{ get; set; }
		public bool		Italic	{ get; set; }
		public double	Size	{ get; set; }
		public SharpDX.DirectWrite.TextFormat ToDirectWriteTextFormat() { return new SharpDX.DirectWrite.TextFormat { FontSize = (float)Size, Bold = Bold }; }
	}
}

namespace NinjaTrader.Gui.SuperDom
{
	internal static class StubMarker { }
}

namespace NinjaTrader.Gui.Chart
{
	public class ChartControlProperties
	{
		public NinjaTrader.Gui.Tools.SimpleFont	LabelFont		{ get; set; }
		public System.Windows.Media.Brush		ChartBackground	{ get; set; }
		public System.Windows.Media.Brush		ChartText		{ get; set; }
		public double							BarDistance		{ get; set; }
	}

	public class ChartBars
	{
		public int						FromIndex	{ get; set; }
		public int						ToIndex		{ get; set; }
		public NinjaTrader.Data.Bars	Bars		{ get; set; }
	}

	/// <summary>Linear fake chart: bar i is drawn at PanelX + (i - FromIndex + 0.5) * BarDistance.</summary>
	public class ChartControl
	{
		public ChartControlProperties	Properties		{ get; set; }
		public double					BarWidth		{ get; set; }
		public ChartBars[]				BarsArray		{ get; set; }
		public System.Windows.PresentationSource PresentationSource { get; set; }
		public int						PanelX			{ get; set; }
		public double					BarDistance		{ get; set; }

		public int GetXByBarIndex(ChartBars chartBars, int barIndex)
		{
			return (int)Math.Round(PanelX + (barIndex - chartBars.FromIndex + 0.5) * BarDistance);
		}
		public int		GetXByTime(DateTime time)							{ return 0; }
		public double	GetBarPaintWidth(ChartBars chartBars)				{ return Math.Max(1, BarDistance * 0.6); }
		public DateTime	GetTimeByX(int x)									{ return DateTime.MinValue; }
	}

	public class ChartScale
	{
		public double	MaxValue	{ get; set; }
		public double	MinValue	{ get; set; }
		public int		Height		{ get; set; }
		public int		PanelY		{ get; set; }
		public int GetYByValue(double val)
		{
			double range = MaxValue - MinValue;
			if (range <= 0) return PanelY;
			return (int)Math.Round(PanelY + (MaxValue - val) / range * Height);
		}
		public double GetValueByY(float y) { return MaxValue - (y - PanelY) / Height * (MaxValue - MinValue); }
	}

	public class ChartPanel
	{
		public int X { get; set; }
		public int Y { get; set; }
		public int W { get; set; }
		public int H { get; set; }
		public int PanelIndex { get; set; }
	}

	public enum ScaleJustification { Left, Overlay, Right }

	public static class ChartingExtensions
	{
		public static int ConvertToHorizontalPixels(this int wpfPixels, System.Windows.PresentationSource source) { return wpfPixels; }
		public static int ConvertToVerticalPixels(this int wpfPixels, System.Windows.PresentationSource source) { return wpfPixels; }
	}
}

namespace NinjaTrader.NinjaScript
{
	public enum State { Undefined, SetDefaults, Configure, Active, DataLoaded, Historical, Transition, Realtime, Terminated, Finalized }

	public enum Calculate { OnBarClose, OnEachTick, OnPriceChange }

	public enum MaximumBarsLookBack { TwoHundredFiftySix, Infinite }

	public enum Priority { High, Low, Medium }

	public enum LogLevel { Alert, Error, Information, Warning }

	public interface ISeries<T>
	{
		T this[int barsAgo] { get; }
		int Count { get; }
		T GetValueAt(int barIndex);
		bool IsValidDataPoint(int barsAgo);
	}

	public class Series<T> : ISeries<T>
	{
		private readonly NinjaScriptBase owner;
		private readonly Dictionary<int, T> values = new Dictionary<int, T>();
		public Series(NinjaScriptBase owner) { this.owner = owner; }
		public Series(NinjaScriptBase owner, MaximumBarsLookBack lookBack) { this.owner = owner; }
		public T this[int barsAgo]
		{
			get { T v; values.TryGetValue(owner.CurrentBar - barsAgo, out v); return v; }
			set { values[owner.CurrentBar - barsAgo] = value; }
		}
		public int Count { get { return owner.Count; } }
		public T GetValueAt(int barIndex) { T v; values.TryGetValue(barIndex, out v); return v; }
		public bool IsValidDataPoint(int barsAgo) { return values.ContainsKey(owner.CurrentBar - barsAgo); }
		public void Reset() { values.Remove(owner.CurrentBar); }
	}

	public class PriceSeries : ISeries<double>
	{
		private readonly NinjaScriptBase	owner;
		private readonly Func<int, double>	getter;
		private readonly string				name;
		public PriceSeries(NinjaScriptBase owner, string name, Func<int, double> getter) { this.owner = owner; this.name = name; this.getter = getter; }
		public double this[int barsAgo]
		{
			get
			{
				int idx = owner.CurrentBar - barsAgo;
				if (barsAgo < 0 || idx < 0)
					throw new ArgumentOutOfRangeException("barsAgo", string.Format("{0}[{1}] accessed on bar {2}: index out of range", name, barsAgo, owner.CurrentBar));
				return getter(idx);
			}
		}
		public int Count { get { return owner.Count; } }
		public double GetValueAt(int barIndex) { return getter(barIndex); }
		public bool IsValidDataPoint(int barsAgo) { return true; }
	}

	public class TimeSeries : ISeries<DateTime>
	{
		private readonly NinjaScriptBase	owner;
		private readonly Func<int, DateTime> getter;
		public TimeSeries(NinjaScriptBase owner, Func<int, DateTime> getter) { this.owner = owner; this.getter = getter; }
		public DateTime this[int barsAgo]
		{
			get
			{
				int idx = owner.CurrentBar - barsAgo;
				if (barsAgo < 0 || idx < 0)
					throw new ArgumentOutOfRangeException("barsAgo", string.Format("Time[{0}] accessed on bar {1}: index out of range", barsAgo, owner.CurrentBar));
				return getter(idx);
			}
		}
		public int Count { get { return owner.Count; } }
		public DateTime GetValueAt(int barIndex) { return getter(barIndex); }
		public bool IsValidDataPoint(int barsAgo) { return true; }
	}

	public sealed class AlertRecord
	{
		public string	Id;
		public Priority	Priority;
		public string	Message;
		public string	Sound;
		public int		RearmSeconds;
		public DateTime	Time;
		public State	State;
		public bool		Delivered;		// false when NinjaTrader would have ignored it (not realtime, or not re-armed yet)
		public override string ToString() { return string.Format("{0:HH:mm:ss} [{1}] {2}{3}", Time, Id, Message, Delivered ? "" : " (suppressed)"); }
	}

	public abstract class NinjaScriptBase
	{
		public string							Name						{ get; set; }
		public string							Description					{ get; set; }
		public State							State						{ get; set; }
		public Calculate						Calculate					{ get; set; }
		public bool								IsOverlay					{ get; set; }
		public bool								DisplayInDataBox			{ get; set; }
		public bool								DrawOnPricePanel			{ get; set; }
		public bool								DrawHorizontalGridLines		{ get; set; }
		public bool								DrawVerticalGridLines		{ get; set; }
		public bool								PaintPriceMarkers			{ get; set; }
		public bool								IsSuspendedWhileInactive	{ get; set; }
		public bool								IsChartOnly					{ get; set; }
		public bool								IsAutoScale					{ get; set; }
		public MaximumBarsLookBack				MaximumBarsLookBack			{ get; set; }
		public int								BarsRequiredToPlot			{ get; set; }
		public NinjaTrader.Gui.Chart.ScaleJustification ScaleJustification { get; set; }
		public int								CurrentBar					{ get; set; }
		/// <summary>NinjaTrader's Count. The harness sets SimCount; -1 means Bars.Count.</summary>
		public int								SimCount = -1;
		public int								Count						{ get { return SimCount >= 0 ? SimCount : (Bars != null ? Bars.Count : 0); } }
		public bool								IsFirstTickOfBar			{ get; set; }
		public NinjaTrader.Data.Bars			Bars						{ get; set; }
		public NinjaTrader.Data.Bars[]			BarsArray					{ get; set; }
		public NinjaTrader.Data.BarsPeriod		BarsPeriod					{ get; set; }
		public NinjaTrader.Cbi.Instrument		Instrument					{ get; set; }
		public double							TickSize					{ get; set; }
		public ISeries<double>					Input						{ get; set; }
		public PriceSeries						Open						{ get; set; }
		public PriceSeries						High						{ get; set; }
		public PriceSeries						Low							{ get; set; }
		public PriceSeries						Close						{ get; set; }
		public PriceSeries						Volume						{ get; set; }
		public TimeSeries						Time						{ get; set; }

		// ---- recording ----
		public readonly List<string>			SimPrints	= new List<string>();
		public readonly List<string>			SimLogs		= new List<string>();
		public readonly List<AlertRecord>		SimAlerts	= new List<AlertRecord>();
		private readonly Dictionary<string, DateTime> alertArmedAt = new Dictionary<string, DateTime>();

		/// <summary>Wires the price/time series to a Bars object (harness).</summary>
		public void SimAttach(NinjaTrader.Data.Bars bars)
		{
			Bars		= bars;
			BarsArray	= new[] { bars };
			BarsPeriod	= bars.BarsPeriod;
			Instrument	= bars.Instrument;
			TickSize	= bars.Instrument.MasterInstrument.TickSize;
			Open		= new PriceSeries(this, "Open", i => bars.GetOpen(i));
			High		= new PriceSeries(this, "High", i => bars.GetHigh(i));
			Low			= new PriceSeries(this, "Low", i => bars.GetLow(i));
			Close		= new PriceSeries(this, "Close", i => bars.GetClose(i));
			Volume		= new PriceSeries(this, "Volume", i => bars.GetVolume(i));
			Time		= new TimeSeries(this, i => bars.GetTime(i));
			Input		= Close;
		}

		protected virtual void OnStateChange() { }
		protected virtual void OnBarUpdate() { }
		protected virtual void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate) { }

		public void Print(object value) { SimPrints.Add(value == null ? "" : value.ToString()); }
		public void Log(string message, LogLevel logLevel) { SimLogs.Add(logLevel + ": " + message); }

		public void Alert(string id, Priority priority, string message, string soundLocation, int rearmSeconds,
			System.Windows.Media.Brush backBrush, System.Windows.Media.Brush foreBrush)
		{
			DateTime now = NinjaTrader.Core.Globals.Now;
			bool delivered = State == State.Realtime;
			DateTime armed;
			if (delivered && alertArmedAt.TryGetValue(id ?? "", out armed) && now < armed)
				delivered = false;
			if (delivered)
				alertArmedAt[id ?? ""] = now.AddSeconds(rearmSeconds);
			SimAlerts.Add(new AlertRecord { Id = id, Priority = priority, Message = message, Sound = soundLocation, RearmSeconds = rearmSeconds,
				Time = now, State = State, Delivered = delivered });
		}
		public void RearmAlert(string id) { alertArmedAt.Remove(id ?? ""); }
		public void ClearOutputWindow() { SimPrints.Clear(); }
		public bool EqualsInput(object input) { return true; }
	}
}

namespace NinjaTrader.NinjaScript.BarsTypes
{
	/// <summary>Per-bar volumetric data with real bid/ask volume per price row.</summary>
	public class VolumetricData
	{
		private readonly Dictionary<long, long[]>	rows = new Dictionary<long, long[]>();	// row key (tick index) -> [ask, bid]
		private readonly double						tickSize;
		private readonly int						ticksPerLevel;

		public VolumetricData(double tickSize, int ticksPerLevel)
		{
			this.tickSize		= tickSize;
			this.ticksPerLevel	= Math.Max(1, ticksPerLevel);
		}

		public long		BarDelta			{ get { return TotalBuyingVolume - TotalSellingVolume; } }
		public long		TotalVolume			{ get { return TotalBuyingVolume + TotalSellingVolume; } }
		public long		TotalBuyingVolume	{ get; private set; }
		public long		TotalSellingVolume	{ get; private set; }
		public long		Trades				{ get; private set; }

		/// <summary>Rows are aligned to multiples of "ticks per level" (harness assumption).</summary>
		private long RowOf(double price)
		{
			long tick = (long)Math.Round(price / tickSize);
			long r = tick % ticksPerLevel;
			if (r < 0) r += ticksPerLevel;
			return tick - r;
		}

		public void SimAdd(double price, long volume, int side)
		{
			long key = RowOf(price);
			long[] v;
			if (!rows.TryGetValue(key, out v)) { v = new long[2]; rows.Add(key, v); }
			if (side > 0) { v[0] += volume; TotalBuyingVolume += volume; }
			else { v[1] += volume; TotalSellingVolume += volume; }
			Trades++;
		}

		public long GetAskVolumeForPrice(double price)		{ long[] v; return rows.TryGetValue(RowOf(price), out v) ? v[0] : 0; }
		public long GetBidVolumeForPrice(double price)		{ long[] v; return rows.TryGetValue(RowOf(price), out v) ? v[1] : 0; }
		public long GetDeltaForPrice(double price)			{ return GetAskVolumeForPrice(price) - GetBidVolumeForPrice(price); }
		public long GetTotalVolumeForPrice(double price)	{ return GetAskVolumeForPrice(price) + GetBidVolumeForPrice(price); }
		public long GetMaximumVolume(bool? askVolume, out double price)
		{
			price = 0;
			long best = -1;
			foreach (KeyValuePair<long, long[]> kv in rows)
			{
				long v = askVolume == true ? kv.Value[0] : askVolume == false ? kv.Value[1] : kv.Value[0] + kv.Value[1];
				if (v > best) { best = v; price = kv.Key * tickSize; }
			}
			return Math.Max(0, best);
		}
		public double GetDeltaPercent() { return TotalVolume > 0 ? 100.0 * BarDelta / TotalVolume : 0; }
	}

	public class VolumetricBarsType : NinjaTrader.Data.BarsType
	{
		public VolumetricData[] Volumes { get; set; }
	}
}

namespace NinjaTrader.NinjaScript.DrawingTools
{
	public enum TextPosition { BottomLeft, BottomRight, Center, TopLeft, TopRight }

	public class DrawingTool { }

	public class TextFixed : DrawingTool { }

	public static class Draw
	{
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition) { return new TextFixed(); }
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition,
			System.Windows.Media.Brush textBrush, NinjaTrader.Gui.Tools.SimpleFont font, System.Windows.Media.Brush outlineBrush,
			System.Windows.Media.Brush areaBrush, int areaOpacity) { return new TextFixed(); }
	}

	public static class DrawObjectsExtensions { }
}

namespace NinjaTrader.Gui.NinjaScript
{
	using NinjaTrader.NinjaScript;
	using NinjaTrader.Gui.Chart;

	public abstract class IndicatorRenderBase : NinjaScriptBase
	{
		public ChartControl						ChartControl	{ get; set; }
		public ChartBars						ChartBars		{ get; set; }
		public ChartPanel						ChartPanel		{ get; set; }
		public SharpDX.Direct2D1.RenderTarget	RenderTarget	{ get; set; }
		public bool								IsInHitTest		{ get; set; }
		public int								ZOrder			{ get; set; }

		protected virtual void OnRender(ChartControl chartControl, ChartScale chartScale) { }
		public virtual void OnRenderTargetChanged() { }
		public int SimForceRefreshes;
		public readonly List<DateTime> SimForceRefreshTimes = new List<DateTime>();
		public void ForceRefresh() { SimForceRefreshes++; SimForceRefreshTimes.Add(NinjaTrader.Core.Globals.Now); }
		public void SetZOrder(int desiredZOrderLevel) { ZOrder = desiredZOrderLevel; }
	}

	public abstract class StrategyRenderBase : NinjaScriptBase { }
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		protected T CacheIndicator<T>(T indicator, ISeries<double> input, ref T[] cache) where T : Indicator { return indicator; }
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		protected NinjaTrader.NinjaScript.Indicators.Indicator indicator;
	}
}

namespace NinjaTrader.NinjaScript
{
	public class MarketAnalyzerColumnBase : NinjaScriptBase { }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		protected NinjaTrader.NinjaScript.Indicators.Indicator indicator;
	}
}
