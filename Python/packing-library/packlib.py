# packlib: the shared packed-column library (D47)
# every packing notebook loads its own data, fills in a settings dict s, then calls these functions
#
# what a notebook has to supply
#   curve: one row per operating point, columns system, uL_m3_m2h, F_Pa05, uG_ms, dpdz_Pa_m, holdup
#   dry:   dry-bed rows used to fit Cp, columns uG_ms, dpdz_Pa_m
#   s:     settings dict, see make_settings below
#
# sources: billet and schultes (1995) equations, as built and checked in flexipac.ipynb sections 4 and 5

import numpy as np
import pandas as pd
from scipy.optimize import minimize_scalar
from sklearn.neural_network import MLPRegressor

g = 9.81


# settings for one packing and column, everything the equations need
def make_settings(ap, eps, dc, rho_G, mu_G, props, C0, dry, rho_W=1000, mu_W=8.02e-4):
    # ap specific area m2/m3, eps void fraction, dc column diameter m
    # props: {system: (rho_L, mu_L)}, C0: level 0 constants {'Cp', 'Clp', 'Cfp', 'Ch'}
    # dry: the dry rows Cp is fitted on (the notebook decides which, e.g. D45)
    return {'ap': ap, 'eps': eps, 'dc': dc, 'rho_G': rho_G, 'mu_G': mu_G, 'props': props,
            'C0': C0, 'dry': dry, 'rho_W': rho_W, 'mu_W': mu_W}


# rows may carry their own fluid properties (D49): columns rho_L, mu_L, rho_G, mu_G
# if they do, that row uses them, otherwise the packing settings are used
def row_settings(s, row):
    if 'mu_L' not in row:
        return s
    s2 = dict(s)
    s2['rho_G'] = row['rho_G']
    s2['mu_G'] = row['mu_G']
    s2['props'] = {row['system']: (row['rho_L'], row['mu_L'])}
    return s2


# which rows form one curve. a packing whose liquid load drifts within a run can give a curve_id
# column (e.g. RSP 250Y, D50), otherwise a curve is one system at one liquid load
def curve_keys(rows):
    if 'curve_id' in rows.columns:
        return rows['curve_id']
    return rows['system'] + ' ' + rows['uL_m3_m2h'].astype(str)


# error score used for every fit, log ratio squared, no answer costs a fixed penalty (5.1)
def log_error(pred, measured, penalty=1):
    pred = np.array(pred, dtype=float)
    measured = np.array(measured, dtype=float)
    sq = np.log(pred / measured) ** 2
    sq[np.isnan(sq)] = penalty
    return sq.sum()


