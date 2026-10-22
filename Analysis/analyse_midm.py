"""
analyse_midm.py  —  one script that produces every table in Chapter 4.

Usage:
    python analyse_midm.py PCG_Results.csv --out results/

Analysis plan, frozen before the evaluation run (evaluation seed 20262016).
Do not change after seeing the evaluation data; anything added later is post-hoc and must be labelled so.
  * Outcomes: SearchNodes, SearchGenerated, PlanLength (log1p-transformed for regression).
    Solved is descriptive only (the bot is expected to solve ~100%); SolveTimeMs and WalkCells are sensitivity outcomes.
  * Inclusion: (A) all valid instances, (B) solved instances only — both reported
  * Hierarchical OLS: M1 baseline (TotalObjects, Steps)
                      M2 = M1 + Ch + Ci            (ClNorm is TotalObjects/12, collinear with M1)
                      M3 = M2 + Mr + Mo + Mi + Md
  * Composite check: MIDstatic vs MIDM added to M1
  * Beyond-certificate check: M1 + CertificateLength vs M1 + CertificateLength + MIDM
  * Weight sensitivity (T8): MID(w) = (1-w)*MIDstatic + w*MIDmob for w in 0, .25, .5, .75, 1
  * Pearson and Spearman correlations reported together, Holm-adjusted p-values
  * RQ1 proportions with Wilson 95% confidence intervals
  * Search effort beyond the minimum: SearchNodes > 1 modelled with nested logistic regressions
    (likelihood-ratio tests); Solved is also tried but is skipped when it has no variation
  * Sensitivity: (i) bot physical work and time (SolveTimeMs, WalkCells) as outcomes, (ii) legacy MID (0.5/0.3/0.2) in place of MIDstatic
    (an old-solver file with WalkTeleports > 0 also gets the no-teleport subset, T7b)
Requires: pandas, numpy, scipy, statsmodels
"""
import argparse, os
import numpy as np, pandas as pd
from scipy import stats
import statsmodels.api as sm

OUTCOMES = ["SearchNodes", "SearchGenerated", "PlanLength"]
SENSITIVITY_OUTCOMES = ["SolveTimeMs", "WalkCells"]
BASELINE = ["TotalObjects", "Steps"]
STATIC = ["Ch", "Ci"]
MOBILITY = ["Mr", "Mo", "Mi", "Md"]
DESCRIPTORS = ["TotalObjects", "Steps", "ClNorm", "Ch", "Ci", "MIDstatic",
               "Mr", "Mo", "Mi", "Md", "MIDmob", "MIDM", "MIDlegacy"]


def load(path):
    df = pd.read_csv(path, keep_default_na=False, na_values=[""])
    n0 = len(df)
    if "LevelValid" in df:
        df = df[df["LevelValid"] == 1]
    print(f"Loaded {n0} rows, {len(df)} valid instances")
    return df


def outcome_table(df):
    rows = {"N": len(df),
            "Solved (%)": 100 * df["Solved"].mean(),
            "Certificate valid (%)": 100 * df.get("CertificateValid", pd.Series(dtype=float)).mean(),
            "Certificate reachable (%)": 100 * df.get("CertificateReachable", pd.Series(dtype=float)).mean(),
            "Search timed out (%)": 100 * df.get("SearchTimedOut", pd.Series(dtype=float)).mean()}
    if "SearchNodes" in df:
        rows["SearchNodes == 1 (%)"] = 100 * (df["SearchNodes"] == 1).mean()
        rows["SearchNodes median"] = df["SearchNodes"].median()
    t = pd.Series(rows).round(2).to_frame("value")
    if "FinalPhase" in df:
        t2 = pd.crosstab(df["FinalPhase"], df["Solved"], margins=True)
        return t, t2
    return t, None


def wilson(k, n, z=1.96):
    if n == 0: return (np.nan, np.nan, np.nan)
    p = k / n; d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d; h = z * np.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (100 * p, 100 * (c - h), 100 * (c + h))


