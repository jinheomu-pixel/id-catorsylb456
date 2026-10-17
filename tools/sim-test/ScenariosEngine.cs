using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class EngineScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("ENG-01 synthetic quotes -> tick rule", "Bid = Last, Ask = Bid + 1 tick on every print switches classification to the up/down tick rule", SyntheticDetection);
			yield return new Scenario("ENG-02 synthetic flag in realtime", "after a synthetic-quote history, realtime prints with real quotes use the quote rule", SyntheticIntoRealtime);
			yield return new Scenario("ENG-03 one-sided sell-off with real quotes", "500+ consecutive prints at the bid must not switch real-quote data to the tick rule for good", SellOff);
			yield return new Scenario("ENG-04 volumetric ticks per level = 4", "footprint from a 4-tick-per-level Volumetric chart keeps all of the bar's volume", TicksPerLevel);
			yield return new Scenario("ENG-05 realtime without stamped quotes", "realtime Last events without Ask/Bid fall back to the last Ask/Bid events", RealtimeQuoteEvents);
		}

		/// <summary>Synthetic-quote tape: a zig-zag (3 up-ticks, 3 down-ticks) so the tick rule yields half buys, half sells.</summary>
		private static Tape ZigZag(int bars, bool synthetic, DateTime start, int firstBar = 0)
		{
			Tape tape = new Tape(0.25, start) { SyntheticQuotes = synthetic };
			double p = 5000.00;
			int k = 0;
			for (int b = firstBar; b < firstBar + bars; b++)
			{
				tape.Now = Gen.BarStart(b).AddMilliseconds(100);
				for (int i = 0; i < 20; i++)
				{
					int dir = (k / 3) % 2 == 0 ? 1 : -1;
					p = tape.P(p + dir * 0.25);
					tape.Print(p, 2, dir, 600);
					k++;
				}
			}
			return tape;
		}

		private static void SyntheticDetection(Ctx c)
		{
			Tape tape = ZigZag(40, true, Gen.T0);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			int detectedAt = -1;
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (detectedAt < 0 && engine.SyntheticQuotes) detectedAt = i;
			}
			c.Check(detectedAt >= 0, "synthetic quotes detected");
			c.Note("detected after {0} prints", detectedAt + 1);
			// after detection the footprint holds tick-rule buys
			IdcFootprintBar late = engine.GetBar(38, true);
			c.Check(late != null && late.TotalAsk > 0 && late.TotalBid > 0, "bar 38 (after detection) has buys and sells: ask=" + (late == null ? 0 : late.TotalAsk) + " bid=" + (late == null ? 0 : late.TotalBid));
			// before detection
			IdcFootprintBar early = engine.GetBar(5, true);
			c.Check(early != null && early.TotalAsk > 0, "bar 5 (before detection, zig-zag of up- and down-ticks) must not be 100 % sells: ask=" + (early == null ? 0 : early.TotalAsk) + " bid=" + (early == null ? 0 : early.TotalBid));
			FakeChart chart = sim.RenderLast(20);
			c.Check(chart.Texts.Any(t => t.Contains("up/down tick rule")), "warning about the tick rule is drawn");
		}

		private static void SyntheticIntoRealtime(Ctx c)
		{
			Tape hist = ZigZag(40, true, Gen.T0);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(hist.Trades);
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			c.Check(engine.SyntheticQuotes, "synthetic quotes detected in history");
			sim.GoRealtime();
			// realtime, real quotes: the offer drops from 5000.75 to 5000.50 and a buyer lifts it at 5000.50 (a DOWN-tick buy)
			Tape rt = new Tape(0.25, Gen.BarStart(41));
			rt.Buy(5001.00, 1);
			Trade t = rt.Buy(5000.50, 7);
			sim.Feed(rt.Trades);
			IdcFootprintBar fp = engine.GetBar(sim.CurrentBar, false);
			double ask = fp == null ? 0 : fp.Ask(sim.Tick(5000.50));
			double bid = fp == null ? 0 : fp.Bid(sim.Tick(5000.50));
			c.Check(ask == 7 && bid == 0, "realtime print at the real ask is a buy (ask=" + ask + ", bid=" + bid + ", SyntheticQuotes=" + engine.SyntheticQuotes + ")");
		}

		private static void SellOff(Ctx c)
		{
			// real quotes, 1-tick spread: 600 consecutive prints hit the bid (a liquidation), then the offer drops and a
			// buyer lifts it on a DOWN-tick. The quote rule says buy, the up/down tick rule says sell.
			Tape tape = new Tape(0.25, Gen.T0);
			double p = 5100.00;
			int k = 0;
			for (int b = 0; b < 30; b++)
			{
				tape.Now = Gen.BarStart(b).AddMilliseconds(100);
				for (int i = 0; i < 20; i++)
				{
					if (k % 4 == 0) p = tape.P(p - 0.25);
					tape.Sell(p, 3, 600);
					k++;
				}
			}
			tape.At(Gen.BarStart(30).AddSeconds(1));
			tape.Sell(p, 1);
			double lifted = tape.P(p - 0.50);
			tape.Buy(lifted, 9);	// Ask = lifted, Bid = lifted - 1 tick: a real quote pattern no substitute can produce
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			IdcFootprintBar fp = engine.GetBar(30, false);
			double ask = fp == null ? 0 : fp.Ask(sim.Tick(lifted)), bid = fp == null ? 0 : fp.Bid(sim.Tick(lifted));
			c.Note("SyntheticQuotes flag after the sell-off: {0}", engine.SyntheticQuotes);
			c.Check(ask == 9 && bid == 0, "after a 600-print sell-off with real quotes, a down-tick print at the real ask is a buy (ask=" + ask + ", bid=" + bid + ", SyntheticQuotes=" + engine.SyntheticQuotes + ")");
			c.Check(!engine.SyntheticQuotes, "no 'historical ticks carry no bid/ask' warning once a print at the real ask was seen");
		}

		private static void TicksPerLevel(Ctx c)
		{
			// bar low 5000.25 (not on the 4-tick grid 5000.00/5001.00/...), high 5002.00
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.00, 5001.25, 5000.75, 5001.50);
			tape.Bar(Gen.BarStart(1), Gen.S15, 5000.50, 5001.75, Gen.Balanced(0.25, 5000.25, 5002.00, 6), 3);
			Gen.Filler(tape, 2, 5001.75, 5001.50, 5001.25, 5002.00);
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions { TickReplay = false, Volumetric = true, TicksPerLevel = 4 }, null);
			sim.RunHistoricalBars(tape.Trades);
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			c.Eq(4, engine.LevelStep, "engine level step");
			IdcFootprintBar fp = engine.GetBar(1, true);
			double expected = sim.Bars.GetVolume(1);
			c.Check(fp != null && Math.Abs(fp.TotalVolume - expected) < 1e-9, "footprint volume " + (fp == null ? 0 : fp.TotalVolume) + " equals the bar volume " + expected
				+ " (rows on the 4-tick grid: 5000.00-5000.75, 5001.00-5001.75, 5002.00-5002.75)");
		}

		private static void RealtimeQuoteEvents(Ctx c)
		{
			Tape tape = ZigZag(5, false, Gen.T0);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions { RealtimeLastHasQuotes = false }, null);
			sim.StartHistorical();
			sim.GoRealtime();
			sim.Feed(tape.Trades);
			IdcFootprintEngine engine = R.Get<IdcFootprintEngine>(sim.I, "engine");
			IdcFootprintBar fp = engine.GetBar(2, true);
			double expectedAsk = tape.Trades.Where(t => t.Time >= Gen.BarStart(2) && t.Time < Gen.BarStart(3) && t.Side > 0).Sum(t => t.Volume);
			c.Check(fp != null && fp.TotalAsk == expectedAsk, "buys classified from the Ask/Bid events: ask " + (fp == null ? 0 : fp.TotalAsk) + " vs " + expectedAsk);
		}
	}
}
