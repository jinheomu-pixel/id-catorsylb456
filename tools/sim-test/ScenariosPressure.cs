using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	public static class PressureScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("PTK-01 bubbles weak/strong + big trade", "dim/bright bubbles at the right levels, big print on the strong bubble", Bubbles);
			yield return new Scenario("PTK-02 absorption dot + lines", "enhanced dot above an upper wick holding heavy volume; lines end when price returns", Absorption);
			yield return new Scenario("PTK-03 psychological step", "auto step for NQ 29800, ES 6500, GC 4100, CL 62, BTC 110000", PsychStep);
			yield return new Scenario("PTK-04 psychological cross alert", "one alert per crossing, also when price rests on the level", PsychCross);
			yield return new Scenario("PTK-05 volumetric history (no Tick Replay)", "bubbles and dots from VolumetricBarsType, big trades need Tick Replay", Volumetric);
			yield return new Scenario("PTK-06 exact edge line touch, GC tick 0.1", "a bar whose high equals an absorption line price must end the line", FloatEdgeLine);
			yield return new Scenario("PTK-07 dots always come with lines", "every absorption dot has at least one absorption line (same threshold)", DotsHaveLines);
			yield return new Scenario("PTK-08 volumetric + Tick Replay", "bubbles/dots from Volumetric data, big trades from the replayed trade stream", VolumetricTickReplay);
		}

		/// <summary>Base bars 0..79: 5 levels each, ask/bid 8..12 (mean 10, sd 1.41), prints of max 3 lots.</summary>
		internal static Tape BaseTape()
		{
			Tape tape = new Tape(0.25, Gen.T0);
			for (int b = 0; b < 80; b++)
			{
				double mid = 5000.75 + ((b % 4) - 2) * 0.25;
				List<Level> lv = new List<Level>();
				for (int l = -2; l <= 2; l++)
					lv.Add(new Level(mid + l * 0.25, 8 + (b + l + 2) % 5, 8 + (b * 3 + l + 2) % 5));
				tape.Bar(Gen.BarStart(b), Gen.S15, mid, b % 2 == 0 ? mid + 0.25 : mid - 0.25, lv, 3);
			}
			return tape;
		}

		/// <summary>Bar 80: strong buy bubble 5001.00 (60 ask incl. one 30-lot print), weak buy 5000.50 (13), weak sell 5001.75 (13).
		/// Bar 81: down bar with an upper wick holding 50 and 60 lots at 5002.50 / 5002.75. Bars 82-84 stay below, bar 85 tags 5002.50.</summary>
		internal static Tape SignalTape()
		{
			Tape tape = BaseTape();
			List<Level> t80 = Gen.Balanced(0.25, 5000.00, 5002.00, 10);
			t80.First(l => l.Price == 5000.50).Ask = 13; t80.First(l => l.Price == 5000.50).Bid = 3;
			t80.First(l => l.Price == 5001.00).Ask = 30; t80.First(l => l.Price == 5001.00).Bid = 4;
			t80.First(l => l.Price == 5001.75).Ask = 2; t80.First(l => l.Price == 5001.75).Bid = 13;
			int before = tape.Trades.Count;
			tape.Bar(Gen.BarStart(80), Gen.S15, 5000.25, 5001.50, t80, 3);
			// one 30-lot buy print at 5001.00 inside bar 80
			int idx = tape.Trades.FindIndex(before, t => t.Price == 5001.00 && t.Side > 0);
			Trade big = new Trade { Time = tape.Trades[idx].Time, Price = 5001.00, Volume = 30, Side = 1, Ask = 5001.00, Bid = 5000.75 };
			tape.Trades.Insert(idx + 1, big);

			List<Level> t81 = Gen.Balanced(0.25, 5000.75, 5002.25, 10);
			t81.Add(new Level(5002.50, 25, 25));
			t81.Add(new Level(5002.75, 32, 28));
			t81.Add(new Level(5003.00, 5, 5));
			tape.Bar(Gen.BarStart(81), Gen.S15, 5001.50, 5001.00, t81, 3);
			Gen.Filler(tape, 82, 5001.00, 5001.25, 5000.50, 5001.75);
			Gen.Filler(tape, 83, 5001.25, 5001.00, 5000.75, 5001.75);
			Gen.Filler(tape, 84, 5001.00, 5001.50, 5000.75, 5001.75);
			Gen.Filler(tape, 85, 5001.50, 5002.00, 5001.25, 5002.50);
			Gen.Filler(tape, 86, 5002.00, 5001.75, 5001.50, 5002.25);
			return tape;
		}

		internal static Dictionary<int, object> Signals(object ind) { return R.IntDict(ind, "signals"); }

		private static string DescribeBubbles(object s, double ts)
		{
			return string.Join(" ", R.List(s, "Bubbles").Select(b => string.Format("[{0}{1} {2} v={3}]", R.Get<int>(b, "Side") > 0 ? "B" : "S",
				R.Get<bool>(b, "Strong") ? "!" : "", F.S(R.Get<long>(b, "Tick") * ts), F.S(R.Get<double>(b, "Volume")))));
		}

		private static void CheckBar80(Ctx c, object ind, double ts, bool expectBigTrade, string mode)
		{
			object s;
			if (!c.Check(Signals(ind).TryGetValue(80, out s), mode + ": signals for bar 80"))
				return;
			List<object> bubbles = R.List(s, "Bubbles");
			c.Note("{0} bar 80 bubbles: {1}", mode, DescribeBubbles(s, ts));
			Func<double, int, object> find = (p, side) => bubbles.FirstOrDefault(b => R.Get<long>(b, "Tick") == (long)Math.Round(p / ts) && R.Get<int>(b, "Side") == side);
			object strong = find(5001.00, 1), weakBuy = find(5000.50, 1), weakSell = find(5001.75, -1);
			if (c.Check(strong != null, mode + ": buy bubble at 5001.00"))
				c.Check(R.Get<bool>(strong, "Strong"), mode + ": 5001.00 bubble (60 lots) is strong");
			if (c.Check(weakBuy != null, mode + ": buy bubble at 5000.50"))
				c.Check(!R.Get<bool>(weakBuy, "Strong"), mode + ": 5000.50 bubble (13 lots) is weak");
			if (c.Check(weakSell != null, mode + ": sell bubble at 5001.75"))
				c.Check(!R.Get<bool>(weakSell, "Strong"), mode + ": 5001.75 bubble (13 lots) is weak");
			c.Eq(3, bubbles.Count, mode + ": bubble count on bar 80");
			List<object> bigs = R.List(s, "BigTrades");
			if (expectBigTrade)
			{
				if (c.Check(bigs.Count == 1, mode + ": one big trade on bar 80, got " + bigs.Count))
				{
					c.Eq((long)Math.Round(5001.00 / ts), R.Get<long>(bigs[0], "Tick"), mode + ": big trade price");
					c.Near(30, R.Get<double>(bigs[0], "Volume"), 1e-9, mode + ": big trade volume");
					c.Eq(1, R.Get<int>(bigs[0], "Side"), mode + ": big trade side");
				}
			}
			else
				c.Eq(0, bigs.Count, mode + ": no big trades without the trade stream");
		}

		private static void CheckBar81(Ctx c, object ind, double ts, string mode)
		{
			object s;
			if (!c.Check(Signals(ind).TryGetValue(81, out s), mode + ": signals for bar 81"))
				return;
			List<object> dots = R.List(s, "Dots");
			if (c.Check(dots.Count == 1, mode + ": one absorption dot on bar 81, got " + dots.Count))
			{
				c.Check(R.Get<bool>(dots[0], "AtHigh"), mode + ": dot above the bar (upper wick)");
				c.Check(R.Get<bool>(dots[0], "Enhanced"), mode + ": enhanced dot (60 lots >> level mean)");
			}
			List<object> lines = R.List(ind, "lines").Where(l => R.Get<int>(l, "Bar") == 81).ToList();
			c.Note("{0} lines: {1}", mode, string.Join(" ", lines.Select(l => F.S(R.Get<long>(l, "Tick") * ts) + "/end=" + R.Get<int>(l, "EndBar"))));
			object l50 = lines.FirstOrDefault(l => R.Get<long>(l, "Tick") == (long)Math.Round(5002.50 / ts));
			object l75 = lines.FirstOrDefault(l => R.Get<long>(l, "Tick") == (long)Math.Round(5002.75 / ts));
			c.Eq(2, lines.Count, mode + ": two absorption lines from bar 81");
			if (c.Check(l50 != null, mode + ": line at 5002.50"))
				c.Eq(85, R.Get<int>(l50, "EndBar"), mode + ": 5002.50 line ends at bar 85 (high 5002.50)");
			if (c.Check(l75 != null, mode + ": line at 5002.75"))
				c.Eq(-1, R.Get<int>(l75, "EndBar"), mode + ": 5002.75 line still open");
		}

		private static void Bubbles(Ctx c)
		{
			Tape tape = SignalTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			int firstOf81 = Gen.FirstAt(tape.Trades, Gen.BarStart(81));
			for (int i = 0; i < tape.Trades.Count; i++)
			{
				sim.Feed(tape.Trades[i]);
				if (i == firstOf81 - 1)
					c.Check(!Signals(sim.I).ContainsKey(80), "bar 80 not judged before it closes (historical)");
			}
			IdcBarWindowStats agg = R.Get<IdcBarWindowStats>(sim.I, "aggressiveStats");
			c.Note("aggressive stats now: mean {0:0.00} sd {1:0.00}", agg.Mean, agg.StdDev);
			CheckBar80(c, sim.I, 0.25, true, "TickReplay");
			CheckBar81(c, sim.I, 0.25, "TickReplay");

			FakeChart chart = sim.Render(70, sim.Bars.Count - 1, 4999, 5004);
			c.Check(chart.RT.Errors.Count == 0, "no render errors");
			c.Check(chart.Texts.Contains("30"), "big trade volume '30' drawn, texts: [" + string.Join(", ", chart.Texts) + "]");
			int x80 = chart.BarX(80);
			List<SharpDX.Direct2D1.DrawOp> bubbleOps = chart.Ops.Where(o => o.Kind == "FillEllipse" && Math.Abs(o.X - x80) < 0.5).ToList();
			c.Eq(3, bubbleOps.Count, "three bubbles drawn on bar 80");
			c.Check(bubbleOps.Count(o => Math.Abs(o.Color.Alpha - 0.75f) < 0.01) == 1 && bubbleOps.Count(o => Math.Abs(o.Color.Alpha - 0.30f) < 0.01) == 2,
				"one bright (75 %) and two dim (30 %) bubbles: " + string.Join(", ", bubbleOps.Select(o => o.Color.Alpha.ToString("0.00"))));
			// absorption dot above bar 81's high
			int x81 = chart.BarX(81);
			SharpDX.Direct2D1.DrawOp dot = chart.Ops.FirstOrDefault(o => o.Kind == "FillEllipse" && Math.Abs(o.X - x81) < 0.5 && o.W < 6);
			if (c.Check(dot != null, "absorption dot drawn on bar 81"))
			{
				c.Check(dot.Y < chart.Y(5003.00), "dot sits above the bar high");
				c.Check(dot.Color.Red > 0.6f && dot.Color.Green < 0.3f, "upper-wick dot is red");
			}
		}

		private static void Absorption(Ctx c)
		{
			// covered in detail by CheckBar81; here also the alert path in realtime
			Tape tape = SignalTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), i => { i.AlertAbsorptionDots = true; i.AlertAbsorptionLineTouch = true; i.AlertBubbles = true; i.AlertBigTrades = true; });
			sim.StartHistorical();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			sim.Feed(tape.Trades.Take(firstOf80));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Skip(firstOf80));
			CheckBar81(c, sim.I, 0.25, "Realtime");
			List<AlertRecord> delivered = sim.I.SimAlerts.Where(a => a.Delivered).ToList();
			c.Check(delivered.Any(a => a.Id.StartsWith("IDCPtkDot81")), "absorption dot alert for bar 81: " + string.Join(" / ", delivered));
			c.Check(delivered.Any(a => a.Id.StartsWith("IDCPtkLine")), "line retest alert");
			c.Check(delivered.Any(a => a.Id.StartsWith("IDCPtkBig80")), "big trade alert for bar 80");
			// one alert id per bubble (bar + side + price), so two same-side bubbles do not swallow each other
			c.Check(delivered.Any(a => a.Id.StartsWith("IDCPtkBubble80B")) && delivered.Any(a => a.Id.StartsWith("IDCPtkBubble80S")), "bubble alerts for bar 80 (buy and sell)");
			c.Check(delivered.Where(a => a.Id.StartsWith("IDCPtkBubble80")).Select(a => a.Id).Distinct().Count() == delivered.Count(a => a.Id.StartsWith("IDCPtkBubble80")), "bubble alert ids are unique");
		}

		private static void PsychStep(Ctx c)
		{
			var cases = new[]
			{
				new { Name = "NQ 12-26", Tick = 0.25, Price = 29800.0, Expected = 100.0, Type = InstrumentType.Future },
				new { Name = "ES 12-26", Tick = 0.25, Price = 6500.0, Expected = 25.0, Type = InstrumentType.Future },
				new { Name = "GC 12-26", Tick = 0.1, Price = 4100.0, Expected = 10.0, Type = InstrumentType.Future },
				new { Name = "CL 12-26", Tick = 0.01, Price = 62.0, Expected = 0.25, Type = InstrumentType.Future },
				new { Name = "BTC 12-26", Tick = 5.0, Price = 110000.0, Expected = 250.0, Type = InstrumentType.Future },
			};
			foreach (var k in cases)
			{
				Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { TickSize = k.Tick, InstrumentName = k.Name, InstrumentType = k.Type }, null);
				double step = (double)R.Call(sim.I, "PsychLevelStep", k.Price);
				c.Near(k.Expected, step, 1e-9, k.Name + " auto psychological step at " + k.Price);
				// the step also comes out of OnBarUpdate
				sim.StartHistorical();
				Tape tape = new Tape(k.Tick, Gen.T0);
				tape.Buy(k.Price, 1); tape.Sell(k.Price, 1);
				tape.At(Gen.BarStart(1)); tape.Buy(k.Price + k.Tick, 1);
				sim.Feed(tape.Trades);
				c.Near(k.Expected, R.Get<double>(sim.I, "psychStep"), 1e-9, k.Name + " psychStep after bars");
				FakeChart chart = sim.Render(0, 1, k.Price - k.Expected * 3, k.Price + k.Expected * 3);
				string expectedText = sim.Bars.Instrument.MasterInstrument.FormatPrice(k.Price);
				c.Check(chart.Texts.Contains(expectedText), k.Name + " level label " + expectedText + " drawn, texts: [" + string.Join(", ", chart.Texts.Take(8)) + "]");
			}
		}

		private static void PsychCross(Ctx c)
		{
			// realtime ES: 6499.00 -> 6500.00, rests on 6500.00 for 40 s, then 6500.25 -> 6501.00 ; later a clean gap cross down 6500.25 -> 6499.75
			Tape tape = new Tape(0.25, Gen.T0);
			tape.StepMs = 2000;
			foreach (double p in new[] { 6499.00, 6499.25, 6499.50, 6499.75 }) tape.Buy(p, 1);
			for (int i = 0; i < 21; i++) tape.Buy(6500.00, 1);	// 42 s at the level
			foreach (double p in new[] { 6500.25, 6500.50, 6500.75, 6501.00 }) tape.Buy(p, 1);
			tape.Wait(60);
			int gapIdx = tape.Trades.Count;
			tape.Sell(6500.25, 1); tape.Sell(6499.75, 1); tape.Sell(6499.50, 1);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), i => i.AlertPsychCross = true);
			sim.StartHistorical();
			sim.Feed(new Tape(0.25, Gen.T0.AddSeconds(-30)).Buy(6499.00, 1));
			sim.GoRealtime();
			sim.Feed(tape.Trades.Take(gapIdx));
			List<AlertRecord> up = sim.I.SimAlerts.Where(a => a.Delivered && a.Message.Contains("6500.00")).ToList();
			c.Check(up.Count == 1, "one delivered alert for the up-cross of 6500, got " + up.Count + ": " + string.Join(" / ", up));
			int before = sim.I.SimAlerts.Count(a => a.Delivered);
			sim.Feed(tape.Trades.Skip(gapIdx));
			int down = sim.I.SimAlerts.Count(a => a.Delivered) - before;
			c.Eq(1, down, "one delivered alert for the gap cross down through 6500");
		}

		private static void Volumetric(Ctx c)
		{
			Tape tape = SignalTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { TickReplay = false, Volumetric = true }, null);
			sim.RunHistoricalBars(tape.Trades);
			CheckBar80(c, sim.I, 0.25, false, "Volumetric");
			CheckBar81(c, sim.I, 0.25, "Volumetric");
			FakeChart chart = sim.Render(70, sim.Bars.Count - 1, 4999, 5004);
			c.Check(chart.Texts.Any(t => t.Contains("big trades need Tick Replay")), "warning about big trades without Tick Replay");
		}

		private static void VolumetricTickReplay(Ctx c)
		{
			Tape tape = SignalTape();
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { TickReplay = true, Volumetric = true }, null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			c.Check(R.Get<IdcFootprintEngine>(sim.I, "engine").UsesVolumetric, "engine reads Volumetric data");
			CheckBar80(c, sim.I, 0.25, true, "Volumetric+TickReplay");
			CheckBar81(c, sim.I, 0.25, "Volumetric+TickReplay");
		}

		private static void FloatEdgeLine(Ctx c)
		{
			const double ts = 0.1;
			long k = Gen.MismatchedTick(ts, 41010, 41100);
			double P(long t) { return Math.Round(t * ts, 10); }
			c.Note("GC tick {0}: ToPrice {1:R} > feed {2:R}", k, k * ts, P(k));
			// base bars around k-20..k-16, then a bar with an upper wick holding heavy volume at k
			Tape tape = new Tape(ts, Gen.T0);
			for (int b = 0; b < 80; b++)
			{
				long mid = k - 18 + (b % 4) - 2;
				List<Level> lv = new List<Level>();
				for (int l = -2; l <= 2; l++)
					lv.Add(new Level(P(mid + l), 8 + (b + l + 2) % 5, 8 + (b * 3 + l + 2) % 5));
				tape.Bar(Gen.BarStart(b), Gen.S15, P(mid), P(mid + (b % 2 == 0 ? 1 : -1)), lv, 3);
			}
			List<Level> w = Gen.Balanced(ts, P(k - 6), P(k - 1), 10);
			w.Add(new Level(P(k), 30, 30));
			w.Add(new Level(P(k + 1), 4, 4));
			tape.Bar(Gen.BarStart(80), Gen.S15, P(k - 3), P(k - 5), w, 3);
			tape.Bar(Gen.BarStart(81), Gen.S15, P(k - 5), P(k - 4), Gen.Balanced(ts, P(k - 6), P(k - 3), 10), 3);
			// bar 82: high exactly at the line price
			tape.Bar(Gen.BarStart(82), Gen.S15, P(k - 4), P(k - 2), Gen.Balanced(ts, P(k - 4), P(k), 10), 3);
			tape.Bar(Gen.BarStart(83), Gen.S15, P(k - 2), P(k - 3), Gen.Balanced(ts, P(k - 4), P(k - 2), 10), 3);
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { TickSize = ts, InstrumentName = "GC 12-26" }, null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			object line = R.List(sim.I, "lines").FirstOrDefault(l => R.Get<long>(l, "Tick") == k);
			if (c.Check(line != null, "absorption line at tick " + k))
			{
				c.Check(sim.Bars.GetHigh(82) == P(k), "bar 82 high equals the line price");
				c.Eq(82, R.Get<int>(line, "EndBar"), "line ended by bar 82 whose high equals the line price (" + P(k).ToString("R") + ")");
			}
		}

		private static void DotsHaveLines(Ctx c)
		{
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions { Capacity = 4000 }, i => i.MaxLines = 10000);
			sim.StartHistorical();
			sim.Feed(Gen.RandomMarket(0.25, Gen.T0, 3000, 40, 99, 5000));
			Dictionary<int, object> sig = Signals(sim.I);
			List<object> lines = R.List(sim.I, "lines");
			int dots = 0, orphan = 0;
			foreach (KeyValuePair<int, object> kv in sig)
			{
				int n = R.List(kv.Value, "Dots").Count;
				if (n == 0) continue;
				dots += n;
				if (!lines.Any(l => R.Get<int>(l, "Bar") == kv.Key)) orphan++;
			}
			c.Note("{0} bars with dots ({1} dots), {2} lines, {3} dot bars without any line", sig.Count(kv => R.List(kv.Value, "Dots").Count > 0), dots, lines.Count, orphan);
			c.Check(dots > 0, "random market produces absorption dots");
			c.Eq(0, orphan, "bars with an absorption dot but no absorption line");
		}
	}
}
