using PackingLibrary;
using System;
using System.Drawing.Text;
using System.Text;

namespace PackedColumn
{
    // =====================================================================
    //  PackedColumnModel.cs
    //  No CAPE-OPEN / toolkit dependencies so it can be unit
    //  tested in a console project and reused elsewhere.
    //
    //  All quantities are SI:
    //    flows      mol/s            fluxes   mol/(m2 s)  or  kg/(m2 s)
    //    pressure   Pa               temperature K
    //    lengths    m                areas    m2/m3
    //    viscosity  Pa s             diffusivity m2/s
    //    surface tension N/m         molar mass kg/mol
    //
    //  The model uses the updated version of the Billet & Schultes, 1999 correlation to predict mass transport and hydraulic properties of packed columns. 
    // =====================================================================

    public class PackedColumnModel
    {
        public BilletSchultesModel BilletSchultes { get; private set; }

        public void SetColumnSpecs(double columnDiameter, double packingHeight)
        {
            var m = BilletSchultes;
            m.ColumnDiameter = columnDiameter;
            m.PackingHeight = packingHeight;
            m.LiquidSuperficialVelocity = null;
            m.GasSuperficialVelocity = null;
            _hasBeenCalculated = false;  // reset calculation flag
        }

        public void SetPacking(Packing packing)
        {
            var m = BilletSchultes;
            var c = packing.BilletSchultes.Constants;
            m.SpecificSurfaceArea = packing.SpecificSurfaceArea;
            m.VoidFraction = packing.VoidFraction;
            m.CS = c.CS; m.CFI = c.CFI; m.Ch = c.Ch; m.CP0 = c.CP0; m.CL = c.CL; m.CV = c.CV;
            _hasBeenCalculated = false;  // reset calculation flag
        }

        public void SetOperatingConditions(
            double rhoL, double etaL, double liquidMassFlow,
            double rhoV, double etaV, double gasMassFlow)
        {
            if (rhoL <= 0 || rhoV <= 0)
                throw new ArgumentException("Phase densities must be > 0.");

            var m = BilletSchultes;
            m.LiquidDensity = rhoL;
            m.LiquidViscosity = etaL;
            m.GasDensity = rhoV;
            m.GasViscosity = etaV;

            m.LiquidMassFlowRate = liquidMassFlow;
            m.GasMassFlowRate = gasMassFlow;
            m.LiquidVolumetricFlowRate = liquidMassFlow / rhoL;   // m3/s
            m.GasVolumetricFlowRate = gasMassFlow / rhoV;
            m.LiquidSuperficialVelocity = null;                   // force re-derivation
            m.GasSuperficialVelocity = null;
            _hasBeenCalculated = false;
        }

        private double _L, _V;
        private bool _massTransfer;
        public void SetMassTransfer(double L_mol, double V_mol, double m, double yIn, double xIn,
                            double sigma, double dL, double dV)
        {
            var b = BilletSchultes;
            _L = L_mol; _V = V_mol; _massTransfer = true;
            b.EquilibriumSlope = m;
            b.GasMoleFractionInlet = yIn;
            b.LiquidMoleFractionInlet = xIn;
            b.LiquidSurfaceTension = sigma;
            b.LiquidDiffusivity = dL;
            b.GasDiffusivity = dV;
            _hasBeenCalculated = false;
        }

        private bool _hasBeenCalculated = false;

        public ColumnResults Results { get; private set; } = new ColumnResults();

        public PackedColumnModel()
        {
            BilletSchultes = new BilletSchultesModel();
        }

