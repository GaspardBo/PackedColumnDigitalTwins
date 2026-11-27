# packlib: the shared packed-column library
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
    # dry: the dry rows Cp is fitted on (the notebook decides which)
    return {'ap': ap, 'eps': eps, 'dc': dc, 'rho_G': rho_G, 'mu_G': mu_G, 'props': props,
            'C0': C0, 'dry': dry, 'rho_W': rho_W, 'mu_W': mu_W}


# rows may carry their own fluid properties: columns rho_L, mu_L, rho_G, mu_G
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
# column (e.g. RSP 250Y), otherwise a curve is one system at one liquid load
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

    # dry then wet pressure drop, billet divides by the wall factor
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

    # dry rows with their own gas properties use them
    s_dry = dict(s)
    if 'mu_G' in dry.columns:
        s_dry['rho_G'] = dry['rho_G'].values
        s_dry['mu_G'] = dry['mu_G'].values

    def score_cp(Cp):
        return log_error(dry_dp(dry['uG_ms'].values, {'Cp': Cp}, s_dry), dry['dpdz_Pa_m'].values)
    # no dry rows (a user without dry-bed runs): keep the starting Cp
    if len(dry) > 0:
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


# level 1 on test rows. a liquid missing from train borrows water's constants,
# or the first trained liquid if there is no water.
# s['fixed_C'] (optional, {'Cp', 'Clp', 'Cfp', 'Ch'}): use these constants for every liquid instead of fitting
def physics(train, test, outputs, s):
    if s.get('fixed_C') is not None:
        lib = {system: dict(s['fixed_C']) for system in train['system'].unique()}
    else:
        lib = fit_constants(train, outputs, s)
    if 'air/water' in lib:
        first = 'air/water'
    else:
        first = list(lib)[0]
    for system in test['system'].unique():
        if system not in lib:
            lib[system] = lib[first]
    return billet_lib(test, lib, s), lib


# the four tests. transfer=(train liquid, test liquid) only when the packing has two liquids
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


# level 2 inputs: liquid reynolds number and gas velocity as a fraction of level 1 flood
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


# the candidate corrections the tuned form chooses between
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


# level 1 on train and test, plus scaled level 2 inputs (scaled with the training mean and sd)
def level1_parts(train, test, s):
    p_test, lib = physics(train, test, ['hold'], s)
    p_train = billet_lib(train, lib, s)
    f_train = features(train, lib, s)
    f_test = features(test, lib, s)
    mean = f_train.mean()
    sd = f_train.std()
    return p_train, p_test, ((f_train - mean) / sd).values, ((f_test - mean) / sd).values


# level 2 = level 1 x exp(correction). form 'linear', 'network' (notebook 6.2) or 'tuned'
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


# choose the correction using only the training rows, leaving one training curve out at a time
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


# fold spread: change in abs error of model against base, averaged per curve, on common points
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


# ---------------------------------------------------------------------------
# mass transfer (step 7): billet (1995) ch. 5, as built by hand in mass_transfer/mass_transfer.ipynb
# c_l and c_v are fitted to the products kl*a and kg*a with billet's own area inside
# hold-up inside the mass-transfer equations is the film hold-up, eq 4-27
# ---------------------------------------------------------------------------

import io

# liquid water at 0.101325 MPa, 0 to 40 C: NIST Chemistry WebBook, SRD 69 (Lemmon, McLinden and Friend), retrieved 17 Nov 2026
water_table = pd.read_csv(io.StringIO("""T_C,rho_kg_m3,mu_Pa_s
0.010000,999.84,0.0017911
1.0100,999.90,0.0017304
2.0100,999.94,0.0016730
3.0100,999.97,0.0016185
4.0100,999.97,0.0015668
5.0100,999.97,0.0015177
6.0100,999.94,0.0014710
7.0100,999.90,0.0014266
8.0100,999.85,0.0013843
9.0100,999.78,0.0013440
10.010,999.70,0.0013055
11.010,999.61,0.0012688
12.010,999.50,0.0012337
13.010,999.38,0.0012001
14.010,999.25,0.0011680
15.010,999.10,0.0011373
16.010,998.94,0.0011078
17.010,998.78,0.0010795
18.010,998.60,0.0010524
19.010,998.41,0.0010264
20.010,998.21,0.0010014
21.010,997.99,0.00097730
22.010,997.77,0.00095417
23.010,997.54,0.00093191
24.010,997.30,0.00091047
25.010,997.05,0.00088982
26.010,996.78,0.00086991
27.010,996.51,0.00085072
28.010,996.23,0.00083220
29.010,995.94,0.00081432
30.010,995.65,0.00079705
31.010,995.34,0.00078037
32.010,995.02,0.00076425
33.010,994.70,0.00074866
34.010,994.37,0.00073358
35.010,994.03,0.00071898
36.010,993.68,0.00070485
37.010,993.33,0.00069117
38.010,992.96,0.00067791
39.010,992.59,0.00066506
40.010,992.21,0.00065261
"""))


