# -*- coding: utf-8 -*-
r"""
Sentinel Observatory — a live look at the Council corpus as it streams/dumps.

Reads the excursion JSONL (schema 1.3) and renders five human views:
  1. Decisions    — price spine per bar type with every fire drawn like a broker fill
                    (arrow=direction, green/red=won/lost, size=conviction) + rolling win-rate
  2. Calibration  — conviction vs first-touch outcome, with the binned win-rate curve + floor
  3. Distributions— conviction bell curves (violins) per bar type
  4. MAE / MFE     — the excursion "shotgun": worst adverse vs best favorable, by outcome
  5. Vote Genome   — a barcode of every decision: rows=fires, cols=voters, green/red/grey

Reuses sentinel_lab.dataset/labels so the numbers match train.py + compare_bartypes.py.

Run:  Sentinel\Lab\.venv\Scripts\streamlit run Sentinel\Lab\viz\observatory.py
"""
import os, sys, glob, json, re, base64
import numpy as np
import pandas as pd
import streamlit as st
import plotly.graph_objects as go
from plotly.subplots import make_subplots

LAB_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, LAB_ROOT)
from lab_faults import swallow
from sentinel_lab import dataset, labels, bartag  # noqa: E402

try:
    from streamlit_autorefresh import st_autorefresh
    HAS_AR = True
except Exception:
    HAS_AR = False

# ── Sentinel palette ────────────────────────────────────────────────────────
CY, UP, DN, GREY, WARN = "#2bd4e6", "#25d08b", "#ff5c6a", "#6b7785", "#e0a83a"
INK, INK2, BG, PANEL, LINE = "#e7edf1", "#8a97a3", "#0a0e13", "#121821", "#243040"
DIR_COLOR = {"LONG": UP, "SHORT": DN, "FLAT": GREY}
OUT_COLOR = {"win": UP, "loss": DN, "censored": GREY}
def bt_label(x):
    # Friendly bar-type label — delegates to the SHARED resolver (the Python mirror of
    # SentinelCore.FriendlyBartag) so the observatory, the explorer, the on-chart cards, and the DB
    # `bar_label` all read the SAME label. (Machine bartag stays the key; this is display only.)
    return bartag.friendly_bartag(x)
VOTERS = dataset.catalog_voter_tags()   # full canonical voter set from the shared catalog (incl. STF, FLUX, …)

# Sentinel mark — a guard's shield with a cyan visor slit (cyan = live/watching, the design law).
_SENTINEL_MARK = (
    "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'>"
    "<path d='M12 1.6 L20.5 4.8 V11 C20.5 16.2 16.6 20.4 12 22.4 C7.4 20.4 3.5 16.2 3.5 11 V4.8 Z' "
    "fill='rgba(43,212,230,0.08)' stroke='#2bd4e6' stroke-width='1.5' stroke-linejoin='round'/>"
    "<rect x='7.4' y='9.5' width='9.2' height='2.7' rx='1.35' fill='#2bd4e6'/>"
    "<path d='M9 14.6 C10 15.7 14 15.7 15 14.6' fill='none' stroke='#2bd4e6' stroke-width='1.2' "
    "stroke-linecap='round' opacity='0.55'/></svg>")
_MARK_B64 = base64.b64encode(_SENTINEL_MARK.encode()).decode()
_MARK_URI = f"data:image/svg+xml;base64,{_MARK_B64}"

st.set_page_config(page_title="Sentinel Observatory", page_icon="🛡️", layout="wide")
st.markdown(f"""<style>
  .stApp {{ background:{BG}; }}
  h1,h2,h3,h4 {{ font-family:ui-monospace,Consolas,monospace; letter-spacing:.02em; }}
  [data-testid="stMetricValue"] {{ font-family:ui-monospace,Consolas,monospace; color:{INK}; }}
  [data-testid="stMetricLabel"] {{ color:{INK2}; }}
  .block-container {{ padding-top:1.3rem; }}
  /* Multiselect (bar-type) pills: dark text on the cyan fill for readability (keep the cyan). */
  span[data-baseweb="tag"] {{ color:#0c1620 !important; }}
  span[data-baseweb="tag"] span {{ color:#0c1620 !important; }}
  span[data-baseweb="tag"] svg {{ color:#0c1620 !important; fill:#0c1620 !important; }}
</style>""", unsafe_allow_html=True)


def style(fig, h=None, title=None):
    fig.update_layout(template="plotly_dark", paper_bgcolor=BG, plot_bgcolor=PANEL,
                      font=dict(color=INK, family="ui-monospace,Consolas,monospace", size=12),
                      margin=dict(l=48, r=20, t=44 if title else 16, b=40),
                      legend=dict(bgcolor="rgba(0,0,0,0)"))
    fig.update_xaxes(gridcolor=LINE, zerolinecolor=LINE)
    fig.update_yaxes(gridcolor=LINE, zerolinecolor=LINE)
    if h:
        fig.update_layout(height=h)
    if title:
        fig.update_layout(title=dict(text=title, font=dict(color=CY, size=14)))
    return fig


# Filenames are  <YYYYMMDDThhmmss>__<INST>__<BARTYPE>.jsonl  (BARTYPE may carry an @LANE).
GRP_RE = re.compile(r"^\d{8}T\d{6}__([A-Za-z0-9]+)__(.+)\.jsonl$")
_SKIP = ("_archive", "/ticks", "_exp", "__pycache__", "_baselines")