        public void Calculate()
        {
            if (BilletSchultes == null)
            {
                throw new InvalidOperationException("BilletSchultes model is not initialized.");
            }

            var b = BilletSchultes;

            // Perform calculations using the Billet & Schultes model
            b.CalculateHydraulics();

            if(_massTransfer && !b.IsFlooded)
            {
                b.CalculateMassTransferCoefficients();
                b.CalculateHTU(_L, _V);
                b.CalculateSeparationPerformance(_L, _V);
            }


            Results = new ColumnResults
            {
                Calculated = true,
                IsFlooded = b.IsFlooded,
                PressureDropPerHeight = b.PressureDropPerHeight ?? double.NaN,
                PressureDrop = b.PressureDrop ?? double.NaN,
                LiquidHoldup = b.LiquidHoldup ?? double.NaN,
                LoadingVelocity = b.LoadingGasVelocity ?? double.NaN,
                FloodingVelocity = b.FloodingGasVelocity ?? double.NaN,
                PercentFlood = b.FloodingPercentage ?? double.NaN,
                GasVelocity = b.GasSuperficialVelocity ?? double.NaN,
                LiquidVelocity = b.LiquidSuperficialVelocity ?? double.NaN,
                GasMassFlowOut = b.GasMassFlowRate ?? double.NaN,
                LiquidMassFlowOut = b.LiquidMassFlowRate ?? double.NaN,
                GasMoleFractionOutlet = b.GasMoleFractionOutlet ?? double.NaN,
                LiquidMoleFractionOutlet = b.LiquidMoleFractionOutlet ?? double.NaN,
                NTUGas = b.NTUGas ?? double.NaN,
                NTULiquid = b.NTULiquid ?? double.NaN,
                GasSuperficialVelocity = b.GasSuperficialVelocity ?? double.NaN,
                LiquidSuperficialVelocity = b.LiquidSuperficialVelocity ?? double.NaN,
                GasVolumetricMTC = b.GasVolumetricMassTransferCoefficient ?? double.NaN,
                LiquidVolumetricMTC = b.LiquidVolumetricMassTransferCoefficient ?? double.NaN,
            };

            _hasBeenCalculated = true;
        }

        public override string ToString()
        {
            if (!_hasBeenCalculated) // check if results are available
            {
                return "This is a PackedColumnModel instance. No calculations have been performed yet.";
            }

            // generate a string representation of the model results
            var sb = new StringBuilder();
            sb.AppendLine("Packed Column Model Results:");
            sb.AppendLine($"Liquid Holdup: {BilletSchultes.LiquidHoldup}");
            return sb.ToString();
        }
    }

    public class BilletSchultesModel
    {
        // ============================================================
        // 1. MATERIAL PROPERTIES (Retrieved from CAPE-OPEN PME)
        // ============================================================

        // Operating conditions
        public double? Temperature { get; set; }          // T [K]
        public double? Pressure { get; set; }             // P [Pa]

        // Liquid properties
        public double? LiquidDensity { get; set; }        // rho_L [kg/m3]
        public double? LiquidViscosity { get; set; }      // eta_L [Pa.s]
        public double? LiquidKinematicViscosity { get; set; } // nu_L [m2/s]
        public double? LiquidSurfaceTension { get; set; } // sigma_L [N/m]
        public double? LiquidDiffusivity { get; set; }    // D_L [m2/s]
        public double? LiquidMolecularWeight { get; set; }// M_L [kg/kmol]

        // Gas / vapour properties
        public double? GasDensity { get; set; }           // rho_V [kg/m3]
        public double? GasViscosity { get; set; }         // eta_V [Pa.s]
        public double? GasKinematicViscosity { get; set; }// nu_V [m2/s]
        public double? GasDiffusivity { get; set; }       // D_V [m2/s]
        public double? GasMolecularWeight { get; set; }   // M_V [kg/kmol]

        // ============================================================
        // 2. COLUMN SPECIFICATIONS AND OPERATING CONDITIONS
        // ============================================================

        public double? ColumnDiameter { get; set; }      // d [m]

        // User-defined packing height (rating mode)
        public double? PackingHeight { get; set; }        // H [m]

        // Column cross-sectional area
        public double? ColumnCrossSectionArea { get; set; } // A [m2]

        // Liquid and gas flow rates
        public double? LiquidMassFlowRate { get; set; }   // kg/s
        public double? GasMassFlowRate { get; set; }      // kg/s

        public double? LiquidMolarFlowRate { get; set; }  // L [kmol/h]
        public double? GasMolarFlowRate { get; set; }     // V [kmol/h]

        public double? LiquidVolumetricFlowRate { get; set; } // m3/s
        public double? GasVolumetricFlowRate { get; set; }    // m3/s

        // Superficial velocities
        public double? LiquidSuperficialVelocity { get; set; } // u_L [m/s]
        public double? GasSuperficialVelocity { get; set; }    // u_V [m/s]

