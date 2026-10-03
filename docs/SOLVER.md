# SpanDraft – Solver

## Ansatz und Schnittstelle

`EulerBernoulliBeamSolver.Solve(BeamModel)` berechnet einen einzelnen geraden
Balken mit konstantem E, A und I mittels direktem Steifigkeitsverfahren.
Math.NET Numerics 5.0.0 übernimmt dichte Matrizen, Vektoren, SVD und Cholesky.
Core enthält weiterhin ausschließlich das Fachmodell.

Die Berechnung ist linear elastisch mit kleinen Verschiebungen und Rotationen.
Euler-Bernoulli setzt voraus, dass Querschnitte eben und senkrecht zur verformten
Balkenachse bleiben; Schubverformung wird vernachlässigt. Axiale Dehnung wird
über EA berücksichtigt. Alle Elemente liegen entlang der globalen x-Achse;
eine Koordinatentransformation ist nicht erforderlich.

## Knoten, Freiheitsgrade und Vorzeichen

Knoten entstehen exakt an 0, Balkenlänge, allen Lagerpositionen, Positionen
von Punktkräften und Punktmomenten sowie Grenzen jeder Streckenlast.
Exakt gleiche Meterwerte erzeugen einen gemeinsamen Knoten. Nahe, aber
unterschiedliche Positionen werden nicht zusammengelegt. Aufsteigend sortierte
Nachbarknoten bilden jeweils ein Element positiver Länge; es gibt keine
zusätzliche gleichmäßige Unterteilung.

Die verbindlichen Vorzeichen aus [DOMAIN.md](DOMAIN.md) gelten:
x und u nach rechts, w sowie aufgebrachte Kräfte und Streckenlasten nach oben,
θ sowie aufgebrachte Momente und Reaktionsmomente gegen den Uhrzeigersinn.
Die Rotation ist θ = dw/dx, in rad. Für interne Schnittgrößen gilt gesondert:

- N > 0 bedeutet Zug.
- M > 0 bedeutet positives/sagendes Biegemoment.
- V ist über `dM/dx = V` definiert, nicht als aufgebrachte Knotenkraft.

Eine mittige Kraft −P (P > 0) auf einem einfach gelagerten Balken ergibt daher
V = +P/2 links und −P/2 rechts der Last sowie M = +PL/4 unter der Last.
Beim links eingespannten Kragarm mit Endkraft −P ist V = +P, M(0) = −PL,
M(L) = 0 und das Reaktionsmoment an der Einspannung +PL.

Globale DOFs eines Knotens i sind `[u, w, θ]` mit Indizes
`[3i, 3i+1, 3i+2]`. Lokal gilt `[u1, w1, θ1, u2, w2, θ2]`.
Eingaben und Berechnungen verwenden ausschließlich SI-Werte.

## Element und Lastvektor

Für `a = EA/L`, `b = 12EI/L³`, `c = 6EI/L²`, `d = 4EI/L`, `e = 2EI/L`
lautet die symmetrische Elementmatrix:

```text
       u1  w1  θ1  u2  w2  θ2
u1      a   0   0  -a   0   0
w1      0   b   c   0  -b   c
θ1      0   c   d   0  -c   e
u2     -a   0   0   a   0   0
w2      0  -b  -c   0   b  -c
θ2      0   c   e   0  -c   d
```

Die Elementbeiträge werden über ihre globalen DOF-Indizes addiert.
Die globale Matrix hat bei N Knoten die Dimension `3N × 3N`.
Punktkräfte gehen unmittelbar in den w-DOF, Punktmomente in den θ-DOF ein.
Lasten am gleichen Ort werden addiert, einschließlich Lasten an Lagern.

Für ξ = x/L lauten die transversalen kubischen Hermite-Formfunktionen:

```text
N1 = 1 − 3ξ² + 2ξ³        N2 = L(ξ − 2ξ² + ξ³)
N3 = 3ξ² − 2ξ³            N4 = L(−ξ² + ξ³)
```

Integration von `p · [N1, N2, N3, N4]` über das Element ergibt für eine
vorzeichenbehaftete konstante Intensität p [N/m]:

```text
Fe = [0, pL/2, pL²/12, 0, pL/2, −pL²/12]
```

UDL-Grenzen erzeugen Knoten. Daher ist jedes Element für jede einzelne UDL
vollständig überdeckt oder nicht überdeckt. Überlappende UDLs werden unabhängig
assembliert und addiert; es findet keine Ersetzung durch viele Punktkräfte statt.