def rq1_table(raw, df):
    """RQ1: generation validity, certificate evidence and solver outcome (Wilson 95% CI)."""
    rows = []
    def add(name, k, n):
        p, lo, hi = wilson(int(k), int(n)); rows.append({"measure": name, "k": int(k), "n": int(n),
                                                     "percent": p, "ci95_low": lo, "ci95_high": hi})
    if "LevelValid" in raw: add("Valid instance generated", (raw["LevelValid"] == 1).sum(), len(raw))
    for c, name in [("CertificateValid", "Certificate valid (logical replay)"),
                    ("CertificateReachable", "Certificate reachable on foot"),
                    ("Solved", "Solved (laser reaches receiver)"),
                    ("LaserHit", "Laser hit at finish"), ("DoorOpened", "Door opened (sustained hit)")]:
        if c in df: add(name, df[c].sum(), len(df))
    if {"CertificateValid", "Solved"} <= set(df.columns):
        cv = df[df["CertificateValid"] == 1]
        add("Unresolved although certificate valid", (cv["Solved"] == 0).sum(), len(cv))
    if "FailReason" in df and "Solved" in df:
        eb = (df["Solved"] == 1) & df["FailReason"].astype(str).str.startswith("no walking path")
        add("Solved but exit blocked (final placement)", eb.sum(), len(df))
    if "WalkTeleports" in df: add("At least one walking teleport", (df["WalkTeleports"] > 0).sum(), len(df))
    return pd.DataFrame(rows).round(2)


def solved_by_cell(df):
    cols = [c for c in ["Steps", "Decoys", "KRel"] if c in df]
    return df.groupby(cols)["Solved"].agg(["count", "mean"]).round(3) if cols else None


def holm(p):
    p = np.asarray(p, float); o = np.argsort(p); m = len(p); adj = np.empty(m); run = 0
    for i, idx in enumerate(o):
        run = max(run, (m - i) * p[idx]); adj[idx] = min(1.0, run)
    return adj


def logistic(df, label, target="Solved"):
    rows = []; prev = None
    steps = [("L1 baseline", BASELINE), ("L2 + static", BASELINE + STATIC),
             ("L3 + mobility", BASELINE + STATIC + [m for m in MOBILITY if m in df])]
    y = (df["SearchNodes"] > 1).astype(float) if target == "SearchNodes>1" else df[target].astype(float)
    if y.nunique() < 2: return pd.DataFrame()
    for name, xs in steps:
        try:
            m = sm.Logit(y, sm.add_constant(df[xs].astype(float))).fit(disp=0)
        except Exception as e:
            rows.append({"subset": label, "target": target, "model": name, "error": str(e)}); break
        row = {"subset": label, "target": target, "model": name, "n": int(m.nobs), "McFaddenR2": m.prsquared, "AIC": m.aic}
        if prev is not None:
            lr = 2 * (m.llf - prev.llf); dfd = m.df_model - prev.df_model
            row.update({"LR": lr, "df": dfd, "p_LR": stats.chi2.sf(lr, dfd)})
        rows.append(row); prev = m
    return pd.DataFrame(rows)


def correlations(df, label):
    out = []
    for d in [c for c in DESCRIPTORS if c in df]:
        for y in OUTCOMES:
            x, yy = df[d].astype(float), df[y].astype(float)
            r, p = stats.pearsonr(x, yy)
            rho, ps = stats.spearmanr(x, yy)
            out.append({"subset": label, "descriptor": d, "outcome": y, "n": len(df),
                        "pearson_r": r, "pearson_p": p, "spearman_rho": rho, "spearman_p": ps})
    out = pd.DataFrame(out)
    if len(out):
        out["pearson_p_holm"] = holm(out["pearson_p"]); out["spearman_p_holm"] = holm(out["spearman_p"])
    return out


def ols(df, y, xs):
    X = sm.add_constant(df[xs].astype(float))
    return sm.OLS(np.log1p(df[y].astype(float)), X).fit(cov_type="HC3")


