using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class ImbalanceScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("IMB-01 buy stack below close (Tick Replay)", "zone at bar close with ratio/volume, extends, freezes at touching bar, label", BuyStackTickReplay);
			yield return new Scenario("IMB-02 discard-next-bar option", "zone retested by the very next bar is removed; later touch only freezes", DiscardNextBar);
			yield return new Scenario("IMB-03 sell stack above close", "resistance zone above close, touched from below at its bottom edge", SellStack);
			yield return new Scenario("IMB-04 volumetric history (no Tick Replay)", "same zone from VolumetricBarsType.Volumes, one OnBarUpdate per bar, then realtime", Volumetric);
			yield return new Scenario("IMB-05 only beyond close filter", "buy stack above the close is not support", OnlyBeyondClose);
			yield return new Scenario("IMB-06 realtime touch alert", "one alert when price returns to the zone in realtime", RealtimeAlert);
			yield return new Scenario("IMB-07 exact edge touch, CL tick 0.01", "a bar whose high equals the resistance zone bottom must freeze it", FloatEdgeCl);
			yield return new Scenario("IMB-08 first realtime bar without history", "no zone from a bar whose footprint only holds the realtime part", PartialFirstRealtimeBar);
		}

		/// <summary>bar 0 filler, bar 1 stacked buy imbalance 5000.25-5000.75 (12.5x | 165), bar 2 stays away, bar 3 touches 5000.75.</summary>
		internal static Tape BuyStackTape(bool bar2Touches = false)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.50, 5001.75, 5001.25, 5002.00);
			List<Level> stack = new List<Level>
			{
				new Level(5000.00, 2, 8),
				new Level(5000.25, 50, 6),
				new Level(5000.50, 75, 5),
				new Level(5000.75, 40, 3),
				new Level(5001.00, 10, 9),
			};
			stack.AddRange(Gen.Balanced(0.25, 5001.25, 5003.00, 8));
			tape.Bar(Gen.BarStart(1), Gen.S15, 5000.75, 5002.00, stack, 5);
			if (bar2Touches)
				Gen.Filler(tape, 2, 5002.00, 5001.00, 5000.50, 5002.25);
			else
				Gen.Filler(tape, 2, 5002.00, 5001.75, 5001.25, 5002.25);
			Gen.Filler(tape, 3, 5001.75, 5001.00, 5000.75, 5001.75);
			Gen.Filler(tape, 4, 5001.25, 5001.50, 5001.00, 5002.00);
			return tape;
		}

		internal static List<object> Zones(object ind) { return R.List(ind, "zones"); }

		private static string Describe(object z)
		{
			return string.Format("{0} {1}-{2} start={3} end={4} ratio={5} vol={6} pending={7}",
				R.Get<bool>(z, "IsSupport") ? "support" : "resistance", R.Get<long>(z, "LowTick"), R.Get<long>(z, "HighTick"),
				R.Get<int>(z, "StartBar"), R.Get<int>(z, "EndBar"), F.S(R.Get<double>(z, "Ratio")), F.S(R.Get<double>(z, "Volume")), R.Get<bool>(z, "Pending"));
		}

		private static void BuyStackTickReplay(Ctx c)
		{
			Tape tape = BuyStackTape();
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();

			int firstOfBar2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			int firstOfBar3 = Gen.FirstAt(tape.Trades, Gen.BarStart(3));
			int touchIdx = -1;
			for (int i = firstOfBar3; i < tape.Trades.Count; i++)
				if (tape.Trades[i].Price <= 5000.75) { touchIdx = i; break; }

			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == firstOfBar2 - 1)
					c.Eq(0, Zones(sim.I).Count, "zones after the last tick of the stack bar (must appear only at bar close)");
				if (i == firstOfBar2)
				{
					List<object> zs = Zones(sim.I);
					if (c.Check(zs.Count == 1, "exactly one zone at the first tick of the next bar, got " + zs.Count + ": " + string.Join("; ", zs.Select(Describe))))
					{
						object z = zs[0];
						c.Check(R.Get<bool>(z, "IsSupport"), "zone is support (buy stack)");
						c.Eq(sim.Tick(5000.25), R.Get<long>(z, "LowTick"), "zone low tick");
						c.Eq(sim.Tick(5000.75), R.Get<long>(z, "HighTick"), "zone high tick");
						c.Eq(1, R.Get<int>(z, "StartBar"), "zone start bar");
						c.Near(12.5, R.Get<double>(z, "Ratio"), 1e-9, "zone ratio (strongest diagonal)");
						c.Near(165, R.Get<double>(z, "Volume"), 1e-9, "zone stack volume");
						c.Eq(-1, R.Get<int>(z, "EndBar"), "zone active at creation");
					}
				}
				if (touchIdx > 0 && i == touchIdx - 1)
					c.Check(Zones(sim.I).All(z => R.Get<int>(z, "EndBar") == -1), "zone still active before the touching tick");
				if (i == touchIdx)
					c.Check(Zones(sim.I).Count == 1 && R.Get<int>(Zones(sim.I)[0], "EndBar") == 3, "zone frozen at bar 3 on the touching tick: " + string.Join("; ", Zones(sim.I).Select(Describe)));
			}
			c.Check(Zones(sim.I).Count == 1 && R.Get<int>(Zones(sim.I)[0], "EndBar") == 3, "zone stays frozen at bar 3 after later bars");

			FakeChart chart = sim.Render(0, sim.Bars.Count - 1, 4999.00, 5004.00);
			List<string> texts = chart.Texts.ToList();
			c.Check(texts.Contains("12.5x | 165"), "label '12.5x | 165' drawn, texts: [" + string.Join(", ", texts) + "]");
			c.Check(chart.RT.Errors.Count == 0, "no render errors: " + string.Join("; ", chart.RT.Errors));
			// label right-aligned inside the zone's right end (touching bar 3)
			SharpDX.Direct2D1.DrawOp label = chart.Ops.FirstOrDefault(o => o.Kind == "Text" && o.Text == "12.5x | 165");
			if (label != null)
			{
				float right = chart.BarX(3) + (float)Math.Max(1.0, chart.Control.GetBarPaintWidth(chart.ChartBars) / 2.0);
				c.Near(right - 5, label.X + label.W, 1.0, "label right edge 5 px inside the frozen zone end");
			}
			c.Note("zone: {0}", string.Join("; ", Zones(sim.I).Select(Describe)));
		}

		private static void DiscardNextBar(Ctx c)
		{
			// a) next bar trades into the zone -> removed
			Tape tape = BuyStackTape(bar2Touches: true);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), i => i.DiscardIfNextBarTouches = true);
			sim.StartHistorical();
			int firstOfBar2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == firstOfBar2)
					c.Check(Zones(sim.I).Count == 1 && R.Get<bool>(Zones(sim.I)[0], "Pending"), "pending zone exists at the first tick of the next bar");
			}
			c.Eq(0, Zones(sim.I).Count, "zone touched by the very next bar is discarded");

			// b) next bar stays away, bar 3 touches -> kept and frozen
			tape = BuyStackTape(bar2Touches: false);
			sim = new Sim<IDCImbalanceLevels>(new SimOptions(), i => i.DiscardIfNextBarTouches = true);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			List<object> zs = Zones(sim.I);
			c.Check(zs.Count == 1 && R.Get<int>(zs[0], "EndBar") == 3 && !R.Get<bool>(zs[0], "Pending"),
				"zone not touched by the next bar survives and freezes at bar 3: " + string.Join("; ", zs.Select(Describe)));

			// b2) same rule on a Volumetric chart without Tick Replay (one OnBarUpdate per bar)
			tape = BuyStackTape(bar2Touches: true);
			sim = new Sim<IDCImbalanceLevels>(new SimOptions { TickReplay = false, Volumetric = true }, i => i.DiscardIfNextBarTouches = true);
			sim.RunHistoricalBars(tape.Trades);
			c.Eq(0, Zones(sim.I).Count, "volumetric history: zone touched by the next bar is discarded");

			// c) option off (default): next-bar touch only freezes
			tape = BuyStackTape(bar2Touches: true);
			sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			zs = Zones(sim.I);
			c.Check(zs.Count == 1 && R.Get<int>(zs[0], "EndBar") == 2, "default (option off): zone kept and frozen at bar 2: " + string.Join("; ", zs.Select(Describe)));
		}

		private static void SellStack(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.50, 5001.25, 5001.00, 5001.75);
			List<Level> stack = Gen.Balanced(0.25, 5000.00, 5001.75, 8);
			stack.Add(new Level(5002.00, 9, 10));
			stack.Add(new Level(5002.25, 9, 36));
			stack.Add(new Level(5002.50, 6, 60));
			stack.Add(new Level(5002.75, 3, 45));
			stack.Add(new Level(5003.00, 4, 2));
			tape.Bar(Gen.BarStart(1), Gen.S15, 5002.25, 5000.75, stack, 5);
			Gen.Filler(tape, 2, 5000.75, 5001.25, 5000.50, 5001.50);
			Gen.Filler(tape, 3, 5001.25, 5001.75, 5001.00, 5002.00);
			// bar 4 rallies exactly to the zone bottom 5002.25
			Gen.Filler(tape, 4, 5001.75, 5002.00, 5001.50, 5002.25);
			Gen.Filler(tape, 5, 5002.00, 5001.75, 5001.50, 5002.00);

			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			List<object> zs = Zones(sim.I);
			if (c.Check(zs.Count == 1, "one zone, got " + zs.Count + ": " + string.Join("; ", zs.Select(Describe))))
			{
				object z = zs[0];
				c.Check(!R.Get<bool>(z, "IsSupport"), "zone is resistance (sell stack)");
				c.Eq(sim.Tick(5002.25), R.Get<long>(z, "LowTick"), "zone low tick");
				c.Eq(sim.Tick(5002.75), R.Get<long>(z, "HighTick"), "zone high tick");
				c.Near(20, R.Get<double>(z, "Ratio"), 1e-9, "ratio");
				c.Near(141, R.Get<double>(z, "Volume"), 1e-9, "volume");
				c.Eq(4, R.Get<int>(z, "EndBar"), "frozen at bar 4 whose high equals the zone bottom");
			}
			FakeChart chart = sim.Render(0, sim.Bars.Count - 1, 4999.00, 5004.00);
			c.Check(chart.Texts.Contains("20x | 141"), "label '20x | 141' (integer ratio without decimals), texts: [" + string.Join(", ", chart.Texts) + "]");
		}

		private static void Volumetric(Ctx c)
		{
			Tape tape = BuyStackTape();
			SimOptions o = new SimOptions { TickReplay = false, Volumetric = true };
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(o, null);
			// history = bars 0..3, bar 4 arrives in realtime
			int firstOfBar4 = Gen.FirstAt(tape.Trades, Gen.BarStart(4));
			sim.RunHistoricalBars(tape.Trades.Take(firstOfBar4));
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			c.Check(engine.UsesVolumetric, "engine uses volumetric data");
			List<object> zs = Zones(sim.I);
			if (c.Check(zs.Count == 1, "one zone from volumetric history, got " + zs.Count + ": " + string.Join("; ", zs.Select(Describe))))
			{
				object z = zs[0];
				c.Eq(sim.Tick(5000.25), R.Get<long>(z, "LowTick"), "zone low tick");
				c.Eq(sim.Tick(5000.75), R.Get<long>(z, "HighTick"), "zone high tick");
				c.Near(12.5, R.Get<double>(z, "Ratio"), 1e-9, "ratio");
				c.Near(165, R.Get<double>(z, "Volume"), 1e-9, "volume");
				c.Eq(1, R.Get<int>(z, "StartBar"), "start bar");
				// bar 3 is the last historical bar (still forming) but its range already touched the zone
				c.Eq(3, R.Get<int>(z, "EndBar"), "frozen at bar 3");
			}
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(firstOfBar4));
			Tape more = new Tape(0.25, Gen.BarStart(5));
			Gen.Filler(more, 5, 5001.50, 5001.25, 5001.00, 5001.75);
			sim.Feed(more.Trades);
			c.Eq(1, Zones(sim.I).Count, "still one zone after realtime bars");
			FakeChart chart = sim.Render(0, sim.Bars.Count - 1, 4999.00, 5004.00);
			c.Check(chart.Texts.Contains("12.5x | 165"), "label drawn, texts: [" + string.Join(", ", chart.Texts) + "]");
		}

		private static void OnlyBeyondClose(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5003.00, 5003.25, 5002.75, 5003.50);
			List<Level> stack = new List<Level>
			{
				new Level(5000.00, 2, 8), new Level(5000.25, 50, 6), new Level(5000.50, 75, 5), new Level(5000.75, 40, 3), new Level(5001.00, 10, 9),
			};
			stack.AddRange(Gen.Balanced(0.25, 5001.25, 5003.00, 8));
			// down bar closing at 5000.00, below the buy stack
			tape.Bar(Gen.BarStart(1), Gen.S15, 5003.00, 5000.00, stack, 5);
			Gen.Filler(tape, 2, 4999.75, 4999.50, 4999.25, 4999.75);
			foreach (bool filter in new[] { true, false })
			{
				Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), i => i.OnlyBeyondClose = filter);
				sim.StartHistorical();
				sim.Feed(tape.Trades);
				int n = Zones(sim.I).Count(z => R.Get<bool>(z, "IsSupport"));
				if (filter)
					c.Eq(0, n, "support zones with the filter on (buy stack above the close)");
				else
					c.Eq(1, n, "support zones with the filter off");
			}
		}

		private static void RealtimeAlert(Ctx c)
		{
			Tape tape = BuyStackTape();
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), i => i.AlertOnTouch = true);
			sim.StartHistorical();
			int firstOfBar3 = Gen.FirstAt(tape.Trades, Gen.BarStart(3));
			sim.Feed(tape.Trades.Take(firstOfBar3));
			c.Eq(0, sim.I.SimAlerts.Count, "no alert calls during history");
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(firstOfBar3));
			List<AlertRecord> delivered = sim.I.SimAlerts.Where(a => a.Delivered).ToList();
			if (c.Check(delivered.Count == 1, "exactly one delivered alert, got " + delivered.Count + ": " + string.Join(" / ", sim.I.SimAlerts)))
			{
				c.Check(delivered[0].Message.Contains("support") && delivered[0].Message.Contains("12.5x | 165"), "alert message names the zone: " + delivered[0].Message);
				c.Check(delivered[0].Message.Contains("5000.25") && delivered[0].Message.Contains("5000.75"), "alert message shows the zone prices: " + delivered[0].Message);
			}
		}

		private static void FloatEdgeCl(Ctx c)
		{
			const double ts = 0.01;
			long k = Gen.MismatchedTick(ts, 6190, 6260);
			c.Note("CL tick {0}: ToPrice = {1:R}, feed price = {2:R}", k, k * ts, Math.Round(k * ts, 10));
			double P(long t) { return Math.Round(t * ts, 10); }

			Tape tape = new Tape(ts, Gen.T0);
			tape.Bar(Gen.BarStart(0), Gen.S15, P(k - 3), P(k - 2), Gen.Balanced(ts, P(k - 4), P(k - 1), 10), 5);
			List<Level> stack = Gen.Balanced(ts, P(k - 6), P(k - 1), 8);
			stack.Add(new Level(P(k), 9, 40));
			stack.Add(new Level(P(k + 1), 4, 60));
			stack.Add(new Level(P(k + 2), 5, 45));
			stack.Add(new Level(P(k + 3), 4, 2));
			tape.Bar(Gen.BarStart(1), Gen.S15, P(k + 1), P(k - 4), stack, 5);
			tape.Bar(Gen.BarStart(2), Gen.S15, P(k - 4), P(k - 3), Gen.Balanced(ts, P(k - 5), P(k - 2), 10), 5);
			// bar 3: high exactly at the zone bottom (k)
			tape.Bar(Gen.BarStart(3), Gen.S15, P(k - 3), P(k - 1), Gen.Balanced(ts, P(k - 3), P(k), 10), 5);
			tape.Bar(Gen.BarStart(4), Gen.S15, P(k - 1), P(k - 2), Gen.Balanced(ts, P(k - 3), P(k - 1), 10), 5);

			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions { TickSize = ts, InstrumentName = "CL 12-26" }, null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			List<object> zs = Zones(sim.I).Where(z => !R.Get<bool>(z, "IsSupport")).ToList();
			if (c.Check(zs.Count == 1, "one resistance zone, got " + string.Join("; ", Zones(sim.I).Select(Describe))))
			{
				c.Eq(k, R.Get<long>(zs[0], "LowTick"), "zone low tick");
				c.Check(sim.Bars.GetHigh(3) == P(k), "bar 3 high equals the zone bottom price");
				c.Eq(3, R.Get<int>(zs[0], "EndBar"), "resistance zone frozen by bar 3 whose high equals the zone bottom (" + P(k).ToString("R") + ")");
			}
		}

		private static void PartialFirstRealtimeBar(Ctx c)
		{
			// Chart without Tick Replay and without Volumetric: the bar that is forming when realtime starts only gets the
			// realtime part of its trades. Here the passive (bid) side of bar 1 traded before the connection, the aggressive
			// asks after it: the realtime-only footprint shows asks against zero bids.
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.50, 5001.75, 5001.25, 5002.00);
			DateTime b1 = Gen.BarStart(1);
			tape.At(b1.AddSeconds(1));
			foreach (double p in new[] { 5001.00, 5000.75, 5000.50, 5000.25, 5000.00 })
				tape.Sell(p, 20, 300);
			int split = tape.Trades.Count;
			tape.At(b1.AddSeconds(8));
			foreach (double p in new[] { 5000.25, 5000.50, 5000.75, 5001.00 })
				tape.Buy(p, 20, 300);
			foreach (double p in new[] { 5001.25, 5001.50, 5001.75, 5002.00 })
			{
				tape.Sell(p, 10, 200);
				tape.Buy(p, 10, 200);
			}
			Gen.Filler(tape, 2, 5002.00, 5002.25, 5001.75, 5002.50);

			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions { TickReplay = false }, null);
			sim.RunHistoricalBars(tape.Trades.Take(split));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(split));
			List<object> zs = Zones(sim.I);
			c.Check(zs.Count == 0, "no zone from the partially collected first realtime bar (full bar is balanced: 20 bid vs 20 ask on the diagonals), got: "
				+ string.Join("; ", zs.Select(Describe)));
		}
	}
}
