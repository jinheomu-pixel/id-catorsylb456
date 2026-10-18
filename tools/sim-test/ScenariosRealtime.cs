using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	/// <summary>Realtime: what the chart shows while a bar is still forming, and when NinjaTrader is asked to repaint.</summary>
	public static class RealtimeScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("RT-01 HVN live profile follows the forming bar", "default 5-minute period; the developing histogram includes every trade of the forming bar (rebuilt at most every 300 ms) and repaints", HvnLive);
			yield return new Scenario("RT-02 Heavy Absorption marker appears and counts up live", "the circle appears on the print itself, its number grows with later same-side prints, each change repaints", AbsorptionLive);
			yield return new Scenario("RT-03 Pressure Toolkit live bubbles", "bubbles of the forming bar show before it closes and repaint at most every 200 ms", PressureLive);
			yield return new Scenario("RT-04 Imbalance zone freezes on the touching tick", "realtime touch ends the zone on the exact trade that reaches it, alert at that trade's time", ImbalanceTouchTick);
		}

		private static void HvnLive(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = HvnScenarios.FourPeriods(out p1);
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), null);
			c.Eq(IdcProfileTimeframe.Minute5, sim.I.ProfileTimeframe, "default period = 5 minutes (vendor videos: 15-second NQ chart with 5-minute nodes)");
			sim.StartHistorical();
			int firstOf20 = Gen.FirstAt(tape.Trades, Gen.BarStart(20));
			int firstOf30 = Gen.FirstAt(tape.Trades, Gen.BarStart(30));
			sim.Feed(tape.Trades.Take(firstOf20));
			sim.GoRealtime();

			object last = null;
			List<DateTime> rebuilds = new List<DateTime>();
			int uncovered = 0, refreshesBefore = sim.I.SimForceRefreshes;
			long lo = long.MaxValue, hi = long.MinValue;
			int firstOf21 = Gen.FirstAt(tape.Trades, Gen.BarStart(21));
			for (int i = firstOf20; i < firstOf21; i++)
			{
				Trade t = tape.Trades[i];
				sim.Feed(t);
				long tick = sim.Tick(t.Price);
				lo = Math.Min(lo, tick); hi = Math.Max(hi, tick);
				object h = R.Get(sim.I, "liveHistogram");
				if (i == firstOf20)
				{
					if (c.Check(h != null, "live histogram exists on the first trade of a new period"))
					{
						c.Eq(20, R.Get<int>(h, "StartBar"), "live histogram starts at the period's first bar");
						c.Check(R.Get<bool>(h, "IsLive"), "histogram marked live");
					}
					c.Check(HvnScenarios.Zones(sim.I).Count > 0, "previous period finalized on the first tick of the new period");
				}
				if (h != null && !ReferenceEquals(h, last))
				{
					rebuilds.Add(t.Time);
					long low = R.Get<long>(h, "LowTick");
					long high = low + (long)R.Get<double[]>(h, "Rows").Length * R.Get<int>(h, "RowTicks") - 1;
					if (low > lo || high < hi)
						uncovered++;
				}
				last = h;
			}
			c.Note("bar 20: {0} trades, {1} live rebuilds, {2} repaints", firstOf21 - firstOf20, rebuilds.Count, sim.I.SimForceRefreshes - refreshesBefore);
			c.Check(rebuilds.Count >= 10, "live profile rebuilt while bar 20 forms (" + rebuilds.Count + " rebuilds in " + (firstOf21 - firstOf20) + " trades)");
			c.Eq(0, uncovered, "every rebuild covers the forming bar's range up to the latest trade");
			double minGap = double.MaxValue;
			for (int k = 2; k < rebuilds.Count; k++)
				minGap = Math.Min(minGap, (rebuilds[k] - rebuilds[k - 1]).TotalMilliseconds);
			c.Check(rebuilds.Count < 3 || minGap >= 300, "rebuilds throttled to 300 ms, smallest gap " + F.S(minGap) + " ms");
			c.Check(sim.I.SimForceRefreshes - refreshesBefore >= rebuilds.Count - 1, "each trade-driven rebuild repaints the chart");

			// half way through P2 the developing profile already shows its nodes (5022 primary, 5027 secondary)
			sim.Feed(tape.Trades.Skip(firstOf21).Take(firstOf30 - firstOf21));
			List<object> live = R.List(sim.I, "liveZones");
			c.Note("live zones at bar 29: {0}", string.Join(" ", live.Select(z => HvnScenarios.Describe(z, 0.25))));
			c.Check(live.Count > 0 && live.All(z => R.Get<bool>(z, "IsLive")), "developing zones exist and are marked live");
			c.Check(live.Any(z => R.Get<long>(z, "LowTick") <= sim.Tick(5022.00) && R.Get<long>(z, "HighTick") >= sim.Tick(5022.00)), "a developing zone covers the 5022 node");
			FakeChart chart = sim.RenderLast(12);
			c.Check(chart.RT.Errors.Count == 0, "no render errors: " + string.Join("; ", chart.RT.Errors));
			int finalizedLabels = chart.Texts.Count(t => t.Contains("|"));
			c.Check(finalizedLabels <= HvnScenarios.Zones(sim.I).Count, "only finalized zones carry labels (" + finalizedLabels + " labels)");
		}

		private static void AbsorptionLive(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			int rt = tape.Trades.Count;
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 2, 400);
			int iBig = tape.Trades.Count; tape.Buy(5001.00, 50, 400);
			int iUp = tape.Trades.Count; tape.Buy(5001.00, 7, 400);
			int iOther = tape.Trades.Count; tape.Sell(5000.75, 3, 400);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(rt));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(rt).Take(iBig - rt));
			c.Check(AbsorptionScenarios.Marker(sim.I, 150, 1) == null, "no marker before the big print");

			int refreshes = sim.I.SimForceRefreshes;
			sim.Feed(tape.Trades[iBig]);
			object m = AbsorptionScenarios.Marker(sim.I, 150, 1);
			if (c.Check(m != null, "marker appears on the 50-lot print while bar 150 is forming"))
				c.Near(52, R.Get<double>(m, "DisplayValue"), 1e-9, "number = buy volume at 5001.00 so far (2 + 50)");
			c.Check(sim.I.SimForceRefreshes > refreshes, "the new marker repaints the chart without waiting for the next tick");

			refreshes = sim.I.SimForceRefreshes;
			sim.Feed(tape.Trades[iUp]);
			m = AbsorptionScenarios.Marker(sim.I, 150, 1);
			if (m != null)
				c.Near(59, R.Get<double>(m, "DisplayValue"), 1e-9, "number counts up with the next buy print at that price (52 + 7)");
			c.Check(sim.I.SimForceRefreshes > refreshes, "count-up repaints");

			refreshes = sim.I.SimForceRefreshes;
			sim.Feed(tape.Trades[iOther]);
			c.Eq(refreshes, sim.I.SimForceRefreshes, "a print that changes no marker does not force a repaint");
			sim.Feed(tape.Trades.Skip(iOther + 1));
			c.Check(AbsorptionScenarios.Marker(sim.I, 150, 1) != null, "marker kept after the bar closes");
		}

		private static void PressureLive(Ctx c)
		{
			Tape tape = PressureScenarios.SignalTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			int firstOf81 = Gen.FirstAt(tape.Trades, Gen.BarStart(81));
			sim.Feed(tape.Trades.Take(firstOf80));
			sim.GoRealtime();
			int refreshes = sim.I.SimForceRefreshes;
			int firstSeen = -1;
			for (int i = firstOf80; i < firstOf81; i++)
			{
				sim.Feed(tape.Trades[i]);
				object live = R.Get(sim.I, "liveSignals");
				if (firstSeen < 0 && live != null && R.List(live, "Bubbles").Any(b => R.Get<long>(b, "Tick") == sim.Tick(5001.00) && R.Get<int>(b, "Side") > 0))
					firstSeen = i;
			}
			c.Note("bar 80: {0} trades, buy bubble at 5001.00 first shown after trade {1}", firstOf81 - firstOf80, firstSeen - firstOf80);
			c.Check(firstSeen >= 0, "buy bubble at 5001.00 shown while bar 80 is still forming");
			List<DateTime> times = sim.I.SimForceRefreshTimes.Skip(refreshes).ToList();
			c.Check(times.Count > 0, "live bubbles repaint the chart during the bar");
			double minGap = double.MaxValue;
			for (int k = 1; k < times.Count; k++)
				minGap = Math.Min(minGap, (times[k] - times[k - 1]).TotalMilliseconds);
			c.Check(times.Count < 2 || minGap >= 200, "repaints throttled to 200 ms, smallest gap " + F.S(minGap) + " ms");
			FakeChart chart = sim.RenderLast(5);
			c.Check(chart.Ops.Any(o => o.Kind == "FillEllipse"), "live bubble drawn on the forming bar");
		}

		private static void ImbalanceTouchTick(Ctx c)
		{
			Tape tape = ImbalanceScenarios.BuyStackTape();
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), i => i.AlertOnTouch = true);
			sim.StartHistorical();
			int firstOf2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			sim.Feed(tape.Trades.Take(firstOf2));
			sim.GoRealtime();
			int touchAt = -1;
			for (int i = firstOf2; i < tape.Trades.Count; i++)
			{
				if (touchAt < 0 && tape.Trades[i].Price <= 5000.75 && tape.Trades[i].Time >= Gen.BarStart(2))
					touchAt = i;
				sim.Feed(tape.Trades[i]);
				object z = ImbalanceScenarios.Zones(sim.I).FirstOrDefault(x => R.Get<int>(x, "StartBar") == 1);
				if (i == firstOf2)
					c.Check(z != null, "zone exists from the first trade of the next bar");
				if (z == null)
					continue;
				int end = R.Get<int>(z, "EndBar");
				if (touchAt < 0 || i < touchAt)
				{
					if (!c.Check(end < 0, "zone not ended before any trade reached it (trade " + i + ")")) break;
				}
				else if (i == touchAt)
					c.Check(end == 3, "zone frozen on the touching trade itself (bar 3), EndBar " + end);
			}
			List<AlertRecord> delivered = sim.I.SimAlerts.Where(a => a.Delivered).ToList();
			if (c.Check(touchAt >= 0 && delivered.Count == 1, "one touch alert, got " + delivered.Count))
				c.Eq(tape.Trades[touchAt].Time, delivered[0].Time, "alert raised at the touching trade's time");
		}
	}
}
