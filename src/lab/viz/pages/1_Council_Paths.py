#!/usr/bin/env python3
"""
Council PATH-QUALITY — Streamlit page (a new tab in the :8501 explorer app).

Renders the same analysis as the CLI `council_paths.py` (imported, so the numbers can never drift):
does higher CONVICTION buy better-SHAPED paths? Per scope — never pools bartypes. Interactive:
pick a scope, see the conviction-vs-shape scatter + bucket table + per-voter table, click a fire
to draw its raw tick path. Reads Sentinel\\Lab\\db\\sentinel.db (source='council').
"""
import os, sys, sqlite3
import numpy as np
import pandas as pd
import streamlit as st
import plotly.graph_objects as go

# import the CLI analysis module (one source of truth for the math). Its main() is __main__-guarded, so
# importing runs no analysis — we only borrow load/enrich/load_votes/bucket/spearman.
LAB = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
if LAB not in sys.path:
    sys.path.insert(0, LAB)
import council_paths as cp
from sentinel_lab.bartag import friendly_scope   # human scope label (display only; machine key unchanged)

st.set_page_config(page_title="Council Path Quality", page_icon="🎯", layout="wide")
# readable dark-slate text on the cyan filter pills (matches the explorer + the Sentinel skin)
st.markdown("""<style>
span[data-baseweb="tag"], span[data-baseweb="tag"] span { color: #0c1620 !important; }
span[data-baseweb="tag"] svg { fill: #0c1620 !important; }
</style>""", unsafe_allow_html=True)

UP, DN, MUTE, ACC = "#26a69a", "#ef5350", "#5b6b7a", "#2bd4e6"

st.title("🎯 Council Path Quality")
st.caption("Does higher conviction buy better-SHAPED paths? (the redirection thesis: a win/loss binary hides shape — "
           "measure the path.) One scope at a time; bartypes are never pooled.")


@st.cache_data(ttl=5)
def load_all():
    con = sqlite3.connect(cp.DB)
    trades, ticks = cp.load(con, "", [])
    if trades.empty:
        return pd.DataFrame(), pd.DataFrame()
    t = cp.enrich(trades, ticks)
    votes = cp.load_votes()
    t["votes"] = t["entry_utc"].map(votes)
    return t, ticks


if not os.path.exists(cp.DB):
    st.error(f"no DB at {cp.DB} — run ingest\\ingest.py first."); st.stop()

t, ticks = load_all()
if t.empty:
    st.warning("no council fires yet — load a Council + Excursion Recorder (v2.1+) on a chart; fires flow to "
               "council\\ticks\\ → SQLite. A fast STF-only chart bakes a corpus quickest."); st.stop()

# ── scope picker ──
c0, c1 = st.columns([3, 1])
scope = c0.selectbox("scope (instrument.bartype)", sorted(t["scope"].unique()), format_func=friendly_scope,
                     help="Each scope is one chart's worth of context — the coordinate the Council is defined over.")
bmode = c1.radio("buckets", ["fixed", "quantile"], horizontal=True)
g = t[t["scope"] == scope].copy()
res = g[g["first_touch"] != 0]
wr = (res["win"].mean() * 100) if len(res) else float("nan")

m = st.columns(5)
m[0].metric("fires", len(g))
m[1].metric("resolved win-rate", f"{wr:.0f}%" if not np.isnan(wr) else "—")
m[2].metric("win / loss / cens", f"{int(g['win'].sum())} / {int(g['loss'].sum())} / {int(g['censored'].sum())}")
m[3].metric("median MFE / MAE", f"{g['mfe'].median():.0f} / {g['mae'].median():.0f}t")
m[4].metric("median barrier", f"{g['barrier_ticks'].median():.0f}t")

convs = g["conviction"].dropna()
degenerate = convs.nunique() < 2
if degenerate:
    cval = convs.iloc[0] if len(convs) else float("nan")
    st.info(f"⚠ conviction has no variation on this scope (all = {cval:.2f}) — it can't test conviction-vs-shape. "
            f"Expected for a single-voter roster (e.g. STF-only). Bake a multi-voter chart (212201/212202) for the thesis.")

# ── conviction vs shape scatter ──
st.subheader("conviction vs path shape")
ycol = st.selectbox("shape metric (Y)", ["mfe_mae", "efficiency", "run_frac", "giveback", "chop"], index=0,
                    format_func=lambda c: {"mfe_mae": "MFE/MAE ratio", "efficiency": "path efficiency (0..1)",
                                           "run_frac": "favorable dwell (frac ticks)", "giveback": "giveback (ticks)",
                                           "chop": "macro reversals"}[c])
col_by = {1: UP, -1: DN, 0: MUTE}
gg = g.dropna(subset=[ycol])
fig = go.Figure()
for ft, name in [(1, "target-first"), (-1, "stop-first"), (0, "censored")]:
    s = gg[gg["first_touch"] == ft]
    if s.empty:
        continue
    fig.add_trace(go.Scatter(x=s["conviction"], y=s[ycol], mode="markers", name=name,
                             marker=dict(size=10, color=col_by[ft], line=dict(width=0.5, color="#0a0e13")),
                             text=s["trade_id"], hovertemplate="conv %{x:.2f} · %{y:.2f}<br>%{text}<extra></extra>"))
