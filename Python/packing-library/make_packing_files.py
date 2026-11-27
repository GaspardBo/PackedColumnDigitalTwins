# make_packing_files.py: build the packing library the C# DLL reads (PackingLibrary/PackingManager.cs)
# from billet_packings.csv, and train the predictive (level 2) models where training data exists.
#
# run:  python make_packing_files.py [library folder] [--data <manifest csv>] [--no-train] [--train-files <folder>]
#
#   library folder   default: the repository's Packings folder (../../Packings from this script)
#   --data           training manifest, default training_data/training_data.csv (columns below)
#   --no-train       write the library without training any models
#   --train-files    also write the json_format.md packing files (the starting files train.py reads) to this folder
#
# layout written (the same as the existing Packings folder):
#   <library folder>/library.json                      library info, units, families, packings index
#   <library folder>/<family folder>/<ID>/<ID>.json    one file per packing
#   <library folder>/<family folder>/<ID>/<ID>_<output>.onnx   predictive models, next to the packing file
# "predictiveModel" in a packing file lists its models by output (dp, holdup, kLa, kGa), with paths relative to
# the library folder; "available" is true when at least one model was trained. Any .onnx file already in a
# packing folder is removed first, so the folder only holds the models listed in its packing file.
#
# dumped (random) and regular (arranged rings, structured packings) entries are all written; identity.arrangement
# says which. a value the database does not give is written as null (packing density of most structured packings,
# nominal size of structured packings, whose size is a product code such as 250Y).
#
# training manifest: one row per packing that has measured data (user_data_format.md)
#   packing_id   library id, e.g. RSR_0_7_M
#   data_file    data csv, relative to the manifest's folder
#   dc_m         column diameter of the measurements, m
# training uses train.py: the Billet-Schultes constants the data supports are refit (C_P0, C_h, C_Fl from
# hydraulic data, C_L, C_V from mass-transfer data; C_S is never fitted) and written to the packing file, with
# the refit noted in billetSchultes.source; the level 2 models are trained on top of those refit constants.
# a packing without C_S cannot be trained.
#
# merging (unchanged): the database has one row per source, so a packing can have several rows. they are merged:
#   - the hydraulic set (C_S, C_Fl, C_h, C_P0) always comes from one row, and C_L, C_V from one row
#   - published rows (BS1999, B1995, Rejl2015) first, then our fits; among our fits, the main row
#     (size with no author tag, the one with more data); a row flagged 'data check pending' goes last
#   - a mass-transfer pair joins a hydraulic set only when both rows have the same a and eps;
#     rows with different a or eps are different samples and get their own files (id suffix _2, _3, ...)

import argparse
import glob
import json
import os
import re
import pandas as pd
import packlib

HERE = os.path.dirname(os.path.abspath(__file__))

HYD = ['C_S', 'C_Fl', 'C_h', 'C_P0']
MT = ['C_L', 'C_V']
PUBLISHED = ['BS1999', 'B1995', 'Rejl2015']

# database constant name -> name in the library packing file
CONSTANT_NAMES = {'C_S': 'CS', 'C_Fl': 'CFI', 'C_h': 'Ch', 'C_P0': 'CP0', 'C_L': 'CL', 'C_V': 'CV'}
SOURCE_NAMES = {'BS1999': 'Billet & Schultes (1999)', 'B1995': 'Billet (1995)',
                'Rejl2015': 'Rejl et al. (2015)', 'ThisWork': 'This work (fitted)'}

