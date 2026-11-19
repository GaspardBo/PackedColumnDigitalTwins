# Level 2 (notebook 6.1 to 6.5), explained slowly

Flexipac 350Y, `flexipac/flexipac.ipynb`, cells 48 to 52. Written 15 Nov 2026.

## 1. The idea in one paragraph

Level 1 is Billet's physics with constants fitted to the packing. It's still wrong, and wrong in a pattern: on Flexipac 350Y it predicts pressure drop about 36% too low, and more so near flooding. Level 2 doesn't touch the physics. It looks at the user's own measurements, works out how far off Level 1 was at each operating point, learns that pattern from two numbers that describe the operating point, and then multiplies Level 1's answer by a correction factor. So:

**Level 2 answer = Level 1 answer × correction factor**

The correction factor is learnt from data. Physics does the heavy lifting; the data patches what's left.

## 2. What the correction actually learns

For each training row we know two things: what was measured, and what Level 1 predicted. The quantity the correction learns is:

**y = log(measured / Level 1)**

A worked example. Measured ΔP is 100 Pa/m and Level 1 says 64 Pa/m.
- measured / Level 1 = 100 / 64 = 1.5625. Level 1 needs multiplying by 1.5625.
- y = log(1.5625) = 0.446.
- Later, to use it: exp(0.446) = 1.5625, and 64 × 1.5625 = 100.

Why the log and not the ratio itself:
- "Twice too big" (ratio 0.5) and "half too small" (ratio 2) become −0.69 and +0.69. They're the same size, just opposite signs, so the fit treats over- and under-prediction equally.
- If y = 0, the ratio is 1 and Level 1 was exactly right. So "no correction" sits at zero, which is where a straight-line fit naturally starts.
- exp() of anything is positive, so the corrected prediction can never come out negative.

This is the same log error used to fit the constants in 5.1, so the whole notebook measures "wrong" the same way.

## 3. The two inputs (cell 48, 6.1)

The correction needs to know *where* on the operating map a row is. We describe that with two dimensionless numbers (D22):

1. **Re_L, the liquid Reynolds number** = u_L ρ_L / (a μ_L). It says how much liquid is flowing, relative to how sticky it is. On 350Y it runs from about 1.6 (Kerosol, lowest load) to about 35 (water, highest load).
2. **x = u_G / u_G,flood**, the gas velocity as a fraction of the velocity at which Level 1 says the column floods. 0.3 means well clear of flooding; 0.95 means right on the edge. Level 1's error grows as x rises, so this is the input that matters most for ΔP.

Both are dimensionless, so they mean the same thing in a 0.2 m rig or a 2 m plant column. That's why we picked them over raw flow rates.

The code, line by line:

```python
def features(rows, lib):
```
A function that takes some rows of data and a set of constants (`lib`, one set per liquid) and returns the two inputs for every row.

```python
    results = []
    for i in rows.index:
        row = rows.loc[i]
```
Make an empty list, then go through the rows one at a time. `row` is one operating point.

```python
        rho_L, mu_L = props[row['system']]
        uL_s = row['uL_m3_m2h'] / 3600
        Re_L = uL_s * rho_L / (ap * mu_L)
```
Look up that liquid's density and viscosity (from cell 20), convert the liquid load from m³/m²h to m/s, and compute Re_L. This is the same Re_L Billet uses in the hold-up equation (cell 25).

```python
        x = row['uG_ms'] / flood_uG(row['system'], row['uL_m3_m2h'], row['uG_ms'], lib[row['system']])
```
Divide the measured gas velocity by the flood velocity that Level 1 predicts at those flows. `flood_uG` is the function from 5.4. The flood velocity uses the *fitted* Cfp from `lib`, so x is measured against Level 1's own idea of flooding.

```python
        results.append([Re_L, x])
    return pd.DataFrame(results, columns=['Re_L', 'x'], index=rows.index)
```
Store the pair, and at the end turn the list into a table with the same row labels (`index`) as the input. Keeping the same index matters: it lets every later step line each row up with its measurement.

Expected output: nothing printed. Cell 48 only defines the function.

## 4. The Level 2 function (cell 49, 6.2)

```python
from sklearn.neural_network import MLPRegressor
```
Load scikit-learn's small neural network. Only the network version uses it.

```python
def level2(train, test, form):
```
`train` = rows we're allowed to learn from. `test` = rows we predict, never seen during fitting. `form` = `'linear'` or `'network'`, which version of the correction to use.

```python
    lib = fit_constants(train, ['hold'])
```
**Step 1: build Level 1 on the training rows only.** This is the 5.8 function: Cp from dry rows, Ch from calm-zone hold-up, then Cfp fitted on hold-up only (the headline choice, D30). Doing this inside the function means the test rows never leak into Level 1 either.

```python
    for system in test['system'].unique():
        if system not in lib:
            lib[system] = lib['air/water']
```
If the test rows contain a liquid that wasn't in training, borrow water's constants. In practice this never triggers in section 6, because the water-to-Kerosol split is skipped (see 6.3). It's kept as a safety net.

