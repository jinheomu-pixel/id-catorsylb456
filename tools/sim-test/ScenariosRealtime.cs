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
			yield return new Scenario("RT-05 HVN quiet market: last trades still shown", "a burst inside the 300 ms throttle window, then no trade: the heartbeat adds it to the live profile and repaints", HvnQuietFlush);
			yield return new Scenario("RT-06 HVN period closes without a next trade", "watched through the period end, then quiet: the finished period's zones appear 30 s later, identical to a chart loaded later", HvnQuietClose);
			yield return new Scenario("RT-07 Imbalance last bar before a pause", "the stack bar is the last bar before a lull: its zone appears 30 s after the bar ended, once", ImbalanceQuietClose);
			yield return new Scenario("RT-08 Pressure Toolkit quiet market", "the heartbeat shows the forming bar's latest signals and closes the bar 30 s after it ended", PressureQuiet);
			yield return new Scenario("RT-09 Heavy Absorption: no stale alert after the daily break", "a bar ending 16:00 must not alert at the 17:00 open; with the heartbeat it alerts 30 s after its close", AbsorptionSessionBreak);
			yield return new Scenario("RT-10 Heavy Absorption repaint inside the throttle", "a count-up 50 ms after the last repaint is still painted by the heartbeat", AbsorptionQuietRepaint);
			yield return new Scenario("RT-11 HVN tick chart: same period live and after reload", "a 20-tick bar straddling 09:35 lands in the same period live and on a Volumetric reload", HvnTickChartKey);
		}

		private static long HistHigh(object h)
		{
			return R.Get<long>(h, "LowTick") + (long)R.Get<double[]>(h, "Rows").Length * R.Get<int>(h, "RowTicks") - 1;
		}

		private static void HvnQuietFlush(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 20; b++)
				Gen.Filler(tape, b, 5002.00, 5002.50, 5000.00, 5005.00, 6);
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			sim.GoRealtime();

			DateTime b20 = Gen.BarStart(20);
			Tape rt = new Tape(0.25, b20.AddSeconds(1));
			rt.Buy(5003.00, 5, 0);
			rt.At(b20.AddSeconds(2)); rt.Buy(5003.00, 5, 0);			// rebuilt (300 ms after the previous rebuild)
			rt.At(b20.AddSeconds(2.05)); rt.Buy(5008.00, 40, 0);		// burst inside the throttle window
			rt.At(b20.AddSeconds(2.10)); rt.Buy(5008.25, 40, 0);
			rt.At(b20.AddSeconds(2.15)); rt.Buy(5008.50, 40, 0);
			sim.Feed(rt.Trades);
			object h = R.Get(sim.I, "liveHistogram");
			c.Check(h != null && HistHigh(h) < sim.Tick(5008.00), "burst inside the 300 ms window is not in the live profile yet (throttled)");

			int refreshes = sim.I.SimForceRefreshes;
			sim.Quiet(b20.AddSeconds(3));
			h = R.Get(sim.I, "liveHistogram");
			c.Check(h != null && HistHigh(h) >= sim.Tick(5008.50), "heartbeat adds the burst within a second without another trade");
			c.Check(sim.I.SimForceRefreshes > refreshes, "and repaints the chart");
			c.Check(sim.I.SimCustomEvents > 0, "work is handed to the data thread with TriggerCustomEvent");
			object before = R.Get(sim.I, "liveHistogram");
			sim.Quiet(b20.AddSeconds(6));
			c.Check(ReferenceEquals(before, R.Get(sim.I, "liveHistogram")), "no further rebuilds while nothing changes");
		}

		private static void HvnQuietClose(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = HvnScenarios.FourPeriods(out p1);
			int firstOf20 = Gen.FirstAt(tape.Trades, Gen.BarStart(20));
			int firstOf40 = Gen.FirstAt(tape.Trades, Gen.BarStart(40));
			Func<Sim<IDCHvnProfile>, List<object>> p2 = s => HvnScenarios.Zones(s.I).Where(z => R.Get<int>(z, "StartBar") == 20).ToList();

			Sim<IDCHvnProfile> live = new Sim<IDCHvnProfile>(new SimOptions(), null);
			live.StartHistorical();
			live.Feed(tape.Trades.Take(firstOf20));
			live.GoRealtime();
			live.Feed(tape.Trades.Skip(firstOf20).Take(firstOf40 - firstOf20));
			live.Quiet(Gen.BarStart(40).AddSeconds(20));
			c.Eq(0, p2(live).Count, "within 30 s of the period end the period stays open (a late print may still come)");
			int refreshes = live.I.SimForceRefreshes;
			live.Quiet(Gen.BarStart(40).AddSeconds(45));
			List<object> watched = p2(live);
			c.Check(live.I.SimForceRefreshes > refreshes, "the finished period is painted without waiting for a trade");
			c.Check(R.Get(live.I, "liveHistogram") == null && R.List(live.I, "liveZones").Count == 0, "no developing profile left for the finished period");

			Sim<IDCHvnProfile> loaded = new Sim<IDCHvnProfile>(new SimOptions(), null);
			loaded.StartHistorical();
			loaded.Feed(tape.Trades.Take(firstOf40));
			NinjaTrader.Core.Globals.SimNow = Gen.BarStart(40).AddMinutes(30);
			loaded.GoRealtime();
			List<object> reference = p2(loaded);
			string w = string.Join(" ", watched.Select(z => HvnScenarios.Describe(z, 0.25)));
			string r = string.Join(" ", reference.Select(z => HvnScenarios.Describe(z, 0.25)));
			c.Note("watched: {0}", w);
			c.Note("loaded later: {0}", r);
			c.Check(watched.Count == 2 && w == r, "watched live = loaded later (2 finalized zones)");
			c.Check(watched.All(z => !R.Get<bool>(z, "IsLive")), "zones are final (labelled)");

			live.Feed(tape.Trades.Skip(firstOf40).Take(Gen.FirstAt(tape.Trades, Gen.BarStart(42)) - firstOf40));
			c.Eq(2, p2(live).Count, "no second profile for the period when trading resumes");
		}

		private static void ImbalanceQuietClose(Ctx c)
		{
			Tape tape = ImbalanceScenarios.BuyStackTape();
			int firstOf1 = Gen.FirstAt(tape.Trades, Gen.BarStart(1));
			int firstOf2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(firstOf1));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(firstOf1).Take(firstOf2 - firstOf1));
			sim.Quiet(Gen.BarStart(2).AddSeconds(10));
			c.Eq(0, ImbalanceScenarios.Zones(sim.I).Count, "no zone within 30 s of the bar end (a late print may still come)");
			int refreshes = sim.I.SimForceRefreshes;
			sim.Quiet(Gen.BarStart(2).AddSeconds(40));
			List<object> zs = ImbalanceScenarios.Zones(sim.I);
			if (c.Check(zs.Count == 1, "zone of the last bar appears during the pause, got " + zs.Count))
				c.Eq(1, R.Get<int>(zs[0], "StartBar"), "zone start bar");
			c.Check(sim.I.SimForceRefreshes > refreshes, "and is painted at once");
			sim.Feed(tape.Trades.Skip(firstOf2));
			zs = ImbalanceScenarios.Zones(sim.I);
			c.Eq(1, zs.Count, "still one zone once trading resumes (bar not analysed twice)");
			if (zs.Count == 1)
				c.Eq(3, R.Get<int>(zs[0], "EndBar"), "zone still freezes at the touching bar 3");
		}

		private static string Sig(object s)
		{
			if (s == null)
				return "none";
			return string.Join(",", R.List(s, "Bubbles").Select(b => R.Get<int>(b, "Side") + "@" + R.Get<long>(b, "Tick") + (R.Get<bool>(b, "Strong") ? "S" : "W") + F.S(R.Get<double>(b, "Volume"))))
				+ " | " + string.Join(",", R.List(s, "BigTrades").Select(t => R.Get<long>(t, "Tick") + "x" + F.S(R.Get<double>(t, "Volume"))))
				+ " | " + string.Join(",", R.List(s, "Dots").Select(d => (R.Get<bool>(d, "AtHigh") ? "H" : "L") + (R.Get<bool>(d, "Enhanced") ? "+" : "")));
		}

		private static void PressureQuiet(Ctx c)
		{
			Tape tape = PressureScenarios.SignalTape();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			int firstOf81 = Gen.FirstAt(tape.Trades, Gen.BarStart(81));
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(firstOf80));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(firstOf80).Take(firstOf81 - firstOf80));
			string fresh = Sig(R.Call(sim.I, "Evaluate", R.Call(R.Get(sim.I, "engine"), "GetBar", 80, false), 80));
			sim.Quiet(tape.Trades[firstOf81 - 1].Time.AddSeconds(1));
			string shown = Sig(R.Get(sim.I, "liveSignals"));
			c.Note("bar 80 footprint: {0}", fresh);
			c.Eq(fresh, shown, "after a second without events the live bar shows the latest footprint");
			c.Check(!R.IntDict(sim.I, "signals").ContainsKey(80), "bar 80 not closed before its end + 30 s");
			sim.Quiet(Gen.BarStart(81).AddSeconds(40));
			c.Check(R.IntDict(sim.I, "signals").ContainsKey(80), "bar 80 closed by the heartbeat during the pause");
			c.Check(R.Get(sim.I, "liveSignals") == null, "its live preview was replaced by the final signals");
			FakeChart chart = sim.RenderLast(5);
			c.Check(chart.Ops.Any(o => o.Kind == "FillEllipse"), "bubbles of the closed bar drawn");
		}

		private static void AbsorptionSessionBreak(Ctx c)
		{
			// bars 1400..1559 = 15:20-16:00 (trading day Mon 2 Mar); next session opens 17:00 (bar 1680)
			Func<Tape> make = () =>
			{
				Tape t = new Tape(0.25, Gen.BarStart(1400));
				Gen.SmallPrints(t, 1400, 159, 20, 4996, 5004, new Random(11));
				t.At(Gen.BarStart(1559).AddSeconds(1));
				t.Buy(5001.00, 60, 500);
				for (int k = 0; k < 10; k++)
					t.Sell(5000.75 - (k % 3) * 0.25, 1 + k % 3, 500);
				Gen.SmallPrints(t, 1680, 3, 20, 4996, 5004, new Random(12));
				return t;
			};
			Tape tape = make();
			int rt = Gen.FirstAt(tape.Trades, Gen.BarStart(1559));
			int open = Gen.FirstAt(tape.Trades, Gen.BarStart(1680));
			DateTime next = Gen.BarStart(1680);

			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.AlertOnBarClose = true);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(rt));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(rt));
			c.Check(sim.Bars.IsFirstBarOfSessionByIndex(sim.Bars.Count - 3), "17:00 bar opens the next session");
			const int lastBar = 159;		// 15:59:45 bar (the tape starts at 15:20)
			c.Check(AbsorptionScenarios.Marker(sim.I, lastBar, 1) != null, "60-lot print marked on the 15:59:45 bar");
			List<AlertRecord> stale = sim.I.SimAlerts.Where(a => a.Delivered && a.Id == "IDCHeavyAbsB" + lastBar).ToList();
			c.Check(stale.Count == 0, "no alert at the next session's open for the bar that ended at 16:00: " + string.Join(" / ", stale));

			Sim<IDCHeavyAbsorption> hb = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.AlertOnBarClose = true);
			hb.StartHistorical();
			hb.Feed(tape.Trades.Take(rt));
			hb.GoRealtime();
			hb.Feed(tape.Trades.Skip(rt).Take(open - rt));
			hb.Quiet(Gen.BarStart(1560).AddSeconds(45));
			List<AlertRecord> atClose = hb.I.SimAlerts.Where(a => a.Delivered && a.Id == "IDCHeavyAbsB" + lastBar).ToList();
			if (c.Check(atClose.Count == 1, "chart open through the close: one alert once the bar is known to be complete, got " + atClose.Count))
				c.Check(atClose[0].Time >= Gen.BarStart(1560).AddSeconds(30) && atClose[0].Time < Gen.BarStart(1560).AddSeconds(31), "alert 30 s after the bar end: " + atClose[0]);
			hb.Feed(tape.Trades.Skip(open));
			c.Eq(1, hb.I.SimAlerts.Count(a => a.Delivered && a.Id == "IDCHeavyAbsB" + lastBar), "no second alert at the next open");
		}

		private static void AbsorptionQuietRepaint(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			int rt = tape.Trades.Count;
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 50, 50);
			tape.Buy(5001.00, 7, 50);			// count-up 50 ms after the repaint for the new marker
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(rt));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(rt));
			object m = AbsorptionScenarios.Marker(sim.I, 150, 1);
			c.Check(m != null && Math.Abs(R.Get<double>(m, "DisplayValue") - 57) < 1e-9, "marker shows 57 (50 + 7)");
			DateTime lastPaint = sim.I.SimForceRefreshTimes.Count > 0 ? sim.I.SimForceRefreshTimes.Last() : DateTime.MinValue;
			c.Check(lastPaint < tape.Trades.Last().Time, "the count-up itself fell inside the 200 ms repaint throttle");
			sim.Quiet(tape.Trades.Last().Time.AddSeconds(1));
			c.Check(sim.I.SimForceRefreshTimes.Count > 0 && sim.I.SimForceRefreshTimes.Last() > tape.Trades.Last().Time, "heartbeat repaints the pending count-up");
		}

		private static void HvnTickChartKey(Ctx c)
		{
			// 20-tick bars of 20 prints 100 ms apart; bar 14 starts at 09:34:59.5 and ends at 09:35:01.4 (straddles 09:35)
			Tape tape = new Tape(0.25, Gen.T0.AddSeconds(1));
			for (int bb = 0; bb < 30; bb++)
			{
				if (bb == 14) tape.Now = Gen.T0.AddMinutes(5).AddSeconds(-0.5);
				else if (bb < 14) tape.Now = Gen.T0.AddSeconds(1 + bb * 20);
				for (int j = 0; j < 20; j++)
					tape.Print(5000.00 + (j % 6) * 0.25, 3, j % 2 == 0 ? 1 : -1, 100);
			}
			Sim<IDCHvnProfile> a = new Sim<IDCHvnProfile>(new SimOptions { PeriodType = NinjaTrader.Data.BarsPeriodType.Tick, PeriodValue = 20 }, null);
			a.StartHistorical();
			a.Feed(tape.Trades.Take(10 * 20));
			a.GoRealtime();
			a.Feed(tape.Trades.Skip(10 * 20));
			Sim<IDCHvnProfile> b = new Sim<IDCHvnProfile>(new SimOptions { PeriodType = NinjaTrader.Data.BarsPeriodType.Tick, PeriodValue = 20, TickReplay = false, Volumetric = true }, null);
			b.RunHistoricalBars(tape.Trades);
			Dictionary<int, object> ka = R.IntDict(a.I, "barKeys"), kb = R.IntDict(b.I, "barKeys");
			c.Note("bar 14 {0:HH:mm:ss.fff}: live key {1}, reload key {2}", a.Bars.GetTime(14), ka.ContainsKey(14) ? ka[14] : "-", kb.ContainsKey(14) ? kb[14] : "-");
			c.Check(ka.ContainsKey(14) && kb.ContainsKey(14) && Equals(ka[14], kb[14]), "bar straddling 09:35 lands in the same period live and after reload");
			string za = string.Join(" ", HvnScenarios.Zones(a.I).Select(z => HvnScenarios.Describe(z, 0.25)));
			string zb = string.Join(" ", HvnScenarios.Zones(b.I).Select(z => HvnScenarios.Describe(z, 0.25)));
			c.Note("live: {0}", za);
			c.Note("reload: {0}", zb);
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
