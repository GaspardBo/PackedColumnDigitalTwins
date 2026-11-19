# packlib and the two new packing notebooks, explained slowly

`packing-twin/packlib.py`, `superring03/superring03.ipynb` and `rsp250y/rsp250y.ipynb`, read against `flexipac/flexipac.ipynb`. Written 17 Nov 2026.

The saved copies of both new notebooks carry no outputs, so this walkthrough quotes no results for the new packings. Worked numbers below are either taken from the decisions log or worked out from the data files with the notebooks' own formulas, and each one says which.

## 1. What packlib is and why it exists

By 16 Nov there were two notebooks (350Y and 350Y HC) holding near-identical copies of the Billet model, the constant fitting, the four tests and Level 2. A fix made in one copy didn't reach the other: the missing hold-up fix was only in the HC copy. With more packings coming, that would only get worse.

So D47 moved all the model code into one file, `packlib.py`. Every packing notebook now does three things only:

1. Load its data and rename the columns to the standard names the library expects.
2. Fill in a settings dict `s` with that packing's geometry, fluid properties and Level 0 constants.
3. Call the library.

D47 also says the library had to reproduce the 350Y and HC notebook results before it was used on RSP 250Y. D46's log entry records that rerun: "Rerun with `../packlib.py` after D45 ... 350Y unchanged at 10.5 / 15.4."

The equations themselves are the ones you built and checked in sections 4 and 5 of `flexipac.ipynb` (Billet and Schultes, 1995). Almost nothing about the physics changed. What changed is *where the numbers come from*: your notebook read globals like `ap`, `eps` and `props`; the library reads them from `s`.

## 2. Where each function came from

I compared every function with the matching cell line by line.

| packlib function | flexipac.ipynb cell (section) | What changed |
|---|---|---|
| `g = 9.81` | cell 20 (4.0) | Nothing. |
| `make_settings` | cell 20 (4.0), the globals | **New.** Packs the cell 20 globals into one dict. |
| `row_settings` | none | **New** (D49). Per-row fluid properties. |
| `curve_keys` | `groupby(['system', 'uL_m3_m2h'])` in cells 42, 43; `curve_name` in cell 47 | **New** as a function (D50). Same grouping unless a `curve_id` column exists. |
| `log_error` | cell 31 (5.1) | Identical. |
| `billet` | cell 25 (4.5, "Billet function") | Takes `s`; reads geometry, gas properties, `props`, `rho_W`, `mu_W` from it. `C` no longer has a default. Equations identical. |
| `dry_dp` | cell 30 (5.0) | Takes `s`. `C` no default. Equations identical. |
| `loading_uG` | cell 34 (5.3) | Takes `s`. Equations identical. |
| `flood_uG` | cell 36 (5.4) | Takes `s`. Equations identical. |
| `billet_rows` | cell 35 (5.3) | Takes `s`, passes `row_settings(s, row)` to `billet`. |
| `billet_lib` | cell 38 (5.5) | Same change as `billet_rows`. |
| `fit_constants` | cell 41 (5.8), using `cp_score` (cell 32), the Ch score (cell 35) and `cfp_score` (cell 37) | Cp fitted on `s['dry']` as supplied (the notebook drops the lowest-F row, D55), with `s_dry` for per-row gas properties. Level 0 start from `s['C0']`. Scores written inside the function. `row_settings` in the loading and flood loops. |
| `physics` | cell 44 (5.11b) | A missing liquid borrows the **first trained liquid**, not `'air/water'`. Returns `(predictions, lib)`, not just predictions. |
| `make_splits` | cell 43 (5.11a) | Uses `curve_keys`. Liquid-transfer test only if `transfer` is given, and its name is built from the liquids. `top_frac` is a setting (default 0.25, same as before). |
| `features` | cell 48 (6.1) | Takes `s`, uses `row_settings`. |
| `tuned_configs` | none | **New** (D46). |
| `correction` | cell 49 (6.2), the linear and network branches | **New** as a function. Linear branch identical; network branch takes `alpha` and averages seeds. |
| `level1_parts` | cell 49 (6.2), its first half | **New** as a function. Same steps. |
| `level2` | cell 49 (6.2) | Calls `level1_parts` and `correction`. Missing-measurement mask. New `'tuned'` branch. |
| `pick_config` | none | **New** (D46). |
| `run_tests` | cells 45 (5.11c) and 50 (6.3) | **New** as a function. Runs any list of models; `skip_level2` replaces the hard-coded water-to-Kerosol skip. |
| `results_table` | cells 46 (5.11d) and 51 (6.4) | **New** as a function. Same maths. |
| `spread_table` | cells 47 (5.11e) and 52 (6.5) | **New** as a function. Same maths; base model is an argument. |

One small check on the network: cell 49 doesn't set `alpha`, so scikit-learn's default of 0.0001 applies. packlib's `'network'` form passes `alpha` 0.0001 explicitly, so it's the same network.

## 3. The new pieces, line by line

### 3.1 The settings dict `s` and `make_settings`

In your notebook, `billet` could see `ap`, `eps`, `dc`, `rho_G` and so on because cell 20 made them globals. That works for one packing. Once a second packing is loaded in the same Python session, the globals can only hold one packing at a time. So the library passes everything in a single dict.

```python
def make_settings(ap, eps, dc, rho_G, mu_G, props, C0, dry, rho_W=1000, mu_W=8.02e-4):
```
A function that takes every number the equations need. `rho_W=1000, mu_W=8.02e-4` are defaults: if a notebook doesn't pass them, these are used. They're the reference water values from your cell 20, so 350Y behaves exactly as before.

