using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class AbsorptionScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("HA-01 big prints: placement, live count-up, relocation", "markers at the right bar/price/side, number counts up live, moves to a bigger print", BigPrints);
			yield return new Scenario("HA-02 grouping SameTimestamp", "same-timestamp prints of one side are summed into one order", Grouping);
			yield return new Scenario("HA-03 synthetic quotes before detection", "Tick Replay without real bid/ask: an up-tick buy print must not become a SELL marker", SyntheticSide);
			yield return new Scenario("HA-04 uniform print sizes", "when every print has the same size nothing is exceptional", UniformPrints);
			yield return new Scenario("HA-05 realtime bar-close alert", "one alert per marked bar at bar close in realtime", BarCloseAlert);
			yield return new Scenario("HA-06 no Tick Replay", "history shows a warning, realtime prints are marked once the population is large enough", NoTickReplay);
		}

		internal static Dictionary<int, object> BarData(object ind) { return R.IntDict(ind, "barData"); }

		internal static object Marker(object ind, int bar, int side)
		{
			object data;
			if (!BarData(ind).TryGetValue(bar, out data)) return null;
			return R.Get(data, side > 0 ? "Buy" : "Sell");
		}

		internal static string Describe(object m, double ts)
		{
			if (m == null) return "none";
			return string.Format("[bar {0} {1} @{2} largest={3} qual={4} display={5}]", R.Get<int>(m, "Bar"), R.Get<int>(m, "Side") > 0 ? "BUY" : "SELL",
				F.S(R.Get<long>(m, "Tick") * ts), F.S(R.Get<double>(m, "LargestPrint")), F.S(R.Get<double>(m, "QualifyingVolume")), F.S(R.Get<double>(m, "DisplayValue")));
		}

		private static int CountMarkers(object ind, int fromBar, int toBar)
		{
			int n = 0;
			foreach (KeyValuePair<int, object> kv in BarData(ind))
				if (kv.Key >= fromBar && kv.Key <= toBar)
					n += (R.Get(kv.Value, "Buy") != null ? 1 : 0) + (R.Get(kv.Value, "Sell") != null ? 1 : 0);
			return n;
		}

		/// <summary>150 bars of small prints (sizes 1..5) then the scripted bar 150.</summary>
		private static Tape Population(int bars = 150, bool synthetic = false)
		{
			Tape tape = new Tape(0.25, Gen.T0) { SyntheticQuotes = synthetic };
			Gen.SmallPrints(tape, 0, bars, 20, 4996, 5004, new Random(11));
			return tape;
		}

		private static void BigPrints(Ctx c)
		{
			Tape tape = Population();
			DateTime b150 = Gen.BarStart(150);
			tape.At(b150.AddSeconds(1));
			tape.Buy(5001.00, 2); tape.Buy(5001.00, 2); tape.Sell(5000.75, 3);
			int iBig = tape.Trades.Count; tape.Buy(5001.00, 50);
			int iUp1 = tape.Trades.Count; tape.Buy(5001.00, 1);
			int iUp2 = tape.Trades.Count; tape.Buy(5001.00, 3);
			int iSell = tape.Trades.Count; tape.Sell(5000.75, 40);
			int iMove = tape.Trades.Count; tape.Buy(5001.50, 80);
			int iSmaller = tape.Trades.Count; tape.Buy(5001.00, 30);
			int iLast = tape.Trades.Count; tape.Buy(5001.50, 4);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			double ts = 0.25;
			// oracle: mean + 2 sd of all prints of the last 100 bars (51..150) seen before the big print
			List<double> window = tape.Trades.Take(iBig).Where(t => t.Time >= Gen.BarStart(51)).Select(t => (double)t.Volume).ToList();
			double mean = window.Average();
			double sd = Math.Sqrt(window.Sum(v => (v - mean) * (v - mean)) / window.Count);
			Dictionary<int, Action> checks = new Dictionary<int, Action>
			{
				{ iBig - 1, () => {
					c.Check(Marker(sim.I, 150, 1) == null, "no buy marker before the big print");
					IdcBarWindowStats st = R.Get<IdcBarWindowStats>(sim.I, "stats");
					c.Near(mean + 2 * sd, st.Threshold(2), 1e-6, "threshold = mean + 2 sd of the prints of the last 100 bars");
					c.Eq((double)window.Count, st.Count, "population = prints of the last 100 bars");
				} },
				{ iBig, () => {
					object m = Marker(sim.I, 150, 1);
					if (c.Check(m != null, "buy marker appears live on the big print"))
					{
						c.Eq(sim.Tick(5001.00), R.Get<long>(m, "Tick"), "marker at the big print's price");
						c.Near(54, R.Get<double>(m, "DisplayValue"), 1e-9, "display = buy volume at 5001.00 in the bar (2+2+50)");
					} } },
				{ iUp1, () => c.Near(55, R.Get<double>(Marker(sim.I, 150, 1), "DisplayValue"), 1e-9, "display counts up to 55") },
				{ iUp2, () => c.Near(58, R.Get<double>(Marker(sim.I, 150, 1), "DisplayValue"), 1e-9, "display counts up to 58") },
				{ iSell, () => {
					object m = Marker(sim.I, 150, -1);
					if (c.Check(m != null, "sell marker on the 40-lot sell print"))
					{
						c.Eq(sim.Tick(5000.75), R.Get<long>(m, "Tick"), "sell marker price");
						c.Near(43, R.Get<double>(m, "DisplayValue"), 1e-9, "sell display = sell volume at 5000.75 (3+40)");
					} } },
				{ iMove, () => {
					object m = Marker(sim.I, 150, 1);
					c.Eq(sim.Tick(5001.50), R.Get<long>(m, "Tick"), "buy marker relocates to the bigger print's price");
					c.Near(80, R.Get<double>(m, "DisplayValue"), 1e-9, "display after relocation");
				} },
				{ iSmaller, () => c.Eq(sim.Tick(5001.50), R.Get<long>(Marker(sim.I, 150, 1), "Tick"), "a smaller qualifying print does not move the marker") },
				{ iLast, () => c.Near(84, R.Get<double>(Marker(sim.I, 150, 1), "DisplayValue"), 1e-9, "display counts up at the new price (80+4)") },
			};
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				Action a;
				if (checks.TryGetValue(i, out a)) a();
			}
			c.Eq(0, CountMarkers(sim.I, 0, 149), "no markers in the population bars (all prints <= 5 < mean + 2 sd)");
			object buy = Marker(sim.I, 150, 1);
			c.Near(160, R.Get<double>(buy, "QualifyingVolume"), 1e-9, "qualifying volume = 50 + 80 + 30");
			IdcBarWindowStats stats = R.Get<IdcBarWindowStats>(sim.I, "stats");
			c.Note("threshold now {0:0.00} (mean {1:0.00}, sd {2:0.00}, n {3})", stats.Threshold(2), stats.Mean, stats.StdDev, stats.Count);
			c.Note("markers: {0} {1}", Describe(buy, ts), Describe(Marker(sim.I, 150, -1), ts));

			// rendering: number inside, size grows with sqrt(volume)
			FakeChart chart = sim.RenderLast(10);
			List<string> texts = chart.Texts.ToList();
			c.Check(texts.Contains("84") && texts.Contains("43"), "circle numbers 84 and 43 drawn, texts: [" + string.Join(", ", texts) + "]");
			SharpDX.Direct2D1.DrawOp big = chart.Ops.Where(o => o.Kind == "FillEllipse").OrderByDescending(o => o.W).FirstOrDefault();
			if (c.Check(big != null, "circles drawn"))
			{
				c.Near((16 + 1.5 * Math.Sqrt(84)) / 2, big.W, 0.01, "buy circle radius = (16 + 1.5 sqrt(84)) / 2");
				c.Near(chart.Y(5001.50), big.Y, 0.5, "buy circle centred at 5001.50");
				c.Near(chart.BarX(150), big.X, 0.5, "circle centred on bar 150");
			}

			// label mode 'qualifying volume'
			Sim<IDCHeavyAbsorption> sim2 = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.LabelMode = IdcAbsorptionLabel.QualifyingVolume);
			sim2.StartHistorical();
			sim2.Feed(tape.Trades);
			c.Near(160, R.Get<double>(Marker(sim2.I, 150, 1), "DisplayValue"), 1e-9, "QualifyingVolume label mode shows 160");
		}

		private static void Grouping(Ctx c)
		{
			Tape tape = Population();
			tape.At(Gen.BarStart(150).AddSeconds(2));
			tape.Buy(5001.00, 4, 0); tape.Buy(5001.25, 4, 0); tape.Buy(5001.50, 4, 100);
			int iNext = tape.Trades.Count; tape.Sell(5001.25, 1);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);

			Sim<IDCHeavyAbsorption> ind = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			ind.StartHistorical();
			ind.Feed(tape.Trades);
			c.Check(Marker(ind.I, 150, 1) == null, "Individual mode: three 4-lot prints are not heavy (threshold ~5.8)");

			Sim<IDCHeavyAbsorption> grp = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.PrintGrouping = IdcPrintGrouping.SameTimestamp);
			grp.StartHistorical();
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				grp.Feed(tape.Trades[i]);
				if (i == iNext)
				{
					object m = Marker(grp.I, 150, 1);
					if (c.Check(m != null, "SameTimestamp mode: the 12-lot sweep is marked once the next print arrives"))
					{
						c.Eq(grp.Tick(5001.00), R.Get<long>(m, "Tick"), "marker at the first price of the sweep (equal parts)");
						c.Near(12, R.Get<double>(m, "LargestPrint"), 1e-9, "grouped print size");
					}
				}
			}
			c.Eq(0, CountMarkers(grp.I, 0, 149), "no markers in population bars (grouping mode)");
		}

		private static void SyntheticSide(Ctx c)
		{
			// Provider without historical bid/ask: every print is stamped Bid = Last, Ask = Last + 1 tick.
			// 15 bars x 20 prints = 300 prints, then a 60-lot print on an UP-tick (a buyer lifting the offer), then more bars.
			Tape tape = Population(15, synthetic: true);
			Trade prev = tape.Trades[tape.Trades.Count - 1];
			tape.At(Gen.BarStart(15).AddSeconds(1));
			tape.Print(prev.Price, 2, -1);
			tape.Print(prev.Price + 0.25, 60, 1);
			Gen.SmallPrints(tape, 16, 30, 20, 4996, 5004, new Random(12));
			// after detection (> 500 prints) another up-tick 60-lot buy
			Trade prev2 = tape.Trades[tape.Trades.Count - 1];
			tape.At(Gen.BarStart(46).AddSeconds(1));
			tape.Print(prev2.Price, 2, -1);
			tape.Print(prev2.Price + 0.25, 60, 1);
			Gen.Filler(tape, 47, 5000.00, 5000.25, 4999.75, 5000.50, 2);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			c.Check(engine.SyntheticQuotes, "synthetic quotes detected by the end");
			c.Check(Marker(sim.I, 46, 1) != null && Marker(sim.I, 46, -1) == null, "after detection: up-tick 60-lot is a BUY marker (bar 46): buy="
				+ Describe(Marker(sim.I, 46, 1), 0.25) + " sell=" + Describe(Marker(sim.I, 46, -1), 0.25));
			c.Check(Marker(sim.I, 15, 1) != null && Marker(sim.I, 15, -1) == null, "before detection (print #302): up-tick 60-lot must be a BUY marker, got buy="
				+ Describe(Marker(sim.I, 15, 1), 0.25) + " sell=" + Describe(Marker(sim.I, 15, -1), 0.25));
		}

		private static void UniformPrints(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 40, 20, 4996, 5004, new Random(3), maxSize: 1);
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			int n = CountMarkers(sim.I, 0, 39);
			c.Check(n == 0, "no heavy-absorption markers when every print is 1 lot (sd = 0), got " + n + " markers on " + BarData(sim.I).Count + " bars");
		}

		private static void BarCloseAlert(Ctx c)
		{
			Tape tape = Population();
			int rt = tape.Trades.Count;
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 50);
			tape.Sell(5000.75, 40);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);
			Gen.Filler(tape, 152, 5001.25, 5001.50, 5001.00, 5001.75, 2);
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.AlertOnBarClose = true);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(rt));
			sim.GoRealtime();
			int firstOf151 = Gen.FirstAt(tape.Trades, Gen.BarStart(151));
			for (int i = rt; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == firstOf151 - 1)
					c.Eq(0, sim.I.SimAlerts.Count, "no alert before bar 150 closes");
			}
			List<AlertRecord> delivered = sim.I.SimAlerts.Where(a => a.Delivered).ToList();
			c.Eq(2, delivered.Count, "two alerts (buy and sell marker of bar 150): " + string.Join(" / ", sim.I.SimAlerts));
			c.Check(delivered.Any(a => a.Message.Contains("BUY") && a.Message.Contains("5001.00")), "buy alert names price 5001.00");
		}

		private static void NoTickReplay(Ctx c)
		{
			Tape tape = Population(30);
			int split = tape.Trades.Count;
			Gen.SmallPrints(tape, 30, 15, 20, 4996, 5004, new Random(5));
			tape.At(Gen.BarStart(45).AddSeconds(1));
			tape.Buy(5000.00, 50);
			Gen.Filler(tape, 46, 5000.00, 5000.25, 4999.75, 5000.50, 2);
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions { TickReplay = false }, null);
			sim.RunHistoricalBars(tape.Trades.Take(split));
			c.Eq(0, BarData(sim.I).Count, "no print data without Tick Replay");
			FakeChart chart = sim.RenderLast(20);
			c.Check(chart.Texts.Any(t => t.Contains("enable Tick Replay")), "warning text asks for Tick Replay");
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(split));
			c.Check(Marker(sim.I, 45, 1) != null, "realtime 50-lot print marked after 300 realtime prints");
		}
	}
}
