# Gleitkomma-Vertrag des Rechenkerns (O2)

Vertrag des Architekturentwurfs für die Biegung im heutigen Migrationsumfang. Die
ausführbare Referenz ist `state/fpc.py`. Maßstab ist die exakte Mechanik auf den
gespeicherten Binary64-Solvereingaben. Der Adapter liefert EI. Die Untersuchung
schließt zusätzlich den Fehler des gerundeten Produkts E·I gegenüber dem
exakten Produkt ein. Die mechanischen Formeln und die Migration stehen in
[der Solver-Architektur](../../../docs/SOLVER_ARCHITEKTUR.md), die Quellen in
[der Literaturprüfung](LITERATURE_AUDIT.md).

## 1. Ergebnis und Fehlergrößen

- `RawBound` ist die unmittelbare adjungierte Schranke für den Residualanteil.
- `VerifiedStateBound` schließt den Zustandsfehler nach Verifikation ein.
- `VerifiedOutputBound` schließt den Fehler des veröffentlichten SI-Werts ein,
  einschließlich Zustands-, Funktional-, Daten-, Auswertungs- und Skalierungsfehlern.

Für jede gelieferte Größe gilt
`|y_computed − y_exact| ≤ VerifiedOutputBound ≤ τ · ReferenceScale`.
`τ = 1/100_000_000` ist ein exakter mathematischer Produktwert (O1).
Bei exakt null Anregung und bestandenem Modell- und Aufbaucheck liefert der
Nullpfad exakte Nullen ohne relatives Maß.

## 2. Voraussetzungen und Operationsfolgen

- Binary64 mit Rundung zur nächsten Zahl, bei Gleichstand zur geraden, für die
  zertifizierten skalaren Operationen. Subnormale Eingaben und Zwischenergebnisse
  werden dort, wo ihr Betrag oder ihre Bits benötigt werden, ausdrücklich
  gelesen oder mit ihrer Verlustschranke behandelt.
- Die gerichteten Primitives lesen den maßgeblichen gespeicherten Wert unabhängig
  von DAZ. Erforderlich sind vorzeichenrichtige Nachbarn, exakte binäre Zerlegung
  und korrektes Skalieren. Die Referenz nutzt dafür Host-Binary64 und emuliert
  FTZ und DAZ nur an den modellierten Operationen. Die Portierung muss diese
  Primitives unabhängig vom Unterlaufmodus realisieren.
- Die exakte kinematische Rangprüfung hat bestanden. Der exakt reduzierte
  Biegeoperator ist regulär. Modellgültigkeit einschließlich fachlicher
  Koinzidenzregeln liegt vor dem numerischen Vertrag.
- Zwischenwerte, an denen Range, Flush, Rundungszählung oder eine gerichtete
  Schranke geprüft werden, müssen mit der modellierten Semantik materialisiert
  werden. Das betrifft insbesondere Skalierung und Klassifikation, Produkte
  und Akkumulatoren im verifizierenden Residual sowie einzelne Operationen
  in der Schrankenarithmetik. Fusion ist für Kandidatenerzeugung in LU,
  Rücksubstitution und Nachiteration zulässig. In einer zertifizierten Formel
  darf sie eingesetzt werden, wenn die gleiche Schranke für die fusionierte
  Folge hergeleitet ist und alle Range-Prämissen erhalten bleiben. Die
  Abnahme braucht keine globale JIT-Annahme über FMA.

## 3. Primitives und Lemmata

**Exponenten und Skalierung.** `PowerOfTwoExponent(x) = floor(log₂(x))` für
positive endliche gespeicherte Werte, binär zerlegt. Die mathematische Notation
beschreibt die Funktion. Die Implementierung benutzt keinen Logarithmus.
`ScaleByPowerOfTwo` skaliert den Operanden mit einem ganzzahligen Exponenten
in einem Schritt. Bei normalem exaktem Ergebnis im darstellbaren Bereich ist
die Skalierung exakt. Im Grenzbereich wird Unterlauf geprüft oder durch einen
absoluten Term eingeschlossen. Überlauf auf einem erforderlichen Wertepfad
ergibt einen Bereichsfehler. Keine Referenzpotenz wird als Double materialisiert.

