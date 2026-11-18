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
                if (Ports.Count() > 0) return;     // guard against a second stream declaration

                // Add ports
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "GasIn", "Gas inlet (bottom)", CapeOpenPortDirection.Inlet));
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "LiquidIn", "Liquid inlet (top)", CapeOpenPortDirection.Inlet));
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "GasOut", "Gas outlet (top)", CapeOpenPortDirection.Outlet));
                Ports.Add(CapeOpenUnitPort.Create(CapeOpenPortType.Material, "LiquidOut", "Liquid outlet (bottom)", CapeOpenPortDirection.Outlet));

                // Add parameters
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
                // todo call model calculation

                // ---------- 6. check material and energy balances & other eg pressure drop ----------
                // todo add checks
                // eg. Check(Gout > 0 && Lout > 0, "Negative outlet flow, check the specification.");

                // ---------- 7. write outlets ----------
                // eg. WriteOutlet(gasOut, Ptop, Tg, Gout, yOutVec);


                // ---------- 8. publish results ----------
                // todo, review publish function and report generation
                // eg. Publish("Result_HG_m", PackedColumnModel.holdup);


                //update summary and log
                var sb = new StringBuilder();
                // eg. sb.AppendLine(mode == 0 ? "Mode: rating" : "Mode: design");
                
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