```python
    return {'ap': ap, 'eps': eps, 'dc': dc, 'rho_G': rho_G, 'mu_G': mu_G, 'props': props,
            'C0': C0, 'dry': dry, 'rho_W': rho_W, 'mu_W': mu_W}
```
Put them all in a dict, with the names as keys. That's all it does. It's a function rather than a typed-out dict so a missing setting gives an error straight away (Python complains about a missing argument).

What each key holds:
- `ap`, `eps`, `dc`: specific area (m²/m³), void fraction, column diameter (m).
- `rho_G`, `mu_G`: gas density and viscosity.
- `props`: `{system: (rho_L, mu_L)}`, same shape as your cell 20 `props`.
- `C0`: Level 0 constants `{'Cp', 'Clp', 'Cfp', 'Ch'}`, your `C_erasumus`.
- `dry`: the dry rows Cp is fitted on.
- `rho_W`, `mu_W`: reference water in Billet's flood hold-up term.

Then inside every function, a line like this replaces the globals:

```python
    ap, eps, dc, rho_G, mu_G = s['ap'], s['eps'], s['dc'], s['rho_G'], s['mu_G']
```
Unpack the dict into local names, so the equation lines below can stay word for word as you wrote them.

### 3.2 `row_settings`: per-row fluid properties (D49)

In Erasmus's data every row is at one temperature, so one set of properties fits all. Wang's rows aren't: on RSP 250Y the water runs about 10 to 17 °C and the air 1 to 23 °C, and water viscosity changes about 14% between 15 and 20 °C (D49). So each row now carries its own `rho_L`, `mu_L`, `rho_G`, `mu_G` columns.

```python
def row_settings(s, row):
    if 'mu_L' not in row:
        return s
```
`row` is one row of the table (a pandas Series). For a Series, `in` checks the column labels, not the values. So this asks: "does this table have a `mu_L` column?" If it doesn't (Flexipac), hand back `s` untouched. That's why packlib gives the same answers as your notebook on 350Y.

```python
    s2 = dict(s)
```
Make a **copy** of the settings. This matters. If we wrote the row's values straight into `s`, the next row would start from the last row's properties, and the fallback values would be lost for good. `dict(s)` makes a new dict with the same contents, so changes to `s2` don't touch `s`.

```python
    s2['rho_G'] = row['rho_G']
    s2['mu_G'] = row['mu_G']
    s2['props'] = {row['system']: (row['rho_L'], row['mu_L'])}
    return s2
```
Overwrite the gas properties with this row's, and replace `props` with a one-entry dict holding this row's liquid. `billet` then does `s['props'][system]` exactly as before and gets this row's values.

Worked example, first wet Super-Ring row (Wang Table C.1: liquid 30.43 °C, air in 41.5 °C, air out 41.79 °C, F 0.38). Using the notebook's formulas (section 5 below), I get ρ_L 995.52 kg/m³, μ_L 7.900e-4 Pa s, ρ_G 1.1213 kg/m³, μ_G 1.915e-5 Pa s. So `row_settings` hands `billet` a copy of `s` with:

```
rho_G 1.1213, mu_G 1.915e-5, props {'air/water': (995.52, 7.900e-4)}
```

### 3.3 `curve_keys`: which rows form one curve (D50)

Your notebook made curves with `groupby(['system', 'uL_m3_m2h'])`. That works when every row on a curve has exactly the same liquid load. In Wang's data the load drifts within a run: on RSP 250Y the lowest run has seven different values, 12.19 to 12.28 (counted from `wang2015_tableC1.csv`). Grouping by exact load would split one curve into seven fragments, and "leave one curve out" would hold out a fragment, not a curve.

```python
def curve_keys(rows):
    if 'curve_id' in rows.columns:
        return rows['curve_id']
```
If the notebook has made a `curve_id` column, use it as the curve label for each row.

```python
    return rows['system'] + ' ' + rows['uL_m3_m2h'].astype(str)
```
Otherwise build the label from system and load, e.g. `'air/water 28.8'`. `.astype(str)` turns the number into text so it can be joined. This is exactly your `curve_name` line from cell 47.

It returns one label per row (a Series). Then `curve.groupby(curve_keys(curve))` groups rows with the same label. Grouping by one label column puts rows together the same way as grouping by the two columns did, so Flexipac's curves are unchanged.

Billet still uses each row's own `uL_m3_m2h`. The curve label only decides which rows are held out together (D50).

### 3.4 The `s_dry` handling in `fit_constants`

```python
    dry = s['dry']
    base = dict(s['C0'])
```
Take the dry rows and a copy of the Level 0 constants as the starting set. In cell 41 these were `dry.drop(dry['F_Pa05'].idxmin())` and `C_erasumus`. The outlier drop has moved into the notebook (D55 makes it the rule for every packing), so the library fits on whatever rows it's given.

```python
    s_dry = dict(s)
    if 'mu_G' in dry.columns:
        s_dry['rho_G'] = dry['rho_G'].values
        s_dry['mu_G'] = dry['mu_G'].values
```
Copy the settings again. If the dry rows carry their own gas properties, replace the single `rho_G` and `mu_G` with **arrays**, one value per dry row.

Why not `row_settings`? Because `dry_dp` doesn't go row by row. It takes the whole column of gas velocities at once (cell 30: "works on a whole column of gas velocities"). numpy does arithmetic element by element when arrays are the same length, so in

`Re_G = uG_s * d_part * rho_G / ((1 - eps) * mu_G) * fs`

row 1's `uG_s` is paired with row 1's `rho_G`, row 2's with row 2's, and so on. Passing arrays inside `s_dry` gets per-row properties without a loop. Only the gas properties change: dry rows have no liquid.

This matters on Super-Ring: the dry-row air goes from about 39 °C in to as much as 60 °C in (Wang Table C.1), so ρ_G changes by several per cent across the dry rows.

