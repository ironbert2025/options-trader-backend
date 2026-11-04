import os,glob,statistics as st,sys
R=r'C:\OptionsData\MarketData\TicksLevelOne'
def load(path):
    out=[]
    for l in open(path,encoding='utf-8',errors='ignore').read().splitlines()[1:]:
        p=l.split(',')
        if len(p)<2: continue
        try:
            h,m,s=p[0].split(':'); t=int(h)*3600+int(m)*60+float(s); px=float(p[1])
        except: continue
        if px<=0: continue
        out.append((t,px))
    out.sort(); return out
O=9*3600+30*60; E=O+900
def first_candle(ticks):
    return [(t,p) for t,p in ticks if O<=t<E]
def simulate(tk,floor,ratio):
    op=tk[0][1]; hi=lo=op; fire=None; cross=None
    for t,p in tk:
        hi=max(hi,p); lo=min(lo,p)
        for side in ('dn','up'):
            wick=(hi-op) if side=='dn' else (op-lo)   # wick on the opposite side of the body
            body=(op-p) if side=='dn' else (p-op)
            if wick>=floor and body>0:
                if cross is None: cross=(t,side)
                if fire is None and body>=ratio*wick: fire=(t,side,wick,body)
    return fire,cross
res={}
syms=sys.argv[1:] or ['AAPL','TSLA','NFLX','NVDA','SPY','QQQ','IWM','DIA']
for sym in syms:
    files=sorted(glob.glob(os.path.join(R,sym,f'{sym}_L1Ticks_*.csv')))
    days=[]
    for f in files:
        tk=first_candle(load(f))
        if len(tk)>=50 and tk[0][0]<O+60: days.append((os.path.basename(f)[-12:-4],tk))
    if len(days)<8: print(sym,'few days',len(days)); continue
    rngs=[max(p for _,p in tk)-min(p for _,p in tk) for _,tk in days]
    print(f'\n== {sym}: {len(days)} days, first-candle range median {st.median(rngs):.3f}')
    for fl in (0.15,0.25,0.35):
        for ra in (0.3,0.4,0.5):
            fires=[];crosses=[];ok=0;n=0
            for i,(d,tk) in enumerate(days):
                prev=rngs[max(0,i-20):i] or rngs[:i+1]
                floor=fl*st.median(prev)
                f,c=simulate(tk,floor,ra)
                if c: crosses.append(c[0]-O)
                if f:
                    fires.append(f[0]-O); n+=1
                    last=tk[-1][1]; op=tk[0][1]
                    if (f[1]=='dn' and last<op) or (f[1]=='up' and last>op): ok+=1
            if fires:
                print(f' floor={fl:.2f}xmed ratio={ra:.1f}: fires {len(fires)}/{len(days)} days; fire median {st.median(fires)/60:.1f} min (p25 {sorted(fires)[len(fires)//4]/60:.1f}, p75 {sorted(fires)[3*len(fires)//4]/60:.1f}); cross median {st.median(crosses)/60:.1f} min; candle closes on body side {ok}/{n}')
            else: print(f' floor={fl:.2f} ratio={ra:.1f}: 0 fires')
