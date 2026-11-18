using Microsoft.Win32;
using Mose.CapeOpenToolkit.UnitProxy;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PackedColumn
{
    // =====================================================================
    //  PackedColumnUnit.cs
    //  CAPE-OPEN packed column model built on the Mose toolkit
    //  
    //  Authors: Amogh Krishnanand Atreya & Gaspard Bouchard
    //  =====================================================================

    [ComVisible(true)]
    [ProgId("PackedColumn.PackedColumnUnit")]
    [Guid("052BE404-4FA4-40FD-AB55-1D7DF7D3234F")]
    [ClassInterface(ClassInterfaceType.None)]
    public class PackedColumnUnit : CapeOpenUnitSimple
    {
        private const string LogPath = @"C:\tmp\PackedColumn.log";
        private const bool VerboseLog = true;

        // Last calculation summary, shown in the Edit dialog
        private string _lastSummary = "No calculation has been run yet.";

        // -----------------------------------------------------------------
        //  Parameter table: name, description (with units), min, max, default
        // -----------------------------------------------------------------
        private sealed class ParamDef
        {
            public string Name, Description;
            public double Min, Max, Default;
        }

        // Parameter definition helper
        private static ParamDef D(string name, string desc, double min, double max, double def)
        {
            return new ParamDef { Name = name, Description = desc, Min = min, Max = max, Default = def };
        }

        private static readonly ParamDef[] InputParams =
        {
            // --- mode ---
            D("CalcMode",            "0 = rating (use PackedHeight_m), 1 = design (use TargetYOut)", 0, 1, 0),
            D("TargetYOut",          "Design mode: target solute mole fraction in the gas outlet", 1e-12, 1, 0.0001),

            // --- geometry ---
            D("ColumnDiameter_m",    "Column inside diameter, m", 0.01, 30, 1.0),
            D("PackedHeight_m",      "Packed height, m (rating mode)", 0.01, 200, 5.0),
            D("PressureDrop_Pa",     "Gas pressure drop over the bed, Pa (take from manufacturer curve)", 0, 1e6, 0),

            // --- packing (manufacturer datasheet) ---
            D("PackingArea_m2pm3",   "Packing specific area a_t, m2/m3", 10, 2000, 200),
            D("PackingSize_m",       "Packing nominal size d_p, m", 0.002, 0.3, 0.025),
            D("PackingSigmaC_Npm",   "Critical surface tension of packing material, N/m", 0.01, 0.2, 0.075),

            // --- calibration against manufacturer data ---
            D("AreaFactor",          "Multiplier on Onda wetted are a (calibration)", 0.05, 5, 1),
            D("kLFactor",            "Multiplier on k_L (calibration)", 0.05, 10, 1),
            D("kGFactor",            "Multiplier on k_G (calibration)", 0.05, 10, 1),
        };

        // Results are written back to these parameters after each calculation
        // (if the host allows writing them; failures are logged, not fatal).
        private static readonly ParamDef[] ResultParams =
        {
            D("Result_HG_m",          "Result: gas-phase transfer unit height H_G, m", 0, 1e12, 0),
            D("Result_HL_m",          "Result: liquid-phase transfer unit height H_L, m", 0, 1e12, 0),
            D("Result_HOG_m",         "Result: overall transfer unit height H_OG, m", 0, 1e12, 0),
            D("Result_NOG",           "Result: number of transfer units N_OG", 0, 1e12, 0),
            D("Result_HETP_m",        "Result: height equivalent to a theoretical plate, m", 0, 1e12, 0),
            D("Result_StripFactor",   "Result: S = m G / L", 0, 1e12, 0),
            D("Result_aw_m2pm3",      "Result: effective interfacial area, m2/m3", 0, 1e12, 0),
            D("Result_kL_mps",        "Result: liquid-side coefficient k_L, m/s", 0, 1e12, 0),
            D("Result_kG_molpm2sPa",  "Result: gas-side coefficient k_G, mol/(m2 s Pa)", 0, 1e12, 0),
            D("Result_Height_m",      "Result: packed height used (given in rating, computed in design), m", 0, 1e12, 0),
            D("Result_YOut",          "Result: solute mole fraction in the gas outlet", 0, 1, 0),
            D("Result_XOut",          "Result: solute mole fraction in the liquid outlet", 0, 1, 0),
        };

        // =================================================================
        //  CAPE-OPEN lifecycle
        // =================================================================
        protected override void OnInitialize()
        {
            Log("OnInitialize called");
            try
            {
                if (Ports.Count() > 0) return;     // guard against a second Initialize

                Name = "PackedColumn";
                Description = "Packed column (Onda mass transfer model)";

                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "GasIn", "Gas inlet (bottom)", CapeOpenPortDirection.Inlet));
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "LiquidIn", "Liquid inlet (top)", CapeOpenPortDirection.Inlet));
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "GasOut", "Gas outlet (top)", CapeOpenPortDirection.Outlet));
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "LiquidOut", "Liquid outlet (bottom)", CapeOpenPortDirection.Outlet));

                foreach (ParamDef p in InputParams.Concat(ResultParams))
                    Parameters.Add(DoubleParameter.Create(p.Name, p.Description,
                        CapeOpenParameterMode.ReadWrite, p.Min, p.Max, p.Default));

                Log("OnInitialize finished (" + Ports.Count() + " ports)");
            }
            catch (Exception ex)
            {
                Log("Error in OnInitialize: " + ex);
                throw;
            }
        }

        protected override void OnEdit()
        {
            Log("OnEdit called");
            MessageBox.Show(_lastSummary, "PackedColumn",
                MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
        }

        // =================================================================
        //  Calculation
        // =================================================================
        protected override void OnCalculate()
        {
            Log("OnCalculate called");
            try
            {
                // ---------- 1. parameters ----------
                int mode = (int)Math.Round(P("CalcMode"));
                double diameter = P("ColumnDiameter_m");
                double height = P("PackedHeight_m");
                double dP = P("PressureDrop_Pa");
                double m = P("HenryM");
                int iSol = (int)Math.Round(P("SoluteIndex"));

                var packing = new PackingData
                {
                    SpecificArea = P("PackingArea_m2pm3"),
                    NominalSize = P("PackingSize_m"),
                    CriticalSurfaceTension = P("PackingSigmaC_Npm"),
                    AreaFactor = P("AreaFactor"),
                    KLFactor = P("kLFactor"),
                    KGFactor = P("kGFactor"),
                };
                var gasProps = new FluidProps
                {
                    Density = P("GasDensity_kgpm3"),
                    Viscosity = P("GasViscosity_Pas"),
                    MolarMass = P("GasMolarMass_kgpmol"),
                    Diffusivity = P("GasDiffusivity_m2ps"),
                };
                var liqProps = new FluidProps
                {
                    Density = P("LiqDensity_kgpm3"),
                    Viscosity = P("LiqViscosity_Pas"),
                    SurfaceTension = P("LiqSurfTension_Npm"),
                    MolarMass = P("LiqMolarMass_kgpmol"),
                    Diffusivity = P("LiqDiffusivity_m2ps"),
                };

                // ---------- 2. streams ----------
                // inlets: duplicates (as in your mixer); outlets: the real objects
                var gasIn = GetMaterialObject("GasIn");
                var liqIn = GetMaterialObject("LiquidIn");
                var gasOut = GetMaterialObject("GasOut", false);
                var liqOut = GetMaterialObject("LiquidOut", false);

                double Gin = gasIn.TotalFlow, Lin = liqIn.TotalFlow;       // mol/s (assumed)
                double Tg = gasIn.Temperature, Tl = liqIn.Temperature;     // K
                double Pg = gasIn.Pressure;                                // Pa
                double[] yv = gasIn.Fraction, xv = liqIn.Fraction;

                // ---------- 3. checks ----------
                Check(Gin > 0, "Gas inlet flow must be > 0.");
                Check(Lin > 0, "Liquid inlet flow must be > 0.");
                Check(yv != null && xv != null, "Stream compositions are not available.");
                Check(yv.Length == xv.Length, "Gas and liquid streams have different component counts.");
                Check(iSol >= 0 && iSol < yv.Length, "SoluteIndex is out of range for the component list.");

                double yIn = yv[iSol], xIn = xv[iSol];
                Check(yIn > 0 && yIn < 1, "Solute mole fraction in the gas inlet must be in (0,1).");
                if (yIn > 0.1) Log("WARNING: gas solute fraction > 0.1, dilute-solute assumption is weak.");

                // ---------- 4. mass transfer: Onda ----------
                double area = Math.PI * diameter * diameter / 4.0;
                double Gm = Gin / area;      // mol/(m2 s)
                double Lm = Lin / area;

                OndaResult onda = PackedColumnModel.Onda(packing, gasProps, liqProps,
                                                         Gm, Lm, Pg, Tg, m);
                double S = onda.StrippingFactor;

                // ---------- 5. rating or design ----------
                double nog, yOut, Z;
                if (mode == 0)
                {
                    Z = height;
                    nog = Z / onda.Hog;
                    yOut = PackedColumnModel.YOutFromNog(S, nog, yIn, xIn, m);
                }
                else
                {
                    yOut = P("TargetYOut");
                    nog = PackedColumnModel.NogColburn(S, yIn, yOut, xIn, m);
                    Z = PackedColumnModel.RequiredHeight(onda.Hog, nog);
                }
                Check(yOut > 0 && yOut < 1, "Computed gas outlet fraction is outside (0,1).");

                // ---------- 6. material balances ----------
                // solute moved from gas to liquid (negative when stripping)
                double nT = Gin * (yIn - yOut) / (1.0 - yOut);
                double Gout = Gin - nT;
                double Lout = Lin + nT;
                Check(Gout > 0 && Lout > 0, "Negative outlet flow, check the specification.");

                double xOut = (Lin * xIn + nT) / Lout;
                if (xOut > 0.1) Log("WARNING: liquid solute fraction > 0.1, dilute-solute assumption is weak.");
                Check(xOut >= 0 && xOut < 1, "Computed liquid outlet fraction is outside [0,1).");

                var yOutVec = new double[yv.Length];
                var xOutVec = new double[xv.Length];
                for (int i = 0; i < yv.Length; i++)
                {
                    yOutVec[i] = (i == iSol) ? yOut : yv[i] * Gin / Gout;
                    xOutVec[i] = (i == iSol) ? xOut : xv[i] * Lin / Lout;
                }

                // ---------- 7. write outlets ----------
                double Ptop = Pg - dP;
                Check(Ptop > 0, "Pressure drop exceeds the inlet pressure.");
                WriteOutlet(gasOut, Ptop, Tg, Gout, yOutVec);
                WriteOutlet(liqOut, Pg, Tl + P("LiqOutDeltaT_K"), Lout, xOutVec);

                // ---------- 8. results ----------
                Publish("Result_HG_m", onda.Hg);
                Publish("Result_HL_m", onda.Hl);
                Publish("Result_HOG_m", onda.Hog);
                Publish("Result_NOG", nog);
                Publish("Result_HETP_m", onda.Hetp);
                Publish("Result_StripFactor", S);
                Publish("Result_aw_m2pm3", onda.EffectiveArea);
                Publish("Result_kL_mps", onda.Kl);
                Publish("Result_kG_molpm2sPa", onda.Kg);
                Publish("Result_Height_m", Z);
                Publish("Result_YOut", yOut);
                Publish("Result_XOut", xOut);

                var sb = new StringBuilder();
                sb.AppendLine(mode == 0 ? "Mode: rating" : "Mode: design");
                sb.Append(onda.ToString());
                sb.AppendLine("N_OG         = " + nog.ToString("G6"));
                sb.AppendLine("Packed height= " + Z.ToString("G6") + " m");
                sb.AppendLine("y_in -> y_out= " + yIn.ToString("G5") + " -> " + yOut.ToString("G5"));
                sb.AppendLine("x_in -> x_out= " + xIn.ToString("G5") + " -> " + xOut.ToString("G5"));
                _lastSummary = sb.ToString();
                if (VerboseLog) Log(_lastSummary);
                Log("OnCalculate finished");
            }
            catch (Exception ex)
            {
                _lastSummary = "Calculation failed: " + ex.Message;
                Log("Error in OnCalculate: " + ex);
                throw;
            }
        }

        // =================================================================
        //  EXTENSION POINTS
        //  * Properties from the streams: in OnCalculate, replace the
        //    constant FluidProps with values read from gasIn / liqIn using
        //    gasIn.CalculateProperty(CapeOpenThermoMaterialPropertyType.XXX,
        //    CapeOpenPhaseType.YYY, CapeOpenCalculationType.ZZZ). Type
        //    "CapeOpenThermoMaterialPropertyType." in Visual Studio to see
        //    which properties your toolkit version exposes (density,
        //    viscosity, ... if present).
        //  * Heat of absorption: compute the liquid temperature rise from
        //    nT * deltaH / (Lout * Cp) and feed it to WriteOutlet instead of
        //    the fixed LiqOutDeltaT_K parameter.
        //  * Non-dilute / curved equilibrium: use
        //    PackedColumnModel.NogNumeric with your own y*(x) function.
        //  * Flooding / pressure drop: add a hydraulic check against the
        //    manufacturer's capacity data and warn when too close to flood.
        //  * Several solutes: loop over components with their own D_G, D_L,
        //    m (or move to a rate-based Maxwell-Stefan model).
        // =================================================================

        // =================================================================
        //  Helpers
        // =================================================================
        // Flash type for fixed T and P. If "TP" does not compile, check the
        // names in CapeOpenFlashType (autocomplete) and change it here.
        private static CapeOpenFlashType FlashTP { get { return CapeOpenFlashType.TP; } }

        private static void WriteOutlet(CapeOpenThermoMaterialObject o, double pressure,
                                        double temperature, double flow, double[] fractions)
        {
            o.Pressure = pressure;
            o.TotalFlow = flow;
            o.Fraction = fractions;
            o.Temperature = temperature;
            o.CalculateEquilibrium(FlashTP);
        }

        private static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        private CapeOpenThermoMaterialObject GetMaterialObject(string portName, bool duplicate = true)
        {
            var matPort = Ports.GetPortByName(portName) as MaterialPort;
            if (matPort == null)
                throw new InvalidOperationException("Port " + portName + " was not found.");
            var matObject = matPort.ConnectedObject as CapeOpenThermoMaterialObject;
            if (matObject == null)
                throw new InvalidOperationException("Port " + portName + " is not connected.");
            return duplicate ? matObject.DuplicatedObject : matObject;
        }

        private double P(string name) { return GetDoubleValueSettings(name); }

        private double GetDoubleValueSettings(string settingName)
        {
            DoubleParameter setting = (DoubleParameter)(Parameters.GetParameterByName(settingName));
            return setting.Value;
        }

        private void SetDoubleValueSettings(string settingName, double value)
        {
            DoubleParameter setting = (DoubleParameter)(Parameters.GetParameterByName(settingName));
            setting.Value = value;
        }

        /// <summary>Write a result into its parameter; never fails the calculation.</summary>
        private void Publish(string name, double value)
        {
            try { SetDoubleValueSettings(name, value); }
            catch (Exception ex) { Log("Could not publish " + name + ": " + ex.Message); }
        }

        private static void Log(string msg)
        {
            try
            {
                System.IO.Directory.CreateDirectory(@"C:\tmp");
                System.IO.File.AppendAllText(LogPath, DateTime.Now + " " + msg + Environment.NewLine);
            }
            catch { /* never let logging break the unit */ }
        }

        //COM registration and unregistration for Cape-Open
        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string clsid = t.GUID.ToString("B");

            // Register CAPE-OPEN component categories
            using (RegistryKey key =
                Registry.ClassesRoot.CreateSubKey(
                    @"CLSID\" + clsid + @"\Implemented Categories"))
            {
                key.CreateSubKey(
                    "{678C09A5-7D66-11D2-A67D-00105A42887F}")?.Close();

                key.CreateSubKey(
                    "{678C09A1-7D66-11D2-A67D-00105A42887F}")?.Close();
            }

            // Read MOSE assembly attributes
            Assembly assembly = t.Assembly;

            object[] attributes =
                assembly.GetCustomAttributes(
                    typeof(CapeOpenUnitInfoAttribute), false);

            // Register CAPE-OPEN metadata
            using (RegistryKey key =
                Registry.ClassesRoot.CreateSubKey(
                    @"CLSID\" + clsid + @"\CapeDescription"))
            {
                foreach (CapeOpenUnitInfoAttribute attr in attributes)
                {
                    switch (attr.CapeOpenUnitInfo)
                    {
                        case CapeOpenUnitInfoEnum.Name:
                            key.SetValue(
                                "Name",
                                attr.CapeOpenInfoValue);
                            break;

                        case CapeOpenUnitInfoEnum.Description:
                            key.SetValue(
                                "Description",
                                attr.CapeOpenInfoValue);
                            break;
                    }
                }
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            string clsid = t.GUID.ToString("B");

            string implementedCategories =
                @"CLSID\" + clsid + @"\Implemented Categories";

            // Remove CAPE-OPEN category registrations
            using (RegistryKey key =
                Registry.ClassesRoot.OpenSubKey(
                    implementedCategories, true))
            {
                if (key != null)
                {
                    key.DeleteSubKey(
                        "{678C09A5-7D66-11D2-A67D-00105A42887F}",
                        false);

                    key.DeleteSubKey(
                        "{678C09A1-7D66-11D2-A67D-00105A42887F}",
                        false);
                }
            }

            // Remove CAPE-OPEN metadata
            Registry.ClassesRoot.DeleteSubKeyTree(
                @"CLSID\" + clsid + @"\CapeDescription",
                false);
        }
    }
}
