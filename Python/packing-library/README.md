# Packing library for the packed-column DLL

Billet and Schultes constants for 142 packing entries, the Billet model in Python, and the tool that fits a user's own data. Everything the DLL needs is in this folder; the research notebooks, raw data and papers are kept separately and are not needed here.

## You only need these

| File | What it is | Who needs it |
|---|---|---|
| `billet_packings.csv` | **the constants**: one row per packing and source (columns below) | the DLL's library and interpolation |
| `json_format.md` | the packing file format, and exactly what the DLL computes for one operating point | DLL |
| `examples/` | a demo packing file, 32 test points the DLL must reproduce, a check of the Level 2 maths, and an example of user data | DLL |
| `make_packing_files.py` | builds the DLL's packing library (`../../Packings`: `library.json`, one folder and file per packing; several CSV rows for the same packing are merged, see `json_format.md`) and trains the predictive models listed in `training_data/training_data.csv` | DLL |
| `train.py` | user data → fitted constants and Level 2 → a packing file, plus one `.onnx` model per Level 2 output next to it | users with their own data |
| `onnx_export.py` | writes a Level 2 block as an ONNX model (interface at the top of the file); used by `train.py` | the scripts |
| `training_data/` | `training_data.csv`: which packings have measured data for `make_packing_files.py` to train on | DLL |
| `user_data_format.md` | the columns `train.py` reads (a proposal, to agree) | DLL export, users |
| `packlib.py` | the model in Python (Billet hydraulics and mass transfer, Level 2); used by the two scripts | the scripts |
| `requirements.txt` | Python packages for the scripts | users |

## The three levels
- **Level 0:** a packing from the list with published constants (Billet and Schultes 1999, Billet 1995, Rejl et al. 2015). A packing not in the list is interpolated from its family in the CSV.
- **Level 1:** constants fitted to data: our fits ("ThisWork" rows), or a user's own through `train.py`.
- **Level 2:** a correction trained on a user's own plant data, on top of Level 1. Library files carry none; `train.py` writes it.

## Running the scripts
```
pip install -r requirements.txt
python make_packing_files.py --train-files packings
python train.py packings/RSR_0_7_M.json examples/user_data_example.csv my_packing.json --dc 0.427
```
The first line rewrites the repository's `Packings` library: every dumped and regular (arranged or structured) packing, with `identity.arrangement` saying which; a value the database lacks (packing density, the nominal size of a structured packing) is written as null. For every packing in `training_data/training_data.csv` it runs `train.py`: the Billet-Schultes constants the data supports are refit and written to the packing file (noted in `billetSchultes.source`), and Level 2 is trained on top of them and stored as `<ID>_<output>.onnx` next to the packing file, listed in its `predictiveModel` section. (`train.py --keep-constants` trains Level 2 without refitting.) `--train-files packings` also writes the `json_format.md` files that `train.py` starts from; `--no-train` skips the training.

The `train.py` line takes about a minute; it prints the fitted constants and the Level 1 and Level 2 errors on the user's rows, and writes `my_packing_<output>.onnx` next to `my_packing.json`.

Each `.onnx` model takes `X` = [Re_L, x_flood] (double, one row per point) and returns `log_correction`, `correction_factor` (corrected output = Level 1 × factor) and `in_valid_range` (use Level 1 where false). The model metadata holds the block's `valid_range` and `level1_constants`.

## The constants file
| Column | Meaning | Unit |
|---|---|---|
| packing, material, size | as printed in the source (size is text: mm, Super-Ring number, inches or a product code) | |
| arrangement | `dumped` (random) or `regular` (arranged, structured) | |
| N_per_m3 | elements per m³ (blank for structured packings) | 1/m³ |
| a_m2_m3, eps | specific surface area, void fraction | m²/m³, m³/m³ |
| C_S, C_Fl | loading and flood point constants | |
| C_h | hold-up constant | |
| C_P0 | dry pressure-drop constant | |
| C_L, C_V | liquid- and gas-side mass-transfer constants | |
| source | `BS1999`, `B1995`, `Rejl2015` or `ThisWork` | |
| table, page | where in the source; `4.3+5.1` means book tables combined | |
| note, borrowed, fitted_on, version | cross-checks, which constants are borrowed and from where, what our fits used, model version | |

A blank cell means the source gives no value; nothing is filled in. A row is ready for the dropdown when C_S, C_Fl, C_h and C_P0 are all present and the note does not say "not dropdown-ready" (86 rows, which merge into 81 dropdown entries; the two unnamed prototype packings are kept for reference only). Rows with only C_L and C_V are mass-transfer fits for packings whose hydraulics are not fitted yet.

**C_L and C_V** were fitted to measured k_L·a and k_G·a with Billet's own interfacial area inside the model, so they are valid only with Billet's area equation. The area itself is not an output.

## Sources
- Billet, R. and Schultes, M. (1999) 'Prediction of mass transfer columns with dumped and arranged packings: updated summary of the calculation method of Billet and Schultes', *Trans IChemE*, 77(A), pp. 498–504. Tables 2a and 2b.
- Billet, R. (1995) *Packed Towers in Processing and Environmental Technology*. Weinheim: VCH. Tables 4.1–4.4, 4.6–4.7, 5.1–5.2.
- Rejl, F. et al. (2015) 'Hydraulic and mass-transfer characteristics of Raschig Super-Pak 250Y', *Chem. Eng. Res. Des.* 99, pp. 20–27, Table 3.
- ThisWork fits use data from Tsai (2010), Wang (2015) and Song (2017), PhD dissertations, University of Texas at Austin; Baumfrisch (2022), Dipl.-Ing. thesis, Montanuniversität Leoben; Erasmus (2004), PhD dissertation, University of Stellenbosch.

## Known issues in the published tables (see the `note` column)
- Hiflow ring, N 6815: printed as Metal in 1999, Plastic in three book tables.
- Raschig ring ceramic 50: C_Fl 1.547 (1999) against 1.574 (book).
- Montz B2-300 (1999) is called B1-300 in the book.
- NOR PAC 25 superscripts 6 and 10 in 1999 are "type B" and "10 webs" in the book.