# database packing name -> family id, family name, folder, id prefix, manufacturer, name used in packing names
FAMILIES = {
    'Raschig Super-Ring': ('RASCHIG_SUPER_RING', 'Raschig Super-Ring', 'Raschig Super Ring', 'RSR', 'Raschig', 'Raschig Super-Ring'),
    'Ralu Flow': ('RALU_FLOW', 'Ralu Flow', 'Ralu Flow', 'RF', 'Raschig', 'Raschig Ralu Flow'),
    'Pall ring': ('PALL_RING', 'Pall Ring', 'Pall Ring', 'PR', None, 'Pall Ring'),
    'Ralu ring': ('RALU_RING', 'Ralu Ring', 'Ralu Ring', 'RR', 'Raschig', 'Raschig Ralu Ring'),
    'NOR PAC ring': ('NOR_PAC_RING', 'NOR PAC Ring', 'NOR PAC Ring', 'NP', None, 'NOR PAC Ring'),
    'Hiflow ring': ('HIFLOW_RING', 'Hiflow Ring', 'Hiflow Ring', 'HR', 'Rauschert', 'Rauschert Hiflow Ring'),
    'Glitsch ring': ('GLITSCH_RING', 'Glitsch Ring', 'Glitsch Ring', 'GR', 'Glitsch', 'Glitsch Ring'),
    'Glitsch CMR ring': ('GLITSCH_CMR_RING', 'Glitsch CMR Ring', 'Glitsch CMR Ring', 'CMR', 'Glitsch', 'Glitsch CMR Ring'),
    'TOP Pak ring': ('TOP_PAK_RING', 'TOP Pak Ring', 'TOP Pak Ring', 'TP', None, 'TOP Pak Ring'),
    'Raschig ring': ('RASCHIG_RING', 'Raschig Ring', 'Raschig Ring', 'RA', 'Raschig', 'Raschig Raschig Ring'),
    'VSP ring': ('VSP_RING', 'VSP Ring', 'VSP Ring', 'VSP', None, 'VSP Ring'),
    'Envi Pac ring': ('ENVI_PAC_RING', 'Envi Pac Ring', 'Envi Pac Ring', 'EP', 'Envimac', 'Envimac Envi Pac Ring'),
    'Bialecki ring': ('BIALECKI_RING', 'Bialecki Ring', 'Bialecki Ring', 'BR', None, 'Bialecki Ring'),
    'Tellerette': ('TELLERETTE', 'Tellerette', 'Tellerette', 'TE', None, 'Tellerette'),
    'Hackette': ('HACKETTE', 'Hackette', 'Hackette', 'HA', None, 'Hackette'),
    'Raflux ring': ('RAFLUX_RING', 'Raflux Ring', 'Raflux Ring', 'RX', None, 'Raflux Ring'),
    'Berl saddle': ('BERL_SADDLE', 'Berl Saddle', 'Berl Saddle', 'BS', None, 'Berl Saddle'),
    'DIN-PAK': ('DIN_PAK', 'DIN-PAK', 'DIN PAK', 'DP', None, 'DIN-PAK'),
    'INTALOX': ('INTALOX', 'Intalox', 'Intalox', 'IX', None, 'Intalox'),
    'Intalox saddle': ('INTALOX_SADDLE', 'Intalox Saddle', 'Intalox Saddle', 'IS', None, 'Intalox Saddle'),
    'Hiflow saddle': ('HIFLOW_SADDLE', 'Hiflow Saddle', 'Hiflow Saddle', 'HS', 'Rauschert', 'Rauschert Hiflow Saddle'),
    'Sphere': ('SPHERE', 'Sphere', 'Sphere', 'SP', None, 'Sphere'),
    # structured packings
    'Ralu pak': ('RALU_PAK', 'Ralu Pak', 'Ralu Pak', 'RP', 'Raschig', 'Raschig Ralu Pak'),
    'Mellapak': ('MELLAPAK', 'Mellapak', 'Mellapak', 'MP', 'Sulzer', 'Sulzer Mellapak'),
    'MellapakPlus': ('MELLAPAK_PLUS', 'MellapakPlus', 'MellapakPlus', 'MPP', 'Sulzer', 'Sulzer MellapakPlus'),
    'Mellapak Grid': ('MELLAPAK_GRID', 'Mellapak Grid', 'Mellapak Grid', 'MPG', 'Sulzer', 'Sulzer Mellapak Grid'),
    'Gempack': ('GEMPACK', 'Gempack', 'Gempack', 'GP', None, 'Gempack'),
    'Impulse packing': ('IMPULSE_PACKING', 'Impulse Packing', 'Impulse Packing', 'IP', None, 'Impulse Packing'),
    'Montz packing': ('MONTZ_PAK', 'Montz-Pak', 'Montz Pak', 'MZ', 'Montz', 'Montz-Pak'),
    'Euroform': ('EUROFORM', 'Euroform', 'Euroform', 'EF', None, 'Euroform'),
    'Raschig Super-Pak': ('RASCHIG_SUPER_PAK', 'Raschig Super-Pak', 'Raschig Super Pak', 'RSP', 'Raschig', 'Raschig Super-Pak'),
    'GT-PAK': ('GT_PAK', 'GT-PAK', 'GT PAK', 'GTP', 'GTC Technology', 'GT-PAK'),
    'GT-OPTIMPak': ('GT_OPTIMPAK', 'GT-OPTIMPak', 'GT OPTIMPak', 'GTO', 'GTC Technology', 'GT-OPTIMPak'),
    'Flexipac': ('FLEXIPAC', 'Flexipac', 'Flexipac', 'FP', 'Koch-Glitsch', 'Koch-Glitsch Flexipac'),
    'Flexipac HC': ('FLEXIPAC_HC', 'Flexipac HC', 'Flexipac HC', 'FPHC', 'Koch-Glitsch', 'Koch-Glitsch Flexipac HC'),
    'RVT Mellapak-type (RMP N)': ('RVT_RMP_N', 'RVT RMP N', 'RVT RMP N', 'RMPN', 'RVT Process Equipment', 'RVT RMP N'),
    'RVT high-performance (RMP SP)': ('RVT_RMP_SP', 'RVT RMP SP', 'RVT RMP SP', 'RMPSP', 'RVT Process Equipment', 'RVT RMP SP'),
    'HFP (hybrid)': ('HFP', 'HFP (hybrid)', 'HFP', 'HFP', None, 'HFP (hybrid)'),
    'Prototype 500': ('PROTOTYPE', 'Prototype', 'Prototype', 'PROTO500', None, 'Prototype 500'),
    'Prototype A': ('PROTOTYPE', 'Prototype', 'Prototype', 'PROTOA', None, 'Prototype A'),
    'Prototype B': ('PROTOTYPE', 'Prototype', 'Prototype', 'PROTOB', None, 'Prototype B'),
}
# families whose packings are structured sheets or grids: their size is a product code, not a nominal size in mm.
# other families (rings, saddles) can also appear as 'regular' (arranged) in the database; those get an ARR id
# suffix and '(arranged)' in their name, so they do not clash with the dumped packing of the same size
STRUCTURED_FAMILIES = {'Ralu pak', 'Mellapak', 'MellapakPlus', 'Mellapak Grid', 'Gempack', 'Impulse packing',
                       'Montz packing', 'Euroform', 'Raschig Super-Pak', 'GT-PAK', 'GT-OPTIMPak', 'Flexipac',
                       'Flexipac HC', 'RVT Mellapak-type (RMP N)', 'RVT high-performance (RMP SP)', 'HFP (hybrid)',
                       'Prototype 500', 'Prototype A', 'Prototype B'}
