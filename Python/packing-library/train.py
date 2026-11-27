# train.py: fit level 1 constants and train level 2 on a user's own data, write a packing file (json_format.md)
#
# run:  python train.py <starting packing file> <data csv> <output packing file> --dc <column diameter, m>
#                       [--keep-constants] [--no-onnx]
#
# starting packing file: a library file for the packing (geometry, and the constants to start from or keep).
# data csv: one row per measured operating point, columns in user_data_format.md.
# what gets fitted depends on what the data has:
#   dry rows with dp            -> C_P0                       (fit_constants; the lowest-gas dry row is dropped)
#   wet rows with dp and holdup -> C_h, C_Fl and level 2 for dp and holdup
#   rows with kLa / kGa         -> C_L / C_V and level 2 for kLa / kGa
# C_S is never fitted; it comes from the starting file.
# --keep-constants: no level 1 fit; level 2 is trained on top of the starting file's constants
#   (used by make_packing_files.py, whose library files keep the database constants).
#
# every level 2 block is also written as an ONNX model next to the output file: <output stem>_<output>.onnx
# (dp, holdup, kLa, kGa; interface in onnx_export.py). --no-onnx skips this.
# train() can be imported: make_packing_files.py uses it to train the library's predictive models.

import json
import os
import sys
from datetime import date
import numpy as np
import pandas as pd
import warnings
warnings.filterwarnings('ignore')
import packlib

LEVEL2_OUTPUTS = ['dp', 'holdup', 'kLa', 'kGa']


