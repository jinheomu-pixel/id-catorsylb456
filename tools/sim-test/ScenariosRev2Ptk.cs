using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	/// <summary>Second adversarial review of IDCPressureToolkit: each scenario states the expected (spec) behaviour.</summary>
	public static class Rev2PtkScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("R2P-01 live preview includes the latest print", "realtime forming bar: preview after the print that makes a strong bubble (prints 260 ms apart in wall time)", LivePreviewLag);
			yield return new Scenario("R2P-02 live preview after a burst + quote updates", "burst of buys within 100 ms, then only quote updates: preview must show the bubble", LivePreviewBurst);
			yield return new Scenario("R2P-03 no stale bar alerts after the daily break", "realtime across 16:00-17:00: bar ending 16:00 must not alert at 17:00", StaleAfterBreak);
			yield return new Scenario("R2P-04 volumetric 4 ticks/level: one-row bar", "a bar that sits inside one footprint row has no resolvable wick: no absorption dot", OneRowBar);
			yield return new Scenario("R2P-05 volumetric 4 ticks/level: dot side bias", "random market: lower-wick dots vs upper-wick dots, ticks/level 1 vs 4", DotBias);
			yield return new Scenario("R2P-06 volumetric big trade inside the bubble's row", "4 ticks/level: 30-lot print in the bubble's row is a big trade", RowProximity);
			yield return new Scenario("R2P-07 big trades with bubbles hidden", "ShowBubbles = false must not remove big trades", BigTradesHiddenBubbles);
			yield return new Scenario("R2P-08 uniform print sizes", "all prints 1 lot: no print is unusually large", UniformPrints);
			yield return new Scenario("R2P-09 psych step on Day bars", "Day bars: auto step follows the current price", DayBars);
			yield return new Scenario("R2P-10 psych step is a tick multiple", "stock at 7.80 (tick 0.01): auto step must be tradeable", StockStep);
			yield return new Scenario("R2P-11 psych levels in any visible range", "scrolled back / zoomed out: every round level in view is drawn", ScrolledBack);
			yield return new Scenario("R2P-12 BTC tick 0.01, 100 ticks/level, wide bar", "a 250-row Volumetric bar still has a footprint (bubble)", WideCryptoBar);
			yield return new Scenario("R2P-13 gap through an absorption line", "a session-open gap across an open line ends it", GapThroughLine);
			yield return new Scenario("R2P-14 dominance ratio 1", "ask == bid at a level: not both a buy and a sell bubble", DominanceOne);
		}

		private static List<object> LiveBubbles(object ind)
		{
			object live = R.Get(ind, "liveSignals");
			return live == null ? new List<object>() : R.List(live, "Bubbles");
		}

		private static void LivePreviewLag(Ctx c)
		{
			Tape tape = PressureScenarios.BaseTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			sim.GoRealtime();
			Tape rt = new Tape(0.25, Gen.BarStart(80).AddMilliseconds(5));
			rt.StepMs = 1000;
			rt.Buy(5000.75, 1);
			for (int i = 0; i < 5; i++) rt.Buy(5001.00, 3);		// 15 lots >= strong (mean 10 + 3 * 1.41)
			sim.AfterTrade = t => Thread.Sleep(260);				// defeat the 250 ms wall-clock throttle
			sim.Feed(rt.Trades);
			sim.AfterTrade = null;
			IdcFootprintEngine eng = R.Get<IdcFootprintEngine>(sim.I, "engine");
			IdcBarWindowStats agg = R.Get<IdcBarWindowStats>(sim.I, "aggressiveStats");
			double ask = eng.GetBar(80, false).Ask(sim.Tick(5001.00));
			List<object> bubbles = LiveBubbles(sim.I);
			c.Note("footprint ask at 5001.00 = {0}; weak {1:0.00}, strong {2:0.00}; live bubbles: {3}", ask, agg.Threshold(1.5), agg.Threshold(3.0),
				string.Join(" ", bubbles.Select(b => R.Get<long>(b, "Tick") + "/" + R.Get<double>(b, "Volume") + (R.Get<bool>(b, "Strong") ? "!" : ""))));
			c.Check(bubbles.Any(b => R.Get<long>(b, "Tick") == sim.Tick(5001.00) && R.Get<bool>(b, "Strong")),
				"live preview shows the strong bubble the forming bar already has (footprint ask " + ask + ")");
		}

		private static void LivePreviewBurst(Ctx c)
		{
			Tape tape = PressureScenarios.BaseTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			sim.GoRealtime();
			Tape rt = new Tape(0.25, Gen.BarStart(80).AddMilliseconds(5));
			rt.Buy(5000.75, 1);
			rt.Wait(2);
			for (int i = 0; i < 20; i++) rt.Buy(5001.00, 3, 5);
			sim.Feed(rt.Trades);
			// one second later: only level-1 quote updates (no trade) - the normal state of a quiet bar
			DateTime q = rt.Now.AddSeconds(1);
			NinjaTrader.Core.Globals.SimNow = q;
			Thread.Sleep(300);
			R.Call(sim.I, "OnMarketData", new MarketDataEventArgs { MarketDataType = MarketDataType.Bid, Price = 5000.75, Volume = 12, Time = q, Instrument = sim.Bars.Instrument });
			R.Call(sim.I, "OnMarketData", new MarketDataEventArgs { MarketDataType = MarketDataType.Ask, Price = 5001.25, Volume = 9, Time = q, Instrument = sim.Bars.Instrument });
			List<object> bubbles = LiveBubbles(sim.I);
			c.Note("live bubbles after burst + quotes: {0}", bubbles.Count);
			c.Check(bubbles.Any(b => R.Get<long>(b, "Tick") == sim.Tick(5001.00) && R.Get<bool>(b, "Strong")), "live preview shows the 60-lot strong buy bubble");
		}

		private static Trade Shifted(Trade t, TimeSpan d)
		{
			return new Trade { Time = t.Time + d, Price = t.Price, Volume = t.Volume, Side = t.Side, Ask = t.Ask, Bid = t.Bid };
		}

		/// <summary>SignalTape bars 0..lastBar shifted so that lastBar ends at 16:00 (CME session end), then filler bars from 17:00.</summary>
		private static List<Trade> SessionBreakTape(int lastBar, double nextOpen, double nextLo, double nextHi, out DateTime sessionEnd, out DateTime nextStart)
		{
			Tape src = PressureScenarios.SignalTape();
			sessionEnd = new DateTime(2026, 3, 2, 16, 0, 0);
			nextStart = new DateTime(2026, 3, 2, 17, 0, 0);
			TimeSpan shift = sessionEnd - Gen.BarStart(lastBar + 1);
			List<Trade> trades = src.Trades.Where(t => t.Time < Gen.BarStart(lastBar + 1)).Select(t => Shifted(t, shift)).ToList();
			Tape next = new Tape(0.25, nextStart);
			for (int k = 0; k < 3; k++)
				next.Bar(nextStart.AddSeconds(15 * k), Gen.S15, k == 0 ? nextOpen : nextLo, nextHi, Gen.Balanced(0.25, nextLo, nextHi, 10), 5);
			trades.AddRange(next.Trades);
			return trades;
		}

		private static void StaleAfterBreak(Ctx c)
		{
			DateTime end, next;
			List<Trade> trades = SessionBreakTape(81, 5001.00, 5000.75, 5001.50, out end, out next);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), i => { i.AlertBubbles = true; i.AlertAbsorptionDots = true; i.AlertBigTrades = true; });
			sim.StartHistorical();
			DateTime rtStart = end - TimeSpan.FromSeconds(30);	// bars 80 and 81 trade in realtime
			sim.Feed(trades.Where(t => t.Time < rtStart));
			sim.GoRealtime();
			sim.Feed(trades.Where(t => t.Time >= rtStart));
			c.Check(sim.Bars.IsFirstBarOfSessionByIndex(82), "bar 82 opens the next session");
			foreach (AlertRecord a in sim.I.SimAlerts.Where(x => x.Delivered))
				c.Note("alert {0}", a);
			List<AlertRecord> stale = sim.I.SimAlerts.Where(a => a.Delivered && a.Time >= next).ToList();
			c.Check(stale.Count == 0, "no alert for a bar of the previous session at the next session's open: " + string.Join(" / ", stale));
			c.Check(sim.I.SimAlerts.Any(a => a.Delivered && a.Id.StartsWith("IDCPtkBubble80")), "bar 80 (closed in session) still alerts");
		}

		/// <summary>80 Volumetric bars of 12 ticks (rows 5000.00 / 5001.00 / 5002.00 at 4 ticks per level), ask/bid 2..4 per tick.</summary>
		private static Tape RowBaseTape()
		{
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 80; b++)
			{
				List<Level> lv = new List<Level>();
				for (int l = 0; l < 12; l++)
					lv.Add(new Level(5000.00 + l * 0.25, 2 + (b + l) % 3, 2 + (b * 2 + l) % 3));
				tape.Bar(Gen.BarStart(b), Gen.S15, 5000.50, b % 2 == 0 ? 5002.25 : 5000.75, lv, 3);
			}
			return tape;
		}

		private static void OneRowBar(Ctx c)
		{
			Tape tape = RowBaseTape();
			// bar 80: whole bar inside the 5000.00 row (5000.00-5000.75), body 5000.25 -> 5000.50
			tape.Bar(Gen.BarStart(80), Gen.S15, 5000.25, 5000.50, Gen.Balanced(0.25, 5000.00, 5000.75, 10), 3);
			// bar 81 (control): heavy row 5000.00 entirely below a body in row 5001.00 (open 5001.00, close 5001.75)
			List<Level> l81 = Gen.Balanced(0.25, 5000.00, 5000.75, 10);
			l81.AddRange(Gen.Balanced(0.25, 5001.00, 5001.75, 10));
			tape.Bar(Gen.BarStart(81), Gen.S15, 5001.00, 5001.75, l81, 3);
			Gen.Filler(tape, 82, 5001.75, 5001.50, 5001.25, 5002.00);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { Volumetric = true, TickReplay = false, TicksPerLevel = 4 }, null);
			sim.RunHistoricalBars(tape.Trades);
			Dictionary<int, object> sig = PressureScenarios.Signals(sim.I);
			Func<int, string> dots = bar => { object s; return sig.TryGetValue(bar, out s) ? string.Join(" ", R.List(s, "Dots").Select(d => (R.Get<bool>(d, "AtHigh") ? "H" : "L") + (R.Get<bool>(d, "Enhanced") ? "!" : ""))) : ""; };
			c.Note("bar 80 (one row) dots: [{0}], bar 81 (row below body) dots: [{1}]", dots(80), dots(81));
			c.Note("bar 80 lines: {0}", R.List(sim.I, "lines").Count(l => R.Get<int>(l, "Bar") == 80));
			c.Eq("", dots(80), "bar 80 (open/close/high/low in one footprint row) has no absorption dot");
			c.Eq(0, R.List(sim.I, "lines").Count(l => R.Get<int>(l, "Bar") == 80), "no absorption line from the one-row bar");
			c.Check(dots(81).Contains("L"), "control: a full row below the body still gives a lower dot");
		}

		private static void DotBias(Ctx c)
		{
			List<Trade> trades = Gen.RandomMarket(0.25, Gen.T0, 3000, 40, 31, 5000).ToList();
			foreach (int tpl in new[] { 1, 4 })
			{
				Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { Volumetric = true, TickReplay = true, TicksPerLevel = tpl, Capacity = 4000 }, i => i.MaxLines = 10000);
				sim.StartHistorical();
				sim.Feed(trades);
				Dictionary<int, object> sig = PressureScenarios.Signals(sim.I);
				int hi = sig.Values.Sum(s => R.List(s, "Dots").Count(d => R.Get<bool>(d, "AtHigh")));
				int lo = sig.Values.Sum(s => R.List(s, "Dots").Count(d => !R.Get<bool>(d, "AtHigh")));
				c.Note("ticks/level {0}: upper-wick dots {1}, lower-wick dots {2}, ratio {3:0.00}", tpl, hi, lo, lo / Math.Max(1.0, hi));
				double ratio = lo / Math.Max(1.0, hi);
				c.Check(ratio > 0.6 && ratio < 1.6, "ticks/level " + tpl + ": symmetric random market gives balanced upper/lower dots (lower/upper = " + ratio.ToString("0.00") + ")");
			}
		}

		private static void RowProximity(Ctx c)
		{
			SimOptions o = new SimOptions { Volumetric = true, TickReplay = true, TicksPerLevel = 4 };
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 80; b++)
			{
				List<Level> lv = new List<Level>();
				for (int l = 0; l < 8; l++)
					lv.Add(new Level(5000.00 + l * 0.25, 2 + (b + l) % 3, 2 + (b * 3 + l) % 3));
				tape.Bar(Gen.BarStart(b), Gen.S15, 5000.50, b % 2 == 0 ? 5001.00 : 5000.75, lv, 3);
			}
			List<Level> t80 = new List<Level>();
			for (int l = 0; l < 8; l++) t80.Add(new Level(5000.00 + l * 0.25, 3, 3));
			t80[4].Ask = 20; t80[5].Ask = 20; t80[6].Ask = 20; t80[7].Ask = 20;
			int before = tape.Trades.Count;
			tape.Bar(Gen.BarStart(80), Gen.S15, 5000.50, 5001.25, t80, 3);
			int idx = tape.Trades.FindIndex(before, t => t.Price == 5001.75 && t.Side > 0);
			tape.Trades.Insert(idx + 1, new Trade { Time = tape.Trades[idx].Time, Price = 5001.75, Volume = 30, Side = 1, Ask = 5001.75, Bid = 5001.50 });
			Gen.Filler(tape, 81, 5001.25, 5001.00, 5000.50, 5001.50);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(o, null);
			sim.StartHistorical(); sim.Feed(tape.Trades);
			object s;
			if (!c.Check(PressureScenarios.Signals(sim.I).TryGetValue(80, out s), "signals on bar 80"))
				return;
			c.Note("bubbles: {0}", string.Join(" ", R.List(s, "Bubbles").Select(b => (R.Get<int>(b, "Side") > 0 ? "B" : "S") + R.Get<long>(b, "Tick") + " v=" + R.Get<double>(b, "Volume"))));
			c.Check(R.List(s, "Bubbles").Any(b => R.Get<int>(b, "Side") > 0 && R.Get<long>(b, "Tick") == 20004), "buy bubble on the 5001.00 row");
			c.Eq(1, R.List(s, "BigTrades").Count, "30-lot print at 5001.75 (inside the bubble's 5001.00-5001.75 row) is a big trade");
		}

		private static void BigTradesHiddenBubbles(Ctx c)
		{
			Tape tape = PressureScenarios.SignalTape();
			Sim<IDCPressureToolkit> on = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			on.StartHistorical(); on.Feed(tape.Trades);
			Sim<IDCPressureToolkit> off = new Sim<IDCPressureToolkit>(new SimOptions(), i => i.ShowBubbles = false);
			off.StartHistorical(); off.Feed(tape.Trades);
			Func<Sim<IDCPressureToolkit>, int> bigs = s => { object v; return PressureScenarios.Signals(s.I).TryGetValue(80, out v) ? R.List(v, "BigTrades").Count : 0; };
			c.Note("big trades on bar 80: bubbles shown {0}, bubbles hidden {1}", bigs(on), bigs(off));
			c.Eq(bigs(on), bigs(off), "big trades on bar 80 do not depend on whether bubbles are drawn");
		}

		private static void UniformPrints(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 80; b++)
			{
				double mid = 5000.75 + ((b % 4) - 2) * 0.25;
				List<Level> lv = new List<Level>();
				for (int l = -2; l <= 2; l++)
					lv.Add(new Level(mid + l * 0.25, 8 + (b + l + 2) % 5, 8 + (b * 3 + l + 2) % 5));
				tape.Bar(Gen.BarStart(b), Gen.S15, mid, b % 2 == 0 ? mid + 0.25 : mid - 0.25, lv, 1);
			}
			List<Level> t80 = Gen.Balanced(0.25, 5000.00, 5002.00, 10);
			t80.First(l => l.Price == 5001.00).Ask = 40; t80.First(l => l.Price == 5001.00).Bid = 4;
			tape.Bar(Gen.BarStart(80), Gen.S15, 5000.25, 5001.50, t80, 1);
			Gen.Filler(tape, 81, 5001.50, 5001.25, 5001.00, 5001.75);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical(); sim.Feed(tape.Trades);
			object s;
			int bigs = PressureScenarios.Signals(sim.I).TryGetValue(80, out s) ? R.List(s, "BigTrades").Count : 0;
			IdcBarWindowStats ps = R.Get<IdcBarWindowStats>(sim.I, "printStats");
			c.Note("print stats mean {0} sd {1}; big trades on bar 80: {2}", ps.Mean, ps.StdDev, bigs);
			c.Eq(0, bigs, "no big trade when every print is 1 lot");
		}

		private static void DayBars(Ctx c)
		{
			SimOptions o = new SimOptions { PeriodType = BarsPeriodType.Day, PeriodValue = 1, TickReplay = false };
			Tape tape = new Tape(0.25, new DateTime(2025, 1, 6, 10, 0, 0));
			int n = 300;
			for (int d = 0; d < n; d++)
			{
				double p = 2000 + (6500 - 2000) * d / (double)(n - 1);
				tape.At(new DateTime(2025, 1, 6, 10, 0, 0).AddDays(d));
				tape.Buy(p, 1); tape.Sell(p + 1, 1); tape.Buy(p + 0.5, 1);
			}
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(o, null);
			sim.RunHistoricalBars(tape.Trades);
			double step = R.Get<double>(sim.I, "psychStep");
			double expected = (double)R.Call(sim.I, "PsychLevelStep", sim.Bars.GetClose(sim.Bars.Count - 1));
			c.Note("last close {0}, psychStep {1}, step for last close {2}", sim.Bars.GetClose(sim.Bars.Count - 1), step, expected);
			c.Near(expected, step, 1e-9, "psychological step follows the current price on a Day chart");
		}

		private static void StockStep(Ctx c)
		{
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { TickSize = 0.01, InstrumentName = "F", InstrumentType = InstrumentType.Stock }, null);
			foreach (double price in new[] { 6.10, 7.80, 11.50 })
			{
				double step = (double)R.Call(sim.I, "PsychLevelStep", price);
				double ticks = step / 0.01;
				c.Note("step at {0}: {1} ({2} ticks)", price, step, ticks);
				c.Check(Math.Abs(ticks - Math.Round(ticks)) < 1e-6, "auto step at " + price + " is a whole number of ticks (got " + step + ")");
			}
		}

		private static void ScrolledBack(Ctx c)
		{
			// 60 bars at ES 5000, then the market moves to 5400 (current price)
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 60; b++)
				Gen.Filler(tape, b, 5000.00, 5000.25, 4999.75, 5000.50);
			for (int b = 60; b < 70; b++)
				Gen.Filler(tape, b, 5400.00, 5400.25, 5399.75, 5400.50);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical(); sim.Feed(tape.Trades);
			double step = R.Get<double>(sim.I, "psychStep");
			c.Note("psych step {0}, last price {1}", step, R.Get<double>(sim.I, "lastPrice"));
			// user scrolls back to bars 0..59 (prices around 5000)
			FakeChart chart = sim.Render(0, 59, 4990, 5010);
			Func<FakeChart, double, bool> drawn = (ch, lvl) => ch.Ops.Any(op => op.Kind == "DrawLine" && op.X == ch.Panel.X && op.W == ch.Panel.X + ch.Panel.W && Math.Abs(op.Y - ch.Y(lvl)) < 0.5);
			c.Check(drawn(chart, 5000), "scrolled-back view 4990-5010: the 5000 round level is drawn");
			// zoomed out over the whole move: every multiple of the step in view
			FakeChart wide = sim.Render(0, 69, 4980, 5420);
			List<double> missing = new List<double>();
			for (double l = Math.Ceiling(4980 / step) * step; l <= 5420; l += step)
				if (!drawn(wide, l)) missing.Add(l);
			c.Note("zoomed-out view 4980-5420 (more than 'levels each side' round levels): not drawn {0}", string.Join(", ", missing));
		}

		private static void WideCryptoBar(Ctx c)
		{
			SimOptions o = new SimOptions { TickSize = 0.01, InstrumentName = "BTCUSD", InstrumentType = InstrumentType.CryptoCurrency, Volumetric = true, TickReplay = false, TicksPerLevel = 100 };
			Tape tape = new Tape(0.01, Gen.T0);
			for (int b = 0; b < 80; b++)
			{
				List<Level> lv = new List<Level>();
				for (int l = 0; l < 5; l++)
					lv.Add(new Level(110000.00 + l, 8 + (b + l) % 5, 8 + (b * 3 + l) % 5));
				tape.Bar(Gen.BarStart(b), Gen.S15, 110002.00, b % 2 == 0 ? 110003.00 : 110001.00, lv, 3);
			}
			// bar 80: $250 range = 25000 ticks = 250 rows of 100 ticks; strong buying in row 110100
			List<Level> t80 = new List<Level>();
			for (int l = 0; l <= 250; l++)
				t80.Add(new Level(110000.00 + l, 10, 10));
			t80[100].Ask = 60; t80[100].Bid = 4;
			tape.Bar(Gen.BarStart(80), Gen.S15, 110050.00, 110200.00, t80, 5);
			List<Level> t81 = new List<Level>();
			for (int l = 0; l < 5; l++) t81.Add(new Level(110200.00 + l, 10, 10));
			tape.Bar(Gen.BarStart(81), Gen.S15, 110201.00, 110202.00, t81, 5);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(o, null);
			sim.RunHistoricalBars(tape.Trades);
			IdcFootprintEngine eng = R.Get<IdcFootprintEngine>(sim.I, "engine");
			IdcFootprintBar fp = eng.GetBar(80, true);
			c.Note("bar 80: {0} ticks, footprint {1}", sim.Tick(sim.Bars.GetHigh(80)) - sim.Tick(sim.Bars.GetLow(80)), fp == null ? "null" : fp.LevelCount + " rows");
			c.Check(fp != null, "footprint exists for a 250-row Volumetric bar");
			object s;
			bool has = PressureScenarios.Signals(sim.I).TryGetValue(80, out s) && R.List(s, "Bubbles").Any(b => R.Get<int>(b, "Side") > 0 && R.Get<bool>(b, "Strong"));
			c.Check(has, "strong buy bubble on the wide BTC bar");
		}

		private static void GapThroughLine(Ctx c)
		{
			// SignalTape up to bar 86 (line 5002.75 from bar 81 still open), session ends; next session opens at 5003.50 (above the line)
			DateTime end, next;
			List<Trade> trades = SessionBreakTape(86, 5003.50, 5003.25, 5004.00, out end, out next);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(trades);
			object line = R.List(sim.I, "lines").FirstOrDefault(l => R.Get<int>(l, "Bar") == 81 && R.Get<long>(l, "Tick") == sim.Tick(5002.75));
			if (!c.Check(line != null, "line 5002.75 from bar 81"))
				return;
			c.Note("bar 86 close {0}, bar 87 range {1}-{2}, line end bar {3}", sim.Bars.GetClose(86), sim.Bars.GetLow(87), sim.Bars.GetHigh(87), R.Get<int>(line, "EndBar"));
			c.Eq(87, R.Get<int>(line, "EndBar"), "price gapped from 5001.75 to 5003.50 across the 5002.75 line: line ends at bar 87");
		}

		private static void DominanceOne(Ctx c)
		{
			Tape tape = PressureScenarios.BaseTape();
			List<Level> t80 = Gen.Balanced(0.25, 5000.00, 5002.00, 10);
			t80.First(l => l.Price == 5001.00).Ask = 20; t80.First(l => l.Price == 5001.00).Bid = 20;
			tape.Bar(Gen.BarStart(80), Gen.S15, 5000.25, 5001.50, t80, 3);
			Gen.Filler(tape, 81, 5001.50, 5001.25, 5001.00, 5001.75);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), i => i.BubbleDominance = 1);
			sim.StartHistorical(); sim.Feed(tape.Trades);
			object s;
			List<object> at = PressureScenarios.Signals(sim.I).TryGetValue(80, out s) ? R.List(s, "Bubbles").Where(b => R.Get<long>(b, "Tick") == sim.Tick(5001.00)).ToList() : new List<object>();
			c.Note("bubbles at 5001.00 (ask 20 / bid 20): {0}", string.Join(" ", at.Select(b => R.Get<int>(b, "Side") > 0 ? "B" : "S")));
			c.Check(at.Count <= 1, "one level cannot be both buy and sell pressure");
		}
	}
}
