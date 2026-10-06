namespace OptionsTrader.WinForms;

// The log text + color for a Bollinger momentum event — one place, so the Simulator's log and the
// live Charts tab's log never drift apart. Bullish green, bearish red, "perdida" orange.
internal static class BbMomentumText
{
    public static (string Text, Color Color) For(BbMomentumEvent ev) => (ev.Kind, ev.Bullish) switch
    {
        (BbMomentumEventKind.Validation, true)  => ($"Validación Alcista — PM↑ BBsup↑ spot entre ambas ↑ (spot {ev.Spot:F2})", Color.LimeGreen),
        (BbMomentumEventKind.Validation, false) => ($"Validación Bajista — PM↓ BBinf↓ spot entre ambas ↓ (spot {ev.Spot:F2})", Color.Red),
        (BbMomentumEventKind.Touch, true)       => ($"Toque Banda Superior (spot {ev.Spot:F2})", Color.LimeGreen),
        (BbMomentumEventKind.Touch, false)      => ($"Toque Banda Inferior (spot {ev.Spot:F2})", Color.Red),
        (BbMomentumEventKind.Lost, true)        => ("Validación Alcista perdida antes del toque", Color.Orange),
        _                                       => ("Validación Bajista perdida antes del toque", Color.Orange),
    };
}
