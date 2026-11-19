# Billet model: the equations behind sections 4.1 to 4.5

Every equation in the `billet` function, written out, in the order the code runs them. Each one sits next to the code line it matches.

**Source:** Erasmus, A. B. (2004), *Mass transfer in structured packing*, PhD thesis, University of Stellenbosch.
- **Equations:** Table B.3, pp. 220 to 223 (B3.x numbers), and Section 4.2.3, pp. 58 to 60.
- **Constants:** Table 7.3, p. 171.
- **Fluid properties:** Table 4.1, p. 74.

Where the notes only give a range of equation numbers, the range is given here too. Open in VS Code with **Ctrl+Shift+V** to see the maths rendered.

## Symbols

| Symbol | Code | Meaning | Value or unit |
|---|---|---|---|
| $u_L$ | `uL_s` | superficial liquid velocity | m/s, = uL in m³/(m²h) / 3600 |
| $u_G$ | `uG_s` | superficial gas velocity | m/s, = $F/\sqrt{\rho_G}$ |
| $\rho_G, \mu_G$ | `rho_G`, `mu_G` | air density, viscosity | 1.15 kg/m³, 1.86e-5 Pa s |
| $\rho_L, \mu_L$ | `rho_L`, `mu_L` | liquid density, viscosity | water 1000, 8.02e-4; Kerosol 763, 2.31e-3 |
| $\rho_W, \mu_W$ | `rho_W`, `mu_W` | water, as the reference liquid | 1000, 8.02e-4 |
| $a_p$ | `ap` | specific area | 350 m²/m³ (hypothesis, from the packing name) |
| $\varepsilon$ | `eps` | void fraction | 0.985 |
| $d_c$ | `dc` | column diameter | 0.2 m |
| $C_p, C_{lp}, C_{fp}, C_h$ | `Cp`, `Clp`, `Cfp`, `Ch` | packing constants | 0.172, 3.157, 2.464, 0.482 |
| $g$ | `g` | gravity | 9.81 m/s² |

## 1. Geometry (4.1)

Particle diameter (B3.1):

$$d_p = \frac{6(1-\varepsilon)}{a_p}$$

`d_part = 6 * (1 - eps) / ap`

Wall factor (B3.18):

$$f_s = \frac{1}{1 + \dfrac{4}{a_p d_c}}$$

`fs = 1 / (1 + 4 / (ap * dc))`

## 2. Dry pressure drop (4.1, B3.16 and B3.17)

Gas Reynolds number:

$$Re_G = \frac{u_G \, d_p \, \rho_G}{(1-\varepsilon)\,\mu_G} \, f_s$$

`Re_G = uG_s * d_part * rho_G / ((1 - eps) * mu_G) * fs`

Dry resistance coefficient:

$$\xi_d = C_p \left( \frac{64}{Re_G} + \frac{1.8}{Re_G^{0.08}} \right)$$

`xi_d = Cp * (64 / Re_G + 1.8 / Re_G ** 0.08)`

Dry pressure drop per metre:

$$\left(\frac{\Delta p}{H}\right)_{dry} = \xi_d \, \frac{a_p}{\varepsilon^3} \, \frac{\rho_G u_G^2}{2} \, \frac{1}{f_s}$$

`dp_dry = xi_d * ap / eps ** 3 * uG_s ** 2 / 2 * rho_G / fs`

**Updated 18 Nov:** Billet divides by the wall factor (book eq. 4-60 and worked example 14.2; Billet and Schultes 1999, eq. 23). The code was changed to divide in notebook 5.10 (D13a). Erasmus multiplied, so his Cp is converted by $f_s^2$ in cell 20.

## 3. Loading point (4.2)

Flow parameter (B3.10):

$$\psi = \frac{u_L \rho_L}{u_G \rho_G} \sqrt{\frac{\rho_G}{\rho_L}}$$

