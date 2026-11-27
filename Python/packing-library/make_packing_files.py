# DRAFT for gaspard: one library packing file per real packing, format in json_format.md.
# he can use it, adapt it or replace it. library files carry no level 2.
# run: python make_packing_files.py [output folder, default packings]
#
# the database has one row per source, so a packing can have several rows. they are merged into one file:
#   - the hydraulic set (C_S, C_Fl, C_h, C_P0) always comes from one row, and C_L, C_V from one row
#   - published rows (BS1999, B1995, Rejl2015) first, then our fits; among our fits, the main row
#     (size with no author tag, the one with more data); a row flagged 'data check pending' goes last
#   - a mass-transfer pair joins a hydraulic set only when both rows have the same a and eps;
#     rows with different a or eps are different samples and get their own files

import json
import os
import re
import sys
import pandas as pd
import packlib

out_dir = sys.argv[1] if len(sys.argv) > 1 else 'packings'
os.makedirs(out_dir, exist_ok=True)
db = pd.read_csv(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'billet_packings.csv'), dtype={'size': str})

HYD = ['C_S', 'C_Fl', 'C_h', 'C_P0']
MT = ['C_L', 'C_V']
PUBLISHED = ['BS1999', 'B1995', 'Rejl2015']

# a size like '250Y (Wang)' or '250Y (Baumfrisch 2022)' is the same packing as '250Y' from another source
db['base_size'] = db['size'].str.replace(r'\s*\((Wang|Tsai|Song|Baumfrisch|Erasmus)[^)]*\)', '', regex=True)
db['author_tag'] = db['base_size'] != db['size']
db['has_hyd'] = db[HYD].notna().all(axis=1) & ~db['note'].astype(str).str.contains('not dropdown-ready')
db['has_mt'] = db[MT].notna().any(axis=1)


def rank(row):
    # lower is better: published first (in list order), then our main row, then our other rows;
    # a row whose note says 'data check pending' goes last, behind any other row
    flagged = 10 if 'data check pending' in str(row['note']) else 0
    if row['source'] in PUBLISHED:
        return flagged + PUBLISHED.index(row['source'])
    return flagged + len(PUBLISHED) + (1 if row['author_tag'] else 0)


def file_id(packing, material, size):
    text = '_'.join(str(p) for p in [packing, material, size] if pd.notna(p))
    return re.sub(r'-?_-?', '_', re.sub(r'[^a-z0-9._-]+', '-', text.lower())).strip('-')


def detail(row):
    if row is None:
        return None
    out = {'source': row['source'], 'size_as_listed': row['size']}
    for k in ['table', 'page', 'note', 'borrowed', 'fitted_on', 'version']:
        out[k] = None if pd.isna(row[k]) else str(row[k])
    return out


def take(row, names):
    if row is None:
        return {k: None for k in names}
    out = {}
    for name in names:
        if pd.isna(row[name]):
            out[name] = None
            continue
        borrowed = isinstance(row['borrowed'], str) and name in row['borrowed']
        status = 'borrowed' if borrowed else ('fitted' if row['source'] == 'ThisWork' else 'published')
        out[name] = {'value': float(row[name]), 'source': row['source'], 'status': status}
    return out


written = 0
used = {}
for key, group in db.groupby(['packing', 'arrangement', 'material', 'base_size'], sort=False):
    # rows with different a or eps are different samples: one entry each
    for geom, rows in group.groupby(['a_m2_m3', 'eps'], sort=False):
        rows = rows.assign(r=rows.apply(rank, axis=1)).sort_values('r', kind='stable')
        hyd = rows[rows['has_hyd']]
        hyd_row = None if len(hyd) == 0 else hyd.iloc[0]
        mt = rows[rows['has_mt']]
        mt_row = None if len(mt) == 0 else mt.iloc[0]
        # a row that is neither (e.g. withdrawn, or only part of the hydraulic set) still gets a file
        first = rows.iloc[0] if hyd_row is None else hyd_row
        constants = take(hyd_row if hyd_row is not None else first, HYD)
        constants.update(take(mt_row, MT))
        packing, arrangement, material, size = key
        pid = file_id(packing, material, size)
        used[pid] = used.get(pid, 0) + 1
        if used[pid] > 1:
            pid = f'{pid}-{used[pid]}'
        packing_file = {
            'format_version': packlib.FORMAT_VERSION,
            'file_kind': 'library',
            'packing': {'id': pid, 'name': packing, 'size': size, 'material': material, 'arrangement': arrangement,
                        'a_m2_m3': float(geom[0]), 'eps': float(geom[1]),
                        'N_per_m3': None if pd.isna(first['N_per_m3']) else float(first['N_per_m3'])},
            'constants': constants,
            'dropdown_ready': hyd_row is not None,
            'model_reference': {'rho_W_kg_m3': 998.21, 'mu_W_Pa_s': 1.0014e-3,
                                'note': 'reference water for the flood-point hold-up'},
            'level2': {'dp': None, 'holdup': None, 'kLa': None, 'kGa': None},
            'source_detail': {'hydraulics': detail(hyd_row) if hyd_row is not None else detail(first),
                              'mass_transfer': detail(mt_row),
                              'other_rows': [f"{r['source']} {r['size']}" for _, r in rows.iterrows()
                                             if r.name != first.name
                                             and not (mt_row is not None and r.name == mt_row.name)]},
        }
        json.dump(packing_file, open(os.path.join(out_dir, pid + '.json'), 'w'), indent=1)
        written += 1
print(written, 'packing files written to', out_dir, 'from', len(db), 'database rows')