# billet model for one operating point, returns dp (Pa/m) and hold-up, nan past the flood point
def billet(system, uL, uG_s, C, s):
    ap, eps, dc, rho_G, mu_G = s['ap'], s['eps'], s['dc'], s['rho_G'], s['mu_G']
    rho_L, mu_L = s['props'][system]
    uL_s = uL / 3600

    d_part = 6 * (1 - eps) / ap
    fs = 1 / (1 + 4 / (ap * dc))
    psi = (uL_s * rho_L) / (uG_s * rho_G) * np.sqrt(rho_G / rho_L)

    # loading point
    ht_lp = 12 ** (1/3) * (uL_s * rho_L / (ap * mu_L)) ** (-1/3) * (uL_s ** 2 * ap / g) ** (1/3)
    if psi <= 0.4:
        n_lp = -0.326
        n_fp = -0.194
    else:
        n_lp = -0.723
        n_fp = -0.708
    xi_lp = g / (C['Clp'] ** 2 * (psi * (mu_L / mu_G) ** 0.4) ** (2 * n_lp))
    uG_lp = np.sqrt(g / xi_lp) * (eps - ht_lp) * np.sqrt(ht_lp / ap) * np.sqrt(rho_L / rho_G)

    # flood point, no answer past it
    ht_fp = 0.3741 * eps * (mu_L * s['rho_W'] / (rho_L * s['mu_W'])) ** 0.05
    xi_fp = g / (C['Cfp'] ** 2 * (psi * (mu_L / mu_G) ** 0.2) ** (2 * n_fp))
    uG_fp = np.sqrt(2 * g / xi_fp) * (eps - ht_fp) ** 1.5 / np.sqrt(eps) * np.sqrt(ht_fp / ap) * np.sqrt(rho_L / rho_G)
    if uG_s > uG_fp:
        return np.nan, np.nan

    # hold-up inside the pressure drop
    if uG_s <= uG_lp:
        ht_dp = ht_lp
    else:
        ht_dp = ht_lp + (ht_fp - ht_lp) * (uG_s / uG_fp) ** 13

    # hold-up compared with the data, partial wetting through Ch
    Re_L = uL_s * rho_L / (ap * mu_L)
    Fr_L = uL_s ** 2 * ap / g
    if Re_L < 5:
        ah_ap = C['Ch'] * Re_L ** 0.15 * Fr_L ** 0.1
    else:
        ah_ap = 0.85 * C['Ch'] * Re_L ** 0.25 * Fr_L ** 0.1
    ht_lp2 = (12 * mu_L * ap ** 2 * uL_s / (rho_L * g)) ** (1/3) * ah_ap ** (2/3)
    ht_fp2 = 2.2 * ht_lp2
    if uG_s <= uG_lp:
        holdup = ht_lp2
    else:
        holdup = ht_lp2 + (ht_fp2 - ht_lp2) * (uG_s / uG_fp) ** 13

    # dry then wet pressure drop, billet divides by the wall factor (D13a)
    Re_G = uG_s * d_part * rho_G / ((1 - eps) * mu_G) * fs
    xi_d = C['Cp'] * (64 / Re_G + 1.8 / Re_G ** 0.08)
    dp_dry = xi_d * ap / eps ** 3 * uG_s ** 2 / 2 * rho_G / fs
    W = np.exp(Re_L / 200) * (ht_dp / ht_lp) ** 0.3
    xi_w = C['Cp'] * W * (64 / Re_G + 1.8 / Re_G ** 0.08) * ((eps - ht_dp) / eps) ** 1.5
    dp_wet = dp_dry * (xi_w / xi_d) * (eps / (eps - ht_dp)) ** 3
    return float(dp_wet), float(holdup)


# dry-bed pressure drop only, works on a whole column of gas velocities
def dry_dp(uG_s, C, s):
    ap, eps, dc, rho_G, mu_G = s['ap'], s['eps'], s['dc'], s['rho_G'], s['mu_G']
    d_part = 6 * (1 - eps) / ap
    fs = 1 / (1 + 4 / (ap * dc))
    Re_G = uG_s * d_part * rho_G / ((1 - eps) * mu_G) * fs
    xi_d = C['Cp'] * (64 / Re_G + 1.8 / Re_G ** 0.08)
    return xi_d * ap / eps ** 3 * uG_s ** 2 / 2 * rho_G / fs


# gas velocity at billet's loading point
def loading_uG(system, uL, uG_s, C, s):
    ap, eps, rho_G, mu_G = s['ap'], s['eps'], s['rho_G'], s['mu_G']
    rho_L, mu_L = s['props'][system]
    uL_s = uL / 3600
    psi = (uL_s * rho_L) / (uG_s * rho_G) * np.sqrt(rho_G / rho_L)
    ht_lp = 12 ** (1/3) * (uL_s * rho_L / (ap * mu_L)) ** (-1/3) * (uL_s ** 2 * ap / g) ** (1/3)
    if psi <= 0.4:
        n_lp = -0.326
    else:
        n_lp = -0.723
    xi_lp = g / (C['Clp'] ** 2 * (psi * (mu_L / mu_G) ** 0.4) ** (2 * n_lp))
    return np.sqrt(g / xi_lp) * (eps - ht_lp) * np.sqrt(ht_lp / ap) * np.sqrt(rho_L / rho_G)


