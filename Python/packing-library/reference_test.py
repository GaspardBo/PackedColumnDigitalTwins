# make_reference.py
import numpy as np
import pandas as pd
import packlib as pl

# Illustrative values, not from a published table. Only parity matters here.
pack = dict(ap=105.0, eps=0.95, dc=0.5, Cp=0.76, Clp=2.6, Cfp=2.2, Ch=0.78)
C = {k: pack[k] for k in ('Cp', 'Clp', 'Cfp', 'Ch')}

fluids = [
    dict(system='air/water', rho_L=998.0,  mu_L=1.0e-3, rho_G=1.2, mu_G=1.8e-5),
    dict(system='viscous',   rho_L=1050.0, mu_L=5.0e-3, rho_G=1.2, mu_G=1.8e-5),
]
uL_list = [5, 20, 40, 80]                 # m3/m2h
uG_list = np.linspace(0.2, 3.5, 34)       # m/s

rows = []
for f in fluids:
    s = pl.make_settings(pack['ap'], pack['eps'], pack['dc'], f['rho_G'], f['mu_G'],
                         {f['system']: (f['rho_L'], f['mu_L'])}, C, None)
    for uL in uL_list:
        for uG in uG_list:
            dp, hold = pl.billet(f['system'], uL, uG, C, s)
            lp = pl.loading_uG(f['system'], uL, uG, C, s)
            fp = pl.flood_uG(f['system'], uL, uG, C, s)
            rows.append(dict(ap=pack['ap'], eps=pack['eps'], dc=pack['dc'],
                             Cp=pack['Cp'], Clp=pack['Clp'], Cfp=pack['Cfp'], Ch=pack['Ch'],
                             rho_L=f['rho_L'], mu_L=f['mu_L'], rho_G=f['rho_G'], mu_G=f['mu_G'],
                             uL=uL, uG=uG, dp=dp, hold=hold, uG_lp=lp, uG_fp=fp))

df = pd.DataFrame(rows)
df.to_csv('reference.csv', index=False, na_rep='NaN', float_format='%.15g')

flooded = df['dp'].isna()
loaded = ~flooded & (df['uG'] > df['uG_lp'])
print(len(df), 'rows |', 'calm:', (~flooded & ~loaded).sum(),
      'loaded:', loaded.sum(), 'flooded:', flooded.sum())