def hierarchical(df, label, outcomes=None):
    rows = []
    for y in (outcomes or OUTCOMES):
        steps = [("M1 baseline", BASELINE),
                 ("M2 + static", BASELINE + STATIC),
                 ("M3 + mobility", BASELINE + STATIC + [m for m in MOBILITY if m in df])]
        prev = None
        for name, xs in steps:
            m = ols(df, y, xs)
            row = {"subset": label, "outcome": y, "model": name, "n": int(m.nobs),
                   "R2": m.rsquared, "adjR2": m.rsquared_adj, "AIC": m.aic}
            if prev is not None:
                # nested-model F test (classical SEs for the test itself)
                Xr = sm.add_constant(df[prev[1]].astype(float))
                Xf = sm.add_constant(df[xs].astype(float))
                yy = np.log1p(df[y].astype(float))
                fr, ff = sm.OLS(yy, Xr).fit(), sm.OLS(yy, Xf).fit()
                f, p, dfd = ff.compare_f_test(fr)
                row.update({"dR2": m.rsquared - prev[0].rsquared, "F_change": f, "p_change": p})
            rows.append(row)
            prev = (m, xs)
        # composite comparison (MIDlegacy = sensitivity analysis)
        for comp in ["MIDstatic", "MIDM", "MIDlegacy"]:
            if comp in df:
                m = ols(df, y, BASELINE + [comp])
                rows.append({"subset": label, "outcome": y, "model": f"M1 + {comp}", "n": int(m.nobs),
                             "R2": m.rsquared, "adjR2": m.rsquared_adj, "AIC": m.aic})
        # does MID-M add anything beyond the certificate (solution) length?
        if "CertificateLength" in df and "MIDM" in df:
            ma = ols(df, y, BASELINE + ["CertificateLength"])
            mb = ols(df, y, BASELINE + ["CertificateLength", "MIDM"])
            yy = np.log1p(df[y].astype(float))
            fa = sm.OLS(yy, sm.add_constant(df[BASELINE + ["CertificateLength"]].astype(float))).fit()
            fb = sm.OLS(yy, sm.add_constant(df[BASELINE + ["CertificateLength", "MIDM"]].astype(float))).fit()
            f, p, _ = fb.compare_f_test(fa)
            rows.append({"subset": label, "outcome": y, "model": "M1 + CertificateLength", "n": int(ma.nobs),
                         "R2": ma.rsquared, "adjR2": ma.rsquared_adj, "AIC": ma.aic})
            rows.append({"subset": label, "outcome": y, "model": "M1 + CertificateLength + MIDM", "n": int(mb.nobs),
                         "R2": mb.rsquared, "adjR2": mb.rsquared_adj, "AIC": mb.aic,
                         "dR2": mb.rsquared - ma.rsquared, "F_change": f, "p_change": p})
    return pd.DataFrame(rows)


def weight_sensitivity(df, label, outcomes=None):
    """T8: does the conclusion depend on the manual 0.5/0.5 weight? MID(w) = (1-w)*MIDstatic + w*MIDmob."""
    if not {"MIDstatic", "MIDmob"} <= set(df.columns): return pd.DataFrame()
    rows = []
    for y in (outcomes or OUTCOMES):
        if y not in df: continue
        yy = np.log1p(df[y].astype(float))
        base = sm.OLS(yy, sm.add_constant(df[BASELINE].astype(float))).fit()
        for w in [0.0, 0.25, 0.5, 0.75, 1.0]:
            d = df[BASELINE].astype(float).copy()
            d["MIDw"] = (1 - w) * df["MIDstatic"].astype(float) + w * df["MIDmob"].astype(float)
            rho, p = stats.spearmanr(d["MIDw"], df[y].astype(float))
            f = sm.OLS(yy, sm.add_constant(d)).fit()
            rows.append({"subset": label, "outcome": y, "w_mobility": w, "n": len(df),
                         "spearman_rho": rho, "spearman_p": p, "R2_M1_plus_MIDw": f.rsquared,
                         "dR2_over_M1": f.rsquared - base.rsquared})
    return pd.DataFrame(rows)


def scale_check(df):
    """Pilot only: SD of each term inside the composite (no outcome is used)."""
    terms = {"ClNorm/3": df["ClNorm"] / 3, "Ch/3": df["Ch"] / 3, "Ci/3": df["Ci"] / 3}
    for m in MOBILITY:
        if m in df: terms[f"{m}/4"] = df[m] / 4
    return pd.Series({k: v.std() for k, v in terms.items()}).round(4).to_frame("SD")


