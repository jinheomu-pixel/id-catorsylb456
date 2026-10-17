// IDCator behaviour-simulation harness.
//
//   dotnet run --project tools/sim-test                    run every scenario (long run 20000 bars x 50 ticks)
//   dotnet run --project tools/sim-test -- --quick         shorter long runs (3000 bars)
//   dotnet run --project tools/sim-test -- IMB HVN-04      only scenarios whose name contains one of the words
//   dotnet run --project tools/sim-test -- --verbose       also print scenario notes for passing scenarios
//   dotnet run --project tools/sim-test --property:IdcSourceDir=/abs/path/to/patched/IDCator -- --quick
//                                                         run against a patched copy of the indicator sources
//
// Exit code = number of failed scenarios. A FAIL means the indicator did not behave as specified
// (vendor description / promo videos / NinjaTrader runtime rules); the failure lines say what differed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace SimTest
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
			bool quick = args.Contains("--quick");
			bool verbose = args.Contains("--verbose");
			List<string> filters = args.Where(a => !a.StartsWith("--")).ToList();

			List<Scenario> scenarios = new List<Scenario>();
			scenarios.AddRange(ImbalanceScenarios.All());
			scenarios.AddRange(HvnScenarios.All());
			scenarios.AddRange(AbsorptionScenarios.All());
			scenarios.AddRange(PressureScenarios.All());
			scenarios.AddRange(EngineScenarios.All());
			scenarios.AddRange(FormatScenarios.All());
			scenarios.AddRange(StressScenarios.All());
			if (filters.Count > 0)
				scenarios = scenarios.Where(s => filters.Any(f => s.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

			Console.WriteLine("IDCator simulation harness - {0} scenario(s){1}", scenarios.Count, quick ? " (quick)" : "");
			Console.WriteLine(new string('=', 100));
			List<Tuple<Scenario, bool, double>> results = new List<Tuple<Scenario, bool, double>>();
			foreach (Scenario s in scenarios)
			{
				Ctx ctx = new Ctx { Quick = quick };
				Stopwatch sw = Stopwatch.StartNew();
				string crash = null;
				try { s.Body(ctx); }
				catch (Exception e)
				{
					Exception inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
					crash = inner.GetType().Name + ": " + inner.Message + "\n        at " + StressScenarios.FirstFrame(inner);
				}
				sw.Stop();
				bool pass = crash == null && ctx.Failures.Count == 0;
				results.Add(Tuple.Create(s, pass, sw.Elapsed.TotalSeconds));
				Console.WriteLine("{0}  {1}  ({2:0.0} s)", pass ? "PASS" : "FAIL", s.Name, sw.Elapsed.TotalSeconds);
				if (!pass || verbose)
					Console.WriteLine("      {0}", s.Description);
				if (crash != null)
					Console.WriteLine("      EXCEPTION {0}", crash);
				foreach (string f in ctx.Failures)
					Console.WriteLine("      x {0}", f);
				if (!pass || verbose)
					foreach (string n in ctx.Notes)
						Console.WriteLine("      - {0}", n);
			}
			Console.WriteLine(new string('=', 100));
			int failed = results.Count(r => !r.Item2);
			Console.WriteLine("SUMMARY: {0} passed, {1} failed, {2} total ({3:0.0} s)", results.Count - failed, failed, results.Count, results.Sum(r => r.Item3));
			foreach (var r in results.Where(r => !r.Item2))
				Console.WriteLine("  FAIL {0}", r.Item1.Name);
			return failed;
		}
	}
}
