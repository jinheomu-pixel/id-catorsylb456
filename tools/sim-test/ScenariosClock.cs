using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.IDCator;

namespace SimTest
{
	/// <summary>
	/// Clocks: Playback of a past day (Core.Globals.Now is the PC clock there, replay time comes from the Playback
	/// connection, as NinjaTrader's own @BarTimer reads it), a PC clock running ahead of the feed's time stamps, and
	/// bar-close alerts at session boundaries. The heartbeat ticks every 300 ms of feed time between the trades.
	/// </summary>
	public static class ClockScenarios
	{
		public static IEnumerable<Scenario> All()
		{
			yield return new Scenario("CLK-01 HVN Playback of a past day", "PC clock 3 days ahead of the replay: periods still span 20 bars, same zones as live", HvnPlayback);
			yield return new Scenario("CLK-02 Imbalance Playback of a past day", "the stack bar is analysed from its full footprint, same zone as live", ImbalancePlayback);
			yield return new Scenario("CLK-03 Pressure Toolkit Playback of a past day", "bar 80 signals from its full footprint, same as live", PressurePlayback);
			yield return new Scenario("CLK-04 Heavy Absorption Playback of a past day", "the circle keeps counting up; the bar-close alert comes after the bar closed", AbsorptionPlayback);
			yield return new Scenario("CLK-05 PC clock ahead of the feed", "skew 0 / 29 / 35 / 50 s: all four indicators give the same result as skew 0", Skew);
			yield return new Scenario("CLK-06 quiet close on the feed clock", "with the PC clock 50 s ahead the last bar still closes 30 s after its end in feed time", QuietSkew);
			yield return new Scenario("SES-01 bar-close alert at a continuous session boundary", "24 h template: the bar ending at the boundary is closed on time by the next session's first trade and alerts (Heavy Absorption)", ContinuousBoundary);
			yield return new Scenario("SES-02 first bar of a session closed by the heartbeat", "a session's first bar, then quiet: it closes 30 s after its end and alerts (Heavy Absorption, Pressure Toolkit)", FirstBarQuiet);
			yield return new Scenario("CLK-07 realtime start mid-bar, PC clock ahead", "chart loaded in the middle of a bar with the PC clock 35 / 50 / 120 s ahead: the forming bar is not closed early (all four)", MidBarSkew);
			yield return new Scenario("CLK-08 Playback pause keeps the forming bar open", "Playback paused for 2 minutes mid-bar: nothing closes; after resuming the result equals live", PlaybackPause);
			yield return new Scenario("CLK-09 Playback disconnect under a live chart", "the replay clock disappears: the PC date must not close the paused bar or raise an alert", PlaybackDisconnect);
			yield return new Scenario("CLK-10 quiet market in Playback", "Playback of a past day: the last bar closes 30 s after its end in replay time; an HVN period is not finalized before its end", PlaybackQuiet);
			yield return new Scenario("CLK-11 Tick Replay history does not drive the feed clock", "history replayed at load time in the evening: the last bar (market closed) still closes at realtime start", TickReplayLoad);
			yield return new Scenario("SES-03 session boundary with Playback / a fast PC clock", "continuous boundary under Playback and with the PC clock 6 minutes ahead still alerts", BoundaryClocks);
			yield return new Scenario("SES-04 stale threshold 5 minutes", "a bar seen to close 10 minutes after its end (session break) does not alert, 3 minutes after does", StaleThreshold);
		}

		private static void Clock(double pcOffsetSeconds, bool playback)
		{
			NinjaTrader.Core.Globals.PcOffset = TimeSpan.FromSeconds(pcOffsetSeconds);
			NinjaTrader.Cbi.Connection.PlaybackConnection = playback ? new NinjaTrader.Cbi.Connection() : null;
		}

		private static void ResetClock()
		{
			Clock(0, false);
		}

		/// <summary>Feeds trades with the heartbeat ticking every 300 ms of feed time in between.</summary>
		internal static void FeedHb<T>(Sim<T> sim, IEnumerable<Trade> trades) where T : Indicator, new()
		{
			DateTime hb = DateTime.MinValue;
			foreach (Trade t in trades)
			{
				if (hb == DateTime.MinValue)
					hb = t.Time;
				while (hb.AddMilliseconds(300) <= t.Time)
				{
					hb = hb.AddMilliseconds(300);
					sim.Heartbeat(hb);
				}
				sim.Feed(t);
			}
		}

