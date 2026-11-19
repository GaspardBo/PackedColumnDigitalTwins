# Flexipac 350Y model walkthrough

My explanations from the 26 Oct walkthrough, tidied into numbered stages, with the extra detail needed to code each step. Quotes are my own words. **Code** bullets are what the notebook actually does and the catches to watch for.

**Label rule:** each number below goes on the first line of the matching notebook cell, e.g. `# 2.4 between two lines`. Sections 0–4 are in `flexipac.ipynb`. Sections 5–7 are only in `flexipac_answers.ipynb` so far (cells F5–F7).

---

## 0. Units and symbols

- **0.1 Gas flow rate, F (`F_Pa05`)**
  - F-factor = gas velocity × √(gas density).
  - Units √Pa = √(kg/(m·s²)). "05" means to the power 0.5, not 10⁵.
- **0.2 Gas velocity (`uG_ms`, `ug_ms` after averaging):** m/s. Billet takes this, not F. Convert with uG = F / √ρG.
- **0.3 Pressure drop (`dPdz_Pa_m`, `dpdz_Pa_m` after averaging):** Pa per metre of packing.
- **0.4 Liquid loading, uL (`uL_m3_m2h`)**
  - m³ of liquid per m² of column per hour (= m/h).
  - "u" for superficial velocity, not "mu". `mu_L` and `mu_G` in the Billet code are viscosities.
  - Billet needs m/s, so divide by 3600.
- **0.5 Liquid hold-up (`holdup_m3_m3`, `holdup` after averaging):** m³ of liquid per m³ of bed, i.e. a fraction. 0.05 means 5% of the bed is liquid.
- **0.6 Systems:** `air/water` and `air/Kerosol 200`. Kerosol is about 3× more viscous than water.

---

## 1. Data preparation

- **1.1 Load and filter**
  - "There's two data sets. One is gas flow rate against pressure drop at different liquid loadings. And the second is gas flow rate against liquid holdup at different liquid loadings."
  - Both come from the same runs: one table, two output columns.
  - **Code:** read `erasmus_flexipac350Y_hydraulics.csv`, keep `packing == 'Flexipac 350Y'` (drop HC for now).
- **1.2 Split off the special rows**
  - **Dry rows** (`uL == 0`): gas only. Not used for interpolation. Erasmus used them to fit Cp.
  - **Static rows** (`uG == 0`): liquid only, no gas. Not used.
  - **Main rows:** everything else.
- **1.3 Find the curves**
  - One curve = one `system` + one `uL_m3_m2h`.
  - **Code:** group by `uL_m3_m2h`, not `uL_block`. Air/water has two blocks at uL 28.8 and two at 12.9, and grouping by value merges them.
  - Sort each curve by `F_Pa05`. `np.interp` needs increasing x and does not check.
- **1.4 Average the repeats**
  - Rows with the same system, uL and F are repeats.
  - If their pressure drops agree within 10%, average them (dp, holdup, uG). That gives **`avg`, 107 points**, one per F per curve.
  - **Why:** duplicates double-weight a point and make leave-one-out look better than it is.
- **1.5 Keep the disagreeing rows aside**
  - 8 rows disagree by more than 10%. All are in the loading region. Saved to `results/flexipac350Y_loading_region_rows.csv`.
  - **Code:** `curve` = `avg` + those 8 rows. Interpolation and the tests use `avg`; the Billet check (4.7) uses `curve`.

---

## 2. Interpolation

- **2.1 The graph**
  - F on the x-axis, pressure drop on the y-axis, one line per liquid loading. Same for hold-up.
  - Pressure-drop lines curve upwards. Hold-up lines are flat, then rise sharply near flooding. Higher-uL lines stop at lower F because they flood sooner.
- **2.2 Inputs**
  - "The user will input a liquid loading value and they'll ask for the pressure drop at a specific gas flow rate… the only thing that's unknown is the pressure drop."
  - **Code:** `predict(system, uL, F, table)`. `table` is the data it is allowed to use, so the tests can hand it less data.
- **2.3 Reading along one line**
  - "Take the x value… draw a straight line up until it hits the specific line… translate horizontally across and read off the y value."
  - Each line is only about 9 dots, so even this is an interpolation between neighbouring dots.
  - **Code:** `np.interp(F, curve['F_Pa05'], curve['dpdz_Pa_m'])`, and the same for `holdup`.
