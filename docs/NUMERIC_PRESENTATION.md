# SpanDraft – Numerische Darstellung

## 1. Grundsätze

- Core, Solver, Engineering und Projektdateien behalten die kanonischen SI-Werte. Einheitenwahl und Rundung verändern weder Modell noch Berechnung.
- Es gibt genau zwei Ergebnis-Darstellungsmodi: **Standard** und **Detailliert**. Standard ist die fachlich verdichtete Ansicht; Detailliert zeigt **drei signifikante Stellen**, ohne modellabhängige Vergröberung.
- Die Regeln gelten für **berechnete** Werte (Kennwerte, Reaktionen, Diagrammextrema). Eingabefelder und gespeicherte Modellwerte bleiben hinreichend genau für verlustfreie Bearbeitung; bestätigte Eingaben werden nicht nach Ergebnisregeln vergröbert.
- Diagrammkurven und Extremwertpositionen verwenden stets die ungerundeten Werte. Achsenticks behalten ihre eigenständige Nice-Ticks-Formatierung.
- Die Standardauflösung ist eine **Darstellungskonvention**, keine mechanische Toleranz, Messunsicherheit oder Aussage über die Zulässigkeit.

## 2. Standardmodus: verbindliche Rundungsregeln

Zunächst werden der **ungerundete SI-Wert** und die in SI bestimmte modellabhängige Mindestauflösung in die gewählte **Anzeigeeinheit** umgerechnet. In dieser Einheit wird die signifikante Schrittweite bestimmt; die gröbere der angegebenen Teilregeln gewinnt als `Δmin`. Diese wird auf die kleinste Schrittweite `Δ ≥ Δmin` der Form `a · 10ᵏ` mit `a ∈ {1, 2, 5}` und ganzzahligem `k` aufgerundet, jeweils in der Anzeigeeinheit der Größe. Bereits passende Schrittweiten bleiben unverändert. Erst danach wird der umgerechnete Rohwert einmal auf das nächste Vielfache von `Δ` gerundet (bei exakten Halbwerten von null weg). Es gibt weder eine SI-Vorrundung noch eine zweite Rundung für die Textausgabe. Vorzeichen bleiben erhalten. Für den Sicherheitsfaktor gilt die eigene Staffelung gemäß Abschnitt 3.

Für `n` signifikante Stellen gilt beim umgerechneten Rohwert `x ≠ 0` in der Anzeigeeinheit:

`Δsig(x, n) = 10^(floor(log10(abs(x))) - n + 1)`

| Ergebnisgröße | Mindestschrittweite `Δmin` im Standardmodus |
| --- | --- |
| Durchbiegung `w` | `max(Δsig(w, 2), L · 10⁻⁵)` |
| Kraft, Querkraft, vertikale Lagerreaktion `F/V/R` | `max(Δsig(F, 3), min(0,001 · Fref, 1 N))` |
| Biege- und Lagermoment `M` | `max(Δsig(M, 3), min(0,001 · Mref, 0,1 N·m))` |
| Biegespannung `σ` | `max(Δsig(σ, n), min(0,001 · Re, 0,1 MPa))`, mit `n` gemäß unten |
| Sicherheitsfaktor `S` | Eigene Staffelung gemäß Abschnitt 3 |
| Übrige berechnete Größen, z. B. Normalkraft `N`, axiale Verschiebung `u`, Rotation `θ`, Querschnittskennwerte `A/I/W` | `Δsig(x, 3)`; **keine** modellabhängige Mindestauflösung |

Die Modellbeiträge der Tabelle (`L · 10⁻⁵` und die `min(...)`-Ausdrücke) werden in SI bestimmt und vor dem Vergleich mit `Δsig` in die Anzeigeeinheit der Ergebnisgröße umgerechnet. Die signifikante Schrittweite und die 1–2–5-Stufung werden dagegen ausschließlich in der Anzeigeeinheit bestimmt.

