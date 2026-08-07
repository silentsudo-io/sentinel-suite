#!/usr/bin/env python3
# =============================================================================
#  Sentinel Lab · news-overlap audit
#  ---------------------------------------------------------------------------
#  Joins Council excursion fires (schema 1.3) to the historical ForexFactory
#  red-folder blackout windows and answers the question that decides whether
#  the current baseline is trustworthy:
#
#     "How many of my 'clean' replayed fires actually happened inside a
#      news blackout that LIVE trading would have refused?"
#
#  and the follow-on:
#
#     "Does removing those fires change the first-touch win rate?"
#
#  This is the pre-wiring measurement. Once SentinelCore.NewsLockoutAt(Time[0])
#  lands and the replay is re-run, in-blackout Council fires should DISAPPEAR
#  from the corpus -- this script quantifies what that will remove and whether
#  it flatters or corrects the measured edge.
#
#  stdlib only. Usage:
#     python news_overlap.py                                  # all 1.3 corpus
#     python news_overlap.py --dir _exp0002 --bartype 212201v6x24
#     python news_overlap.py --news ../News/history/ff_usd_red.jsonl
# =============================================================================
import argparse, json, os, glob, bisect
import datetime as dt
from lab_faults import swallow

BASE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(BASE, "..", "Excursions", "council", "1.3")
NEWS = os.path.join(BASE, "..", "News", "history", "ff_usd_red.jsonl")


def parse_iso_utc(s):
    """Robustly parse '2025-12-09T05:00:41.1240000Z' -> epoch seconds (float)."""
    s = s.strip()
    if s.endswith("Z"):
        s = s[:-1]
    if "." in s:
        head, frac = s.split(".", 1)
        frac = "".join(ch for ch in frac if ch.isdigit())[:6]  # microseconds max
        s = head + "." + frac if frac else head
    d = dt.datetime.fromisoformat(s).replace(tzinfo=dt.timezone.utc)
    return d.timestamp()


def load_news_windows(path):
    """Return sorted list of (start_epoch, end_epoch, name)."""
    wins = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            e = json.loads(line)
            ep = e["epoch"]
            b = e.get("beforeMin", 15) * 60
            a = e.get("afterMin", 15) * 60
            wins.append((ep - b, ep + a, e.get("name")))
    wins.sort()
    return wins


def in_blackout(wins, starts, ep):
    """True if ep falls in any [start,end] window. `starts` is the sorted start list."""
    i = bisect.bisect_right(starts, ep) - 1
    # walk back a few in case of overlapping windows
    j = i
    while j >= 0 and j >= i - 6:
        s, e, _ = wins[j]
        if s <= ep <= e:
            return True, wins[j][2]
        if e < ep - 3600:  # far past, stop
            break
        j -= 1
    return False, None


def load_corpus(corpus_dir, subdir, bartype):
    d = os.path.join(corpus_dir, subdir) if subdir else corpus_dir
    files = sorted(glob.glob(os.path.join(d, "*.jsonl")))
    rows = []
    for fp in files:
        with open(fp, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    o = json.loads(line)
                except Exception as _swex:
                    swallow("news_overlap.load_corpus", _swex)
                    continue
                if o.get("signal") != "COUNCIL":
                    continue
                if bartype and o.get("bartype") != bartype:
                    continue
                rows.append(o)
    return rows


def winrate(rows):
    w = sum(1 for r in rows if r.get("firstTouch", 0) > 0)
    l = sum(1 for r in rows if r.get("firstTouch", 0) < 0)
    n = w + l
    return (100.0 * w / n if n else float("nan")), w, l, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default=CORPUS)
    ap.add_argument("--dir", dest="subdir", default="", help="subdir under corpus (e.g. _exp0002)")
    ap.add_argument("--news", default=NEWS)
    ap.add_argument("--bartype", default="", help="filter to one scope tag (else per-bartype breakdown)")
    args = ap.parse_args()

    wins = load_news_windows(args.news)
    starts = [w[0] for w in wins]
    rows = load_corpus(args.corpus, args.subdir, args.bartype)
    print(f"news windows: {len(wins)}   corpus COUNCIL fires: {len(rows)}")
    if not rows:
        return

    for r in rows:
        try:
            ep = parse_iso_utc(r["fireTime"])
        except Exception:
            r["_blk"] = None
            continue
        blk, nm = in_blackout(wins, starts, ep)
        r["_blk"] = blk
        r["_blkName"] = nm

    valid = [r for r in rows if r.get("_blk") is not None]
    inb = [r for r in valid if r["_blk"]]
    outb = [r for r in valid if not r["_blk"]]
    print(f"\n=== OVERALL ===")
    print(f"  inside blackout : {len(inb):5d}  ({100.0*len(inb)/len(valid):.1f}% of fires)")
    print(f"  outside         : {len(outb):5d}")
    wa, *_ = winrate(valid); wi, *_ = winrate(inb); wo, *_ = winrate(outb)
    print(f"  win%  all={wa:.1f}   in-blackout={wi:.1f}   out={wo:.1f}")

    # per bartype
    bts = sorted(set(r.get("bartype") for r in valid))
    if len(bts) > 1:
        print(f"\n=== BY BAR TYPE ===")
        print(f"  {'bartype':<16} {'fires':>6} {'in_blk':>7} {'%blk':>6} {'win_all':>8} {'win_out':>8} {'win_in':>7}")
        for bt in bts:
            g = [r for r in valid if r.get("bartype") == bt]
            gi = [r for r in g if r["_blk"]]
            go = [r for r in g if not r["_blk"]]
            wa, *_ = winrate(g); wo, *_ = winrate(go); wi, *_ = winrate(gi)
            pct = 100.0 * len(gi) / len(g) if g else 0
            print(f"  {bt:<16} {len(g):>6} {len(gi):>7} {pct:>5.1f}% {wa:>7.1f}% {wo:>7.1f}% {wi:>6.1f}%")

    # which events catch the most fires
    from collections import Counter
    c = Counter(r.get("_blkName") for r in inb if r.get("_blkName"))
    if c:
        print(f"\n=== fires-in-blackout by event (top 12) ===")
        for nm, k in c.most_common(12):
            print(f"  {k:4d}  {nm}")


if __name__ == "__main__":
    main()
