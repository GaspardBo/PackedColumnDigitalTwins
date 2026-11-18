import os
import numpy as np, pandas as pd
from scipy.optimize import minimize_scalar
D=os.path.dirname(os.path.abspath(__file__)) + "/"
g=9.81
# ---- Fig 3.38: Mellapak 250Y hold-up, air/water 295 K, below loading
rhoL,muL=997.8,0.955e-3; a=250
h=pd.read_csv(D+'fig3_38_mellapak_holdup.csv'); uL=h.uL_1e3_m3_m2s/1e3; meas=h.holdup_1e2_m3_m3/1e2
def hold(Ch,uL,a=a):
    Re=uL*rhoL/(a*muL); Fr=uL**2*a/g
    ah=np.where(Re<5,Ch*Re**0.15*Fr**0.1,0.85*Ch*Re**0.25*Fr**0.1)
    return (12*muL*a**2*uL/(rhoL*g))**(1/3)*ah**(2/3), Re
p,Re=hold(0.554,uL)
dev=100*(p-meas)/meas
print('3.38 Re_L range',Re.min().round(1),Re.max().round(1))
print('3.38 Ch 0.554: mean|dev| %.1f%%  bias %.1f%%  max %.1f%%'%(abs(dev).mean(),dev.mean(),abs(dev).max()))
f=minimize_scalar(lambda c: np.sum(np.log(hold(c,uL)[0]/meas)**2),bounds=(0.1,2),method='bounded').x
dv=100*(hold(f,uL)[0]-meas)/meas
print('3.38 fitted Ch %.3f: mean|dev| %.1f%%  bias %.1f%%'%(f,abs(dv).mean(),dv.mean()))
print(pd.DataFrame({'uL_e3':uL*1e3,'meas_e2':meas*1e2,'billet_e2':(p*1e2).round(2),'dev%':dev.round(1)}).to_string(index=False))
# ---- Fig 3.39: flood, air/water 293 K
rhoL,muL,muV=998.2,1.002e-3,1.81e-5
tab={'Mellapak 250Y':(250,0.970,2.464),'Ralupak 250YC':(250,0.945,2.558),'Montz B1-200':(200,0.979,2.339),
     'Montz B1-300 (barred square)':(300,0.930,2.464),'Montz B1-300 (square)':(300,0.930,2.464)}
fl=pd.read_csv(D+'fig3_39_flood_structured.csv')
def yfl(X,a,eps,CFl):
    psi=X/100; n=np.where(psi<=0.4,-0.194,-0.708)
    xi=g/(CFl**2*(psi*(muL/muV)**0.2)**(2*n))
    hF=0.3741*eps   # water is Billet's reference liquid, viscosity factor = 1
    return np.sqrt(2*g/xi)*(eps-hF)**1.5/np.sqrt(eps)*np.sqrt(hF/a)*100
print()
rows=[]
for pk,(a_,e_,C) in tab.items():
    s=fl[fl.packing==pk]; X=s.flow_param_1e2.values; m=s.flood_factor_1e2_m_s.values
    d=100*(yfl(X,a_,e_,C)-m)/m
    Cf=minimize_scalar(lambda c: np.sum(np.log(yfl(X,a_,e_,c)/m)**2),bounds=(0.5,6),method='bounded').x
    d2=100*(yfl(X,a_,e_,Cf)-m)/m
    rows.append([pk,len(s),C,round(abs(d).mean(),1),round(d.mean(),1),round(Cf,3),round(abs(d2).mean(),1)])
print(pd.DataFrame(rows,columns=['packing','n','CFl_table','mean|dev|%','bias%','CFl_fit','mean|dev|% fit']).to_string(index=False))
# ---- Fig 4.7
r=pd.read_csv(D+'fig4_7_hiflow_ratio.csv')
for s_,t in r.groupby('series'): print('4.7',s_,'ratio range %.2f-%.2f mean %.2f'%(t.dp_ratio.min(),t.dp_ratio.max(),t.dp_ratio.mean()))