MATERIAL_CODES = {'Metal': 'M', 'Plastic': 'P', 'Ceramic': 'C', 'Alu': 'A', 'Carbon': 'CB', 'Glass': 'G'}

LIBRARY_INFO = {
    'id': 'DEFAULT-PACKING-LIBRARY',
    'name': 'Random and Structured Packings Library',
    'version': '1.2.0',
    'schemaVersion': '1.0',
    'description': 'Packing data for packed column calculations',
    'author': 'Amogh Krishnanand Atreya, Gaspard Bouchard',
}
UNITS = {'specificSurfaceArea': 'm2/m3', 'packing unit density': '1/m3', 'pressure': 'Pa',
         'pressureDropPerHeight': 'Pa/m', 'hetp': 'm'}
MODEL_REFERENCE = {'rho_W_kg_m3': 998.21, 'mu_W_Pa_s': 1.0014e-3, 'note': 'reference water for the flood-point hold-up'}
ORDINALS = {2: '2nd', 3: '3rd'}


# ---- merging (one entry per packing sample) ----------------------------------------

def load_database():
    db = pd.read_csv(os.path.join(HERE, 'billet_packings.csv'), dtype={'size': str})
    # a size like '250Y (Wang)' or '250Y (Baumfrisch 2022)' is the same packing as '250Y' from another source
    db['base_size'] = db['size'].str.replace(r'\s*\((Wang|Tsai|Song|Baumfrisch|Erasmus)[^)]*\)', '', regex=True)
    db['author_tag'] = db['base_size'] != db['size']
    db['has_hyd'] = db[HYD].notna().all(axis=1) & ~db['note'].astype(str).str.contains('not dropdown-ready')
    db['has_mt'] = db[MT].notna().any(axis=1)
    return db