# water density, viscosity (nist table) and surface tension (iapws 2014) at T in C
def water_props(T_C):
    rho = np.interp(T_C, water_table['T_C'], water_table['rho_kg_m3'])
    mu = np.interp(T_C, water_table['T_C'], water_table['mu_Pa_s'])
    tau = 1 - (np.asarray(T_C) + 273.15) / 647.096
    sigma = 235.8e-3 * tau ** 1.256 * (1 - 0.625 * tau)
    return rho, mu, sigma


# dry air density (ideal gas) and viscosity (sutherland, white's constants), as the hydraulics
def air_props(T_C, P=101325):
    T = np.asarray(T_C) + 273.15
    rho = P * 0.0289644 / (8.314462 * T)
    mu = 1.716e-5 * (T / 273.15) ** 1.5 * (273.15 + 110.4) / (T + 110.4)
    return rho, mu


# liquid diffusivity, wilke and chang (1955). mu in pa s, V solute molar volume at boiling point cm3/mol
# returns m2/s. defaults: water solvent (phi 2.6, M 18.015), toluene (le bas 118.2)
def diff_liquid(T_C, mu_L, V=118.2, phi=2.6, M_solvent=18.015):
    T = np.asarray(T_C) + 273.15
    return 7.4e-8 * (phi * M_solvent) ** 0.5 * T / (mu_L * 1000 * V ** 0.6) * 1e-4


# gas diffusivity, fuller (1966) in the form of poling et al. (2001). returns m2/s
# defaults: so2 (64.06 g/mol, diffusion volume 41.8) in air (28.96, 19.7)
def diff_gas(T_C, P=101325, M_A=64.06, M_B=28.96, v_A=41.8, v_B=19.7):
    T = np.asarray(T_C) + 273.15
    M_AB = 2 / (1 / M_A + 1 / M_B)
    return 0.00143 * T ** 1.75 / ((P / 1e5) * M_AB ** 0.5 * (v_A ** (1/3) + v_B ** (1/3)) ** 2) * 1e-4


# film hold-up, eq 4-27, uL in m/s
def film_holdup(uL_s, rho_L, mu_L, ap):
    return (12 * mu_L * ap ** 2 * uL_s / (g * rho_L)) ** (1/3)


# billet's phase-boundary area a_ph in m2/m3, eq 5-16 (froude exponent -0.45)
def billet_area(uL_s, rho_L, mu_L, sigma, ap, eps):
    Re = uL_s * rho_L / (ap * mu_L)
    We = uL_s ** 2 * rho_L / (sigma * ap)
    Fr = uL_s ** 2 * ap / g
    return ap * 3 * eps ** 0.5 * Re ** -0.2 * We ** 0.75 * Fr ** -0.45


# billet's liquid-side coefficient with c_l = 1, m/s: eq 5-4 / a_ph with the film hold-up
def beta_L1(uL_s, h_L, D_L, ap, eps):
    d_h = 4 * eps / ap
    return 12 ** (1/6) * (uL_s / h_L) ** 0.5 * (D_L / d_h) ** 0.5


# billet's gas-side coefficient with c_v = 1, m/s: eq 5-13 / a_ph
def beta_V1(uG_s, rho_G, mu_G, D_V, h_L, ap, eps):
    d_h = 4 * eps / ap
    Re_V = uG_s * rho_G / (ap * mu_G)
    Sc_V = mu_G / (rho_G * D_V)
    return ap ** 0.5 / (d_h ** 0.5 * (eps - h_L) ** 0.5) * D_V * Re_V ** 0.75 * Sc_V ** (1/3)