		internal static string Sig(object s)
		{
			if (s == null)
				return "none";
			return string.Join(",", R.List(s, "Bubbles").Select(b => R.Get<int>(b, "Side") + "@" + R.Get<long>(b, "Tick") + (R.Get<bool>(b, "Strong") ? "S" : "W") + F.S(R.Get<double>(b, "Volume"))))
				+ " | " + string.Join(",", R.List(s, "BigTrades").Select(t => R.Get<long>(t, "Tick") + "x" + F.S(R.Get<double>(t, "Volume"))))
				+ " | " + string.Join(",", R.List(s, "Dots").Select(d => (R.Get<bool>(d, "AtHigh") ? "H" : "L") + (R.Get<bool>(d, "Enhanced") ? "+" : "")));
		}

		// ---------------------------------------------------------------- per-indicator runs (current clock settings)
		private static string RunHvn()
		{
			Dictionary<long, long> p1;
			Tape tape = HvnScenarios.FourPeriods(out p1);
			int firstOf20 = Gen.FirstAt(tape.Trades, Gen.BarStart(20));
			int firstOf41 = Gen.FirstAt(tape.Trades, Gen.BarStart(41));
			Sim<IDCHvnProfile> sim = new Sim<IDCHvnProfile>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(firstOf20));
			sim.GoRealtime();
			FeedHb(sim, tape.Trades.Skip(firstOf20).Take(firstOf41 - firstOf20));
			return R.List(sim.I, "histograms").Count + " periods; " + string.Join(" ", HvnScenarios.Zones(sim.I).Select(z => HvnScenarios.Describe(z, 0.25)));
		}

