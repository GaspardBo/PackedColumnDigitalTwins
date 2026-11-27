# train.py input format (proposal, 27 Nov 2026)

A proposal to agree with Gaspard: ideally the DLL can export a user's logged plant data in exactly this layout. Example: `examples/user_data_example.csv` (Wang 2015 data for Super-Ring #0.7).

One CSV, one row per measured operating point. Leave a cell empty where nothing was measured. Units are SI except the liquid load.

## Every row
| Column | Unit | Meaning |
|---|---|---|
| `uL_m3_m2h` | m³/(m² h) | liquid load; 0 for a dry-bed run |
| `uG_ms` | m/s | superficial gas velocity |
| `rho_L`, `mu_L`, `sigma_L` | kg/m³, Pa·s, N/m | liquid density, viscosity, surface tension at that point |
| `rho_G`, `mu_G` | kg/m³, Pa·s | gas density and viscosity at that point |
| `T_L_C`, `T_G_C` | °C | temperatures (for the record; the model uses the properties above) |

## Measurements (any that were made)
| Column | Unit | Fits |
|---|---|---|
| `dp_Pa_m` | Pa/m | dry rows: C_P0. Wet rows with `holdup`: C_h, C_Fl, Level 2 for ΔP |
| `holdup` | m³/m³ | wet rows: C_h, C_Fl, Level 2 for hold-up |
| `kLa_1_s` with `D_L` | 1/s, m²/s | C_L and Level 2 for k_L·a; `D_L` is the diffusivity of the transferring solute in the liquid |
| `kGa_1_s` with `D_V` | 1/s, m²/s | C_V and Level 2 for k_G·a; `D_V` is its diffusivity in the gas |
| `curve` | any label | optional: which hydraulic curve a row belongs to (default: liquid load rounded to 1 m³/(m² h)) |

## What train.py does with it
- C_S is never fitted; it comes from the starting packing file, which also gives a and ε.
- Hydraulic Level 1 needs wet rows with both ΔP and hold-up; Level 2 for ΔP and hold-up needs at least 3 curves.
- Mass transfer uses only rows at or below the Level 1 loading point; Level 2 needs at least 4 such rows per side.
- Errors printed at the end are on the training rows. A proper held-out test is in the research notebooks, not in train.py.

## Questions for Gaspard
1. Can the DLL, or Aspen and COFE through it, export logged points in this layout, with the stream properties filled in?
2. Is k_L·a and k_G·a what a plant user will have, or HETP? HETP needs the equilibrium as well and would be a different input (not supported yet).