`psi = (uL_s * rho_L) / (uG_s * rho_G) * np.sqrt(rho_G / rho_L)`

Liquid Reynolds and Froude numbers. These are the $a_p$-based definitions, a hypothesis checked against Erasmus's plotted hold-ups:

$$Re_L = \frac{u_L \rho_L}{a_p \mu_L} \qquad Fr_L = \frac{u_L^2 a_p}{g}$$

`Re_L = uL_s * rho_L / (ap * mu_L)`, `Fr_L = uL_s ** 2 * ap / g`

Hold-up at the loading point (B3.7):

$$h_{lp} = \left(12 \, \frac{Fr_L}{Re_L}\right)^{1/3}$$

`ht_lp = 12 ** (1/3) * (uL_s * rho_L / (ap * mu_L)) ** (-1/3) * (uL_s ** 2 * ap / g) ** (1/3)`

Exponent (B3.9). Every point in the data has $\psi \le 0.4$, so only the first line is used:

$$n_{lp} = \begin{cases} -0.326 & \psi \le 0.4 \\ -0.723 & \psi > 0.4 \end{cases}$$

Resistance factor at loading (B3.8):

$$\xi_{lp} = \frac{g}{C_{lp}^2 \left[ \psi \left( \dfrac{\mu_L}{\mu_G} \right)^{0.4} \right]^{2 n_{lp}}}$$

`xi_lp = g / (Clp ** 2 * (psi * (mu_L / mu_G) ** 0.4) ** (2 * n_lp))`

Gas velocity at loading (B3.6):

$$u_{G,lp} = \sqrt{\frac{g}{\xi_{lp}}} \, (\varepsilon - h_{lp}) \sqrt{\frac{h_{lp}}{a_p}} \sqrt{\frac{\rho_L}{\rho_G}}$$

`uG_lp = np.sqrt(g / xi_lp) * (eps - ht_lp) * np.sqrt(ht_lp / ap) * np.sqrt(rho_L / rho_G)`

## 4. Flood point (4.2)

Hold-up at the flood point (B3.13):

$$h_{fp} = 0.3741 \, \varepsilon \left( \frac{\mu_L \, \rho_W}{\rho_L \, \mu_W} \right)^{0.05}$$

`ht_fp = 0.3741 * eps * (mu_L * rho_W / (rho_L * mu_W)) ** 0.05`

Exponent and resistance factor at flooding (B3.14, B3.15):

$$n_{fp} = \begin{cases} -0.194 & \psi \le 0.4 \\ -0.708 & \psi > 0.4 \end{cases} \qquad \xi_{fp} = \frac{g}{C_{fp}^2 \left[ \psi \left( \dfrac{\mu_L}{\mu_G} \right)^{0.2} \right]^{2 n_{fp}}}$$

`xi_fp = g / (Cfp ** 2 * (psi * (mu_L / mu_G) ** 0.2) ** (2 * n_fp))`

Gas velocity at flooding (B3.11):

$$u_{G,fp} = \sqrt{\frac{2g}{\xi_{fp}}} \, \frac{(\varepsilon - h_{fp})^{1.5}}{\sqrt{\varepsilon}} \sqrt{\frac{h_{fp}}{a_p}} \sqrt{\frac{\rho_L}{\rho_G}}$$

`uG_fp = np.sqrt(2 * g / xi_fp) * (eps - ht_fp) ** 1.5 / np.sqrt(eps) * np.sqrt(ht_fp / ap) * np.sqrt(rho_L / rho_G)`

If $u_G > u_{G,fp}$, the function returns no answer (`nan`). This refusal is our choice, not part of the model.

## 5. Hold-up for the pressure drop (4.3, B3.23)

Complete wetting:

$$h = \begin{cases} h_{lp} & u_G \le u_{G,lp} \\ h_{lp} + (h_{fp} - h_{lp}) \left( \dfrac{u_G}{u_{G,fp}} \right)^{13} & u_G > u_{G,lp} \end{cases}$$