## Lager, Stabilität und Lösung

| Lager | Gesperrte DOFs | Freie DOFs |
| --- | --- | --- |
| Fixed | u, w, θ | keine |
| Pinned | u, w | θ |
| Roller | w | u, θ |

Alle gesperrten Verschiebungen sind null. Unterschiedliche Lagertypen am
gleichen Knoten vereinigen ihre Sperren. Ihre gemeinsame Reaktion wird je
Knoten ausgegeben; eine Aufteilung auf dort übereinanderliegende Lager ist
nicht eindeutig und wird nicht vorgenommen.

Zuerst wird `BeamModelValidator.Validate` ausgeführt. Bei Modellfehlern wird
nicht assembliert oder gelöst. Danach prüft der Solver die Starrkörpermoden
des verbundenen Balkens: axiale Translation `u = c` sowie transversale Bewegung
`w = a + bx`, `θ = b`. Mindestens eine u-Sperre und entweder eine Einspannung
oder w-Sperren an zwei unterschiedlichen Positionen sind erforderlich. Diese
Prüfung erfolgt auch bei unbelasteten Modellen.

Aus der unveränderten vollständigen Matrix K und dem Lastvektor F werden
nur die freien DOFs extrahiert: `Kff · qf = Ff`. Es gibt keine Strafwerte oder
künstlichen Federn. Zur numerischen Skalierung werden
`Dii = 1/√Kff,ii`, `A = D Kff D`, `b = D Ff` verwendet.
Die Skalierung verändert das Gleichungssystem nicht: `qf = Dy` für `Ay = b`.

Math.NET-SVD ohne Singulärvektoren prüft A. Bei `σmin/σmax ≤ 1e-12` wird die
Berechnung als numerisch unzureichend konditioniert abgebrochen. Ansonsten löst
Cholesky das symmetrisch positiv definite System. Der Rückwärtsfehler

```text
||Ay − b||∞ / (||A||∞ · ||y||∞ + ||b||∞)
```

muss höchstens `1e-10` sein. Beim Nullfall mit Nenner null muss auch das Residuum
null sein. Nicht endliche Werte, nichtpositive Steifigkeits-/Skalierungswerte
und Faktorierungsfehler führen zum Abbruch. Sind sämtliche DOFs gesperrt,
bleibt q null und die reduzierte Lösung entfällt.

`BeamSolverException.Code` unterscheidet `InvalidModel` (mit allen ursprünglichen
Validierungsfehlern), `UnstableModel` (freie Starrkörpermoden),
`IllConditionedSystem` (numerische Singularität/Kondition) und `NumericalFailure`
(Arithmetik, Faktorisierung oder Rückwärtsfehler). Es gibt keine Teilresultate,
Pseudoinverse oder automatische Reparatur. Null als Modelleingabe erzeugt
`ArgumentNullException`.

## Ergebnisse und Reaktionen

Die Reaktionen werden ausschließlich aus dem vollständigen ursprünglichen
System bestimmt: `R = Kq − F`. Damit werden auch Lagerlasten und statisch
unbestimmte Systeme berücksichtigt. Freie DOFs haben nur ein numerisches
Residuum; dieses wird nicht als Lagerreaktion veröffentlicht.

`BeamSolution` verweist auf das unveränderte ursprüngliche `BeamModel` und
enthält eine unveränderliche Liste sortierter `BeamNodeResult`-Einträge:
Knotenindex, Position als Core-`Length`, u/w als solver-eigener signed
`Displacement` mit `Meters`-Property, Rotation als `RotationRadians` sowie
nullable Reaktionen in Core-`Force` und Core-`Moment`.
`null` kennzeichnet freie DOFs; auch eine Reaktion von null N bzw. Nm an einem
gesperrten DOF bleibt ein vorhandener Wert. `Displacement` akzeptiert nur endliche
Werte einschließlich negativer Werte und null. Core-`Length` bleibt unverändert.

## Kontinuierliche Elementfunktionen

Die vorhandene Knotenlösung wird je Element durch eine analytische Feldfunktion
ergänzt. Knotengenerierung, Elementsteifigkeit, Assembly, reduzierte Lösung und
Reaktionen werden weiterverwendet; es gibt keinen zweiten Solver.

