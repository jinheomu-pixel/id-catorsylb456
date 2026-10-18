// Minimal NinjaTrader 8 API surface used ONLY to compile-check the IDCator indicators outside
// NinjaTrader (e.g. on Linux / CI). Signatures follow the official NinjaScript documentation
// (developer.ninjatrader.com/docs/desktop). Nothing here is executed and this file must NOT be
// copied into NinjaTrader - the real platform provides all of these types.
//
// Compile with:  dotnet build tools/compile-check
#pragma warning disable 0067, 0649, 0169, 1591
using System;
using System.Collections.Generic;

namespace NinjaTrader.Core
{
	public static class Globals
	{
		public static SharpDX.DirectWrite.Factory	DirectWriteFactory	{ get { return null; } }
		public static string						InstallDir			{ get { return ""; } }
		public static string						UserDataDir			{ get { return ""; } }
		public static DateTime						Now					{ get { return DateTime.Now; } }
		public static DateTime						MinDate				{ get { return DateTime.MinValue; } }
		public static double ToCryptocurrencyVolume(long volume) { return volume; }
		public static NinjaTrader.Core.GeneralOptions GeneralOptions { get { return null; } }
	}

	public class GeneralOptions
	{
		public TimeZoneInfo TimeZoneInfo { get { return TimeZoneInfo.Local; } }
	}
}

namespace NinjaTrader.Core.FloatingPoint
{
	public static class DoubleExtensions
	{
		public static int ApproxCompare(this double a, double b) { return a.CompareTo(b); }
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
		public double RoundToTickSize(double price) { return price; }
		public double RoundDownToTickSize(double price) { return price; }
		public int Compare(double price1, double price2) { return price1.CompareTo(price2); }
		public string FormatPrice(double price, bool round = true) { return price.ToString(); }
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

	public class BarsSeries
	{
		public BarsType BarsType { get; set; }
	}

	public class TradingHours
	{
		public TimeZoneInfo TimeZoneInfo { get; set; }
	}

	public class Bars
	{
		public int							Count					{ get; set; }
		public bool							IsTickReplay			{ get; set; }
		public bool							IsFirstBarOfSession		{ get; set; }
		public bool							IsResetOnNewTradingDay	{ get; set; }
		public BarsPeriod					BarsPeriod				{ get; set; }
		public BarsSeries					BarsSeries				{ get; set; }
		public BarsType						BarsType				{ get; set; }
		public NinjaTrader.Cbi.Instrument	Instrument				{ get; set; }
		public TradingHours					TradingHours			{ get; set; }
		public long							TickCount				{ get; set; }
		public double	GetOpen(int index)		{ return 0; }
		public double	GetHigh(int index)		{ return 0; }
		public double	GetLow(int index)		{ return 0; }
		public double	GetClose(int index)		{ return 0; }
		public long		GetVolume(int index)	{ return 0; }
		public DateTime	GetTime(int index)		{ return DateTime.MinValue; }
		public int		GetBar(DateTime time)	{ return 0; }
		public DateTime	GetSessionEndTime(int index) { return DateTime.MinValue; }
		public bool		IsFirstBarOfSessionByIndex(int index) { return false; }
	}

	public class SessionIterator
	{
		public SessionIterator(Bars bars) { }
		public DateTime ActualSessionBegin			{ get; set; }
		public DateTime ActualSessionEnd			{ get; set; }
		public DateTime ActualTradingDayExchange	{ get; set; }
		public DateTime ActualTradingDayEndLocal	{ get; set; }
		public bool		GetNextSession(DateTime timeLocal, bool includesEndTimeStamp) { return true; }
		public DateTime	GetTradingDay(DateTime timeLocal) { return timeLocal.Date; }
		public bool		IsInSession(DateTime timeLocal, bool includesEndTimeStamp, bool isIntraDay) { return true; }
		public bool		IsNewSession(DateTime time, bool includesEndTimeStamp) { return false; }
	}
}

namespace NinjaTrader.Gui
{
	public static class Serialize
	{
		public static string BrushToString(System.Windows.Media.Brush brush) { return ""; }
		public static System.Windows.Media.Brush StringToBrush(string value) { return null; }
	}

	public static class DxExtensions
	{
		public static SharpDX.Direct2D1.Brush ToDxBrush(this System.Windows.Media.Brush brush, SharpDX.Direct2D1.RenderTarget renderTarget) { return null; }
		public static SharpDX.Vector2 ToVector2(this System.Windows.Point point) { return new SharpDX.Vector2((float)point.X, (float)point.Y); }
	}

	public class Stroke
	{
		public System.Windows.Media.Brush	Brush		{ get; set; }
		public float						Width		{ get; set; }
		public DashStyleHelper				DashStyleHelper	{ get; set; }
		public Stroke() { }
		public Stroke(System.Windows.Media.Brush brush, DashStyleHelper dashStyleHelper, float width) { }
	}

