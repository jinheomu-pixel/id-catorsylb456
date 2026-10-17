using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	/// <summary>Scenarios for behaviour changed after the adversarial code review.</summary>
	public static class ReviewScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("REV-01 zero opposite counts as the minimum ratio", "a stack over empty diagonal bids inside the bar is labelled '5x | volume' (vendor labels)", ZeroOpposite);
			yield return new Scenario("REV-02 bar-edge levels are not compared", "the lowest ask / highest bid of a bar has no diagonal partner and never forms an imbalance", BarEdge);
			yield return new Scenario("REV-03 market closed: last bar analysed at realtime start", "with the market closed the last loaded (complete) bar is analysed without waiting for a tick", MarketClosed);
			yield return new Scenario("REV-04 Heavy Absorption min volume hides markers", "the floor hides small markers instead of filtering prints", MinVolumeFloor);
			yield return new Scenario("REV-05 alert sound path never throws", "quoted or odd sound paths resolve without exceptions", SoundPath);
		}

		/// <summary>Bar 1 holds asks 30/20/25 at 5000.25-5000.75 over EMPTY bids one level lower (inside the bar's range).</summary>
		private static Tape ZeroOppositeTape()
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.50, 5001.75, 5001.25, 5002.00);
			List<Level> levels = new List<Level>
			{
				new Level(4999.75, 1, 3),
				new Level(5000.00, 4, 0),
				new Level(5000.25, 30, 0),
				new Level(5000.50, 20, 0),
				new Level(5000.75, 25, 6),
			};
			levels.AddRange(Gen.Balanced(0.25, 5001.00, 5003.00, 8));
			tape.Bar(Gen.BarStart(1), Gen.S15, 5000.75, 5002.00, levels, 5);
			Gen.Filler(tape, 2, 5002.00, 5001.75, 5001.25, 5002.25);
			Gen.Filler(tape, 3, 5001.75, 5001.50, 5001.25, 5002.00);
			return tape;
		}

		private static void ZeroOpposite(Ctx c)
		{
			Tape tape = ZeroOppositeTape();
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			c.Eq(IdcZeroHandling.CountAsMinRatio, sim.I.ZeroHandling, "default zero handling");
			sim.StartHistorical();
			sim.Feed(tape.Trades);

			List<object> zones = R.List(sim.I, "zones").Where(z => R.Get<int>(z, "StartBar") == 1 && R.Get<bool>(z, "IsSupport")).ToList();
			if (c.Check(zones.Count == 1, "one support zone from bar 1, got " + zones.Count))
			{
				object z = zones[0];
				c.Eq(sim.Tick(5000.25), R.Get<long>(z, "LowTick"), "zone low tick");
				c.Eq(sim.Tick(5000.75), R.Get<long>(z, "HighTick"), "zone high tick");
				c.Near(5, R.Get<double>(z, "Ratio"), 1e-9, "ratio = minimum ratio when the diagonal bid is empty");
				c.Near(75, R.Get<double>(z, "Volume"), 1e-9, "stack volume");
			}

			// the old CountAsOne behaviour turns 30 contracts over an empty bid into a 30x imbalance
			Sim<IDCImbalanceLevels> legacy = new Sim<IDCImbalanceLevels>(new SimOptions(), i => i.ZeroHandling = IdcZeroHandling.CountAsOne);
			legacy.StartHistorical();
			legacy.Feed(tape.Trades);
			object lz = R.List(legacy.I, "zones").FirstOrDefault(z => R.Get<int>(z, "StartBar") == 1 && R.Get<bool>(z, "IsSupport"));
			if (c.Check(lz != null, "CountAsOne still forms the zone"))
				c.Near(30, R.Get<double>(lz, "Ratio"), 1e-9, "CountAsOne ratio = volume / 1");
		}

		private static void BarEdge(Ctx c)
		{
			// bar 1: asks 9/9/9 on its three LOWEST levels, nothing traded below its low
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.50, 5001.75, 5001.25, 5002.00);
			List<Level> levels = new List<Level>
			{
				new Level(5000.00, 9, 0),
				new Level(5000.25, 9, 9),
				new Level(5000.50, 9, 9),
			};
			levels.AddRange(Gen.Balanced(0.25, 5000.75, 5002.50, 8));
			tape.Bar(Gen.BarStart(1), Gen.S15, 5000.50, 5002.00, levels, 5);
			Gen.Filler(tape, 2, 5002.00, 5001.75, 5001.25, 5002.25);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			int bar1Support = R.List(sim.I, "zones").Count(z => R.Get<int>(z, "StartBar") == 1 && R.Get<bool>(z, "IsSupport"));
			c.Eq(0, bar1Support, "no buy imbalance built from the bar's lowest ask");
		}

		private static void MarketClosed(Ctx c)
		{
			// history ends with the stack bar itself (bar 1); no more ticks arrive (weekend)
			Tape tape = ImbalanceScenarios.BuyStackTape();
			SimOptions o = new SimOptions { TickReplay = false, Volumetric = true };
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(o, null);
			int firstOfBar2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			sim.RunHistoricalBars(tape.Trades.Take(firstOfBar2));
			c.Eq(0, R.List(sim.I, "zones").Count, "last loaded bar is left for realtime while it may still be forming");

			sim.Bars.BarsSeries.BarsType.IsTimeBased = true;
			NinjaTrader.Core.Globals.SimNow = Gen.BarStart(2).AddDays(2);		// Saturday
			sim.GoRealtime();
			List<object> zones = R.List(sim.I, "zones");
			if (c.Check(zones.Count == 1, "zone from the complete last bar appears at realtime start, got " + zones.Count))
				c.Eq(1, R.Get<int>(zones[0], "StartBar"), "zone start bar");

			// a bar whose close time is still in the future is not touched
			Sim<IDCImbalanceLevels> open = new Sim<IDCImbalanceLevels>(o, null);
			open.RunHistoricalBars(tape.Trades.Take(firstOfBar2));
			open.Bars.BarsSeries.BarsType.IsTimeBased = true;
			NinjaTrader.Core.Globals.SimNow = Gen.BarStart(1).AddSeconds(5);	// inside bar 1
			open.GoRealtime();
			c.Eq(0, R.List(open.I, "zones").Count, "forming last bar is not analysed early");
		}

		private static void MinVolumeFloor(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 80);
			Gen.SmallPrints(tape, 151, 3, 20, 4996, 5004, new Random(12));

			Sim<IDCHeavyAbsorption> all = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			all.StartHistorical();
			all.Feed(tape.Trades);
			c.Check(AbsorptionScenarios.Marker(all.I, 150, 1) != null, "80-lot print marks bar 150");
			FakeChart chart = all.RenderLast(10);
			c.Check(chart.Ops.Any(op => op.Kind == "FillEllipse"), "circle drawn without a floor");

			Sim<IDCHeavyAbsorption> floored = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.MinVolume = 1000);
			floored.StartHistorical();
			floored.Feed(tape.Trades);
			c.Check(AbsorptionScenarios.Marker(floored.I, 150, 1) != null, "marker still detected with the floor (prints are not filtered)");
			FakeChart hidden = floored.RenderLast(10);
			c.Check(!hidden.Ops.Any(op => op.Kind == "FillEllipse"), "circle hidden below the 1000 floor");
		}

		private static void SoundPath(Ctx c)
		{
			string[] inputs = { "Alert2.wav", "\"C:\\Users\\me\\ding.wav\"", "  Alert4.wav  ", "bad|name<>.wav", "", null };
			foreach (string input in inputs)
			{
				string resolved = null;
				Exception error = null;
				try { resolved = IdcSound.Resolve(input); }
				catch (Exception e) { error = e; }
				c.Check(error == null, "no exception for " + (input ?? "null") + (error != null ? ": " + error.Message : ""));
				if (resolved != null)
					c.Check(!resolved.Contains("\""), "no quotes left in '" + resolved + "'");
			}
			c.Eq(string.Empty, IdcSound.Resolve(""), "empty sound means a silent alert");
		}
	}
}