Die lokale Elementkoordinate ist `s = x − x1`, mit `0 ≤ s ≤ Le`. x1 ist die
globale Position des linken Knotens, Le die Elementlänge. Für das unbelastete
Element ist `wH(s)` die kubische Hermite-Funktion aus `[w1, θ1, w2, θ2]` mit
den oben angegebenen Formfunktionen, ausgewertet bei ξ = s/Le.

Die resultierende Linienlast q ist innerhalb eines Elements konstant: Alle
UDL-Grenzen sind Elementgrenzen, und alle überdeckenden Intensitäten werden
vorzeichenbehaftet addiert. q bezeichnet hier die Linienlast [N/m], nicht den
globalen Verschiebungsvektor. Die kontinuierliche Lösung lautet:

```text
w(s) = wH(s) + q·s²·(Le − s)²/(24·EI)
EI·w''''(s) = q

w(0) = w1       w'(0) = θ1
w(Le) = w2      w'(Le) = θ2
```

Der Zusatzterm hat an beiden Elementenden sowohl Wert als auch erste
Ableitung null. Er erhält daher die vorhandenen Knotenergebnisse und erzeugt
die korrekte quartische Durchbiegung unter konstanter UDL. Bei q = 0 verbleibt
die kubische Hermite-Lösung. Das gilt auch bei vollständig gesperrten Endknoten:
Eine volle UDL erzeugt zwischen zwei Einspannungen eine Durchbiegung, obwohl
alle Knotenverschiebungen und -rotationen null sind.

Intern wird das Polynom in der dimensionslosen Koordinate t = s/Le einmal
aufgebaut und mit dem Horner-Schema ausgewertet. Ableitungen berücksichtigen
jeweils die notwendigen Faktoren von Le. Alle physikalischen Eingabe- und
Ausgabewerte bleiben SI-Werte.

Die weiteren Feldgrößen stammen aus derselben Funktion beziehungsweise dem
axialen Elementanteil:

```text
u(s) = u1 + (u2 − u1)·s/Le
θ(s) = dw/ds                    [rad]
N(s) = EA·(u2 − u1)/Le           [N], konstant im Element
M(s) = EI·w''(s)                [Nm], höchstens quadratisch
V(s) = dM/ds = EI·w'''(s)        [N], höchstens linear
dV/ds = q                      [N/m]
```

Die Funktionen werden analytisch/stückweise ausgewertet. Es gibt weder eine
Sampling-Auflösung noch künstliche Zwischenknoten. Eine spätere Darstellung
kann die Funktionen unabhängig davon beliebig fein abtasten.

## Auswertungs-API und einseitige Grenzwerte

`BeamSolution.EvaluateAt(Length position, EvaluationSide side)` liefert einen
unveränderlichen `BeamSectionResult` mit folgenden Properties:

| Property | Typ / Einheit |
| --- | --- |
| Position | Core-`Length`, m |
| AxialDisplacement | Solver-`Displacement`, signed m |
| TransverseDisplacement | Solver-`Displacement`, signed m |
| RotationRadians | `double`, rad |
| AxialForce | Core-`Force`, N |
| ShearForce | Core-`Force`, N |
| BendingMoment | Core-`Moment`, Nm |

Die Seitenangabe ist ausdrücklich erforderlich; es gibt keinen Default und
keine Überladung ohne Seite. An inneren Knoten wählt `Left` den Grenzwert
aus dem linken Element, `Right` den aus dem rechten Element. Innerhalb eines
Elements liefern beide Angaben identische Ergebnisse. An x = 0 liefern beide
Angaben den vorhandenen Grenzwert 0+, an x = L den Grenzwert L−.

```csharp
BeamSolution solution = new EulerBernoulliBeamSolver().Solve(beam);
BeamSectionResult before = solution.EvaluateAt(loadPosition, EvaluationSide.Left);
BeamSectionResult after = solution.EvaluateAt(loadPosition, EvaluationSide.Right);
BeamExtremum<Displacement> minimumW = solution.Extrema.MinimumTransverseDisplacement;
```

An exakten Knoten werden u, w und θ aus dem gemeinsamen Knotenergebnis
übernommen. N, V und M stammen immer aus dem gewählten Element. Schnittgrößen
werden weder gemittelt noch geglättet. Für einen inneren Knoten gilt:

```text
V(x+) − V(x−) = Summe Punktkräfte + ReaktionY
M(x+) − M(x−) = −(Summe Punktmomente + Reaktionsmoment)
```