**Gerichtete Schranken.** Für ein korrekt auf Binary64 gerundetes Ergebnis
bildet `outward` den gerichteten Nachbarn. Subnormale Nachbarn werden nach
außen projiziert. Die folgende Tabelle spezifiziert die Grenzfälle. Null in
der Tabelle ist eine gespeicherte exakte null als Schrankenwert.

| Gerundeter Wert | Untere Schranke | Obere Schranke |
|---|---|---|
| Sicher exakte null | 0 | 0 |
| Gerundete ±0 ohne gesichertes Vorzeichen | −λ | λ |
| Subnormal positiv bzw. negativ | 0 bzw. −λ | λ bzw. 0 |
| +λ | 0 | nächster größerer Normalwert |
| −λ | nächster kleinerer Normalwert | 0 |
| Endlich normal außerhalb ±λ | Vorgänger | Nachfolger |
| +∞ bzw. −∞ | MaxDouble bzw. −∞ | +∞ bzw. −MaxDouble |

Das ist eine äußere Einschließung, auch wenn eine Überlaufrundung Unendlichkeit
ergibt. NaN führt zur Ablehnung. Für gerichtete Skalierung einer bekannten
vorzeichenbehafteten Zahl ist das Vorzeichen auch beim Unterlauf auf null
bekannt. Exakt darstellbare normale Skalierungsergebnisse werden übernommen.
Gespeicherte Schranken sind normal, null oder unendlich. Für die Freigabe
müssen die verwendeten Schranken endlich sein.

**Relative Schranken.** Für normale, auslöschungsfreie Koeffizientenketten
gilt [Hi02] mit `u = 2^-53` und `γ_k = ku/(1−ku)`. Bei Produkt und Quotient
addieren sich die Rundungszahlen der Operanden und die neue Rundung. Bei einer
gleichvorzeichigen Addition genügt das Maximum plus eins. Exakte
Zweierpotenzskalierungen erhöhen die Zahl nicht. Eine rekursive Summe von
`m` positiven normal skalierten Federkoeffizienten hat höchstens `m−1`
Rundungen. Bei `0 < k < 2^53` ist `ku` exakt darstellbar. `GammaUpper`
rundet den Nenner nach unten und den Quotienten nach oben. Null Rundungen
ergeben null. Ab `ku ≥ 1` liegt ein Bereichsfehler vor. Die Umrechnung auf
den berechneten Betrag verwendet `γ̄/(1−γ̄)` gerichtet und erfordert
`γ̄ < 1`. Damit bleibt auch die dynamische Rundungszahl gültig.

**Absolute Fortpflanzung.** Für die lokalen Last- und Feldformeln führt
`Bounded` den berechneten Wert, eine Obergrenze seines Betrags `A` und eine
Schranke `a` für seinen absoluten Fehler. Kann ein Operand durch DAZ verloren
gehen, erhöht sich `a` um höchstens `min(A, λ)`. Bei Addition und Subtraktion
addieren sich die beiden Fehler. Bei Multiplikation begrenzt
`a·(B+b)+A·b` den Datenfehler, wobei `A,B` die Betragsmajoranten und
`a,b` die Fehler einschließlich möglicher DAZ-Verluste sind. Bei Division
mit normalem gespeichertem Divisorbetrag `d` und positiver Untergrenze
`d_low ≤ |d_exact|` ist der Datenfehler höchstens
`(a + (A/d)·b)/d_low`. Dazu kommt pro Operation
`u·operationMagnitude + λ`, gerichtet berechnet. Die neue Betragsmajorante
enthält den lokalen Rundungsanteil. Nullabkürzungen verlangen einen
nachgewiesenen exakten Nulloperanden. Der absolute Term deckt graduellen
und abrupten Unterlauf ab, einschließlich Ergebnissen an der Grenze λ.