        // ============================================================
        // 3. PACKING SPECIFICATIONS
        // ============================================================

        public string PackingName { get; set; }
        public string PackingType { get; set; } // Random or Structured

        public double? PackingSize { get; set; }         // Nominal size [m]
        public double? SpecificSurfaceArea { get; set; } // a [m2/m3]
        public double? VoidFraction { get; set; }        // epsilon [-]
        public double? PackingElementDensity { get; set; } // N [1/m3]

        // Billet and Schultes packing-specific constants
        public double? CS { get; set; }                  // C_S
        public double? CFI { get; set; }                 // C_FI
        public double? Ch { get; set; }                  // C_h
        public double? CP0 { get; set; }                 // C_P,0
        public double? CL { get; set; }                  // C_L
        public double? CV { get; set; }                  // C_V

        // ============================================================
        // 4. EQUILIBRIUM AND SEPARATION PARAMETERS
        // ============================================================

        public double? EquilibriumSlope { get; set; }    // m_yx

        public double? StrippingFactor { get; set; }     // lambda

        // Gas and liquid mole fractions
        public double? GasMoleFractionInlet { get; set; }    // y_in
        public double? GasMoleFractionOutlet { get; set; }   // y_out

        public double? LiquidMoleFractionInlet { get; set; } // x_in
        public double? LiquidMoleFractionOutlet { get; set; }// x_out

        // Equilibrium mole fractions
        public double? GasEquilibriumMoleFraction { get; set; }    // y*
        public double? LiquidEquilibriumMoleFraction { get; set; } // x*

        // ============================================================
        // 5. CALCULATED GEOMETRIC AND HYDRAULIC VARIABLES
        // ============================================================

        public double? HydraulicDiameter { get; set; }   // d_h [m]

        public double? EffectiveInterfacialArea { get; set; } // a_ph [m2/m3]

        public double? RelativeInterfacialArea { get; set; }  // a_ph / a

        public double? LiquidHoldup { get; set; }        // h_L [-]

        public double? LiquidHoldupAtLoading { get; set; } // h_L,S
        public double? LiquidHoldupAtFlooding { get; set; }// h_L,FI

        public double? EffectiveLiquidVelocity { get; set; } // u_bar_L [m/s]

        public double? ReynoldsNumberLiquid { get; set; } // Re_L
        public double? ReynoldsNumberGas { get; set; }    // Re_V

        public double? FroudeNumberLiquid { get; set; }   // Fr_L

        public double? GasCapacityFactor { get; set; }   // F_V

        public double? ResistanceCoefficient { get; set; } // psi_L

        // ============================================================
        // 6. MASS TRANSFER VARIABLES
        // ============================================================

        // Mass transfer coefficients
        public double? LiquidMassTransferCoefficient { get; set; } // beta_L [m/s]
        public double? GasMassTransferCoefficient { get; set; }    // beta_V [m/s]

        // Volumetric mass transfer coefficients
        public double? LiquidVolumetricMassTransferCoefficient { get; set; } // beta_L * a_ph [1/s]
        public double? GasVolumetricMassTransferCoefficient { get; set; }    // beta_V * a_ph [1/s]

        // Individual heights of transfer units
        public double? HTULiquid { get; set; }           // HTU_L [m]
        public double? HTUGas { get; set; }              // HTU_V [m]

        // Overall heights of transfer units
        public double? HTUOverallLiquid { get; set; }    // HTU_OL [m]
        public double? HTUOverallGas { get; set; }       // HTU_OV [m]

        // Number of transfer units
        public double? NTULiquid { get; set; }           // NTU_OL
        public double? NTUGas { get; set; }              // NTU_OV

        // ============================================================
        // 7. HYDRAULIC PERFORMANCE AND COLUMN LIMITS
        // ============================================================

        public double? PressureDrop { get; set; }        // delta_p [Pa]
        public double? PressureDropPerHeight { get; set; } // delta_p/H [Pa/m]

        // Loading point
        public double? LoadingGasVelocity { get; set; }  // u_V,S [m/s]

        // Flooding point
        public double? FloodingGasVelocity { get; set; } // u_V,FI [m/s]

        // Percentage of flooding
        public double? FloodingPercentage { get; set; }  // [%]

