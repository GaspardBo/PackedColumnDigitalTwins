# Calm-zone wet/dry dP ratio, measured vs Billet's multiplier using measured hold-up.
import csv, math, numpy as np, sys
from collections import defaultdict
D = sys.argv[1]
a = {'M250Y':250,'M500Y':500,'M250X':250,'MP252Y':250,'M250YS':250,'M125Y':125,'M2Y':205,'P500':500,
     'MP2X':205,'MP250Y':250,'MP250X':250,'RSP250Y':250,'RSP200X':200,'GTC350Y':350,'GTC350Z':350,
     'A350Y':350,'B350X':350,'GTC500Y':500,'RSR#0.3':315,'RSR#0.7':180}
eps, nu = 0.97, 0.89e-6           # hypothesis: same eps for all; water at about 25 C
def f(x):
    try: return float(x)
    except: return None
rows = []
for fn, hk, sc in [('tsai2010_tableB9.csv','holdup_pct',0.01), ('wang2015_tableC1.csv','holdup_frac',1)]:
    for r in csv.DictReader(open(f'{D}/{fn}', encoding='utf-8')):
        if 'Baseline' not in r['run_header'] and fn.startswith('tsai'): continue
        if r['packing'] not in a: continue
        rows.append((r['packing'], f(r['L_m3_m2h']), f(r['FG_Pa05']), f(r['dPdz_Pa_m']),
                     None if f(r[hk]) is None else f(r[hk])*sc))
out = defaultdict(list)
for p in a:
    dry = [(F, dp) for q, L, F, dp, h in rows if q == p and L == 0 and dp and dp > 0]
    if len(dry) < 4: continue
    n, k = np.polyfit(np.log([x for x, _ in dry]), np.log([y for _, y in dry]), 1)
    Fmin = min(x for x, _ in dry)
    for q, L, F, dp, h in rows:
        if q != p or not L or not h or h <= 0 or F > 1.2 or F < Fmin: continue
        Lb = min([6, 12, 24, 37, 49, 61, 73], key=lambda v: abs(v - L))
        meas = dp / math.exp(k + n*math.log(F))
        ReL = (L/3600) / (a[p]*nu)
        bil = math.exp(ReL/200) * (eps/(eps-h))**1.5
        out[(p, Lb)].append((meas, bil))
print(f"{'packing':9s} {'uL':>4s} {'n':>3s} {'measured':>9s} {'Billet':>7s}")
for (p, Lb), v in sorted(out.items(), key=lambda x: (x[0][0], x[0][1])):
    if Lb not in (12, 24, 49): continue
    m = np.mean([x for x, _ in v]); b = np.mean([y for _, y in v])
    print(f'{p:9s} {Lb:4d} {len(v):3d} {m:9.2f} {b:7.2f}')