# gas velocity at billet's flood point
def flood_uG(system, uL, uG_s, C, s):
    ap, eps, rho_G, mu_G = s['ap'], s['eps'], s['rho_G'], s['mu_G']
    rho_L, mu_L = s['props'][system]
    uL_s = uL / 3600
    psi = (uL_s * rho_L) / (uG_s * rho_G) * np.sqrt(rho_G / rho_L)
    if psi <= 0.4:
        n_fp = -0.194
    else:
        n_fp = -0.708
    ht_fp = 0.3741 * eps * (mu_L * s['rho_W'] / (rho_L * s['mu_W'])) ** 0.05
    xi_fp = g / (C['Cfp'] ** 2 * (psi * (mu_L / mu_G) ** 0.2) ** (2 * n_fp))
    return np.sqrt(2 * g / xi_fp) * (eps - ht_fp) ** 1.5 / np.sqrt(eps) * np.sqrt(ht_fp / ap) * np.sqrt(rho_L / rho_G)


# billet on many rows with one set of constants
def billet_rows(rows, C, s):
    results = []
    for i in rows.index:
        row = rows.loc[i]
        results.append(billet(row['system'], row['uL_m3_m2h'], row['uG_ms'], C, row_settings(s, row)))
    return pd.DataFrame(results, columns=['dp', 'hold'], index=rows.index)


# billet on many rows, each row uses the constants for its own liquid
def billet_lib(rows, lib, s):
    results = []
    for i in rows.index:
        row = rows.loc[i]
        results.append(billet(row['system'], row['uL_m3_m2h'], row['uG_ms'], lib[row['system']], row_settings(s, row)))
    return pd.DataFrame(results, columns=['dp', 'hold'], index=rows.index)


# level 1: Cp on dry rows, Ch on calm-zone hold-up, Cfp above its floor, one set per liquid (5.8)
def fit_constants(train, outputs, s):
    dry = s['dry']
    base = dict(s['C0'])

    # dry rows with their own gas properties use them (D49)
    s_dry = dict(s)
    if 'mu_G' in dry.columns:
        s_dry['rho_G'] = dry['rho_G'].values
        s_dry['mu_G'] = dry['mu_G'].values

    def score_cp(Cp):
        return log_error(dry_dp(dry['uG_ms'].values, {'Cp': Cp}, s_dry), dry['dpdz_Pa_m'].values)
    base['Cp'] = minimize_scalar(score_cp, bounds=(0.01, 1.0), method='bounded').x

    lib = {}
    for system in train['system'].unique():
        rows = train[train['system'] == system]

        # Ch from the rows below the loading point
        lp = [loading_uG(system, r['uL_m3_m2h'], r['uG_ms'], base, row_settings(s, r)) for i, r in rows.iterrows()]
        calm_rows = rows[rows['uG_ms'] <= np.array(lp)]

        def score_ch(Ch):
            C = dict(base)
            C['Ch'] = Ch
            return log_error(billet_rows(calm_rows, C, s)['hold'], calm_rows['holdup'])

        C = dict(base)
        C['Ch'] = minimize_scalar(score_ch, bounds=(0.05, 3.0), method='bounded').x

        # Cfp, floor = smallest Cfp that keeps every training row below flood
        ratio = [r['uG_ms'] / flood_uG(system, r['uL_m3_m2h'], r['uG_ms'], C, row_settings(s, r)) for i, r in rows.iterrows()]
        floor = C['Cfp'] * max(ratio) * 1.000001

        def score_cfp(Cfp):
            C2 = dict(C)
            C2['Cfp'] = Cfp
            pred = billet_rows(rows, C2, s)
            score = 0
            if 'dp' in outputs:
                score = score + log_error(pred['dp'], rows['dpdz_Pa_m'])
            if 'hold' in outputs:
                score = score + log_error(pred['hold'], rows['holdup'])
            return score

        C['Cfp'] = minimize_scalar(score_cfp, bounds=(floor, 10), method='bounded').x
        lib[system] = C
    return lib


# level 1 on test rows. a liquid missing from train borrows water's constants (D31),
# or the first trained liquid if there is no water
def physics(train, test, outputs, s):
    lib = fit_constants(train, outputs, s)
    if 'air/water' in lib:
        first = 'air/water'
    else:
        first = list(lib)[0]
    for system in test['system'].unique():
        if system not in lib:
            lib[system] = lib[first]
    return billet_lib(test, lib, s), lib


