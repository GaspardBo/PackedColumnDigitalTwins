# SRP hydraulic data (UT Austin, Separations Research Program)

Found 14 Nov 2026 for PLAN step 4 (shortlist of new packings with tabulated raw hydraulic data).

## Sources
- Tsai, R.E. (2010). *Mass Transfer Area of Structured Packing*. PhD dissertation, The University of Texas at Austin. https://sites.utexas.edu/rochelle/files/2015/02/Mass_Transfer_Area_of_Structured_Packing.pdf. Data: Table B.9 (PDF pp. 356–381). Geometry: Table 6.1 (PDF p. 168).
- Wang, C. (2015). *Mass Transfer Coefficients and Effective Area of Packing*. PhD dissertation, The University of Texas at Austin. https://sites.utexas.edu/rochelle/files/2015/02/PhD-Dissertation_Wang-Chao.pdf. Data: Table C.1 (PDF pp. 144–165). Geometry: Tables 5.1 and 5.2.

Both measured on the same SRP column (0.427 m ID, about 3 m bed), with dry and irrigated ΔP and hold-up against F_G at several liquid loads.

## Files
| File | Rows | Columns |
|---|---|---|
| tsai2010_tableB9.csv | 1343 | packing, run_header, page, L (m³/m²h), F_G (Pa^0.5), three temperatures, ΔP/Z (Pa/m), hold-up (%), Re_L, We_L×10⁴, Fr_L |
| wang2015_tableC1.csv | 885 | packing, run_header, page, L, F_G, three temperatures, ΔP/Z, hold-up (fraction), Re_L |

`run_header` holds the packing, SRP campaign number, liquid condition (Baseline = water; High/Intermediate μL = viscous; Low σ = surfactant) and bed height.

Extraction: words rebuilt into rows by position (`raw/extract_rows.py`), then parsed (`raw/rows_to_csv.py`). Fixes:
- Tsai p359, dry run M250Y SRP 0715: two temperature cells blank; ΔP read from the 4th value.
- Tsai p363, M500Y SRP 0625: no hold-up column; ΔP kept, hold-up blank.
- Tsai p372: two rows without hold-up dropped.

