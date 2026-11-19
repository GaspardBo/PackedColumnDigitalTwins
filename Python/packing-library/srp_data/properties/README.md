# Fluid properties for the SRP data (D49)

## nist_water_0_30C.csv
Liquid water at 0.101325 MPa, 0 to 30 °C in 1 °C steps: density (kg/m³) and viscosity (Pa·s), copied from the table as printed (the WebBook prints temperatures as 0.01, 1.01, ... °C).
Source: NIST Chemistry WebBook, NIST Standard Reference Database 69, Thermophysical Properties of Fluid Systems (Lemmon, McLinden and Friend). Retrieved 17 Nov 2026:
https://webbook.nist.gov/cgi/fluid.cgi?Action=Data&Wide=on&ID=C7732185&Type=IsoBar&Digits=5&P=0.101325&THigh=30&TLow=0&TInc=1&RefState=DEF&TUnit=C&PUnit=MPa&DUnit=kg%2Fm3&HUnit=kJ%2Fkg&WUnit=m%2Fs&VisUnit=Pa*s&STUnit=N%2Fm
Use: linear interpolation in temperature.

## nist_water_0_40C.csv
Same source and columns, extended to 40 °C (retrieved 17 Nov 2026, same URL with THigh=40) for the Super-Ring #0.3 runs (liquid up to 31.9 °C). Rows 0 to 30 °C are identical to nist_water_0_30C.csv.
