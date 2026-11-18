import re,pandas as pd,numpy as np
num=re.compile(r'^-?\d+(\.\d+)?$|^N$')
def build(fn,cols,fix=None):
    out=[];hdr=None;skipped=0
    for line in open(fn,encoding='utf-8'):
        pg,txt=line.rstrip('\n').split('\t',1); t=txt.split()
        if t and all(num.match(x) for x in t):
            if fix and len(t)!=len(cols): t=fix(t)
            if t and len(t)==len(cols) and hdr: out.append([hdr,pg]+t)
            elif len(t or [])>2: skipped+=1
        elif 'SRP' in txt: hdr=txt
    d=pd.DataFrame(out,columns=['run_header','page']+cols)
    print(fn,'rows',len(d),'skipped',skipped); return d
TC=['L_m3_m2h','FG_Pa05','Tair_in_C','Tliq_in_C','Tair_out_C','dPdz_Pa_m','holdup_pct','ReL','WeL_1e4','FrL']
def tfix(t):
    if len(t)==8 and t[0]=='0': return ['0',t[1],'','','',t[3],'0','0','0','0']   # dry rows, two temperatures blank (p359)
    if len(t)==9: return t[:6]+['']+t[6:]                                       # M500Y SRP 0625: no hold-up column (p363)
    return None                                                                  # p372: two rows without hold-up, dropped
w=build('wang_rows.txt',['L_m3_m2h','FG_Pa05','Tair_in_C','Tliq_in_C','Tair_out_C','dPdz_Pa_m','holdup_frac','ReL'])
t=build('tsai_rows.txt',TC,tfix)
for d in (w,t):
    d.insert(0,'packing',d.run_header.str.split().str[0])
w.to_csv('wang2015_tableC1.csv',index=False); t.to_csv('tsai2010_tableB9.csv',index=False)
print(t[t.run_header.str.contains('0715|0625')].groupby('run_header').size())
# quick sanity: dry dP vs FG for M250Y in both sources at F~1.8
for n,d,c in [('tsai',t,'M250Y'),('wang',w,'MP250Y')]:
    x=d[(d.packing==c)&(d.L_m3_m2h.astype(float)==0)]; x=x.astype({'FG_Pa05':float,'dPdz_Pa_m':float})
    print(n,c,'dry rows',len(x),'dP at F~1.8:',x.iloc[(x.FG_Pa05-1.8).abs().argsort()[:2]][['FG_Pa05','dPdz_Pa_m']].values.tolist())