def rank(row):
    # lower is better: published first (in list order), then our main row, then our other rows;
    # a row whose note says 'data check pending' goes last, behind any other row
    flagged = 10 if 'data check pending' in str(row['note']) else 0
    if row['source'] in PUBLISHED:
        return flagged + PUBLISHED.index(row['source'])
    return flagged + len(PUBLISHED) + (1 if row['author_tag'] else 0)


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


def merged_entries(db):
    """Yields one merged entry per packing sample, in database order."""
    for key, group in db.groupby(['packing', 'arrangement', 'material', 'base_size'], sort=False):
        # rows with different a or eps are different samples: one entry each
        for sample, (geom, rows) in enumerate(group.groupby(['a_m2_m3', 'eps'], sort=False), start=1):
            rows = rows.assign(r=rows.apply(rank, axis=1)).sort_values('r', kind='stable')
            hyd = rows[rows['has_hyd']]
            hyd_row = None if len(hyd) == 0 else hyd.iloc[0]
            mt = rows[rows['has_mt']]
            mt_row = None if len(mt) == 0 else mt.iloc[0]
            # a row that is neither (e.g. withdrawn, or only part of the hydraulic set) still gets an entry
            first = rows.iloc[0] if hyd_row is None else hyd_row
            constants = take(first, HYD)
            constants.update(take(mt_row, MT))
            packing, arrangement, material, size = key
            yield {'packing': packing, 'arrangement': arrangement, 'material': material, 'size': size,
                   'sample': sample, 'a': float(geom[0]), 'eps': float(geom[1]),
                   'N': None if pd.isna(first['N_per_m3']) else float(first['N_per_m3']),
                   'constants': constants, 'hyd_row': hyd_row, 'mt_row': mt_row, 'first': first, 'rows': rows}


# ---- library packing files (C# format) ---------------------------------------------

def family_of(packing):
    if packing in FAMILIES:
        return FAMILIES[packing]
    # a family not in the table: derive everything from the database name
    print(f"note: family '{packing}' is not in FAMILIES; id and names derived from the database name")
    words = re.findall(r'[A-Za-z0-9]+', packing)
    return ('_'.join(w.upper() for w in words), packing, packing, ''.join(w[0] for w in words).upper(), None, packing)


def parse_size(size):
    """'1.5in T' -> (id part '1_5IN', name part '1.5"', nominal size mm 38.1, suffix id 'T', suffix name 'T')."""
    m = re.match(r'^\s*(\d+(?:\.\d+)?)\s*(in)?\b\s*(.*)$', size)
    if m is None:
        token = re.sub(r'[^A-Za-z0-9]+', '_', size).strip('_').upper()
        return token, size, 0.0, '', ''
    number, inch, rest = m.groups()
    id_part = number.replace('.', '_') + ('IN' if inch else '')
    name_part = number + ('"' if inch else '')
    nominal = round(float(number) * (25.4 if inch else 1.0), 6)
    rest = rest.lstrip(', ').strip()
    fn = re.match(r'^\((\d+)\)$', rest)
    if fn:  # NOR PAC 25 (6) / (10): footnote numbers in Billet & Schultes (1999)
        return id_part, name_part, nominal, 'fn' + fn.group(1), f'(fn {fn.group(1)})'
    return id_part, name_part, nominal, re.sub(r'[^A-Za-z0-9]+', '_', rest).strip('_').upper(), rest


def source_text(entry):
    hyd_src = entry['first']['source'] if any(entry['constants'][k] is not None for k in HYD) else None
    mt_src = entry['mt_row']['source'] if entry['mt_row'] is not None else None
    names = [SOURCE_NAMES.get(s, s) for s in [hyd_src, mt_src] if s is not None]
    if not names:
        return None
    if len(names) == 1 or names[0] == names[1]:
        return names[0]
    return f'{names[0]} (hydraulics); {names[1]} (mass transfer)'


def number(value):
    # whole numbers are written without a decimal point (6242, not 6242.0), as in the hand-written files
    return int(value) if value is not None and float(value).is_integer() else value


def quantity(value, unit):
    return {'value': number(value), 'unit': unit}


