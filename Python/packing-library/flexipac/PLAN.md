# Plan (Plan C, 11 Nov – 1 Dec, write week of 7 Dec)

Current step: **Week 2, step 5** (Week 1 done 17 Nov: 4a HC, 4b RSP 250Y and Super-Ring #0.3, all on `../packlib.py`) (items 1–4 done 15 Nov; Flexipac 350Y Level 1 and Level 2 are version 1, see D42 for the planned surface-tension update).

**Scope (Amogh, 11 Nov): everything below is in. Nothing gets cut for time; we put in more hours instead.**

**The two tiers:**
- **Level 1:** physics with fitted or geometry-predicted constants, for any user.
- **Level 2:** a correction trained on the user's own operating data, on top of Level 1. This is the digital twin Klaus requires.

## Week 1 (11–17 Nov)
1. **Level 1: fit Billet's constants** (notebook section 5). Fix the section 3 bugs first. **Done 15 Nov (version 1).**
2. **The Level 2 method: a dimensionless correction** on top of Level 1. Inputs are Re_L and u_G/u_G,flood. Trained on Flexipac's rig data here. **Done 15 Nov (notebook 6.1–6.5): linear correction chosen (D22).** Leave one curve out, ΔP / hold-up %: Level 0 22.7 / 27.3, Level 1 35.8 / 18.6, Level 2 15.6 / 11.6. Not applicable to the water-to-Kerosol test.
3. **Four splits** for Level 0 vs Level 1, with and without the correction, compared on common points. **15 Nov: Level 0 vs Level 1 done (notebook 5.11).** Headline Level 1 = Cfp on hold-up only (D30). On common points it's no worse than Level 0 on hold-up and worse on ΔP (the early-flood cancellation, D36 (5)); it answers every point where Level 0 answers 75/115, 18/41 and 4/26. Top-F comparison rests on 4 common points, so it's reported as coverage only. With correction: done 15 Nov (step 2).
4. **Shortlist tabulated raw hydraulic data for 6–8 new packings.** Checkpoint 17 Nov: fewer than 6 means geometry moves to future work. **14 Nov: data found and extracted for 21 packings** (Tsai 2010, Wang 2015, UT Austin SRP column; see `../srp_data/README.md`). Checkpoint met. **Shortlist decided 14 Nov (D37):** M250Y, M500Y, M125Y, MP250Y, GT-PAK 350Y, RSP 250Y, RSP 200X, Super-Ring #0.3; the other Wang packings with full mass-transfer data are added as time allows. **Mass transfer:** effective area for all 21 (Tsai B.8, Wang D.1), and k_L and k_G for Wang's 13 (D.2, D.3), extracted to `../srp_data/`. These match Billet's three mass-transfer outputs (a_Ph, β_L with C_L, β_V with C_V; Billet 1995 Ch. 5), so steps 7 and 8 can fit C_L and C_V per packing.
4a. **"Works on any packing" test 1: Flexipac 350Y HC.** Its data is already in `data/erasmus_flexipac350Y_hydraulics.csv`: 15 dry rows plus 5 water and 5 Kerosol curves. Run the same notebook pipeline. A scratch preview beat Level 0 on unseen curves: on the 54 points both answer, ΔP 28.6% against 35.9% (14 Nov recheck); hold-up 20.1% against 34.6% (12 Nov); 109 points answered against 54. HC's specific area: 350 m²/m³, confirmed 14 Nov from the Koch-Glitsch brochure (p. 4), so the preview's assumption stands (D8). **15 Nov: `flexipac_hc.ipynb` set up** (copy of `flexipac.ipynb`; changes: packing filter, ε 0.984 from Erasmus Table 4.2, file names, Level 2 skips the one Kerosol row with no measured hold-up). Level 0 borrows 350Y's constants (D44); Cp fitted without the lowest-F dry row, Cp 0.196 (D45). Run 16 Nov, leave one curve out, ΔP / hold-up % on 54 common points: Level 0 35.9 / 34.6, Level 1 28.9 / 19.9, Level 2 linear 11.1 / 13.6, network 8.6 / 18.7. Top-F test has no common points (Level 0 answers 0 of 27).
4b. **"Works on any packing" test 2: a packing from a fully different family** (random, or a different structured family). **Decided 14 Nov (D38): RSP 250Y first, then Super-Ring #0.3** (a different size from the FRI #0.7 demo packing). HC is only a sibling of 350Y (same maker, rig and thesis), so the claim needs this second test. **17 Nov: RSP 250Y done (version 1), `../rsp250y/rsp250y.ipynb` on the shared library `../packlib.py` (D47).** Level 0 = Rejl et al. (2015) constants from a 0.29 m column (D48). Leave one curve out, ΔP / hold-up % on 51 common points of 74: Level 0 19.6 / 19.3 (answers 51 of 74, floods early), Level 1 23.0 / 7.9 (answers 73), Level 2 linear 16.4 / 6.2, Level 2 tuned 4.7 / 6.5 (rerun under D55). Level 1 hold-up beats Level 0 beyond the spread (−12.2 ± 7.1 per curve); ΔP no worse. Top-F test has 1 common point, coverage only. **17 Nov: Super-Ring #0.3 done (version 1, D54, D55), `../superring03/superring03.ipynb`.** Geometry a 315, ε 0.96 (Wang Table 5.2; Billet and Schultes 1999 Table 2a agree). Level 0 = Billet and Schultes (1999) Table 2a constants for this exact packing. Leave one curve out, ΔP / hold-up % on 45 common points of 50: Level 0 26.9 / 21.3 (answers 45), Level 1 46.3 / 7.6, Level 2 linear 14.8 / 6.3, Level 2 tuned 6.0 / 6.0. Level 1 hold-up better on every curve; Level 1 ΔP worse on every curve: the published Cp overpredicts dry ΔP by 38%, which hides Billet's weak liquid term in Level 0 (same mechanism as D36 on Flexipac 350Y). Also found: FRI test report on RSP 250 (1.22 m column, C6/C7 and o/p-xylene distillation, Cai and King 2009), usable for step 10 HETP and a cross-column check.

## Week 2 (18–24 Nov)
5. Extract shortlisted tables into the library format. **18 Nov: first run done for all six (D56), one notebook each in `../<packing>/`.** Leave one curve out, ΔP / hold-up % on common points (* = borrowed Level 0): M250Y L0 52.5 / 13.8, L1 10.5 / 11.1, L2 tuned 10.6 / 10.1; M500Y L0* 41.5 / 18.0, L1 36.0 / 10.2, L2 tuned 13.6 / 7.8; M125Y L0* 35.9 / 15.0, L1 10.0 / 12.9, L2 tuned 5.1 / 6.7; MP250Y L0 49.2 / 15.3, L1 19.7 / 6.6, L2 tuned 6.7 / 7.6; GT-PAK 350Y L0* 44.0 / 16.7, L1 39.9 / 7.7, L2 tuned 30.4 / 6.3 (ε provisional, ΔP to be investigated); RSP 200X L0* 27.0 / 26.8, L1 43.0 / 7.6, L2 tuned 9.7 / 5.5. Open: GT-PAK ε (Texas A&M dissertation, needs library login), D57 (M250Y dry run without temperatures).
6. Run the pipeline on Super-Ring 0.7.
6a. **Level 2 demo on FRI Super-Ring 0.7 (1.22 m industrial column), standing in for the user's plant.**
    - Level 1 comes from the library: constants fitted on Wang's SRP Super-Ring #0.7 data (0.427 m, air/water), so the demo also measures how far Level 1 carries to a different column (D39).
    - The correction is trained on part of the FRI data and tested on the rest.
    - Outputs: pressure drop and hold-up.
7. Code Billet's mass-transfer model.
7a. **Flexipac 350Y mass transfer (reinstated 11 Nov).**
    - Get HETP from Erasmus's total-reflux runs (Tables F.1 and F.2, 20 runs in `data/erasmus_flexipac350Y_total_reflux.csv`): count stages with NRTL (Erasmus Appendix F), then HETP = packed height (3.18 m) ÷ stages.
    - Re-extract the effective-area table (Table E.4). The file was lost earlier.
    - Fit Billet's mass-transfer constants for Flexipac. Then Flexipac has all three outputs, and the mass-transfer model is tested on two packings.

8b. **Level 2 network auto-tuned per packing (D46):** add to the notebook pipeline, run next to linear on every packing and on FRI (6a). Week 2–3.

8a. **Liquid-property groups in Level 1 (D42):** on Tsai's four-liquid Mellapak 250Y and 500Y data, leave one liquid out. Week 2–3.

## Week 3 (25 Nov – 1 Dec)
8. Fit constants for each new packing.
9. Geometry → constants, leaving one packing out at a time.
9a. **Level 2 with geometry-predicted Level 1:** the same demo as 6a, with Super-Ring's constants predicted from geometry instead of taken from the library.
10. Mass-transfer constants from FRI HETP.
10a. **Mass-transfer efficiency as the third Level 2 output.**
11. Freeze results and plots. **Every reported table is regenerated from the final pipeline at this point, so no comparison mixes model versions (D43).**
12. TikZ diagrams.

## Gaspard (from the 28 Oct plan)
Dynamics, with a dimensionless-group basis for SINDy. Aspen absorber model for Klaus's "show it beats Aspen" comparison.

## Cut (future work)
- DWSIM.
- Flexipac 500Y: Tables F.3 and F.4 are missing from our copy of the thesis. Reinstate if a complete copy turns up.
- Wall-factor check.