Definitionen:
- `L`: physikalische Balkenlänge. Der Beitrag `L · 10⁻⁵` wird in die Anzeigeeinheit von `w` umgerechnet, unabhängig von der ausgewählten Balkenlängeneinheit. Die längenabhängige Mindestauflösung wächst **proportional und ohne künstliche Obergrenze** mit `L`. Die tatsächliche Schrittweite folgt der oben festgelegten Stufung.
- `Fref`: größter Betrag aus Punktkräften, Resultierenden der einzelnen Streckenlasten `|q| · Lastlänge`, äquivalenten Kräften der Punktmomente `|M|/L` sowie berechneten Querkräften und vertikalen Lagerreaktionen. Gegensinnige Lasten werden **nicht** vorher saldiert.
- `Mref`: größter Betrag des berechneten Biegemomentverlaufs (aus den vorhandenen analytischen Extrema).
- `Re`: Streckgrenze des gewählten Werkstoffs.
- Für Spannungen gilt `n = 2`; ab `|σ| ≥ 0,5 · Re` gilt `n = 3`. Damit der Übergang keine rückläufige Anzeige verursacht, gilt `n = 3` bereits dann, wenn die vorläufige Rundung auf zwei signifikante Stellen `0,5 · Re` erreicht oder überschreitet.
- Die Kraftregel mit `Fref` gilt für **transversale** Kräfte und Reaktionen. Die Normalkraft `N` verwendet bis zur fachlichen Definition eines axialen Lastmaßstabs die Auffangregel. `u` übernimmt **nicht** die längenabhängige Durchbiegungsregel; `A/I/W` sind nur bei **lesender** Anzeige betroffen, nicht beim Bearbeiten eines Querschnitts.
- Ist eine Referenzgröße null, entfällt ihr Modellbeitrag. Exakte Ergebnisse `0` werden als `0` ausgegeben. Ein Wert ungleich null, der auf null gerundet würde, erscheint als `≈ 0` (mit Einheit); niemals als scheinbar exakte Null.
- In Diagrammextremum-Beschriftungen ersetzt `≈` bei angenäherter Null das Gleichheitszeichen: `min ≈ 0 mm`, `max ≈ 0 mm` beziehungsweise `min = max ≈ 0 mm`; niemals `min = ≈ 0 mm`.
- Die Rundungspräzision folgt den festgelegten Schrittweiten. Nicht benötigte nachgestellte Nullen werden entfernt. Die Anzeige vermittelt keine zusätzliche Genauigkeit durch künstlich aufgefüllte Dezimalstellen.

### Beispiele im Standardmodus

| Modell / Ergebnis | Berechnet | Anzeige |
| --- | ---: | ---: |
| `L = 50 mm`, Durchbiegung | `0,00765 mm` | `0,0075 mm` |
| `L = 73,12 mm`, Durchbiegung | `0,008 mm` | `0,008 mm` (`Δmin = 0,0007312 mm → Δ = 0,001 mm`) |
| `L = 1000 mm`, Durchbiegung | `0,00765 mm` | `0,01 mm` |
| `L = 1000 mm`, Durchbiegung | `0,05 mm` | `0,05 mm` |
| `L = 1000 mm`, Durchbiegung | `10,01 mm` | `10 mm` |
| `L = 10 000 mm`, Durchbiegung | `0,05 mm` | `0,1 mm` (exakter Halbwert) |
| `L = 10 000 mm`, Durchbiegung | `0,04 mm` | `≈ 0 mm` |
| `L = 10 000 mm`, Durchbiegung | `3,07 mm` | `3,1 mm` |
| `Fref = 1000 N`, Reaktion | `0,000084 N` | `≈ 0 N` |
| `Fref = 1000 N`, Reaktion | `2,7 N` | `3 N` |
| `Fref = 0,1 N`, Reaktion | `0,0347 N` | `0,0347 N` |
| `Fref = 5000 N`, Kraft | `4516,02 N` | `4520 N` |
| `Mref = 1000 N·m`, Moment | `0,034 N·m` | `≈ 0 N·m` |
| `Mref = 2000 N·m`, Moment | `1491,23 N·m` | `1490 N·m` |
| `Re = 235 MPa`, Spannung | `26,01 MPa` | `26 MPa` |
| `Re = 235 MPa`, Spannung | `234,9 MPa` | `235 MPa` |
| Normalkraft `N` | `0,000084 N` | `84·10⁻⁶ N` |
| Axiale Verschiebung `u` | `0,00007654 mm` | `76,5·10⁻⁶ mm` |
| Rotation `θ` | `0,001234 rad` | `1,23·10⁻³ rad` |
| Fläche `A` (nur Anzeige) | `1900,45 mm²` | `1900 mm²` |
| Flächenträgheitsmoment `I` (nur Anzeige) | `2 874 599 mm⁴` | `2,87·10⁶ mm⁴` |
| Widerstandsmoment `W` (nur Anzeige) | `57 316,7 mm³` | `57 300 mm³` |

Die `0,05 mm` bei `L = 10 m` werden aufgrund der festgelegten Halbwert-Rundung zu `0,1 mm`. Es erfolgt **keine** Bewertung, ob eine Durchbiegung konstruktiv relevant oder zulässig ist.

## 3. Sicherheitsfaktor

Die Staffelung richtet sich nach dem **ungerundeten** dimensionslosen Wert:

| Bereich | Standardanzeige |
| --- | --- |
| `0 ≤ S < 3` | Hundertstel (`0,01`) |
| `3 ≤ S < 10` | Zehntel (`0,1`) |
| `10 ≤ S < 100` | Ganze Zahl (`1`) |
| `S ≥ 100` | 2 signifikante Stellen |
| `S = +∞` | `∞` |