def library_packing(entry, pid, name, family, nominal):
    c = entry['constants']
    constants = {CONSTANT_NAMES[k]: (None if c[k] is None else c[k]['value']) for k in HYD + MT}
    return {
        'id': pid,
        'identity': {'name': name, 'manufacturer': family[4], 'family': family[0],
                     'material': entry['material'], 'arrangement': entry['arrangement'],
                     'nominalSize': number(nominal)},
        'physicalProperties': {'packingDensity': quantity(entry['N'], '1/m3'),
                               'specificSurfaceArea': quantity(entry['a'], 'm2/m3'),
                               'voidFraction': quantity(entry['eps'], '-')},
        'billetSchultes': {'available': any(v is not None for v in constants.values()),
                           'constants': constants, 'source': source_text(entry)},
        'predictiveModel': {'available': False, 'models': {}},
    }


def train_file(entry, pid):
    # the packing file format of json_format.md, which train.py starts from
    rows, first, hyd_row, mt_row = entry['rows'], entry['first'], entry['hyd_row'], entry['mt_row']
    return {
        'format_version': packlib.FORMAT_VERSION,
        'file_kind': 'library',
        'packing': {'id': pid, 'name': entry['packing'], 'size': entry['size'], 'material': entry['material'],
                    'arrangement': entry['arrangement'], 'a_m2_m3': entry['a'], 'eps': entry['eps'],
                    'N_per_m3': entry['N']},
        'constants': entry['constants'],
        'dropdown_ready': hyd_row is not None,
        'model_reference': dict(MODEL_REFERENCE),
        'level2': {'dp': None, 'holdup': None, 'kLa': None, 'kGa': None},
        'source_detail': {'hydraulics': detail(first),
                          'mass_transfer': detail(mt_row),
                          'other_rows': [f"{r['source']} {r['size']}" for _, r in rows.iterrows()
                                         if r.name != first.name
                                         and not (mt_row is not None and r.name == mt_row.name)]},
    }


def build_library(db):
    """Returns (families in order, list of (index entry, library packing dict, train-format dict, folder))."""
    families, packings, used = [], [], {}
    for entry in merged_entries(db):
        family = family_of(entry['packing'])
        id_part, name_part, nominal, suffix_id, suffix_name = parse_size(entry['size'])
        code = MATERIAL_CODES.get(entry['material'], entry['material'][:1].upper())
        structured = entry['packing'] in STRUCTURED_FAMILIES
        arranged = entry['arrangement'] == 'regular' and not structured
        pid = '_'.join(p for p in [family[3], id_part, code, suffix_id, 'ARR' if arranged else ''] if p)
        if structured:  # sizes are product codes: 'Sulzer Mellapak Metal 250Y', no nominal size
            name = f"{family[5]} {entry['material']} {entry['size']}"
            nominal = None
        else:
            name = f"{family[5]} {entry['material']} No. {name_part}" + (f' {suffix_name}' if suffix_name else '')
            if arranged:
                name += ' (arranged)'
        used[pid] = used.get(pid, 0) + 1
        if used[pid] > 1:
            name += f" ({ORDINALS.get(used[pid], str(used[pid]) + 'th')} entry)"
            pid = f'{pid}_{used[pid]}'
        if family[0] not in [f['id'] for f in families]:
            families.append({'id': family[0], 'name': family[1]})
        folder = f'{family[2]}/{pid}'
        index = {'id': pid, 'family': family[0], 'name': name, 'path': folder}
        packings.append((index, library_packing(entry, pid, name, family, nominal), train_file(entry, pid), folder))
    return families, packings


# ---- training -------------------------------------------------------------------------

def read_manifest(path):
    if not path or not os.path.isfile(path):
        return {}
    m = pd.read_csv(path, comment='#', skipinitialspace=True)
    base = os.path.dirname(os.path.abspath(path))
    return {str(r['packing_id']).strip(): (os.path.normpath(os.path.join(base, str(r['data_file']).strip())), float(r['dc_m']))
            for _, r in m.iterrows()}


