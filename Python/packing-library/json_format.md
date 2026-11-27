# Packing file format, version 1.0 (27 Nov 2026)

For Gaspard, to read packing files in the C# DLL and check the results against Python. All units SI unless the field name says otherwise.

## Files
| File | What |
|---|---|
| `examples/raschig-superring-0.7_demo.json` | a packing file with Level 2 blocks for k_L·a (network) and k_G·a (linear): Wang 2015 data for Super-Ring #0.7 treated as a user's plant |
| `examples/raschig-superring-0.7_tests.json` | 32 operating points with every property given and the Python results; the DLL must match to 1e-6 relative |
| `examples/format_check.json` | a linear and a network block whose outputs clearly vary, with 6 cases (two outside the training range), to test the evaluator alone; match to 1e-9 relative |
| `make_packing_files.py` | draft generator: one library file per real packing, merged from the rows of `billet_packings.csv` |
| `train.py` | writes user files from a user's data |

Library files (from the database) have the same layout with every `level2` entry `null`. The database has one row per source, so one packing can have several rows; the generator merges them into one file. The hydraulic set (C_S, C_Fl, C_h, C_P0) comes whole from one row and C_L, C_V from one row, published sources first (BS1999, B1995, Rejl2015), then our fits; a row whose note says "data check pending" is used only when there is no other. A mass-transfer pair is only combined with a hydraulic set from a row with the same a and ε. Samples with different a or ε get separate files, and the second has `-2` on its id. In a user file, an output also has `null` when the tuner found that no correction does best.

## Packing file fields
| Field | Type | Meaning |
|---|---|---|
| `format_version` | string | "1.0" |
| `file_kind` | string | `library` or `user` |
| `packing` | object | `id`, `name`, `size`, `maker`, `material`, `arrangement` (`regular` or `dumped`), `a_m2_m3`, `eps` |
| `constants` | object | `C_S`, `C_Fl`, `C_h`, `C_P0`, `C_L`, `C_V`, each `{value, source, status}`; `status` says fitted, published or borrowed |
| `model_reference` | object | reference water for the flood-point hold-up: `rho_W_kg_m3`, `mu_W_Pa_s` |
| `dropdown_ready` | bool | true when the full hydraulic set is present |
| `level2` | object | `dp`, `holdup`, `kLa`, `kGa`: a Level 2 block or `null` |
| `source_detail` | object | `hydraulics` and `mass_transfer`: the database row each set came from (`source`, `size_as_listed`, `table`, `page`, `note`, `borrowed`, `fitted_on`, `version`), or `null`; `other_rows`: rows for the same packing that were not used |

## Level 2 block
| Field | Meaning |
|---|---|
| `output` | which output it corrects |
| `form` | `linear` or `network` |
| `inputs` | input names in order: `Re_L` = u_L ρ_L / (a μ_L) with u_L in m/s; `x_flood` = u_G / u_G,flood from Level 1 |
| `scaling` | `mean`, `sd` per input; z = (x − mean) / sd |
| `valid_range` | `Re_L: [min, max]` and `x_flood: [min, max]` of the training data. Both are always given, even when the form uses only one input, so a correction is never applied at conditions it was not trained on |
| `level1_constants` | the Level 1 constant(s) the correction was trained on top of; if the file's constant differs, do not apply the block |
| `linear` | `intercept`, `coef` (one per input): y = intercept + Σ coef·z |
| `network` | `activation` relu, `output_activation` identity, `members`: one per seed, each with `weights` (list of matrices, rows = output nodes of that layer) and `biases`; y = mean over members |
| `trained_on_runs` | number of runs it was trained on |

Evaluation at one operating point:
1. Compute the inputs from the Level 1 results.
2. If `Re_L` or `x_flood` is outside `valid_range` (inclusive), do not apply: return Level 1 for that output with a flag.
3. Scale to z.
4. Linear: y as above. Network, for each member: h = z; for each layer k, h = W_k h + b_k, then ReLU (max(h, 0)) on every layer except the last; y_member = h[0]. y = mean of y_member.
5. Corrected output = Level 1 output × exp(y).

## Level 1 mass transfer (what the DLL computes before Level 2)
Billet (1995), below the loading point; u_L in m/s, a and ε from the packing file.
- d_h = 4ε/a (eq 5-15)
- film hold-up h_L = (12 μ_L a² u_L / (g ρ_L))^(1/3) (eq 4-27), g = 9.81
- a_Ph = a · 3 ε^0.5 Re_L^−0.2 We_L^0.75 Fr_L^−0.45 with Re_L = u_L ρ_L/(a μ_L), We_L = u_L² ρ_L/(σ_L a), Fr_L = u_L² a / g (eq 5-16; Froude exponent −0.45)
- β_L = C_L · 12^(1/6) (u_L/h_L)^0.5 (D_L/d_h)^0.5 (eq 5-4 ÷ a_Ph)
- β_V = C_V · a^0.5 / (d_h^0.5 (ε − h_L)^0.5) · D_V · Re_V^0.75 · Sc_V^(1/3), Re_V = u_G ρ_G/(a μ_G), Sc_V = μ_G/(ρ_G D_V) (eq 5-13 ÷ a_Ph)
- outputs k_L·a = β_L·a_Ph and k_G·a = β_V·a_Ph (1/s). a_Ph is not an output.

Hydraulics (loading, flood, ΔP, hold-up): as `billet()`, `loading_uG()` and `flood_uG()` in `packlib.py`; the test file gives `uG_loading_ms`, `uG_flood_ms`, `dp_Pa_m` and `holdup` for every point. `dp_Pa_m` and `holdup` are `null` past the flood point. The mass-transfer outputs (and so HTU and HETP) are `null` above the loading point.

## Test file fields
Each point has `inputs` (u_L in m³/(m² h), u_G in m/s, T, column diameter, and every property: `rho_L`, `mu_L`, `sigma_L`, `rho_G`, `mu_G`, `D_L`, `D_V`) and `expected` (the Python results above, the Level 2 inputs, `below_loading`, and for each corrected output the corrected value, whether it was applied, and `*_log_correction` = y). Properties are given so the DLL can be checked before its own property methods exist.

## Still open
1. **Hydraulic Level 2 blocks** (`dp`, `holdup`) use the same layout; an example comes with `train.py`.
2. **Narrow training ranges.** Wang's k_G runs cover one or two liquid loads, so a k_G·a block applies only there; in the Super-Ring #0.7 test file it applies at 12 of 32 points.
Settled 27 Nov: no mass-transfer answer above loading; "no correction" is a tuner option for mass transfer, giving a `null` block; the range rule holds for single-load data.
