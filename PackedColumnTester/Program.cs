using PackedColumn;
using PackingLibrary;
using System.Globalization;

/*
string path = args.Length > 0 ? args[0] : "C:\\Users\\bouch\\source\\repos\\PackedColumn\\reference.csv";
int result = ParityTest.Run(path);
Console.ReadKey();   // keeps the window open, remove if running from a terminal
return result;
*/

PackedColumnModel model = new PackedColumnModel();
PackingManager packingManager = PackingManager.Load("C:\\Users\\bouch\\source\\repos\\PackedColumn\\Packings\\library.json");

Console.WriteLine($"Packing library loaded with {packingManager.Count} packings");


public static class ParityTest
{
    const double Tol = 1e-8;   // relative

    public static int Run(string path = "reference.csv")
    {
        string[] lines = File.ReadAllLines(path);
        string[] head = lines[0].Split(',');
        var col = new Dictionary<string, int>();
        for (int i = 0; i < head.Length; i++) col[head[i]] = i;

        int n = 0, bad = 0, calm = 0, loaded = 0, flooded = 0;
        double worst = 0;
        var failures = new List<string>();

        for (int r = 1; r < lines.Length; r++)
        {
            if (string.IsNullOrWhiteSpace(lines[r])) continue;
            string[] f = lines[r].Split(',');
            Func<string, double> v = name => double.Parse(f[col[name]], CultureInfo.InvariantCulture);

            var m = new BilletSchultesModel
            {
                SpecificSurfaceArea = v("ap"),
                VoidFraction = v("eps"),
                ColumnDiameter = v("dc"),
                CP0 = v("Cp"),
                CS = v("Clp"),
                CFI = v("Cfp"),
                Ch = v("Ch"),
                LiquidDensity = v("rho_L"),
                LiquidViscosity = v("mu_L"),
                GasDensity = v("rho_G"),
                GasViscosity = v("mu_G"),
                LiquidSuperficialVelocity = v("uL") / 3600.0,   // CSV is m3/m2h
                GasSuperficialVelocity = v("uG"),
                GravitationalAcceleration = 9.81                // match the Python
            };
            m.CalculateHydraulics();

            double dp = v("dp"), hold = v("hold"), lp = v("uG_lp"), fp = v("uG_fp");
            bool expectFlooded = double.IsNaN(dp);
            string tag = string.Format(CultureInfo.InvariantCulture,
                "row {0} (uL={1}, uG={2})", r, v("uL"), v("uG"));

            // flood status must agree exactly
            if (m.IsFlooded != expectFlooded)
                failures.Add(tag + ": IsFlooded=" + m.IsFlooded + ", expected " + expectFlooded);

            Check("uG_lp", m.LoadingGasVelocity, lp, tag, failures, ref worst);
            Check("uG_fp", m.FloodingGasVelocity, fp, tag, failures, ref worst);
            Check("dp", m.PressureDropPerHeight, dp, tag, failures, ref worst);
            Check("hold", m.LiquidHoldup, hold, tag, failures, ref worst);

            if (expectFlooded) flooded++;
            else if (v("uG") > lp) loaded++;
            else calm++;
            n++;
        }

        bad = failures.Count;
        Console.WriteLine("{0} rows | calm {1}, loaded {2}, flooded {3}", n, calm, loaded, flooded);
        Console.WriteLine("worst relative error: {0:E2}", worst);
        if (calm == 0 || loaded == 0 || flooded == 0)
            Console.WriteLine("WARNING: a regime is not covered, widen the sweep in make_reference.py");
        foreach (string s in failures.GetRange(0, Math.Min(10, failures.Count)))
            Console.WriteLine("FAIL " + s);
        Console.WriteLine(bad == 0 ? "PASS" : "FAILED (" + bad + " mismatches)");
        return bad == 0 ? 0 : 1;
    }

    static void Check(string name, double? actual, double expected, string tag,
                      List<string> failures, ref double worst)
    {
        if (double.IsNaN(expected))
        {
            if (actual != null) failures.Add(tag + ": " + name + " should be null, got " + actual);
            return;
        }
        if (actual == null) { failures.Add(tag + ": " + name + " is null, expected " + expected); return; }

        double rel = Math.Abs(actual.Value - expected) / Math.Max(Math.Abs(expected), 1e-300);
        if (rel > worst) worst = rel;
        if (rel > Tol)
            failures.Add(string.Format(CultureInfo.InvariantCulture,
                "{0}: {1} = {2:R}, expected {3:R} (rel {4:E2})", tag, name, actual.Value, expected, rel));
    }
}