        // ============================================================
        // 8. ADDITIONAL MODEL CALCULATIONS
        // ============================================================

        public double? OverallMassTransferCoefficient { get; set; } // K_O

        public double? MassTransferEfficiency { get; set; }

        // Gravitational acceleration
        public double GravitationalAcceleration { get; set; } = 9.80665; // g [m/s2]

        // ============================================================
        // Calculation methods
        // ============================================================

        public bool IsFlooded { get; private set; }

        // Hydraulic area ratio a_h/a. This is NOT the mass-transfer area a_ph,
        // so it is not written to EffectiveInterfacialArea.
        public double? HydraulicAreaRatio { get; private set; }

        // Water reference properties used in the flood-point hold-up.
        // Values taken from the Python notebook; confirm they are the ones you want.
        const double RhoWater = 1000.0;
        const double EtaWater = 8.02e-4;

        double _eps, _ap, _dPart, _fs, _rhoL, _etaL, _rhoV, _etaV, _uL, _uV, _g;
        double _cs, _cfi, _ch, _cp;
        double _psi, _htLpDp, _htFpDp;

        static double Req(double? v, string name) =>
            v ?? throw new InvalidOperationException($"{name} is required but not set.");

        void ResolveInputs()
        {
            _eps = Req(VoidFraction, nameof(VoidFraction));
            _ap = Req(SpecificSurfaceArea, nameof(SpecificSurfaceArea));
            _rhoL = Req(LiquidDensity, nameof(LiquidDensity));
            _etaL = Req(LiquidViscosity, nameof(LiquidViscosity));
            _rhoV = Req(GasDensity, nameof(GasDensity));
            _etaV = Req(GasViscosity, nameof(GasViscosity));
            _cs = Req(CS, nameof(CS));
            _cfi = Req(CFI, nameof(CFI));
            _ch = Req(Ch, nameof(Ch));
            _cp = Req(CP0, nameof(CP0));
            _g = GravitationalAcceleration;

            double d = Req(ColumnDiameter, nameof(ColumnDiameter));
            ColumnCrossSectionArea = Math.PI * d * d / 4.0;      // always calculated from the diameter
            double area = ColumnCrossSectionArea.Value;

            // calculate superficial velocities from volumetric flow rates if they are provided
            if (LiquidVolumetricFlowRate.HasValue)
                LiquidSuperficialVelocity = LiquidVolumetricFlowRate.Value / area;
            if (GasVolumetricFlowRate.HasValue)
                GasSuperficialVelocity = GasVolumetricFlowRate.Value / area;

            _uL = Req(LiquidSuperficialVelocity, nameof(LiquidSuperficialVelocity));
            _uV = Req(GasSuperficialVelocity, nameof(GasSuperficialVelocity));

            if (_uL <= 0 || _uV <= 0)
                throw new InvalidOperationException("Superficial velocities must be > 0.");

            _dPart = 6 * (1 - _eps) / _ap;
            _fs = 1 / (1 + 4 / (_ap * d));
            _psi = (_uL * _rhoL) / (_uV * _rhoV) * Math.Sqrt(_rhoV / _rhoL);

            ReynoldsNumberLiquid = _uL * _rhoL / (_ap * _etaL);
            FroudeNumberLiquid = _uL * _uL * _ap / _g;
            ReynoldsNumberGas = _uV * _dPart * _rhoV / ((1 - _eps) * _etaV) * _fs;
        }

        public void CalculateGeometricParameters()
        {
            double eps = Req(VoidFraction, nameof(VoidFraction));
            double ap = Req(SpecificSurfaceArea, nameof(SpecificSurfaceArea));
            HydraulicDiameter = 4 * eps / ap;       // dh = 4*eps/a
        }