Die Betrags- und Fehlerausdrücke sind innerhalb eines Stücks monoton in den
wachsenden Abständen. Die Divisoren der aktiven Last- und Feldformeln sind je
Stück konstant. Die Vorzeichen der Summanden und Auslöschung beeinflussen die
Majoranten nicht. Der Wert an einem Ereignis auf der anderen Seite gehört zum
separaten Stationsgate.

## 4. Beweiskette und Abnahmestufen

| Stufe | Vertrag | Unabhängige Gegenprobe |
|---|---|---|
| A | Primitives und lokale Formeln schließen ihren tatsächlichen Fehler ein | Exakte rationale Produkte, Quotienten, Summen und Grenzwerte |
| B | `|Ŝ−S_exact| ≤ δS`, `|ŝb−sb_exact| ≤ δsb`, `|r_exact| ≤ d` | Matrix, RHS und Residual einzeln |
| C | `|I−R·S_exact| ≤ M`, `Mv ≤ μv`, `v > 0`, `μ < 1` | Exakte Matrixprodukte und Kontraktion |
| D | Zustand innerhalb von `VerifiedStateBound` | Exakte Lösung des reduzierten Systems |
| E | Veröffentlichte Werte innerhalb von `VerifiedOutputBound` | Unabhängige Übertragungsreferenz, Feldstücke, Reaktionen und Kandidatenwerte |
| F | SI-Bereiche und mathematische Schwelle eingehalten | Exakte Bezugsskalen, Gate, gezielte Range-Fälle |

## 5. Operationsvertrag

1. **Eingaben und Steifigkeit.** Jede verwendete Referenzgröße ist vor
   `ILogB` positiv, endlich und normal. Für E·I wird das exakte Produkt
   aus binären Signifikanden und Exponenten mit `[λ, MaxDouble]`
   verglichen. Außerhalb gilt `NumericalRange`, innerhalb erfolgt eine
   normale Rundung. Dieselbe Adapterregel gilt später für E·A.
2. **Normierung.** `e_L = ILogB(L)`, `e_EI = ILogB(max EI)`.
   Für den späteren Axialpfad gilt `e_EA = ILogB(max EA)`. Die
   entsprechenden normierten Referenzgrößen liegen in `[1,2)`.
   Geometrische Abstände werden aus gespeicherten globalen Koordinaten
   gebildet. Die lokale Feldformel schließt die geschützte Differenz
   und das Skalieren mit `u·|d̂| + λ·2^-e_L + λ` nach oben ein.
3. **Anregung.** `unloaded` testet jedes gespeicherte Lastprimitiv auf
   exakte null, unabhängig vom Akkumulator. Biegeanregung ist
   `Σ|P| + Σ((|q_a|+|q_b|)/2)·l + Σ|C|/L`. Die Trapeznorm ist auch
   bei Vorzeichenwechsel positiv. Faktoren werden vor Produkt und
   Division binär zerlegt. Die Mantissen der Beiträge werden relativ zum
   größten Exponenten gerichtet summiert. Nicht darstellbare Restbeiträge
   bleiben in der Einschließung. Überspannt die Einschließung eine
   Zweierpotenzgrenze, entscheidet ein exakter Vergleich. Die
   eingeschlossene Summe liefert `e_F` und die gerichteten Referenzskalen.
4. **Assembly und Felder.** Normale Übertragungskoeffizienten werden als
   `a/e`, `((a*a)/2)/e`, `(((a*a)*a)/6)/e` gebildet. Nicht-null
   Zwischenergebnisse im Unterlaufbereich und Unterlauf auf null führen
   zu `NumericalRange`. Lastterme folgen den Punktkraft-, Punktmoment-,
   Difference- und Ramp-Polynomen. Intensitäten gehen vor den
   Abstandspotenzen ein. Die innere Rampe verwendet `rise·(n/d)·n^p`
   mit expliziter Divisorprüfung. Abgeschnittene Intensitäten werden aus
   globalen Abständen interpoliert und ihre Fehler fortgepflanzt.
   Stationslasten, Rechte-Seiten-Summen, Feldrekonstruktion und
   Reaktionsauswertung verwenden die absoluten Regeln aus Abschnitt 3.