	public enum DashStyleHelper { Dash, DashDot, DashDotDot, Dot, Solid }
}

namespace NinjaTrader.Gui.Tools
{
	public class SimpleFont
	{
		public SimpleFont() { }
		public SimpleFont(string familyName, int size) { }
		public bool		Bold	{ get; set; }
		public bool		Italic	{ get; set; }
		public double	Size	{ get; set; }
		public SharpDX.DirectWrite.TextFormat ToDirectWriteTextFormat() { return null; }
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

	public class ChartControl
	{
		public ChartControlProperties	Properties		{ get; set; }
		public double					BarWidth		{ get; set; }
		public ChartBars[]				BarsArray		{ get; set; }
		public System.Windows.PresentationSource PresentationSource { get; set; }
		public int		GetXByBarIndex(ChartBars chartBars, int barIndex)	{ return 0; }
		public int		GetXByTime(DateTime time)							{ return 0; }
		public double	GetBarPaintWidth(ChartBars chartBars)				{ return 0; }
		public DateTime	GetTimeByX(int x)									{ return DateTime.MinValue; }
	}

	public class ChartScale
	{
		public double	MaxValue	{ get; set; }
		public double	MinValue	{ get; set; }
		public int		Height		{ get; set; }
		public int		GetYByValue(double val)	{ return 0; }
		public double	GetValueByY(float y)	{ return 0; }
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
	public enum State { SetDefaults, Configure, Active, DataLoaded, Historical, Transition, Realtime, Terminated, Finalized }

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
		public Series(NinjaScriptBase owner) { }
		public Series(NinjaScriptBase owner, MaximumBarsLookBack lookBack) { }
		public T this[int barsAgo] { get { return default(T); } set { } }
		public int Count { get { return 0; } }
		public T GetValueAt(int barIndex) { return default(T); }
		public bool IsValidDataPoint(int barsAgo) { return true; }
		public void Reset() { }
	}

	public class PriceSeries : ISeries<double>
	{
		public double this[int barsAgo] { get { return 0; } }
		public int Count { get { return 0; } }
		public double GetValueAt(int barIndex) { return 0; }
		public bool IsValidDataPoint(int barsAgo) { return true; }
	}

	public class TimeSeries : ISeries<DateTime>
	{
		public DateTime this[int barsAgo] { get { return DateTime.MinValue; } }
		public int Count { get { return 0; } }
		public DateTime GetValueAt(int barIndex) { return DateTime.MinValue; }
		public bool IsValidDataPoint(int barsAgo) { return true; }
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
		public int								Count						{ get; set; }
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

		protected virtual void OnStateChange() { }
		protected virtual void OnBarUpdate() { }
		protected virtual void OnMarketData(NinjaTrader.Data.MarketDataEventArgs marketDataUpdate) { }

		public void Print(object value) { }
		public void Log(string message, LogLevel logLevel) { }
		public void TriggerCustomEvent(Action<object> customEvent, object state) { }
		public void TriggerCustomEvent(Action<object> customEvent, int barsSeriesIndex, object state) { }
		public void Alert(string id, Priority priority, string message, string soundLocation, int rearmSeconds,
			System.Windows.Media.Brush backBrush, System.Windows.Media.Brush foreBrush) { }
		public void RearmAlert(string id) { }
		public void ClearOutputWindow() { }
		public bool EqualsInput(object input) { return true; }
	}
}

namespace NinjaTrader.NinjaScript.BarsTypes
{
	public class VolumetricData
	{
		public long		BarDelta			{ get; set; }
		public long		TotalVolume			{ get; set; }
		public long		TotalBuyingVolume	{ get; set; }
		public long		TotalSellingVolume	{ get; set; }
		public long		Trades				{ get; set; }
		public long		GetAskVolumeForPrice(double price)		{ return 0; }
		public long		GetBidVolumeForPrice(double price)		{ return 0; }
		public long		GetDeltaForPrice(double price)			{ return 0; }
		public long		GetTotalVolumeForPrice(double price)	{ return 0; }
		public long		GetMaximumVolume(bool? askVolume, out double price) { price = 0; return 0; }
		public double	GetDeltaPercent()						{ return 0; }
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
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition) { return null; }
		public static TextFixed TextFixed(NinjaScriptBase owner, string tag, string text, TextPosition textPosition,
			System.Windows.Media.Brush textBrush, NinjaTrader.Gui.Tools.SimpleFont font, System.Windows.Media.Brush outlineBrush,
			System.Windows.Media.Brush areaBrush, int areaOpacity) { return null; }
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
		public void ForceRefresh() { }
		public void SetZOrder(int desiredZOrderLevel) { }
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
