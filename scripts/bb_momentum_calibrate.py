"""Offline calibration of the Bollinger-momentum detector over real Level 1 ticks.

Mirrors what the Simulator will do: 15m RTH candles built from L1 ticks (bucket = floor((t-9:30)/15m),
close = last tick of the bucket), Bollinger(20, 2, population std) where the FORMING candle's close is the
current spot; "previous" values = same bands ending at the last COMPLETED candle (yesterday's last candle
for the first bucket of the day).
"""
import bisect, math, os, statistics, sys

L1 = r'C:\OptionsData\MarketData\TicksLevelOne'
RTH_START = 9 * 3600 + 30 * 60
RTH_END = 16 * 3600
BUCKET = 15 * 60


def parse_day(sym, ymd):
    path = os.path.join(L1, sym, f'{sym}_L1Ticks_{ymd}.csv')
    if not os.path.exists(path):
        return None
    out = []
    with open(path, encoding='utf-8') as f:
        next(f, None)
        for line in f:
            parts = line.strip().split(',')
            if len(parts) < 2:
                continue
            try:
                h, m, s = parts[0].split(':')
                t = int(h) * 3600 + int(m) * 60 + float(s)
                p = float(parts[1])
            except ValueError:
                continue
            if p <= 0:
                continue
            out.append((t, p))
    out.sort()
    # same isolated-spike filter as SimulationDataLoader.RemoveOutlierTicks (0.4%)
    if len(out) >= 3:
        keep = [out[0]]
        for i in range(1, len(out) - 1):
            prev, cur, nxt = out[i - 1][1], out[i][1], out[i + 1][1]
            if prev > 0 and abs(cur - prev) / prev >= 0.004 and abs(nxt - prev) / prev < 0.004:
                continue
            keep.append(out[i])
        keep.append(out[-1])
        out = keep
    return [x for x in out if RTH_START <= x[0] < RTH_END]


def completed_closes(ticks):
    """Close of every RTH 15m bucket of a whole day (all buckets are complete)."""
    last = {}
    for t, p in ticks:
        last[int((t - RTH_START) // BUCKET)] = p
    return [last[k] for k in sorted(last)]


def fmt(t):
    h = int(t // 3600); m = int((t % 3600) // 60); s = t % 60
    return f'{h:02d}:{m:02d}:{s:06.3f}'


def run_day(prev_closes, ticks, window_s, rearm='immediate'):
    closes = list(prev_closes)
    times = [t for t, _ in ticks]
    state = {'bull': 'idle', 'bear': 'idle'}
    locked = {'bull': False, 'bear': False}  # post-touch lock for rearm policies
    lock_bucket = {'bull': None, 'bear': None}
    broken = {'bull': False, 'bear': False}
    val_t = {}
    ev = []  # (time, kind, dir, spot)
    cur_bucket = None
    cur_last = None
    mid_prev = up_prev = lo_prev = None
    s19 = q19 = 0.0

    def refresh_prev():
        nonlocal mid_prev, up_prev, lo_prev, s19, q19
        w20 = closes[-20:]
        if len(w20) < 20:
            mid_prev = up_prev = lo_prev = None
        else:
            mean = sum(w20) / 20
            sd = math.sqrt(sum((x - mean) ** 2 for x in w20) / 20)
            mid_prev, up_prev, lo_prev = mean, mean + 2 * sd, mean - 2 * sd
        w19 = closes[-19:]
        s19 = sum(w19)
        q19 = sum(x * x for x in w19)

    refresh_prev()
    for i, (t, spot) in enumerate(ticks):
        b = int((t - RTH_START) // BUCKET)
        if cur_bucket is None:
            cur_bucket = b
        elif b != cur_bucket:
            closes.append(cur_last)       # forming candle completed
            cur_bucket = b
            refresh_prev()
        cur_last = spot
        if mid_prev is None:
            continue
        mid = (s19 + spot) / 20
        sd = math.sqrt(max((q19 + spot * spot) / 20 - mid * mid, 0.0))
        up, lo = mid + 2 * sd, mid - 2 * sd
        j = bisect.bisect_right(times, t - window_s) - 1
        ago = ticks[j][1] if j >= 0 else None
        up_move = ago is not None and spot > ago
        dn_move = ago is not None and spot < ago

        struct = {
            'bull': mid > mid_prev and up > up_prev and mid < spot < up,
            'bear': mid < mid_prev and lo < lo_prev and lo < spot < mid,
        }
        move = {'bull': up_move, 'bear': dn_move}
        touched = {'bull': spot >= up, 'bear': spot <= lo}
        for d in ('bull', 'bear'):
            if state[d] == 'idle':
                if locked[d]:
                    if rearm == 'mid' and ((d == 'bull' and spot <= mid) or (d == 'bear' and spot >= mid)): locked[d] = False
                    elif rearm == 'candle' and b != lock_bucket[d]: locked[d] = False
                    elif rearm == 'cool60' and t - lock_bucket[d] >= 60: locked[d] = False
                    if locked[d]: continue
                if struct[d] and move[d]:
                    state[d] = 'pending'; broken[d] = False; val_t[d] = t
                    ev.append((t, 'VAL', d, spot))
            else:
                if touched[d]:
                    ev.append((t, 'TOUCH', d, spot)); state[d] = 'idle'
                    if rearm != 'immediate':
                        locked[d] = True; lock_bucket[d] = b if rearm == 'candle' else t
                elif not struct[d] and not broken[d]:
                    broken[d] = True
                    ev.append((t, 'BROKEN', d, spot))
    pend = [d for d in state if state[d] == 'pending']
    return ev, pend


def trading_days(sym):
    d = os.path.join(L1, sym)
    return sorted(f.split('_')[-1][:8] for f in os.listdir(d) if f.endswith('.csv'))


def main():
    syms = sys.argv[1].split(',') if len(sys.argv) > 1 else ['TSLA']
    ndays = int(sys.argv[2]) if len(sys.argv) > 2 else 10
    windows = [1, 3, 5, 10, 30]
    for sym in syms:
        days = trading_days(sym)
        # need 2 previous days of context + the day itself
        targets = days[-ndays:]
        print(f'\n=== {sym} ===')
        agg = {w: [0, 0, 0, 0] for w in windows}  # val, touch, broken, pending-at-eod
        for ymd in targets:
            idx = days.index(ymd)
            if idx < 2:
                continue
            ctx = []
            for pd in days[idx - 2:idx]:
                tk = parse_day(sym, pd)
                if tk:
                    ctx += completed_closes(tk)
            tk = parse_day(sym, ymd)
            if not tk or len(ctx) < 20:
                continue
            row = []
            for w in windows:
                ev, pend = run_day(ctx, tk, w)
                v = sum(1 for e in ev if e[1] == 'VAL'); t_ = sum(1 for e in ev if e[1] == 'TOUCH')
                b = sum(1 for e in ev if e[1] == 'BROKEN')
                agg[w][0] += v; agg[w][1] += t_; agg[w][2] += b; agg[w][3] += len(pend)
                row.append(f'{w}s:{v}v/{t_}t/{b}b')
                if w == 5 and ymd == targets[-1]:
                    last5 = ev
            print(ymd, '  '.join(row))
        print('TOTAL ', '  '.join(f'{w}s:{a[0]}v/{a[1]}t/{a[2]}b/{a[3]}p' for w, a in agg.items()))
        if 'last5' in dir():
            print(f'-- sample events (5s window) last day {targets[-1]}:')
            for t, k, d, s in last5[:14]:
                print('  ', fmt(t), k, d, f'{s:.2f}')


if __name__ == '__main__':
    main()
