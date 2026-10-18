using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class HvnScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("HVN-01 5-min bimodal profile (Tick Replay)", "two clusters per period at the expected prices, primary = biggest, appear at period end, not closed by own bars, touched later", FiveMinuteTickReplay);
			yield return new Scenario("HVN-02 5-min bimodal profile (Volumetric history)", "same zones from VolumetricBarsType without Tick Replay", FiveMinuteVolumetric);
			yield return new Scenario("HVN-03 no footprint (bar volume spread)", "plain chart without Tick Replay still builds periods without exceptions", NoFootprint);
			yield return new Scenario("HVN-04 Daily periods with CME sessions", "evening session belongs to the next trading day; one profile per trading day", DailyPeriods);
			yield return new Scenario("HVN-05 Weekly profile on Day bars", "weekly periods on a daily chart (IsFirstBarOfSession is always false on Day bars)", WeeklyOnDayBars);
			yield return new Scenario("HVN-06 exact edge touch, GC tick 0.1", "a bar whose high equals the zone bottom must freeze it", FloatEdgeGc);
			yield return new Scenario("HVN-07 POC line (video fidelity)", "bright POC line starts at the zone's left edge and is short (about 6-7 bars of 15 s on a 5-minute period)", PocLine);
		}

		private static double G(double p, double c, double w) { return Math.Exp(-Math.Pow((p - c) / w, 2)); }

		/// <summary>
		/// One period of 20 15-second bars whose combined profile is base + a1*g(primary) + a2*g(secondary).
		/// Buy share: primaryBuy inside the primary node, secondaryBuy inside the secondary, 50 % elsewhere.
		/// Every bar trades the same per-level fraction, so own-period bars sweep through the nodes.
		/// </summary>
		internal static Dictionary<long, long> Period(Tape tape, int firstBar, int bars, double lo, double hi, double primary, double secondary,
			double primaryBuy = 0.65, double secondaryBuy = 0.40, double a1 = 120, double a2 = 100)
		{
			double ts = tape.TickSize;
			long loT = (long)Math.Round(lo / ts), hiT = (long)Math.Round(hi / ts);
			Dictionary<long, long> total = new Dictionary<long, long>();
			for (long t = loT; t <= hiT; t++)
			{
				double p = t * ts;
				total[t] = (long)Math.Round(4 + a1 * G(p, primary, 0.75) + a2 * G(p, secondary, 0.75));
			}
			for (int b = 0; b < bars; b++)
			{
				List<Level> levels = new List<Level>();
				foreach (KeyValuePair<long, long> kv in total)
				{
					long v = kv.Value / bars + (b < kv.Value % bars ? 1 : 0);
					if (v <= 0) continue;
					double p = Math.Round(kv.Key * ts, 10);
					double share = Math.Abs(p - primary) <= 1.0 ? primaryBuy : Math.Abs(p - secondary) <= 1.0 ? secondaryBuy : 0.5;
					long ask = (long)Math.Round(v * share);
					levels.Add(new Level(p, ask, v - ask));
				}
				double mid = tape.P((lo + hi) / 2);
				tape.Bar(Gen.BarStart(firstBar + b), Gen.S15, mid, b % 2 == 0 ? tape.P(mid + ts) : mid, levels, 7);
			}
			return total;
		}

		/// <summary>P1 bars 0-19 (5003-5012, nodes 5010/5005), P2 20-39 (5020-5029, nodes 5022/5027), P3 40-59 (5040-5049),
		/// P4 60-79 drops: bar 63 into P2, bar 64 to 5009.75 (P1 primary only), then stays 5013-5016; bars 80-81 finish P4.</summary>
		internal static Tape FourPeriods(out Dictionary<long, long> p1)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			p1 = Period(tape, 0, 20, 5003, 5012, 5010, 5005);
			Period(tape, 20, 20, 5020, 5029, 5022, 5027);
			Period(tape, 40, 20, 5040, 5049, 5045, 5042);
			Gen.Filler(tape, 60, 5035.00, 5034.00, 5033.00, 5035.00);
			Gen.Filler(tape, 61, 5034.00, 5033.00, 5032.00, 5034.00);
			Gen.Filler(tape, 62, 5033.00, 5031.00, 5030.50, 5033.00);
			Gen.Filler(tape, 63, 5031.00, 5016.00, 5016.00, 5031.00, 2);
			Gen.Filler(tape, 64, 5016.00, 5014.00, 5009.75, 5016.00, 2);
			for (int b = 65; b < 82; b++)
				Gen.Filler(tape, b, 5014.00, 5015.00, 5013.00, 5016.00, 3);
			return tape;
		}

		internal static List<object> Zones(object ind) { return R.List(ind, "zones"); }

		internal static string Describe(object z, double ts)
		{
			return string.Format("[{0}{1} {2}-{3} poc={4} start={5} pend={6} end={7} label={8} buy={9:0.00}]",
				R.Get<bool>(z, "IsPrimary") ? "P" : "S", R.Get<bool>(z, "IsLive") ? "live" : "",
				F.S(R.Get<long>(z, "LowTick") * ts), F.S(R.Get<long>(z, "HighTick") * ts), F.S(R.Get<long>(z, "PocTick") * ts),
				R.Get<int>(z, "StartBar"), R.Get<int>(z, "PeriodEndBar"), R.Get<int>(z, "EndBar"), F.S(R.Get<double>(z, "LabelValue")), R.Get<double>(z, "BuyShare"));
		}

		private static object ZoneAt(List<object> zones, int startBar, long tick)
		{
			return zones.FirstOrDefault(z => R.Get<int>(z, "StartBar") == startBar && R.Get<long>(z, "LowTick") <= tick && R.Get<long>(z, "HighTick") >= tick);
		}

		/// <summary>Buy share of the trades of bars [fromBar, toBar] inside the tick range [lo, hi] (the oracle for the label percentage).</summary>
		internal static double BuyShare(List<Trade> trades, int fromBar, int toBar, long lo, long hi, double ts)
		{
			double buy = 0, all = 0;
			foreach (Trade t in trades)
			{
				if (t.Time <= Gen.BarStart(fromBar) || t.Time > Gen.BarStart(toBar + 1)) continue;
				long k = (long)Math.Round(t.Price / ts);
				if (k < lo || k > hi) continue;
				all += t.Volume;
				if (t.Side > 0) buy += t.Volume;
			}
			return all > 0 ? buy / all : double.NaN;
		}

		private static void CheckFourPeriods<T>(Ctx c, Sim<T> sim, Dictionary<long, long> p1, string mode, List<Trade> trades) where T : NinjaTrader.NinjaScript.Indicators.Indicator, new()
		{
			double ts = sim.O.TickSize;
			List<object> zones = Zones(sim.I);
			string all = string.Join(" ", zones.Select(z => Describe(z, ts)));
			c.Note("{0} zones: {1}", mode, all);

			// P1
			List<object> z1 = zones.Where(z => R.Get<int>(z, "StartBar") == 0).ToList();
			c.Eq(2, z1.Count, mode + ": P1 zone count");
			object prim = ZoneAt(zones, 0, sim.Tick(5010.00));
			object sec = ZoneAt(zones, 0, sim.Tick(5005.00));
			if (c.Check(prim != null, mode + ": P1 zone containing 5010.00 exists, zones: " + all))
			{
				c.Check(R.Get<bool>(prim, "IsPrimary"), mode + ": the 5010 node (largest) is the primary zone");
				c.Eq(sim.Tick(5010.00), R.Get<long>(prim, "PocTick"), mode + ": P1 primary POC");
				c.Eq(19, R.Get<int>(prim, "PeriodEndBar"), mode + ": P1 period end bar");
				c.Near(p1[sim.Tick(5010.00)], R.Get<double>(prim, "LabelValue"), 1e-9, mode + ": P1 primary label = POC volume");
				c.Near(BuyShare(trades, 0, 19, R.Get<long>(prim, "LowTick"), R.Get<long>(prim, "HighTick"), ts), R.Get<double>(prim, "BuyShare"), 1e-9, mode + ": P1 primary buy share = buy volume / volume inside the zone");
				c.Eq(64, R.Get<int>(prim, "EndBar"), mode + ": P1 primary frozen at bar 64 (low 5009.75)");
			}
			if (c.Check(sec != null, mode + ": P1 zone containing 5005.00 exists"))
			{
				c.Check(!R.Get<bool>(sec, "IsPrimary"), mode + ": the 5005 node is secondary");
				c.Eq(sim.Tick(5005.00), R.Get<long>(sec, "PocTick"), mode + ": P1 secondary POC");
				c.Near(BuyShare(trades, 0, 19, R.Get<long>(sec, "LowTick"), R.Get<long>(sec, "HighTick"), ts), R.Get<double>(sec, "BuyShare"), 1e-9, mode + ": P1 secondary buy share");
				c.Check(R.Get<double>(sec, "BuyShare") < 0.5, mode + ": P1 secondary is seller-dominated");
				c.Eq(-1, R.Get<int>(sec, "EndBar"), mode + ": P1 secondary untouched");
			}
			// P2 touched by bar 63, P3 untouched
			foreach (object z in zones.Where(z => R.Get<int>(z, "StartBar") == 20))
				c.Eq(63, R.Get<int>(z, "EndBar"), mode + ": P2 zone frozen at bar 63 " + Describe(z, ts));
			c.Check(zones.Count(z => R.Get<int>(z, "StartBar") == 20) == 2, mode + ": P2 has two zones");
			foreach (object z in zones.Where(z => R.Get<int>(z, "StartBar") == 40))
				c.Eq(-1, R.Get<int>(z, "EndBar"), mode + ": P3 zone untouched " + Describe(z, ts));
			// no zone closed by a bar of its own period
			foreach (object z in zones)
			{
				int end = R.Get<int>(z, "EndBar");
				c.Check(end < 0 || end > R.Get<int>(z, "PeriodEndBar"), mode + ": zone not closed by its own period " + Describe(z, ts));
			}
			// P4 (bars 60-79) finalized at bar 80
			c.Check(zones.Any(z => R.Get<int>(z, "StartBar") == 60), mode + ": P4 finalized by bar 80/81");
		}

		private static void FiveMinuteTickReplay(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = FourPeriods(out p1);
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), i => i.ProfileTimeframe = IdcProfileTimeframe.Minute5);
			sim.StartHistorical();
			int firstOfBar20 = Gen.FirstAt(tape.Trades, Gen.BarStart(20));
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == firstOfBar20 - 1)
					c.Eq(0, Zones(sim.I).Count, "no finalized zone before P1 ends");
				if (i == firstOfBar20)
					c.Eq(2, Zones(sim.I).Count(z => R.Get<int>(z, "StartBar") == 0), "P1 zones appear at the first tick of the next period");
			}
			CheckFourPeriods(c, sim, p1, "TickReplay", tape.Trades);

			// labels
			FakeChart chart = sim.Render(0, sim.Bars.Count - 1, 5000, 5052);
			List<string> texts = chart.Texts.ToList();
			List<object> zs = Zones(sim.I);
			object prim = ZoneAt(zs, 0, sim.Tick(5010.00)), sec = ZoneAt(zs, 0, sim.Tick(5005.00));
			string pctP = " | " + (int)Math.Round(100 * R.Get<double>(prim, "BuyShare")) + "%";
			string pctS = " | " + (int)Math.Round(100 * (1 - R.Get<double>(sec, "BuyShare"))) + "%";
			c.Check(texts.Contains(pctP), "primary label percentage '" + pctP + "' (buyers), texts: [" + string.Join(", ", texts) + "]");
			c.Check(texts.Contains(pctS), "secondary label percentage '" + pctS + "' (sellers)");
			// percentage colours: buyers green (#2EA06E), sellers red (#C04A44)
			SharpDX.Direct2D1.DrawOp opP = chart.Ops.FirstOrDefault(o => o.Kind == "Text" && o.Text == pctP);
			SharpDX.Direct2D1.DrawOp opS = chart.Ops.FirstOrDefault(o => o.Kind == "Text" && o.Text == pctS);
			c.Check(opP != null && opP.Color.Green > opP.Color.Red, "buyer percentage drawn green");
			c.Check(opS != null && opS.Color.Red > opS.Color.Green, "seller percentage drawn red");
			c.Check(texts.Contains(p1[sim.Tick(5010.00)].ToString()), "primary label number = POC volume " + p1[sim.Tick(5010.00)]);
			c.Check(chart.RT.Errors.Count == 0, "no render errors: " + string.Join("; ", chart.RT.Errors));
			// orange primary / slate secondary fills
			c.Check(chart.Ops.Any(o => o.Kind == "FillRectangle" && Math.Abs(o.Color.Red - 1f) < 0.01 && Math.Abs(o.Color.Green - 0xA5 / 255f) < 0.01),
				"an orange (#FFA500) primary zone fill is drawn");
		}

		private static void FiveMinuteVolumetric(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = FourPeriods(out p1);
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions { TickReplay = false, Volumetric = true }, i => i.ProfileTimeframe = IdcProfileTimeframe.Minute5);
			sim.RunHistoricalBars(tape.Trades);
			CheckFourPeriods(c, sim, p1, "Volumetric", tape.Trades);
			int before = Zones(sim.I).Count;
			sim.GoRealtime();
			Tape more = new Tape(0.25, Gen.BarStart(82));
			for (int b = 82; b < 84; b++)
				Gen.Filler(more, b, 5014.00, 5015.00, 5013.00, 5016.00, 3);
			sim.Feed(more.Trades);
			c.Check(Zones(sim.I).Count >= before, "zones kept after realtime bars");
		}

		private static void NoFootprint(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = FourPeriods(out p1);
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions { TickReplay = false }, i => i.ProfileTimeframe = IdcProfileTimeframe.Minute5);
			sim.RunHistoricalBars(tape.Trades);
			List<object> zones = Zones(sim.I);
			c.Note("spread-volume zones: {0}", string.Join(" ", zones.Select(z => Describe(z, 0.25))));
			c.Check(zones.All(z => double.IsNaN(R.Get<double>(z, "BuyShare"))), "buy share unknown without footprint");
			FakeChart chart = sim.Render(0, sim.Bars.Count - 1, 5000, 5052);
			c.Check(chart.Texts.Any(t => t.Contains("no footprint history")), "warning about missing footprint drawn");
			c.Check(chart.Texts.All(t => !t.Contains("%")), "no percentage on labels without sides");
		}

		private static void DailyPeriods(Ctx c)
		{
			// 1-minute bars. Trading day Mon 2 Mar: Mon 09:30-10:30 around 5000-5010 (nodes 5008 / 5003).
			// Trading day Tue 3 Mar: Mon 17:00-17:30 + Tue 09:30-10:30 around 5100-5110 (nodes 5105 / 5101).
			// Trading day Wed 4 Mar: Tue 17:00-17:30 + Wed 09:30-10:00 around 5200, then Wed 10:00-10:10 drops to 5104.
			Tape tape = new Tape(0.25, Gen.T0);
			Random rng = new Random(7);
			Action<DateTime, int, double, double, double, double> session = (start, minutes, lo, hi, n1, n2) =>
			{
				for (int m = 0; m < minutes; m++)
				{
					DateTime bs = start.AddMinutes(m);
					List<Level> lv = new List<Level>();
					for (double p = lo; p <= hi + 1e-9; p += 0.25)
					{
						long v = (long)Math.Round(1 + 12 * G(p, n1, 0.75) + 10 * G(p, n2, 0.75));
						long a = (long)Math.Round(v * 0.55);
						lv.Add(new Level(p, a, v - a));
					}
					double mid = tape.P((lo + hi) / 2);
					tape.Bar(bs, TimeSpan.FromMinutes(1), mid, m % 2 == 0 ? mid + 0.25 : mid, lv, 4);
				}
			};
			DateTime mon = new DateTime(2026, 3, 2), tue = mon.AddDays(1), wed = mon.AddDays(2);
			session(mon.AddHours(9.5), 60, 5000, 5010, 5008, 5003);
			session(mon.AddHours(17), 30, 5100, 5110, 5105, 5101);
			session(tue.AddHours(9.5), 60, 5100, 5110, 5105, 5101);
			session(tue.AddHours(17), 30, 5195, 5205, 5200, 5197);
			session(wed.AddHours(9.5), 30, 5195, 5205, 5200, 5197);
			for (int m = 0; m < 10; m++)
			{
				double top = 5195 - m * 9;
				double bottom = Math.Max(5104, top - 9);
				List<Level> lv = Gen.Balanced(0.25, bottom, top, 1);
				tape.Bar(wed.AddHours(10).AddMinutes(m), TimeSpan.FromMinutes(1), top, bottom, lv, 4);
				if (bottom <= 5104) break;
			}

			SimOptions o = new SimOptions { PeriodType = BarsPeriodType.Minute, PeriodValue = 1 };
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(o, i => i.ProfileTimeframe = IdcProfileTimeframe.Daily);
			int callsBefore = SessionIterator.GetNextSessionCalls;
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			int calls = SessionIterator.GetNextSessionCalls - callsBefore;

			c.Check(sim.Bars.IsFirstBarOfSessionByIndex(60), "bar 60 (Mon 17:01) is the first bar of the Tue session");
			c.Check(!sim.Bars.IsFirstBarOfSessionByIndex(90), "bar 90 (Tue 09:31) is not a first bar of session");
			List<object> zones = Zones(sim.I);
			c.Note("daily zones: {0}", string.Join(" ", zones.Select(z => Describe(z, 0.25))));
			List<int> starts = zones.Select(z => R.Get<int>(z, "StartBar")).Distinct().OrderBy(x => x).ToList();
			c.Check(starts.SequenceEqual(new[] { 0, 60 }), "finalized periods start at bars 0 (Mon) and 60 (Mon 17:00 = Tue trading day), got [" + string.Join(",", starts) + "]");
			object tuePrimary = ZoneAt(zones, 60, sim.Tick(5105.00));
			if (c.Check(tuePrimary != null, "Tue primary zone around 5105"))
			{
				c.Eq(149, R.Get<int>(tuePrimary, "PeriodEndBar"), "Tue period ends with the Tue 10:30 bar (149)");
				c.Check(R.Get<bool>(tuePrimary, "IsPrimary"), "5105 node is primary");
				c.Check(R.Get<int>(tuePrimary, "EndBar") > 180, "Tue primary touched by the Wed drop (bar > 180), end = " + R.Get<int>(tuePrimary, "EndBar"));
			}
			c.Check(calls <= 6, "SessionIterator.GetNextSession called only on first bars of session (calls = " + calls + ")");
		}

		private static void WeeklyOnDayBars(Ctx c)
		{
			// 3 weeks of daily bars (Mon-Fri). Each day trades 10:00-14:00 inside an overlapping range so every week has a node.
			Tape tape = new Tape(0.25, Gen.T0);
			DateTime day = new DateTime(2026, 3, 2);
			double[] los = { 5000, 5003, 5004, 5004, 5002 };
			double[] his = { 5010, 5008, 5007, 5006, 5009 };
			int d = 0;
			for (int w = 0; w < 3; w++)
			{
				for (int i = 0; i < 5; i++)
				{
					DateTime dt = day.AddDays(7 * w + i).AddHours(10);
					double off = w * 30;
					tape.At(dt);
					for (double p = los[i] + off; p <= his[i] + off + 1e-9; p += 0.25)
						tape.Buy(p, 20, 60000);
					tape.Sell(los[i] + off, 5, 1000);
					d++;
				}
			}
			SimOptions o = new SimOptions { PeriodType = BarsPeriodType.Day, PeriodValue = 1, TickReplay = false };
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(o, i => i.ProfileTimeframe = IdcProfileTimeframe.Weekly);
			sim.RunHistoricalBars(tape.Trades);
			c.Eq(15, sim.Bars.Count, "daily bars built");
			Dictionary<int, object> keys = R.IntDict(sim.I, "barKeys");
			int distinct = keys.Values.Distinct().Count();
			c.Check(distinct == 3, "three distinct weekly period keys over 15 daily bars, got " + distinct);
			List<object> zones = Zones(sim.I);
			c.Note("weekly zones: {0}", string.Join(" ", zones.Select(z => Describe(z, 0.25))));
			List<int> starts = zones.Select(z => R.Get<int>(z, "StartBar")).Distinct().OrderBy(x => x).ToList();
			c.Check(starts.SequenceEqual(new[] { 0, 5 }), "finalized weekly periods start at bars 0 and 5, got [" + string.Join(",", starts) + "]");
		}

		private static void PocLine(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = FourPeriods(out p1);
			c.Check(!new Sim<IDCHvnProfile>(new SimOptions(), null).I.ShowPocLine, "POC line off by default (vendor default setup shows plain zones)");
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), i => { i.ProfileTimeframe = IdcProfileTimeframe.Minute5; i.ShowPocLine = true; });
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(Gen.FirstAt(tape.Trades, Gen.BarStart(45))));
			FakeChart chart = sim.Render(0, sim.Bars.Count - 1, 5000, 5052);
			int y = chart.Y(5010.00);
			List<SharpDX.Direct2D1.DrawOp> poc = chart.Ops.Where(o => o.Kind == "DrawLine" && Math.Abs(o.Y - y) < 0.5 && Math.Abs(o.H - y) < 0.5).ToList();
			if (c.Check(poc.Count > 0, "POC line drawn at 5010.00"))
			{
				SharpDX.Direct2D1.DrawOp bright = poc.OrderByDescending(o => o.Color.Alpha).First();
				double bars = (bright.W - bright.X) / chart.Control.BarDistance;
				c.Note("bright POC segment {0:0.0} bars long (alpha {1:0.00}); {2} segments", bars, bright.Color.Alpha, poc.Count);
				c.Check(Math.Abs(bright.X - (chart.BarX(0) - chart.Control.GetBarPaintWidth(chart.ChartBars) / 2)) <= 1, "bright POC line starts at the zone's left edge");
				c.Check(bars <= 8, "bright POC segment about 6-7 bars long as in the vendor video, got " + bars.ToString("0.0") + " bars");
			}
		}

		private static void FloatEdgeGc(Ctx c)
		{
			const double ts = 0.1;
			// shift a one-node period until its zone bottom lands on a tick where tick * 0.1 is above the feed price
			for (int shift = 0; shift < 40; shift++)
			{
				double baseP = 4100.0 + shift * ts;
				Tape tape = new Tape(ts, Gen.T0);
				Period(tape, 0, 20, baseP, baseP + 3.0, baseP + 1.5, baseP + 1.5, 0.5, 0.5, 120, 0);
				Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions { TickSize = ts, InstrumentName = "GC 12-26" }, i => i.ProfileTimeframe = IdcProfileTimeframe.Minute5);
				sim.StartHistorical();
				sim.Feed(tape.Trades);
				// next period starts below the zone
				Tape p2 = new Tape(ts, Gen.BarStart(20));
				Gen.Filler(p2, 20, baseP - 2.0, baseP - 2.1, baseP - 2.5, baseP - 1.8);
				sim.Feed(p2.Trades);
				List<object> zs = Zones(sim.I);
				if (zs.Count == 0) continue;
				object z = zs[0];
				long low = R.Get<long>(z, "LowTick");
				double feed = Math.Round(low * ts, 10);
				if (!(low * ts > feed)) continue;

				c.Note("GC zone {0}, bottom tick {1}: ToPrice {2:R} > feed {3:R}", Describe(z, ts), low, low * ts, feed);
				Tape p3 = new Tape(ts, Gen.BarStart(21));
				// bar 21 rallies exactly to the zone bottom
				p3.Bar(Gen.BarStart(21), Gen.S15, baseP - 2.0, feed - 0.5, Gen.Balanced(ts, baseP - 2.0, feed, 3), 5);
				Gen.Filler(p3, 22, feed - 0.5, feed - 0.8, feed - 1.0, feed - 0.4);
				sim.Feed(p3.Trades);
				c.Check(sim.Bars.GetHigh(21) == feed, "bar 21 high equals the zone bottom");
				c.Eq(21, R.Get<int>(z, "EndBar"), "HVN zone frozen by bar 21 whose high equals the zone bottom (" + feed.ToString("R") + ")");
				return;
			}
			c.Check(false, "could not construct a zone with a mismatched bottom tick");
		}
	}
}
