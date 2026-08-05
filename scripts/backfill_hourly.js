// Backfills C:\OptionsData\MarketData\Candles\{Symbol}_Hourly1h.csv with real hourly OHLC from
// Yahoo Finance's unofficial chart API (no signup/API key needed), merged with whatever the app
// already collected itself (that data wins on any timestamp collision), filtered to RTH
// (9:30-16:00 ET) to match HourlyCandleStore's own format, then trimmed to the most recent
// MAX_CANDLES rows (same MaxCandles the app itself uses — 1500, to support the Daily chart view,
// which needs up to ~200 TRADING days = ~1400 hourly candles).
//
// Use this whenever a new ticker is added to tickers.json (or Tickers screen), so its SMA100/200
// have real context from the first render instead of building up from zero live candles.
//
// Usage: node scripts/backfill_hourly.js SYMBOL [SYMBOL ...]
//   e.g. node scripts/backfill_hourly.js NFLX NVDA
const fs = require('fs');
const https = require('https');

const SYMBOLS = process.argv.slice(2);
if (SYMBOLS.length === 0) {
  console.error('Usage: node scripts/backfill_hourly.js SYMBOL [SYMBOL ...]');
  process.exit(1);
}

const OUT_DIR = 'C:\\OptionsData\\MarketData\\Candles';
const MAX_CANDLES = 1500;
const RTH_START_MIN = 9 * 60 + 30; // 9:30 ET
const RTH_END_MIN = 16 * 60;       // 16:00 ET

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

// Minutes since midnight ET for a given UTC epoch-seconds timestamp.
function etMinutesOfDay(epochSec) {
  const d = new Date(epochSec * 1000);
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'America/New_York', hour: '2-digit', minute: '2-digit', hour12: false,
  }).formatToParts(d);
  const h = parseInt(parts.find(p => p.type === 'hour').value, 10) % 24;
  const m = parseInt(parts.find(p => p.type === 'minute').value, 10);
  return h * 60 + m;
}

function parseExistingCsv(path) {
  const map = new Map(); // epochMs -> row (existing app-collected data, kept as-is)
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
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${symbol}?interval=60m&range=2y`;
  const json = await fetchJson(url);
  const result = json?.chart?.result?.[0];
  if (!result) throw new Error(`No data returned for ${symbol}`);

  const ts = result.timestamp;
  const q = result.indicators.quote[0];

  const outPath = `${OUT_DIR}\\${symbol}_Hourly1h.csv`;
  const existing = parseExistingCsv(outPath); // existing app-collected rows win on collision

  const merged = new Map(existing); // epochMs -> row
  let added = 0, skippedNull = 0, skippedOutsideRth = 0, skippedExisting = 0;

  for (let i = 0; i < ts.length; i++) {
    const open = q.open[i], high = q.high[i], low = q.low[i], close = q.close[i];
    if (open == null || high == null || low == null || close == null) { skippedNull++; continue; }

    const minutesEt = etMinutesOfDay(ts[i]);
    if (minutesEt < RTH_START_MIN || minutesEt > RTH_END_MIN) { skippedOutsideRth++; continue; }

    // Yahoo's last bar for the current in-progress hour is often just a flat "latest price"
    // snapshot (open === high === low === close), not a real OHLC candle — drop it.
    if (open === high && high === low && low === close) { skippedNull++; continue; }

    const epochMs = ts[i] * 1000;
    if (merged.has(epochMs)) { skippedExisting++; continue; } // app's own data wins

    merged.set(epochMs, {
      time: new Date(epochMs),
      open: open.toFixed(4), high: high.toFixed(4), low: low.toFixed(4), close: close.toFixed(4),
    });
    added++;
  }

  let rows = Array.from(merged.values()).sort((a, b) => a.time - b.time);
  if (rows.length > MAX_CANDLES) rows = rows.slice(rows.length - MAX_CANDLES);

  const header = 'Time,Open,High,Low,Close';
  const csv = [header, ...rows.map(r => `${r.time.toISOString()},${r.open},${r.high},${r.low},${r.close}`)].join('\r\n') + '\r\n';
  fs.writeFileSync(outPath, csv, 'utf8');

  console.log(`${symbol}: wrote ${rows.length} candles (added ${added} from Yahoo, kept ${skippedExisting} existing app rows, skipped ${skippedNull} null + ${skippedOutsideRth} outside-RTH from Yahoo) -> ${outPath}`);
  console.log(`  range: ${rows[0].time.toISOString()} .. ${rows[rows.length - 1].time.toISOString()}`);
}

(async () => {
  for (const sym of SYMBOLS) {
    await backfillSymbol(sym);
  }
})().catch(err => { console.error(err); process.exit(1); });
