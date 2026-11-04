# Compares the 15m RTH candles built from the recorded Level 1 ticks (what the Simulator and the live
# tick-decided buckets produce) against Schwab's official 15m history for one day.
# Usage: python scripts/compare_candles_vs_official.py AAPL 20261016   (needs a valid access token: hub open)
import json,os,csv,time,datetime,urllib.request,urllib.parse,sys
sym=sys.argv[1]; day=sys.argv[2]   # AAPL 20261016
d=json.load(open(os.path.expandvars(r'%APPDATA%\OptionsTrader\schwab_tokens.json')))
tok=d['AccessToken']
tz=datetime.timezone(datetime.timedelta(hours=-4))
dd=datetime.datetime.strptime(day,'%Y%m%d')
def ms(h,m): return int(dd.replace(hour=h,minute=m,tzinfo=tz).timestamp()*1000)
q=dict(symbol=sym,periodType='day',frequencyType='minute',frequency=15,startDate=ms(9,0),endDate=ms(16,5),needExtendedHoursData='false')
r=urllib.request.Request('https://api.schwabapi.com/marketdata/v1/pricehistory?'+urllib.parse.urlencode(q),headers={'Authorization':'Bearer '+tok})
try: c=json.loads(urllib.request.urlopen(r,timeout=30).read()).get('candles',[])
except Exception as e: print('REST error',e); sys.exit(1)
official={}
for x in c:
    t=datetime.datetime.fromtimestamp(x['datetime']/1000,tz)
    if t.strftime('%Y%m%d')==day and '09:30'<=t.strftime('%H:%M')<'16:00':
        official[t.strftime('%H:%M')]=(x['open'],x['high'],x['low'],x['close'],x['volume'])
# L1 ticks aggregated by tick time (what the Simulator and the new live logic do)
ticks=[(r[0],float(r[1])) for r in csv.reader(open(rf'C:/OptionsData/MarketData/TicksLevelOne/{sym}/{sym}_L1Ticks_{day}.csv')) if r[0]!='Time']
ticks=[(t,p) for t,p in ticks if p>0 and '09:30:00'<=t<'16:00:00']
def bucket(t):
    h,m=int(t[:2]),int(t[3:5]); mins=(h*60+m)-(9*60+30); b=mins//15*15+9*60+30
    return f'{b//60:02d}:{b%60:02d}'
agg={}
for t,p in ticks:
    k=bucket(t)
    if k not in agg: agg[k]=[p,p,p,p]
    a=agg[k]; a[1]=max(a[1],p); a[2]=min(a[2],p); a[3]=p
print(f'{sym} {day}: official candles {len(official)}, tick-aggregated candles {len(agg)}, ticks {len(ticks)}')
print('time   | official O/H/L/C            | ticks O/H/L/C               | diff O  H  L  C')
bad=0
for k in sorted(official):
    o=official[k][:4]; a=agg.get(k)
    if not a: print(k,'no ticks'); continue
    df=[round(a[i]-o[i],2) for i in range(4)]
    flag='' if all(abs(x)<=0.05 for x in df) else '  <--'
    if flag: bad+=1
    print(f'{k}  | {o[0]:8.2f} {o[1]:8.2f} {o[2]:8.2f} {o[3]:8.2f} | {a[0]:8.2f} {a[1]:8.2f} {a[2]:8.2f} {a[3]:8.2f} | {df[0]:+.2f} {df[1]:+.2f} {df[2]:+.2f} {df[3]:+.2f}{flag}')
print('candles with any |diff| > 0.05:',bad,'of',len(official))
