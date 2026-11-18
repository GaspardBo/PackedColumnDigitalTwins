import os
# Level 0 (Billet's own Mellapak 250Y constants) vs Level 1 (fitted), Tsai 2010 water data, leave one curve out.
import numpy as np, pandas as pd, sys, re
from scipy.optimize import minimize_scalar
D = os.path.dirname(os.path.abspath(__file__))
ap, eps, dc, g = 250.0, float(sys.argv[1]) if len(sys.argv) > 1 else 0.970, 0.427, 9.81
rho_W, mu_W, mu_G = 1000.0, 1.0e-3, 1.84e-5          # Billet's water reference; air viscosity
C0 = {'Cp': 0.292, 'Clp': 3.157, 'Cfp': 2.464, 'Ch': 0.554}   # Billet & Schultes 1999 Table 2b
def mu_water(T): return 2.414e-5 * 10**(247.8/(T + 273.15 - 140))   # Pa s
def rho_water(T): return 1000*(1 - (T + 288.9414)/(508929.2*(T + 68.12963))*(T - 3.9863)**2)
def rho_air(T): return 101325*0.02897/(8.314*(T + 273.15))
def num(x):
    try: return float(x)
    except: return np.nan
t = pd.read_csv(f'{D}/tsai2010_tableB9.csv')
t = t[(t.packing == 'M250Y') & t.run_header.str.contains('Baseline')].copy()
for c in ['L_m3_m2h','FG_Pa05','Tair_in_C','Tliq_in_C','dPdz_Pa_m','holdup_pct']: t[c] = t[c].map(num)
t['rhoG'] = rho_air(t.Tair_in_C); t['uG'] = t.FG_Pa05/np.sqrt(t.rhoG)
t['rhoL'] = rho_water(t.Tliq_in_C); t['muL'] = mu_water(t.Tliq_in_C)
t['hold'] = t.holdup_pct/100; t['run'] = t.run_header.str.extract(r'(SRP \d+)')[0]
dry = t[t.L_m3_m2h == 0].copy()
wet = t[(t.L_m3_m2h > 0) & (t.hold > 0)].copy()
wet['Lnom'] = wet.L_m3_m2h.round(-0) ; wet['Lnom'] = wet.groupby('run').L_m3_m2h.transform(lambda s: s.round(0))
wet['curve'] = wet.run + ' L' + wet.L_m3_m2h.map(lambda v: f'{v:.0f}')
fs = 1/(1 + 4/(ap*dc))                                  # Billet's K
def dry_dp(r, C):
    dpp = 6*(1-eps)/ap; Re = r.uG*dpp*r.rhoG/((1-eps)*mu_G)*fs
    return C['Cp']*(64/Re + 1.8/Re**0.08)*ap/eps**3*r.uG**2/2*r.rhoG/fs      # divides (D13a)
def points(r, C):
    uL = r.L_m3_m2h/3600; rL, mL, rG, uG = r.rhoL, r.muL, r.rhoG, r.uG
    psi = uL*rL/(uG*rG)*np.sqrt(rG/rL)
    ht = 12**(1/3)*(uL*rL/(ap*mL))**(-1/3)*(uL**2*ap/g)**(1/3)
    n = -0.326 if psi <= 0.4 else -0.723
    xi = g/(C['Clp']**2*(psi*(mL/mu_G)**0.4)**(2*n)); u_lp = np.sqrt(g/xi)*(eps-ht)*np.sqrt(ht/ap)*np.sqrt(rL/rG)
    nf = -0.194 if psi <= 0.4 else -0.708
    xf = g/(C['Cfp']**2*(psi*(mL/mu_G)**0.2)**(2*nf)); hf = 0.3741*eps*(mL*rho_W/(rL*mu_W))**0.05
    u_fp = np.sqrt(2*g/xf)*(eps-hf)**1.5/np.sqrt(eps)*np.sqrt(hf/ap)*np.sqrt(rL/rG)
    return ht, hf, u_lp, u_fp