# loading point for each row: rows need uL_m3_m2h, uG_ms, rho_L, mu_L, rho_G, mu_G
def below_loading(rows, ap, eps, Clp):
    out = []
    for i, row in rows.iterrows():
        s = {'ap': ap, 'eps': eps, 'rho_G': row['rho_G'], 'mu_G': row['mu_G'],
             'props': {'water': (row['rho_L'], row['mu_L'])}}
        uG_lp = loading_uG('water', row['uL_m3_m2h'], row['uG_ms'], {'Clp': Clp}, s)
        out.append([uG_lp, row['uG_ms'] <= uG_lp])
    return pd.DataFrame(out, columns=['uG_lp', 'below_lp'], index=rows.index)


# wang table d.2 rows -> per-run c_l on the kl*a basis. air temperature not given, set by T_air_C
def liquid_side(kl_rows, ap, eps, Clp, T_air_C=None):
    t = kl_rows.copy()
    t['uL_m3_m2h'] = t['L_m3_m2h']
    t['rho_L'], t['mu_L'], t['sigma'] = water_props(t['Twater_in_C'])
    T_air = t['Twater_in_C'] if T_air_C is None else T_air_C
    t['rho_G'], t['mu_G'] = air_props(T_air)
    uL_s = t['L_m3_m2h'] / 3600
    t['D_L'] = diff_liquid(t['Twater_in_C'], t['mu_L'])
    t['h_L'] = film_holdup(uL_s, t['rho_L'], t['mu_L'], ap)
    t['aPh'] = billet_area(uL_s, t['rho_L'], t['mu_L'], t['sigma'], ap, eps)
    t['beta_1'] = beta_L1(uL_s, t['h_L'], t['D_L'], ap, eps)
    t['ka_meas'] = t['kL_1e5_ms'] * 1e-5 * t['ae_m2_m3']
    t['C_run'] = t['ka_meas'] / (t['beta_1'] * t['aPh'])
    t[['uG_lp', 'below_lp']] = below_loading(t, ap, eps, Clp)
    return t


# wang table d.3 rows -> per-run c_v on the kg*a basis. liquid at T_liq_C, gas at air inlet T
def gas_side(kg_rows, ap, eps, Clp, T_liq_C=25):
    t = kg_rows.copy()
    t['uL_m3_m2h'] = t['L_m3_m2h']
    t['rho_L'], t['mu_L'], t['sigma'] = water_props(T_liq_C + 0 * t['L_m3_m2h'])
    t['rho_G'], t['mu_G'] = air_props(t['Tair_in_C'])
    uL_s = t['L_m3_m2h'] / 3600
    t['D_V'] = diff_gas(t['Tair_in_C'])
    t['h_L'] = film_holdup(uL_s, t['rho_L'], t['mu_L'], ap)
    t['aPh'] = billet_area(uL_s, t['rho_L'], t['mu_L'], t['sigma'], ap, eps)
    t['beta_1'] = beta_V1(t['uG_ms'], t['rho_G'], t['mu_G'], t['D_V'], t['h_L'], ap, eps)
    t['ka_meas'] = t['kG_1e2_ms'] * 1e-2 * t['ae_m2_m3']
    t['C_run'] = t['ka_meas'] / (t['beta_1'] * t['aPh'])
    t[['uG_lp', 'below_lp']] = below_loading(t, ap, eps, Clp)
    return t


# fit one constant: geometric mean of C_run on the rows below loading
# returns the constant, runs used, runs in total, mean abs % error on the runs used
def fit_mt(t):
    used = t[t['below_lp']]
    if len(used) == 0:
        return np.nan, 0, len(t), np.nan
    C = np.exp(np.log(used['C_run']).mean())
    err = (C / used['C_run'] - 1) * 100
    return C, len(used), len(t), err.abs().mean()