5. **Ruiz und Lösen.** Zeilen- und Spaltenfaktoren sind ganzzahlige
   Exponenten. Ruiz wählt aus den Maxima eine gerundete binäre
   Korrektur für die Quadratwurzel des Maximums ohne Materialisierung
   der Wurzel oder der Korrekturpotenz. Matrixeinträge werden mit der
   Summe der beiden Exponenten skaliert. Für die RHS ist ein
   subnormales Ergebnis erlaubt, wenn sein Verlust in `δsb` steht.
   Primärlösung und Adjungierte erhalten mindestens einen
   Nachiterationsschritt. Die verifizierende Rechnung verwendet das
   resultierende gespeicherte Ergebnis. Unterlauf beim Rückskalieren
   des Zustands oder eines Reaktionsfunktionals wird separat eingeschlossen.
6. **SI und Gate.** Einen subnormalen normierten Ausgabewert vor der
   Skalierung kontrolliert auf null setzen und den Verlust in der
   normierten Schranke einschließen. Nach dem Skalieren einen weiteren
   absoluten SI-Term λ für Werte an oder unter der Normalgrenze
   berücksichtigen. Für eine ganze Stückmajorante beide Verluste
   konservativ vorab budgetieren.

## 6. Residual, Adjungierte und verifizierte Schranken

Sei `r̂` das Residual auf der gespeicherten Matrix. In der gemeinsamen
Residualprimitive stehen nur normale oder nullwertige Matrix- und
Lösungsoperanden. Eine subnormale RHS wird kontrolliert auf null
gesetzt und mit λ budgetiert. Für jedes nichttriviale Produkt und
jede tatsächlich ausgeführte Subtraktion gilt:

- Bei einem Ergebnisbetrag über λ schließt `u·|result|` die normale
  Rundung relativ zum gespeicherten Ergebnis ein.
- Bei einem Ergebnis an oder unter λ deckt λ sowohl die
  Unterlaufrundung als auch den möglichen Flush ab. Produkt und
  Akkumulator werden vor weiterer Verwendung kontrolliert geflusht.
  Damit kann DAZ keinen unbudgetierten Operandenverlust verursachen.

Die gespeicherten Zwischenfehler gehen mit Koeffizient ±1 in die
Additions-/Subtraktionskette ein. Die gerichtete Summe der lokalen
Schranken `δr` erfasst daher den gesamten Rundungsfehler dieser
Kette. Andere Formeln verwenden die Multiplikations- und
Divisionsfortpflanzung aus Abschnitt 3. Einschließlich Assembly gilt
`d ≥ |r̂| + δr + δS·|ŷ| + δsb ≥ |sb_exact − S_exact·ŷ|`.

Die Zeilen von `R` sind die gespeicherten adjungierten Lösungen.
Ihr verifiziertes Residual auf Ŝ liefert `SB`. Dann gilt
`|I−R·S_exact| ≤ M := SB + |R|·δS`. Für
`e = y_exact − ŷ` folgt
`|e| ≤ RawBound + M·|e|` mit `RawBound ≥ |R|·d`.
Für endliches `v > 0`, `v ≥ RawBound` und gerichtet bewiesenem
`Mv ≤ μv`, `μ < 1`, gilt
`max_i |e_i|/v_i ≤ 1/(1−μ)`. Damit ist
`VerifiedStateBound = RawBound + Mv/(1−μ)` nach oben auszuwerten.
Ohne Verifikation wird kein Output-Bound gebildet.

Für ein Reaktionsfunktional wird derselbe Residualnachweis auf dessen
Adjungierte angewendet. Die Abweichung des Funktionals von seinem
Adjungiertenprodukt wird gegen `VerifiedStateBound` multipliziert.
Koeffizienten-, Konstanten- und Auswertungsfehler kommen getrennt hinzu.
Dabei muss das Funktional zwei Skalierungsfehler unterscheiden. Der
Verlust beim Rückskalieren des gespeicherten Zustands geht mit dem
Betrag des Funktionalkoeffizienten ein. Der Verlust beim Skalieren des
adjungierten RHS-Koeffizienten wird mit dem verifizierten Zustandsfehler
im äquilibrierten System multipliziert.
Die Forschung kann zusätzlich eine unabhängig eingeschlossene
Federreaktion über das Federgesetz auswählen.

