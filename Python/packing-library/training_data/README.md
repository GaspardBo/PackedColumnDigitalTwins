# Training data

Measured data for the packings in the library, in the layout of `../user_data_format.md`: one CSV per library packing, named by its library id. `training_data.csv` lists the files `make_packing_files.py` trains on, with the column diameter of each.

All rows are air/water. Other liquids in the sources (viscous and low surface tension runs, aqueous glycerol, Kerosol) are not included. Each file has a `source` column saying which table every row came from. Hydraulic curves are labelled by source and liquid load in `curve`, so curves from different campaigns are kept apart.

## Sources
| Source | Column | Packings | What is used |
|---|---|---|---|
| Tsai (2010), PhD dissertation, University of Texas at Austin, Table B.9 | SRP, 0.427 m | Mellapak 125Y, 250Y, 500Y, 2Y, 250Y smooth; MellapakPlus 252Y; Prototype 500; Flexipac 1Y (dry only) | ΔP and hold-up, baseline (water) runs |
| Wang (2015), PhD dissertation, University of Texas at Austin, Tables C.1, D.2, D.3 | SRP, 0.427 m | Mellapak 125Y, 250Y, 250X, 2X; GT-PAK 350Y, 350Z, 500Y; Super-Pak 200X, 250Y; Super-Ring #0.3, #0.7; Prototypes A, B | ΔP and hold-up; k_L·a (toluene stripping) and k_G·a (SO₂ into NaOH) |
| Song (2017), PhD dissertation, University of Texas at Austin, Tables C.2, C.4, C.5 | SRP, 0.427 m | Mellapak Grid 64Y; Montz B1-250MN; HFP 2; Super-Ring #1.5; GT-OPTIMPak 250Y; k_L·a also for Mellapak 125Y, 250Y, 250X, GT-PAK 350Y, 500Y, Super-Pak 250Y | ΔP and hold-up; k_L·a (toluene, water runs); K_OG·a (SO₂) as k_G·a |
| Baumfrisch (2022), Dipl.-Ing. thesis, Montanuniversität Leoben, Tables 12, 13 and mass-transfer tables | 0.422 m (V-422) | RVT RMP N 250Y, 250X, RMP SP 250; Super-Pak 250Y | ΔP and hold-up; k_L·a (CO₂ desorption) and k_G·a (SO₂) |
| Erasmus (2004), PhD dissertation, University of Stellenbosch | 0.2 m | Flexipac 350Y, Flexipac 350Y HC | ΔP and hold-up |

Where one packing was measured in several campaigns on the same column, they are in one file. Tsai's Mellapak 250X hydraulics are left out, because its hold-up data is still being checked; Wang's are used.

## Properties
- Water: density and viscosity from the NIST table, surface tension from IAPWS, at the liquid temperature of each row. Air: ideal gas at 101.325 kPa, viscosity by Sutherland's law.
- Tsai and Wang hydraulics: liquid at its inlet temperature, air at the mean of inlet and outlet. Song and Baumfrisch: the temperatures each table gives.
- Erasmus: the fixed properties of his Table 4.1 (water 1000 kg/m³, 8.02e-4 Pa·s; air 1.15 kg/m³, 1.86e-5 Pa·s). He gives no surface tension, so `sigma_L` is blank, and the temperature columns are blank because the thesis does not say which of its four temperatures is which.
- k_L·a rows: `D_L` from Wilke and Chang (1955), toluene molar volume 118.2 cm³/mol (Wang, Song) or CO₂ 34.0 (Baumfrisch). Wang's k_L runs: air at the water temperature.
- k_G·a rows: `D_V` of SO₂ in air from Fuller et al. (1966) in the form of Poling et al. (2001). Wang's k_G runs: water at 25 °C, air at its inlet temperature.
- Measured k·a: Wang gives k and a_e separately, so k·a = k × a_e. Song and Baumfrisch give k·a directly.

## Rows left out or blank
- Rows without temperatures (no properties can be computed) are dropped.
- A wet-bed hold-up of zero or not given is left blank; its ΔP is kept.
- Song's Table C.2 row printed at −17.8 °C with ΔP 0 (a logging glitch in the source) is dropped.
- Erasmus rows at zero gas velocity are dropped.

## Not listed in training_data.csv
- `RSP_250Y_M_baumfrisch2022.csv`: Super-Pak 250Y from the 0.422 m column. The manifest takes one file per packing, and Super-Pak 250Y uses the SRP file. Swap it in to train on Baumfrisch's data instead.
- `GTO_250Y_M.csv`: GT-OPTIMPak 250Y has no published void fraction, so the library gives it no constants and it cannot be trained yet.
- `FP_1Y_M.csv`: Flexipac 1Y has dry-bed rows only, and the library has only C_P0 for it, no C_S; `train.py` needs a C_S and wet rows to train.

## Check
Training `RSR_0_7_M`, `MPG_64Y_M` and `RMPN_250Y_M` from these files with `train.py` gives the library's constants exactly. Files that combine campaigns hold more data than the library fits and so give slightly different constants.