# any source: per-run c_l or c_v on the k*a basis. rows need uL_m3_m2h, uG_ms, T_L_C, T_G_C, ka_meas (1/s)
# side 'L' (liquid diffusivity of the solute, molar volume V) or 'G' (so2 in air)
def mt_side(rows, side, ap, eps, Clp, V=118.2):
    t = rows.copy()
    t['rho_L'], t['mu_L'], t['sigma'] = water_props(t['T_L_C'])
    t['rho_G'], t['mu_G'] = air_props(t['T_G_C'])
    uL_s = t['uL_m3_m2h'] / 3600
    t['h_L'] = film_holdup(uL_s, t['rho_L'], t['mu_L'], ap)
    t['aPh'] = billet_area(uL_s, t['rho_L'], t['mu_L'], t['sigma'], ap, eps)
    if side == 'L':
        t['D'] = diff_liquid(t['T_L_C'], t['mu_L'], V=V)
        t['beta_1'] = beta_L1(uL_s, t['h_L'], t['D'], ap, eps)
    else:
        t['D'] = diff_gas(t['T_G_C'])
        t['beta_1'] = beta_V1(t['uG_ms'], t['rho_G'], t['mu_G'], t['D'], t['h_L'], ap, eps)
    t['C_run'] = t['ka_meas'] / (t['beta_1'] * t['aPh'])
    t[['uG_lp', 'below_lp']] = below_loading(t, ap, eps, Clp)
    return t


# level 2 for mass transfer
# candidates: no correction, then the hydraulics tuner's list
mt_configs = [('none', None, None)] + tuned_configs

# inputs per run: Re_L, and u_G / u_G,flood from level 1 (needs the packing's hydraulic constants C)
def mt_features(t, C, ap, eps, dc=0.427):
    t = t.copy()
    t['Re_L'] = t['uL_m3_m2h'] / 3600 * t['rho_L'] / (ap * t['mu_L'])
    x = []
    for i, row in t.iterrows():
        s = {'ap': ap, 'eps': eps, 'dc': dc, 'rho_G': row['rho_G'], 'mu_G': row['mu_G'],
             'props': {'w': (row['rho_L'], row['mu_L'])}, 'rho_W': 998.21, 'mu_W': 1.0014e-3}
        x.append(row['uG_ms'] / flood_uG('w', row['uL_m3_m2h'], row['uG_ms'], C, s))
    t['x_fl'] = x
    return t


# one correction on scaled inputs: linear on one input (lin_col), network on both
def mt_correction(config, train, test, lin_col, seeds):
    if config[0] == 'none':
        return np.zeros(len(test))
    cols = [lin_col] if config[0] == 'linear' else ['Re_L', 'x_fl']
    m = train[cols].mean()
    sd = train[cols].std().replace(0, 1)
    X_train = ((train[cols] - m) / sd).values
    X_test = ((test[cols] - m) / sd).values
    return correction(config, X_train, train['resid'].values, X_test, seeds)


# leave one run out on the runs below loading; level 1 constant refitted on each training set;
# tuner picks the form by an inner leave-one-out on the training runs only.
# fewer than min_runs below loading: no level 2. returns per-run table and the chosen configs
def mt_level2_test(t, lin_col, min_runs=4):
    t = t[t['below_lp']].copy()
    t['L1_err'] = np.nan
    t['L2_err'] = np.nan
    picks = []
    if len(t) < min_runs:
        return t, picks
    for i in t.index:
        train = t.drop(i).copy()
        test = t.loc[[i]].copy()
        C = np.exp(np.log(train['C_run']).mean())
        t.loc[i, 'L1_err'] = 100 * (C / test['C_run'].iloc[0] - 1)
        train['resid'] = np.log(train['C_run'] / C)
        # inner choice
        scores = {}
        for config in mt_configs:
            seeds = (0,) if config[0] != 'network' else (0, 1, 2)
            errs = []
            for j in train.index:
                inner = train.drop(j)
                val = train.loc[[j]]
                corr = mt_correction(config, inner, val, lin_col, seeds)
                errs.append(abs(np.exp(corr[0] - val['resid'].iloc[0]) - 1))
            scores[config] = np.mean(errs)
        best = min(scores, key=scores.get)
        picks.append(best[0])
        seeds = (0,) if best[0] != 'network' else (0, 1, 2)
        corr = mt_correction(best, train, test, lin_col, seeds)
        t.loc[i, 'L2_err'] = 100 * (C * np.exp(corr[0]) / test['C_run'].iloc[0] - 1)
    return t, picks


# ---------------------------------------------------------------------------
# packing files (format: json_format.md). level 2 blocks are written and evaluated here,
# so train.py and the example files use one copy of the code
# ---------------------------------------------------------------------------