Beispiele: `1,2134 → 1,21`; `2,996 → 3`; `3,456 → 3,5`; `9,96 → 10`; `10,376 → 10`; `25,673 → 26`. Unnötige Endnullen entfallen im Standardmodus.

Falls künftig Grenzwerte bewertet werden, erfolgt der Vergleich ausschließlich mit dem **ungerundeten** Ergebnis. Insbesondere bedeuten zwei identische Anzeigen `1` nicht zwangsläufig denselben Status relativ zur Streckgrenze.

## 4. Detailliert, Schreibweise und Einheiten

- **Detailliert:** Den ungerundeten SI-Wert zuerst in die Anzeigeeinheit umrechnen und dort einmal auf maximal drei signifikante Stellen runden; keine Standard-Auflösungsgrenzen. `0` bleibt `0`, `+∞` bleibt `∞`.
- **Schreibweise:** SpanDraft wählt **automatisch** zwischen gewöhnlicher Dezimaldarstellung und Engineering-Notation (`·10ⁿ`, Exponent in Dreierschritten). Dezimaldarstellung gilt, wenn der Betrag des bereits gerundeten Werts kleiner als `10⁶` ist und höchstens vier notwendige Nachkommastellen hat. Nicht benötigte Endnullen zählen nicht als Nachkommastellen. Sonst gilt Engineering-Notation; **nie `·10⁰`** (in diesem Fall Dezimaldarstellung). Keine zusätzliche Benutzerauswahl für die Notation.
- Die Entscheidung zur Schreibweise fällt **nach** Einheitenauswahl und numerischer Rundung. Sie darf weder eine erneute Rundung noch eine automatische Einheitenskalierung (`N → kN`) auslösen.
- Einheitensymbole sind sprachunabhängig. Dezimaltrennzeichen folgen standardmäßig `CurrentCulture`; eine explizit übergebene Kultur hat Vorrang. `de-DE` und `en-US` sind zu unterstützen, auch wenn `CurrentUICulture` abweicht.
- Die physikalische modellabhängige Mindestauflösung wird in **SI** bestimmt und in die Anzeigeeinheit übertragen. Signifikante Schrittweite, 1–2–5-Stufung und einmalige Rundung erfolgen in der Anzeigeeinheit, auch bei gemischten Profilen.

Eingebaute Einheitenprofile (später durch weitere erweiterbar; derzeit sehr niedrige Priorität):

| Größenart | Metrisch – Maschinenbau (Standard) | Metrisch – Bauwesen | US-amerikanisch |
| --- | --- | --- | --- |
| Balkenlänge / Position | mm | m | ft |
| Querschnittsmaße | mm | mm | in |
| Durchbiegung / axiale Verschiebung | mm | mm | in |
| Querschnittsfläche `A` | mm² | cm² | in² |
| Flächenträgheitsmoment `I` | mm⁴ | cm⁴ | in⁴ |
| Widerstandsmoment `W` | mm³ | cm³ | in³ |
| Rotation `θ` | rad | rad | rad |
| Kraft / Querkraft / Reaktion / Normalkraft | N | kN | kip |
| Moment | N·m | kN·m | kip·ft |
| Streckenlast | N/m | kN/m | kip/ft |
| Spannung | MPa | MPa | ksi |

Eingebaute Profile sind unveränderlich. Jede individuelle Einheitenänderung erzeugt die Einstellung **Benutzerdefiniert**; unterschiedliche Einheiten dürfen gemischt werden. Einheitenprofil und Modus werden lokal und projektunabhängig gespeichert. Eine kleine Konfigurations-Schemaversion erlaubt spätere Erweiterungen; fehlende oder defekte Einstellungen führen zu Defaults. Darstellungswechsel lösen keine erneute Berechnung aus und verändern weder Projektdatei noch Undo/Redo oder Dirty-State.

## 5. Umsetzung und Tests

- Eine zentrale Darstellungsschicht bündelt Einheiten, Format und Präzisionsregeln; keine Rundungslogik in Core, Solver oder Engineering.
- Getrennte Formatierung für **bearbeitbare Eingaben**, **berechnete Werte** und **Achsenticks**. Offene Eingaben werden beim Einheitenwechsel nicht stillschweigend neu interpretiert.
- Referenzfälle oben sowie Halbwerte, negative Werte, sehr kleine Nichtnullwerte, `0`, `−0`, `∞`, Grenzen bei `S = 3/10/100`, Übergang bei `σ = 0,5 Re`, Grenzen der Schrittweitenstufung `1/2/5`, unterschiedliche Modellgrößen und gemischte Einheiten testen.
- Diagramme müssen dieselben Kurven, Sprünge und Extremwertpositionen zeigen; nur Text und Einheiten ändern sich.
- Die Zahlenbeispiele sind **Abnahmekriterien**. Die Darstellungsvorgaben sind keine Validierung der mechanischen Berechnung.
