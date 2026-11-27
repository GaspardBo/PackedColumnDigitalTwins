#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PackingLibrary
{
    /// <summary>
    /// One packing, as stored in its own JSON file (e.g. RSR_1_M/RSR_1_M.json).
    /// Property names are mapped explicitly so the C# naming style can differ from the JSON.
    /// </summary>
    public sealed class Packing
    {
        private static readonly PackingIdentity packingIdentity = new PackingIdentity();

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("identity")]
        public PackingIdentity Identity { get; set; } = packingIdentity;

        [JsonPropertyName("physicalProperties")]
        public PhysicalProperties PhysicalProperties { get; set; } = new PhysicalProperties();

        [JsonPropertyName("billetSchultes")]
        public BilletSchultesData BilletSchultes { get; set; } = new BilletSchultesData();

        [JsonPropertyName("predictiveModel")]
        public PredictiveModelInfo PredictiveModel { get; set; } = new PredictiveModelInfo();

        /// <summary>Full path of the JSON file this packing was loaded from (set by <see cref="PackingManager"/>).</summary>
        [JsonIgnore]
        public string? SourceFile { get; internal set; }

        // ---- Convenience accessors -------------------------------------------------

        [JsonIgnore] public string Name => Identity.Name;
        [JsonIgnore] public string FamilyId => Identity.Family;
        [JsonIgnore] public string Material => Identity.Material;

        /// <summary>Specific surface area a, m2/m3.</summary>
        [JsonIgnore] public double SpecificSurfaceArea => PhysicalProperties.SpecificSurfaceArea.Value;

        /// <summary>Void fraction epsilon, m3/m3.</summary>
        [JsonIgnore] public double VoidFraction => PhysicalProperties.VoidFraction.Value;

        /// <summary>Packing density N, 1/m3.</summary>
        [JsonIgnore] public double PackingDensity => PhysicalProperties.PackingDensity.Value;

        /// <summary>True when Billet-Schultes data is flagged available and all six constants are present.</summary>
        [JsonIgnore]
        public bool HasCompleteBilletSchultesData =>
            BilletSchultes.Available && BilletSchultes.Constants.IsComplete;

        public override string ToString() => $"{Id} ({Name})";
    }

    public sealed class PackingIdentity
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Null when the manufacturer is not known.</summary>
        [JsonPropertyName("manufacturer")]
        public string? Manufacturer { get; set; }

        /// <summary>Family id, matches an entry in the library's "families" list.</summary>
        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("material")]
        public string Material { get; set; } = string.Empty;

        /// <summary>Nominal size in mm.</summary>
        [JsonPropertyName("nominalSize")]
        public double NominalSize { get; set; }
    }

    public sealed class PhysicalProperties
    {
        [JsonPropertyName("packingDensity")]
        public QuantityValue PackingDensity { get; set; } = new QuantityValue();

        [JsonPropertyName("specificSurfaceArea")]
        public QuantityValue SpecificSurfaceArea { get; set; } = new QuantityValue();

        [JsonPropertyName("voidFraction")]
        public QuantityValue VoidFraction { get; set; } = new QuantityValue();
    }

    /// <summary>A number with its unit, e.g. { "value": 165, "unit": "m2/m3" }.</summary>
    public sealed class QuantityValue
    {
        [JsonPropertyName("value")]
        public double Value { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = string.Empty;

        public override string ToString() => $"{Value} {Unit}";
    }

    public sealed class BilletSchultesData
    {
        [JsonPropertyName("available")]
        public bool Available { get; set; }

        [JsonPropertyName("constants")]
        public BilletSchultesConstants Constants { get; set; } = new BilletSchultesConstants();

        [JsonPropertyName("source")]
        public string? Source { get; set; }
    }

    /// <summary>
    /// Packing-specific constants of the Billet and Schultes (1999) model.
    /// A constant is null when the source table has no value for it.
    /// </summary>
    public sealed class BilletSchultesConstants
    {
        /// <summary>Loading point constant C_S.</summary>
        [JsonPropertyName("CS")] public double? CS { get; set; }

        /// <summary>Flooding point constant C_Fl.</summary>
        [JsonPropertyName("CFI")] public double? CFI { get; set; }

        /// <summary>Hydraulic area / holdup constant C_h.</summary>
        [JsonPropertyName("Ch")] public double? Ch { get; set; }

        /// <summary>Pressure drop constant C_P,0.</summary>
        [JsonPropertyName("CP0")] public double? CP0 { get; set; }

        /// <summary>Liquid-side mass transfer constant C_L.</summary>
        [JsonPropertyName("CL")] public double? CL { get; set; }

        /// <summary>Gas-side mass transfer constant C_V.</summary>
        [JsonPropertyName("CV")] public double? CV { get; set; }

        [JsonIgnore]
        public bool IsComplete =>
            CS.HasValue && CFI.HasValue && Ch.HasValue && CP0.HasValue && CL.HasValue && CV.HasValue;

        /// <summary>Names of the constants that have no value.</summary>
        public IEnumerable<string> GetMissingConstants()
        {
            if (CS is null) yield return nameof(CS);
            if (CFI is null) yield return nameof(CFI);
            if (Ch is null) yield return nameof(Ch);
            if (CP0 is null) yield return nameof(CP0);
            if (CL is null) yield return nameof(CL);
            if (CV is null) yield return nameof(CV);
        }
    }

    public sealed class PredictiveModelInfo
    {
        [JsonPropertyName("available")]
        public bool Available { get; set; }

        /// <summary>Model file path, relative to the library directory (e.g. "models/hetp_model.onnx").</summary>
        [JsonPropertyName("file")]
        public string? File { get; set; }
    }
}