`ht_dp`

## 6. Hold-up compared with the data (4.3, B3.24 to B3.27)

Partial wetting.

Hydraulic area ratio (B3.25, B3.26):

$$\frac{a_h}{a_p} = \begin{cases} C_h \, Re_L^{0.15} \, Fr_L^{0.1} & Re_L < 5 \\ 0.85 \, C_h \, Re_L^{0.25} \, Fr_L^{0.1} & Re_L \ge 5 \end{cases}$$

`ah_ap`

Hold-up below loading (B3.24):

$$h_{lp,2} = \left( \frac{12 \, \mu_L \, a_p^2 \, u_L}{\rho_L \, g} \right)^{1/3} \left( \frac{a_h}{a_p} \right)^{2/3}$$

`ht_lp2 = (12 * mu_L * ap ** 2 * uL_s / (rho_L * g)) ** (1/3) * ah_ap ** (2/3)`

Flood hold-up (B3.27), then the same switch as section 5:

$$h_{fp,2} = 2.2 \, h_{lp,2}$$

$$h_{2} = \begin{cases} h_{lp,2} & u_G \le u_{G,lp} \\ h_{lp,2} + (h_{fp,2} - h_{lp,2}) \left( \dfrac{u_G}{u_{G,fp}} \right)^{13} & u_G > u_{G,lp} \end{cases}$$

`holdup`

## 7. Wet pressure drop (4.4, B3.19 to B3.22)

Liquid correction factor:

$$W = \exp\!\left( \frac{Re_L}{200} \right) \left( \frac{h}{h_{lp}} \right)^{0.3}$$

`W = np.exp(Re_L / 200) * (ht_dp / ht_lp) ** 0.3`

Wet resistance coefficient:

$$\xi_w = C_p \, W \left( \frac{64}{Re_G} + \frac{1.8}{Re_G^{0.08}} \right) \left( \frac{\varepsilon - h}{\varepsilon} \right)^{1.5}$$

`xi_w = Cp * W * (64 / Re_G + 1.8 / Re_G ** 0.08) * ((eps - ht_dp) / eps) ** 1.5`

Wet pressure drop per metre, the model's output:

$$\left(\frac{\Delta p}{H}\right)_{wet} = \left(\frac{\Delta p}{H}\right)_{dry} \frac{\xi_w}{\xi_d} \left( \frac{\varepsilon}{\varepsilon - h} \right)^3$$

`dp_wet = dp_dry * (xi_w / xi_d) * (eps / (eps - ht_dp)) ** 3`

**Outputs:** `billet` returns $(\Delta p/H)_{wet}$ and $h_2$.

## Hypotheses carried in these equations

From the F4 notes, section "Assumptions and open points":

1. **$a_p$ = 350 m²/m³.** Not stated in the thesis. **Sourced 14 Nov:** Koch-Glitsch brochure (2010), pp. 2 and 4 (D8).
2. **$Re_L$ and $Fr_L$ use $a_p$.** These are Billet's definitions. Appendix B uses the corrugation side for the SRP model instead. The $a_p$ versions reproduce Erasmus's plotted hold-ups.
3. **Loading hold-up in the pressure drop section.** Table B.3 cites B3.6 there. We read this as B3.7.
4. **$u_{G,lp}$ and $u_{G,fp}$ come from the complete-wetting hold-ups** and are used for both hold-ups.
5. **$\psi$ is taken at each point's own $u_G$,** with no iteration.
6. **Wall factor:** settled, Billet divides by $f_s$ (see section 2, D13a).
7. **Constants above $\psi = 0.4$:** settled. Billet and Schultes (1999) eqs. 35 and 40 apply a viscosity correction to $C_S$ and $C_{Fl}$ there. This doesn't affect our data, because every point has $\psi \le 0.4$.

## 8. Mass transfer (not in the code yet, PLAN step 7)

