using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	/// <summary>Regression scenarios for commits 4b39cca + b6d518c (rev2 review).</summary>
	public static class RegrScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("RG-01 session-open bar kept in realtime-only mode", "no Tick Replay / no Volumetric: realtime starts while the market is closed, the next session's first bar is fully observed and must be analysed", SessionOpenBar);
			yield return new Scenario("RG-02 Day bars: forming daily bar not closed at realtime start (Imbalance)", "Day bars are stamped with the trading date (00:00): during the session the forming bar must not be analysed as complete", DailyImbalance);
			yield return new Scenario("RG-03 Day bars: forming daily bar not closed at realtime start (Heavy Absorption)", "markers created after realtime start on the forming daily bar must count up and show their volume; bar-close alert must fire", DailyAbsorption);
			yield return new Scenario("RG-04 Heavy Absorption SameTimestamp preview in QualifyingVolume label mode", "the live circle of a heavy sweep must not show 0 while the sweep is pending", PreviewQualifying);
			yield return new Scenario("RG-06 HVN Daily: realtime start in an intra-day break", "a trading day with two sessions must stay one Daily period when realtime starts during the break", HvnDailyBreak);
			yield return new Scenario("RG-07 HVN window never starts when Max bars back cuts the current period", "Daily profile on 15-s bars late in the session: the current day must still be profiled", HvnWindowNeverStarts);
			yield return new Scenario("RG-08 Heavy Absorption: print stamped inside a bar closed at realtime start", "PC clock slightly ahead of the feed: a late print of the last bar must not create a marker showing 0", LatePrint);
			yield return new Scenario("RG-09 Heavy Absorption absolute circle size is stable", "an old marker keeps its diameter when later bars trade bigger prints (Absolute sizing)", StableSize);
			yield return new Scenario("RG-05 HVN single dominant node survives sensitivity trim", "a period with one sharp high volume node must still produce a zone", HvnSharpNode);
		}

		// ------------------------------------------------------------------------------------------------
		private static void SessionOpenBar(Ctx c)
		{
			// history: filler bars 0..1 (Monday). Market closes. Next session (Tuesday 09:30) the very first realtime bar holds
			// the buy stack from ImbalanceScenarios.BuyStackTape (bar 1 there), the bar after it closes it.
			Tape hist = new Tape(0.25, Gen.T0);
			Gen.Filler(hist, 0, 5001.50, 5001.75, 5001.25, 5002.00);
			Gen.Filler(hist, 1, 5001.75, 5001.50, 5001.25, 5002.00);

			DateTime open = Gen.T0.AddDays(1);	// Tuesday 09:30
			Tape rt = new Tape(0.25, open);
			List<Level> stack = new List<Level>
			{
				new Level(5000.00, 2, 8),
				new Level(5000.25, 50, 6),
				new Level(5000.50, 75, 5),
				new Level(5000.75, 40, 3),
				new Level(5001.00, 10, 9),
			};
			stack.AddRange(Gen.Balanced(0.25, 5001.25, 5003.00, 8));
			rt.Bar(open, Gen.S15, 5000.75, 5002.00, stack, 5);
			rt.Bar(open.AddSeconds(15), Gen.S15, 5002.00, 5001.75, Gen.Balanced(0.25, 5001.25, 5002.25, 10), 5);
			rt.Bar(open.AddSeconds(30), Gen.S15, 5001.75, 5001.75, Gen.Balanced(0.25, 5001.50, 5002.00, 10), 5);

			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions { TickReplay = false }, null);
			sim.RunHistoricalBars(hist.Trades);
			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;
			NinjaTrader.Core.Globals.SimNow = Gen.T0.AddHours(8);	// Monday 17:30, market closed
			sim.GoRealtime();
			sim.Feed(rt.Trades);

			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			int firstRtBar = 2;
			c.Note("firstStreamBar = {0}, footprint of bar 2 = {1}", R.Get<int>(engine, "firstStreamBar"), engine.GetBar(firstRtBar, true) == null ? "null" : "present");
			List<object> zones = R.List(sim.I, "zones").Where(z => R.Get<int>(z, "StartBar") == firstRtBar && R.Get<bool>(z, "IsSupport")).ToList();
			c.Check(zones.Count == 1, "support zone from the fully observed session-open bar 2, got " + zones.Count);

			// same for the Pressure Toolkit / HVN: footprint of a fully observed bar must be available
			c.Check(engine.GetBar(firstRtBar, true) != null, "engine returns the footprint of the session-open bar (all of its trades were seen)");
		}

		// ------------------------------------------------------------------------------------------------
		private static readonly DateTime DayA = new DateTime(2026, 3, 2, 9, 30, 0);	// trading day Mon 2 March
		private static readonly DateTime DayB = new DateTime(2026, 3, 3, 9, 30, 0);	// trading day Tue 3 March
		private static readonly DateTime DayC = new DateTime(2026, 3, 4, 9, 30, 0);	// trading day Wed 4 March

		private static void DailyImbalance(Ctx c)
		{
			Tape tape = new Tape(0.25, DayA);
			tape.Bar(DayA, TimeSpan.FromMinutes(1), 5001.50, 5001.75, Gen.Balanced(0.25, 5001.25, 5002.00, 10), 5);
			// day B, before the indicator went realtime: balanced trade above the later stack
			tape.Bar(DayB, TimeSpan.FromMinutes(1), 5001.50, 5001.75, Gen.Balanced(0.25, 5001.25, 5002.00, 10), 5);
			int split = tape.Trades.Count;
			// day B after realtime start (market open, 10:00): the buy stack trades
			List<Level> stack = new List<Level>
			{
				new Level(5000.00, 2, 8),
				new Level(5000.25, 50, 6),
				new Level(5000.50, 75, 5),
				new Level(5000.75, 40, 3),
				new Level(5001.00, 10, 9),
			};
			stack.AddRange(Gen.Balanced(0.25, 5001.25, 5003.00, 8));
			tape.Bar(DayB.AddMinutes(30), TimeSpan.FromMinutes(1), 5000.75, 5002.00, stack, 5);
			// day C opens and closes day B
			tape.Bar(DayC, TimeSpan.FromMinutes(1), 5002.00, 5002.25, Gen.Balanced(0.25, 5001.75, 5002.50, 10), 5);

			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions { PeriodType = BarsPeriodType.Day, PeriodValue = 1 }, null);
			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;		// Day bars are time based in NinjaTrader
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(split));
			int barB = sim.CurrentBar;
			c.Note("day B = bar {0}, stamped {1:yyyy-MM-dd HH:mm}", barB, sim.Bars.GetTime(barB));
			NinjaTrader.Core.Globals.SimNow = DayB.AddMinutes(30);		// 10:00, session open
			sim.GoRealtime();
			c.Eq(-1 + barB, R.Get<int>(sim.I, "lastAnalyzedBar"), "forming daily bar B not analysed at realtime start (lastAnalyzedBar)");
			sim.Feed(tape.Trades.Skip(split));
			List<object> zones = R.List(sim.I, "zones").Where(z => R.Get<int>(z, "StartBar") == barB && R.Get<bool>(z, "IsSupport")).ToList();
			c.Check(zones.Count == 1, "support zone from the complete daily bar B (stack traded after realtime start), got " + zones.Count);
		}

		private static void DailyAbsorption(Ctx c)
		{
			Tape tape = new Tape(0.25, DayA);
			// day A: 300 small prints (population), sizes 1..5
			Random rng = new Random(7);
			double price = 5000;
			for (int i = 0; i < 300; i++)
			{
				price = tape.P(Math.Max(4996, Math.Min(5004, price + (rng.Next(3) - 1) * 0.25)));
				tape.Print(price, 1 + i % 5, i % 2 == 0 ? 1 : -1, 100);
			}
			// day B history: a few small prints and a 60-lot buy at 5000.50 (buy marker exists before realtime)
			tape.At(DayB);
			for (int i = 0; i < 20; i++)
				tape.Print(5000.25 + (i % 3) * 0.25, 1 + i % 5, i % 2 == 0 ? 1 : -1, 100);
			tape.Buy(5000.50, 60, 100);
			int split = tape.Trades.Count;
			// realtime part of day B
			tape.At(DayB.AddMinutes(30));
			int iUp = tape.Trades.Count; tape.Buy(5000.50, 2, 100);
			int iSell = tape.Trades.Count; tape.Sell(5001.00, 90, 100);
			tape.Buy(5000.75, 1, 100);
			// day C opens
			tape.At(DayC);
			tape.Buy(5001.00, 1, 100);
			tape.Sell(5001.00, 2, 100);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions { PeriodType = BarsPeriodType.Day, PeriodValue = 1 },
				i => i.AlertOnBarClose = true);
			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(split));
			int barB = sim.CurrentBar;
			NinjaTrader.Core.Globals.SimNow = DayB.AddMinutes(30);		// 10:00, session open
			sim.GoRealtime();
			for (int i = split; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == iUp)
				{
					object m = AbsorptionScenarios.Marker(sim.I, barB, 1);
					c.Check(m != null, "buy marker on day B");
					double histBuy = tape.Trades.Take(split).Where(t => t.Time >= DayB && t.Side > 0 && Math.Abs(t.Price - 5000.50) < 1e-9).Sum(t => (double)t.Volume);
					if (m != null)
						c.Near(histBuy + 2, R.Get<double>(m, "DisplayValue"), 1e-9, "buy marker counts up live after realtime start (" + histBuy + " + 2)");
				}
				if (i == iSell)
				{
					object m = AbsorptionScenarios.Marker(sim.I, barB, -1);
					if (c.Check(m != null, "sell marker for the 90-lot realtime sell on day B"))
						c.Near(90, R.Get<double>(m, "DisplayValue"), 1e-9, "sell marker shows its volume");
				}
			}
			List<AlertRecord> alerts = sim.I.SimAlerts.Where(a => a.Delivered).ToList();
			c.Check(alerts.Count == 2, "bar-close alerts for day B when day C opens (buy + sell), got " + alerts.Count + ": " + string.Join(" / ", sim.I.SimAlerts));
		}

		// ------------------------------------------------------------------------------------------------
		private static void PreviewQualifying(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			tape.At(Gen.BarStart(150).AddSeconds(2));
			int i1 = tape.Trades.Count; tape.Buy(5001.00, 30, 0);
			int i2 = tape.Trades.Count; tape.Buy(5001.25, 30, 0);
			int i3 = tape.Trades.Count; tape.Buy(5001.50, 30, 3000);
			// 3 s later the next print (different timestamp) flushes the sweep
			int iNext = tape.Trades.Count; tape.Sell(5001.25, 1);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), i =>
			{
				i.PrintGrouping = IdcPrintGrouping.SameTimestamp;
				i.LabelMode = IdcAbsorptionLabel.QualifyingVolume;
			});
			sim.StartHistorical();
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == i3)
				{
					object m = AbsorptionScenarios.Marker(sim.I, 150, 1);
					if (c.Check(m != null, "pending sweep previewed as a circle"))
					{
						double shown = R.Get<double>(m, "DisplayValue");
						c.Check(shown > 0, "previewed circle shows the sweep volume (expected 90 or at least > 0), shows " + shown);
						FakeChart chart = sim.RenderLast(5);
						c.Note("texts drawn while the sweep is pending: [{0}]", string.Join(", ", chart.Texts));
					}
				}
				if (i == iNext)
				{
					object m = AbsorptionScenarios.Marker(sim.I, 150, 1);
					c.Near(90, R.Get<double>(m, "DisplayValue"), 1e-9, "after the flush the circle shows 90");
				}
			}
		}

		// ------------------------------------------------------------------------------------------------
		private static void HvnSharpNode(Ctx c)
		{
			// one 5-minute period (20 bars of 15 s): base volume 6 per tick over 5000.00..5010.00 (41 ticks, 1 tick per row),
			// plus a node of 70 extra contracts at 5005.00 (h ~ 12x base). After two 1-2-1 smoothing passes the node
			// covers 3 rows above the 70 % threshold.
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 20; b++)
			{
				List<Level> levels = new List<Level>();
				for (long t = 20000; t <= 20040; t++)
				{
					double p = t * 0.25;
					long extra = (t == 20020 && b < 10) ? 3 : 0;		// 10 bars x 3 x 2 sides = 60 extra at 5005.00 (+ open/close prints)
					// base 6 per tick spread over the 20 bars: give the bar 1+1 lots on 3 of 20 bars per tick
					long baseV = ((t + b) % 20 < 3) ? 1 : 0;
					if (baseV + extra > 0)
						levels.Add(new Level(p, baseV + extra, baseV + extra));
				}
				tape.Bar(Gen.BarStart(b), Gen.S15, 5005.00, 5005.00, levels, 5);
			}
			// next period
			for (int b = 20; b < 24; b++)
				Gen.Filler(tape, b, 5005.00, 5005.00, 5004.00, 5006.00, 2);

			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), i => i.ProfileTimeframe = IdcProfileTimeframe.Minute5);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			List<object> zones = R.List(sim.I, "zones");
			c.Note("zones: {0}", string.Join("; ", zones.Select(z => string.Format("{0}-{1} poc {2} primary {3}",
				R.Get<long>(z, "LowTick"), R.Get<long>(z, "HighTick"), R.Get<long>(z, "PocTick"), R.Get<bool>(z, "IsPrimary")))));
			foreach (object h in R.List(sim.I, "histograms"))
			{
				double[] rows = R.Get<double[]>(h, "Rows");
				long lo = R.Get<long>(h, "LowTick");
				c.Note("histogram bars {0}-{1}: rows around 5005: {2}", R.Get<int>(h, "StartBar"), R.Get<int>(h, "EndBar"),
					string.Join(" ", Enumerable.Range(0, rows.Length).Where(r => Math.Abs(lo + r - 20020) <= 3).Select(r => (lo + r) + ":" + rows[r].ToString("0.000"))));
			}
			c.Check(zones.Count >= 1, "the period's dominant node (POC 5005.00) produces a zone, got " + zones.Count + " zones");

			Sim<IDCHvnProfile> legacy = new Sim<IDCHvnProfile>(new SimOptions(), i => { i.ProfileTimeframe = IdcProfileTimeframe.Minute5; i.ClusterSensitivity = 0; });
			legacy.StartHistorical();
			legacy.Feed(tape.Trades);
			List<object> lz = R.List(legacy.I, "zones");
			c.Note("with sensitivity 0 (= old behaviour, no trim): {0} zone(s): {1}", lz.Count, string.Join("; ", lz.Select(z => string.Format("{0}-{1} poc {2} primary {3}",
				R.Get<long>(z, "LowTick"), R.Get<long>(z, "HighTick"), R.Get<long>(z, "PocTick"), R.Get<bool>(z, "IsPrimary")))));
		}
	
		// ------------------------------------------------------------------------------------------------
		private static void HvnDailyBreak(Ctx c)
		{
			// trading day 2026-03-02: session 1 = 01 Mar 17:00 -> 02 Mar 06:00, break 06:00-07:30, session 2 = 07:30 -> 16:00
			Tape tape = new Tape(0.25, new DateTime(2026, 3, 2, 5, 55, 0));
			DateTime s1 = new DateTime(2026, 3, 2, 5, 55, 0);
			for (int b = 0; b < 20; b++)
				tape.Bar(s1.AddSeconds(15 * b), Gen.S15, 5000.00, 5000.25, Gen.Balanced(0.25, 4999.50, 5000.75, 3), 5);
			int split = tape.Trades.Count;
			DateTime s2 = new DateTime(2026, 3, 2, 7, 30, 0);
			for (int b = 0; b < 20; b++)
				tape.Bar(s2.AddSeconds(15 * b), Gen.S15, 5002.00, 5002.25, Gen.Balanced(0.25, 5001.50, 5002.75, 3), 5);
			int dayEnd = tape.Trades.Count;
			DateTime d2 = new DateTime(2026, 3, 2, 17, 0, 0);	// next trading day
			for (int b = 0; b < 4; b++)
				tape.Bar(d2.AddSeconds(15 * b), Gen.S15, 5003.00, 5003.25, Gen.Balanced(0.25, 5002.50, 5003.75, 3), 5);

			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), i => i.ProfileTimeframe = IdcProfileTimeframe.Daily);
			sim.Bars.TradingHours.BreakOffset = TimeSpan.FromHours(13);
			sim.Bars.TradingHours.BreakLength = TimeSpan.FromHours(1.5);
			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(split));
			int lastS1 = sim.CurrentBar;
			NinjaTrader.Core.Globals.SimNow = new DateTime(2026, 3, 2, 6, 30, 0);	// inside the break
			sim.GoRealtime();
			c.Eq(0, R.List(sim.I, "histograms").Count, "no finalized Daily profile while the trading day continues after the break");
			sim.Feed(tape.Trades.Skip(split));
			List<object> day1 = R.List(sim.I, "histograms").Where(h => R.Get<int>(h, "StartBar") <= lastS1 + 20).ToList();
			c.Note("finalized histograms: {0}", string.Join("; ", R.List(sim.I, "histograms").Select(h => R.Get<int>(h, "StartBar") + "-" + R.Get<int>(h, "EndBar"))));
			c.Eq(1, day1.Count, "trading day 2026-03-02 is one Daily period (bars 0..39)");
			if (day1.Count > 0)
				c.Eq(lastS1 + 20, R.Get<int>(day1[day1.Count - 1], "EndBar"), "Daily period ends with the last bar of session 2");
		}

		private static void HvnWindowNeverStarts(Ctx c)
		{
			// trading day 2026-03-02 starts 01 Mar 17:00; 21 hours of 15-s bars = 5040 bars > Max bars back 5000
			DateTime start = new DateTime(2026, 3, 1, 17, 0, 0);
			Tape tape = new Tape(0.25, start);
			Random rng = new Random(5);
			double price = 5000;
			int bars = 5040;
			for (int b = 0; b < bars + 40; b++)
			{
				tape.At(start.AddSeconds(15 * b + 1));
				for (int k = 0; k < 3; k++)
				{
					price = tape.P(Math.Max(4990, Math.Min(5010, price + (rng.Next(3) - 1) * 0.25)));
					tape.Print(price, 1 + rng.Next(4), rng.Next(2) == 0 ? 1 : -1, 3000);
				}
			}
			int split = Gen.FirstAt(tape.Trades, start.AddSeconds(15 * bars));
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions { TickReplay = false }, i => i.ProfileTimeframe = IdcProfileTimeframe.Daily);
			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;
			sim.RunHistoricalBars(tape.Trades.Take(split));
			NinjaTrader.Core.Globals.SimNow = sim.Bars.GetTime(sim.Bars.Count - 1).AddSeconds(-5);	// market open, last bar forming
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(split));
			object live = R.Get(sim.I, "liveHistogram");
			object current = R.Get(sim.I, "current");
			c.Note("Count {0}, MaxBarsBack {1}, windowStarted {2}, current period {3}", sim.I.Count, sim.I.MaxBarsBack, R.Get<bool>(sim.I, "windowStarted"),
				current == null ? "none" : R.Get<int>(current, "StartBar") + "-" + R.Get<int>(current, "EndBar"));
			c.Check(live != null, "the current trading day has a live profile (it was cut by Max bars back, but 4999 of its bars are loaded)");
		}
	
		private static void LatePrint(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 60);
			int split = tape.Trades.Count;
			// realtime: the feed delivers one more print of bar 150 (stamped 14.5 s into the bar) after the PC clock passed the bar end
			tape.At(Gen.BarStart(150).AddMilliseconds(14500));
			int iLate = tape.Trades.Count; tape.Sell(5000.75, 90, 0);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(split));
			NinjaTrader.Core.Globals.SimNow = Gen.BarStart(151).AddSeconds(1);	// PC clock 1.5 s ahead of the feed
			sim.GoRealtime();
			for (int i = split; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == iLate)
				{
					object m = AbsorptionScenarios.Marker(sim.I, 150, -1);
					c.Check(m == null || R.Get<double>(m, "DisplayValue") > 0, "a marker for the late 90-lot sell must show its volume, got " + AbsorptionScenarios.Describe(m, 0.25));
				}
			}
		}
	
		private static void StableSize(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 60);
			Gen.Filler(tape, 151, 5001.25, 5001.50, 5001.00, 5001.75, 2);
			int split = tape.Trades.Count;
			// busier market afterwards: prints of 6..14 lots
			Gen.SmallPrints(tape, 152, 60, 20, 4996, 5004, new Random(12), maxSize: 5);
			for (int i = split; i < tape.Trades.Count; i++) tape.Trades[i].Volume += 8;

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(split));
			FakeChart a = sim.Render(140, 152, 4995, 5005);
			double d1 = a.Ops.Where(o => o.Kind == "FillEllipse").Select(o => (double)o.W).DefaultIfEmpty(-1).Max();
			sim.Feed(tape.Trades.Skip(split));
			FakeChart b = sim.Render(140, 152, 4995, 5005);
			double d2 = b.Ops.Where(o => o.Kind == "FillEllipse").Select(o => (double)o.W).DefaultIfEmpty(-1).Max();
			c.Note("bar-150 circle radius before {0:0.00}, after the busier hour {1:0.00}", d1, d2);
			c.Near(d1, d2, 0.01, "the bar-150 circle keeps its size");
		}
	}
}
