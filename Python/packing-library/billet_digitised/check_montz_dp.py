import os
import numpy as np, pandas as pd
from scipy.optimize import minimize_scalar
D=os.path.dirname(os.path.abspath(__file__)) + "/"
g=9.81; a=300; eps=0.930; ds=0.22; Cp0=0.295; Cs=3.098; CFl=2.464
# book Table 3.8: (rhoV, nuV, rhoL, nuL)
P={133:(0.510,15.8e-6,926,0.45e-6), 67:(0.268,28.7e-6,941,0.52e-6)}
def billet_dp(F,p,Cp=Cp0):
    rV,nV,rL,nL=P[p]; mV=rV*nV; mL=rL*nL
    uV=F/np.sqrt(rV); uL=uV*rV/rL            # total reflux, L/V = 1 by mass (MW 112.6 vs 106.2, hypothesis-level approx)
    psi=uL*rL/(uV*rV)*np.sqrt(rV/rL)
    hS=(12*mL*a**2*uL/(rL*g))**(1/3)         # theoretical hold-up below loading (eq 4-27 form, no Ch)
    n=-0.326 if psi<=0.4 else -0.723
    xi=g/(Cs**2*(psi*(mL/mV)**0.4)**(2*n)); uS=np.sqrt(g/xi)*(eps-hS)*np.sqrt(hS/a)*np.sqrt(rL/rV)
    hF=0.3741*eps*(mL*1000/(rL*1.0e-3))**0.05
    n=-0.194 if psi<=0.4 else -0.708
    xi=g/(CFl**2*(psi*(mL/mV)**0.2)**(2*n)); uF=np.sqrt(2*g/xi)*(eps-hF)**1.5/np.sqrt(eps)*np.sqrt(hF/a)*np.sqrt(rL/rV)
    if uV>uF: return np.nan,uS*np.sqrt(rV),uF*np.sqrt(rV)
    h=hS if uV<=uS else hS+(hF-hS)*(uV/uF)**13
    dp_=6*(1-eps)/a; K=1/(1+4/(a*ds))
    ReV=uV*dp_*rV/((1-eps)*mV)*K; ReL=uL*rL/(a*mL)
    xiL=Cp*np.exp(ReL/200)*(h/hS)**0.3*(64/ReV+1.8/ReV**0.08)*((eps-h)/eps)**1.5
    return xiL*a/(eps-h)**3*F**2/2/K, uS*np.sqrt(rV), uF*np.sqrt(rV)
d=pd.read_csv(D+'fig3_20_montz_b1300_dp.csv')
out=[]
for x in d.itertuples():
    pr,FS,FF=billet_dp(x.F,x.pressure_mbar); out.append((pr/100,FS,FF))
d['billet_mbar_m'],d['F_load'],d['F_flood']=zip(*out)
d['dev%']=100*(d.billet_mbar_m-d.dp_mbar_m)/d.dp_mbar_m
pd.set_option('display.width',200); print(d.round(3).to_string(index=False))
for p,t in d.groupby('pressure_mbar'):
    ok=t.dropna(); lo=ok[ok.F<=ok.F_load]; hi=ok[ok.F>ok.F_load]
    print(p,'mbar: answered %d/%d, mean|dev| all %.1f%%, bias %.1f%%; below loading %.1f%% (n=%d), above %.1f%% (n=%d)'%(len(ok),len(t),abs(ok['dev%']).mean(),ok['dev%'].mean(),abs(lo['dev%']).mean(),len(lo),abs(hi['dev%']).mean(),len(hi)))
ok=d.dropna(); big=ok[ok.dp_mbar_m>0.5]
print('excluding dp<0.5 mbar/m (digitising floor): mean|dev| %.1f%%, bias %.1f%%, n=%d'%(abs(big['dev%']).mean(),big['dev%'].mean(),len(big)))
print()
for lab,sel in [('below loading, dp>0.5',lambda t:(t.F<=t.F_load)&(t.dp_mbar_m>0.5)),('all answered, dp>0.5',lambda t:t.dp_mbar_m>0.5)]:
    t=d.dropna(); t=t[sel(t)]
    f=minimize_scalar(lambda c: np.sum(np.log(np.array([billet_dp(x.F,x.pressure_mbar,c)[0]/100 for x in t.itertuples()])/t.dp_mbar_m)**2),bounds=(0.05,1),method='bounded').x
    dv=np.array([100*(billet_dp(x.F,x.pressure_mbar,f)[0]/100-x.dp_mbar_m)/x.dp_mbar_m for x in t.itertuples()])
    print('Cp fitted on %s: %.3f (table 0.295) -> mean|dev| %.1f%%, bias %.1f%%, n=%d'%(lab,f,abs(dv).mean(),dv.mean(),len(t)))
    print('   devs:',np.round(dv,1))
K=1/(1+4/(a*ds)); print('K^2 =',round(K**2,3))