```python
    def score_cp(Cp):
        return log_error(dry_dp(dry['uG_ms'].values, {'Cp': Cp}, s_dry), dry['dpdz_Pa_m'].values)
    base['Cp'] = minimize_scalar(score_cp, bounds=(0.01, 1.0), method='bounded').x
```
Same as your `cp_score` and cell 41's Cp line, written inside the function. `{'Cp': Cp}` works because `dry_dp` only reads `C['Cp']`. Note the column is `dpdz_Pa_m` (lower-case p): the notebook renames Wang's `dPdz_Pa_m` before the split.

The rest of `fit_constants` is cell 41 with `row_settings(s, r)` added to the loading and flood loops, and `cfp_score` written inline as `score_cfp`.

### 3.5 The missing-measurement mask in `level2`

```python
        ok = p_train[col].notna().values & train[true].notna().values
```
In cell 49 this line was only `p_train[col].notna().values`: keep rows where Level 1 gave an answer. Now there's a second test: keep rows where the measurement exists too. `&` is "and", element by element.

Worked example with four training rows:
- Level 1 answered: `[True, True, False, True]` (row 3 is past Level 1's flood point).
- Measured hold-up exists: `[True, True, True, False]` (row 4's hold-up was set to missing, as D51 does on RSP 250Y).
- `ok`: `[True, True, False, False]`. Only rows 1 and 2 are learnt from.

Why it's needed: y = log(measured / Level 1). A missing measurement makes y `nan` for that row. I checked what happens then: `np.linalg.lstsq` returns `[nan nan]` for every coefficient, so every Level 2 prediction becomes `nan`, and `MLPRegressor.fit` refuses outright with "Input y contains NaN." One bad row would wipe out the whole correction. On Flexipac every row has a hold-up, so this never mattered there. This is the "missing hold-up fix" D47 mentions.

The same mask appears in `pick_config` for the same reason.

### 3.6 `physics` borrowing the first trained liquid

```python
def physics(train, test, outputs, s):
    lib = fit_constants(train, outputs, s)
    first = list(lib)[0]
```
Fit Level 1 on the training rows. `list(lib)` gives the dict's keys in the order they were added, which is the order `train['system'].unique()` met them. `[0]` takes the first.

```python
    for system in test['system'].unique():
        if system not in lib:
            lib[system] = lib[first]
    return billet_lib(test, lib, s), lib
```
Any test liquid with no constants borrows the first trained liquid's. Cell 44 wrote `lib['air/water']`, which would crash on a packing with no water. The new packings are water-only, so in practice this line never fires on them.

The function now returns two things, the predictions and the constants, because `level1_parts` needs the constants to build Level 2 inputs. `run_tests` takes only the first with `[0]`.

### 3.7 `tuned_configs`: the candidate corrections (D46)

```python
tuned_configs = [('linear', None, None)] + [('network', h, a) for h in [(10, 10), (5,), (3,)] for a in [0.1, 1.0, 10.0]]
```
A list of candidate corrections, each a tuple `(form, layers, alpha)`.
- The first is the linear form. It has no layers or alpha, so `None`.
- The rest come from a list comprehension, which is a loop written in one line. For each layer shape `h` and each `a`, make `('network', h, a)`. Three shapes times three alphas is nine networks.

So ten candidates in all (I counted by running the line):

| Layers | alpha 0.1 | alpha 1.0 | alpha 10.0 |
|---|---|---|---|
| (10, 10): two layers of 10 | yes | yes | yes |
| (5,): one layer of 5 | yes | yes | yes |
| (3,): one layer of 3 | yes | yes | yes |

plus linear. `(5,)` has a trailing comma because a one-item tuple needs one in Python; `(5)` would just be the number 5.

`alpha` is the network's penalty on large weights. A bigger alpha forces a smoother, flatter correction, which is the way to stop a network learning noise. Your 6.2 network used the default, 0.0001, and D22 records that it overfitted hold-up. D46's grid offers smaller networks and much stronger penalties.

### 3.8 `correction`: one correction, either form

```python
def correction(config, X_train, y, X_test, seeds=(0,)):
    form, layers, alpha = config
```
Take one candidate from the list and unpack its three parts. `seeds=(0,)` is a default: one seed, 0, as in your 6.2.

```python
    if form == 'linear':
        A = np.column_stack([np.ones(len(y)), X_train])
        coef = np.linalg.lstsq(A, y, rcond=None)[0]
        return coef[0] + X_test @ coef[1:]
```
Your 6.2 linear branch, unchanged apart from `len(y)` in place of `ok.sum()`. They're the same number, because `y` was already cut down to the `ok` rows. y = a + b × (scaled Re_L) + c × (scaled x), three numbers fitted by least squares.

```python
    preds = []
    for seed in seeds:
        net = MLPRegressor(hidden_layer_sizes=layers, alpha=alpha, solver='lbfgs', random_state=seed, max_iter=2000)
        net.fit(X_train, y)
        preds.append(net.predict(X_test))
    return np.mean(preds, axis=0)
```
The network branch. For each seed, build a network with this candidate's layers and alpha, fit it, predict the test rows. Then average the predictions across seeds. `axis=0` means "average down the list", giving one number per test row.

Why several seeds: a network starts from random weights, and on about 100 rows two different starts can land on noticeably different corrections. Averaging three (D46: "3 seeds averaged") smooths out that luck. With one seed, the answer is whatever that start happened to give.

### 3.9 `level1_parts`: Level 1 plus the scaled inputs

```python
def level1_parts(train, test, s):
    p_test, lib = physics(train, test, ['hold'], s)
    p_train = billet_lib(train, lib, s)
    f_train = features(train, lib, s)
    f_test = features(test, lib, s)
    mean = f_train.mean()
    sd = f_train.std()
    return p_train, p_test, ((f_train - mean) / sd).values, ((f_test - mean) / sd).values
```
This is the first half of your cell 49, cut out so it can be called from two places (`level2` and `pick_config`).
- Fit Level 1 on `train` with Cfp on hold-up only (D30's headline), and predict `test`.
- Predict `train` too: the correction learns from training residuals.
- Work out (Re_L, x) for both.
- Standardise with the **training** mean and sd (D23), and return four things.

### 3.10 `level2` and its `'tuned'` branch

```python
def level2(train, test, form, s):
    p_train, p_test, X_train, X_test = level1_parts(train, test, s)
    out = pd.DataFrame(index=test.index, columns=['dp', 'hold'], dtype=float)
    for col, true in [('dp', 'dpdz_Pa_m'), ('hold', 'holdup')]:
        # skip rows where level 1 floods or the measurement is missing
        ok = p_train[col].notna().values & train[true].notna().values
        y = np.log(train[true].values[ok] / p_train[col].values[ok])
```
Same as 6.2 up to the target, with the new mask (3.5).

```python
        if form == 'linear':
            corr = correction(('linear', None, None), X_train[ok], y, X_test)
        elif form == 'network':
            corr = correction(('network', (10, 10), 0.0001), X_train[ok], y, X_test)
```
Your two fixed forms, now one call each. `'network'` is exactly the 6.2 network: (10, 10), alpha 0.0001, seed 0.

```python
        else:
            best = pick_config(train, col, true, s)
            seeds = (0,) if best[0] == 'linear' else (0, 1, 2)
            corr = correction(best, X_train[ok], y, X_test, seeds)
```
**The tuned form.** Ask `pick_config` which of the ten candidates works best, using `train` only. Then fit that candidate on all of `train` and predict `test`. Three seeds if it's a network, one if it's linear (linear has no randomness, so more seeds would just repeat the same answer).

The choice is made separately for `'dp'` and `'hold'`: `pick_config` is called inside the loop over `col`. So ΔP might get a (5,) network and hold-up the linear form (D46: "separately for ΔP and hold-up").

```python
        out[col] = p_test[col].values * np.exp(corr)
    return out
```
Level 2 = Level 1 × exp(correction), unchanged.

### 3.11 `pick_config`: choosing without looking at the test rows (D46)

The problem it solves: there are ten candidates. Something has to pick one. The pick must not use the test rows.

```python
def pick_config(train, col, true, s):
    errors = {config: [] for config in tuned_configs}
```
A dict with one empty list per candidate. The tuples themselves are the keys (tuples can be dict keys because they can't be changed).

```python
    for name, val in train.groupby(curve_keys(train)):
        inner = train.drop(val.index)
```
**Leave one training curve out, inside the training set.** Go through the training curves one at a time. `val` is the held-out training curve (the "validation" curve); `inner` is every other training row.

```python
        p_train, p_val, X_train, X_val = level1_parts(inner, val, s)
        ok = p_train[col].notna().values & inner[true].notna().values
        y = np.log(inner[true].values[ok] / p_train[col].values[ok])
```
Build Level 1 and the scaled inputs on `inner` only, and the target, exactly as `level2` does. Level 1 is refitted here too, so the held-out curve doesn't leak into Level 1 either.

```python
        for config in tuned_configs:
            seeds = (0,) if config[0] == 'linear' else (0, 1, 2)
            pred = p_val[col].values * np.exp(correction(config, X_train[ok], y, X_val, seeds))
            errors[config].append(100 * np.abs(pred - val[true].values) / val[true].values)
```
Try every candidate: fit on `inner`, predict the held-out curve, store the absolute % error for each of its rows.

```python
    score = {config: np.nanmean(np.concatenate(errors[config])) for config in tuned_configs}
    return min(score, key=score.get)
```
For each candidate, join its errors from all the held-out curves into one array and take the mean. `nanmean` skips rows with no answer (Level 1 flooded) or no measurement. `min(score, key=score.get)` returns the candidate with the lowest mean error.

**How it nests, with numbers.** Super-Ring has 6 curves (counted from the data with the notebook's nominal-load rule). Under leave one curve out:
- Outer fold 1 holds out curve A for the real test. The other 5 curves are `train`.
- Inside, `pick_config` holds out each of those 5 in turn. Curve A is never touched.
- For each output that's 5 inner folds × 10 candidates, and each network candidate is 3 fits. Counting from the code: 5 × (1 + 9 × 3) = 140 correction fits per output, 280 per outer fold, so about 1,700 over the 6 outer folds, plus 66 calls to `fit_constants`. That's why cell 11 runs the tuned form on leave one curve out only.

**Why not just pick the candidate that scores best on the test rows?** Because then the score you report is no longer a test score. Each candidate's test error is part real skill, part luck on those particular rows. Take the best of ten, and you're partly picking the one that got lucky. The reported number then looks better than the model will do on new conditions. D46 records this on 350Y HC: settings chosen by looking at test scores gave 15.9 / 15.2 (ΔP / hold-up %), while the honest nested procedure gave 18.5 / 15.5. The 15.9 is excluded because "that score includes selection luck and does not predict error on new conditions" (D46). Choosing inside the training set means the test curve plays no part in the choice, so its error is a fair estimate.

### 3.12 `run_tests`: every split, every model

This replaces cells 45 and 50, where the model list and the water-to-Kerosol skip were typed in.

```python
def run_tests(splits, s, models, skip_level2=()):
    level0 = {}
    tests = {}
```
`models` is a list of names from `'L0'`, `'L1'`, `'L2 linear'`, `'L2 network'`, `'L2 tuned'`. `skip_level2` is a list of split names where Level 2 shouldn't run (on Flexipac, the liquid-transfer test, D22). The default `()` is empty: skip nothing.

```python
    for split, pairs in splits.items():
        preds = {}
        for model in models:
            if model.startswith('L2') and split in skip_level2:
                continue
```
For each test and each model. Any Level 2 model on a skipped split is jumped over.

```python
            parts = []
            for train, test in pairs:
                if model == 'L0':
                    for system in test['system'].unique():
                        level0[system] = s['C0']
                    parts.append(billet_lib(test, level0, s))
```
Level 0: every liquid gets the published constants. Nothing is fitted, so `train` isn't used.

```python
                elif model == 'L1':
                    parts.append(physics(train, test, ['hold'], s)[0])
```
Level 1, the headline version (Cfp on hold-up only, D30). `[0]` keeps the predictions and drops the constants.

```python
                else:
                    parts.append(level2(train, test, model.split(' ')[1], s))
```
Level 2. `'L2 tuned'.split(' ')` gives `['L2', 'tuned']`, and `[1]` is `'tuned'`, which is the `form` `level2` wants.

```python
            preds[model] = pd.concat(parts)
        tests[split] = preds
        print(split, 'done')
    return tests
```
Glue the folds together, as in 5.11c. The result has the same shape as your `tests`: `tests[split][model]` is a table of predictions. Labels are short (`'L1'`) where yours were long (`'Level 1, Cfp on hold-up only'`).

### 3.13 `results_table`

This is cell 51 written as a function, with the model list taken from whatever is in `tests`.

```python
        labels = list(preds)
        idx = preds[labels[0]].index
        common = idx
        for label in labels:
            common = common[preds[label].loc[common, 'dp'].notna().values]
```
All models that were run on this split, the test rows (taken from the first model; every model predicts the same rows), and the common points every model answered. The rest is the 6.4 table: tested, answered, common, ΔP and hold-up mean absolute error and bias.

### 3.14 `spread_table`

This is cells 47 and 52 written as one function. The only real difference: the model you compare against is now an argument.

```python
def spread_table(tests, curve, base, models):
    curve_name = curve_keys(curve)
```
Curve labels for the per-curve averages. On the new packings these are the `curve_id`s, so a curve is averaged as one unit even though its loads differ slightly.

```python
        if base not in preds:
            continue
```
Skip a split that doesn't have the base model.

```python
                err1 = (100 * (preds[label].loc[common, out] - curve.loc[common, col]) / curve.loc[common, col]).abs()
                err0 = (100 * (preds[base].loc[common, out] - curve.loc[common, col]) / curve.loc[common, col]).abs()
                per_curve = (err1 - err0).groupby(curve_name[common]).mean()
```
Exactly 6.5: absolute error of the model minus absolute error of the base, averaged per curve. Negative means the model helped (D34). Calling it with `base='L0', models=['L1']` gives 5.11e; with `base='L1'` and the Level 2 names gives 6.5.

## 4. Functions that are essentially unchanged

- **`log_error`** (5.1): identical, character for character.
- **`billet`** (4.5): same equations in the same order. Takes `C` and `s` with no defaults. Reads `ap`, `eps`, `dc`, `rho_G`, `mu_G` and `props` from `s`, and the reference water as `s['rho_W']`, `s['mu_W']` instead of globals. The comment on the dry ΔP line (D13a) has moved to the section heading.
- **`dry_dp`** (5.0): same equation; takes `s`.
- **`loading_uG`** (5.3) and **`flood_uG`** (5.4): same equations; take `s`. They don't call `row_settings` themselves. Whoever calls them passes `row_settings(s, r)`.
- **`billet_rows`** (5.3) and **`billet_lib`** (5.5): same loop, but each row goes through `row_settings(s, row)` first.
- **`fit_constants`** (5.8): covered in 3.4. The calm-zone Ch fit, the Cfp floor (`× 1.000001`) and the bounds (0.01–1.0, 0.05–3.0, floor–10) are unchanged.
- **`make_splits`** (5.11a): same four tests, built with `curve_keys`. The liquid-transfer split appears only when `transfer=(a, b)` is passed, and it's named `'train ' + a + ', test ' + b`. On Flexipac that's `'train air/water, test air/Kerosol 200'`, not your `'train water, test kerosol'`. Top F uses `quantile(1 - top_frac)`, which with the default 0.25 is your `quantile(0.75)`. For example, a curve with F values 1 to 8 gives a cut of 6.25 (pandas' default straight-line interpolation), so F 7 and 8 are held out.
- **`features`** (6.1): same two inputs. Re_L uses the row's own ρ_L and μ_L via `row_settings`, and x uses the row's own properties in `flood_uG`.

## 5. superring03.ipynb, cell by cell

Raschig Super-Ring #0.3 is a random (dumped) packing, so a different family from Flexipac's structured sheets (cell 0). Data: Wang (2015) Table C.1, SRP column, 0.427 m, air/water.

### Cell 0 (markdown)
States the packing, the data source, that all model code lives in `packlib.py` (D47), and the decisions used: D49, D53, D54, D55.

### Cell 1 (1.1): load the library and the data

```python
import sys
sys.path.insert(0, '..')
```
`packlib.py` sits one folder up from the notebook. `sys.path` is the list of folders Python searches on `import`. Inserting `'..'` (the parent folder) at the front means `import packlib` finds it.

```python
df = pd.read_csv('../srp_data/wang2015_tableC1.csv')
data = df[df['packing'] == 'RSR#0.3'].copy()
```
Wang's table holds several packings. Keep the Super-Ring rows. `.copy()` for the same reason as your 1.1: we're about to add columns. There are 63 rows: 13 dry and 50 wet (counted from the CSV).

### Cell 2 (1.2): water properties per row (D49)

```python
water = pd.read_csv('../srp_data/properties/nist_water_0_40C.csv')
data['rho_L'] = np.interp(data['Tliq_in_C'], water['T_C'], water['rho_kg_m3'])
data['mu_L'] = np.interp(data['Tliq_in_C'], water['T_C'], water['mu_Pa_s'])
```
The NIST Chemistry WebBook table of liquid water at 0.101325 MPa, 0 to 40 °C in 1 °C steps (`srp_data/properties/README.md`). For each row, `np.interp` reads density and viscosity at that row's liquid inlet temperature, drawing a straight line between the two table rows either side. Same `np.interp` you used in 2.3, now along temperature instead of F.

The 0–40 °C table is used here (RSP uses 0–30 °C) because Super-Ring's liquid runs from 23.8 to 31.9 °C (from the CSV).

Worked example, liquid at 30.43 °C. The table rows either side are 30.01 °C (995.65 kg/m³, 7.97e-4 Pa s) and 31.01 °C (995.34, 7.80e-4). 30.43 is 42% of the way from 30.01 to 31.01, so ρ_L = 995.65 − 0.42 × 0.31 ≈ 995.52 kg/m³ and μ_L ≈ 7.90e-4 Pa s.

### Cell 3 (1.3): air properties per row (D53)

```python
P = 101325        # Pa, assumed
M_air = 0.0289644 # kg/mol, dry air (iso 2533 standard atmosphere)
R = 8.314462      # J/mol/K
```
Wang gives no column pressure, so 101.325 kPa is assumed (D53 (ii)). D53 notes Austin is about 150 m up, so the real pressure is roughly 1.8% lower, which moves u_G by about 0.9%. Dry-air molar mass from ISO 2533, as the comment cites. Humidity is ignored: D53 labels "air treated as dry" a hypothesis.

```python
T_gas = (data['Tair_in_C'] + data['Tair_out_C']) / 2 + 273.15
```
The gas temperature is the mean of air in and air out, in kelvin (D53 (i)): the best estimate of the air inside the bed. In and out differ by up to about 2% in density (D53, for RSP 250Y).

```python
data['rho_G'] = P * M_air / (R * T_gas)
```
Ideal gas: ρ = PM / (RT). Worked: at 20 °C (293.15 K), 101325 × 0.0289644 / (8.314462 × 293.15) = 1.204 kg/m³. For the first wet row (air 41.5 in, 41.79 out, mean 314.80 K) it's 1.1213 kg/m³.

```python
data['mu_G'] = 1.716e-5 * (T_gas / 273.15) ** 1.5 * (273.15 + 110.4) / (T_gas + 110.4)
```
Sutherland's law with White's constants (*Viscous Fluid Flow*, Table 1-2, per the comment):

μ = μ₀ (T / T₀)^1.5 × (T₀ + S) / (T + S), with μ₀ = 1.716e-5 Pa s, T₀ = 273.15 K, S = 110.4 K.

At T = T₀ the two brackets are both 1, so μ = μ₀ exactly. At 293.15 K it gives 1.813e-5 Pa s; at 314.80 K, 1.915e-5 Pa s. Compare Erasmus's 1.86e-5 in your cell 20.

### Cell 4 (1.4): the standard columns

```python
data['system'] = 'air/water'
data['F_Pa05'] = data['FG_Pa05']
data['uG_ms'] = data['F_Pa05'] / np.sqrt(data['rho_G'])
data['dpdz_Pa_m'] = data['dPdz_Pa_m']
data['holdup'] = data['holdup_frac']
```
Rename Wang's columns to the names packlib expects (see the top of `packlib.py`). The one calculation: Wang gives the gas load factor F = u_G √ρ_G, so u_G = F / √ρ_G, as in your 4.1 (`uG_s = 1.9 / np.sqrt(rho_G)`). The difference is that each row uses its own ρ_G. Worked: first wet row, 0.38 / √1.1213 = 0.359 m/s.

```python
dry = data[data['L_m3_m2h'] == 0].copy()
dry_fit = dry.drop(dry['F_Pa05'].idxmin())
wet = data[data['L_m3_m2h'] > 0].copy()
```
Split dry and wet rows (your 1.2). `dry_fit` drops the lowest-F dry row, F 0.33, under D55: one rule for every packing. D55 says that on Super-Ring this changes Cp from 0.552 to 0.545. It's dropped for consistency, not because it looks wrong. `idxmin` is the same trick you used in 5.2.

```python
wet.loc[wet['holdup'] <= 0, 'holdup'] = np.nan
```
Any wet row reporting zero hold-up becomes missing. The comment says it's the D51 rule and none are expected; I counted zero in the CSV. It's there so the notebook stays safe if the data changes.

### Cell 5 (1.5): curves by nominal load

```python
nominal = np.array([12.2, 24.5, 36.6, 48.9, 61.2, 73.4])
wet['uL_m3_m2h'] = wet['L_m3_m2h']
wet['curve_load'] = [nominal[np.abs(nominal - L).argmin()] for L in wet['L_m3_m2h']]
```
The six nominal loads (D54 (2)). For each row, `np.abs(nominal - L)` is the distance to each nominal load, `argmin()` is the position of the smallest, and `nominal[...]` reads off that load. Worked: L = 12.23 gives distances [0.03, 12.27, ...], so the row joins the 12.2 curve. `uL_m3_m2h` keeps the row's own load for Billet.

```python
curve = wet[['system', 'curve_load', 'uL_m3_m2h', 'F_Pa05', 'uG_ms', 'dpdz_Pa_m', 'holdup',
             'rho_L', 'mu_L', 'rho_G', 'mu_G']].copy()
curve = curve.sort_values(['curve_load', 'F_Pa05']).reset_index(drop=True)
```
Keep the columns the library uses, sort along each curve, renumber 0, 1, 2... as in your 1.3. The four property columns are what makes `row_settings` switch on.

```python
curve['curve_id'] = curve['system'] + ' ' + curve['curve_load'].astype(str)
```
The curve label `curve_keys` looks for, e.g. `'air/water 12.2'`. Counted from the data: 6 curves of 8 or 9 rows, 50 rows in all.

### Cell 6 (2.1): settings

```python
C_bs = {'Cp': 0.760, 'Clp': 3.560, 'Cfp': 2.340, 'Ch': 0.750}
```
Level 0 constants from Billet and Schultes (1999), Table 2a, Raschig Super-Ring metal 0.3 (D54 (1)): C_P,0 0.760, C_S 3.560 (our Clp), C_Fl 2.340 (our Cfp), C_h 0.750. D54 records a caveat (hypothesis): the 1999 paper rescales C_S and C_Fl by a viscosity group and uses a different wet ΔP term, while our model is the 1995 book form with the same tabulated constants.

```python
s = packlib.make_settings(ap=315, eps=0.96, dc=0.427,
                          rho_G=curve['rho_G'].mean(), mu_G=curve['mu_G'].mean(),
                          props={'air/water': (curve['rho_L'].mean(), curve['mu_L'].mean())},
                          C0=C_bs, dry=dry_fit, rho_W=998.21, mu_W=1.0014e-3)
```
- Geometry: a 315 m²/m³, ε 0.96 from Wang Table 5.2, matching Billet and Schultes (1999) Table 2a (cell comment, D54). Column 0.427 m.
- `rho_G`, `mu_G`, `props`: means over the rows. They're fallbacks only, because every row carries its own (D49).
- `dry=dry_fit`: Cp is fitted on 12 dry rows.
- `rho_W=998.21, mu_W=1.0014e-3`: reference water in Billet's flood hold-up term, NIST at 20 °C (D53 (iii)). You can see the same numbers in the 20.01 °C row of the NIST table. On Flexipac this was Erasmus's 1000 and 8.02e-4.

### Cell 7 (2.2): Level 0 on every row

```python
pred0 = packlib.billet_rows(curve, C_bs, s)
```
Billet with the published constants on every wet row, each with its own properties. The next lines are your 4.7 summary: how many answered, mean absolute error and bias.

```python
s_dry = dict(s)
s_dry['rho_G'] = dry['rho_G'].values
s_dry['mu_G'] = dry['mu_G'].values
dry_err = 100 * (packlib.dry_dp(dry['uG_ms'].values, C_bs, s_dry) - dry['dpdz_Pa_m'].values) / dry['dpdz_Pa_m'].values
```
The same per-row trick as inside `fit_constants` (3.4), done by hand to check the published Cp on the dry rows. This uses **all 13** dry rows (`dry`, not `dry_fit`), so it's a Level 0 check, not a fit. D54 records the result: published Cp over-predicts SRP dry ΔP by 38%.

### Cell 8 (2.3): Level 1 on every row

```python
lib = packlib.fit_constants(curve, ['hold'], s)
```
Your 5.8 check: fit on all rows, print the constants. D54 records Cp 0.5445.

### Cell 9 (3.1): the tests

```python
splits = packlib.make_splits(curve)
```
No `transfer`, because there's only one liquid. So three tests: leave one point out (50 folds), leave one curve out (6 folds, by `curve_id`), top F.

### Cell 10 (3.2): Level 0, Level 1, two fixed Level 2s

```python
tests = packlib.run_tests(splits, s, ['L0', 'L1', 'L2 linear', 'L2 network'])
```
Your 5.11c and 6.3 in one call.

### Cell 11 (3.3): the tuned Level 2 on leave one curve out only

```python
tuned = packlib.run_tests({'leave one curve out': splits['leave one curve out']}, s, ['L2 tuned'])
tests['leave one curve out']['L2 tuned'] = tuned['leave one curve out']['L2 tuned']
```
Run the tuned form on one split only (it refits many times per fold, see the count in 3.11), then slot its predictions into the main `tests` dict next to the others.

### Cell 12 (3.4) and cell 13 (3.5): the tables

```python
results = packlib.results_table(tests, curve)
```
Errors on common points, as 6.4.

```python
print(packlib.spread_table(tests, curve, 'L0', ['L1']).round(1))
packlib.spread_table(tests, curve, 'L1', ['L2 linear', 'L2 network', 'L2 tuned']).round(1)
```
Spread (D34) twice: Level 1 against Level 0 (your 5.11e), then each Level 2 against Level 1 (your 6.5).

## 6. How rsp250y.ipynb differs

The two notebooks are the same apart from these points.

- **Packing and data.** Raschig Super-Pak 250Y, structured, from the same Wang (2015) Table C.1 and column. 14 dry rows and 74 wet rows (counted from the CSV).
- **Water table.** `nist_water_0_30C.csv`, because the RSP liquid stays below 30 °C. The README says its 0–30 °C rows are identical to the 40 °C file.
- **Level 0 constants (D48).** `C_rejl = {'Cp': 0.260, 'Clp': 3.89, 'Cfp': 2.67, 'Ch': 0.941}` from Rejl et al. (2015), *Chem. Eng. Res. Des.* 99, 20–27, Table 3, fitted on a 0.29 m column with air/water. C_lp = C_S and C_fp = C_Fl. The plan was the 2014 AIChE paper, with Mellapak 250Y as the fallback; neither was needed. D48 labels it a hypothesis that Rejl divided by the wall factor as we do (D13a).
- **Geometry.** a 250 m²/m³, ε 0.98: Wang Table 5.1 (a), the Raschig data sheet and Rejl Table 2.
- **The zero hold-up row (D51).** L 73.36, F 2.46, ΔP 1671 Pa/m, hold-up 0.00 (I found it in the CSV). Zero hold-up in a flooding column is physically impossible. The same `wet.loc[wet['holdup'] <= 0, 'holdup'] = np.nan` line keeps its ΔP and makes its hold-up missing. Here it does fire, once. The mask in 3.5 then keeps it out of Level 2's hold-up target, and `nanmean` keeps it out of `pick_config`'s score. In the Cfp fit, `log_error` gives a missing measurement the fixed penalty of 1 whatever Cfp is, so it doesn't move the fitted Cfp.
- **Nominal loads and the 46.76 row (D50).** `nominal = np.array([12.2, 24.4, 36.7, 48.9, 61.1, 73.3])`. One row has L 46.76. Its distances are 10.06 to 36.7 and 2.14 to 48.9, so the `argmin` rule puts it on the 48.9 curve, as D50 records.
- **Cp rows.** The lowest-F dry row is F 0.48. D52 first kept all 14 rows; D55 superseded it, and Cp moves from 0.211 to 0.207.

## 7. Ten likely viva questions

**1. Why move the code into a library at all?**
Two notebooks held copies of the same model, and a fix made in one (the missing hold-up mask) didn't reach the other (D47). With one file, every packing runs exactly the same code, so differences in results come from the data, not from drift between copies. The library was checked to reproduce the Flexipac results before use on new packings (D47, D46).

**2. How does the library give your old Flexipac answers if it now supports per-row properties?**
`row_settings` checks whether the row has a `mu_L` column. Flexipac's `curve` doesn't, so it returns `s` unchanged and every function uses the single set from cell 20. `make_settings` defaults `rho_W` and `mu_W` to Erasmus's 1000 and 8.02e-4, the cell 20 values.

**3. Why does `row_settings` copy the dict with `dict(s)`?**
If it wrote a row's properties into `s` itself, the next row would see them, and the fallback values would be lost. The copy changes only what this one row sees. Note `dict(s)` is a shallow copy, but the function replaces whole entries (`props` gets a new dict), so the original is never edited.

**4. Why group curves by a `curve_id` and not by liquid load?**
In Wang's runs the load drifts slightly within one curve: seven different values from 12.19 to 12.28 on RSP 250Y's lowest run. Grouping by exact load would split a curve into fragments, and leave one curve out wouldn't hold out a real curve. The label only sets which rows are held out together; Billet still uses each row's own load (D50).

**5. Why are the air properties evaluated at the mean of inlet and outlet temperature, and what's assumed?**
The mean is the best estimate of the air inside the bed (D53). Density uses the ideal gas law for dry air at an assumed 101.325 kPa, since Wang gives no pressure; the real pressure is about 1.8% lower, a 0.9% effect on u_G (D53). Viscosity uses Sutherland's law with White's constants.

**6. What does the `ok` mask in `level2` protect against?**
A row where Level 1 floods, or where the measurement is missing, gives a `nan` target. A single `nan` makes least squares return `nan` coefficients, and scikit-learn refuses to fit at all. The mask drops those rows so one missing reading (like the D51 row) can't wipe out the correction.

**7. What does the tuned Level 2 choose, and how?**
It picks one of ten corrections: the linear form or a network with (10, 10), (5,) or (3,) neurons and alpha 0.1, 1 or 10, separately for ΔP and hold-up (D46). Inside the training set, it holds out each training curve in turn, fits every candidate on the rest, and keeps the one with the lowest mean absolute % error on the held-out curves. The test curve plays no part.

**8. Why not choose the settings by looking at the test results?**
Picking the best of ten test scores partly picks the luckiest, so the reported score is lower than what the model will do on new conditions. On 350Y HC the test-picked settings scored 15.9 / 15.2 against 18.5 / 15.5 for the honest nested choice (D46). The test set has to stay out of every decision, or its error stops being a fair estimate.

**9. Why average three seeds for the networks?**
A network starts from random weights, and with about 50 to 100 rows different starts can give noticeably different corrections. Averaging three (D46) reduces that dependence on one lucky or unlucky start. The linear form has no randomness, so it uses one.

**10. Why drop the lowest-F dry row on every packing, even where it isn't an outlier?**
D55 sets one rule for all packings instead of a judgement call per packing. On 350Y the F 0.84 row was a clear outlier; on the others it's dropped for consistency. The effect is small: Cp changes by 3% or less and Level 2 results by under 1.5 points (D55).

## Sources

- Billet, R. and Schultes, M. (1995) model equations, as implemented in `flexipac.ipynb` sections 4 and 5.
- Billet, R. and Schultes, M. (1999) *Trans IChemE* 77A, Table 2a: Super-Ring #0.3 constants and geometry (D54).
- Rejl et al. (2015) *Chem. Eng. Res. Des.* 99, 20–27, Tables 2 and 3: RSP 250Y constants and geometry (D48).
- Wang (2015) Tables C.1, 5.1 (a) and 5.2: SRP data and geometry, `srp_data/wang2015_tableC1.csv`.
- NIST Chemistry WebBook, SRD 69, liquid water at 0.101325 MPa (`srp_data/properties/README.md`).
- ISO 2533 (dry-air molar mass) and White, *Viscous Fluid Flow*, Table 1-2 (Sutherland constants), as cited in the notebook comments. I haven't checked these two against the originals.
- Decisions log `flexipac/docs/2026-11-11_level1_methodology_notes.md`: D13a, D22, D23, D24, D30, D31, D34, D45 to D55.
- Worked numbers marked "from the CSV" or "counted" were computed from the data files with the notebooks' own formulas on 17 Nov.
