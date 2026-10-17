// Used only when compile-checking against the REAL NinjaTrader 8 assemblies (-p:NT8BinDir=...).
// NinjaTrader keeps these partial classes as source in bin\Custom (@Indicator.cs, @Strategy.cs,
// @MarketAnalyzerColumn.cs); this file provides the same minimal shape so the generated-code
// regions of the indicators compile.
namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase { }
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		private readonly NinjaTrader.NinjaScript.Indicators.Indicator indicator = null;
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		private readonly NinjaTrader.NinjaScript.Indicators.Indicator indicator = null;
	}
}

// Order Flow+ Volumetric bars type: compiled into NinjaTrader's Custom assembly, not into
// NinjaTrader.Core/Gui, so it is shimmed here with the members documented under
// "Order Flow Volumetric Bars" in the NinjaScript help guide.
namespace NinjaTrader.NinjaScript.BarsTypes
{
	public class VolumetricData
	{
		public long BarDelta { get; set; }
		public long TotalVolume { get; set; }
		public long TotalBuyingVolume { get; set; }
		public long TotalSellingVolume { get; set; }
		public long GetAskVolumeForPrice(double price) { return 0; }
		public long GetBidVolumeForPrice(double price) { return 0; }
		public long GetDeltaForPrice(double price) { return 0; }
		public long GetTotalVolumeForPrice(double price) { return 0; }
		public long GetMaximumVolume(bool? askVolume, out double price) { price = 0; return 0; }
	}

	public abstract class VolumetricBarsType : NinjaTrader.Data.BarsType
	{
		public VolumetricData[] Volumes { get; set; }
	}
}