def discover_groups(base):
    """Auto-find every data group under the Excursions root: (corpus, instrument, bartype).
    A 'group' is one scope's worth of a corpus — exactly what a viewer wants to pick. No paths,
    no schema-folder spelunking. Sorted freshest-first so the default IS what you're baking now."""
    groups = {}
    for p in glob.glob(os.path.join(base, "**", "*.jsonl"), recursive=True):
        rel = os.path.relpath(p, base).replace("\\", "/")
        if any(x in rel.lower() for x in _SKIP):
            continue
        m = GRP_RE.match(os.path.basename(p))
        if not m:
            continue
        inst, bartype = m.group(1), m.group(2)
        corpus = os.path.dirname(rel)                      # e.g. council/1.4  ·  _replay/council/1.4
        key = (corpus, inst, bartype)
        g = groups.get(key)
        if g is None:
            base_bt, _, lane = bartype.partition("@")
            try:
                friendly = bartag.friendly_bartag(base_bt)
            except Exception:
                friendly = base_bt
            if lane:
                friendly += f" @{lane}"
            g = groups[key] = dict(corpus=corpus, inst=inst, bartype=bartype,
                                   friendly=friendly, files=[], bytes=0, mtime=0.0)
        g["files"].append(p)
        try:
            g["bytes"] += os.path.getsize(p)
            g["mtime"] = max(g["mtime"], os.path.getmtime(p))
        except OSError as _swex:
            swallow("viz.observatory.discover_groups", _swex)
    return sorted(groups.values(), key=lambda g: g["mtime"], reverse=True)


