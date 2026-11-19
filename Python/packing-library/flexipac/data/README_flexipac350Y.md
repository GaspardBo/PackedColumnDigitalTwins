# Koch-Glitsch Flexipac 350Y (and 350Y HC): Erasmus 2004 thesis data

Source: Erasmus, A. B. (2004). Mass transfer in structured packing. PhD thesis, University of Stellenbosch. Appendices C, E, F. Extracted from the PDF text layer by script, not retyped.

Packing (Table 4.2): 350Y crimp 8.4 mm, base 15.5 mm, side 11.5 mm, void fraction 0.985, 45°, element height 265 mm (HC: 267 mm, void 0.984).

## Files
- `erasmus_flexipac350Y_hydraulics.csv`: Tables C.1-C.4. Pressure drop and liquid hold-up vs gas load. 303 rows: 350Y air/water 119, 350Y air/Kerosol 200 46, HC air/water 92, HC air/Kerosol 46. Rows with uG = 0 are static hold-up; uL = 0 rows are dry pressure drop.
- `erasmus_flexipac350Y_total_reflux.csv`: Tables F.1-F.2. Chlorobenzene/ethylbenzene total reflux, 20 runs (350Y) + 21 (HC), at about 1.01, 0.6 and 0.3 bar (Ptop). HETP is not tabulated: compute from distillate/bottoms compositions with the NRTL parameters in Appendix F.
- `erasmus_flexipac350Y_effective_area.csv`: Table E.4. Effective area from CO2 absorption into MEA in n-propanol. 12 rows (3 gas loads x 4 liquid loads).

## Rigs
- Hydraulics: 200 mm ID glass column, 3 elements (0.795 m bed for 350Y), ambient. Hold-up = free-draining volume after shutting off flows.
- Distillation: 200 mm ID, 4 beds of 3 elements (3.18 m packing for 350Y, my calculation), total reflux.

## Units
uL m3/(m2 h); uG m/s; F (Pa)^0.5; dP/dz Pa/m; hold-up m3/m3; dPseg and dPtotal mmH2O; Q kW; compositions wt% chlorobenzene.

## Caveats
- T1-T4 in Tables C are air and liquid inlet/outlet temperatures; the thesis does not say which column is which.
- `dPtotal` blank = "O/R" (outside measuring range).
- Tables F.3-F.4 (Flexipac 500Y) are referred to in the text but missing from this copy.
- Checks: no malformed rows; F/uG ratio 1.07-1.09 on every row (consistent with air density). Not checked by eye against page images.
