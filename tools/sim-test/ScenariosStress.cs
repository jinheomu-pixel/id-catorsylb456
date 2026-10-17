using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class StressScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("LONG-01 Imbalance Levels long run", "Tick Replay history + realtime with alerts, periodic OnRender; no exceptions, bounded memory", c => Long<IDCImbalanceLevels>(c, i => i.AlertOnTouch = true, CheckImbalance));
			yield return new Scenario("LONG-02 Heavy Absorption long run", "same for Heavy Absorption", c => Long<IDCHeavyAbsorption>(c, i => i.AlertOnBarClose = true, CheckAbsorption));
			yield return new Scenario("LONG-03 HVN Profile long run", "same for HVN Profile (5-minute periods)", c => Long<IDCHvnProfile>(c, i => { i.AlertOnTouch = true; i.ProfileTimeframe = IdcProfileTimeframe.Minute5; }, CheckHvn));
			yield return new Scenario("LONG-04 Pressure Toolkit long run", "same for Pressure Toolkit with every alert on", c => Long<IDCPressureToolkit>(c, i => { i.AlertBubbles = i.AlertBigTrades = i.AlertAbsorptionDots = i.AlertAbsorptionLineTouch = i.AlertPsychCross = true; }, CheckPressure));
			yield return new Scenario("CONC-01 OnRender on a second thread", "UI-thread OnRender while OnBarUpdate/OnMarketData run: no collection-modified exceptions", Concurrency);
			yield return new Scenario("LIFE-01 render target change + terminate", "brushes recreated after OnRenderTargetChanged; Terminated disposes; data series reload", Lifecycle);
		}

		private static void Long<T>(Ctx c, Action<T> configure, Action<Ctx, Sim<T>> check) where T : Indicator, new()
		{
			int bars = c.Quick ? 3000 : 20000;
			int ticks = 50;
			int histBars = bars - bars / 20;
			Sim<T> sim = new Sim<T>(new SimOptions { Capacity = bars + 10 }, configure);
			sim.StartHistorical();
			Stopwatch sw = Stopwatch.StartNew();
			long renderMs = 0;
			int renders = 0;
			int bar = 0;
			int lastRendered = 0;
			long trades = 0;
			GC.Collect();
			long memBefore = GC.GetTotalMemory(true);
			foreach (Trade t in Gen.RandomMarket(0.25, Gen.T0, bars, ticks, 2026, 5000))
			{
				if (sim.State == State.Historical && t.Time >= Gen.BarStart(histBars))
					sim.GoRealtime();
				sim.Feed(t);
				trades++;
				bar = sim.CurrentBar;
				if (bar - lastRendered >= 500)
				{
					lastRendered = bar;
					Stopwatch rs = Stopwatch.StartNew();
					FakeChart chart = sim.RenderLast(150);
					renderMs += rs.ElapsedMilliseconds;
					renders++;
					if (chart.RT.Errors.Count > 0)
						c.Check(false, "render errors at bar " + bar + ": " + string.Join("; ", chart.RT.Errors.Take(3)));
				}
			}
			sw.Stop();
			FakeChart last = sim.RenderLast(150);
			c.Check(last.RT.Errors.Count == 0, "final render without errors");
			long memAfter = GC.GetTotalMemory(true);
			double usPerTick = sw.Elapsed.TotalMilliseconds * 1000.0 / trades;
			c.Note("{0} bars x {1} ticks: {2:0.0} s total, {3:0.0} us/tick (incl. {4} renders, {5} ms), retained memory +{6:0.0} MB, alerts delivered {7}",
				bars, ticks, sw.Elapsed.TotalSeconds, usPerTick, renders, renderMs, (memAfter - memBefore) / 1048576.0, sim.I.SimAlerts.Count(a => a.Delivered));
			c.Check(usPerTick < 50, string.Format("average cost per tick {0:0.0} us (budget 50 us; NQ can print >1000 trades/s)", usPerTick));
			c.Check(sim.I.SimAlerts.All(a => a.State == State.Realtime), "Alert() only called in State.Realtime");
			check(c, sim);
			sim.Terminate();
		}

		private static IdcFootprintEngine Engine(object ind) { return R.Get<IdcFootprintEngine>(ind, "engine"); }

		private static int EngineBars(object ind)
		{
			IdcFootprintEngine e = Engine(ind);
			return R.IntDict(e, "tickBars").Count + R.IntDict(e, "volumetricCache").Count;
		}

		private static void CheckImbalance(Ctx c, Sim<IDCImbalanceLevels> sim)
		{
			List<object> zones = R.List(sim.I, "zones");
			c.Note("zones {0} (active {1}), engine bars {2}", zones.Count, zones.Count(z => R.Get<int>(z, "EndBar") < 0), EngineBars(sim.I));
			c.Check(zones.Count > 0, "random market produced imbalance zones");
			c.Check(zones.Count <= sim.I.MaxZones, "zones bounded by MaxZones");
			c.Check(EngineBars(sim.I) <= sim.I.MaxBarsBack + 10 + 260, "footprint store bounded (" + EngineBars(sim.I) + ")");
			c.Check(zones.All(z => R.Get<int>(z, "EndBar") < 0 || R.Get<int>(z, "EndBar") > R.Get<int>(z, "StartBar")), "no zone touched by its own bar");
		}

		private static void CheckAbsorption(Ctx c, Sim<IDCHeavyAbsorption> sim)
		{
			Dictionary<int, object> data = R.IntDict(sim.I, "barData");
			int markers = data.Values.Count(d => R.Get(d, "Buy") != null) + data.Values.Count(d => R.Get(d, "Sell") != null);
			c.Note("barData {0} bars, markers {1}, engine bars {2}", data.Count, markers, EngineBars(sim.I));
			c.Check(markers > 0, "random market produced heavy-absorption markers");
			// closed bars keep only their markers: bars without a marker are dropped, per-price volumes are released
			int withMarker = data.Values.Count(d => R.Get(d, "Buy") != null || R.Get(d, "Sell") != null);
			c.Check(data.Count <= withMarker + 2, "closed bars without markers released (" + data.Count + " entries, " + withMarker + " with markers)");
			int openLevels = data.Values.Count(d => R.Get(d, "BuyVolume") != null);
			c.Check(openLevels <= 2, "per-price volumes released after bar close (" + openLevels + " still held)");
			c.Check(EngineBars(sim.I) <= 10 + 260, "footprint store bounded");
			double perBar = markers / (double)Math.Min(sim.Bars.Count, sim.I.MaxBarsBack);
			c.Note("markers per bar {0:0.00}", perBar);
		}

		private static void CheckHvn(Ctx c, Sim<IDCHvnProfile> sim)
		{
			List<object> zones = R.List(sim.I, "zones");
			int hist = R.List(sim.I, "histograms").Count;
			int keys = R.IntDict(sim.I, "barKeys").Count;
			c.Note("zones {0} (active {1}), histograms {2}, barKeys {3}, engine bars {4}", zones.Count, zones.Count(z => R.Get<int>(z, "EndBar") < 0), hist, keys, EngineBars(sim.I));
			c.Check(zones.Count > 0, "random market produced HVN zones");
			c.Check(zones.Count <= sim.I.MaxZones, "zones bounded");
			c.Check(hist <= 400, "histograms bounded");
			c.Check(keys <= sim.I.MaxBarsBack + 250, "bar keys bounded (" + keys + ")");
			c.Check(zones.All(z => R.Get<int>(z, "EndBar") < 0 || R.Get<int>(z, "EndBar") > R.Get<int>(z, "PeriodEndBar")), "no zone closed by its own period");
			c.Check(zones.All(z => R.Get<long>(z, "LowTick") <= R.Get<long>(z, "PocTick") && R.Get<long>(z, "PocTick") <= R.Get<long>(z, "HighTick")), "POC inside every zone");
			c.Check(zones.All(z => double.IsNaN(R.Get<double>(z, "BuyShare")) || R.Get<double>(z, "BuyShare") >= 0 && R.Get<double>(z, "BuyShare") <= 1), "buy share in [0,1]");
		}

		private static void CheckPressure(Ctx c, Sim<IDCPressureToolkit> sim)
		{
			Dictionary<int, object> sig = R.IntDict(sim.I, "signals");
			int bubbles = sig.Values.Sum(s => R.List(s, "Bubbles").Count);
			int bigs = sig.Values.Sum(s => R.List(s, "BigTrades").Count);
			int dots = sig.Values.Sum(s => R.List(s, "Dots").Count);
			int lines = R.List(sim.I, "lines").Count;
			int large = R.IntDict(sim.I, "largePrints").Count;
			c.Note("signal bars {0}: bubbles {1}, big trades {2}, dots {3}; lines {4}; pending large-print bars {5}; engine bars {6}", sig.Count, bubbles, bigs, dots, lines, large, EngineBars(sim.I));
			c.Check(bubbles > 0 && dots > 0 && lines > 0, "random market produced bubbles, dots and lines");
			c.Check(sig.Count <= sim.I.MaxBarsBack + 210, "signals bounded");
			c.Check(lines <= sim.I.MaxLines, "lines bounded by MaxLines");
			c.Check(large <= 5, "large prints of closed bars released (" + large + " bars left)");
			c.Check(EngineBars(sim.I) <= sim.I.LookbackBars + 20 + 260, "footprint store bounded");
		}

		private static void Concurrency(Ctx c)
		{
			int bars = c.Quick ? 800 : 3000;
			RunConcurrent<IDCImbalanceLevels>(c, bars, null);
			RunConcurrent<IDCHeavyAbsorption>(c, bars, null);
			RunConcurrent<IDCHvnProfile>(c, bars, i => i.ProfileTimeframe = IdcProfileTimeframe.Minute5);
			RunConcurrent<IDCPressureToolkit>(c, bars, i => i.LiveBar = true);
		}

		private static void RunConcurrent<T>(Ctx c, int bars, Action<T> configure) where T : Indicator, new()
		{
			Sim<T> sim = new Sim<T>(new SimOptions { Capacity = bars + 10 }, configure);
			sim.StartHistorical();
			FakeChart chart = new FakeChart(sim.Bars);
			MethodInfoHolder render = new MethodInfoHolder(typeof(T));
			Exception renderError = null;
			int renderCount = 0;
			bool stop = false;
			Thread ui = new Thread(() =>
			{
				try
				{
					while (!Volatile.Read(ref stop))
					{
						int n = sim.Bars.Count;
						if (n < 5) { Thread.Yield(); continue; }
						chart.Prepare(sim.I, Math.Max(0, n - 120), n - 1, 4900, 5100);
						render.OnRender(sim.I, chart);
						renderCount++;
					}
				}
				catch (Exception e) { renderError = e; }
			});
			ui.IsBackground = true;
			Exception dataError = null;
			ui.Start();
			try
			{
				int realtimeBar = bars - 200;
				foreach (Trade t in Gen.RandomMarket(0.25, Gen.T0, bars, 30, 77, 5000))
				{
					if (sim.State == State.Historical && t.Time >= Gen.BarStart(realtimeBar))
						sim.GoRealtime();
					sim.Feed(t);
					if (renderError != null) break;
				}
			}
			catch (Exception e) { dataError = e; }
			Volatile.Write(ref stop, true);
			ui.Join(5000);
			c.Note("{0}: {1} concurrent renders", typeof(T).Name, renderCount);
			c.Check(renderError == null, typeof(T).Name + " OnRender thread: " + (renderError == null ? "" : renderError.GetType().Name + ": " + renderError.Message + " @ " + FirstFrame(renderError)));
			c.Check(dataError == null, typeof(T).Name + " data thread: " + (dataError == null ? "" : dataError.GetType().Name + ": " + dataError.Message + " @ " + FirstFrame(dataError)));
		}

		internal static string FirstFrame(Exception e)
		{
			string st = e.StackTrace ?? "";
			string line = st.Split('\n').FirstOrDefault(l => l.Contains("IDCator")) ?? st.Split('\n').FirstOrDefault() ?? "";
			return line.Trim();
		}

		private sealed class MethodInfoHolder
		{
			private readonly System.Reflection.MethodInfo m;
			public MethodInfoHolder(Type t)
			{
				for (Type x = t; x != null && m == null; x = x.BaseType)
					m = x.GetMethod("OnRender", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
			}
			public void OnRender(object ind, FakeChart chart)
			{
				try { m.Invoke(ind, new object[] { chart.Control, chart.Scale }); }
				catch (System.Reflection.TargetInvocationException e) { throw e.InnerException; }
			}
		}

		private static void Lifecycle(Ctx c)
		{
			LifecycleOf<IDCImbalanceLevels>(c, null);
			LifecycleOf<IDCHeavyAbsorption>(c, null);
			LifecycleOf<IDCHvnProfile>(c, i => i.ProfileTimeframe = IdcProfileTimeframe.Minute1);
			LifecycleOf<IDCPressureToolkit>(c, null);
		}

		private static void LifecycleOf<T>(Ctx c, Action<T> configure) where T : Indicator, new()
		{
			string n = typeof(T).Name;
			Sim<T> sim = new Sim<T>(new SimOptions { Capacity = 1000 }, configure);
			sim.StartHistorical();
			// the chart can paint before the first bar was processed
			try { sim.Render(0, -1, 4990, 5010); c.Check(sim.Chart.RT.Errors.Count == 0, n + ": render with no bars"); }
			catch (Exception e) { c.Check(false, n + ": render with no bars threw " + e.GetType().Name + ": " + e.Message + " @ " + FirstFrame(e)); }
			List<Trade> trades = Gen.RandomMarket(0.25, Gen.T0, 600, 40, 5, 5000).ToList();
			sim.Feed(trades);
			sim.RenderLast(100);
			SharpDX.Direct2D1.RenderTarget first = sim.Chart.RT;
			// the chart's render target is recreated (e.g. resize / monitor change)
			sim.RenderTargetChanged();
			try { sim.RenderLast(100); c.Check(sim.Chart.RT.Errors.Count == 0, n + ": render after OnRenderTargetChanged"); }
			catch (Exception e) { c.Check(false, n + ": render after OnRenderTargetChanged threw " + e.GetType().Name + ": " + e.Message); }
			// NinjaTrader can hand OnRender a new RenderTarget without OnRenderTargetChanged in between (second chart tab)
			sim.Chart.NewRenderTarget(sim.I);
			try { sim.RenderLast(100); }
			catch (Exception e) { c.Check(false, n + ": render with a new RenderTarget threw " + e.GetType().Name + ": " + e.Message); }
			sim.Terminate();
			// F5 / reload: a NEW instance goes through the lifecycle again on the same data
			Sim<T> again = new Sim<T>(new SimOptions { Capacity = 1000 }, configure);
			again.StartHistorical();
			again.Feed(trades);
			try { again.RenderLast(100); }
			catch (Exception e) { c.Check(false, n + ": reload render threw " + e.Message); }
			// Terminated on an instance that never got past SetDefaults (NinjaTrader does this for the property grid copy)
			T bare = new T();
			try
			{
				System.Reflection.MethodInfo osc = typeof(T).GetMethod("OnStateChange", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				bare.State = State.SetDefaults; osc.Invoke(bare, null);
				bare.State = State.Terminated; osc.Invoke(bare, null);
			}
			catch (Exception e) { c.Check(false, n + ": Terminated without DataLoaded threw " + (e.InnerException ?? e).Message); }
		}
	}
}
