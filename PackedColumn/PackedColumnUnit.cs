using CAPEOPEN100;
using Microsoft.Win32;
using Mose.CapeOpenToolkit.UnitProxy;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PackedColumn
{
    // =====================================================================
    //  PackedColumnUnit.cs
    //  CAPE-OPEN packed column model built on the Mose toolkit.
    //  the PackedColumnUnit class is the CAPE-OPEN unit object which interfaces with the process simulator and the packed column model class.
    //  OnEdit shows a dialog for editing the column parameters, OnCalculate runs the model and writes the results to the output streams and result parameters, and ICapeUnitReport.ProduceReport returns a text report of the last calculation.
    //  
    //  Authors: Amogh Krishnanand Atreya & Gaspard Bouchard
    //  =====================================================================

    // CAPE-OPEN unit metadata, used for registration and in the simulator's unit selection dialog
    [ComVisible(true)]
    [ProgId("PackedColumn.PackedColumnUnit")]
    [Guid("052BE404-4FA4-40FD-AB55-1D7DF7D3234F")]
    [ClassInterface(ClassInterfaceType.None)]

    public class PackedColumnUnit : CapeOpenUnitSimple, ICapeUnitReport
    {
        // import the Win32 API function to get the foreground window handle, used for setting the owner of the edit dialog
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        // Logging settings
        private const string LogPath = @"C:\tmp\PackedColumn.log";
        private const bool VerboseLog = true;

        // Last calculation summary, shown in the Edit dialog
        private string _lastSummary = "No calculation has been run yet.";

        // Column model object, created in OnInitialize and used in OnCalculate
        private PackedColumnModel _model = null;

        // -----------------------------------------------------------------
        //  Parameter table: name, description (with units), min, max, default
        // -----------------------------------------------------------------
        // TODO: Add unit column
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
            // --- column ---
            D("ColHeight_m",         "Packed height, m", 0, 1e12, 1),
            D("ColDiameter_m",       "Column diameter, m", 0, 1e12, 1),
        };

        // Results are written back to these parameters after each calculation
        private static readonly ParamDef[] ResultParams =
        {
            D("Result_HETP_m",        "Result: height equivalent to a theoretical plate, m", 0, 1e12, 0),
            D("Result_YOut",          "Result: solute mole fraction in the gas outlet", 0, 1, 0),
            D("Result_XOut",          "Result: solute mole fraction in the liquid outlet", 0, 1, 0),
            D("Result_Holdup_m3",     "Result: holdup volume, m3", 0, 1e12, 0),
        };

        // =================================================================
        //  CAPE-OPEN lifecycle
        // =================================================================
        protected override void OnInitialize()
        {
            Log("OnInitialize called");
            try
            {
                if (_model == null)
                    _model = new PackedColumnModel();


                if (Ports.Count() == 0)
                {     // guard against a second stream declaration

                    // Add ports
                    Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "GasIn", "Gas inlet (bottom)", CapeOpenPortDirection.Inlet));
                    Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "LiquidIn", "Liquid inlet (top)", CapeOpenPortDirection.Inlet));
                    Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "GasOut", "Gas outlet (top)", CapeOpenPortDirection.Outlet));
                    Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "LiquidOut", "Liquid outlet (bottom)", CapeOpenPortDirection.Outlet));

                    // Add input parameters
                    foreach (ParamDef p in InputParams)
                        Parameters.Add(DoubleParameter.Create(p.Name, p.Description,
                            CapeOpenParameterMode.ReadWrite, p.Min, p.Max, p.Default));

                    // Add result parameters
                    foreach (ParamDef p in ResultParams)
                        Parameters.Add(DoubleParameter.Create(p.Name, p.Description,
                            CapeOpenParameterMode.Read, p.Min, p.Max, p.Default));

                }

                Log("OnInitialize finished (" + Ports.Count() + " ports)");
            }
            catch (Exception ex)
            {
                Log("Error in OnInitialize: " + ex);
                throw;
            }
        }

        // Helper class to wrap a Win32 window handle for use with ShowDialog in On Edit

        private sealed class WinHandle : IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public WinHandle(IntPtr h) { Handle = h; }
        }
        protected override void OnEdit()
        {
            Log("OnEdit called, apartment=" + Thread.CurrentThread.GetApartmentState());

            // Gather everything on the calling thread
            var fields = InputParams.Select(p => new EditField
            {
                Name = p.Name,
                Description = p.Description,
                Min = p.Min,
                Max = p.Max,
                Value = P(p.Name)
            }).ToList();
            string report = BuildReport(ReportSummary);

            IntPtr owner = GetForegroundWindow();          // Aspen's active window
            if (owner == IntPtr.Zero)
                owner = Process.GetCurrentProcess().MainWindowHandle;

            bool accepted = false;
            Exception error = null;

            var t = new Thread(() =>
            {
                try
                {
                    using (var dlg = new ColumnEditForm("Packed Column", fields, report))
                    {
                        accepted = dlg.ShowDialog(new WinHandle(owner)) == DialogResult.OK;
                    }
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();                                      

            if (error != null) { Log("Error in OnEdit: " + error); throw error; }

            // Write back on the calling thread
            if (accepted)
                foreach (EditField f in fields)
                    if (P(f.Name) != f.Value)
                    {
                        SetDoubleValueSettings(f.Name, f.Value);
                        Log("Parameter " + f.Name + " set to " + f.Value);
                        Status = CapeOpenValidationStatus.NotYetValidated;  // force revalidation
                    }

            UpdateModelSpecs();  // update the model with the new parameters
        }

        public override bool Validate(ref string message)
        {
            return base.Validate(ref message);
        }

        // =================================================================
        //  Calculation
        // =================================================================
        protected override void OnCalculate()
        {
            Log("OnCalculate called");
            try
            {
                // ---------- 1. streams ----------
                // inlets: duplicates (as in your mixer); outlets: the real objects
                var gasIn = GetMaterialObject("GasIn");
                var liqIn = GetMaterialObject("LiquidIn");
                var gasOut = GetMaterialObject("GasOut", false);
                var liqOut = GetMaterialObject("LiquidOut", false);

                // Get inlet properties 
                double Gin = gasIn.TotalFlow, Lin = liqIn.TotalFlow;       // mol/s (assumed)
                double Tg = gasIn.Temperature, Tl = liqIn.Temperature;     // K
                double Pg = gasIn.Pressure;                                // Pa
                double[] yv = gasIn.Fraction, xv = liqIn.Fraction;

                // ---------- 3. checks ----------
                Check(Gin > 0, "Gas inlet flow must be > 0."); // check input validity

                // ---------- 4. calculation ----------
                UpdateModelOCs(gasIn, liqIn);
                _model.Calculate();
                var res = _model.Results;

                // ---------- 6. check material and energy balances & other eg pressure drop ----------
                // todo add checks
                Check(res.GasMassFlowOut > 0 && res.LiquidMassFlowOut > 0, "Negative outlet flow, check the specification.");

                // ---------- 7. write outlets ----------
                WriteOutlet(gasOut, 10, 273.15, res.GasMassFlowOut, new double[] { 0, 0.01, 0.99 });
                WriteOutlet(liqOut, 10, 273.15, res.LiquidMassFlowOut, new double[] { 0.9, 0.1, 0 });


                // ---------- 8. publish results 

                // Update the result parameters with the calculated values
                Publish("Result_Holdup_m3", res.LiquidHoldup);

                _lastSummary = BuildReport(ReportSummary);

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
        // Column model handling
        // =================================================================

        public void UpdateModelSpecs()
        {
            Log("UpdateModel called");
            if (_model == null) return;

            _model.SetColumnSpecs(columnDiameter: P("ColDiameter_m"), packingHeight: P("ColHeight_m"));
            // _model.SetPackingParameters();
            return;
        }

        public void UpdateModelOCs(CapeOpenThermoMaterialObject gasIn, CapeOpenThermoMaterialObject liqIn)
        {
            if (_model == null) return;

            ICapeThermoMaterialObject g = gasIn.ConnectedObject;
            ICapeThermoMaterialObject l = liqIn.ConnectedObject;

            _model.SetOperatingConditions(
                rhoL: PhaseProp(l, "density", "Liquid", "mass"),
                etaL: PhaseProp(l, "viscosity", "Liquid", null),
                liquidMassFlow: PhaseMassFlow(l, "Liquid"),
                rhoV: PhaseProp(g, "density", "Vapor", "mass"),
                etaV: PhaseProp(g, "viscosity", "Vapor", null),
                gasMassFlow: PhaseMassFlow(g, "Vapor"));
        }


        // =================================================================
        //  CAPE-OPEN reporting
        // =================================================================

        // Define the report types
        // TODO: add more report types if needed, such as a detailed report with the column profile, etc.
        private const string ReportSummary = "Summary";
        private string _selectedReport = ReportSummary;

        // ICapeUnitReport.reports : names of the available reports
        object ICapeUnitReport.reports
        {
            get { Log("get_reports"); return new string[] { ReportSummary }; }
        }


        // ICapeUnitReport.selectedReport
        string ICapeUnitReport.selectedReport
        {
            get { Log("get_selectedReport"); return _selectedReport; }
            set { Log("set_selectedReport " + value); _selectedReport = value; }
        }

        // ICapeUnitReport.ProduceReport
        void ICapeUnitReport.ProduceReport(ref string message)
        {
            Log("ProduceReport");
            message = BuildReport(_selectedReport);  
        }


        private string BuildReport(string which)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var res = _model.Results;
            var sb = new StringBuilder();
            sb.AppendLine("PACKED COLUMN REPORT");
            sb.AppendLine("====================");
            if (!res.Calculated)
            {
                sb.AppendLine("No successful calculation yet.");
                return sb.ToString();
            }

            sb.Append(_model.ToString());

            return sb.ToString();
        }

        // =================================================================
        //  Helper methods
        // =================================================================

        static double First(object o, string what)
        {
            double[] a = o as double[];
            if (a == null || a.Length == 0 || double.IsNaN(a[0]))
                throw new InvalidOperationException("Material returned no value for " + what + ".");
            return a[0];
        }

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

        static double PhaseProp(ICapeThermoMaterialObject mat, string prop, string phase, string basis)
        {
            mat.CalcProp(new string[] { prop }, new string[] { phase }, "Mixture");
            return First(mat.GetProp(prop, phase, null, "Mixture", basis), prop + " (" + phase + ")");
        }

        static double PhaseMassFlow(ICapeThermoMaterialObject mat, string phase)
        {
            // total mass flow, kg/s, times the phase's mass fraction
            double total = First(mat.GetProp("totalFlow", "Overall", null, null, "mass"), "totalFlow");
            mat.CalcProp(new string[] { "phaseFraction" }, new string[] { phase }, "Mixture");
            double frac = First(mat.GetProp("phaseFraction", phase, null, "Mixture", "mass"), "phaseFraction (" + phase + ")");
            return total * frac;
        }

        // Helper method get double parameter values
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

        // Writes a result to its corresponding parameter and logs it. Exceptions are caught and logged, but not rethrown, to avoid breaking the unit.
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