# the four tests (D24). transfer=(train liquid, test liquid) only when the packing has two liquids
def make_splits(curve, transfer=None, top_frac=0.25):
    splits = {}

    pairs = []
    for i in curve.index:
        pairs.append((curve.drop(i), curve.loc[[i]]))
    splits['leave one point out'] = pairs

    pairs = []
    for name, rows in curve.groupby(curve_keys(curve)):
        pairs.append((curve.drop(rows.index), rows))
    splits['leave one curve out'] = pairs

    if transfer is not None:
        a, b = transfer
        splits['train ' + a + ', test ' + b] = [(curve[curve['system'] == a], curve[curve['system'] == b])]

    # top F: on each curve, hold out the top 25% of F
    cut = curve.groupby(curve_keys(curve))['F_Pa05'].transform(lambda f: f.quantile(1 - top_frac))
    top = curve['F_Pa05'] > cut
    splits['top F test'] = [(curve[~top], curve[top])]
    return splits


# level 2 inputs: liquid reynolds number and gas velocity as a fraction of level 1 flood (D22)
def features(rows, lib, s):
    results = []
    for i in rows.index:
        row = rows.loc[i]
        sr = row_settings(s, row)
        rho_L, mu_L = sr['props'][row['system']]
        uL_s = row['uL_m3_m2h'] / 3600
        Re_L = uL_s * rho_L / (s['ap'] * mu_L)
        x = row['uG_ms'] / flood_uG(row['system'], row['uL_m3_m2h'], row['uG_ms'], lib[row['system']], sr)
        results.append([Re_L, x])
    return pd.DataFrame(results, columns=['Re_L', 'x'], index=rows.index)


# the candidate corrections the tuned form chooses between (D46)
tuned_configs = [('linear', None, None)] + [('network', h, a) for h in [(10, 10), (5,), (3,)] for a in [0.1, 1.0, 10.0]]


# one correction: learn y = log(measured / level 1) from X_train, return y for X_test
def correction(config, X_train, y, X_test, seeds=(0,)):
    form, layers, alpha = config
    if form == 'linear':
        A = np.column_stack([np.ones(len(y)), X_train])
        coef = np.linalg.lstsq(A, y, rcond=None)[0]
        return coef[0] + X_test @ coef[1:]
    preds = []
    for seed in seeds:
        net = MLPRegressor(hidden_layer_sizes=layers, alpha=alpha, solver='lbfgs', random_state=seed, max_iter=2000)
        net.fit(X_train, y)
        preds.append(net.predict(X_test))
    return np.mean(preds, axis=0)


# level 1 on train and test, plus scaled level 2 inputs (D23: scaled with the training mean and sd)
def level1_parts(train, test, s):
    p_test, lib = physics(train, test, ['hold'], s)
    p_train = billet_lib(train, lib, s)
    f_train = features(train, lib, s)
    f_test = features(test, lib, s)
    mean = f_train.mean()
    sd = f_train.std()
    return p_train, p_test, ((f_train - mean) / sd).values, ((f_test - mean) / sd).values


# level 2 = level 1 x exp(correction). form 'linear', 'network' (notebook 6.2) or 'tuned' (D46)
def level2(train, test, form, s):
    p_train, p_test, X_train, X_test = level1_parts(train, test, s)
    out = pd.DataFrame(index=test.index, columns=['dp', 'hold'], dtype=float)
    for col, true in [('dp', 'dpdz_Pa_m'), ('hold', 'holdup')]:
        # skip rows where level 1 floods or the measurement is missing
        ok = p_train[col].notna().values & train[true].notna().values
        y = np.log(train[true].values[ok] / p_train[col].values[ok])
        if form == 'linear':
            corr = correction(('linear', None, None), X_train[ok], y, X_test)
        elif form == 'network':
            corr = correction(('network', (10, 10), 0.0001), X_train[ok], y, X_test)
        else:
            best = pick_config(train, col, true, s)
            seeds = (0,) if best[0] == 'linear' else (0, 1, 2)
            corr = correction(best, X_train[ok], y, X_test, seeds)
        out[col] = p_test[col].values * np.exp(corr)
    return out