Fehlende Reaktionen werden hier als null behandelt. Eine Punktkraft erzeugt
einen Querkraftsprung, ein Punktmoment einen Momentsprung mit entgegengesetztem
Vorzeichen zum aufgebrachten Moment. Auch innere Lager können durch ihre
Reaktionen Sprünge erzeugen. Beispielsweise ergibt C = +400 Nm bei x = 0,75 m
auf einem einfach gelagerten Balken mit L = 2 m: V beidseitig +200 N,
M(x−) = +150 Nm und M(x+) = −250 Nm, also einen Sprung von −400 Nm.
Die beiden Endmomente sind null.

An einem Knoten ohne konzentrierte Last/Reaktion sind w, θ, M und V stetig.
Eine Änderung der UDL-Intensität erzeugt keinen Sprung in V oder M, sondern
ändert deren Ableitungen.

Die Elementzuordnung erfolgt binär mit exakt verglichenen Meterwerten. Eine
Position direkt neben einem Knoten wird nicht auf diesen gerundet.
Positionen außerhalb des Balkens und unbekannte `EvaluationSide`-Werte erzeugen
`ArgumentOutOfRangeException`; negative/nicht endliche Positionen werden bereits
durch Core-`Length` ausgeschlossen. Nicht endliche Feldberechnungen erzeugen
`BeamSolverException` mit `NumericalFailure`.

## Globale Extremwerte

`BeamSolution.Extrema` enthält die globalen vorzeichenbehafteten Minima und
Maxima von w, V und M. Die Properties sind `MinimumTransverseDisplacement`,
`MaximumTransverseDisplacement`, `MinimumShearForce`, `MaximumShearForce`,
`MinimumBendingMoment` und `MaximumBendingMoment`.

Jeder `BeamExtremum<T>` enthält `Value` (Displacement, Force oder Moment),
globale `Position` (Length) und nullable `Side`. Im Elementinneren ist `Side`
null; an Elementgrenzen kennzeichnet es den untersuchten Grenzwert. An den
Balkenenden gilt ausschließlich die innere Seite (Right bei 0, Left bei L).
Alle Ergebnisobjekte sind unveränderlich; die Extremwerte werden bereits beim
Lösen bestimmt.

Die Kandidaten entstehen aus den stückweisen Polynomen:

- w: Elementendpunkte und alle inneren reellen Nullstellen von θ(s).
- M: beide einseitigen Elementendwerte und innere Nullstellen von V(s).
- V: beide einseitigen Elementendwerte, da V im Element linear ist.

Die Wurzelsuche benötigt keine Samplingpunkte. Polynome werden nach ihrem
größten absoluten Koeffizienten skaliert; verschwindende führende Koeffizienten
reduzieren den Grad. Lineare Wurzeln werden direkt, quadratische mit einer
auslöschungsarmen Formel berechnet. Kubische Polynome werden durch die reellen
Ableitungsnullstellen in monotone Intervalle zerlegt und dort durch Bisektion
mit maximal 128 Schritten gelöst. Stationäre Stellen werden zusätzlich auf
Mehrfachwurzeln geprüft. Die Intervallbreite steuert die Bisektion, nicht eine
pauschale kleine Funktionswertschwelle.

Lokale Rundungsschranken verwenden ε = 2⁻⁵² (nicht `double.Epsilon`).
Wurzeln innerhalb von 32ε an 0 oder 1 werden dem betreffenden Endpunkt
zugeordnet; weiter außerhalb liegende Kandidaten werden verworfen. Wurzeln
innerhalb von 32ε werden dedupliziert. Diskriminanten beziehungsweise stationäre
Funktionswerte werden anhand von 64ε und ihrer jeweiligen Rechenskala geprüft.
Numerisch unauflösbare Mehrfachwurzelcluster werden einmal repräsentiert.
Diese Regeln gelten nur in der lokalen Polynomrechnung und ändern die exakten
Positionsvergleiche im Modell und in der Auswertungs-API nicht.

Jedes globale Minimum/Maximum liefert genau einen repräsentativen Ort. Für
Wertgleichstände gilt ein relativer Rundungsvergleich von 64ε ohne absolute
SI-Schwelle. Dann gewinnt die kleinste globale Position, bei gleicher Position
Left vor Right. Konstante Abschnitte werden durch einen Endpunkt repräsentiert,
nicht als Intervalle veröffentlicht. Die veröffentlichte Position lässt sich
mit der angegebenen Seite wieder über `EvaluateAt` auswerten.

## Verifikation und Grenzen

