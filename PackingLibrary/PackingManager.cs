#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackingLibrary
{
    // ---- library.json model -------------------------------------------------------

    public sealed class LibraryInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
        [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
    }

    public sealed class PackingFamily
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    /// <summary>One line of the library's "packings" index: where to find a packing's JSON file.</summary>
    public sealed class PackingIndexEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("family")] public string? Family { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }

        /// <summary>Folder (relative to the library root), e.g. "Raschig Super Ring/RSR_1_M".</summary>
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    }

    internal sealed class LibraryFile
    {
        [JsonPropertyName("library")] public LibraryInfo? Library { get; set; }
        [JsonPropertyName("units")] public Dictionary<string, string>? Units { get; set; }
        [JsonPropertyName("families")] public List<PackingFamily>? Families { get; set; }
        [JsonPropertyName("packings")] public List<PackingIndexEntry>? Packings { get; set; }
    }

    // ---- Manager ---------------------------------------------------------------------

    /// <summary>
    /// Loads library.json and every packing file it references, and offers lookups.
    ///
    /// Expected layout (the packings folder may also be the folder that contains library.json):
    ///   library.json
    ///   packings/
    ///     Raschig Super Ring/
    ///       RSR_1_M/
    ///         RSR_1_M.json
    ///
    /// Problems with individual packings (missing file, bad JSON, id mismatch) do not stop the load;
    /// they are recorded in <see cref="Warnings"/> and that packing is skipped.
    /// A missing or unreadable library.json throws.
    /// </summary>
    public sealed class PackingManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private readonly Dictionary<string, Packing> _byId = new Dictionary<string, Packing>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Packing> _packings = new List<Packing>();
        private readonly List<PackingFamily> _families;
        private readonly HashSet<string> _familyIds;
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _searchRoots;

        private PackingManager(string libraryFilePath, string? packingsRoot, LibraryFile file)
        {
            LibraryFilePath = libraryFilePath;
            LibraryDirectory = Path.GetDirectoryName(libraryFilePath) ?? string.Empty;
            Info = file.Library ?? new LibraryInfo();
            Units = file.Units ?? new Dictionary<string, string>();
            _families = file.Families ?? new List<PackingFamily>();
            _familyIds = new HashSet<string>(_families.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

            _searchRoots = packingsRoot != null
                ? new List<string> { packingsRoot }
                : new List<string> { LibraryDirectory, Path.Combine(LibraryDirectory, "packings") };
        }

        /// <summary>Full path of the library.json that was loaded.</summary>
        public string LibraryFilePath { get; }

        /// <summary>Directory containing library.json. Model file paths are resolved against it.</summary>
        public string LibraryDirectory { get; }

        public LibraryInfo Info { get; }

        /// <summary>The "units" block of library.json (unit name per quantity).</summary>
        public IReadOnlyDictionary<string, string> Units { get; }

        public IReadOnlyList<PackingFamily> Families => _families;

        /// <summary>All successfully loaded packings, in library order.</summary>
        public IReadOnlyList<Packing> Packings => _packings;

        public int Count => _packings.Count;

        /// <summary>Non-fatal problems found while loading.</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        // ---- Loading -----------------------------------------------------------------

        /// <summary>
        /// Loads the library.
        /// </summary>
        /// <param name="libraryJsonPath">Path to library.json.</param>
        /// <param name="packingsRoot">
        /// Folder that the packing "path" entries are relative to. When null, the folder containing
        /// library.json is tried first, then its "packings" subfolder.
        /// </param>
        public static PackingManager Load(string libraryJsonPath, string? packingsRoot = null)
        {
            if (string.IsNullOrWhiteSpace(libraryJsonPath))
                throw new ArgumentException("A path to library.json is required.", nameof(libraryJsonPath));

            var libraryFile = Path.GetFullPath(libraryJsonPath);
            if (!File.Exists(libraryFile))
                throw new FileNotFoundException("Packing library file not found.", libraryFile);

            LibraryFile? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(libraryFile), JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"'{libraryFile}' is not valid JSON: {ex.Message}", ex);
            }

            if (parsed is null)
                throw new InvalidDataException($"'{libraryFile}' is empty.");

            var manager = new PackingManager(
                libraryFile,
                packingsRoot is null ? null : Path.GetFullPath(packingsRoot),
                parsed);

            manager.LoadPackings(parsed.Packings ?? new List<PackingIndexEntry>());
            return manager;
        }

        private void LoadPackings(IEnumerable<PackingIndexEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    _warnings.Add("Library entry without an id was skipped.");
                    continue;
                }

                if (_byId.ContainsKey(entry.Id))
                {
                    _warnings.Add($"{entry.Id}: duplicate id in library, later entry skipped.");
                    continue;
                }

                var file = ResolvePackingFile(entry);
                if (file is null)
                {
                    _warnings.Add($"{entry.Id}: no JSON file found for path '{entry.Path}'.");
                    continue;
                }

                Packing? packing;
                try
                {
                    packing = JsonSerializer.Deserialize<Packing>(File.ReadAllText(file), JsonOptions);
                }
                catch (JsonException ex)
                {
                    _warnings.Add($"{entry.Id}: invalid JSON in '{file}': {ex.Message}");
                    continue;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _warnings.Add($"{entry.Id}: could not read '{file}': {ex.Message}");
                    continue;
                }

                if (packing is null)
                {
                    _warnings.Add($"{entry.Id}: '{file}' is empty.");
                    continue;
                }

                if (!string.Equals(packing.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _warnings.Add($"{entry.Id}: file '{file}' contains id '{packing.Id}', skipped.");
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.Family) &&
                    !string.Equals(packing.Identity.Family, entry.Family, StringComparison.OrdinalIgnoreCase))
                {
                    _warnings.Add($"{entry.Id}: family '{packing.Identity.Family}' in file differs from '{entry.Family}' in library.");
                }

                if (!_familyIds.Contains(packing.Identity.Family))
                    _warnings.Add($"{entry.Id}: family '{packing.Identity.Family}' is not listed in the library's families.");

                ValidatePacking(packing);

                packing.SourceFile = file;
                _byId[packing.Id] = packing;
                _packings.Add(packing);
            }
        }

        /// <summary>
        /// Finds the JSON file for an index entry. Tries, for each search root:
        ///   root/path/id.json, root/path.json, root/path (if path itself names a .json file).
        /// Anything that would resolve outside the root is ignored.
        /// </summary>
        private string? ResolvePackingFile(PackingIndexEntry entry)
        {
            var relative = (entry.Path ?? string.Empty).Trim()
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (relative.Length == 0)
                relative = entry.Id;

            foreach (var root in _searchRoots)
            {
                var candidates = new[]
                {
                    Path.Combine(root, relative, entry.Id + ".json"),
                    Path.Combine(root, relative + ".json"),
                    Path.Combine(root, relative),
                };

                foreach (var candidate in candidates)
                {
                    var full = Path.GetFullPath(candidate);
                    if (IsInside(root, full) && File.Exists(full))
                        return full;
                }
            }

            return null;
        }

        private static bool IsInside(string root, string fullPath)
        {
            var relative = GetRelativePath(root, fullPath);
            return !Path.IsPathRooted(relative)
                   && relative != ".."
                   && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        private void ValidatePacking(Packing p)
        {
            var props = p.PhysicalProperties;
            CheckUnit(p, "specificSurfaceArea", props.SpecificSurfaceArea, "m2/m3");
            CheckUnit(p, "packingDensity", props.PackingDensity, "1/m3");
            CheckUnit(p, "voidFraction", props.VoidFraction, "-");

            if (props.SpecificSurfaceArea.Value <= 0)
                _warnings.Add($"{p.Id}: specific surface area must be positive.");

            if (props.VoidFraction.Value <= 0 || props.VoidFraction.Value >= 1)
                _warnings.Add($"{p.Id}: void fraction {props.VoidFraction.Value} is outside (0, 1).");

            if (p.BilletSchultes.Available && !p.BilletSchultes.Constants.IsComplete)
            {
                var missing = string.Join(", ", p.BilletSchultes.Constants.GetMissingConstants());
                _warnings.Add($"{p.Id}: Billet-Schultes data is incomplete (missing: {missing}).");
            }
        }

        private void CheckUnit(Packing p, string property, QuantityValue quantity, string expected)
        {
            if (!string.Equals(quantity.Unit, expected, StringComparison.Ordinal))
                _warnings.Add($"{p.Id}: {property} unit is '{quantity.Unit}', expected '{expected}'.");
        }

        // ---- Lookups -----------------------------------------------------------------

        public bool TryGetPacking(string id, out Packing? packing)
        {
            if (id != null && _byId.TryGetValue(id, out var found))
            {
                packing = found;
                return true;
            }

            packing = null;
            return false;
        }

        /// <exception cref="KeyNotFoundException">No packing with this id was loaded.</exception>
        public Packing? GetPacking(string id) =>
            TryGetPacking(id, out var packing)
                ? packing
                : throw new KeyNotFoundException($"Packing '{id}' was not found in the library.");

        public bool Contains(string id) => id != null && _byId.ContainsKey(id);

        public PackingFamily? GetFamily(string familyId) =>
            _families.FirstOrDefault(f => string.Equals(f.Id, familyId, StringComparison.OrdinalIgnoreCase));

        // get all families
        public IReadOnlyList<string> GetFamilies() =>
            _packings.Select(p => p.Identity.Family)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                     .ToList();

        /// <summary>Packings of one family, in library order.</summary>
        public IReadOnlyList<Packing> GetPackingsByFamily(string familyId) =>
            _packings.Where(p => string.Equals(p.Identity.Family, familyId, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>Packings of one material (e.g. "Metal", "Plastic", "Ceramic"), in library order.</summary>
        public IReadOnlyList<Packing> GetPackingsByMaterial(string material) =>
            _packings.Where(p => string.Equals(p.Identity.Material, material, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>
        /// Full path of the packing's predictive model file, or null if no model is available.
        /// The path in the packing file is resolved against the directory containing library.json.
        /// </summary>
        public string? GetModelPath(Packing packing)
        {
            if (!packing.PredictiveModel.Available || string.IsNullOrWhiteSpace(packing.PredictiveModel.File))
                return null;

            return Path.GetFullPath(Path.Combine(LibraryDirectory, packing.PredictiveModel.File));
        }

        // Helper to find relative path
        private static string GetRelativePath(string relativeTo, string path)
        {
            var fromUri = new Uri(
                Path.GetFullPath(relativeTo).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar);

            var toUri = new Uri(Path.GetFullPath(path));

            if (fromUri.Scheme != toUri.Scheme)
                return path;

            var relativeUri = fromUri.MakeRelativeUri(toUri);

            return Uri.UnescapeDataString(
                relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
        }
    }
}