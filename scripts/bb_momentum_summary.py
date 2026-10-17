import statistics, sys
sys.path.insert(0, '.')
import bb_momentum_calibrate as c

syms = ['AAPL', 'TSLA', 'NFLX', 'NVDA', 'SPY', 'QQQ']
policies = ['immediate', 'cool60', 'candle', 'mid']
NDAYS = 10
print(f'{"sym":5} {"policy":10} {"val/day":>8} {"touch/day":>9} {"brk/day":>8} {"pend/day":>8} {"med VAL->TOUCH s":>17} {"%val<30s after touch":>21}')
for sym in syms:
    days = c.trading_days(sym)
    targets = days[-NDAYS:]
    for pol in policies:
        V = T = B = P = n = 0
        gaps = []; chatter = 0; total_after_touch = 0
        for ymd in targets:
            idx = days.index(ymd)
            if idx < 2: continue
            ctx = []
            for pd in days[idx-2:idx]:
                tk = c.parse_day(sym, pd)
                if tk: ctx += c.completed_closes(tk)
            tk = c.parse_day(sym, ymd)
            if not tk or len(ctx) < 20: continue
            ev, pend = c.run_day(ctx, tk, 5, rearm=pol)
            n += 1
            V += sum(e[1]=='VAL' for e in ev); T += sum(e[1]=='TOUCH' for e in ev)
            B += sum(e[1]=='BROKEN' for e in ev); P += len(pend)
            last_val = {}; last_touch = {}
            for t, k, d, s in ev:
                if k == 'VAL':
                    if d in last_touch:
                        total_after_touch += 1
                        if t - last_touch[d] < 30: chatter += 1
                    last_val[d] = t
                elif k == 'TOUCH':
                    if d in last_val: gaps.append(t - last_val[d])
                    last_touch[d] = t
        if n == 0: continue
        med = statistics.median(gaps) if gaps else float('nan')
        pct = (100.0 * chatter / total_after_touch) if total_after_touch else 0.0
        print(f'{sym:5} {pol:10} {V/n:8.1f} {T/n:9.1f} {B/n:8.1f} {P/n:8.1f} {med:17.1f} {pct:20.0f}%')
