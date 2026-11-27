using CAPEOPEN100;
using Microsoft.Win32;
using Mose.CapeOpenToolkit.UnitProxy;
using PackedColumn.PackedColumn;
using PackingLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using IStream = System.Runtime.InteropServices.ComTypes.IStream;

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

    public class PackedColumnUnit : CapeOpenUnitSimple, ICapeUnitReport, IPersistStream, IPersistStreamInit
    {
        // import the Win32 API function to get the foreground window handle, used for setting the owner of the edit dialog
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        // Resolves dependency DLLs (System.Memory, etc.) from this DLL's folder.
        static PackedColumnUnit()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromUnitFolder;
        }

        private static Assembly ResolveFromUnitFolder(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);

            // skip resource satellite lookups
            if (requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                return null;

            string dir = System.IO.Path.GetDirectoryName(typeof(PackedColumnUnit).Assembly.Location);
            string path = System.IO.Path.Combine(dir, requested.Name + ".dll");

            if (!System.IO.File.Exists(path))
                return null;

            // already loaded (any version)? reuse it
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(a.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                    return a;

            return Assembly.LoadFrom(path);
        }

        private readonly Guid _instanceId = Guid.NewGuid();

        // Logging settings
        private const string LogPath = @"C:\tmp\PackedColumn.log";
        private const bool VerboseLog = true;

        // Last calculation summary, shown in the Edit dialog
        private string _lastSummary = "No calculation has been run yet.";

        // Column model object, created in OnInitialize and used in OnCalculate
        private PackedColumnModel _model = null;

        // Packing library created in OnInitialize
        private PackingManager _packingManager = null;
        private string _defaultLibraryPath = ComputeDefaultLibraryPath();

        // relative to the DLL location
        private static string ComputeDefaultLibraryPath()
        {
            string dllDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return System.IO.Path.GetFullPath(
                System.IO.Path.Combine(dllDir, @"..\..\..\Packings\library.json"));
        }
        private string _loadedLibraryPath = null;

        // packing related fields
        private string _packingId = null;
        private string _packingFamily = null;
        private string _packingName = null;
        private Packing _selectedPacking = null;

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
            D("Temperature_C",       "Isothermal column temperature, Celcius", 0, 1e12, 25),
            // --- mass transfer ---
            D("DiffusivityLiquid_m2s", "Solute diffusivity in liquid, m2/s", 1e-12, 1e-6, 1e-9),
            D("DiffusivityGas_m2s",    "Solute diffusivity in gas, m2/s",    1e-7,  1e-3, 1e-5),
        };

        // Results are written back to these parameters after each calculation
        private static readonly ParamDef[] ResultParams =
        {
            D("Result_NTU_Gas",       "Result: number of gas side transfer units", 0, 1e12, 0),
            D("Results_NTU_Liquid",   "Result: number of liquid size transfer units", 0, 1e12, 0),
            D("Result_YOut",          "Result: solute mole fraction in the gas outlet", 0, 1, 0),
            D("Result_XOut",          "Result: solute mole fraction in the liquid outlet", 0, 1, 0),
            D("Result_Holdup_m3",     "Result: holdup volume, m3", 0, 1e12, 0),
            D("Result_VG_m/s",        "Result: Gas superficial velocity, m/s", 0, 1e12, 0),
            D("Result_VL_m/s",        "Result: Liquid superficial velocity, m/s", 0, 1e12, 0),
            D("Result_kG_1/s",        "Result: Gas volumetric mass transfer coefficient, 1/s", 0, 1e12, 0),
            D("Result_kL_1/s",        "Result: Liquid volumetric mass transfer coefficient, 1/s", 0, 1e12, 0),
        };



        // =================================================================
        //  CAPE-OPEN lifecycle
        // =================================================================
        protected override void OnInitialize()
        {
            Log("OnInitialize called");
            Log("INSTANCE ID: " + _instanceId);
            try
            {
                if (_model == null)
                    _model = new PackedColumnModel();

                try { EnsurePackingLibrary(); } catch { }

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

                    // Add packing library path
                    Parameters.Add(OptionParameter.Create(
                        "PackingLibraryPath",
                        "Path to the packing library JSON file",
                        CapeOpenParameterMode.ReadWrite,
                        null,      // possible option (null)
                        false,
                        _defaultLibraryPath)); //default value

                    // Add the solute name parameter
                    Parameters.Add(OptionParameter.Create("SoluteName",
                        "Transferring component (exact name in the PME component list)",
                        CapeOpenParameterMode.ReadWrite, null, false, "Carbon dioxide"));

                }

                // A PME may call Load before Initialize; apply any state that was waiting for the parameters to exist
                if (_pending != null)
                {
                    ApplyState(_pending);
                    _pending = null;
                    _savedSnapshot = SerializeState();
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

            // ensure packing library is loaded
            EnsurePackingLibrary();

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
            Packing chosen = null;
            Exception error = null;

            var t = new Thread(() =>
            {
                try
                {
                    using (var dlg = new ColumnEditForm("Packed Column", fields, report,
                                                        _packingManager, _packingFamily, _packingName))
                    {
                        accepted = dlg.ShowDialog(new WinHandle(owner)) == DialogResult.OK;
                        if (accepted) chosen = dlg.SelectedPacking;
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

            if (accepted && chosen != null)
            {
                _selectedPacking = chosen;
                _packingId = chosen.Id;          // <-- adapt to wherever your Packing exposes its ID
                _packingFamily = chosen.Identity.Family;
                _packingName = chosen.Identity.Name;
                Status = CapeOpenValidationStatus.NotYetValidated;
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
                ResolveSelectedPacking();
                Check(_selectedPacking != null, "No packing selected. Open Edit and choose one.");
                EnsurePackingLibrary();
                UpdateModelSpecs();                                   // model was only updated in OnEdit before

                var gasIn = GetMaterialObject("GasIn");
                var liqIn = GetMaterialObject("LiquidIn");
                var gasOut = GetMaterialObject("GasOut", false);
                var liqOut = GetMaterialObject("LiquidOut", false);

                double V = gasIn.TotalFlow, L = liqIn.TotalFlow;      // mol/s
                double T = GetDoubleValueSettings("Temperature_C") + 273.15;
                double Pg = gasIn.Pressure, Pl = liqIn.Pressure;
                double[] yv = gasIn.Fraction, xv = liqIn.Fraction;
                Check(V > 0 && L > 0, "Gas and liquid inlet flows must be > 0.");

                string solute = GetStringValueSettings("SoluteName");
                int i = Array.IndexOf(gasIn.Ids, solute);
                Check(i >= 0, "SoluteName '" + solute + "' not found. Components: " + string.Join(", ", gasIn.Ids));

                // equilibrium slope at mean temperature and gas inlet pressure (dilute, isothermal)
                double m = KSlope(gasIn, liqIn, i, T, Pg);

                double sigma = SurfaceTension(gasIn, liqIn, T, Pg);

                UpdateModelOCs(gasIn, liqIn);
                _model.SetMassTransfer(L, V, m, yv[i], xv[i], sigma,
                                       P("DiffusivityLiquid_m2s"), P("DiffusivityGas_m2s"));
                _model.Calculate();
                var res = _model.Results;

                Check(!res.IsFlooded, "Column is flooded (" + res.PercentFlood.ToString("F0") + "% of flood).");

                // solute balance: N mol/s moves from gas to liquid (negative when stripping)
                double N = V * (yv[i] - res.GasMoleFractionOutlet);

                double[] gFlow = yv.Select(y => y * V).ToArray();
                double[] lFlow = xv.Select(x => x * L).ToArray();
                gFlow[i] -= N;
                lFlow[i] += N;
                Check(gFlow[i] >= 0 && lFlow[i] >= 0, "Solute balance went negative, check m and the specification.");

                double gTot = gFlow.Sum(), lTot = lFlow.Sum();
                WriteOutlet(gasOut, Pg - res.PressureDrop, T, gTot, gFlow.Select(f => f / gTot).ToArray());
                WriteOutlet(liqOut, Pl, T, lTot, lFlow.Select(f => f / lTot).ToArray());

                Publish("Result_YOut", res.GasMoleFractionOutlet);
                Publish("Result_XOut", res.LiquidMoleFractionOutlet);
                Publish("Result_NTU_Gas", res.NTUGas);
                Publish("Result_NTU_Liquid", res.NTULiquid);
                Publish("Result_Holdup_m3", res.LiquidHoldup * Math.PI / 4 * P("ColDiameter_m") * P("ColDiameter_m") * P("ColHeight_m"));
                Publish("Result_VG_m/s", res.GasSuperficialVelocity);
                Publish("Result_VL_m/s", res.LiquidSuperficialVelocity);
                Publish("Result_kG_1/s", res.GasVolumetricMTC);
                Publish("Result_kL_1/s", res.LiquidVolumetricMTC);

                _lastSummary = BuildReport(ReportSummary);
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
            ResolveSelectedPacking();

            _model.SetColumnSpecs(columnDiameter: P("ColDiameter_m"), packingHeight: P("ColHeight_m"));
            _model.SetPacking(_selectedPacking);
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

        // Check packing library
        private void EnsurePackingLibrary()
        {
            string path = GetStringValueSettings("PackingLibraryPath");

            if (_packingManager != null && path == _loadedLibraryPath)
                return;   // already loaded from this path

            try
            {
                _packingManager = PackingManager.Load(path);
                _loadedLibraryPath = path;
                Log("Packing library loaded from " + path);
            }
            catch (Exception ex)
            {
                _packingManager = null;
                _loadedLibraryPath = null;
                Log("Failed to load packing library from " + path + ": " + ex.Message);
                throw new InvalidOperationException("Could not load packing library: " + path, ex);
            }
        }

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

        private CapeOpenThermoMaterialObject GetMaterialObject  (string portName, bool duplicate = true)
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
            double frac = First(mat.GetProp("phaseFraction", phase, null, null, "mass"), "phaseFraction (" + phase + ")");
            return total * frac;
        }

        //
        private static double KSlope(CapeOpenThermoMaterialObject gasIn, CapeOpenThermoMaterialObject liqIn,
                             int i, double T, double P)
        {
            return FugacityCoeff(liqIn, "Liquid", i, T, P) / FugacityCoeff(gasIn, "Vapor", i, T, P);
        }

        static double FugacityCoeff(CapeOpenThermoMaterialObject feed, string phase, int i, double T, double P)
        {
            var work = feed.DuplicateMaterialObject();
            double[] z = work.Fraction;
            if (z[i] < 1e-8)                                   // avoid zero-fraction failures
            {
                z[i] = 1e-8;
                double s = z.Sum();
                for (int k = 0; k < z.Length; k++) z[k] /= s;
                work.Fraction = z;
            }
            work.Temperature = T;
            work.Pressure = P;
            work.CalculateEquilibrium(CapeOpenFlashType.TP);

            var mat = work.ConnectedObject;
            mat.CalcProp(new string[] { "fugacityCoefficient" }, new string[] { phase }, "Mixture");
            double[] phi = (double[])mat.GetProp("fugacityCoefficient", phase, null, "Mixture", null);
            return phi[i];
        }

        // Surface tension (N/m) of the gas-liquid interface, from an equilibrium mixture of both feeds
        private static double SurfaceTension(CapeOpenThermoMaterialObject gasIn,
                                             CapeOpenThermoMaterialObject liqIn, double T, double press)
        {
            double V = gasIn.TotalFlow, L = liqIn.TotalFlow;
            double[] y = gasIn.Fraction, x = liqIn.Fraction;
            double[] n = y.Select((yk, k) => V * yk + L * x[k]).ToArray();
            double nTot = n.Sum();

            var work = liqIn.DuplicateMaterialObject();
            work.Temperature = T;
            work.Pressure = press;
            work.TotalFlow = nTot;
            work.Fraction = n.Select(v => v / nTot).ToArray();
            work.CalculateEquilibrium(CapeOpenFlashType.TP);   // both phases now exist

            var mat = work.ConnectedObject;
            mat.CalcProp(new[] { "surfaceTension" }, new[] { "VaporLiquid" }, "Mixture");
            return First(mat.GetProp("surfaceTension", "VaporLiquid", null, "Mixture", null), "surfaceTension");
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

        private string GetStringValueSettings(string name)
        {
            var setting = (OptionParameter)Parameters.GetParameterByName(name);
            return setting.Value;
        }

        private void SetStringValueSettings(string name, string value)
        {
            var setting = (OptionParameter)Parameters.GetParameterByName(name);
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

        // =================================================================
        //  COM persistence (IPersistStream / IPersistStreamInit)
        // =================================================================
        private const int PersistVersion = 1;
        private static readonly string[] PersistedStringParams = { "PackingLibraryPath", "SoluteName" };

        private byte[] _savedSnapshot;       // serialized state at last Save/Load; used for IsDirty
        private PersistedState _pending;     // state loaded before OnInitialize created the parameters

        private sealed class PersistedState
        {
            public Dictionary<string, double> Doubles = new Dictionary<string, double>();
            public Dictionary<string, string> Strings = new Dictionary<string, string>();
            public string PackingId;
        }

        // ---- explicit interface implementations, all delegating to the helpers below ----
        void IPersistStream.GetClassID(out Guid id) { id = typeof(PackedColumnUnit).GUID; }
        void IPersistStreamInit.GetClassID(out Guid id) { id = typeof(PackedColumnUnit).GUID; }

        int IPersistStream.IsDirty() { return PersistIsDirty(); }
        int IPersistStreamInit.IsDirty() { return PersistIsDirty(); }

        void IPersistStream.Load(IStream s) { PersistLoad(s); }
        void IPersistStreamInit.Load(IStream s) { PersistLoad(s); }

        void IPersistStream.Save(IStream s, bool clear) { PersistSave(s, clear); }
        void IPersistStreamInit.Save(IStream s, bool clear) { PersistSave(s, clear); }

        void IPersistStream.GetSizeMax(out long size) { size = PersistSizeMax(); }
        void IPersistStreamInit.GetSizeMax(out long size) { size = PersistSizeMax(); }

        void IPersistStreamInit.InitNew()
        {
            Log("InitNew");
            _pending = null;
            _savedSnapshot = null;       // brand new object: dirty until first save
        }

        // ---- helpers ----
        private int PersistIsDirty()
        {
            // S_OK (0) = dirty, S_FALSE (1) = clean
            try
            {
                byte[] now = SerializeState();
                return (_savedSnapshot != null && now.SequenceEqual(_savedSnapshot)) ? 1 : 0;
            }
            catch { return 0; }
        }

        private long PersistSizeMax()
        {
            try { return SerializeState().Length + 4; }
            catch { return 1 << 20; }
        }

        private void PersistSave(IStream stm, bool clearDirty)
        {
            Log("PersistSave, clearDirty=" + clearDirty);
            byte[] payload = SerializeState();
            byte[] len = BitConverter.GetBytes(payload.Length);
            stm.Write(len, len.Length, IntPtr.Zero);
            stm.Write(payload, payload.Length, IntPtr.Zero);
            if (clearDirty) _savedSnapshot = payload;
        }

        private void PersistLoad(IStream stm)
        {
            Log("PersistLoad");
            int length = BitConverter.ToInt32(ReadExactly(stm, 4), 0);
            if (length < 0 || length > 64 * 1024 * 1024)
                throw new InvalidDataException("Corrupt PackedColumn state (length " + length + ").");

            PersistedState state = ReadState(ReadExactly(stm, length));

            if (Ports.Count() > 0)               // parameters already exist (Initialize came first)
            {
                ApplyState(state);
                _pending = null;
                _savedSnapshot = SerializeState();
            }
            else                                 // Load came before Initialize; OnInitialize will apply it
            {
                _pending = state;
            }
        }

        private byte[] SerializeState()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, Encoding.UTF8))
                {
                    w.Write(PersistVersion);

                    // Doubles by name (inputs and results), so adding or removing parameters later stays compatible
                    var doubles = InputParams.Concat(ResultParams).ToList();
                    w.Write(doubles.Count);
                    foreach (ParamDef p in doubles)
                    {
                        w.Write(p.Name);
                        w.Write(P(p.Name));
                    }

                    w.Write(PersistedStringParams.Length);
                    foreach (string name in PersistedStringParams)
                    {
                        w.Write(name);
                        w.Write(GetStringValueSettings(name) ?? "");
                    }

                    w.Write(_packingId ?? "");
                }
                return ms.ToArray();
            }
        }

        private static PersistedState ReadState(byte[] bytes)
        {
            var s = new PersistedState();
            using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                int version = r.ReadInt32();
                if (version > PersistVersion)
                    throw new InvalidDataException("State was saved by a newer version of PackedColumn (v" + version + ").");

                int nd = r.ReadInt32();
                for (int i = 0; i < nd; i++)
                {
                    string name = r.ReadString();
                    s.Doubles[name] = r.ReadDouble();
                }

                int ns = r.ReadInt32();
                for (int i = 0; i < ns; i++)
                {
                    string name = r.ReadString();
                    s.Strings[name] = r.ReadString();
                }

                string id = r.ReadString();
                s.PackingId = id.Length == 0 ? null : id;
            }
            return s;
        }

        private void ApplyState(PersistedState s)
        {
            foreach (var kv in s.Strings)
                try { SetStringValueSettings(kv.Key, kv.Value); }
                catch (Exception ex) { Log("Could not restore " + kv.Key + ": " + ex.Message); }

            foreach (var kv in s.Doubles)
                try { SetDoubleValueSettings(kv.Key, kv.Value); }
                catch (Exception ex) { Log("Could not restore " + kv.Key + ": " + ex.Message); }

            _packingId = s.PackingId;
            _selectedPacking = null;
            ResolveSelectedPacking();

            Status = CapeOpenValidationStatus.NotYetValidated;
            Log("State restored (" + s.Doubles.Count + " doubles, packing id=" + _packingId + ")");
        }

        // Rebuilds _selectedPacking from the persisted family/name once the library is available
        private void ResolveSelectedPacking()
        {
            if (_selectedPacking != null || _packingId == null) return;
            try
            {
                EnsurePackingLibrary();
                _selectedPacking = _packingManager.GetPacking(_packingId); 

                if (_selectedPacking != null)
                {
                    // keep the family/name that ColumnEditForm expects in sync
                    _packingFamily = _selectedPacking.Identity.Family;
                    _packingName = _selectedPacking.Identity.Name;
                }
                else
                {
                    Log("Packing id '" + _packingId + "' not found in library");
                }
            }
            catch (Exception ex)
            {
                Log("Could not resolve packing id " + _packingId + ": " + ex.Message);
            }
        }

        // IStream.Read may return fewer bytes than requested, so loop until we have them all
        private static byte[] ReadExactly(IStream stm, int count)
        {
            var buf = new byte[count];
            IntPtr pRead = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                int total = 0;
                while (total < count)
                {
                    var tmp = new byte[count - total];
                    Marshal.WriteInt32(pRead, 0);
                    stm.Read(tmp, tmp.Length, pRead);
                    int got = Marshal.ReadInt32(pRead);
                    if (got <= 0) throw new EndOfStreamException();
                    Buffer.BlockCopy(tmp, 0, buf, total, got);
                    total += got;
                }
            }
            finally { Marshal.FreeCoTaskMem(pRead); }
            return buf;
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