        public void CalculateLoadingAndFlooding()
        {
            ResolveInputs();

            // hold-up at loading, used inside the pressure drop (no Ch)
            _htLpDp = Math.Pow(12 * _etaL * _ap * _ap * _uL / (_rhoL * _g), 1.0 / 3);

            bool low = _psi <= 0.4;
            double nLp = low ? -0.326 : -0.723;
            double nFp = low ? -0.194 : -0.708;

            // loading point
            double xiLp = _g / (_cs * _cs *
                Math.Pow(_psi * Math.Pow(_etaL / _etaV, 0.4), 2 * nLp));
            LoadingGasVelocity = Math.Sqrt(_g / xiLp) * (_eps - _htLpDp)
                * Math.Sqrt(_htLpDp / _ap) * Math.Sqrt(_rhoL / _rhoV);

            // flood point
            _htFpDp = 0.3741 * _eps * Math.Pow(_etaL * RhoWater / (_rhoL * EtaWater), 0.05);
            double xiFp = _g / (_cfi * _cfi *
                Math.Pow(_psi * Math.Pow(_etaL / _etaV, 0.2), 2 * nFp));
            FloodingGasVelocity = Math.Sqrt(2 * _g / xiFp) * Math.Pow(_eps - _htFpDp, 1.5)
                / Math.Sqrt(_eps) * Math.Sqrt(_htFpDp / _ap) * Math.Sqrt(_rhoL / _rhoV);

            FloodingPercentage = 100 * _uV / FloodingGasVelocity;
            IsFlooded = _uV > FloodingGasVelocity;
        }

        public void CalculateLiquidHoldup()
        {
            if (FloodingGasVelocity is null) CalculateLoadingAndFlooding();
            if (IsFlooded) { LiquidHoldup = null; return; }

            double reL = ReynoldsNumberLiquid.Value;
            double frL = FroudeNumberLiquid.Value;

            double ahAp = reL < 5
                ? _ch * Math.Pow(reL, 0.15) * Math.Pow(frL, 0.1)
                : 0.85 * _ch * Math.Pow(reL, 0.25) * Math.Pow(frL, 0.1);
            HydraulicAreaRatio = ahAp;

            double hLs = Math.Pow(12 * _etaL * _ap * _ap * _uL / (_rhoL * _g), 1.0 / 3)
                         * Math.Pow(ahAp, 2.0 / 3);
            double hFl = 2.2 * hLs;
            LiquidHoldupAtLoading = hLs;
            LiquidHoldupAtFlooding = hFl;

            LiquidHoldup = _uV <= LoadingGasVelocity
                ? hLs
                : hLs + (hFl - hLs) * Math.Pow(_uV / FloodingGasVelocity.Value, 13);
        }

        public void CalculatePressureDrop()
        {
            if (FloodingGasVelocity is null) CalculateLoadingAndFlooding();
            if (IsFlooded) { PressureDropPerHeight = null; PressureDrop = null; return; }

            double htDp = _uV <= LoadingGasVelocity
                ? _htLpDp
                : _htLpDp + (_htFpDp - _htLpDp) * Math.Pow(_uV / FloodingGasVelocity.Value, 13);

            double reG = ReynoldsNumberGas.Value;
            double reL = ReynoldsNumberLiquid.Value;
            double shape = 64 / reG + 1.8 / Math.Pow(reG, 0.08);

            double xiD = _cp * shape;
            double dpDry = xiD * _ap / Math.Pow(_eps, 3) * _uV * _uV / 2 * _rhoV / _fs;

            double w = Math.Exp(reL / 200) * Math.Pow(htDp / _htLpDp, 0.3);
            double xiW = _cp * w * shape * Math.Pow((_eps - htDp) / _eps, 1.5);
            double dpWet = dpDry * (xiW / xiD) * Math.Pow(_eps / (_eps - htDp), 3);

            PressureDropPerHeight = dpWet;                       // Pa/m
            if (PackingHeight.HasValue) PressureDrop = dpWet * PackingHeight.Value;  // Pa
        }