def train(pk, data, dc, keep_constants=False, data_file=None, log=print):
    """Train on one packing. pk: packing file dict (json_format.md), data: DataFrame (user_data_format.md).
    Returns (packing file dict with constants and level2 filled in, report DataFrame)."""
    pk = json.loads(json.dumps(pk))  # work on a copy
    data = data.copy()
    ap = pk['packing']['a_m2_m3']
    eps = pk['packing']['eps']
    ref = pk['model_reference']
    start = {k: (None if v is None else v['value']) for k, v in pk['constants'].items()}
    if start['C_S'] is None:
        raise ValueError('the starting file has no C_S; the loading point needs one')
    for col in ['dp_Pa_m', 'holdup', 'kLa_1_s', 'kGa_1_s', 'D_L', 'D_V', 'curve']:
        if col not in data.columns:
            data[col] = np.nan

    C = dict(start)
    status = {k: (None if v is None else v['status']) for k, v in pk['constants'].items()}
    level2 = {k: None for k in LEVEL2_OUTPUTS}
    report = []
    today = date.today().isoformat()

    # ---- hydraulics ----
    data['system'] = 'user'
    data['F_Pa05'] = data['uG_ms'] * np.sqrt(data['rho_G'])
    data['dpdz_Pa_m'] = data['dp_Pa_m']
    dry = data[(data['uL_m3_m2h'] == 0) & data['dp_Pa_m'].notna()].copy()
    wet = data[(data['uL_m3_m2h'] > 0) & data['dp_Pa_m'].notna() & data['holdup'].notna()].copy()
    if len(dry) > 2:
        dry = dry.drop(dry['uG_ms'].idxmin())

    if len(wet) > 0 and all(start[k] is not None for k in ['C_Fl', 'C_h', 'C_P0']):
        if wet['curve'].isna().all():
            wet['curve'] = wet['uL_m3_m2h'].round(0)
        wet['curve_id'] = 'user ' + wet['curve'].astype(str)
        C0 = {'Cp': start['C_P0'], 'Clp': start['C_S'], 'Cfp': start['C_Fl'], 'Ch': start['C_h']}
        s = packlib.make_settings(ap=ap, eps=eps, dc=dc, rho_G=wet['rho_G'].mean(), mu_G=wet['mu_G'].mean(),
                                  props={'user': (wet['rho_L'].mean(), wet['mu_L'].mean())}, C0=C0, dry=dry,
                                  rho_W=ref['rho_W_kg_m3'], mu_W=ref['mu_W_Pa_s'])
        if keep_constants:
            s['fixed_C'] = C0
            lib = dict(C0)
        else:
            lib = packlib.fit_constants(wet, ['hold'], s)['user']
            if len(dry) > 0:
                C['C_P0'] = lib['Cp']
                status['C_P0'] = f'fitted on user data ({today})'
            C['C_h'], C['C_Fl'] = lib['Ch'], lib['Cfp']
            status['C_h'] = status['C_Fl'] = f'fitted on user data ({today})'
        pred = packlib.billet_lib(wet, {'user': lib}, s)
        feats = packlib.features(wet, {'user': lib}, s)
        hyd_consts = {'C_P0': C['C_P0'], 'C_h': C['C_h'], 'C_Fl': C['C_Fl'], 'C_S': C['C_S']}
        for out, col, true in [('dp', 'dp', 'dpdz_Pa_m'), ('holdup', 'hold', 'holdup')]:
            err1 = 100 * (pred[col] / wet[true] - 1)
            line = [out, len(wet), err1.abs().mean(), np.nan, 'none']
            if wet['curve_id'].nunique() >= 3:
                config = packlib.pick_config(wet, col, true, s)
                ok = pred[col].notna() & wet[true].notna()
                t = pd.DataFrame({'Re_L': feats['Re_L'], 'x_fl': feats['x'], 'resid': np.log(wet[true] / pred[col])})[ok]
                level2[out] = packlib.make_block(t, config, ['Re_L', 'x_fl'], out, hyd_consts)
                fac = [packlib.apply_block(level2[out], {'Re_L': r, 'x_flood': x}) for r, x in zip(t['Re_L'], t['x_fl'])]
                err2 = 100 * (pred[col][ok] * np.array([1 if f is None else f for f in fac]) / wet[true][ok] - 1)
                line[3], line[4] = err2.abs().mean(), config[0]
            report.append(line)
    else:
        log('hydraulics: no wet rows with both dp and holdup (or no starting C_Fl, C_h, C_P0): hydraulic constants kept')

    # ---- mass transfer ----
    Ch = {'Clp': C['C_S'], 'Cfp': C['C_Fl'], 'Cp': C['C_P0'], 'Ch': C['C_h']}
    for side, meas, Dcol, name, out, lin_col in [('L', 'kLa_1_s', 'D_L', 'C_L', 'kLa', 'Re_L'),
                                                 ('G', 'kGa_1_s', 'D_V', 'C_V', 'kGa', 'x_fl')]:
        t = data[data[meas].notna() & data[Dcol].notna()].copy()
        if len(t) == 0:
            continue
        if keep_constants and start[name] is None:
            log(f'{out}: the starting file has no {name} and --keep-constants is set: no level 2')
            continue
        uL_s = t['uL_m3_m2h'] / 3600
        t['h_L'] = packlib.film_holdup(uL_s, t['rho_L'], t['mu_L'], ap)
        t['aPh'] = packlib.billet_area(uL_s, t['rho_L'], t['mu_L'], t['sigma_L'], ap, eps)
        if side == 'L':
            t['beta_1'] = packlib.beta_L1(uL_s, t['h_L'], t[Dcol], ap, eps)
        else:
            t['beta_1'] = packlib.beta_V1(t['uG_ms'], t['rho_G'], t['mu_G'], t[Dcol], t['h_L'], ap, eps)
        t['C_run'] = t[meas] / (t['beta_1'] * t['aPh'])
        t[['uG_lp', 'below_lp']] = packlib.below_loading(t, ap, eps, C['C_S'])
        if keep_constants:
            used_rows = t[t['below_lp']]
            Cfit, used = start[name], len(used_rows)
            err1 = ((Cfit / used_rows['C_run'] - 1) * 100).abs().mean() if used else np.nan
        else:
            Cfit, used, total, err1 = packlib.fit_mt(t)
        if used == 0:
            log(f'{out}: no runs below the loading point, constant kept')
            continue
        if not keep_constants:
            C[name] = Cfit
            status[name] = f'fitted on user data ({today})'
        line = [out, used, err1, np.nan, 'none']
        if Ch['Cfp'] is not None:
            t = packlib.mt_features(t, Ch, ap, eps, dc)
            config, scores = packlib.mt_choose(t, lin_col, Cfit)
            if config is not None:
                u = t[t['below_lp']].copy()
                u['resid'] = np.log(u['C_run'] / Cfit)
                level2[out] = packlib.make_block(u, config, [lin_col], out, {name: Cfit}, scores)
                line[4] = config[0]
                fac = [1.0 if level2[out] is None else packlib.apply_block(level2[out], {'Re_L': r, 'x_flood': x}) or 1.0
                       for r, x in zip(u['Re_L'], u['x_fl'])]
                line[3] = (100 * (np.array(fac) * Cfit / u['C_run'] - 1)).abs().mean()
        report.append(line)

    # ---- result ----
    pk['file_kind'] = 'user'
    pk['trained'] = {'date': today, 'data_file': data_file, 'rows': int(len(data)), 'column_diameter_m': dc,
                     'started_from': pk['packing'].get('id'), 'constants_kept': bool(keep_constants)}
    for k in C:
        if C[k] is not None:
            pk['constants'][k] = {'value': float(C[k]), 'source': 'user data' if 'user' in str(status[k]) else
                                  (pk['constants'][k] or {}).get('source'), 'status': status[k]}
    pk['level2'] = level2
    rep = pd.DataFrame(report, columns=['output', 'rows used', 'level 1 error %', 'level 2 error %', 'level 2 form'])
    return pk, rep