# D46: choose the correction using only the training rows, leaving one training curve out at a time
def pick_config(train, col, true, s):
    errors = {config: [] for config in tuned_configs}
    for name, val in train.groupby(curve_keys(train)):
        inner = train.drop(val.index)
        p_train, p_val, X_train, X_val = level1_parts(inner, val, s)
        ok = p_train[col].notna().values & inner[true].notna().values
        y = np.log(inner[true].values[ok] / p_train[col].values[ok])
        for config in tuned_configs:
            seeds = (0,) if config[0] == 'linear' else (0, 1, 2)
            pred = p_val[col].values * np.exp(correction(config, X_train[ok], y, X_val, seeds))
            errors[config].append(100 * np.abs(pred - val[true].values) / val[true].values)
    score = {config: np.nanmean(np.concatenate(errors[config])) for config in tuned_configs}
    return min(score, key=score.get)


# run every split for the chosen models. models: names from 'L0', 'L1', 'L2 linear', 'L2 network', 'L2 tuned'
def run_tests(splits, s, models, skip_level2=()):
    level0 = {}
    tests = {}
    for split, pairs in splits.items():
        preds = {}
        for model in models:
            if model.startswith('L2') and split in skip_level2:
                continue
            parts = []
            for train, test in pairs:
                if model == 'L0':
                    for system in test['system'].unique():
                        level0[system] = s['C0']
                    parts.append(billet_lib(test, level0, s))
                elif model == 'L1':
                    parts.append(physics(train, test, ['hold'], s)[0])
                else:
                    parts.append(level2(train, test, model.split(' ')[1], s))
            preds[model] = pd.concat(parts)
        tests[split] = preds
        print(split, 'done')
    return tests


# one table: every split and model, errors on the rows every model answers (common points)
def results_table(tests, curve):
    rows_out = []
    for split, preds in tests.items():
        labels = list(preds)
        idx = preds[labels[0]].index
        common = idx
        for label in labels:
            common = common[preds[label].loc[common, 'dp'].notna().values]
        for label in labels:
            pred = preds[label]
            dp_err = 100 * (pred['dp'] - curve.loc[idx, 'dpdz_Pa_m']) / curve.loc[idx, 'dpdz_Pa_m']
            hold_err = 100 * (pred['hold'] - curve.loc[idx, 'holdup']) / curve.loc[idx, 'holdup']
            rows_out.append([split, label, len(idx), pred['dp'].notna().sum(), len(common),
                             dp_err[common].abs().mean(), dp_err[common].mean(),
                             hold_err[common].abs().mean(), hold_err[common].mean()])
    return pd.DataFrame(rows_out, columns=['split', 'model', 'tested', 'answered', 'common',
                                           'dp_abs_%', 'dp_bias_%', 'hold_abs_%', 'hold_bias_%'])


# fold spread (D34): change in abs error of model against base, averaged per curve, on common points
def spread_table(tests, curve, base, models):
    curve_name = curve_keys(curve)
    rows_out = []
    for split, preds in tests.items():
        labels = list(preds)
        if base not in preds:
            continue
        idx = preds[base].index
        common = idx
        for label in labels:
            common = common[preds[label].loc[common, 'dp'].notna().values]
        for label in models:
            if label not in preds:
                continue
            for out, col in [('dp', 'dpdz_Pa_m'), ('hold', 'holdup')]:
                err1 = (100 * (preds[label].loc[common, out] - curve.loc[common, col]) / curve.loc[common, col]).abs()
                err0 = (100 * (preds[base].loc[common, out] - curve.loc[common, col]) / curve.loc[common, col]).abs()
                per_curve = (err1 - err0).groupby(curve_name[common]).mean()
                rows_out.append([split, label, out, len(per_curve), per_curve.mean(), per_curve.std(), per_curve.min(), per_curve.max()])
    return pd.DataFrame(rows_out, columns=['split', 'model', 'output', 'curves', 'change_mean', 'change_sd', 'change_min', 'change_max'])
