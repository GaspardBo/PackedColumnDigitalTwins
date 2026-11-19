# Packing library (Python)

Packed-column hydraulics library behind the digital twin: pressure drop and liquid hold-up for a packing, at three levels.

- **Level 0:** the Billet and Schultes model with published constants.
- **Level 1:** the same model with its constants (Cp, Ch, Cfp) fitted to test data for the packing.
- **Level 2:** a correction on top of Level 1, trained on a user's own column data (linear, or a small neural network chosen automatically per packing).

Each level is tested with four splits (leave one point out, leave one curve out, train on one liquid and test on another where available, and hold out the top 25% of gas load per curve), always on the points every model answers.

## Folder map

| Where | What |
|---|---|
| `packlib.py` | The shared library: Billet model, constant fitting, test splits, Level 2, results tables. Every packing notebook imports it. |
| `flexipac/` | Flexipac 350Y and 350Y HC (Erasmus 2004 data, 0.2 m column, water and Kerosol). `flexipac.ipynb` is the original step-by-step build; `PLAN.md` is the project plan. |
| `rsp250y/`, `rsp200x/` | Raschig Super-Pak 250Y and 200X. |
| `superring03/` | Raschig Super-Ring #0.3 (random packing). |
| `m250y/`, `m500y/`, `m125y/`, `mp250y/` | Mellapak 250Y, 500Y, 125Y (Tsai 2010) and Mellapak 250Y (Wang 2015). |
| `gtpak350y/` | GT-PAK 350Y (void fraction still provisional). |
| `srp_data/` | Hydraulic and mass-transfer tables extracted from the Tsai (2010) and Wang (2015) theses (UT Austin SRP column, 0.427 m), the extraction scripts, and NIST water properties. See `srp_data/README.md`. |
| `billet_digitised/` | Data digitised from Billet (1995) figures, used to check the model against Billet's own data. |
| `docs/` | Explainers: Level 2 (notebook section 6) and a line-by-line walkthrough of `packlib.py`. |

## Running a notebook

```
pip install -r requirements.txt
```

Open a packing notebook (e.g. `superring03/superring03.ipynb`) from inside its own folder and run all cells. Each one reads `../packlib.py` and `../srp_data/`. The leave-one-point-out split and the auto-tuned Level 2 refit the model many times, so a full run takes from a few minutes to about half an hour for the larger Mellapak data sets.

## Sources (not stored here, for copyright)

- Billet, R. (1995). *Packed Towers in Processing and Environmental Technology*. VCH.
- Billet, R. and Schultes, M. (1999). Prediction of mass transfer columns with dumped and arranged packings. *Trans IChemE* 77(A), 498–504.
- Erasmus, A. B. (2004). *Mass transfer in structured packing*. PhD thesis, University of Stellenbosch.
- Tsai, R. E. (2010). *Mass Transfer Area of Structured Packing*. PhD thesis, UT Austin. https://sites.utexas.edu/rochelle/files/2015/02/Mass_Transfer_Area_of_Structured_Packing.pdf
- Wang, C. (2015). *Mass Transfer Coefficients and Effective Area of Packing*. PhD thesis, UT Austin. https://sites.utexas.edu/rochelle/files/2015/02/PhD-Dissertation_Wang-Chao.pdf
- Rejl, F. J. et al. (2015). Hydraulic and mass-transfer characteristics of Raschig Super-Pak 250Y. *Chem. Eng. Res. Des.* 99, 20–27.
- Koch-Glitsch structured packing brochure (2010); Raschig Super-Pak data sheet (Raschig USA, 2014).
- NIST Chemistry WebBook, SRD 69 (water properties).

Code comments cite decision numbers (D13a, D46 and so on) from the project's methodology notes, which are kept separately for the report.
