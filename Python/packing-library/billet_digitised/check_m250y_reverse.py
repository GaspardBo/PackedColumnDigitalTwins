import os
# D39 (i): Mellapak 250Y constants fitted on Tsai's SRP column, used to predict Billet's own data
# (Fig 3.38 hold-up, Fig 3.39 flood points). Same equations as check_holdup_flood.py.
import numpy as np, pandas as pd
D = os.path.dirname(os.path.abspath(__file__)) + "/"
g, a, eps = 9.81, 250, 0.970
sets = {"Billet's own (Ch 0.554, CFl 2.464)": (0.554, 2.464), "fitted on Tsai (Ch 0.637, CFl 2.787)": (0.637, 2.787)}
h = pd.read_csv(D+'fig3_38_mellapak_holdup.csv'); uL = h.uL_1e3_m3_m2s/1e3; hm = h.holdup_1e2_m3_m3/1e2
rL, mL = 997.8, 0.955e-3
def hold(Ch):
    Re = uL*rL/(a*mL); Fr = uL**2*a/g
    ah = np.where(Re < 5, Ch*Re**0.15*Fr**0.1, 0.85*Ch*Re**0.25*Fr**0.1)
    return (12*mL*a**2*uL/(rL*g))**(1/3)*ah**(2/3)
fl = pd.read_csv(D+'fig3_39_flood_structured.csv'); fl = fl[fl.packing == 'Mellapak 250Y']
X, fm = fl.flow_param_1e2.values, fl.flood_factor_1e2_m_s.values
rL2, mL2, mV = 998.2, 1.002e-3, 1.81e-5
def yfl(C):
    psi = X/100; n = np.where(psi <= 0.4, -0.194, -0.708)
    xi = g/(C**2*(psi*(mL2/mV)**0.2)**(2*n)); hF = 0.3741*eps
    return np.sqrt(2*g/xi)*(eps-hF)**1.5/np.sqrt(eps)*np.sqrt(hF/a)*100
for k, (Ch, CF) in sets.items():
    d1 = 100*(hold(Ch)-hm)/hm; d2 = 100*(yfl(CF)-fm)/fm
    print(f'{k:38s} hold-up {abs(d1).mean():5.1f}% (bias {d1.mean():+5.1f})   flood {abs(d2).mean():5.1f}% (bias {d2.mean():+5.1f})')