- **2.4 Between two lines**
  - "If there is no specific liquid loading line… it will find how far away it is from the two neighbouring lines… and using the distance linearly, it scales the pressure drop… single interpolation or a double interpolation."
  - **Code:**
    - `low` = biggest measured uL ≤ input; `high` = smallest measured uL ≥ input.
    - Read dp on both lines (2.3), then `w = (uL − low)/(high − low)`, `dp = dp_low + w·(dp_high − dp_low)`.
    - If the uL is an exact match, `high == low`, so set `w = 0` to avoid dividing by zero.
- **2.5 Range check and refusal**
  - "If I ask it for a value higher than the highest flow rate that I have data for… you need to extrapolate… this model is merely an interpolation model… So it just says no answer."
  - **Code:**
    - uL must sit between the lowest and highest measured lines.
    - F must sit inside **both** neighbouring lines' F ranges. The shorter (higher-uL) line sets the cap.
    - Out of range → return `NaN, NaN, True`. Do the check before `np.interp`, which would otherwise clamp silently.
- **2.6 Outputs:** pressure drop, hold-up and the out-of-range flag.

---

## 3. Testing

- **3.1 Scoring a point**
  - Hide a measured point, predict it from the rest, compare.
  - **Code:**
    - `score(test, train)`: for each test row, call the method on `train`, then error % = |predicted − measured| / measured × 100.
    - `NaN` error = no answer. Count answers separately from the mean error.
- **3.2 Leave one out**
  - "We took eight points… and said interpolate to find the ninth point… where we already knew the answer… You can't take out the end point and then interpolate, because that's the extrapolation issue."
  - **Code:** loop over all 107 rows of `avg`, `train = avg.drop(i)`. The point is hidden from the whole dataset, not just its own line.
  - Interpolation answers 87/107. The 20 misses are mostly line end points.
- **3.3 Leave one curve out**
  - A whole uL line is hidden, which forces the between-lines step.
  - **Code:** group `avg` by `(system, uL)`, hide each group in turn.
  - Interpolation answers 38/107. The outer lines have no neighbour on one side, and each gap is capped where the shorter neighbour stops.
