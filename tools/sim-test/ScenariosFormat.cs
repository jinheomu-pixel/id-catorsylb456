using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class FormatScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("FMT-01 label number formats", "ratio and volume texts as in the vendor description and videos ('6.7x | 350', '13.5x | 42', '1.2k')", Formats);
		}

		private static void Formats(Ctx c)
		{
			c.Eq("6.7x", IdcDraw.Ratio(6.6667), "ratio 6.67");
			c.Eq("13.5x", IdcDraw.Ratio(13.5), "ratio 13.5");
			c.Eq("5x", IdcDraw.Ratio(5.0), "integer ratio without decimals");
			c.Eq("15x", IdcDraw.Ratio(14.96), "ratio 14.96 rounds to 15x");
			c.Eq("350", IdcDraw.Volume(350), "volume 350");
			c.Eq("42", IdcDraw.Volume(42), "volume 42");
			c.Eq("1.2k", IdcDraw.Volume(1200), "volume 1200 as in the vendor text ('traded volume (e.g., 1.2k)')");
		}
	}
}