rho = cp.spearman(g["conviction"], g[ycol])
sub = f"Spearman ρ(conviction, {ycol}) = {rho:+.2f}" if not (rho is None or np.isnan(rho)) else \
      "Spearman ρ = n/a (need conviction variation + N≥3)"
st.caption(sub)   # kept out of the plot title so it never overlaps the top-left of the scatter
fig.update_layout(template="plotly_dark", height=430, margin=dict(l=50, r=20, t=30, b=44),
                  paper_bgcolor="#0a0e13", plot_bgcolor="#0d131c",
                  xaxis_title="conviction", yaxis_title=ycol, legend=dict(orientation="h", y=1.1))
st.plotly_chart(fig, use_container_width=True)

# ── conviction-bucket shape table ──
st.subheader("shape by conviction bucket")
g["bucket"] = cp.bucket(g["conviction"], bmode)
order = ["LOW", "MID", "HIGH"] if bmode != "quantile" else ["Q1", "Q2", "Q3", "Q4"]
recs = []
for bk in order:
    b = g[g["bucket"] == bk]
    if b.empty:
        continue
    r = b[b["first_touch"] != 0]
    recs.append(dict(bucket=bk, N=len(b), win_pct=round((r["win"].mean() * 100) if len(r) else float("nan"), 0),
                     MFE=round(b["mfe"].median(), 0), MAE=round(b["mae"].median(), 0),
                     mfe_mae=round(b["mfe_mae"].median(), 2), eff=round(b["efficiency"].median(), 2),
                     dwell=round(b["run_frac"].median(), 2), giveback=round(b["giveback"].median(), 0),
                     chop=round(b["chop"].median(), 0)))
if recs:
    st.dataframe(pd.DataFrame(recs), use_container_width=True, hide_index=True)

# ── per-voter shape (when multi-voter) ──
rows = g[g["votes"].apply(lambda v: isinstance(v, dict) and len(v) > 0)]
tags = sorted({tag for v in rows["votes"] for tag in v}) if not rows.empty else []
st.subheader("per-voter shape")
if len(tags) < 2:
    st.caption(f"only {len(tags)} voter present here ({tags or '—'}) — the per-voter view needs a multi-voter chart. "
               "This is where weight-fitting starts: which voters, when they agree, buy cleaner paths?")
else:
    vrecs = []
    for tag in tags:
        ag = rows[rows.apply(lambda r: r["votes"].get(tag, 0) != 0 and np.sign(r["votes"].get(tag, 0)) == np.sign(r["dir"]), axis=1)]
        ot = rows[~rows.index.isin(ag.index)]
        def wr_(x):
            rr = x[x["first_touch"] != 0]
            return round((rr["win"].mean() * 100) if len(rr) else float("nan"), 0)
        vrecs.append(dict(voter=tag, agree_n=len(ag), agree_win=wr_(ag),
                          agree_mfe_mae=round(ag["mfe_mae"].median(), 2), agree_dwell=round(ag["run_frac"].median(), 2),
                          other_win=wr_(ot), other_mfe_mae=round(ot["mfe_mae"].median(), 2)))
    st.dataframe(pd.DataFrame(vrecs), use_container_width=True, hide_index=True)

# ── per-fire tick path ──
st.subheader("tick path")
fid = st.selectbox("fire", g.sort_values("entry_utc", ascending=False)["trade_id"].tolist())
row = g[g["trade_id"] == fid].iloc[0]
pth = ticks[ticks["trade_id"] == fid].sort_values("ms")
if pth.empty:
    st.info("no ticks for this fire.")
else:
    sec = pth["ms"] / 1000.0
    br = float(row["barrier_ticks"]) if pd.notna(row["barrier_ticks"]) else np.nan
    dir_s = "LONG" if row["dir"] == 1 else "SHORT"
    ftlbl = {1: "target-first ✓", -1: "stop-first ✗", 0: "censored"}.get(int(row["first_touch"]), "?")
    f = go.Figure()
    f.add_hline(y=0, line=dict(color="#889", width=1))
    if not np.isnan(br):
        f.add_hline(y=br, line=dict(color=UP, width=1, dash="dot"), annotation_text=f"+R {br:.0f}t")
        f.add_hline(y=-br, line=dict(color=DN, width=1, dash="dot"), annotation_text=f"-R {br:.0f}t")
    f.add_trace(go.Scatter(x=sec, y=pth["fav_t"], mode="lines", line=dict(color="#4fc3f7", width=1.4),
                           hovertemplate="%{x:.1f}s · %{y:.1f}t<extra></extra>"))
    f.update_layout(template="plotly_dark", height=430, margin=dict(l=50, r=20, t=44, b=44),
                    paper_bgcolor="#0a0e13", plot_bgcolor="#0d131c",
                    xaxis_title="seconds from fire", yaxis_title="favorable excursion (ticks) · fire = 0",
                    title=f"{dir_s} {row['inst']} · conv {row['conviction']:.2f} · {ftlbl} · {int(row['n_ticks'])} ticks")
    st.plotly_chart(f, use_container_width=True)
