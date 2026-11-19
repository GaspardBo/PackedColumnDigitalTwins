using System;
using System.Text;

namespace PackedColumn
{
    // =====================================================================
    //  PackedColumnModel.cs
    //  Pure maths, no CAPE-OPEN / toolkit dependencies, so it can be unit
    //  tested in a console project and reused elsewhere.
    //
    //  All quantities are SI:
    //    flows      mol/s            fluxes   mol/(m2 s)  or  kg/(m2 s)
    //    pressure   Pa               temperature K
    //    lengths    m                areas    m2/m3
    //    viscosity  Pa s             diffusivity m2/s
    //    surface tension N/m         molar mass kg/mol
    //
    //  The correlation structure below is Onda, Takeuchi & Okumoto (1968)
    //  as it is usually presented in textbooks. CHECK the constants and
    //  exponents against the original paper / your textbook before using
    //  results for design work.
    // =====================================================================

    /// <summary>Packing description (from the manufacturer's datasheet).</summary>
    /// 


    public class Packing
    {
        /// <summary>Total (geometric) specific area a_t, m2/m3.</summary>
        public double SpecificArea = 200.0;
        /// <summary>Nominal packing size d_p, m.</summary>
        public double NominalSize = 0.025;
        /// <summary>Critical surface tension of the packing material sigma_c, N/m.
        /// Typical textbook values (verify): ceramic ~0.061, steel ~0.075,
        /// carbon ~0.056, plastic ~0.033.</summary>
        public double CriticalSurfaceTension = 0.075;

        // Calibration factors: tune these against manufacturer HETP / HTU data.
        public double AreaFactor = 1.0;   // multiplies Onda's wetted area
        public double KLFactor = 1.0;     // multiplies k_L
        public double KGFactor = 1.0;     // multiplies k_G
    }

    /// <summary>Phase properties (constant values for now).</summary>
    public class FluidProps
    {
        public double Density = 1.0;        // kg/m3
        public double Viscosity = 1e-5;     // Pa s
        public double SurfaceTension = 0.0; // N/m (liquid only)
        public double MolarMass = 0.029;    // kg/mol
        public double Diffusivity = 1e-5;   // m2/s, solute diffusivity in this phase
    }

    public class PackingData
    {
        public int i = 0;

