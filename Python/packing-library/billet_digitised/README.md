# Digitised figures from Billet (1995)

Source: Billet, R. (1995). *Packed Towers in Processing and Environmental Technology*. VCH. File: `../vdoc.pub_packed-towers-in-processing-and-environmental-technology.pdf`. Digitised by Amogh, 13 Nov 2026. Values are on the printed axis scale (×10², ×10³).

| Tidy file | Figure (book page / PDF page) | Contents |
|---|---|---|
| fig3_38_mellapak_holdup.csv | Fig. 3.38 (66 / 88) | Mellapak 250Y metal, air/water, 295 K, below loading. 19 points. |
| fig3_39_flood_structured.csv | Fig. 3.39 (66 / 88) | Flood points, 7 marker sets, air/water 293 K except the chloro-/ethylbenzene set (67 mbar). 34 points. |
| fig3_20_montz_b1300_dp.csv | Fig. 3.20 (55 / 77), middle panel | Montz B1-300 metal, ds 0.22 m, chloro-/ethylbenzene, L/V = 1. 133 mbar (16 points) and 67 mbar (10). 33 mbar not digitised; the three curves overlap. |
| fig4_7_hiflow_ratio.csv | Fig. 4.7 (90 / 112) | 50 mm Hiflow ring PP, air/water, wet/dry ΔP ratio. 36 data points, 7 theory-curve points. |

## Raw files (`raw/`) and how they map
- Fig. 3.38: `fig3_38_mellapak_holdup.csv` (`Default Dataset.csv` is an identical copy).
- Fig. 3.39: `fig3_39_flood_structured.csv` = Mellapak 250Y (circles); `Dataset 1` = Ralupak 250YC (triangles); `Dataset 2` = Montz B1-300 (barred squares); `Dataset 3` = Montz B1-300 (plain squares); `Dataset 4` = Montz B1-200 (open down-triangles; `Dataset 4 (1)` identical); `Dataset 5` = Montz B1-100 (filled down-triangles); `Dataset 6` = Montz B1-300, chloro-/ethylbenzene. Mapping checked against a zoomed render of the figure.
- Fig. 3.20: `Default Dataset (1)` = 133 mbar; `Dataset 1 (1)` = 67 mbar (`Dataset 1 (2)` identical).
- Fig. 4.7: `Default Dataset (2)` = data; `theory.csv` = theory curve.

## Checks
- `check_holdup_flood.py`: Figs. 3.38, 3.39, 4.7 against Billet with his tabulated constants.
- `check_montz_dp.py`: Fig. 3.20 against Billet's full ΔP model (book form, wall factor divides). Properties from book Table 3.8; Cp 0.295 from Table 5.6; C_S 3.098, C_Fl 2.464 from the loading/flood table.
- Results are in D36 of `../flexipac/docs/2026-11-11_level1_methodology_notes.md`.