## 7. Bereiche und kontinuierliches Gate

Die Range-Policy prüft Eingaben, normal vorausgesetzte Zwischenwerte,
Pivots, Skalierungen und SI-Referenzgrößen. Ein Nullpivot im exakt
stabilen belasteten System ist `ZeroPivot`. Ein nicht-null Pivot an
oder unter λ ist `NumericalRange`. Ein fehlender Kontraktionsnachweis
ergibt `NotVerified`, eine überschrittene Akzeptanzschwelle `Gate`.
Bei verletzter Range-Regel wird kein Ersatzwert geliefert.

Variante C bestimmt pro Stück eine obere Fehlermajorante `B` aus den
inneren Randseiten. Die absoluten Fehlermajoranten aus Abschnitt 3
bleiben im Stück monoton. Ereignissprünge werden separat geprüft.
Für M und V verbessern gerichtete Untergrenzen von `min |Q_exact|`
die Referenzskala. Dazu die geschätzten Randwerte um ihren verifizierten
Fehler und die maximal mögliche Änderung absenken. Die Änderung folgt
aus `V' = q` und `M' = V` mit einer Obergrenze für die Summe der
exakten absoluten Intensitäten. Diese umfasst die Datenfehler der
abgeschnittenen Endintensitäten. Der Abzug von 2B summiert die
Randwertunsicherheit und die Fehlermajorante im Inneren. Damit gilt
die Untergrenze sowohl für den exakten als auch für den berechneten
Betrag im Stück. Mit denselben Bewegungsschranken
und `|θ'| = |M|/EI`, `|w'| = |θ|` werden die Beträge im gesamten
Stück nach oben eingeschlossen und in SI auf den Bereich geprüft.

Sei `D` die gerichtete untere SI-Bezugsskala. Der Wertvergleich lautet
`B_SI ≤ ThresholdDown(D)`. `ThresholdDown` bildet den nächsten
Binary64-Wert zu `D/100_000_000`, prüft durch exakten Ganzzahlvergleich
der binären Signifikanden und Exponenten eine eventuelle Aufrundung
und geht dann zum Vorgänger. Eine positive subnormale Schwelle wird
auf null projiziert. So gilt `ThresholdDown(D) ≤ τ·ReferenceScale`
auch an der exakten Grenze. Der gerundete diagnostische Quotient
entscheidet die Akzeptanz nicht.

## 8. Konstanten

| Kategorie | Größen | Begründung |
|---|---|---|
| Korrektheitsgrenze | `u = 2^-53`, `λ = 2^-1022`, `ku < 1`, `γ̄ < 1`, `μ < 1` | Binary64-Rundung und Normalgrenze, positiver Nenner der jeweiligen Schranke, Kontraktionsbeweis |
| Korrektheitsgrenze | `τ = 1/100_000_000`, gerichteter Schwellenvergleich | Exakte Produktanforderung und deren konservative Umsetzung |
| Korrektheitsgrenze | Vorzeichen, Integrationsfaktoren und Fakultäten, Exponentenkombinationen, Faktoren der Bewegungsgrenzen | Folgen aus Zustandsübertragung, analytischen Lastintegralen, Einheiten und Integration der Feldableitungen |
| Range-Policy | `[λ, MaxDouble]` für Adapterprodukte, strikte Normalitätsregeln für Koeffizienten, Skalierungen und Pivots | Schützt die relativen Prämissen und macht Grenzentscheidungen explizit |
| Range-Policy | `L ≥ 2^-968`, normierte Lastlänge `≥ 2^-969`, SI-Skalen in `[2^-969, 2^1000]` und SI-Wertmajorante `≤ 2^1000` | Begrenzt verstärkten absoluten Unterlauf und lässt Abstand zum SI-Überlauf. Bei der SI-Untergrenze ist `λ/S ≤ u` |
| Strategie | 8 Ruiz-Durchgänge, höchstens 5 Nachiterationsschritte, Abbruch mit `1/2`, `2u` und `1/16`, `2^-200` im Startgewicht und 3 Neumann-Schritte | Bestimmen die Kandidaten. Scheitert die verifizierte Schranke, folgt Ablehnung oder ein anderer Kandidat |
| Strategie | Root-Finder einschließlich `32ε`, `64ε` und Iterationslimit | Randzuordnung, Nähe, Clustering und Deduplizierung. Validierung über unabhängige Root-Oracles nach O5 |