        public double KGFactor { get; internal set; }
        public double KLFactor { get; internal set; }
        public double AreaFactor { get; internal set; }
        public double CriticalSurfaceTension { get; internal set; }
        public double SpecificArea { get; internal set; }
        public double NominalSize { get; internal set; }
    }
    public class OndaResult
    {
        public double WettedAreaOnda;   // a_w from Onda, m2/m3
        public double EffectiveArea;    // a_w * AreaFactor, m2/m3
        public double WettedFraction;   // a_w / a_t
        public double Kl;               // m/s
        public double Kg;               // mol/(m2 s Pa)
        public double Hg;               // m
        public double Hl;               // m
        public double StrippingFactor;  // S = m G / L
        public double Hog;              // m
        public double Hetp;             // m

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("a_w (Onda)   = " + WettedAreaOnda.ToString("G6") + " m2/m3  (a_w/a_t = " + WettedFraction.ToString("G4") + ")");
            sb.AppendLine("a_eff        = " + EffectiveArea.ToString("G6") + " m2/m3");
            sb.AppendLine("k_L          = " + Kl.ToString("G6") + " m/s");
            sb.AppendLine("k_G          = " + Kg.ToString("G6") + " mol/(m2 s Pa)");
            sb.AppendLine("H_G          = " + Hg.ToString("G6") + " m");
            sb.AppendLine("H_L          = " + Hl.ToString("G6") + " m");
            sb.AppendLine("S = mG/L     = " + StrippingFactor.ToString("G6"));
            sb.AppendLine("H_OG         = " + Hog.ToString("G6") + " m");
            sb.AppendLine("HETP         = " + Hetp.ToString("G6") + " m");
            return sb.ToString();
        }
    }

    public class PackedColumnModel
    {
        public const double R = 8.314462618;   // J/(mol K)
        public const double G = 9.80665;       // m/s2

        // -----------------------------------------------------------------
        //  Onda: wetted area, k_L, k_G  ->  H_G, H_L, H_OG
        // -----------------------------------------------------------------
        /// <param name="gasMolarFlux">Superficial gas molar flux, mol/(m2 s)</param>
        /// <param name="liqMolarFlux">Superficial liquid molar flux, mol/(m2 s)</param>
        /// <param name="pressurePa">Column pressure, Pa</param>
        /// <param name="temperatureK">Temperature, K</param>
        /// <param name="henryM">Equilibrium slope m in y* = m x (dimensionless)</param>
        public static OndaResult Onda(PackingData pk, FluidProps gas, FluidProps liq,
                                      double gasMolarFlux, double liqMolarFlux,
                                      double pressurePa, double temperatureK, double henryM)
        {
            Require(pk.SpecificArea > 0, "Packing specific area must be > 0");
            Require(pk.NominalSize > 0, "Packing nominal size must be > 0");
            Require(liq.SurfaceTension > 0, "Liquid surface tension must be > 0");
            Require(gasMolarFlux > 0, "Gas molar flux must be > 0");
            Require(liqMolarFlux > 0, "Liquid molar flux must be > 0");
            Require(pressurePa > 0 && temperatureK > 0, "P and T must be > 0");

            double at = pk.SpecificArea;
            double dp = pk.NominalSize;
            double Gw = gasMolarFlux * gas.MolarMass;   // kg/(m2 s)
            double Lw = liqMolarFlux * liq.MolarMass;   // kg/(m2 s)

            // --- wetted area (Onda) ---
            double expo = 1.45
                * Math.Pow(pk.CriticalSurfaceTension / liq.SurfaceTension, 0.75)
                * Math.Pow(Lw / (at * liq.Viscosity), 0.1)
                * Math.Pow(Lw * Lw * at / (liq.Density * liq.Density * G), -0.05)
                * Math.Pow(Lw * Lw / (liq.Density * liq.SurfaceTension * at), 0.2);
            double wetFrac = 1.0 - Math.Exp(-expo);
            double awOnda = wetFrac * at;
            double aEff = awOnda * pk.AreaFactor;

            // --- liquid-side coefficient, m/s ---
            double kL = 0.0051
                * Math.Pow(Lw / (awOnda * liq.Viscosity), 2.0 / 3.0)
                * Math.Pow(liq.Viscosity / (liq.Density * liq.Diffusivity), -0.5)
                * Math.Pow(at * dp, 0.4)
                / Math.Pow(liq.Density / (liq.Viscosity * G), 1.0 / 3.0);
            kL *= pk.KLFactor;

            // --- gas-side coefficient, mol/(m2 s Pa) ---
            // Onda: k_G R T / (a_t D_G) = K (Gw/(a_t mu_G))^0.7 (mu_G/(rho_G D_G))^(1/3) (a_t d_p)^-2
            double K = dp >= 0.015 ? 5.23 : 2.0;
            double kG = K
                * Math.Pow(Gw / (at * gas.Viscosity), 0.7)
                * Math.Pow(gas.Viscosity / (gas.Density * gas.Diffusivity), 1.0 / 3.0)
                * Math.Pow(at * dp, -2.0)
                * at * gas.Diffusivity / (R * temperatureK);
            kG *= pk.KGFactor;

            // --- heights of transfer units ---
            double cL = liq.Density / liq.MolarMass;                 // mol/m3
            double Hg = gasMolarFlux / (kG * aEff * pressurePa);
            double Hl = liqMolarFlux / (kL * aEff * cL);
            double S = henryM * gasMolarFlux / liqMolarFlux;
            double Hog = Hg + S * Hl;

            return new OndaResult
            {
                WettedAreaOnda = awOnda,
                EffectiveArea = aEff,
                WettedFraction = wetFrac,
                Kl = kL,
                Kg = kG,
                Hg = Hg,
                Hl = Hl,
                StrippingFactor = S,
                Hog = Hog,
                Hetp = Hetp(Hog, S)
            };
        }

        // -----------------------------------------------------------------
        //  HETP from H_OG (for distillation-type conversions)
        // -----------------------------------------------------------------
        public static double Hetp(double hog, double lambda)
        {
            if (Math.Abs(lambda - 1.0) < 1e-8 || lambda <= 0) return hog;
            return hog * Math.Log(lambda) / (lambda - 1.0);
        }

        // -----------------------------------------------------------------
        //  N_OG, dilute system with straight equilibrium line y* = m x
        //  (Colburn).  Works for absorption and stripping.
        // -----------------------------------------------------------------
        public static double NogColburn(double S, double yIn, double yOut, double xIn, double m)
        {
            double num = yIn - m * xIn;
            double den = yOut - m * xIn;
            Require(num * den > 0, "No driving force: gas outlet/inlet are on opposite sides of equilibrium.");
            double ratio = num / den;
            if (Math.Abs(S - 1.0) < 1e-8) return ratio - 1.0;
            double arg = (1.0 - S) * ratio + S;
            Require(arg > 0, "Specified separation is not achievable (check S and targets).");
            return Math.Log(arg) / (1.0 - S);
        }

        /// <summary>Inverse of NogColburn: gas outlet composition for a given N_OG (rating).</summary>
        public static double YOutFromNog(double S, double nog, double yIn, double xIn, double m)
        {
            double num = yIn - m * xIn;
            double ratio;   // (yOut - m xIn) / (yIn - m xIn)
            if (Math.Abs(S - 1.0) < 1e-8) ratio = 1.0 / (1.0 + nog);
            else ratio = (1.0 - S) / (Math.Exp(nog * (1.0 - S)) - S);
            return m * xIn + num * ratio;
        }

        // -----------------------------------------------------------------
        //  N_OG by numerical integration: any equilibrium curve, concentrated
        //  solutions.  N_OG = Int (1-y)lm / ((1-y)(y-y*)) dy, yOut -> yIn
        //  Operating line in mole ratios:  X = Xin + (Gs/Ls)(Y - Yout)
        // -----------------------------------------------------------------
        /// <param name="gs">Solute-free gas flow (flux), same units as ls</param>
        /// <param name="ls">Solute-free liquid flow (flux)</param>
        /// <param name="yEq">Equilibrium: gas mole fraction y* as a function of liquid mole fraction x</param>
        public static double NogNumeric(double gs, double ls, double yIn, double yOut, double xIn,
                                        Func<double, double> yEq, int intervals = 200)
        {
            if (intervals % 2 == 1) intervals++;
            double Yout = yOut / (1.0 - yOut);
            double Xin = xIn / (1.0 - xIn);
            double h = (yIn - yOut) / intervals;

            Func<double, double> f = y =>
            {
                double Y = y / (1.0 - y);
                double X = Xin + (gs / ls) * (Y - Yout);
                double x = X / (1.0 + X);
                double ys = yEq(x);
                if (Math.Abs(y - ys) < 1e-14)
                    throw new InvalidOperationException("Pinch: operating and equilibrium lines touch at y = " + y);
                double lnTerm = Math.Log((1.0 - ys) / (1.0 - y));
                if (Math.Abs(lnTerm) < 1e-14) return 1.0 / ((1.0 - y) * (y - ys)) * (1.0 - y);
                return 1.0 / ((1.0 - y) * lnTerm);
            };

            double sum = f(yOut) + f(yIn);
            for (int i = 1; i < intervals; i++)
                sum += f(yOut + i * h) * (i % 2 == 1 ? 4.0 : 2.0);
            return sum * h / 3.0;
        }

        // -----------------------------------------------------------------
        //  Packed height for a required separation (design)
        // -----------------------------------------------------------------
        public static double RequiredHeight(double hog, double nog) { return hog * nog; }

        // -----------------------------------------------------------------
        //  Self test: call from a throwaway console project.
        //  Checks (1) Colburn vs numerical integration, (2) inverse formula,
        //  (3) prints an Onda result for typical air/water-like inputs.
        // -----------------------------------------------------------------
        public static string SelfTest()
        {
            var sb = new StringBuilder();

            // (1) Dilute linear case: numerical N_OG must match Colburn
            double m = 1.5, yIn = 0.001, yOut = 0.0001, xIn = 0.0;
            double gOverL = 0.4;                         // G/L -> S = m G/L = 0.6
            double S = m * gOverL;
            double nogC = NogColburn(S, yIn, yOut, xIn, m);
            double nogN = NogNumeric(gOverL, 1.0, yIn, yOut, xIn, x => m * x);
            sb.AppendLine("N_OG Colburn = " + nogC.ToString("G6") + " | numeric = " + nogN.ToString("G6") +
                          " | diff = " + (100 * (nogN - nogC) / nogC).ToString("F3") + " %");

            // (2) Inverse
            double back = YOutFromNog(S, nogC, yIn, xIn, m);
            sb.AppendLine("y_out from N_OG = " + back.ToString("G6") + " (target " + yOut.ToString("G6") + ")");

            // (3) Onda with illustrative numbers (NOT a validated design case)
            var pk = new PackingData { SpecificArea = 200, NominalSize = 0.025, CriticalSurfaceTension = 0.075 };
            var gas = new FluidProps { Density = 1.2, Viscosity = 1.8e-5, MolarMass = 0.029, Diffusivity = 1.5e-5 };
            var liq = new FluidProps { Density = 1000, Viscosity = 1.0e-3, SurfaceTension = 0.072, MolarMass = 0.018, Diffusivity = 1.5e-9 };
            var res = Onda(pk, gas, liq, gasMolarFlux: 20.0, liqMolarFlux: 50.0,
                           pressurePa: 101325, temperatureK: 298.15, henryM: 1.5);
            sb.AppendLine("--- Onda, illustrative inputs ---");
            sb.Append(res.ToString());
            return sb.ToString();
        }

        private static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }
    }
}