def billet(r, C):
    ht, hf, u_lp, u_fp = points(r, C)
    if r.uG > u_fp: return np.nan, np.nan
    uL = r.L_m3_m2h/3600; rL, mL = r.rhoL, r.muL
    hdp = ht if r.uG <= u_lp else ht + (hf-ht)*(r.uG/u_fp)**13
    ReL = uL*rL/(ap*mL); FrL = uL**2*ap/g
    ah = C['Ch']*ReL**0.15*FrL**0.1 if ReL < 5 else 0.85*C['Ch']*ReL**0.25*FrL**0.1
    h2 = (12*mL*ap**2*uL/(rL*g))**(1/3)*ah**(2/3)
    hold = h2 if r.uG <= u_lp else h2 + (2.2*h2 - h2)*(r.uG/u_fp)**13
    dpp = 6*(1-eps)/ap; Re = r.uG*dpp*r.rhoG/((1-eps)*mu_G)*fs
    xi_d = C['Cp']*(64/Re + 1.8/Re**0.08); d0 = dry_dp(r, C)
    xi_w = xi_d*np.exp(ReL/200)*(hdp/ht)**0.3*((eps-hdp)/eps)**1.5
    return d0*(xi_w/xi_d)*(eps/(eps-hdp))**3, hold
def pred(rows, C): return pd.DataFrame([billet(r, C) for r in rows.itertuples()], columns=['dp','hold'], index=rows.index)
def le(p, m):
    s = np.log(np.asarray(p, float)/np.asarray(m, float))**2; s[np.isnan(s)] = 1; return s.sum()
def fit(train, mode):
    C = dict(C0)
    C['Cp'] = minimize_scalar(lambda c: le([dry_dp(r, dict(C, Cp=c)) for r in dry.itertuples()], dry.dPdz_Pa_m), bounds=(0.01, 2), method='bounded').x
    calm = train[[r.uG <= points(r, C0)[2] for r in train.itertuples()]]
    C['Ch'] = minimize_scalar(lambda c: le(pred(calm, dict(C, Ch=c)).hold, calm.hold), bounds=(0.05, 3), method='bounded').x
    floor = C['Cfp']*max(r.uG/points(r, C)[3] for r in train.itertuples())*1.000001
    def sc(c):
        p = pred(train, dict(C, Cfp=c)); return le(p.hold, train.hold) + (le(p.dp, train.dPdz_Pa_m) if mode == 'both' else 0)
    C['Cfp'] = minimize_scalar(sc, bounds=(floor, 10), method='bounded').x
    return C
print(f'eps {eps}; dry rows {len(dry)}, wet rows {len(wet)}, curves {wet.curve.nunique()}')
print('Level 1 on all rows, both:', {k: round(v, 3) for k, v in fit(wet, 'both').items()})
print('Level 1 on all rows, hold:', {k: round(v, 3) for k, v in fit(wet, 'hold').items()})
P = {'L0': [], 'both': [], 'hold': []}
for cv, test in wet.groupby('curve'):
    tr = wet.drop(test.index)
    P['L0'].append(pred(test, C0)); P['both'].append(pred(test, fit(tr, 'both'))); P['hold'].append(pred(test, fit(tr, 'hold')))
P = {k: pd.concat(v).loc[wet.index] for k, v in P.items()}
common = wet.index[np.all([P[k].dp.notna() for k in P], axis=0)]
calm = wet.index[[r.uG <= points(r, C0)[2] for r in wet.itertuples()]]
print(f'answered by all: {len(common)} of {len(wet)}')
print(f"{'model':5s} {'ans':>4s} {'dp_abs':>7s} {'dp_bias':>8s} {'h_abs':>6s} {'h_bias':>7s} | calm dp_abs, loading dp_abs")
for k, p in P.items():
    e = 100*(p.dp - wet.dPdz_Pa_m)/wet.dPdz_Pa_m; h = 100*(p.hold - wet.hold)/wet.hold
    cc = common.intersection(calm); cl = common.difference(calm)
    print(f'{k:5s} {p.dp.notna().sum():4d} {e[common].abs().mean():7.1f} {e[common].mean():8.1f} {h[common].abs().mean():6.1f} {h[common].mean():7.1f} | {e[cc].abs().mean():5.1f} ({len(cc)}), {e[cl].abs().mean():5.1f} ({len(cl)})')
dc_ = 100*(np.array([dry_dp(r, C0) for r in dry.itertuples()]) - dry.dPdz_Pa_m)/dry.dPdz_Pa_m
print(f'dry dP with Billet Cp 0.292: abs {dc_.abs().mean():.1f}%, bias {dc_.mean():.1f}%')