def vif(df):
    from statsmodels.stats.outliers_influence import variance_inflation_factor
    xs = [c for c in BASELINE + STATIC + MOBILITY if c in df]
    X = sm.add_constant(df[xs].astype(float))
    return pd.Series({c: variance_inflation_factor(X.values, i + 1) for i, c in enumerate(xs)}).round(2).to_frame("VIF")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("csv"); ap.add_argument("--out", default="results")
    ap.add_argument("--pilot", action="store_true", help="only run the scale check")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    df = load(a.csv)

    raw = pd.read_csv(a.csv, keep_default_na=False, na_values=[""])

    if a.pilot:
        sc = scale_check(df); print(sc); sc.to_csv(f"{a.out}/pilot_scale_check.csv")
        modes = df["Mode"].astype(str).str.lower().unique().tolist() if "Mode" in df else []
        cvs = df["CodeVersion"].unique().tolist() if "CodeVersion" in df else []
        print(f"Mode: {modes} | CodeVersion: {cvs}")
        k, n = int(df["Solved"].sum()), len(df); p, lo, hi = wilson(k, n)
        if modes == ["replay"]:
            verdict = "PASS" if p >= 95 else "FAIL"
            print(f"Logic-physics agreement (replay Solved): {k}/{n} = {p:.1f}% (95% CI {lo:.1f}-{hi:.1f}) -> {verdict} (criterion >= 95%)")
        else:
            print(f"Bot success in {modes} mode: {k}/{n} = {p:.1f}%. NOT the pilot gate (the gate is defined for Replay mode only)")
        rq1_table(raw, df).to_csv(f"{a.out}/pilot_rq1.csv", index=False)
        if "KRel" in df: print(df.groupby("KRel")["Solved"].agg(["count", "mean"]))
        return

    modes = df["Mode"].astype(str).str.lower().unique().tolist() if "Mode" in df else []
    cvs = df["CodeVersion"].unique().tolist() if "CodeVersion" in df else []
    print(f"Mode: {modes} | CodeVersion: {cvs}")
    if "replay" in modes:
        raise SystemExit("This file contains Replay-mode rows (SearchNodes = 0); the evaluation needs Solver mode. Stopping.")
    cell_cols = [c for c in ["Steps", "Decoys", "KRel"] if c in df]
    if len(cell_cols) == 3:
        cc = df.groupby(cell_cols).size()
        print(f"Design cells with data: {len(cc)} | runs per cell min/max: {cc.min()}/{cc.max()}")
        if "KRelApplied" in df: print("Runs per KRelApplied:", df["KRelApplied"].value_counts().sort_index().to_dict())
        print("Runs per requested KRel:", df["KRel"].value_counts().sort_index().to_dict())
    r1 = rq1_table(raw, df); r1.to_csv(f"{a.out}/T0_rq1_wilson.csv", index=False); print(r1.to_string())
    sc = solved_by_cell(df)
    if sc is not None: sc.to_csv(f"{a.out}/T0b_solved_by_cell.csv")
    t, phase = outcome_table(df)
    t.to_csv(f"{a.out}/T1_outcomes.csv"); print(t)
    if phase is not None: phase.to_csv(f"{a.out}/T1b_phase_by_solved.csv"); print(phase)

    subsets = {"all": df, "solved": df[df["Solved"] == 1]}
    pd.concat([correlations(s, k) for k, s in subsets.items()]).round(4).to_csv(f"{a.out}/T2_correlations.csv", index=False)
    h = pd.concat([hierarchical(s, k) for k, s in subsets.items()]).round(4)
    h.to_csv(f"{a.out}/T3_hierarchical.csv", index=False); print(h.to_string())
    vif(df).to_csv(f"{a.out}/T4_vif.csv")
    pd.concat([logistic(df, "all", "Solved"), logistic(df, "all", "SearchNodes>1")]).round(4).to_csv(
        f"{a.out}/T6_logistic.csv", index=False)
    ws = pd.concat([weight_sensitivity(s, k) for k, s in subsets.items()]).round(4)
    ws.to_csv(f"{a.out}/T8_weight_sensitivity.csv", index=False); print(ws.to_string())
    work = [c for c in SENSITIVITY_OUTCOMES if c in df]
    if work:
        sol = df[df["Solved"] == 1]
        sw = pd.concat([hierarchical(sol, "solved", work)])
        sw.round(4).to_csv(f"{a.out}/T7_sensitivity_bot_work.csv", index=False)
    if "WalkTeleports" in df and (df["WalkTeleports"] > 0).any():
        nt = df[df["WalkTeleports"] == 0]
        sens = pd.concat([hierarchical(nt, "noTeleport_all"), hierarchical(nt[nt["Solved"] == 1], "noTeleport_solved")])
        sens.round(4).to_csv(f"{a.out}/T7b_sensitivity_no_teleport.csv", index=False)
    scale_check(df).to_csv(f"{a.out}/T5_term_sd.csv")
    print(f"Tables written to {a.out}/")


if __name__ == "__main__":
    main()