```python
    p_train = billet_lib(train, lib)
    p_test = billet_lib(test, lib)
```
Level 1's predictions (ΔP and hold-up) on the training rows and on the test rows.

```python
    f_train = features(train, lib)
    f_test = features(test, lib)
```
The two inputs (Re_L, x) for both sets of rows.

```python
    mean = f_train.mean()
    sd = f_train.std()
    X_train = ((f_train - mean) / sd).values
    X_test = ((f_test - mean) / sd).values
```
**Standardising (D23).** Re_L runs 1.6 to 35; x runs 0.3 to 1. A network learns badly when one input is about 30 times bigger than the other. So each input is shifted by its mean and divided by its standard deviation, which puts both on roughly −2 to +2. For example, if training Re_L has mean 15 and sd 10, a row with Re_L 25 becomes (25 − 15) / 10 = 1.0.

The important detail: the **test rows use the training mean and sd**. In real use you don't have the future data yet, so you can't use it to scale things. `.values` turns the table into a plain array of numbers, which is what both fitting methods want.

```python
    out = pd.DataFrame(index=test.index, columns=['dp', 'hold'], dtype=float)
```
An empty table to fill with the Level 2 answers for the test rows.

```python
    for col, true in [('dp', 'dpdz_Pa_m'), ('hold', 'holdup')]:
```
Do everything twice: once for pressure drop, once for hold-up. Each output gets its own correction, because Level 1 is wrong in different ways for each.

```python
        ok = p_train[col].notna().values
```
A True/False list: True where Level 1 actually gave an answer. Billet returns nothing (`nan`) past its flood point, and you can't learn from a missing number. With Cfp fitted on hold-up only, Level 1 answers every 350Y row, so this rarely removes anything.

```python
        y = np.log(train[true].values[ok] / p_train[col].values[ok])
```
**The target**, from section 2: log(measured / Level 1) for every usable training row.

```python
        if form == 'linear':
            A = np.column_stack([np.ones(ok.sum()), X_train[ok]])
            coef = np.linalg.lstsq(A, y, rcond=None)[0]
            corr = coef[0] + X_test @ coef[1:]
```
**The linear correction.** It assumes:

y = a + b × (scaled Re_L) + c × (scaled x)

