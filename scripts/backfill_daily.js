// Backfills C:\OptionsData\MarketData\Candles\{Symbol}_Daily.csv with real daily OHLC from
// Yahoo Finance's unofficial chart API (no signup/API key needed), merged with whatever's already
// there (Yahoo wins on any date collision — unlike backfill_hourly.js, this store has no
// app-collected data of its own to protect), trimmed to the most recent MAX_CANDLES rows.
//
// Exists because HourlyCandleStore (the Daily chart's other data source, aggregated up from
// hourly bars) caps at 1500 hourly rows — Yahoo's 60m granularity itself only covers ~2 years,
// nowhere near enough runway for SMA100/200 to keep growing. Yahoo's 1d interval has no such
// practical limit, so this store can hold many years instead — see DailyCandleStore.cs and
// ChartPanel.GetLastDailyCandles, which merges the two (this one for depth, HourlyCandleStore's
// own daily aggregation for the most recent/still-forming days).
//
// Usage: node scripts/backfill_daily.js SYMBOL [SYMBOL ...]
//   e.g. node scripts/backfill_daily.js TSLA AAPL NVDA NFLX
const fs = require('fs');
const https = require('https');

const SYMBOLS = process.argv.slice(2);
if (SYMBOLS.length === 0) {
  console.error('Usage: node scripts/backfill_daily.js SYMBOL [SYMBOL ...]');
  process.exit(1);
}

const OUT_DIR = 'C:\\OptionsData\\MarketData\\Candles';
const MAX_CANDLES = 3000; // ~12 years — matches DailyCandleStore.cs

function fetchJson(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { headers: { 'User-Agent': 'Mozilla/5.0' } }, res => {
      let data = '';
      res.on('data', c => data += c);
      res.on('end', () => {
        try { resolve(JSON.parse(data)); } catch (e) { reject(e); }
      });
    }).on('error', reject);
  });
}

function parseExistingCsv(path) {
  const map = new Map(); // epochMs -> row
  if (!fs.existsSync(path)) return map;
  const lines = fs.readFileSync(path, 'utf8').split(/\r?\n/).filter(Boolean);
  for (let i = 1; i < lines.length; i++) {
    const parts = lines[i].split(',');
    if (parts.length < 5) continue;
    const t = new Date(parts[0]);
    if (isNaN(t.getTime())) continue;
    map.set(t.getTime(), { time: t, open: parts[1], high: parts[2], low: parts[3], close: parts[4] });
  }
  return map;
}

async function backfillSymbol(symbol) {
  // range=10y — Yahoo happily returns whatever it actually has (often less, e.g. IPO date), no
  // error either way; 1d interval isn't subject to the ~2-year window 60m data is.
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${symbol}?interval=1d&range=10y`;
  const json = await fetchJson(url);
  const result = json?.chart?.result?.[0];
  if (!result) throw new Error(`No data returned for ${symbol}`);

  const ts = result.timestamp;
  const q = result.indicators.quote[0];

  const outPath = `${OUT_DIR}\\${symbol}_Daily.csv`;
  const existing = parseExistingCsv(outPath);

  const merged = new Map(existing); // epochMs -> row
  let added = 0, skippedNull = 0, updated = 0;

  for (let i = 0; i < ts.length; i++) {
    const open = q.open[i], high = q.high[i], low = q.low[i], close = q.close[i];
    if (open == null || high == null || low == null || close == null) { skippedNull++; continue; }

    const epochMs = ts[i] * 1000;
    const row = {
      time: new Date(epochMs),
      open: open.toFixed(4), high: high.toFixed(4), low: low.toFixed(4), close: close.toFixed(4),
    };
    if (merged.has(epochMs)) updated++; else added++;
    merged.set(epochMs, row); // Yahoo wins — this store has no app-collected data to protect
  }

  let rows = Array.from(merged.values()).sort((a, b) => a.time - b.time);
  if (rows.length > MAX_CANDLES) rows = rows.slice(rows.length - MAX_CANDLES);

  const header = 'Time,Open,High,Low,Close';
  const csv = [header, ...rows.map(r => `${r.time.toISOString()},${r.open},${r.high},${r.low},${r.close}`)].join('\r\n') + '\r\n';
  fs.writeFileSync(outPath, csv, 'utf8');

  console.log(`${symbol}: wrote ${rows.length} candles (added ${added}, updated ${updated} from Yahoo, skipped ${skippedNull} null) -> ${outPath}`);
  console.log(`  range: ${rows[0].time.toISOString()} .. ${rows[rows.length - 1].time.toISOString()}`);
}

(async () => {
  for (const sym of SYMBOLS) {
    await backfillSymbol(sym);
  }
})().catch(err => { console.error(err); process.exit(1); });