def train_packing(pk_train, data_file, dc, folder, pid):
    """Refit the constants and train level 2 (train.py). Returns (trained packing dict, onnx files, report)."""
    import train  # imports scikit-learn and onnx only when there is something to train
    pk, report = train.train(pk_train, pd.read_csv(data_file), dc, keep_constants=False,
                             data_file=os.path.relpath(data_file, HERE), log=lambda m: print(f'  {pid}: {m}'))
    files = train.write_onnx(pk['level2'], folder, pid, pid)
    return pk, files, report


def apply_refit(lib_pk, pk, data_file):
    """Copy the constants train.py refitted into the library packing file; returns their names."""
    refit = [k for k in HYD + MT
             if pk['constants'].get(k) and str(pk['constants'][k].get('status', '')).startswith('fitted on user data')]
    bs = lib_pk['billetSchultes']
    for k in refit:
        bs['constants'][CONSTANT_NAMES[k]] = pk['constants'][k]['value']
    if refit:
        bs['available'] = True
        note = (f"{', '.join(CONSTANT_NAMES[k] for k in refit)} refit on {os.path.basename(data_file)} "
                f"({pk['trained']['date']})")
        bs['source'] = f"{bs['source']}; {note}" if bs['source'] else note
    return refit


# ---- main -------------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description='Build the packing library and train its predictive models.')
    ap.add_argument('library', nargs='?', default=os.path.normpath(os.path.join(HERE, '..', '..', 'Packings')),
                    help='library folder (default: the repository Packings folder)')
    ap.add_argument('--data', default=os.path.join(HERE, 'training_data', 'training_data.csv'),
                    help='training manifest csv (packing_id, data_file, dc_m)')
    ap.add_argument('--no-train', action='store_true', help='do not train predictive models')
    ap.add_argument('--train-files', help='also write json_format.md packing files (train.py starting files) here')
    args = ap.parse_args()

    db = load_database()
    families, packings = build_library(db)
    manifest = {} if args.no_train else read_manifest(args.data)
    ids = {index['id'] for index, *_ in packings}
    for pid in manifest:
        if pid not in ids:
            print(f'warning: training manifest names {pid}, which is not in the library; skipped')

    root = os.path.abspath(args.library)
    os.makedirs(root, exist_ok=True)
    if args.train_files:
        os.makedirs(args.train_files, exist_ok=True)

    trained = 0
    for index, lib_pk, pk_train, folder in packings:
        pid = index['id']
        full = os.path.join(root, *folder.split('/'))
        os.makedirs(full, exist_ok=True)
        for old in glob.glob(os.path.join(full, '*.onnx')):
            os.remove(old)

        if pid in manifest:
            data_file, dc = manifest[pid]
            print(f'training {pid} on {os.path.relpath(data_file, HERE)} (dc = {dc} m) ...')
            try:
                pk, files, report = train_packing(pk_train, data_file, dc, full, pid)
            except (ValueError, OSError) as ex:
                print(f'  {pid}: not trained: {ex}')
                pk, files, report = None, {}, None
            if report is not None:
                print('  ' + report.round(1).to_string(index=False).replace('\n', '\n  '))
            if pk is not None:
                refit = apply_refit(lib_pk, pk, data_file)
                print(f"  {pid}: constants refit: {', '.join(refit) if refit else 'none'}")
            if files:
                lib_pk['predictiveModel'] = {'available': True,
                                             'models': {out: f'{folder}/{name}' for out, name in files.items()}}
                trained += 1
                print(f"  {pid}: models {', '.join(files)}")
            else:
                print(f'  {pid}: no level 2 model could be trained from this data')

        with open(os.path.join(full, pid + '.json'), 'w', encoding='utf-8') as f:
            json.dump(lib_pk, f, indent=4)
        if args.train_files:
            with open(os.path.join(args.train_files, pid + '.json'), 'w', encoding='utf-8') as f:
                json.dump(pk_train, f, indent=1)

    library = {'library': LIBRARY_INFO, 'units': UNITS, 'families': families,
               'packings': [index for index, *_ in packings]}
    with open(os.path.join(root, 'library.json'), 'w', encoding='utf-8') as f:
        json.dump(library, f, indent=2)

    structured = sum(1 for _, lib_pk, _, _ in packings if lib_pk['identity']['arrangement'] == 'regular')
    print(f'{len(packings)} packings ({structured} regular) in {len(families)} families written to {root} '
          f'from {len(db)} database rows; predictive models for {trained} packing(s)')


if __name__ == '__main__':
    main()