## Caveats
- Wang hold-up is given as a fraction to two decimals (e.g. 0.03), so about ±15% rounding at low loads. Tsai gives % to two decimals.
- Wang gives no void fraction. Tsai Table 6.1 does (e.g. M250Y 0.95, which differs from Billet's 0.970).
- Tsai's viscous and low-σ runs need liquid density and viscosity per run (μ and σ are in Tsai Table 7.1; density to be found).
- Wang tabulates RSR#0.3 and #0.7 only, not #0.5.
- Same packing, two campaigns: M250Y dry ΔP at F ≈ 1.8 is 91 Pa/m (Tsai) and 78 Pa/m (Wang MP250Y). Different elements and campaigns, so about 15% between-campaign spread.

## Mass-transfer tables (added 14 Nov)
| File | Rows | Source | Measures |
|---|---|---|---|
| tsai2010_tableB8_masstransfer.csv | 493 | Tsai Table B.8 (PDF pp. 341–355) | a_f = a_e/a_p by CO₂ absorption into 0.1 M NaOH, plus ΔP/Z, Re_L, We_L, Fr_L, and the kinetics and property values used (k_OH, D_CO2, H_CO2, [OH⁻]) |
| wang2015_tableD1_area.csv | 337 | Wang Table D.1 (PDF pp. 166–174) | a_f, same method as Tsai |
| wang2015_tableD2_kL.csv | 120 | Wang Table D.2 (PDF pp. 175–178) | k_L by air stripping of toluene from water (liquid-film controlled), with NTU, HTU and the a_e used |
| wang2015_tableD3_kG.csv | 69 | Wang Table D.3 (PDF pp. 178–180) | k_G by SO₂ absorption into 0.1 M NaOH (gas-film controlled), with NTU, HTU×10 and the a_e used |

Columns keep the printed scaling (e.g. `kL_1e5_ms` is k_L × 10⁵ in m/s). Each file has `packing`, `liquid` (Baseline = water), `bed_height_m`, `run_header` and `page`.

How the coefficients were obtained (Wang, 2015, pp. 31–33): k_L·a from the toluene balance, then k_L = k_L·a / a_e (eq. 3-5); k_G = u_G·ln(y_in/y_out) / (Z·R·T·a_e) (eq. 3-6), with SO₂ end effects subtracted. a_e is taken at the same flows. So k_L·a and k_G·a are recovered exactly as k × a_e from the `ae_m2_m3` column.

Extraction: Wang rows rebuilt by position (`raw/extract_mt_rows.py`, output `raw/wang_mt_rows.txt`); every row has the expected 10 or 9 values. Tsai B.8 is laid out with cells at uneven heights, so it is parsed in text-stream order instead (`raw/mt_to_csv.py`); every run splits into whole 14-value rows. First and last rows checked by eye against the PDF.

### How this maps onto Billet's mass-transfer model
Billet (1995, Ch. 5) predicts three things separately: liquid-side β_L·a_Ph with constant C_L (eq. 5-8, p. 121), vapour-side β_V·a_Ph with constant C_V (section 5.2), and the interfacial area ratio a_Ph/a (section 5.3, p. 124). Wang's a_e, k_L and k_G line up with those three, so C_L and C_V can be fitted per packing the same way as C_h and C_P on the hydraulics side.

Billet's Table 5.2 (p. 123) gives C_L and C_V for arranged packings, but only for Ralupak YC-250, Montz B1-200, B1-300, C1-200, C2-200 and Euroform. None of our shortlist candidates is in it, so a mass-transfer Level 0 needs another source of constants. Billet and Schultes (1999) Table 2b may list Mellapak 250Y; that PDF is a scan with no text layer, so this is unchecked.

### Coverage per packing
✓ = tabulated. Liquids: W = water, V = viscous (intermediate or high μ_L), S = low σ.

| Packing | Source | Hydraulics | a_e | k_L | k_G | Liquids |
|---|---|---|---|---|---|---|
| M250Y | Tsai | ✓ | ✓ | | | W, V, S (both tables) |
| M500Y | Tsai | ✓ | ✓ | | | W, V, S (both tables) |
| M250X | Tsai | ✓ | ✓ | | | W, V (hydraulics); W, V, S (area) |
| MP252Y | Tsai | ✓ | ✓ | | | W, V (hydraulics); W, V, S (area) |
| M250YS | Tsai | ✓ | ✓ | | | W (hydraulics); W, S (area) |
| M125Y | Tsai | ✓ | ✓ | | | W (hydraulics); W, S (area) |
| M2Y | Tsai | ✓ | ✓ | | | W |
| P500 | Tsai | ✓ | ✓ | | | W (hydraulics); W, S (area) |
| F1Y (Flexipac 1Y) | Tsai | dry only | ✓ | | | W, V (area) |
| MP250Y, MP250X, MP2X | Wang | ✓ | ✓ | ✓ | ✓ | W |
| RSP250Y, RSP200X | Wang | ✓ | ✓ | ✓ | ✓ | W |
| GT-PAK 350Y, 350Z, 500Y | Wang | ✓ | ✓ | ✓ | ✓ | W |
| A350Y, B350X | Wang | ✓ | ✓ | ✓ | ✓ | W |
| RSR #0.3, #0.7 | Wang | ✓ | ✓ | ✓ | ✓ | W |
| MP125Y | Wang | | ✓ | ✓ | ✓ | W |
| RSR #0.5 | Wang | | ✓ | | | W |

### Mass-transfer caveats
- k_G runs are few (4–7 per packing) and mostly at one or two liquid loads, so k_G constrains C_V mainly through the gas rate.
- Wang's k_G bed heights for MP125Y (1.87 m) and B350X (2.87 m) are printed as shown but match their k_L and area beds, not the 0.2–0.9 m k_G beds. Probably a typo in the table; kept as printed.
- a_e from CO₂/NaOH is the area active for a fast reaction. Whether it equals Billet's a_Ph for physical absorption is an assumption of the SRP method (hypothesis for our use).
- Only Tsai varies the liquid, and only for a_e. All k_L and k_G data are air/water.

## Packings available
Tsai: M250Y (water, 2 viscous, low σ; 3 packing configs), M500Y (water, 2 viscous, low σ), M250X (water, viscous), MP252Y (water, viscous), M250YS (smooth), M125Y, M2Y, P500 (prototype), F1Y (dry only).
Wang: MP125Y? (in Table 5.1, not tabulated), MP2X, MP250Y, MP250X, RSP200X, RSP250Y, GT-PAK 350Y, GT-PAK 350Z, A350Y, B350X, GT-PAK 500Y, RSR#0.3, RSR#0.7 (all air/water).