def load(paths, prefix, signal):
    rows = []
    for p in paths:
        b = os.path.basename(p)
        if prefix and not b.startswith(prefix):
            continue
        try:
            with open(p, encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        r = json.loads(line)
                    except Exception as _swex:
                        swallow("viz.observatory.load", _swex)
                        continue
                    if r.get("kind") != "excursion":
                        continue
                    r["_file"] = b
                    rows.append(r)
        except Exception as _swex:
            swallow("viz.observatory.load#2", _swex)
            continue
    df = pd.DataFrame(rows)
    if df.empty:
        return df
    if signal and "signal" in df:
        df = df[df["signal"].astype(str) == signal]
    if df.empty:
        return df
    df["fireTime"] = pd.to_datetime(df["fireTime"], utc=True, format="ISO8601", errors="coerce")
    df = df.dropna(subset=["fireTime"]).sort_values("fireTime").reset_index(drop=True)
    df["bt"] = df["bartype"].astype(str).map(bt_label)
    df["lane"] = df["inst"].astype(str) + " · " + df["bt"]   # lane = INSTRUMENT + bar type (never merge GC & NQ)
    # Use the RECORDED conviction (denomW / kind-aware — what the Council actually gated on).
    # dataset.conviction() recomputes |netScore|/activeW (pre-fix "agreement among speakers"),
    # which saturates near 1.0 and is NOT the live floor's conviction. Fall back only for 1.2 rows.
    _rec = pd.to_numeric(df["conviction"], errors="coerce") if "conviction" in df.columns \
        else pd.Series(np.nan, index=df.index)
    df["conv"] = _rec.where(_rec.notna(), dataset.conviction(df))
    lab = labels.make_labels(df)
    df["y"] = lab["y"].values
    df["t0"] = lab["t0"].values
    df["t1"] = lab["t1"].values
    df["dirstr"] = df["dir"].map(lambda d: "LONG" if d > 0 else ("SHORT" if d < 0 else "FLAT"))
    df["outcome"] = df["y"].map({1: "win", 0: "loss", -1: "censored"})
    return df


def eff_n(g):
    try:
        w = labels.uniqueness_weights(pd.Series(g["t0"].values), pd.Series(g["t1"].values))
        return labels.effective_n(w)
    except Exception as _swex:
        swallow("viz.observatory.eff_n", _swex)
        return float(len(g))


@st.cache_data(show_spinner=False)
def load_sidecars(ticks_dir, sig):
    """Parse a council/ticks sidecar dir → per-fire dict (header + full ms/px path).
    `sig` (file count, latest mtime) only keys the cache so a fresh bake invalidates it."""
    out = []
    for p in glob.glob(os.path.join(ticks_dir, "*.jsonl")):
        try:
            L = [json.loads(x) for x in open(p, encoding="utf-8") if x.strip()]
        except Exception as _swex:
            swallow("viz.observatory.load_sidecars", _swex)
            continue
        if len(L) < 2:
            continue
        h = L[0]
        samp = [(d["ms"], d["px"]) for d in L[1:] if isinstance(d, dict) and "px" in d and "ms" in d]
        if not samp:
            continue
        out.append(dict(
            t=h.get("fireTime"), dir=int(h.get("dir", 0)), firePx=float(h.get("firePx", np.nan)),
            inst=str(h.get("inst", "")), bartype=str(h.get("bartype", "")),
            ms=np.asarray([s[0] for s in samp], float), px=np.asarray([s[1] for s in samp], float),
            conv=h.get("conviction"), mfe=h.get("maxFavTicks"), mae=h.get("maxAdvTicks"),
            barrier=h.get("barrierTicks"), ft=h.get("firstTouchTick"), fid=str(h.get("fireId", ""))))
    return out


def ticktrue_equity(fires, T, S, tick, cost, gate=0.0, be=0.0):
    """Realistic flip-to-flip: enter at the FIRST tradeable tick after each flip, walk the tick path
    to the first exit. Real entry fills (no brick-close fantasy). Only TAKE flips with conviction ≥ gate
    (skipped = flat, no cost). If be>0, trail the stop up to BREAKEVEN (0) once price reaches +be ticks
    favorable — protects a winner from round-tripping. Returns (x, cumulative_pnl, n_taken)."""
    fs = sorted(fires, key=lambda f: f["t"])
    d = np.array([f["dir"] for f in fs])
    keep = np.ones(len(d), bool)
    keep[1:] = d[1:] != d[:-1]
    flips = [f for f, k in zip(fs, keep) if k]
    if len(flips) < 2:
        return None, None, 0
    xs, pnl, taken = [], [], 0
    for f in flips:
        xs.append(f["t"])
        if (f.get("conv") or 0.0) < gate:
            pnl.append(0.0)                                 # skipped flip: flat, no cost
            continue
        rel = f["dir"] * (f["px"] - f["px"][0]) / tick      # ticks favorable, along the path
        stop, res = -float(S), None
        for r in rel:
            if be and stop < 0.0 and r >= be:               # once +be reached, stop can't lose
                stop = 0.0
            if r >= T:
                res = T
                break
            if r <= stop:
                res = stop
                break
        pnl.append((rel[-1] if res is None else res) - cost)   # time-exit at last px if neither hit
        taken += 1
    return pd.to_datetime(xs, utc=True), np.cumsum(pnl), taken


# ── Sidebar ─────────────────────────────────────────────────────────────────
st.sidebar.markdown(
    f"<div style='display:flex;align-items:center;gap:9px;margin:.1rem 0 .5rem 0;'>"
    f"<img src='{_MARK_URI}' width='28' height='28'/>"
    f"<span style='font-family:ui-monospace,Consolas,monospace;font-size:1.55rem;font-weight:700;"
    f"letter-spacing:.02em;color:{INK};'>Observatory</span></div>", unsafe_allow_html=True)

EXC_ROOT = os.path.normpath(os.path.join(LAB_ROOT, "..", "Excursions"))
with st.sidebar.expander("⚙ source (advanced)", expanded=False):
    base = st.text_input("Excursions root", EXC_ROOT)
    signal = st.text_input("Signal filter", "",
                           help="blank = every row in the group. Each group is already one corpus, "
                                "so a filter is rarely needed (council rows are COUNCIL, candidates aren't).")
    prefix = st.text_input("Run prefix filter", "", help="e.g. 20260712T0415 for one run; blank = all")

groups = discover_groups(base)
st.title("Sentinel Observatory")
if not groups:
    st.info(f"No data groups found under `{base}`. The Recorder flushes at each session boundary; "
            "this page fills in as it dumps.")
    st.stop()


def _src(g):
    c = g["corpus"].lower()
    return "replay" if "_replay" in c else ("cand" if "candidat" in c else "live")


def _fmt(g):
    return f'{g["inst"]} · {g["friendly"]}   [{_src(g)}]   ·   {g["corpus"]}   ·   {g["bytes"]/1e6:.1f} MB'


# Default to the freshest DECISION corpus (council / replay), not a candidate group —
# a candidate default would open on the wrong signal and look empty.
for g in groups:
    g["_key"] = f'{g["corpus"]}|{g["inst"]}|{g["bartype"]}'
k2g = {g["_key"]: g for g in groups}
_decision = [g for g in groups if g["corpus"].startswith(("council", "_replay/council"))]
default_group = _decision[0] if _decision else groups[0]


def _folder_chain(corpus):
    parts = (corpus or "(root)").split("/")
    return ["dir:" + "/".join(parts[:i + 1]) for i in range(len(parts))]


def _build_tree(gs):
    """Turn the corpus folder paths into a nested tree: folders (📁) organise, group leaves check."""
    folders, tops = {}, []

    def folder(path):
        if path in folders:
            return folders[path]
        parts = path.split("/")
        node = {"label": "🗀 " + parts[-1], "value": "dir:" + path, "children": []}
        folders[path] = node
        (tops if len(parts) == 1 else folder("/".join(parts[:-1]))["children"]).append(node)
        return node

    for g in gs:
        folder(g["corpus"] or "(root)")["children"].append(
            {"label": f'{g["inst"]} · {g["friendly"]}   [{_src(g)}]   ·   {g["bytes"]/1e6:.1f} MB',
             "value": g["_key"]})
    return tops


st.sidebar.markdown("**Data group**  ·  check to view · add more to compare")
try:
    from streamlit_tree_select import tree_select
    if "dg_checked" not in st.session_state:
        st.session_state.dg_checked = [default_group["_key"]]
        st.session_state.dg_expanded = _folder_chain(default_group["corpus"])
    with st.sidebar:
        res = tree_select(_build_tree(groups), only_leaf_checkboxes=True, expand_on_click=True,
                          show_expand_all=True, check_model="all",
                          checked=st.session_state.dg_checked, expanded=st.session_state.dg_expanded,
                          key="dgtree")
    if res:
        st.session_state.dg_checked = res.get("checked", st.session_state.dg_checked)
        st.session_state.dg_expanded = res.get("expanded", st.session_state.dg_expanded)
    sel = [k2g[v] for v in st.session_state.dg_checked if v in k2g] or [default_group]
except Exception as e:                                    # never let the picker break the app
    st.sidebar.caption(f"tree view unavailable ({e}); using list")
    label_of = {_fmt(g): g for g in groups}
    picked = st.sidebar.multiselect("Data group", list(label_of), default=[_fmt(default_group)])
    sel = [label_of[l] for l in picked] if picked else [default_group]

live = st.sidebar.toggle("🔴 Live (auto-refresh)", value=True)
interval = st.sidebar.slider("Refresh (sec)", 2, 30, 5)
floor = st.sidebar.slider("Conviction floor", 0.0, 1.0, 0.20, 0.01)
genomeN = st.sidebar.slider("Genome: last N fires", 50, 800, 250, 50)

if live:
    if HAS_AR:
        st_autorefresh(interval=interval * 1000, key="ar")
    else:
        st.sidebar.caption("`pip install streamlit-autorefresh` for auto; use ⟳ Rerun")

paths = [p for g in sel for p in g["files"]]
df = load(paths, prefix.strip(), signal.strip())

if df.empty:
    st.info("Selected group has no rows yet " + (f"(prefix `{prefix}`) " if prefix else "") +
            "— it fills in as the Recorder dumps.")
    st.stop()

# ── bartype filter + time scrubber ──────────────────────────────────────────
allbt = sorted(df["bt"].unique())
pick = st.sidebar.multiselect("Bar types", allbt, allbt)
df = df[df["bt"].isin(pick)] if pick else df

tmin, tmax = df["fireTime"].min().to_pydatetime(), df["fireTime"].max().to_pydatetime()
if tmin < tmax:
    lo, hi = st.slider("⏱ Time window (fireTime, UTC)", tmin, tmax, (tmin, tmax), format="MM-DD HH:mm")
    def _utc(x):
        t = pd.Timestamp(x)
        return t.tz_localize("UTC") if t.tzinfo is None else t.tz_convert("UTC")
    df = df[(df["fireTime"] >= _utc(lo)) & (df["fireTime"] <= _utc(hi))]

if df.empty:
    st.warning("No fires in this window / selection.")
    st.stop()

# ── KPI tiles ───────────────────────────────────────────────────────────────
summ = []
for lane, g in df.groupby("lane"):
    res = g["y"].values[g["y"].values != -1]
    summ.append((lane, len(g), (100 * np.mean(res == 1) if len(res) else np.nan), eff_n(g), np.median(g["conv"])))
summ = pd.DataFrame(summ, columns=["lane", "n", "win", "effn", "conv"]).sort_values("lane")

top = st.columns(2 + len(summ))
top[0].metric("total fires", f"{len(df):,}")
top[1].metric("window", f"{(df['fireTime'].max()-df['fireTime'].min()).days}d")
for i, r in enumerate(summ.itertuples()):
    top[2 + i].metric(f"{r.lane} win%  ·  eff_n {r.effn:.0f} · conv {r.conv:.2f}",
                      f"{r.win:.1f}%" if r.win == r.win else "—")

# tick-true sidecars (real entry fills) — loaded once, shared by Decisions panel ③ and the Trade Path tab
TICK_SIZES = {"GC": 0.1, "MGC": 0.1, "NQ": 0.25, "MNQ": 0.25, "ES": 0.25, "MES": 0.25, "YM": 1.0, "CL": 0.01}
sidecar_fires, _seen_td = {}, set()
for gsel in sel:
    tdir = os.path.join(base, os.path.dirname(gsel["corpus"]), "ticks")
    if tdir in _seen_td or not os.path.isdir(tdir):
        continue
    _seen_td.add(tdir)
    tf = glob.glob(os.path.join(tdir, "*.jsonl"))
    if not tf:
        continue
    sig = (len(tf), max((os.path.getmtime(f) for f in tf), default=0.0))
    for f in load_sidecars(tdir, sig):
        sidecar_fires.setdefault((f["inst"], f["bartype"]), []).append(f)
has_ticks = bool(sidecar_fires)

tape, cal, dist, mae, genome, pathtab, sensortab = st.tabs(
    ["🎞 Decisions", "🎯 Calibration", "🔔 Distributions", "🔫 MAE / MFE", "🧬 Vote Genome",
     "🔬 Trade Path", "⚖ Sensor Truth"])

# ── 1. Decisions vs Reality ──────────────────────────────────────────────────
def _break_gaps(t, y, max_gap="2h"):
    """Insert a NaN wherever fires are separated by more than max_gap, so the price
    spine does NOT draw a straight diagonal across a weekend/session gap — a line
    through empty time reads as price data that isn't there."""
    t = pd.Series(list(t)).reset_index(drop=True)
    y = pd.Series(list(y)).reset_index(drop=True)
    big = t.diff() > pd.Timedelta(max_gap)
    xs, ys = [], []
    for i in range(len(t)):
        if i and big.iloc[i]:
            xs.append(t.iloc[i]); ys.append(np.nan)
        xs.append(t.iloc[i]); ys.append(y.iloc[i])
    return xs, ys

with tape:
    top1, top2, top3, top4, top5, top6 = st.columns([2.0, 0.9, 0.9, 1.0, 1.3, 0.9])
    mode = top1.radio("Show", ["flips only", "every fire"], horizontal=True,
                      help="The Council re-states a verdict every brick and flips direction every ~3 bricks. "
                           "'flips only' keeps just the direction-CHANGES — the actual decisions. "
                           "'every fire' shows all verdicts (dense).")
    flips_only = mode.startswith("flips")
    tgt = int(top2.number_input("target (t)", 4, 400, 20, 2, help="bracket take-profit (ticks)"))
    stp = int(top3.number_input("stop (t)", 4, 400, 20, 2, help="bracket stop-loss (ticks)"))
    be = int(top4.number_input("BE @+ (t)", 0, 400, 0, 2,
                               help="trail stop to BREAKEVEN once price reaches +N ticks favorable. "
                                    "0 = off. Walks the tick path, so it applies to the honest panel ③ only."))
    gate = top5.slider("conviction gate", 0.0, 1.0, 0.0, 0.05,
                       help="only TAKE flips with conviction ≥ this. 0 = take all (your current view). "
                            "Drag up to test whether selectivity rescues the tick-true (③) edge.")
    st.caption("Price the Council fired at (grey spine); each decision is a broker-style fill — "
               "**▲ long · ▼ short**, **green won · red lost**, grey undecided, **bigger = more conviction**. "
               "Below: an **honesty ladder** of the same flip-to-flip trades, each panel stripping one layer of "
               "optimism — **①** brick-close fills, winners unbounded (fantasy ceiling) → **②** brick-close entry "
               "but a real ±bracket exit → **③** REAL entry at the first tradeable tick, bracket walked on the "
               "actual tick path (the honest one). Watch the edge shrink — and where ③ crosses below zero, "
               "'chase every flip' loses money. Drag target/stop to re-shape the brackets.")
    df["px"] = pd.to_numeric(df.get("firePx"), errors="coerce")
    lanes = sorted(df["lane"].unique())        # one lane per INSTRUMENT+bartype (GC & NQ never merge)
    SPINE = "#46525f"
    SYM = {"LONG": "triangle-up", "SHORT": "triangle-down", "FLAT": "circle"}
    COST_TICKS = 2.0   # round-trip per flip: commission + ~1 tick/side slippage (pathlab's "great filter")

    def _flips(g):
        d = g["dir"].to_numpy()
        keep = np.ones(len(d), bool)
        keep[1:] = d[1:] != d[:-1]           # keep a row only where direction changed
        return g[keep]

    n_eq = 3 if has_ticks else 2
    rows_n = len(lanes) + n_eq
    eqA, eqB = len(lanes) + 1, len(lanes) + 2
    eqC = (len(lanes) + 3) if has_ticks else None
    heights = [(1.0 - 0.16 * n_eq) / len(lanes)] * len(lanes) + [0.16] * n_eq
    titles = [*lanes,
              "① flip-to-flip P&L — firePx fills (optimistic ceiling)",
              f"② bracket +{tgt}/−{stp}t — firePx entry, tick-true exit"]
    if has_ticks:
        titles.append(f"③ bracket +{tgt}/−{stp}t{f' · BE@+{be}' if be else ''} — REAL entry fills from ticks (honest)")
    fig = make_subplots(rows=rows_n, cols=1, shared_xaxes=True, row_heights=heights,
                        vertical_spacing=0.04, subplot_titles=titles)
    shown_total = 0
    ladder = []                                            # per-lane (bt, ①final, ②final, ③final, taken, flips)
    for li, bt in enumerate(lanes, 1):
        g = df[df["lane"] == bt].sort_values("fireTime")
        # spine is ALWAYS the full price path — context doesn't change with marker mode
        gx, gy = _break_gaps(g["fireTime"], g["px"])
        fig.add_trace(go.Scatter(x=gx, y=gy, mode="lines", line=dict(color=SPINE, width=1),
                      showlegend=False, hoverinfo="skip", connectgaps=False), li, 1)
        shown = _flips(g) if flips_only else g
        shown_total += len(shown)
        # draw undecided, then losers, then WINNERS LAST so wins are never hidden under red overplot
        for o, col, nm, op in [("censored", GREY, "undecided", 0.45), ("loss", DN, "lost", 0.7),
                               ("win", UP, "won", 0.95)]:
            gg = shown[shown["outcome"] == o]
            if gg.empty:
                continue
            fig.add_trace(go.Scatter(
                x=gg["fireTime"], y=gg["px"], mode="markers", name=nm,
                legendgroup=nm, showlegend=(li == 1),
                marker=dict(color=col, size=8 + 16 * gg["conv"].clip(0, 1),
                            symbol=[SYM[d] for d in gg["dirstr"]], line=dict(width=0), opacity=op),
                customdata=np.stack([gg["dirstr"], gg["conv"]], axis=-1),
                hovertemplate="%{x|%m-%d %H:%M} · px %{y}<br>%{customdata[0]} · conv "
                              "%{customdata[1]:.2f} · " + nm + "<extra></extra>"), li, 1)
        # equity = REALIZED flip-to-flip P&L, always (not touch labels): enter at a flip's firePx in
        # its direction, exit at the NEXT flip's firePx, in ticks, minus round-trip cost. This is the
        # actual "follow the Council's direction, flip when it flips" strategy — non-overlapping, real
        # magnitude, and it CAN lose. Independent of the marker display mode.
        fl = _flips(g).dropna(subset=["px"]).reset_index(drop=True)
        ts = TICK_SIZES.get(str(g["inst"].iloc[0])) if len(g) else None
        fA = fB = fC = None
        ntak, nflip = None, max(0, len(fl) - 1)
        if len(fl) >= 2:
            px = pd.to_numeric(fl["px"]).to_numpy()
            dn = fl["dir"].to_numpy()
            cv = pd.to_numeric(fl["conv"], errors="coerce").fillna(0.0).to_numpy()   # gate on recorded conviction
            unit = "ticks" if ts else "pts"
            # ① OPTIMISTIC: firePx entry → next firePx exit (unbounded). Gate: take legs whose entry conv ≥ gate.
            take_leg = cv[:-1] >= gate
            seg = dn[:-1] * (px[1:] - px[:-1])
            seg = seg / ts if ts else seg
            seg = np.where(take_leg, seg - COST_TICKS, 0.0)        # skipped leg = flat, no cost
            eqa = np.concatenate([[0.0], np.cumsum(seg)]); fA = float(eqa[-1])
            fig.add_trace(go.Scatter(x=fl["fireTime"], y=eqa, mode="lines", line=dict(color=INK2, width=2),
                          showlegend=False, name=f"{bt} ①",
                          hovertemplate="%{x|%m-%d %H:%M} · " + f"%{{y:+.0f}} {unit}<extra></extra>"), eqA, 1)
            # ② REALISTIC exit: firePx entry, fixed ±bracket graded on tick-true MFE/MAE (which came first).
            mfe_t = pd.to_numeric(fl["maxMFE"], errors="coerce").to_numpy()
            mae_t = pd.to_numeric(fl["maxMAE"], errors="coerce").to_numpy()
            bmf = pd.to_numeric(fl["barsToMFE"], errors="coerce").to_numpy()
            bma = pd.to_numeric(fl["barsToMAE"], errors="coerce").to_numpy()
            hitT, hitS = mfe_t >= tgt, mae_t >= stp
            brk = np.where(hitT & hitS, np.where(bmf <= bma, tgt, -stp),
                           np.where(hitT, tgt, np.where(hitS, -stp, 0.0)))
            take_flip = cv >= gate
            pnl = np.where(take_flip, brk - COST_TICKS, 0.0)       # gate per flip
            eqb = np.concatenate([[0.0], np.cumsum(pnl)]); fB = float(eqb[-1])
            fig.add_trace(go.Scatter(x=fl["fireTime"], y=eqb, mode="lines", line=dict(color=CY, width=2),
                          showlegend=False, name=f"{bt} ②",
                          hovertemplate="%{x|%m-%d %H:%M} · %{y:+.0f} ticks<extra></extra>"), eqB, 1)
        # ③ HONEST: real entry at the first tradeable tick, bracket walked on the actual tick path
        if has_ticks and ts:
            fires = sidecar_fires.get((str(g["inst"].iloc[0]), str(g["bartype"].iloc[0])))
            if fires:
                xc, eqc, ntak = ticktrue_equity(fires, tgt, stp, ts, COST_TICKS, gate, be)
                if xc is not None:
                    fC = float(eqc[-1])
                    fig.add_trace(go.Scatter(x=xc, y=eqc, mode="lines", line=dict(color=WARN, width=2),
                                  showlegend=False, name=f"{bt} ③",
                                  hovertemplate="%{x|%m-%d %H:%M} · %{y:+.0f} ticks<extra></extra>"), eqC, 1)
        ladder.append((bt, fA, fB, fC, ntak, nflip))
    for rr in [eqA, eqB] + ([eqC] if has_ticks else []):
        fig.add_hline(y=0, line=dict(color=LINE, dash="dot", width=1), row=rr, col=1)
        fig.update_yaxes(title_text="ticks", row=rr, col=1)
    top6.metric("markers shown", f"{shown_total:,}",
                help="flips-only collapses the per-brick re-statements into decision-changes")
    st.plotly_chart(style(fig, h=110 + 210 * len(lanes) + 145 * n_eq), width="stretch")
    # live ladder summary — final P&L per fill model + how many flips the gate lets through
    for bt, fA, fB, fC, ntak, nflip in ladder:
        parts = []
        if fA is not None:
            parts.append(f"① {fA:+,.0f}t")
        if fB is not None:
            parts.append(f"② {fB:+,.0f}t")
        if fC is not None:
            parts.append(f"③ {fC:+,.0f}t")
        taken = f"{ntak}/{nflip} flips taken" if ntak is not None else f"{nflip} flips"
        color = "#25d08b" if (fC is not None and fC > 0) else ("#ff5c6a" if fC is not None else "#8a97a3")
        st.markdown(f"**{bt}** &nbsp; " + " &nbsp;·&nbsp; ".join(parts) +
                    f" &nbsp;·&nbsp; gate **{gate:.2f}** → {taken}"
                    + (f" &nbsp;·&nbsp; <span style='color:{color}'>tick-true {'PROFITABLE' if (fC or 0)>0 else 'underwater'}</span>"
                       if fC is not None else ""), unsafe_allow_html=True)

# ── 2. Calibration ──────────────────────────────────────────────────────────
with cal:
    st.caption("Does conviction predict wins? Points = fires (green win / red loss), the cyan line = binned "
               "win-rate, the amber line = the conviction floor. If the cyan line rises through 50%+, conviction earns its keep.")
    bts = sorted(df["lane"].unique())
    fig = make_subplots(rows=1, cols=len(bts), subplot_titles=bts, shared_yaxes=True)
    rng = np.random.default_rng(7)
    for i, bt in enumerate(bts, 1):
        g = df[(df["lane"] == bt) & (df["y"] != -1)].copy()
        if g.empty:
            continue
        jit = g["y"].values + rng.uniform(-0.06, 0.06, len(g))
        fig.add_trace(go.Scatter(x=g["conv"], y=jit, mode="markers", showlegend=False,
                      marker=dict(color=[OUT_COLOR[o] for o in g["outcome"]], size=5, opacity=0.5)), 1, i)
        if len(g) >= 20:
            g["b"] = pd.qcut(g["conv"], min(8, g["conv"].nunique()), duplicates="drop")
            bw = g.groupby("b", observed=True).agg(c=("conv", "mean"), w=("y", "mean")).dropna()
            fig.add_trace(go.Scatter(x=bw["c"], y=bw["w"], mode="lines+markers", showlegend=False,
                          line=dict(color=CY, width=2)), 1, i)
        fig.add_vline(x=floor, line=dict(color=WARN, dash="dash", width=1), row=1, col=i)
        fig.add_hline(y=0.5, line=dict(color=LINE, dash="dot", width=1), row=1, col=i)
    fig.update_yaxes(range=[-0.2, 1.2], tickvals=[0, 1], ticktext=["loss", "win"])
    fig.update_xaxes(title_text="conviction")
    st.plotly_chart(style(fig, h=460), width="stretch")

# ── 3. Distributions (bell curves) ──────────────────────────────────────────
with dist:
    st.caption("The conviction distribution per bar type — do they even sample the same range? "
               "The amber line is the floor; mass to its right is tradeable conviction.")
    fig = go.Figure()
    for bt in sorted(df["lane"].unique()):
        g = df[df["lane"] == bt]
        fig.add_trace(go.Violin(x=g["lane"], y=g["conv"], name=bt, box_visible=True, meanline_visible=True,
                      points=False, line_color=CY, fillcolor="rgba(43,212,230,0.15)"))
    fig.add_hline(y=floor, line=dict(color=WARN, dash="dash", width=1.5))
    fig.update_yaxes(title_text="conviction", range=[0, max(0.05, df["conv"].max() * 1.1)])
    st.plotly_chart(style(fig, h=460), width="stretch")

# ── 4. MAE / MFE shotgun ────────────────────────────────────────────────────
with mae:
    have = "maxMAE" in df.columns and "maxMFE" in df.columns
    if not have:
        st.info("No maxMAE / maxMFE fields in this corpus.")
    else:
        st.caption("Each fire: worst adverse move (x) vs best favorable move (y), colored by outcome. "
                   "The cloud's shape shows where stops and targets bite — and why one bar type has edge.")
        g = df.dropna(subset=["maxMAE", "maxMFE"])
        fig = go.Figure()
        for o in ["win", "loss", "censored"]:
            gg = g[g["outcome"] == o]
            fig.add_trace(go.Scatter(x=pd.to_numeric(gg["maxMAE"], errors="coerce"),
                          y=pd.to_numeric(gg["maxMFE"], errors="coerce"), mode="markers", name=o,
                          marker=dict(color=OUT_COLOR[o], size=6, opacity=0.55,
                                      symbol=[["circle", "diamond", "square", "x"][hash(b) % 4] for b in gg["bt"]])))
        m = pd.to_numeric(g[["maxMAE", "maxMFE"]].stack(), errors="coerce").max()
        fig.add_trace(go.Scatter(x=[0, m], y=[0, m], mode="lines", name="1:1",
                      line=dict(color=LINE, dash="dot"), showlegend=False))
        fig.update_xaxes(title_text="max adverse excursion (ticks)")
        fig.update_yaxes(title_text="max favorable excursion (ticks)")
        st.plotly_chart(style(fig, h=520), width="stretch")

# ── 5. Vote Genome ──────────────────────────────────────────────────────────
with genome:
    st.caption("A barcode of the decisions: each row a fire (newest at top), each column a voter. "
               "Green = agreed with the taken side, red = against, grey = abstained/absent.")
    g = df.tail(genomeN).copy()

    def folded(m, tag, d):
        if not isinstance(m, dict) or tag not in m:
            return np.nan
        return float(m[tag]) * (1 if d >= 0 else -1)   # fold by direction: +1 = agreed with the side

    Z = np.array([[folded(r["votes"], t, r["dir"]) for t in VOTERS] for _, r in g.iterrows()], dtype=float)
    Z = Z[::-1]  # newest at top
    ylab = [t.strftime("%m-%d %H:%M") for t in g["fireTime"]][::-1]
    fig = go.Figure(go.Heatmap(
        z=Z, x=VOTERS, y=ylab, zmin=-1, zmax=1,
        colorscale=[[0, DN], [0.5, "#2a3340"], [1, UP]], showscale=False,
        hovertemplate="%{y} · %{x} = %{z}<extra></extra>", xgap=1, ygap=0))
    fig.update_yaxes(showticklabels=len(g) <= 60)
    st.plotly_chart(style(fig, h=max(320, min(900, 16 * len(g)))), width="stretch")

# ── 6. Trade Path (folds in the old Trade Explorer, sourced from the tick sidecars) ──────────
with pathtab:
    st.caption("Every fire's ACTUAL tick path — favorable excursion (ticks from entry, entry = 0) over time. "
               "Green above / red below; dotted lines mark MFE, −MAE, and the ATR barrier. The honest shape of a "
               "single decision, straight from the sidecar (this replaces the old Trade Explorer + its sentinel.db).")
    if not has_ticks:
        st.info("No tick sidecars for the selected group. Groups baked with a `ticks/` sidecar dir "
                "(e.g. the legacy-node replay corpus) expose per-trade paths here.")
    else:
        allf = [f for lst in sidecar_fires.values() for f in lst]
        bl = pd.DataFrame([{
            "fireTime": pd.to_datetime(f["t"], utc=True), "inst": f["inst"], "bt": bt_label(f["bartype"]),
            "dir": "LONG" if f["dir"] > 0 else "SHORT",
            "conv": round(f["conv"], 3) if f.get("conv") is not None else None,
            "MFE_t": f.get("mfe"), "MAE_t": f.get("mae"), "barrier_t": f.get("barrier"), "fireId": f["fid"]}
            for f in allf]).sort_values("fireTime", ascending=False).reset_index(drop=True)
        idmap = {f["fid"]: f for f in allf}
        opts = bl["fireId"].tolist()
        if st.session_state.get("tp_selbox") not in opts:      # tp_selbox = the selectbox key = single source of truth
            st.session_state.tp_selbox = opts[0]

        def _row_clicked():
            ev = st.session_state.get("tp_blotter")
            rows = []
            try:
                rows = ev["selection"]["rows"]                 # dict-style access
            except Exception:
                try:
                    rows = list(ev.selection.rows)             # attribute-style fallback
                except Exception:
                    rows = []
            if rows and 0 <= rows[0] < len(opts):
                st.session_state.tp_selbox = opts[rows[0]]      # write the SELECTBOX's own key → both stay in sync

        colL, colR = st.columns([3, 2])
        colL.caption("click a row to load its path →")
        colL.dataframe(bl.drop(columns=["fireId"]), width="stretch", height=440, hide_index=True,
                       key="tp_blotter", on_select=_row_clicked, selection_mode="single-row")
        colR.selectbox(f"record  ({len(bl):,} fires)", opts, key="tp_selbox")
        f = idmap.get(st.session_state.tp_selbox)
        if f is not None and f["px"].size:
            tk = TICK_SIZES.get(f["inst"], 0.1)
            e = f["px"][0]
            fav = f["dir"] * (f["px"] - e) / tk               # favorable ticks from the real entry
            sec = f["ms"] / 1000.0
            mfe = float(f["mfe"]) if f.get("mfe") is not None else float(np.nanmax(fav))
            mae = float(f["mae"]) if f.get("mae") is not None else float(-np.nanmin(fav))
            side_s = "LONG" if f["dir"] > 0 else "SHORT"
            pf = go.Figure()
            pf.add_hline(y=0, line=dict(color=INK2, width=1), annotation_text="entry")
            pf.add_hline(y=mfe, line=dict(color=UP, dash="dot", width=1), annotation_text=f"MFE {mfe:.0f}t")
            pf.add_hline(y=-mae, line=dict(color=DN, dash="dot", width=1), annotation_text=f"MAE {mae:.0f}t")
            if f.get("barrier"):
                pf.add_hline(y=float(f["barrier"]), line=dict(color=WARN, dash="dash", width=1),
                             annotation_text=f"barrier ±{float(f['barrier']):.0f}t")
                pf.add_hline(y=-float(f["barrier"]), line=dict(color=WARN, dash="dash", width=1))
            pf.add_trace(go.Scatter(x=sec, y=np.clip(fav, 0, None), fill="tozeroy", line=dict(width=0),
                         fillcolor="rgba(37,208,139,0.15)", hoverinfo="skip", showlegend=False))
            pf.add_trace(go.Scatter(x=sec, y=np.clip(fav, None, 0), fill="tozeroy", line=dict(width=0),
                         fillcolor="rgba(255,92,106,0.15)", hoverinfo="skip", showlegend=False))
            pf.add_trace(go.Scatter(x=sec, y=fav, mode="lines", line=dict(color=CY, width=1.6),
                         name="favorable ticks", hovertemplate="%{x:.0f}s · %{y:.0f}t<extra></extra>"))
            pf.update_xaxes(title_text="seconds from entry")
            pf.update_yaxes(title_text="favorable excursion (ticks) · entry = 0")
            ft = "fav first" if f.get("ft") == 1 else ("adv first" if f.get("ft") == -1 else "—")
            colR.plotly_chart(style(pf, h=420,
                              title=f"{side_s} {f['inst']} · {pd.to_datetime(f['t']):%m-%d %H:%M} · first touch: {ft}"),
                              width="stretch")

# ── 7. Sensor Truth Table (Phase-1 edge discovery: each sensor's STANDALONE tick-true edge) ──
with sensortab:
    st.caption("Decompose the fused verdict into **each sensor's standalone tick-true edge**: for every brick a "
               "sensor votes, enter in ITS direction at the real first-tick fill, walk the actual tick path to "
               "the ±bracket. A sensor **survives only if BOTH in-sample AND holdout expR are > 0** — the "
               "time-split guards against small-sample mirages. This is where we find the pieces that work.")
    if not has_ticks:
        st.info("Needs tick sidecars — pick a group with a `ticks/` sibling (e.g. the legacy-node replay corpus).")
    elif "votes" not in df.columns:
        st.info("This corpus has no vote vector (needs schema 1.3+). Bake with the Council recording per-voter votes.")
    else:
        COST = 2.0
        cA, cB, cC, cD, cE = st.columns(5)
        sT = int(cA.number_input("target (t)", 4, 400, 20, 2, key="st_t"))
        sS = int(cB.number_input("stop (t)", 4, 400, 20, 2, key="st_s"))
        sBE = int(cC.number_input("BE @+ (t)", 0, 400, 0, 2, key="st_be", help="trail to breakeven at +N ticks"))
        holdout = cD.slider("holdout %", 0, 50, 30, 5, key="st_ho", help="last N% of fires = out-of-sample test")
        minv = int(cE.number_input("min votes", 10, 5000, 40, 10, key="st_mv", help="hide sensors below this sample"))

        def _walk(px, d, tk):
            rel = d * (px - px[0]) / tk
            stop, res = -float(sS), None
            for x in rel:
                if sBE and stop < 0.0 and x >= sBE:
                    stop = 0.0
                if x >= sT:
                    res = sT
                    break
                if x <= stop:
                    res = stop
                    break
            return (rel[-1] if res is None else res) - COST

        for bt in sorted(df["lane"].unique()):
            g = df[df["lane"] == bt].sort_values("fireTime")
            inst, bartype = str(g["inst"].iloc[0]), str(g["bartype"].iloc[0])
            fires = sidecar_fires.get((inst, bartype))
            if not fires:
                continue
            tk = TICK_SIZES.get(inst, 0.1)
            pmap = {pd.to_datetime(f["t"], utc=True): f["px"] for f in fires}
            recs = []                                       # (votes, path), time-ordered
            for _, r in g.iterrows():
                v = r.get("votes")
                path = pmap.get(r["fireTime"])
                if isinstance(v, dict) and v and path is not None and len(path):
                    recs.append((v, path))
            if len(recs) < 2:
                continue
            cut = int(len(recs) * (1 - holdout / 100.0))
            tags = sorted({t for v, _ in recs for t in v})
            table = []
            for s in tags:
                tr = [_walk(px, v[s], tk) for v, px in recs[:cut] if v.get(s, 0) != 0]
                te = [_walk(px, v[s], tk) for v, px in recs[cut:] if v.get(s, 0) != 0]
                allp = tr + te
                if len(allp) < minv:
                    continue
                table.append(dict(sensor=s, n=len(allp), expR=float(np.mean(allp)),
                                  train=float(np.mean(tr)) if tr else np.nan,
                                  test=float(np.mean(te)) if te else np.nan, ntest=len(te),
                                  win=100 * float(np.mean(np.array(allp) > 0)), total=float(np.sum(allp))))
            if not table:
                st.markdown(f"**{bt}** — no sensor cleared min votes {minv}.")
                continue
            tdf = pd.DataFrame(table).sort_values("expR", ascending=False).reset_index(drop=True)
            surv = tdf[(tdf["train"] > 0) & (tdf["test"] > 0)]
            st.markdown(f"**{bt}** — {len(recs):,} fires · holdout last {holdout}% · "
                        + (f"<span style='color:#25d08b'>survivors: {', '.join(surv['sensor'])}</span>"
                           if len(surv) else "<span style='color:#ff5c6a'>no survivors — nothing beats fills both in- and out-of-sample</span>"),
                        unsafe_allow_html=True)
            fig = go.Figure()
            fig.add_trace(go.Bar(x=tdf["sensor"], y=tdf["train"], name="in-sample expR", marker_color=INK2))
            fig.add_trace(go.Bar(x=tdf["sensor"], y=tdf["test"], name="holdout expR", marker_color=CY))
            fig.add_hline(y=0, line=dict(color=LINE, dash="dot", width=1))
            fig.update_yaxes(title_text="expR (ticks / trade, net cost)")
            st.plotly_chart(style(fig, h=360,
                            title="standalone tick-true edge — a sensor is real only if BOTH bars clear 0"),
                            width="stretch")
            show = tdf.copy()
            for c in ("expR", "train", "test"):
                show[c] = show[c].round(2)
            show["win"] = show["win"].round(1)
            show["total"] = show["total"].round(0)
            st.dataframe(show[["sensor", "n", "expR", "train", "test", "ntest", "win", "total"]],
                         hide_index=True, width="stretch")

st.caption(f"{len(sel)} group(s): " + " · ".join(f'{g["inst"]} {g["friendly"]} [{_src(g)}]' for g in sel) +
           f"  ·  {len(df):,} fires  ·  {'🔴 live' if live else 'paused'}  ·  reuses sentinel_lab (matches train.py)")