def write_onnx(level2, folder, stem, packing_id=None, check=True):
    """Write every non-null level 2 block as <folder>/<stem>_<output>.onnx. Returns {output: file name}.
    check: compare each model with packlib.apply_block when onnxruntime is installed."""
    import onnx_export
    files = {}
    for out in LEVEL2_OUTPUTS:
        block = level2.get(out)
        if block is None:
            continue
        name = f'{stem}_{out}.onnx'
        path = os.path.join(folder, name)
        onnx_export.write_block(block, path, packing_id)
        if check:
            worst = onnx_export.check_against_block(block, path)
            if worst is not None and worst > 1e-9:
                raise AssertionError(f'{path}: ONNX model differs from the level 2 block by {worst:.2e}')
        files[out] = name
    return files


if __name__ == '__main__':
    if len(sys.argv) < 4 or '--dc' not in sys.argv:
        print('usage: python train.py <starting packing file> <data csv> <output packing file> --dc <column diameter m>'
              ' [--keep-constants] [--no-onnx]')
        sys.exit(1)
    start_file, data_file, out_file = sys.argv[1], sys.argv[2], sys.argv[3]
    dc = float(sys.argv[sys.argv.index('--dc') + 1])

    try:
        pk, rep = train(json.load(open(start_file)), pd.read_csv(data_file), dc,
                        keep_constants='--keep-constants' in sys.argv, data_file=data_file)
    except ValueError as ex:
        print(ex)
        sys.exit(1)

    if '--no-onnx' not in sys.argv:
        folder = os.path.dirname(os.path.abspath(out_file))
        stem = os.path.splitext(os.path.basename(out_file))[0]
        pk['onnx_models'] = write_onnx(pk['level2'], folder, stem, pk['packing'].get('id'))
    json.dump(pk, open(out_file, 'w'), indent=1)

    print('constants:', {k: (None if v is None else round(float(v['value']), 4)) for k, v in pk['constants'].items()})
    print(rep.round(1).to_string(index=False))
    if pk.get('onnx_models'):
        print('onnx models:', ', '.join(pk['onnx_models'].values()))
    print('errors are on the training rows (in-sample); written', out_file)