		private static string RunImbalance()
		{
			Tape tape = ImbalanceScenarios.BuyStackTape();
			int firstOf1 = Gen.FirstAt(tape.Trades, Gen.BarStart(1));
			Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(firstOf1));
			sim.GoRealtime();
			FeedHb(sim, tape.Trades.Skip(firstOf1));
			return string.Join(" ", ImbalanceScenarios.Zones(sim.I).Select(z => R.Get<int>(z, "StartBar") + ":" + F.S(R.Get<long>(z, "LowTick") * 0.25) + "-" + F.S(R.Get<long>(z, "HighTick") * 0.25)
				+ " x" + F.S(R.Get<double>(z, "Ratio")) + " end=" + R.Get<int>(z, "EndBar")));
		}

		private static string RunPressure()
		{
			Tape tape = PressureScenarios.SignalTape();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(firstOf80));
			sim.GoRealtime();
			FeedHb(sim, tape.Trades.Skip(firstOf80));
			Dictionary<int, object> sig = R.IntDict(sim.I, "signals");
			return "80: " + (sig.ContainsKey(80) ? Sig(sig[80]) : "missing") + " || 81: " + (sig.ContainsKey(81) ? Sig(sig[81]) : "missing");
		}

		/// <summary>50-lot buy at 5001.00 one second into bar 150, then 4 x 7 lots at the same price: the circle shows 78.</summary>
		private static Tape AbsorptionTape()
		{
			Tape tape = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(tape, 0, 150, 20, 4996, 5004, new Random(11));
			tape.At(Gen.BarStart(150).AddSeconds(1));
			tape.Buy(5001.00, 50, 0);
			for (int k = 0; k < 4; k++)
			{
				tape.At(Gen.BarStart(150).AddSeconds(2 + k * 3));
				tape.Buy(5001.00, 7, 0);
			}
			Gen.SmallPrints(tape, 151, 3, 20, 4996, 5004, new Random(12));
			return tape;
		}

		private static string RunAbsorption(Ctx c, string label)
		{
			Tape tape = AbsorptionTape();
			int rt = Gen.FirstAt(tape.Trades, Gen.BarStart(150));
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.AlertOnBarClose = true);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(rt));
			sim.GoRealtime();
			FeedHb(sim, tape.Trades.Skip(rt));
			object m = AbsorptionScenarios.Marker(sim.I, 150, 1);
			double shown = m == null ? -1 : R.Get<double>(m, "DisplayValue");
			List<AlertRecord> alerts = sim.I.SimAlerts.Where(a => a.Delivered && a.Id == "IDCHeavyAbsB150").ToList();
			c.Check(Math.Abs(shown - 78) < 1e-9, label + ": circle counts up to 78 (50 + 4 x 7), shows " + shown);
			if (c.Check(alerts.Count == 1, label + ": one bar-close alert, got " + alerts.Count))
				c.Check(alerts[0].Message.Contains("78"), label + ": alert carries the final value: " + alerts[0].Message);
			return shown + " | " + string.Join(" / ", alerts.Select(a => a.Message));
		}

		// ---------------------------------------------------------------- Playback
		private static void Compare(Ctx c, string what, Func<string> run)
		{
			string live, playback;
			try { Clock(0, false); live = run(); }
			finally { ResetClock(); }
			try { Clock(3 * 86400, true); playback = run(); }
			finally { ResetClock(); }
			c.Note("live:     {0}", live);
			c.Note("playback: {0}", playback);
			c.Check(live == playback, what + ": Playback of a past day gives the same result as live");
		}

		private static void HvnPlayback(Ctx c)
		{
			Compare(c, "HVN", RunHvn);
			try
			{
				Clock(3 * 86400, true);
				string r = RunHvn();
				c.Check(r.StartsWith("2 periods"), "two finalized 5-minute periods (P1, P2), not one per bar: " + r);
			}
			finally { ResetClock(); }
		}

		private static void ImbalancePlayback(Ctx c)
		{
			Compare(c, "Imbalance", RunImbalance);
		}

		private static void PressurePlayback(Ctx c)
		{
			Compare(c, "Pressure Toolkit", RunPressure);
		}

		private static void AbsorptionPlayback(Ctx c)
		{
			Compare(c, "Heavy Absorption", () => RunAbsorption(c, NinjaTrader.Cbi.Connection.PlaybackConnection != null ? "playback" : "live"));
		}

		// ---------------------------------------------------------------- PC clock ahead of the feed
		private static void Skew(Ctx c)
		{
			Dictionary<string, Func<string>> runs = new Dictionary<string, Func<string>>
			{
				{ "HVN", RunHvn },
				{ "Imbalance", RunImbalance },
				{ "Pressure Toolkit", RunPressure },
				{ "Heavy Absorption", () => RunAbsorption(c, "skew " + F.S(NinjaTrader.Core.Globals.PcOffset.TotalSeconds)) },
			};
			foreach (KeyValuePair<string, Func<string>> kv in runs)
			{
				string reference = null;
				foreach (double skew in new[] { 0.0, 29.0, 35.0, 50.0 })
				{
					string r;
					try { Clock(skew, false); r = kv.Value(); }
					finally { ResetClock(); }
					if (skew == 0)
					{
						reference = r;
						c.Note("{0}: {1}", kv.Key, r);
					}
					else
						c.Check(r == reference, kv.Key + ", PC clock " + skew + " s ahead: same as without skew" + (r == reference ? "" : ": " + r));
				}
			}
		}

		private static void QuietSkew(Ctx c)
		{
			Tape tape = PressureScenarios.SignalTape();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			int firstOf81 = Gen.FirstAt(tape.Trades, Gen.BarStart(81));
			foreach (double skew in new[] { 0.0, 50.0, -10.0 })
			{
				try
				{
					Clock(skew, false);
					Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
					sim.StartHistorical();
					sim.Feed(tape.Trades.Take(firstOf80));
					sim.GoRealtime();
					FeedHb(sim, tape.Trades.Skip(firstOf80).Take(firstOf81 - firstOf80));
					DateTime closedAt = DateTime.MinValue;
					DateTime t = NinjaTrader.Core.Globals.SimNow;
					while (t < Gen.BarStart(81).AddSeconds(90))
					{
						t = t.AddMilliseconds(300);
						sim.Heartbeat(t);
						if (closedAt == DateTime.MinValue && R.IntDict(sim.I, "signals").ContainsKey(80))
							closedAt = t;
					}
					c.Note("PC clock {0,3} s ahead: bar 80 (ends {1:HH:mm:ss}) closed by the heartbeat at feed time {2:HH:mm:ss.f}", skew, Gen.BarStart(81), closedAt);
					if (c.Check(closedAt != DateTime.MinValue, "skew " + skew + ": closed during the lull"))
					{
						if (skew >= 0)
							c.Check(closedAt >= Gen.BarStart(81).AddSeconds(30) && closedAt < Gen.BarStart(81).AddSeconds(31), "skew " + skew + ": closed 30 s after the end on the feed clock");
						else
							c.Check(closedAt >= Gen.BarStart(81).AddSeconds(30 - skew), "PC clock behind: not closed before the PC clock reaches the end + 30 s");
					}
				}
				finally { ResetClock(); }
			}
		}

		// ---------------------------------------------------------------- session boundaries
		private static Tape HeavyTape(int heavyBar, int barsAfter)
		{
			Tape t = new Tape(0.25, Gen.T0);
			Gen.SmallPrints(t, 0, heavyBar, 20, 4996, 5004, new Random(11));
			t.At(Gen.BarStart(heavyBar).AddSeconds(1));
			t.Buy(5001.00, 60, 500);
			for (int k = 0; k < 10; k++)
				t.Sell(5000.75 - (k % 3) * 0.25, 1 + k % 3, 500);
			if (barsAfter > 0)
				Gen.SmallPrints(t, heavyBar + 1, barsAfter, 20, 4996, 5004, new Random(12));
			return t;
		}

		private static void ContinuousBoundary(Ctx c)
		{
			Tape tape = HeavyTape(150, 4);
			SimOptions o = new SimOptions { SessionStart = Gen.BarStart(151).TimeOfDay, SessionLength = TimeSpan.FromHours(24) };
			int rt = Gen.FirstAt(tape.Trades, Gen.BarStart(150));
			Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(o, i => i.AlertOnBarClose = true);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(rt));
			sim.GoRealtime();
			FeedHb(sim, tape.Trades.Skip(rt));
			c.Check(sim.Bars.IsFirstBarOfSessionByIndex(151), "bar 151 opens the next (back-to-back) session");
			c.Eq(1, sim.I.SimAlerts.Count(a => a.Delivered && a.Id == "IDCHeavyAbsB150"), "the boundary bar still alerts");
		}

		private static void FirstBarQuiet(Ctx c)
		{
			// Heavy Absorption: heavy print in bar 151 = first bar of a session starting 10:07:45, then no trade
			Tape tape = HeavyTape(151, 0);
			SimOptions o = new SimOptions { SessionStart = Gen.BarStart(151).TimeOfDay, SessionLength = TimeSpan.FromHours(24) };
			int rt = Gen.FirstAt(tape.Trades, Gen.BarStart(150));
			Sim<IDCHeavyAbsorption> ha = new Sim<IDCHeavyAbsorption>(o, i => i.AlertOnBarClose = true);
			ha.StartHistorical();
			ha.Feed(tape.Trades.Take(rt));
			ha.GoRealtime();
			FeedHb(ha, tape.Trades.Skip(rt));
			c.Check(ha.Bars.IsFirstBarOfSessionByIndex(151), "bar 151 opens the session");
			ha.Quiet(Gen.BarStart(152).AddSeconds(45));
			c.Check(R.Get<int>(ha.I, "lastClosedBar") >= 151, "bar 151 closed by the heartbeat");
			c.Eq(1, ha.I.SimAlerts.Count(a => a.Delivered && a.Id == "IDCHeavyAbsB151"), "Heavy Absorption: the session's first bar alerts when the heartbeat closes it");

			// Pressure Toolkit: bar 80 opens a session (09:50), then quiet
			Tape st = PressureScenarios.SignalTape();
			int firstOf80 = Gen.FirstAt(st.Trades, Gen.BarStart(80));
			int firstOf81 = Gen.FirstAt(st.Trades, Gen.BarStart(81));
			SimOptions po = new SimOptions { SessionStart = Gen.BarStart(80).TimeOfDay, SessionLength = TimeSpan.FromHours(24) };
			Sim<IDCPressureToolkit> pt = new Sim<IDCPressureToolkit>(po, i => { i.AlertBubbles = true; i.AlertBigTrades = true; i.AlertAbsorptionDots = true; });
			pt.StartHistorical();
			pt.Feed(st.Trades.Take(firstOf80));
			pt.GoRealtime();
			FeedHb(pt, st.Trades.Skip(firstOf80).Take(firstOf81 - firstOf80));
			c.Check(pt.Bars.IsFirstBarOfSessionByIndex(80), "bar 80 opens the session");
			pt.Quiet(Gen.BarStart(81).AddSeconds(45));
			int n = pt.I.SimAlerts.Count(a => a.Delivered && a.Id.StartsWith("IDCPtkBubble80"));
			c.Check(n > 0, "Pressure Toolkit: bubble alerts for the session's first bar when the heartbeat closes it, got " + n);
		}

		// ---------------------------------------------------------------- review round 3
		private static string RunMidBar(string which)
		{
			if (which == "Pressure Toolkit")
			{
				Tape tape = PressureScenarios.SignalTape();
				int mid = (Gen.FirstAt(tape.Trades, Gen.BarStart(80)) + Gen.FirstAt(tape.Trades, Gen.BarStart(81))) / 2;
				Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(mid));
				sim.GoRealtime();
				FeedHb(sim, tape.Trades.Skip(mid));
				Dictionary<int, object> sig = R.IntDict(sim.I, "signals");
				return "80: " + (sig.ContainsKey(80) ? Sig(sig[80]) : "missing");
			}
			if (which == "Imbalance")
			{
				Tape tape = ImbalanceScenarios.BuyStackTape();
				int mid = (Gen.FirstAt(tape.Trades, Gen.BarStart(1)) + Gen.FirstAt(tape.Trades, Gen.BarStart(2))) / 2;
				Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(mid));
				sim.GoRealtime();
				FeedHb(sim, tape.Trades.Skip(mid));
				return string.Join(" ", ImbalanceScenarios.Zones(sim.I).Select(z => R.Get<int>(z, "StartBar") + ":" + R.Get<long>(z, "LowTick") + "-" + R.Get<long>(z, "HighTick")));
			}
			if (which == "Heavy Absorption")
			{
				Tape tape = AbsorptionTape();
				int mid = Gen.FirstAt(tape.Trades, Gen.BarStart(150).AddSeconds(1.5));
				Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), null);
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(mid));
				sim.GoRealtime();
				FeedHb(sim, tape.Trades.Skip(mid));
				return AbsorptionScenarios.Describe(AbsorptionScenarios.Marker(sim.I, 150, 1), 0.25);
			}
			Dictionary<long, long> p1;
			Tape t4 = HvnScenarios.FourPeriods(out p1);
			int m4 = (Gen.FirstAt(t4.Trades, Gen.BarStart(19)) + Gen.FirstAt(t4.Trades, Gen.BarStart(20))) / 2;
			Sim<IDCHvnProfile> hv = new Sim<IDCHvnProfile>(new SimOptions(), null);
			hv.StartHistorical();
			hv.Feed(t4.Trades.Take(m4));
			hv.GoRealtime();
			FeedHb(hv, t4.Trades.Skip(m4).Take(Gen.FirstAt(t4.Trades, Gen.BarStart(41)) - m4));
			return string.Join(" ", HvnScenarios.Zones(hv.I).Select(z => HvnScenarios.Describe(z, 0.25)));
		}

		private static void MidBarSkew(Ctx c)
		{
			foreach (string which in new[] { "Pressure Toolkit", "Imbalance", "Heavy Absorption", "HVN" })
			{
				string reference = RunMidBar(which);
				c.Note("{0}: {1}", which, reference);
				foreach (double skew in new[] { 35.0, 50.0, 120.0 })
				{
					string r;
					try { Clock(skew, false); r = RunMidBar(which); }
					finally { ResetClock(); }
					c.Check(r == reference, which + ", loaded mid-bar with the PC clock " + skew + " s ahead: same as without skew" + (r == reference ? "" : ": " + r));
				}
			}
		}

		/// <summary>Pressure Toolkit, realtime from bar 80; optionally Playback paused for 2 minutes in the middle of bar 80.</summary>
		private static string RunPressurePause(Ctx c, bool pause)
		{
			Tape tape = PressureScenarios.SignalTape();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			int mid = (firstOf80 + Gen.FirstAt(tape.Trades, Gen.BarStart(81))) / 2;
			Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
			sim.StartHistorical();
			sim.Feed(tape.Trades.Take(firstOf80));
			sim.GoRealtime();
			FeedHb(sim, tape.Trades.Skip(firstOf80).Take(mid - firstOf80));
			if (pause)
			{
				// replay time stands still, the PC clock keeps running
				DateTime frozen = NinjaTrader.Core.Globals.SimNow;
				TimeSpan lead = NinjaTrader.Core.Globals.PcOffset;
				for (int k = 1; k <= 400; k++)
				{
					NinjaTrader.Core.Globals.PcOffset = lead + TimeSpan.FromMilliseconds(300 * k);
					sim.Heartbeat(frozen);
				}
				c.Check(!R.IntDict(sim.I, "signals").ContainsKey(80), "bar 80 still open after a 2-minute Playback pause");
			}
			FeedHb(sim, tape.Trades.Skip(mid));
			Dictionary<int, object> sig = R.IntDict(sim.I, "signals");
			return "80: " + (sig.ContainsKey(80) ? Sig(sig[80]) : "missing") + " || 81: " + (sig.ContainsKey(81) ? Sig(sig[81]) : "missing");
		}

		private static void PlaybackPause(Ctx c)
		{
			string live, paused;
			try { Clock(0, false); live = RunPressurePause(c, false); }
			finally { ResetClock(); }
			try { Clock(3 * 86400, true); paused = RunPressurePause(c, true); }
			finally { ResetClock(); }
			c.Note("live:   {0}", live);
			c.Note("paused: {0}", paused);
			c.Check(live == paused, "after resuming, the paused Playback gives the same signals as live");
		}

		private static void PlaybackDisconnect(Ctx c)
		{
			Tape tape = AbsorptionTape();
			int rt = Gen.FirstAt(tape.Trades, Gen.BarStart(150));
			int mid = Gen.FirstAt(tape.Trades, Gen.BarStart(150).AddSeconds(4.5));
			try
			{
				Clock(90 * 86400, true);		// replaying a day three months ago
				Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(new SimOptions(), i => i.AlertOnBarClose = true);
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(rt));
				sim.GoRealtime();
				FeedHb(sim, tape.Trades.Skip(rt).Take(mid - rt));
				NinjaTrader.Cbi.Connection.PlaybackConnection = null;		// Playback disconnected, the chart stays open
				DateTime t = NinjaTrader.Core.Globals.SimNow;
				for (int k = 0; k < 400; k++)
				{
					t = t.AddMilliseconds(300);
					sim.Heartbeat(t);
				}
				c.Eq(149, R.Get<int>(sim.I, "lastClosedBar"), "the bar that was forming is not closed by the PC date");
				c.Eq(0, sim.I.SimAlerts.Count(a => a.Delivered), "no alert after the disconnect");
			}
			finally { ResetClock(); }
		}

		private static void PlaybackQuiet(Ctx c)
		{
			Tape tape = PressureScenarios.SignalTape();
			int firstOf80 = Gen.FirstAt(tape.Trades, Gen.BarStart(80));
			int firstOf81 = Gen.FirstAt(tape.Trades, Gen.BarStart(81));
			try
			{
				Clock(3 * 86400, true);
				Sim<IDCPressureToolkit> sim = new Sim<IDCPressureToolkit>(new SimOptions(), null);
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(firstOf80));
				sim.GoRealtime();
				FeedHb(sim, tape.Trades.Skip(firstOf80).Take(firstOf81 - firstOf80));
				DateTime closedAt = DateTime.MinValue;
				DateTime t = NinjaTrader.Core.Globals.SimNow;
				while (t < Gen.BarStart(81).AddSeconds(60))
				{
					t = t.AddMilliseconds(300);
					sim.Heartbeat(t);
					if (closedAt == DateTime.MinValue && R.IntDict(sim.I, "signals").ContainsKey(80))
						closedAt = t;
				}
				c.Note("Playback: bar 80 (ends {0:HH:mm:ss}) closed at replay time {1:HH:mm:ss.f}", Gen.BarStart(81), closedAt);
				c.Check(closedAt >= Gen.BarStart(81).AddSeconds(30) && closedAt < Gen.BarStart(81).AddSeconds(31), "closed 30 s after its end in replay time");

				// HVN: a lull inside a period (bars 20..29 traded, the period runs until bar 40) must not finalize it early
				Dictionary<long, long> p1;
				Tape t4 = HvnScenarios.FourPeriods(out p1);
				int firstOf20 = Gen.FirstAt(t4.Trades, Gen.BarStart(20));
				int firstOf30 = Gen.FirstAt(t4.Trades, Gen.BarStart(30));
				int firstOf41 = Gen.FirstAt(t4.Trades, Gen.BarStart(41));
				Sim<IDCHvnProfile> hv = new Sim<IDCHvnProfile>(new SimOptions(), null);
				hv.StartHistorical();
				hv.Feed(t4.Trades.Take(firstOf20));
				hv.GoRealtime();
				FeedHb(hv, t4.Trades.Skip(firstOf20).Take(firstOf30 - firstOf20));
				hv.Quiet(Gen.BarStart(30).AddSeconds(50));		// bar 29 complete, the period still runs
				c.Eq(0, HvnScenarios.Zones(hv.I).Count(z => R.Get<int>(z, "StartBar") == 20), "no zones for a period that has not ended");
				FeedHb(hv, t4.Trades.Skip(firstOf30).Take(firstOf41 - firstOf30));
				c.Eq(2, HvnScenarios.Zones(hv.I).Count(z => R.Get<int>(z, "StartBar") == 20), "one profile for the period once it ended (2 zones)");
			}
			finally { ResetClock(); }
		}

		private static void TickReplayLoad(Ctx c)
		{
			Tape tape = ImbalanceScenarios.BuyStackTape();
			int firstOf2 = Gen.FirstAt(tape.Trades, Gen.BarStart(2));
			DateTime load = Gen.BarStart(2).AddHours(8);		// chart opened in the evening, the market closed after bar 1
			try
			{
				Sim<IDCImbalanceLevels> sim = new Sim<IDCImbalanceLevels>(new SimOptions(), null);
				sim.StartHistorical();
				foreach (Trade t in tape.Trades.Take(firstOf2))
				{
					NinjaTrader.Core.Globals.PcOffset = load - t.Time;	// NinjaTrader replays the history at load time
					sim.Feed(t);
				}
				NinjaTrader.Core.Globals.PcOffset = TimeSpan.Zero;
				NinjaTrader.Core.Globals.SimNow = load;
				sim.GoRealtime();
				c.Eq(1, ImbalanceScenarios.Zones(sim.I).Count, "the zone of the last (complete) bar appears at realtime start");
			}
			finally { ResetClock(); }
		}

		private static void BoundaryClocks(Ctx c)
		{
			try { Clock(3 * 86400, true); ContinuousBoundary(c); }
			finally { ResetClock(); }
			try { Clock(360, false); ContinuousBoundary(c); }
			finally { ResetClock(); }
		}

		private static void StaleThreshold(Ctx c)
		{
			foreach (int breakMinutes in new[] { 10, 3 })
			{
				// heavy print in bar 150; the session ends with it (10:07:45) and the next one opens breakMinutes later
				Tape tape = HeavyTape(150, 0);
				DateTime reopen = Gen.BarStart(151).AddMinutes(breakMinutes);
				Tape after = new Tape(0.25, reopen.AddMilliseconds(100));
				for (int k = 0; k < 20; k++)
					after.Buy(5001.00, 1, 200);
				SimOptions o = new SimOptions { SessionStart = reopen.TimeOfDay, SessionLength = TimeSpan.FromHours(24) - TimeSpan.FromMinutes(breakMinutes) };
				int rt = Gen.FirstAt(tape.Trades, Gen.BarStart(150));
				Sim<IDCHeavyAbsorption> sim = new Sim<IDCHeavyAbsorption>(o, i => i.AlertOnBarClose = true);
				sim.StartHistorical();
				sim.Feed(tape.Trades.Take(rt));
				sim.GoRealtime();
				sim.Feed(tape.Trades.Skip(rt));		// no heartbeat during the break: the bar is closed by the reopen
				sim.Feed(after.Trades);
				c.Check(sim.Bars.IsFirstBarOfSessionByIndex(151), breakMinutes + "-minute break: bar 151 opens the next session");
				int n = sim.I.SimAlerts.Count(a => a.Delivered && a.Id == "IDCHeavyAbsB150");
				if (breakMinutes > 5)
					c.Eq(0, n, breakMinutes + "-minute break: the close seen at the reopen is stale, no alert");
				else
					c.Eq(1, n, breakMinutes + "-minute break: alert");
			}
		}
	}
}