Billet's mass-transfer model predicts three things separately: the interfacial area, the liquid-side coefficient and the gas-side coefficient. Each has its own packing constant, $C_L$ and $C_V$, fitted the same way as $C_h$ and $C_p$ on the hydraulics side.

**Source:** Billet, R. and Schultes, M. (1999), *Trans IChemE* 77(A), 498 to 504, p. 499, eqs. 1 to 16 (read from the page image of the scanned PDF in the packing-twin folder). The 1995 book gives the same three quantities in Chapter 5 (eqs. 5-8, 5-13 and section 5.3, pp. 121 to 129). Its text layer is garbled, so the book forms haven't been checked symbol by symbol against the 1999 ones (hypothesis that they match).

### Extra symbols

| Symbol | Meaning | Unit |
|---|---|---|
| $d_h$ | hydraulic diameter of the packing | m |
| $h_L$ | liquid hold-up used in the mass-transfer equations | m³/m³ |
| $\bar u_L$ | effective liquid velocity in the film | m/s |
| $a_{Ph}$ | interfacial (phase-boundary) area per unit packed volume | m²/m³ |
| $\beta_L, \beta_V$ | liquid- and gas-side mass transfer coefficients | m/s |
| $D_L, D_V$ | diffusion coefficients of the transferring component in liquid and gas | m²/s |
| $\nu_L, \nu_V$ | kinematic viscosities of liquid and gas, $\mu/\rho$ | m²/s |
| $\sigma_L, \sigma_W$ | surface tension of the liquid, and of water | N/m |
| $u_V$ | superficial gas velocity (our $u_G$) | m/s |
| $C_L, C_V$ | packing constants for liquid- and gas-side mass transfer | none |

### 8.1 Geometry and hold-up below the loading point

Hydraulic diameter (eq. 10):

$$d_h = 4 \, \frac{\varepsilon}{a}$$

Hold-up below loading (eq. 11). This is the complete-wetting hold-up, the same as $h_{lp}$ in section 3:

$$h_L = \left( 12 \, \frac{1}{g} \, \frac{\mu_L}{\rho_L} \, u_L \, a^2 \right)^{1/3} \qquad u_V \le u_{V,S}$$

