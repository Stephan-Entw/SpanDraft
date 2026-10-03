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
x und u nach rechts, w und Querkräfte nach oben, θ und Momente gegen den
Uhrzeigersinn. Die Rotation ist θ = dw/dx, in rad.

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

## Verifikation und Grenzen

Tests decken die analytischen Fälle mittige Punktkraft auf Pinned/Roller,
Kragarm mit Endkraft, Endmoment, voller und partieller UDL sowie Pinned/Roller
mit konstanter UDL ab. Der Mittelknoten des letzten Falls entsteht durch zwei
angrenzende UDLs gleicher Intensität. Weitere Referenzen prüfen drei Lager,
beidseitige Einspannung, Überhänge, Lastüberlagerung und Lagerlasten.
Struktur- und Fehlerprüfungen sichern DOFs, Assembly, Starrkörpermoden,
Singularität, Kondition, Eingabe-/Ergebnis-Unveränderlichkeit und signed Werte ab.

Ergebnisvergleiche verwenden `absTol + 1e-9 · |Soll|`: absolut `1e-7 N`,
`1e-7 Nm`, `1e-12 m` und `1e-12 rad`. Diese Testtoleranzen sind von den
numerischen Abbruchschwellen getrennt.

Ergebnisse existieren ausschließlich an erzeugten Knoten; zwischen ihnen
wird keine Verformung interpoliert. Schnittgrößen, Spannungen, Sicherheitsfaktoren,
Schubverformung, geometrische Nichtlinearität und Eigengewicht sind nicht
implementiert. Core hat derzeit keinen axialen Lasttyp; der axiale Gleichungsanteil
wird separat intern getestet. Querschnitt und Material sind konstant,
Lagerbewegungen und Gelenkfreigaben sind nicht vorgesehen.

Sehr kurze Elemente oder extreme Längenverhältnisse können trotz mechanischer
Stabilität die Konditionsgrenze überschreiten. Auch Über-/Unterläufe bei
Steifigkeitskennwerten werden abgelehnt. Die dichte Numerik benötigt quadratischen
Speicher und kubische Faktorisierungsarbeit und ist für kleine Einzelbalkenmodelle
vorgesehen. Der Solver liefert keinen Knick- oder sonstigen Stabilitätsnachweis.
