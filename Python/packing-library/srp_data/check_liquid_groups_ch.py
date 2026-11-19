import os
# D42 quick test: can a liquid-property group make Ch carry across liquids? Tsai M250Y and M500Y, calm-zone hold-up,
# leave one liquid out. Viscosity per row from Tsai's Re_L (mu = rho*uL/(a*Re_L)); density ~ water (Tsai Tables B.3-B.5).
# Surface tension: 72 mN/m for water and polymer runs, 30 for the surfactant runs (Tsai abstract; polymer value is a hypothesis).
import numpy as np, pandas as pd
from scipy.optimize import minimize
D = os.path.dirname(os.path.abspath(__file__))
g, muG, Clp = 9.81, 1.84e-5, 3.157
t = pd.read_csv(f'{D}/tsai2010_tableB9.csv')
for c in ['ReL','L_m3_m2h','FG_Pa05','Tair_in_C','Tliq_in_C','holdup_pct']: t[c] = pd.to_numeric(t[c], errors='coerce')
t = t[t.packing.isin(['M250Y','M500Y']) & (t.L_m3_m2h > 0) & (t.holdup_pct > 0)].copy()
t['a'] = t.packing.map({'M250Y':250,'M500Y':500}); eps = 0.97
t['liq'] = t.run_header.str.extract(r'(Baseline|Intermediate μL|High μL|Low σ)')[0]
t['rhoL'] = 1000*(1 - (t.Tliq_in_C + 288.9414)/(508929.2*(t.Tliq_in_C + 68.12963))*(t.Tliq_in_C - 3.9863)**2)
t['uL'] = t.L_m3_m2h/3600; t['mu'] = t.rhoL*t.uL/(t.a*t.ReL)
t['sig'] = np.where(t.liq == 'Low σ', 0.030, 0.072)
t['rhoG'] = 101325*0.02897/(8.314*(t.Tair_in_C + 273.15)); t['uG'] = t.FG_Pa05/np.sqrt(t.rhoG)
t['h'] = t.holdup_pct/100
# calm zone: below Billet's loading point (Mellapak C_S), each row's own properties
psi = t.uL*t.rhoL/(t.uG*t.rhoG)*np.sqrt(t.rhoG/t.rhoL)
ht = 12**(1/3)*(t.uL*t.rhoL/(t.a*t.mu))**(-1/3)*(t.uL**2*t.a/g)**(1/3)
n = np.where(psi <= 0.4, -0.326, -0.723)
xi = g/(Clp**2*(psi*(t.mu/muG)**0.4)**(2*n))
t = t[t.uG <= np.sqrt(g/xi)*(eps-ht)*np.sqrt(ht/t.a)*np.sqrt(t.rhoL/t.rhoG)].copy()
def hold(r, Ch):
    Re = r.uL*r.rhoL/(r.a*r.mu); Fr = r.uL**2*r.a/g
    ah = np.where(Re < 5, Ch*Re**0.15*Fr**0.1, 0.85*Ch*Re**0.25*Fr**0.1)
    return (12*r.mu*r.a**2*r.uL/(r.rhoL*g))**(1/3)*ah**(2/3)
forms = {'one shared Ch': lambda r, p: p[0],
         'Ch x (mu/mu_w)^k': lambda r, p: p[0]*(r.mu/1e-3)**p[1],
         'Ch x (sigma/sigma_w)^m': lambda r, p: p[0]*(r.sig/0.072)**p[1],
         'Ch x both': lambda r, p: p[0]*(r.mu/1e-3)**p[1]*(r.sig/0.072)**p[2]}
npar = {'one shared Ch': 1, 'Ch x (mu/mu_w)^k': 2, 'Ch x (sigma/sigma_w)^m': 2, 'Ch x both': 3}
def fit(rows, form):
    obj = lambda p: np.sum(np.log(hold(rows, forms[form](rows, p))/rows.h)**2)
    return minimize(obj, [0.6] + [0]*(npar[form]-1), method='Nelder-Mead', options={'xatol':1e-6,'fatol':1e-10,'maxiter':4000}).x
for pk in ['M250Y', 'M500Y']:
    d = t[t.packing == pk]
    print(f'\n{pk}: calm rows per liquid', d.groupby('liq').size().to_dict())
    per = {lq: fit(d[d.liq == lq], 'one shared Ch')[0] for lq in d.liq.unique()}
    print('  Ch fitted per liquid:', {k: round(v, 3) for k, v in per.items()}, '| median mu mPa s:', d.groupby('liq').mu.median().mul(1000).round(1).to_dict())
    rows = []
    for form in forms:
        errs = {}
        for lq in d.liq.unique():
            tr, te = d[d.liq != lq], d[d.liq == lq]
            p = fit(tr, form)
            e = 100*(hold(te, forms[form](te, p)) - te.h)/te.h
            errs[lq] = (abs(e).mean(), e.mean())
        pa = fit(d, form)
        rows.append([form] + [f'{errs[l][0]:.1f} ({errs[l][1]:+.0f})' for l in ['Baseline','Intermediate μL','High μL','Low σ']] + [round(np.mean([v[0] for v in errs.values()]), 1), np.round(pa, 3).tolist()])
    print(pd.DataFrame(rows, columns=['form','water out','visc 5 out','visc 11-14 out','low σ out','mean','params on all']).to_string(index=False))