- `A` is a table with three columns: a column of 1s (it multiplies a), scaled Re_L (multiplies b) and scaled x (multiplies c).
- `np.linalg.lstsq` is least squares: it picks a, b and c so the sum of (fitted y − true y)² is as small as possible. `[0]` takes the answer out of what it returns. `coef` is then `[a, b, c]`.
- `corr` is the predicted y for each test row: `coef[0]` is a, and `X_test @ coef[1:]` is b × Re_L + c × x for each row (`@` is matrix multiplication; here it's just "multiply and add up across the row").

Three numbers in total. That's why it's hard to overfit.

```python
        else:
            net = MLPRegressor(hidden_layer_sizes=(10, 10), solver='lbfgs', random_state=0, max_iter=2000)
            net.fit(X_train[ok], y)
            corr = net.predict(X_test)
```
**The network correction.** Same inputs, same target, but now the shape isn't fixed to a flat plane; it can bend.
- `hidden_layer_sizes=(10, 10)`: two layers of 10 neurons each. That's about 150 numbers to fit, against about 100 training rows.
- `solver='lbfgs'`: a fitting method that works well on small data sets.
- `random_state=0`: networks start from random numbers. Fixing the seed means you get the same result every run.
- `max_iter=2000`: allow up to 2000 fitting steps.
- `.fit` learns; `.predict` gives y for the test rows.

```python
        out[col] = p_test[col].values * np.exp(corr)
    return out
```
**Apply it.** exp(corr) turns the predicted log ratio back into a multiplier, and Level 1's test prediction is multiplied by it. That's the Level 2 answer.

Expected output: nothing printed. Cell 49 only defines the function.

## 5. Running it on every test (cell 50, 6.3)

```python
L2a = 'Level 2, linear correction'
L2b = 'Level 2, network correction'
```
Labels, so the table rows read in plain words.

```python
for split, pairs in splits_c.items():
```
Loop over the four tests built in 5.11a. Each test is a list of (train, test) pairs, called folds. Leave one curve out has 10 folds, one per curve, and leave one point out has 115.

```python
    if split == 'train water, test kerosol':
        continue
```
**Skip the water-to-Kerosol test.** Level 2 is always trained on the user's own column running their own liquid. Nobody would train it on water and use it on Kerosol, so the test doesn't describe a real use (D22). `continue` jumps to the next test.

```python
    preds = tests[split]
    preds[L2a] = []
    preds[L2b] = []
```
`tests[split]` already holds Level 0 and Level 1 predictions from 5.11. This adds two empty lists for the Level 2 versions, so all four models end up side by side in one place.

```python
    for train, test in pairs:
        preds[L2a].append(level2(train, test, 'linear'))
        preds[L2b].append(level2(train, test, 'network'))
```
For every fold: build Level 2 on that fold's training rows and predict its test rows, once linear, once network.

```python
    preds[L2a] = pd.concat(preds[L2a])
    preds[L2b] = pd.concat(preds[L2b])
    print(split, 'done')
```
Glue the fold-by-fold pieces into one table per model. Each row was a test row exactly once, so the result covers every row.

Expected output:
```
leave one point out done
leave one curve out done
top F test done
```
Takes a few minutes: leave one point out refits everything 115 times.

## 6. The results table (cell 51, 6.4)

```python
show = [L0, H, L2a, L2b]
```
The four models in the table: Level 0, headline Level 1, and the two Level 2s.

```python
for split, preds in tests.items():
    labels = [label for label in show if label in preds]
```
For each test, keep only the models that were run. Water-to-Kerosol has no Level 2, so its rows show Level 0 and Level 1 only.

```python
    idx = preds[L0].index
    common = idx
    for label in labels:
        common = common[preds[label].loc[common, 'dp'].notna().values]
```
**Common points.** Start with every test row, then remove any row that any model failed to answer. Level 0 floods early and answers only 75 of 115, so all models are scored on those same 75 rows. That's the only fair comparison: a model can't score well by declining hard points.

```python
    for label in labels:
        pred = preds[label]
        dp_err = 100 * (pred['dp'] - curve.loc[idx, 'dpdz_Pa_m']) / curve.loc[idx, 'dpdz_Pa_m']
        hold_err = 100 * (pred['hold'] - curve.loc[idx, 'holdup']) / curve.loc[idx, 'holdup']
```
Percentage error per row, with its sign kept: (predicted − measured) / measured × 100. Negative means the model predicted too low.

```python
        rows_out.append([split, label, len(idx), pred['dp'].notna().sum(), len(common),
                         dp_err[common].abs().mean(), dp_err[common].mean(),
                         hold_err[common].abs().mean(), hold_err[common].mean()])
```
One table row per model and test:
- `tested`: how many rows were in the test.
- `answered`: how many this model gave an answer for.
- `common`: how many every model answered.
- `dp_abs_%`: mean of the error with the sign dropped, so "how far off on average". This is the accuracy number.
- `dp_bias_%`: mean with the sign kept, so "too high or too low on average". Near 0 means no lean either way.
- Same two for hold-up.

### What it showed (350Y, run 15 Nov)

ΔP and hold-up mean absolute error %, on common points:

| Test | Level 0 | Level 1 | Level 2 linear | Level 2 network |
|---|---|---|---|---|
| Leave one point out (75 common) | 22.7 / 27.3 | 35.8 / 15.6 | 15.2 / 10.2 | 7.7 / 15.0 |
| Leave one curve out (75 common) | 22.7 / 27.3 | 35.8 / 18.6 | 15.6 / 11.6 | 11.5 / 25.4 |
| Water to Kerosol (18 common) | 30.3 / 26.7 | 35.0 / 23.7 | not run | not run |
| Top F (4 common) | 6.5 / 36.1 | 50.9 / 37.3 | 10.0 / 34.3 | 15.2 / 36.4 |

How to read it:
- **Level 2 linear cuts Level 1's ΔP error by more than half** on unseen curves (35.8 to 15.6), and its bias drops from −35.8% to +7.7%. That's the correction doing its job: it learnt that Level 1 runs low.
- **The network wins on ΔP but loses on hold-up**: 25.4% on unseen curves, worse than Level 1 itself. About 150 numbers fitted on about 100 rows is too many, so it learns noise. That's why linear was chosen (D22).
- **Top F** rests on 4 common points, so it's reported as coverage, not accuracy.

## 7. Is the improvement real? (cell 52, 6.5, brief)

A mean can hide that the model got better on some curves and worse on others. 6.5 works out, curve by curve, Level 2's error minus Level 1's error. Then it reports the mean, sd, min and max over the 10 curves. Negative means Level 2 helped (D34).

Leave one curve out, linear correction:
- **ΔP: −20.3 ± 7.2, worst curve −9.7.** Every single curve got better. That's a solid result.
- **Hold-up: −2.8 ± 13.5, range −30.9 to +11.8.** On average a bit better, but some curves got worse and the spread is bigger than the gain. Honest reading: Level 2's hold-up gain isn't consistent yet on 350Y. Level 1's hold-up was already fairly good (fitted Ch), so there's less left to fix.

## 8. Same pipeline on Flexipac 350Y HC (test 4a, scratch run 15 Nov)

For comparison, leave one curve out on HC (54 common points of 112):

| | Level 0 | Level 1 | Level 2 linear |
|---|---|---|---|
| ΔP abs % | 35.9 | 31.0 | 11.1 |
| Hold-up abs % | 34.6 | 19.9 | 13.6 |

On HC, Level 1 beats Level 0 on both outputs. The early-flood cancellation that flattered Level 0 on 350Y doesn't happen here.

## Sources
- Billet, R. and Schultes, M. (1995) model equations, as implemented in notebook sections 4 and 5.
- Erasmus, A.B. (2004) PhD thesis, Stellenbosch: Flexipac 350Y and 350Y HC data, Tables 4.1, 4.2 and 7.3.
- Method choices: decisions log `flexipac/docs/2026-11-11_level1_methodology_notes.md`, D22, D23, D24, D30, D34.