(The paper writes viscosity as $\eta$; it's our $\mu$.)

Above loading, $h_L$ rises with the same exponent-13 rule as section 5 (eq. 12).

Effective liquid velocity (eq. 7):

$$\bar u_L = \frac{u_L}{h_L}$$

### 8.2 Interfacial area

Below the loading point (eq. 9):

$$\frac{a_{Ph}}{a} = 1.5 \, (a d_h)^{-0.5} \left( \frac{u_L d_h}{\nu_L} \right)^{-0.2} \left( \frac{u_L^2 \rho_L d_h}{\sigma_L} \right)^{0.75} \left( \frac{u_L^2}{g d_h} \right)^{-0.45}$$

The three brackets are a liquid Reynolds, Weber and Froude number, all based on $d_h$. There's no packing constant here: the area depends only on geometry and liquid properties. Note that $\sigma_L$ appears, unlike in the hold-up equation (D42).

Above the loading point, the area rises towards its flood value with the same exponent 13 (eq. 14):

$$\frac{a_{Ph}}{a} = \frac{a_{Ph,S}}{a} + \left( \frac{a_{Ph,Fl}}{a} - \frac{a_{Ph,S}}{a} \right) \left( \frac{u_V}{u_{V,Fl}} \right)^{13}$$

with the flood-point area (eq. 15):

$$\frac{a_{Ph,Fl}}{a} = 10.5 \left( \frac{\sigma_L}{\sigma_W} \right)^{0.56} (a d_h)^{-0.5} \left( \frac{u_L d_h}{\nu_L} \right)^{-0.2} \left( \frac{u_L^2 \rho_L d_h}{\sigma_L} \right)^{0.75} \left( \frac{u_L^2}{g d_h} \right)^{-0.45}$$

Eq. 16 gives $\bar u_L$ between loading and flooding. The paper also adds a correction for "negative" rectification systems, where surface tension falls along the column (p. 499 onwards). That doesn't apply to the SRP air/water data.

### 8.3 Liquid-side coefficient (eq. 6)

$$\beta_L \, a_{Ph} = C_L \, 12^{1/6} \, \bar u_L^{1/2} \left( \frac{D_L}{d_h} \right)^{1/2} a \left( \frac{a_{Ph}}{a} \right)$$

This is penetration theory: the liquid film renews itself each time it flows a distance of order $d_h$. $C_L$ is the only fitted number.

### 8.4 Gas-side coefficient (eq. 8)

$$\beta_V \, a_{Ph} = C_V \, \frac{1}{(\varepsilon - h_L)^{1/2}} \, \frac{a^{3/2}}{d_h^{1/2}} \, D_V \left( \frac{u_V}{a \nu_V} \right)^{3/4} \left( \frac{\nu_V}{D_V} \right)^{1/3} \left( \frac{a_{Ph}}{a} \right)$$

The two brackets are a gas Reynolds number and the gas Schmidt number. $C_V$ is the only fitted number.

### 8.5 From coefficients to column height (eqs. 1 to 5)

$$HTU_{OV} = HTU_V + \lambda \, HTU_L = \frac{u_V}{\beta_V a_{Ph}} + \frac{m_{yx}}{L/V} \, \frac{u_L}{\beta_L a_{Ph}}$$

$$H = HTU_{OV} \, NTU_{OV}$$

$\lambda = m_{yx} / (L/V)$ is the stripping factor. $m_{yx}$ is the slope of the equilibrium line and $L/V$ the molar flow ratio. For distillation data (Erasmus's total-reflux runs, FRI's HETP), HETP follows from these, which is how $C_L$ and $C_V$ can be fitted on HETP (PLAN steps 7a and 10).

### 8.6 How this maps onto the SRP data

| Billet | SRP measurement | Source |
|---|---|---|
| $a_{Ph}$ | effective area $a_e$ (CO₂ into 0.1 M NaOH) | Tsai Table B.8, Wang Table D.1 |
| $\beta_L$ | $k_L$ (toluene stripping, $k_L a$ divided by $a_e$) | Wang Table D.2, eq. 3-5 |
| $\beta_V$ | $k_G$ (SO₂ into NaOH) | Wang Table D.3, eq. 3-6 |

Unit note: Wang's $k_G$ comes from $u_G \ln(y_{in}/y_{out}) / (Z R T a_e)$ (Wang 2015, eq. 3-6), so it's per unit partial pressure. Billet's $\beta_V$ is in m/s. They are linked by $\beta_V = k_G R T$ (ideal gas). The units of the tabulated column need checking before fitting.

Two assumptions to carry (hypotheses):
1. **The CO₂/NaOH area equals Billet's $a_{Ph}$.** The SRP method measures the area active for a fast reaction. Billet's area is for physical absorption.
2. **Eq. 9 has no packing constant,** so for area, Level 0 and Level 1 are the same model unless we add one (e.g. a multiplier fitted per packing). That would be a method decision.

### 8.7 Published constants we have

| Packing | $C_L$ | $C_V$ | Source |
|---|---|---|---|
| Mellapak 250Y | not in Table 2b | not in Table 2b | Billet and Schultes (1999) |
| Raschig Super-Ring metal 0.3 | 1.500 | 0.450 | Billet and Schultes (1999), Table 2a |
| Raschig Super-Pak 250Y | 1.446 | 1.023 | Rejl et al. (2015), Table 3 |
| Flexipac 350Y (Erasmus's choice, borrowed from Montz B1-300) | 1.165 | 0.422 (Erasmus calls it $C_G$) | Erasmus (2004), Table 7.3 |
