using System;
using System.Collections.Generic;
using System.Linq;

namespace SimTest
{
	/// <summary>Synthetic market data generators shared by the scenarios.</summary>
	public static class Gen
	{
		/// <summary>Monday 2 March 2026, 09:30 (inside the CME ETH session of trading day 2026-03-02).</summary>
		public static readonly DateTime T0 = new DateTime(2026, 3, 2, 9, 30, 0);
		public static readonly TimeSpan S15 = TimeSpan.FromSeconds(15);

		public static DateTime BarStart(int bar) { return T0.AddSeconds(15 * bar); }

		/// <summary>Balanced levels (ask = bid = v) from lo to hi.</summary>
		public static List<Level> Balanced(double tickSize, double lo, double hi, long v)
		{
			List<Level> l = new List<Level>();
			long a = (long)Math.Round(lo / tickSize), b = (long)Math.Round(hi / tickSize);
			for (long t = a; t <= b; t++)
				l.Add(new Level(Math.Round(t * tickSize, 10), v, v));
			return l;
		}

		/// <summary>A filler bar with balanced volume (no imbalances, no bubbles).</summary>
		public static void Filler(Tape tape, int bar, double open, double close, double lo, double hi, long v = 10)
		{
			tape.Bar(BarStart(bar), S15, open, close, Balanced(tape.TickSize, lo, hi, v), 5);
		}

		/// <summary>
		/// Population of small prints for the print-size statistics: 'bars' bars from 'firstBar', 'perBar' prints each,
		/// sizes cycling 1..5 (mean 3, sd 1.414), sides alternating, price wandering inside [lo, hi].
		/// </summary>
		public static void SmallPrints(Tape tape, int firstBar, int bars, int perBar, double lo, double hi, Random rng, int maxSize = 5)
		{
			double tick = tape.TickSize;
			double price = tape.P((lo + hi) / 2);
			int k = 0;
			for (int b = firstBar; b < firstBar + bars; b++)
			{
				tape.Now = BarStart(b).AddMilliseconds(100);
				int step = (int)(14000 / perBar);
				for (int i = 0; i < perBar; i++)
				{
					int move = rng.Next(3) - 1;
					double next = tape.P(price + move * tick);
					if (next >= lo && next <= hi) price = next;
					long size = 1 + (k % maxSize);
					int side = (k % 2 == 0) ? 1 : -1;
					tape.Print(price, size, side, step);
					k++;
				}
			}
		}

		/// <summary>
		/// Random-walk market for long runs: 'ticksPerBar' trades per 15 s bar, sides by tick direction (ties random),
		/// sizes 1..10 with rare large prints and occasional one-sided bursts (stacked imbalances).
		/// </summary>
		public static IEnumerable<Trade> RandomMarket(double tickSize, DateTime start, int bars, int ticksPerBar, int seed, double startPrice)
		{
			Random rng = new Random(seed);
			double price = startPrice;
			double last = price;
			int burst = 0, burstSide = 0;
			int trend = 0;
			for (int b = 0; b < bars; b++)
			{
				DateTime barStart = start.AddSeconds(15 * b);
				if (rng.NextDouble() < 0.05) trend = rng.Next(3) - 1;
				for (int i = 0; i < ticksPerBar; i++)
				{
					DateTime t = barStart.AddMilliseconds(1 + i * (14998.0 / ticksPerBar));
					if (burst == 0 && rng.NextDouble() < 0.004) { burst = 8 + rng.Next(10); burstSide = rng.Next(2) == 0 ? 1 : -1; }
					int move;
					if (burst > 0) { move = burstSide; burst--; }
					else
					{
						double u = rng.NextDouble();
						move = u < 0.33 + trend * 0.03 ? 1 : u < 0.66 ? 0 : -1;
						if (move == 0 && u > 0.5) move = 0;
					}
					price = Math.Round(Math.Round((price + move * tickSize) / tickSize) * tickSize, 10);
					int side = price > last ? 1 : price < last ? -1 : (rng.Next(2) == 0 ? 1 : -1);
					if (burst > 0) side = burstSide;
					long size;
					double r = rng.NextDouble();
					if (r < 0.004) size = 60 + rng.Next(240);
					else if (burst > 0) size = 5 + rng.Next(20);
					else size = 1 + (long)Math.Floor(-Math.Log(1 - rng.NextDouble()) * 3);
					if (size > 40 && r >= 0.004) size = 40;
					Trade tr = new Trade { Time = t, Price = price, Volume = size, Side = side };
					if (side > 0) { tr.Ask = price; tr.Bid = Math.Round(price - tickSize, 10); }
					else { tr.Bid = price; tr.Ask = Math.Round(price + tickSize, 10); }
					last = price;
					yield return tr;
				}
			}
		}

		/// <summary>Index of the first trade at or after time t.</summary>
		public static int FirstAt(List<Trade> trades, DateTime t)
		{
			for (int i = 0; i < trades.Count; i++)
				if (trades[i].Time >= t) return i;
			return trades.Count;
		}

		/// <summary>First tick k in [from, to) for which k * tickSize (the indicators' ToPrice) is above the feed price (nearest double of the decimal).</summary>
		public static long MismatchedTick(double tickSize, long from, long to)
		{
			for (long k = from; k < to; k++)
			{
				double feed = Math.Round(k * tickSize, 10);
				if (k * tickSize > feed)
					return k;
			}
			return -1;
		}
	}
}
