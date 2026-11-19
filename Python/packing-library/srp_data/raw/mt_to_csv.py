# Mass-transfer tables to CSV. Tsai B.8 parsed in text-stream order (14 values per row);
# Wang D.1-D.3 parsed from position-rebuilt rows (extract_mt_rows.py output).
# Usage: python -I mt_to_csv.py <pymupdf dir> <tsai pdf> <wang rows txt> <out dir>
import sys, re, csv; sys.path.insert(0, sys.argv[1]); import pymupdf
num = re.compile(r'^-?\d+(\.\d+)?$'); out = sys.argv[4]
# ---- Tsai Table B.8, PDF pp. 341-355
d = pymupdf.open(sys.argv[2]); runs = []; head = []; vals = []; inhead = False
for i in range(340, 355):
    lines = [l.strip() for l in d[i].get_text().split('\n') if l.strip()]
    lines = lines[1:]  # page number
    for l in lines:
        if num.match(l):
            if not inhead: vals.append((l, i+1))
        else:
            if l.startswith(('Table', 'L', 'uG', 'Tcorr', '∆P', 'kOH', 'DCO2', 'HCO2', '[OH', 'CO2', 'af', 'ReL', 'WeL', 'FrL', '(')) and not inhead:
                continue
            if not inhead:
                if head or vals: runs.append((head, vals))
                head, vals, inhead = [], [], True
            head.append(l)
            if re.match(r'^\d+(\.\d+)? m$', l): inhead = False
runs.append((head, vals))
cols = ['L_m3_m2h','uG_ms','Tcorr_C','dPdz_Pa_m','kOH_1e-3','DCO2_1e9','HCO2_1e-5','OH_kmol_m3','CO2_in_ppm','CO2_out_ppm','af','ReL','WeL_1e4','FrL']
rows = []
for h, v in runs:
    if not h: continue
    if len(v) % 14: print('TSAI MISALIGNED', h, len(v)); continue
    for k in range(0, len(v), 14):
        rows.append([h[0], ' | '.join(h), v[k][1]] + [x for x, _ in v[k:k+14]])
with open(f'{out}/tsai2010_tableB8_masstransfer.csv', 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f); w.writerow(['packing','run_header','page'] + cols); w.writerows(rows)
print('tsai rows', len(rows), 'runs', len([r for r in runs if r[0]]))
# ---- Wang D.1 (10 cols), D.2 (9), D.3 (9)
tab, head, out_rows = None, [], {'D1': [], 'D2': [], 'D3': []}
for l in open(sys.argv[3], encoding='utf-8'):
    p, t = l.rstrip('\n').split('\t'); tok = t.split()
    if not tok: continue
    if 'Table D.1' in t: tab = 'D1'
    elif 'Table D.2' in t: tab = 'D2'
    elif 'Table D.3' in t: tab = 'D3'
    if all(num.match(x) for x in tok):
        if len(tok) == 1: continue  # page number
        n = 10 if tab == 'D1' else 9
        if len(tok) != n: print('WANG BAD', tab, p, t); continue
        out_rows[tab].append([head[0] if head else '', ' | '.join(head), p[1:]] + tok)
        newhead = True
    elif tab and re.match(r'^[A-Z][A-Za-z0-9#.]*$', tok[0]) and not t.startswith(('L ', 'Table', 'Height')) and tok[0] not in ('L','uG','Tcorr','Water','Air','NTU','HTU','ae','af'):
        if 'newhead' in dir() and newhead: head = []; newhead = False
        head.append(t)
    elif t.startswith('Height'): head.append(t)
hdr = {'D1': ['L_m3_m2h','uG_ms','Tcorr_C','kOH_1e-3','DCO2_1e9','HCO2_1e-5','OH_kmol_m3','CO2_in_ppm','CO2_out_ppm','af'],
       'D2': ['L_m3_m2h','uG_ms','Twater_in_C','tol_in_ppm','tol_out_ppm','NTU','HTU_m','ae_m2_m3','kL_1e5_ms'],
       'D3': ['L_m3_m2h','uG_ms','Tair_in_C','SO2_in_ppm','SO2_out_ppb','NTU','HTU_x10_m','ae_m2_m3','kG_1e2_ms']}
name = {'D1': 'wang2015_tableD1_area', 'D2': 'wang2015_tableD2_kL', 'D3': 'wang2015_tableD3_kG'}
for k in out_rows:
    with open(f'{out}/{name[k]}.csv', 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f); w.writerow(['packing','run_header','page'] + hdr[k]); w.writerows(out_rows[k])
    print(k, len(out_rows[k]))
