using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	/// <summary>Visual details measured in the vendor footage (realtime/fidelity audit).</summary>
	public static class FidelityScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("FID-01 imbalance label on a 2-bar frozen zone", "zone touched by the very next bar (about 0.9x the text width) is labelled, text runs past its end (reel 0 f035)", Frozen2Bar);
			yield return new Scenario("FID-02 imbalance label on a fresh zone", "zone just created (start bar + forming bar) is labelled at once (reel 0)", FreshZone);
			yield return new Scenario("FID-03 imbalance label hidden on a much narrower zone", "zone at about 0.7x the text width has no label (reel 2 z011)", NarrowHidden);
			yield return new Scenario("FID-04 imbalance label right-aligned on a wide zone", "label ends 5 px inside the zone end", WideZone);
			yield return new Scenario("FID-05 touched zones keep their fill", "Imbalance and HVN: same fill before and after the touch, only the extension stops (reels 0 and 2)", TouchedFill);
			yield return new Scenario("FID-06 HVN developing zones hidden by default", "running period: no zones until it ends (reel 2 f015/f016), optional setting draws them", DevelopingZones);
			yield return new Scenario("FID-07 Heavy Absorption thousands with capital K", "circle text '1.2K' as in the ETH reel ('9.1K', '1K')", CapitalK);
		}

		private static FakeChart RenderWidth(Sim<IDCImbalanceLevels> sim, int panelW)
		{
			sim.Chart = new FakeChart(sim.Bars);
			sim.Chart.Panel.W = panelW;
			return sim.Render(0, sim.Bars.Count - 1, 4999.00, 5004.00);
		}

		private static void LabelCase(Ctx c, Sim<IDCImbalanceLevels> sim, int panelW, bool expect)
		{
			const string label = "12.5x | 165";
			FakeChart chart = RenderWidth(sim, panelW);
			SharpDX.Direct2D1.DrawOp rect = chart.Ops.FirstOrDefault(o => o.Kind == "FillRectangle");
			SharpDX.Direct2D1.DrawOp text = chart.Ops.FirstOrDefault(o => o.Kind == "Text" && o.Text == label);
			c.Note("zone x {0}..{1} ({2} px); text {3}", rect != null ? F.S(rect.X) : "-", rect != null ? F.S(rect.X + rect.W) : "-", rect != null ? F.S(rect.W) : "-",
				text != null ? F.S(text.X) + ".." + F.S(text.X + text.W) + " (" + F.S(text.W) + " px)" : "not drawn");
			if (!expect)
			{
				c.Check(text == null, "label hidden");
				return;
			}
			if (c.Check(text != null && rect != null, "label drawn"))
			{
				if (text.W > rect.W - 8)
					c.Near(rect.X + 3, text.X, 0.6, "wider than the zone: starts 3 px inside its left edge");
				else
					c.Near(rect.X + rect.W - 5, text.X + text.W, 0.6, "fits: right-aligned 5 px inside the zone end");
			}
		}

		private static Sim<IDCImbalanceLevels> FrozenSim()
		{
			Tape tape = ImbalanceScenarios.BuyStackTape(bar2Touches: true);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			return sim;
		}

		private static void Frozen2Bar(Ctx c)
		{
			Sim<IDCImbalanceLevels> sim = FrozenSim();
			List<object> zs = ImbalanceScenarios.Zones(sim.I);
			c.Check(zs.Count == 1 && R.Get<int>(zs[0], "EndBar") == 2, "zone frozen at bar 2 by the very next bar");
			LabelCase(c, sim, 348, true);		// 2-bar zone 56 px vs text 60.5 px
		}

		private static void FreshZone(Ctx c)
		{
			Tape tape = ImbalanceScenarios.BuyStackTape();
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			int firstOfBar2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			for (int i = 0; i <= firstOfBar2; i++)
				sim.Feed(tape.Trades[i]);
			c.Check(ImbalanceScenarios.Zones(sim.I).Count == 1, "zone exists at the first tick of bar 2");
			LabelCase(c, sim, 278, true);
		}

		private static void NarrowHidden(Ctx c)
		{
			LabelCase(c, FrozenSim(), 260, false);	// 2-bar zone 41.6 px vs text 60.5 px
		}

		private static void WideZone(Ctx c)
		{
			// the BuyStackTape stack bar, then five bars that stay above the zone: a zone several bars wide
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.Filler(tape, 0, 5001.50, 5001.75, 5001.25, 5002.00);
			List<Level> stack = new List<Level>
			{
				new Level(5000.00, 2, 8), new Level(5000.25, 50, 6), new Level(5000.50, 75, 5), new Level(5000.75, 40, 3), new Level(5001.00, 10, 9),
			};
			stack.AddRange(Gen.Balanced(0.25, 5001.25, 5003.00, 8));
			tape.Bar(Gen.BarStart(1), Gen.S15, 5000.75, 5002.00, stack, 5);
			for (int b = 2; b < 7; b++)
				Gen.Filler(tape, b, 5002.00, 5001.75, 5001.25, 5002.25);
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			FakeChart chart = RenderWidth(sim, 700);
			SharpDX.Direct2D1.DrawOp rect = chart.Ops.FirstOrDefault(o => o.Kind == "FillRectangle");
			SharpDX.Direct2D1.DrawOp text = chart.Ops.FirstOrDefault(o => o.Kind == "Text" && o.Text == "12.5x | 165");
			c.Note("zone {0} px wide, text {1} px", rect != null ? F.S(rect.W) : "-", text != null ? F.S(text.W) : "-");
			if (c.Check(rect != null && text != null && rect.W > 2 * text.W, "zone much wider than its label"))
				c.Near(rect.X + rect.W - 5, text.X + text.W, 0.6, "label ends 5 px inside the zone end");
		}

		private static float FillAlpha(FakeChart chart, Func<SharpDX.Direct2D1.DrawOp, bool> where)
		{
			SharpDX.Direct2D1.DrawOp op = chart.Ops.Where(o => o.Kind == "FillRectangle").Where(where).OrderByDescending(o => o.W).FirstOrDefault();
			return op == null ? -1 : op.Color.Alpha;
		}

		private static void TouchedFill(Ctx c)
		{
			// Imbalance: zone from bar 1 active at bar 2, touched in bar 3
			Tape tape = ImbalanceScenarios.BuyStackTape();
			Sim<IDCImbalanceLevels> imb = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			imb.StartHistorical();
			imb.Feed(tape.Trades.Take(Gen.FirstAt(tape.Trades, Gen.BarStart(3))));
			float active = FillAlpha(imb.Render(0, imb.Bars.Count - 1, 4999.00, 5004.00), o => true);
			imb.Feed(tape.Trades.Skip(Gen.FirstAt(tape.Trades, Gen.BarStart(3))));
			c.Check(R.Get<int>(ImbalanceScenarios.Zones(imb.I)[0], "EndBar") == 3, "imbalance zone touched at bar 3");
			float touched = FillAlpha(imb.Render(0, imb.Bars.Count - 1, 4999.00, 5004.00), o => true);
			c.Note("imbalance fill alpha active {0}, touched {1}", F.S(active), F.S(touched));
			c.Check(active > 0 && Math.Abs(active - touched) < 1e-4, "imbalance: touched zone keeps its fill");

			// HVN: P2 primary zone (5022) touched at bar 63
			Dictionary<long, long> p1;
			Tape t2 = HvnScenarios.FourPeriods(out p1);
			Sim<IDCHvnProfile> hvn = new Sim<IDCHvnProfile>(new SimOptions(), i => i.ShowHistogram = false);
			hvn.StartHistorical();
			hvn.Feed(t2.Trades.Take(Gen.FirstAt(t2.Trades, Gen.BarStart(62))));
			FakeChart before = hvn.Render(0, hvn.Bars.Count - 1, 5000, 5052);
			int y = before.Y(5022.00);
			Func<SharpDX.Direct2D1.DrawOp, bool> atNode = o => o.Y <= y && o.Y + o.H >= y && o.X < before.BarX(25);
			float hActive = FillAlpha(before, atNode);
			hvn.Feed(t2.Trades.Skip(Gen.FirstAt(t2.Trades, Gen.BarStart(62))).Take(Gen.FirstAt(t2.Trades, Gen.BarStart(66)) - Gen.FirstAt(t2.Trades, Gen.BarStart(62))));
			object z = HvnScenarios.Zones(hvn.I).FirstOrDefault(q => R.Get<int>(q, "StartBar") == 20 && R.Get<long>(q, "PocTick") == hvn.Tick(5022.00));
			c.Check(z != null && R.Get<int>(z, "EndBar") >= 0, "HVN 5022 zone touched");
			FakeChart after = hvn.Render(0, hvn.Bars.Count - 1, 5000, 5052);
			float hTouched = FillAlpha(after, atNode);
			c.Note("HVN fill alpha active {0}, touched {1}", F.S(hActive), F.S(hTouched));
			c.Check(hActive > 0 && Math.Abs(hActive - hTouched) < 1e-4, "HVN: touched zone keeps its fill");
		}

		private static void DevelopingZones(Ctx c)
		{
			Dictionary<long, long> p1;
			Tape tape = HvnScenarios.FourPeriods(out p1);
			int firstOf20 = Gen.FirstAt(tape.Trades, Gen.BarStart(20));
			int firstOf32 = Gen.FirstAt(tape.Trades, Gen.BarStart(32));
			int fills = -1, fillsOn = -1, textsOff = -1, textsOn = -1;
			foreach (bool show in new[] { false, true })
			{
				Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), i => { i.ShowDevelopingZones = show; i.ShowHistogram = false; });
				if (!show)
					c.Check(!sim.I.ShowDevelopingZones, "developing zones off by default");
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(firstOf20));
				sim.GoRealtime();
				sim.Feed(tape.Trades.Skip(firstOf20).Take(firstOf32 - firstOf20));
				c.Check(R.List(sim.I, "liveZones").Count > 0, "developing clusters are computed (show=" + show + ")");
				FakeChart chart = sim.Render(18, sim.Bars.Count - 1, 5018, 5032);
				int y = chart.Y(5022.00);
				int n = chart.Ops.Count(o => o.Kind == "FillRectangle" && o.Y <= y && o.Y + o.H >= y && o.X >= chart.BarX(20) - 15);
				if (show) fillsOn = n; else fills = n;
				int texts = chart.Ops.Count(o => o.Kind == "Text" && o.X >= chart.BarX(20) - 15);
				if (show) textsOn = texts; else textsOff = texts;
			}
			c.Eq(0, fills, "no zone drawn for the running period by default");
			c.Check(fillsOn > 0, "the option draws the developing zone (" + fillsOn + " fills)");
			c.Eq(textsOff, textsOn, "developing zones are drawn without labels");
		}

		private static void CapitalK(Ctx c)
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 1200);
			Gen.SmallPrints(tape, 151, 2, 20, 4996, 5004, new Random(12));
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades);
			FakeChart chart = sim.RenderLast(6);
			c.Check(chart.Texts.Contains("1.2K"), "circle text '1.2K', texts: [" + string.Join(", ", chart.Texts) + "]");
			c.Eq("1.2k", IdcDraw.Volume(1200), "imbalance/HVN labels keep the lower-case k of the vendor page ('1.2k')");
		}
	}
}