FORMAT_VERSION = '1.0'


# choose the mass-transfer correction form by leaving one run out on all runs
# t needs C_run, Re_L, x_fl, below_lp. returns (config, scores) or (None, {}) under min_runs
def mt_choose(t, lin_col, level1_constant, min_runs=4):
    t = t[t['below_lp']].copy()
    if len(t) < min_runs:
        return None, {}
    t['resid'] = np.log(t['C_run'] / level1_constant)
    scores = {}
    for config in mt_configs:
        seeds = (0,) if config[0] != 'network' else (0, 1, 2)
        errs = []
        for j in t.index:
            corr = mt_correction(config, t.drop(j), t.loc[[j]], lin_col, seeds)
            errs.append(abs(np.exp(corr[0] - t.loc[j, 'resid']) - 1))
        scores[config] = np.mean(errs)
    return min(scores, key=scores.get), scores


# fit the chosen correction on every row of t (columns Re_L, x_fl, resid) and return a level 2 block.
# lin_cols: inputs of the linear form (mass transfer one input; hydraulics both). none -> None
def make_block(t, config, lin_cols, output, level1_constants, scores=None):
    form, layers, alpha = config
    if form == 'none':
        return None
    cols = lin_cols if form == 'linear' else ['Re_L', 'x_fl']
    names = ['Re_L' if c == 'Re_L' else 'x_flood' for c in cols]
    m = t[cols].mean()
    sd = t[cols].std().replace(0, 1)
    X = ((t[cols] - m) / sd).values
    y = t['resid'].values
    block = {'output': output, 'form': form, 'inputs': names,
             'scaling': {'mean': [float(v) for v in m], 'sd': [float(v) for v in sd]},
             'valid_range': {'Re_L': [float(t['Re_L'].min()), float(t['Re_L'].max())],
                             'x_flood': [float(t['x_fl'].min()), float(t['x_fl'].max())]},
             'level1_constants': {k: float(v) for k, v in level1_constants.items()},
             'trained_on_runs': int(len(t))}
    if scores:
        block['tuner_scores_mean_abs_frac'] = {str(k): float(v) for k, v in scores.items()}
    if form == 'linear':
        A = np.column_stack([np.ones(len(y)), X])
        coef = np.linalg.lstsq(A, y, rcond=None)[0]
        block['linear'] = {'intercept': float(coef[0]), 'coef': [float(c) for c in coef[1:]]}
    else:
        members = []
        for seed in (0, 1, 2):
            net = MLPRegressor(hidden_layer_sizes=layers, alpha=alpha, solver='lbfgs', random_state=seed, max_iter=2000)
            net.fit(X, y)
            members.append({'seed': seed, 'weights': [w.T.tolist() for w in net.coefs_],
                            'biases': [b.tolist() for b in net.intercepts_]})
            # our forward pass must equal sklearn's
            assert abs(forward(members[-1], X[:1])[0] - net.predict(X[:1])[0]) < 1e-10
        block['network'] = {'hidden_layers': list(layers), 'alpha': alpha, 'activation': 'relu',
                            'output_activation': 'identity', 'average_over': 'seeds', 'members': members}
    return block


# plain forward pass, as the dll does it: rows of weights[k] are the output nodes of layer k
def forward(member, X):
    out = []
    for x in X:
        h = np.array(x, dtype=float)
        n = len(member['weights'])
        for k in range(n):
            h = np.array(member['weights'][k]) @ h + np.array(member['biases'][k])
            if k < n - 1:
                h = np.maximum(h, 0)
        out.append(float(h[0]))
    return out


# evaluate a level 2 block at one point: the correction factor, or None outside the training range
def apply_block(block, inputs):
    for name, (lo, hi) in block['valid_range'].items():
        if inputs[name] < lo or inputs[name] > hi:
            return None
    x = np.array([inputs[n] for n in block['inputs']])
    z = (x - np.array(block['scaling']['mean'])) / np.array(block['scaling']['sd'])
    if block['form'] == 'linear':
        y = block['linear']['intercept'] + float(np.dot(block['linear']['coef'], z))
    else:
        y = float(np.mean([forward(mb, [z])[0] for mb in block['network']['members']]))
    return float(np.exp(y))