Tests decken die analytischen Fälle mittige Punktkraft auf Pinned/Roller,
Kragarm mit Endkraft, Endmoment, voller und partieller UDL sowie Pinned/Roller
mit konstanter UDL ab. Die bisherigen Knotenreferenzen bleiben erhalten.
Die kontinuierliche UDL-Referenz verwendet ausdrücklich eine einzige UDL
über 0 bis L mit nur zwei Knoten: Viertelpunkt und Mitte werden gegen die
analytische Durchbiegung geprüft, insbesondere `w(L/2) = −5q_absL⁴/(384EI)`.
M-Maximum und w-Minimum werden ohne Mittelknoten bei L/2 gefunden.
Weitere Referenzen prüfen drei Lager,
beidseitige Einspannung, Überhänge, Lastüberlagerung und Lagerlasten.
Struktur- und Fehlerprüfungen sichern DOFs, Assembly, Starrkörpermoden,
Singularität, Kondition, Eingabe-/Ergebnis-Unveränderlichkeit und signed Werte ab.
Kontinuierliche Referenzen prüfen innere Verformungs- und Schnittgrößenwerte,
Punktkraft-/Punktmomentsprünge mit beiden Lastvorzeichen, teilweise überlappende
UDLs, UDL-Grenzstetigkeit, innere Lagerreaktionen, zusätzliche Nullastknoten,
Kräfte-/Momentengleichgewicht und die Ableitungsbeziehungen. Extremwerttests
prüfen einseitige Werte, nichtnodale Extremstellen, mehrere stationäre Stellen,
Lastskalierung und deterministische Gleichstände. Separate Wurzeltests decken
reduzierte Grade, reelle/komplexe und mehrfache Wurzeln sowie Randfälle ab.

Ergebnisvergleiche verwenden `absTol + 1e-9 · |Soll|`: absolut `1e-7 N`,
`1e-7 Nm`, `1e-12 m` und `1e-12 rad`. Diese Testtoleranzen sind von den
numerischen Abbruchschwellen getrennt.

Der Solver liefert keine Festigkeitsbewertung. Reine elastische Biegespannung
und ein einfaches Verhältnis zur Streckgrenze werden als getrenntes Postprocessing
in [SpanDraft.Engineering](ENGINEERING.md) ausgewertet. Normative
Festigkeits-/Gebrauchstauglichkeitsnachweise, Schubverformung, geometrische
Nichtlinearität und Eigengewicht sind nicht implementiert.
Core hat derzeit keinen axialen Lasttyp; der axiale Gleichungsanteil
wird einschließlich u(x) und N(x) separat intern getestet. Öffentliche
Lastmodelle liefern normalerweise N = 0. Globale N-Extrema sind nicht Teil
der API. Querschnitt und Material sind konstant; nichtkonstante Linienlasten,
Lagerbewegungen und Gelenkfreigaben sind nicht vorgesehen. Diagramm-Rendering
und Diagramm-Sampling gehören nicht zum Solver.

Sehr kurze Elemente oder extreme Längenverhältnisse können trotz mechanischer
Stabilität die Konditionsgrenze überschreiten. Auch Über-/Unterläufe bei
Steifigkeitskennwerten werden abgelehnt. Die dichte Numerik benötigt quadratischen
Speicher und kubische Faktorisierungsarbeit und ist für kleine Einzelbalkenmodelle
vorgesehen. Der Solver liefert keinen Knick- oder sonstigen Stabilitätsnachweis.

Die Feldfunktionen sind analytisch, werden aber mit `double` ausgewertet.
Auslöschung kann insbesondere bei nominell verschwindenden Schnittgrößen
kleine Residuen hinterlassen. Nahe mehrfache Wurzeln können hinsichtlich ihrer
Position schlechter konditioniert sein als einfache Wurzeln; unterhalb der
Koeffizienten-Rundungsauflösung werden sie nicht getrennt ausgewiesen.
Extrem kleine Beiträge können unterlaufen; nicht endliche Koeffizienten oder
Ergebniswerte werden als NumericalFailure abgelehnt. Sehr kleine Elemente
weit vom Ursprung können zusätzlich die Darstellung innerer globaler
Positionen begrenzen. Mathematisch gleiche, binär unterschiedliche
Eingabepositionen werden weiterhin nicht zusammengelegt; deren Behandlung
an der Eingabe-/Modellgrenze bleibt ein separater späterer Auftrag.