        public void CalculateMassTransferCoefficients()
        {
            if (FloodingGasVelocity == null) CalculateLoadingAndFlooding();
            if (LiquidHoldup == null) CalculateLiquidHoldup();
            if (IsFlooded || LiquidHoldup == null) return;

            double dh = 4 * _eps / _ap;
            double hL = LiquidHoldup.Value;
            double cl = Req(CL, nameof(CL)), cv = Req(CV, nameof(CV));
            double dL = Req(LiquidDiffusivity, nameof(LiquidDiffusivity));
            double dV = Req(GasDiffusivity, nameof(GasDiffusivity));
            double sigma = Req(LiquidSurfaceTension, nameof(LiquidSurfaceTension));
            double nuV = _etaV / _rhoV;

            double betaL = cl * Math.Pow(12, -1.0 / 6) * Math.Sqrt(_uL / hL) * Math.Sqrt(dL / dh);
            double betaV = cv / Math.Sqrt(_eps - hL) * Math.Sqrt(_ap / dh) * dV
                           * Math.Pow(_uV / (_ap * nuV), 0.75) * Math.Pow(nuV / dV, 1.0 / 3);

            double aphOverA = 1.5 * Math.Pow(_ap * dh, -0.5)
                            * Math.Pow(_uL * dh * _rhoL / _etaL, -0.2)
                            * Math.Pow(_uL * _uL * _rhoL * dh / sigma, 0.75)
                            * Math.Pow(_uL * _uL / (_g * dh), -0.45);

            RelativeInterfacialArea = aphOverA;
            EffectiveInterfacialArea = aphOverA * _ap;       // a_ph, m2/m3
            LiquidMassTransferCoefficient = betaL;
            LiquidVolumetricMassTransferCoefficient = LiquidMassTransferCoefficient * EffectiveInterfacialArea;
            GasMassTransferCoefficient = betaV;
            GasVolumetricMassTransferCoefficient = GasMassTransferCoefficient * EffectiveInterfacialArea;
        }

        public void CalculateHTU(double L, double V)
        {
            double aph = Req(EffectiveInterfacialArea, nameof(EffectiveInterfacialArea));
            double m = Req(EquilibriumSlope, nameof(EquilibriumSlope));

            HTUGas = _uV / (GasMassTransferCoefficient.Value * aph);
            HTULiquid = _uL / (LiquidMassTransferCoefficient.Value * aph);
            StrippingFactor = m * V / L;                          // lambda
            HTUOverallGas = HTUGas + StrippingFactor.Value * HTULiquid;
            NTUGas = Req(PackingHeight, nameof(PackingHeight)) / HTUOverallGas.Value;
        }

        public void CalculateSeparationPerformance(double L, double V)
        {
            double lam = StrippingFactor.Value, ntu = NTUGas.Value, m = EquilibriumSlope.Value;
            double yIn = GasMoleFractionInlet.Value, xIn = LiquidMoleFractionInlet.Value;

            double yOut;
            if (Math.Abs(1 - lam) < 1e-9)
                yOut = m * xIn + (yIn - m * xIn) / (1 + ntu);
            else
                yOut = m * xIn + (yIn - m * xIn) * (1 - lam) / (Math.Exp((1 - lam) * ntu) - lam);

            double xOut = xIn + V * (yIn - yOut) / L;             // solute balance (dilute)

            GasMoleFractionOutlet = yOut;
            LiquidMoleFractionOutlet = xOut;
        }

        // convenience entry point
        public void CalculateHydraulics()
        {
            CalculateGeometricParameters();
            CalculateLoadingAndFlooding();
            CalculateLiquidHoldup();
            CalculatePressureDrop();
        }
    }

    public class ColumnResults
    {
        public bool Calculated;
        public bool IsFlooded;
        public double PressureDropPerHeight;   // Pa/m
        public double PressureDrop;            // Pa
        public double LiquidHoldup;            // -
        public double LoadingVelocity;         // m/s
        public double FloodingVelocity;        // m/s
        public double PercentFlood;            // %
        public double GasVelocity;             // m/s
        public double LiquidVelocity;          // m/s
        public double GasMassFlowOut;          // kg/s
        public double LiquidMassFlowOut;       // kg/s
        public double GasMoleFractionOutlet;   // mol/mol
        public double LiquidMoleFractionOutlet;// mol/mol
        public double NTUGas, NTULiquid; // -
        public double GasVolumetricMTC, LiquidVolumetricMTC; // 1/s
        public double GasSuperficialVelocity, LiquidSuperficialVelocity; // m/s
    }

    public enum Phase { Gas, Liquid }

    public enum Prop
    {
        Density,          // kg/m3
        Viscosity,        // Pa.s
        SurfaceTension,   // N/m
        Diffusivity,      // m2/s
        MolarMass,        // kg/mol
        MassFlow,         // kg/s
        MolarFlow         // mol/s
    }
}