- **3.4 Water → Kerosol**
  - Train on water only, test on Kerosol: a new liquid.
  - **Code:** interpolation has no Kerosol curves, so the test rows are relabelled as `air/water` (it just reads water's curves).
- **3.5 Top F held out**
  - Train on F < 2, test on F ≥ 2: pure extrapolation.
  - Interpolation answers 0/58, which is correct behaviour, not a bug.
- **3.6 Error table:** tested, answered, mean dp error %, mean hold-up error %, one row per split.
- **3.7 One runner for every method** (answers notebook, F5.5–F5.6)
  - `splits` = a dict of `(train, test)` pairs for the four tests, built once so every method gets identical tests.
  - Every method is a function `method(train, test)` that returns `dp_pred` and `hold_pred` for each test row.
  - `run(method, pairs)` loops the pairs, then attaches true values and errors.
  - **Catch:** each method must be **fitted fresh on each `train`**, never on all of `avg`, or the test is leaked.

---

## 4. Billet

Physics, no measured data. Inputs go through four stages; out come one pressure drop and one hold-up, "same as what we had with the interpolation".

- **4.0 Constants and properties**
  - Packing: Cp 0.172, Clp 3.157, Cfp 2.464, Ch 0.482 (Erasmus Table 7.3); ap 350 m²/m³; ε 0.985; column 0.2 m.
  - Fluids (Erasmus Table 4.1): air ρG 1.15, μG 1.86e-5; water ρL 1000, μL 8.02e-4; Kerosol ρL 763, μL 2.31e-3.
  - **Code:** `props = {system: (rho_L, mu_L)}` so the function looks up the right liquid.
- **4.1 Stage 1: dry pressure drop**
  - "Gas will flow through the column, and due to the resistance it faces, there's going to be a pressure drop… affected by gas velocity, packing area to volume, void fraction and the fudge factor." Also gas density and viscosity, through the Reynolds number.
  - "This has nothing to do with the liquid holdup and it's not affected by liquid loading."
  - "Two density things: one is the void fraction, and the other is the surface area per volume."
  - **Code:**
    - Particle size `d = 6(1−ε)/ap`; wall factor `fs = 1/(1 + 4/(ap·dc))` (narrow column).
    - `Re_G = uG·d·ρG / ((1−ε)·μG) · fs`.
    - Friction `ξ = Cp·(64/Re_G + 1.8/Re_G^0.08)`: the 64 term matters at slow, smooth flow, the 1.8 term at fast flow.
    - `ΔP_dry = ξ · (ap/ε³) · (ρG·uG²/2) · fs`.
- **4.2 Stage 2: loading and flood points**
  - "Step two gives the loading point and the flood point… then we will know which zone we're in, and in each of those zones a different equation applies."
  - These are two **gas speeds**. Loading = gas starts holding liquid back. Flood = liquid can't drain, the column chokes.
  - **Code:**
    - Flow parameter `ψ = (uL·ρL)/(uG·ρG) · √(ρG/ρL)`. More liquid relative to gas → earlier flooding.
    - Exponents switch at ψ = 0.4.
    - Loading speed from Clp, flood speed from Cfp (gas drag balanced against gravity on the film).
    - `uG > uG_flood` → return `NaN, NaN` (no answer).
- **4.3 Stage 3: hold-up, one equation per zone**
  - **4.3.1 Calm zone (below loading):** "The liquid properties dominate… not going to involve anything about the gas. The gas velocity also doesn't matter."
    - Film thickness × wetted area. Thicker film with more liquid flow, more viscous liquid, more surface.
  - **4.3.2 Loading zone (between loading and flood):** "What fraction of the way you are to flooding… to the power of 13."
    - h = calm + (max − calm) × (uG / uG_flood)¹³. A power law: flat, flat, then sharply up.
    - At 50% of flood speed you get almost none of the extra; at 90%, about a quarter; at 100%, all of it.
  - **4.3.3 Flood zone:** no answer (NaN, not 0).
  - **Code catch: the function computes two hold-ups.**
    - `ht_dp`, used inside the pressure drop: calm = pure film, max = Billet's flood hold-up.
    - `holdup`, the output compared with data: calm = film × (wetted fraction)^(2/3), with wetted fraction from Ch (formula switches at Re_L = 5); max = 2.2 × calm.
- **4.4 Stage 4: wet pressure drop**
  - Dry × a liquid factor. The liquid steals open space, so the gas squeezes harder.
  - **Code:**
    - `W = exp(Re_L/200) · (ht_dp/ht_calm)^0.3`, which grows once hold-up climbs.
    - The code multiplies by `(ε/(ε−h))³`, but the wet friction factor also contains `((ε−h)/ε)^1.5`, so the **net effect is `ΔP_wet = ΔP_dry · (ε/(ε−h))^1.5 · W`**.
    - Example: h = 0.09 → (0.985/0.895)^1.5 ≈ 1.15, so +15% from lost space, then × W.
- **4.5 The function**
  - `billet(system, uL, uG_s)` takes uL in m³/(m²h) and uG in m/s, and returns `(dp, holdup)`.
  - **Catch:** it takes gas velocity, not F. Pass `row['ug_ms']`.
- **4.6 Sanity check:** compare a few points with Billet's lines in Erasmus Figs 4.12–4.15, read off by eye.
- **4.7 Run on every point**
  - Billet on all of `curve`, error % with sign (minus = Billet too low), summary by system and F band (below 2 / 2 and above).
  - Billet answers only **73/107**. Its flood point comes too early in this version.

### 4.8 Billet equations

| Stage | Gives | Depends on | Equation (as coded) |
|---|---|---|---|
| 4.1 Dry ΔP | ΔP_dry | uG, ρG, μG, ap, ε, Cp, dc | Re = uG·d·ρG/((1−ε)μG)·fs; ξ = Cp(64/Re + 1.8/Re^0.08); ΔP_dry = ξ·(ap/ε³)·(ρG·uG²/2)·fs |
| 4.2 Loading / flood | uG_load, uG_flood | uL, uG (via ψ), ρL, ρG, μL, μG, ap, ε, Clp, Cfp | drag vs gravity on the film; uG > uG_flood → NaN |
| 4.3.1 Calm hold-up | h_calm | uL, μL, ρL, ap, Ch | film = (12·μL·ap²·uL/(ρL·g))^(1/3); output hold-up = film × (Ch·Re_L^0.15·Fr_L^0.1)^(2/3) |
| 4.3.2 Loading hold-up | h | h_calm, uG/uG_flood | h = h_calm + (h_max − h_calm)·(uG/uG_flood)^13 |
| 4.4 Wet ΔP | ΔP_wet | ΔP_dry, ht_dp, ε, W | ΔP_wet = ΔP_dry·(ε/(ε−h))^1.5·W; W = e^(Re_L/200)·(ht_dp/ht_calm)^0.3 |

---

## 5. Billet + correction

- **5.1 Why**
  - "We started with physics… Billet spits out an answer for liquid holdup and pressure drop. This answer is okay, not bad, but not great. It requires a correction."
- **5.2 Error table**
  - "Input point as one column, the Billet number in the second, the actual data point in the third, and then there's an error in the fourth… calculated for every single point."
  - The error is a ratio, logged: `r = log(measured / Billet)`. 1.3 means Billet was 30% low.
  - **Code:** only rows where Billet answered (73). Drop the NaNs before fitting.
- **5.3 The rule**
  - "The error comes from three things. One, a baseline value a. Plus two, a flow-related error b × F. And three, a loading-related error c × uL."
  - log(measured / Billet) ≈ a + b·F + c·uL. An assumption: the error is linear in F and uL.
- **5.4 Fitting a, b, c**
  - "On a 3D plot… it finds a plane…" The plane passes as close as possible to the 73 points, not through them, and fits F and uL together.
  - **Code:**
    - `X` = columns `[1, F, uL]`. The column of ones is what gives a.
    - `np.linalg.lstsq(X, r)` returns `[a, b, c]`.
    - Fit separately for dp and for hold-up (two sets of a, b, c).
- **5.5 Using it**
  - "We take e to the power of this error and multiply it by the Billet value."
  - **Code:** `dp = billet_dp · exp(a + b·F + c·uL)`. The result is a better estimate, not the exact measured value.
- **5.6 As functions**
  - `fit_correction(train)` → coefficients; `corrected(...)` → corrected dp and hold-up; `correction_method(train, test)` plugs into the runner (3.7).
  - **Catch:** refit on every `train`, never on all of `avg`.
- **5.7 Limits**
  - The straight line is a local approximation: good near where it was fitted, drifting further away from it.
  - Extending it past the data took top-F dp error from 15% (Billet alone) to 35%.
  - There's no viscosity term, so water → Kerosol hold-up gets worse (27% → 35%).
  - Leave-one-out is still about 14%, so even inside the data the plane only partly captures the pattern.
- **5.8 To try**
  - Plot `r` against F and against uL first, and pick the shape from what you see.
  - Try `log F` (bends gently, flattens out) and add liquid viscosity as a third input.
  - Avoid squared and cubic terms: better inside, worse at the edges.
  - Freeze the correction past the data edge and let Billet carry the shape. Run top-F with and without the freeze.

---

## 6. Neural network

- **6.1 Idea**
  - "The same principle of saying we're going to fit it to data, but now using a neural network rather than having any physics underpinning it… the best way of encapsulating all of this information using a curved surface."
  - No Billet underneath: it predicts pressure drop and hold-up directly, and trains on all 107 points.
- **6.2 Inputs and outputs**
  - **Code:**
    - `nn_inputs`: columns `[F, uL, μL]`, with μL looked up from `props` by system.
    - `nn_targets`: `[log dp, log holdup]`. Logs keep predictions positive and errors proportional. Undo with `exp` after predicting.
- **6.3 Shape**
  - A curved surface built from straight pieces joined at bends (ReLU).
  - **Code:** `MLPRegressor(hidden_layer_sizes=(10, 10))`. 3 inputs → 10 → 10 → 2 outputs = **172 knobs** (weights and biases), against about 107 training points.
- **6.4 Training**
  - "Adjusting all of these 172 knobs, go slightly up, go slightly down, until the overall error across the entire system goes down."
  - **Code:**
    - `make_pipeline(StandardScaler(), MLPRegressor(...))`. The scaler puts F (1–4), uL (6–36) and μL (0.0008–0.0023) on the same scale. Without it the network barely notices μL.
    - `solver='lbfgs'`: a smarter version of "turn the knobs downhill" that also uses the slope's curvature. Good for small datasets.
    - `max_iter=5000`, `random_state=0` so results repeat.
    - `nn_method(train, test)` trains a fresh network on each `train`. Leave-one-out trains 107 networks, so it takes a minute.
- **6.5 Beyond the data**
  - "The neural network cannot be [trusted] beyond the data points… we'd go into a space where we have no idea whether the network is doing the right thing."
  - It never says no answer: past the edge the last straight piece just keeps going. Top-F dp error 24%.
  - **Water → Kerosol catch:** in training μL is always water's value, so the scaler maps it to a constant and the network learns nothing about viscosity. Its Kerosol answers are water's curves.
- **6.6 Capping**
  - Keep it uncapped in the comparison table (showing it fail is the result).
  - Cap or flag it in the final tool. Cap = the same range check as interpolation (2.5), reused.

---

## 7. Results

- **7.1 Every method through the same runner**
  - `methods = {interpolation, billet, billet + correction, neural network}`, each run on every split.
  - **Code:** `billet_method` ignores `train` (it learns nothing). `interp_method` falls back to the train system's curves for a new liquid.
- **7.2 The table**
  - Errors two ways: on what each method answered, and on the **common** points every method answered. Compare on the common columns.
  - Top-F has no common points (interpolation answers 0), so compare Billet, correction and network on Billet's 29 answered points.
- **7.3 Save:** `results/flexipac350Y_results_table.csv`.
- **7.4 Plots**
  - Measured dp against F for two air/water curves, with all four methods overlaid.
  - Predicted against measured (leave-one-out), one panel per method. On the diagonal = perfect.

### 7.5 Results so far

From `results/flexipac350Y_results_table.csv`, regenerated 27 Oct by running `flexipac_answers.ipynb` top to bottom. Every number matches the 22 Oct run. One rounding fix: water → Kerosol hold-up for the correction is 35.1, not 35.2 (the CSV holds 35.15, which rounds down).

Each cell is **mean absolute error %, dp / hold-up**, with the number of points answered in brackets.

**As answered** (each method on the points it could answer, so not directly comparable):

| Split (points tested) | Interpolation | Billet | Billet + correction | Neural network |
|---|---|---|---|---|
| Leave one out (107) | 7.5 / 4.0 (87) | 23.1 / 27.8 (73) | 13.7 / 16.9 (73) | 8.5 / 5.3 (107) |
| Leave one curve out (107) | 8.3 / 3.9 (38) | 23.1 / 27.8 (73) | 13.8 / 18.9 (73) | 15.8 / 10.5 (107) |
| Water → Kerosol (41) | 27.5 / 17.1 (37) | 30.3 / 26.7 (18) | 21.1 / 35.1 (18) | 30.1 / 18.4 (41) |
| Top F held out (58) | — (0) | 15.0 / 26.1 (29) | 35.2 / 24.8 (29) | 24.1 / 19.1 (58) |

**Common points only** (the points every method answered — this is the fair comparison):

| Split (common points) | Interpolation | Billet | Billet + correction | Neural network |
|---|---|---|---|---|
| Leave one out (62) | 6.7 / 4.0 | 23.2 / 27.5 | 12.6 / 17.6 | 7.1 / 4.8 |
| Leave one curve out (37) | 7.4 / 3.6 | 24.4 / 28.1 | 15.3 / 16.4 | 7.4 / 9.2 |
| Water → Kerosol (18) | 11.5 / 8.8 | 30.3 / 26.7 | 21.1 / 35.1 | 12.0 / 9.1 |
| Top F held out (0) | — | — | — | — |
| Top F, on Billet's 29 points (answer key 8.5) | — (0) | 15.0 / 26.1 | 35.2 / 24.8 | 24.3 / 18.4 |

Read with three caveats: Billet answers only 73/107 (to-do 2), the correction has no viscosity term, and the network never refuses, so its top-F number includes blind extrapolation.

---

## 8. To do

1. **Run the line-range check:** `avg.groupby(['system', 'uL_m3_m2h'])['F_Pa05'].agg(['min', 'max'])`, to size the blank high-F, high-uL corner.
2. **Check the Billet flood point** against Erasmus Appendix B, Table B.3 (B3.10–B3.15): it comes too early (73/107 answered).
   - Dry and wet pressure drop (B3.16–B3.22) already match the code, including the x = 1.5 exponent.
   - One thing to check in the original Billet & Schultes paper: Erasmus (and the code) multiply dry ΔP by the wall factor `fs`. I believe the original divides by it, which would make ΔP about 11% higher.
3. **Improve the correction** (5.8), then rerun all four splits.
4. **Run the capped vs uncapped top-F test** for the correction and the network.
5. **Compare on common points** (7.2).
6. **Add these labels to the first line of each notebook cell.**
7. **Build the interpolation demo UI:** data table, graph with one line per uL, inputs for F and uL that draw the line up to the curve and across to the pressure drop.