Die früheren pauschalen absoluten Faktoren wie 64 in Last- und Feldbounds
sind durch die Fortpflanzung aus Abschnitt 3 ersetzt.

## 9. Prüfung der Kette

Die reproduzierbaren Befehle stehen im [Untersuchungs-README](README.md).
`state-fpc-boundary.log` prüft die Primitives und drei bestätigte Fehler
an festen Grenzwerten und weist die Erkennung der Primitive-Mutanten
nach. `state-fpc-summary.log` indiziert die Formel-, Ketten- und
Regressionsprotokolle. Die exakten Vergleiche prüfen konkrete Fälle und
ergänzen die mathematischen Voraussetzungen aus Abschnitten 2–7.
Die vier Unterlaufmodi werden im Residual und in den gezielten
Unterlauffällen getrennt geprüft. Das frühere Modell `state.py`
bleibt in A5 als Vergleich für den bereits bekannten Datenfehler
abgeschnittener Linearlasten erhalten. A5 gehört zur historischen
Vergleichsevidenz, A4 prüft den aktuellen Vertrag.

## 10. Verifikationsstrategie

Gewichte starten bei `RawBound + 2^-200 D` und können mit drei
Neumann-Schritten verbessert werden. Maßgeblich sind die verifizierten
Ungleichungen aus Abschnitt 6. Die beiden aus Runde 4 bekannten Fälle
werden in `state-fpc-unverified.log` separat nachvollzogen.

## 11. Extremwerte (O5)

Variante B ist beschlossen. `PolynomialRoots` bleibt der unabhängig
validierte Produktalgorithmus. Yun/Sturm und die vorhandene unabhängige
Gegenprobe bleiben Validierungsoracles. Der Wert an einem gefundenen
Kandidaten fällt unter denselben Bound wie jeder Feldwert am ausgegebenen
Ort und auf der ausgegebenen Seite. Fehler des Ortes und Vollständigkeit
der Kandidaten sind getrennte Validierungsgrößen.

## 12. Aufwand (O9)

Die Operationsanalyse `state-o9-sizing.log` bleibt eine
Größenordnungsschätzung. Die endgültige Modellgrößen- und Latenzgrenze
folgt aus Messungen der C#-Implementierung. Die Verifikation bleibt bei
festgehaltener Bandbreite quadratisch in der Zahl der Unbekannten,
weil die adjungierten Lösungen im Allgemeinen voll besetzt sind.

## 13. Abnahme und Feature-Tracks

Die C#-Abnahme wiederholt A–F mit festen exakten Sollwerten. Auf jeder
unterstützten Plattform müssen die fachlichen Entscheidungen,
Fehlerklassen, Schranken und das mathematische Gate gelten.
Bitvergleiche sind diagnostisch. Die Primitives und benötigten
Zwischenwerte werden insbesondere auf x64 und ARM64 geprüft.

Zur Migration gehört die exakte axiale Nullsemantik des heutigen
öffentlichen Umfangs. `u` und `N` sind null. An axial gehaltenen
Knoten ist `ReactionX` nullwertig, an freien fehlt die Reaktion.
Der belastete Axialvertrag, die zweite unabhängige Referenz und das
Axialfedergesetz gehören zu DEV-12.
