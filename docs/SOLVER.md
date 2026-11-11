# SpanDraft – Solver

## Produkteinstieg und mechanischer Umfang

`EulerBernoulliBeamSolver.Solve(BeamModel)` ist der einzige Produkteinstieg.
Er validiert das unveränderte Core-Modell, bildet dessen freigegebene Mechanik
auf den internen `SolverKernel` ab und liefert eine unveränderliche
`BeamSolution`. Core bleibt unabhängig von Numerik, UI und Persistenz.

Berechnet wird ein einzelner gerader Euler-Bernoulli-Balken mit konstantem
Werkstoff und Querschnitt, linear elastisch bei kleinen Verschiebungen und
Rotationen. Querschnitte bleiben eben und senkrecht zur verformten Balkenachse;
Schubverformung wird vernachlässigt. Unterstützt sind starre Einspannungen,
Fest- und Loslager, Überhänge, transversale Punktkräfte, Punktmomente und
konstante, auch überlappende Streckenlasten. Eigengewicht wird bereits im
Fachmodell als konstante Streckenlast abgeleitet. Alle Werte verwenden SI.

Die Biegesteifigkeit ist `EI = E · I` mit E aus dem Werkstoff und I ausschließlich
aus `beam.BendingAxisProperties.SecondMomentOfArea`. Y/Z/U/V wählen die
zugehörige Querschnittsachse, ohne die planare Solverformulierung zu ändern.
Die positive transversale Richtung w gehört zu dieser ausgewählten Biegeebene
und zeigt in der Balkendarstellung nach oben; sie ist nicht allgemein die
geometrische +z-Richtung des Querschnitts. Räumliche Profilorientierung,
3D-Lasttransformation und gleichzeitige biaxiale Biegung sind nicht modelliert.

Der öffentliche Lastumfang enthält keine äußeren Axiallasten. Deshalb sind
u und N an allen Positionen und auf beiden Seiten exakt +0. An axial gehaltenen
Stationen ist `ReactionX` vorhanden und exakt +0; an freien fehlt die Reaktion.
Ein belastetes Axialsystem wird nicht gelöst, `EA` wird nicht gebildet.
Die Fläche bleibt unter anderem für Eigengewicht und Engineering relevant.

## Stationen, Ereignisse und Lager

Mechanische Stationen liegen an beiden Balkenenden und an allen Lagerpositionen.
Nur dort entstehen freie Komponenten des lokalen Biegezustands `(w, θ, M, V)`.
Vorgeschriebene Komponenten werden exakt eliminiert. Reine Lastpositionen,
Streckenlastgrenzen und spätere Auswertungspositionen erzeugen keine zusätzlichen
mechanischen Unbekannten und verändern die Mechanikmatrix nicht.

Ereignisstationen sind die sortierte Vereinigung von Balkenenden, Lagern,
Punktkräften, Punktmomenten und Streckenlastgrenzen, einschließlich Positionen
nullwertiger Lasten. Sie bestimmen die öffentlichen `Nodes` und die Grenzen der
Feldstücke. Exakt gleiche gespeicherte Meterwerte bilden eine gemeinsame
Station. Verschiedene Werte bleiben verschieden, auch bei benachbarten
Binary64-Zahlen; es gibt kein Snapping und keine Abstandstoleranz.
±0 bezeichnet dieselbe Position. Abstände stammen aus gespeicherten globalen
Koordinaten und nicht aus einer gerundeten lokalen Koordinatendifferenz.

| Lager | Gesperrte Richtungen | Freie Richtungen |
| --- | --- | --- |
| Fixed / Einspannung | u, w, θ | keine |
| Pinned / Festlager | u, w | θ |
| Roller / Loslager | w | u, θ |

Alle vorgeschriebenen Verschiebungen und Rotationen sind exakt null.
Verschiedene Lagertypen an derselben Position vereinigen ihre Sperren.
Die Reaktion wird einmal je Station und gesperrter Richtung ausgegeben;
eine Aufteilung auf übereinanderliegende Lager ist nicht eindeutig und wird
nicht vorgenommen. Die Core-Prüfung gleicher Lagerduplikate bleibt vorgeschaltet.
Es gibt keine Strafsteifigkeiten, künstlichen Federn oder Regularisierung.

## Vorzeichen und Lastabbildung

Die Vorzeichen aus [DOMAIN.md](DOMAIN.md) gelten: x und u nach rechts,
w und aufgebrachte Kräfte/Streckenlasten nach oben, θ und aufgebrachte Momente
gegen den Uhrzeigersinn. `θ = dw/dx`, N > 0 bedeutet Zug, M > 0 positives/sagendes
Biegemoment; `dM/dx = V` und `dV/dx = q` auf offenen Laststücken.
Für die Biegung gilt `EI · w'' = M` und `EI · w'''' = q`.

Punktkräfte und Punktmomente wirken mit ihrer tatsächlichen Position über
analytische Lastintegrale und Stationssprünge. Konstante Streckenlasten werden
über ihre tatsächlichen Grenzen integriert, nicht durch Punktkräfte ersetzt.
Überlappungen und Lasten an Lagern werden additiv berücksichtigt. Innerhalb
eines mechanischen Abschnitts bleiben sämtliche Last-Ereignisse erhalten,
ohne den Abschnitt in Steifigkeitselemente zu zerlegen.

Eine mittige Kraft −P auf einem einfach gelagerten Balken ergibt V = +P/2 links,
V = −P/2 rechts und M = +PL/4 unter der Last. Beim links eingespannten Kragarm
mit Endkraft −P gilt V = +P, M(0) = −PL, M(L) = 0; das Reaktionsmoment ist +PL.

## Stabilität, Lösung und verifizierte Ausgabe

Zuerst läuft `BeamModelValidator.Validate`. Bei Modellfehlern wird nicht gelöst.
Danach prüft der Kernel die Starrkörpermoden unabhängig von Lasten und
Steifigkeit: axiale Translation `u = c` und Biegebewegung `w = a + bx`, `θ = b`.
Er verwendet den exakten Rang der Lagerbedingungen auf den gespeicherten
Binary64-Koordinaten, ohne numerische Rangschwelle. Nötig sind mindestens eine
u-Sperre und entweder eine Einspannung oder w-Sperren an zwei verschiedenen
Positionen. Die Mechanismusprüfung hat Vorrang vor numerischen Bereichsfehlern,
auch wenn bereits das Adapterprodukt E·I nicht darstellbar wäre.
Sie findet auch bei unbelasteten Modellen statt.

Analytische Lastintegrale übertragen lokale Biegezustände zwischen mechanischen
Stationen. Rand-, Stetigkeits- und Sprungbedingungen ergeben ein reduziertes
lineares System ausschließlich freier Zustandskomponenten. Es ist kein globales
FE-Verschiebungssystem. Zweierpotenz-Normierung und Ruiz-Äquilibrierung schützen
die Darstellung unterschiedlicher Größenordnungen. Eine dichte LU-Zerlegung
mit partieller Pivotisierung und Nachiteration erzeugt einen Lösungskandidaten.

Die Freigabe hängt nicht allein von einem kleinen Residuum, einer geschätzten
Kondition oder dem Erfolg der Faktorisierung ab. Daten- und Rundungsfehler
werden eingeschlossen. Verifizierte adjungierte Residuen und ein gerichteter
Kontraktionsnachweis mit μ < 1 begrenzen den Zustandsfehler. Feld- und
Reaktionsauswertung führen daraus eigene Ausgabeschranken fort, einschließlich
Lastintegralen, Auswertung und Rückskalierung in SI.

Ein gerichtetes Ausgabe-Gate prüft Ereignisseiten, Reaktionen und kontinuierliche
Stückgrenzen über den ganzen Balken. Für gelieferte Größen gilt am ausgegebenen
Ort und auf der ausgegebenen Seite:

```text
|berechneter Wert − exakter Wert| ≤ VerifiedOutputBound ≤ τ · ReferenceScale
τ = 1 / 100 000 000
```

Die Referenz ist die exakte Mechanik auf den gespeicherten Solvereingaben.
Die Implementierung schließt zusätzlich die Rundung des Adapterprodukts E·I ein.
`ReferenceScale` ist eine mechanisch definierte Bezugsskala aus Anregung und
verifizierten Feldgrößen, bei Schnittgrößen zusätzlich dem lokalen Betrag.
Die Aussage ist keine relative Fehlergarantie gegenüber einem einzelnen
verschwindenden Feldwert und kein Nachweis der Genauigkeit realer Eingabedaten.
Der Schwellenvergleich berücksichtigt das mathematisch exakte τ; ein gerundeter
Fehlerquotient entscheidet die Freigabe nicht. Die Schranken bleiben intern.

Nur wenn jedes gespeicherte Lastprimitiv exakt null ist, darf nach erfolgreicher
Modell-, Mechanismus- und Aufbauprüfung der Nullpfad ohne Faktorisierung exakte
Nullergebnisse liefern. Kompensierende nicht-null Lasten oder ein nullwertiger
rechter Vektor bleiben belastete Systeme und durchlaufen die Verifikation.

Kann ein Bereich, die Verifikation oder das Gate nicht eingehalten werden,
lehnt `Solve` das Modell ohne Teilresultat ab. Es gibt keinen alternativen
Produktsolver, keine Pseudoinverse und keine automatische Modellreparatur.
Den Detailvertrag beschreiben [SOLVER_ARCHITEKTUR.md](SOLVER_ARCHITEKTUR.md)
und der [Gleitkomma-Vertrag](../validation/investigations/solver-rework/FLOATING_POINT_CONTRACT.md).
Deren Erweiterungsentwürfe sind keine zusätzlichen Produktfunktionen.

## Öffentlicher Fehlervertrag

`Solve(null)` wirft `ArgumentNullException`. Erwartete Modell- und numerische
Ablehnungen werden als `BeamSolverException` mit unverändertem `SolverErrorCode`
veröffentlicht:

| Code | Bedeutung |
| --- | --- |
| `InvalidModel` | Core-Modellprüfung fehlgeschlagen; `ValidationErrors` enthält alle ursprünglichen Fehler. |
| `UnstableModel` | Exakte Lagerprüfung lässt eine axiale oder transversale Starrkörpermode frei. |
| `IllConditionedSystem` | Ein exakt stabiles belastetes System ist numerisch nicht sicher lieferbar: Nullpivot (`ZeroPivot`), fehlender Verifikationsnachweis (`NotVerified`) oder überschrittenes Ausgabe-Gate (`Gate`). |
| `NumericalFailure` | Bereichsregel verletzt (`NumericalRange`), erforderlicher Wert nicht endlich (`NonFinite`) oder interne Struktur inkonsistent (`InternalInconsistency`). |

`IllConditionedSystem` bedeutet „numerisch nicht sicher lösbar“, nicht
„eine Konditionsschwelle wurde überschritten“. Ein winziger nicht-null Pivot
an oder unter der Normalgrenze ist ein Bereichsfehler; ein Nullpivot ist davon
getrennt. Bei Kernelablehnungen bleibt die ursprüngliche Ausnahme als
`InnerException` für Diagnose erhalten. Die internen Ursachentypen sind keine
zusätzliche öffentliche API. Außer bei `InvalidModel` ist `ValidationErrors` leer.

## Ergebnisse, Reaktionen und Auswertung

`BeamSolution.Beam` verweist auf das ursprüngliche unveränderte `BeamModel`.
`Nodes` ist eine unveränderliche, nach Ereignisposition sortierte Liste von
`BeamNodeResult`: fortlaufender Index, Position als Core-`Length`, u/w als
solver-eigener signed `Displacement` mit `Meters`, Rotation als
`RotationRadians` und nullable Reaktionen in Core-`Force` bzw. Core-`Moment`.
`null` bezeichnet eine freie Richtung. Eine nullwertige Reaktion an einer
gesperrten Richtung bleibt ein vorhandener Wert.

Biegereaktionen folgen aus den Zustands- und Lastsprüngen an der jeweiligen
mechanischen Station; ihre Fehler werden mit eigenen Reaktionsfunktionalen
verifiziert. Lasten unmittelbar am Lager sind eingeschlossen. Freie Richtungen
veröffentlichen kein numerisches Residuum als Reaktion.

`BeamSolution.EvaluateAt(Length position, EvaluationSide side)` liefert einen
unveränderlichen `BeamSectionResult` mit Position, `AxialDisplacement`,
`TransverseDisplacement`, `RotationRadians`, `AxialForce`, `ShearForce` und
`BendingMoment`, jeweils in SI. Die Seite ist verpflichtend; es gibt keinen
Default und keine Überladung ohne Seite.

An inneren Ereignissen wählt `Left` den linksseitigen und `Right` den
rechtsseitigen Grenzwert. Innerhalb eines offenen Stücks liefern beide Seiten
dieselben Werte. An x = 0 liefern beide den vorhandenen inneren Grenzwert 0+,
an x = L beide L−. An exakten Stationen stimmen w und θ auf beiden Seiten
bitgleich mit dem gemeinsamen Knotenergebnis überein; gesperrte Komponenten
bleiben exakt null. V und M behalten dagegen ihre jeweilige Seite und werden
weder gemittelt noch geglättet. u und N bleiben auf beiden Seiten exakt +0.

```csharp
BeamSolution solution = new EulerBernoulliBeamSolver().Solve(beam);
BeamSectionResult before = solution.EvaluateAt(loadPosition, EvaluationSide.Left);
BeamSectionResult after = solution.EvaluateAt(loadPosition, EvaluationSide.Right);
BeamExtremum<Displacement> minimumW = solution.Extrema.MinimumTransverseDisplacement;
```

Für innere Ereignisse gelten die Sprünge:

```text
V(x+) − V(x−) = Summe Punktkräfte + ReactionY
M(x+) − M(x−) = −(Summe Punktmomente + ReactionMoment)
```

Fehlende Reaktionen zählen hier als null. Eine Änderung der Streckenlastintensität
ändert Ableitungen, erzeugt aber keinen Sprung in V oder M. Ohne konzentrierte
Last/Reaktion sind w, θ, V und M stetig.

Die Felder sind analytisch und stückweise: w ist bei konstanten Streckenlasten
höchstens quartisch, θ kubisch, M quadratisch und V linear. Es gibt keine
Solver-Samplingauflösung und keine künstlichen Zwischenknoten. Diagramm-Sampling
und Rendering liegen außerhalb des Solvers. Die Zuordnung verwendet binäre
Suche und exakte Metervergleiche; eine Position neben einer Station wird nicht
auf diese gerundet. Positionen außerhalb des Balkens und unbekannte Seiten
erzeugen `ArgumentOutOfRangeException`; negative oder nicht endliche Positionen
verhindert bereits Core-`Length`.

## Globale Extremwerte

`BeamSolution.Extrema` enthält globale vorzeichenbehaftete Minima und Maxima:
`MinimumTransverseDisplacement`, `MaximumTransverseDisplacement`,
`MinimumShearForce`, `MaximumShearForce`, `MinimumBendingMoment` und
`MaximumBendingMoment`. Jeder unveränderliche `BeamExtremum<T>` enthält `Value`,
globale `Position` und nullable `Side`. Im offenen Stück ist `Side` null, an
Grenzen bezeichnet es den untersuchten Grenzwert. An den Balkenenden wird nur
die innere Seite veröffentlicht: Right bei 0 und Left bei L.

Kandidaten sind beide inneren Randseiten jedes Ereignisstücks sowie die inneren
reellen Nullstellen von θ für w und von V für M. V benötigt bei konstanter
Streckenlast nur die Randwerte. `PolynomialRoots` bestimmt die Kandidaten ohne
Raster; Skalierung, Gradreduktion, auslöschungsarme quadratische Formeln und
Ableitungsintervalle mit Bisektion behandeln auch Mehrfachwurzeln.
Lokale Randzuordnung und Wurzeldeduplizierung verwenden 32ε, Rundungsvergleiche
64ε mit ε = 2⁻⁵². Diese lokalen Regeln ändern keine Modellpositionen.
Numerisch unauflösbare Wurzelcluster können nur einmal repräsentiert werden.

Bei Wertgleichstand innerhalb des relativen Rundungsvergleichs von 64ε gewinnt
die kleinste globale Position, danach Left vor Right. Es gibt keine absolute
SI-Gleichheitsschwelle. Konstante Intervalle werden durch einen Endpunkt
repräsentiert. Jeder ausgegebene Extremwert lässt sich mit seiner Position
und Seite über `EvaluateAt` reproduzieren; bei `Side = null` liefern beide
Seiten denselben Wert.

Der verifizierte Output-Bound gilt für den Wert am ausgegebenen Kandidatenort.
Er beweist weder einen ebenso kleinen Ortsfehler noch für beliebige entartete
Polynome die Vollständigkeit aller getrennten Extremorte. Diese Eigenschaften
werden durch unabhängige analytische und exakte Root-Oracles geprüft.

## Verifikation und bekannte Grenzen

Die Product-Suite prüft analytische Lösungen, Zustands- und Ausgabeschranken,
exakte Grenzfälle, Fehlerklassen, Koinzidenzen, nahe Ereignisse, Sprünge und
Extrema. Die separate öffentliche Produkt-Acceptance vergleicht gegen feste
analytische und externe Referenzen sowie physikalische und metamorphische
Prüfungen. Herkunft und aktuelle Abnahme stehen in [VALIDATION.md](VALIDATION.md),
die Teststrategie in [TESTING.md](TESTING.md). Testtoleranzen und numerische
Freigabeschwellen sind getrennte Verträge.

Bereichsgrenzen schützen die Gleitkomma-Prämissen für Eingaben, EI, Abstände,
normierte Lasten, Koeffizienten, Pivots und SI-Ausgaben. Auch ein mechanisch
stabiles Modell kann deshalb oder wegen konservativer Bounds abgelehnt werden.
Nähe allein ist kein Mechanismus und kein pauschaler Ablehnungsgrund.
Gespeicherte Eingabepositionen werden nicht nachträglich vereinigt.

Die Implementierung verwendet dichte Matrizen mit quadratischem Speicherbedarf
und kubischer Lösungs-/Verifikationsarbeit. Viele Lasten in einem Abschnitt
verteuern die wiederholte analytische Auswertung. Eine allgemeine Modellgrößen-
oder Latenzzusage wird daraus nicht abgeleitet. Sehr kurze Stücke weit vom
Ursprung können die Darstellung innerer globaler Extrempositionen begrenzen.
Die Rechnung verwendet `double`; kleine Rundungsreste werden nicht pauschal
auf null gesetzt oder geglättet.

Nicht unterstützt sind belastete Axialmechanik, Federn, Gelenkfreigaben,
veränderliche Steifigkeiten, nichtkonstante Streckenlasten, Lagerbewegungen,
Schubverformung und geometrische Nichtlinearität. Historische Prototypen und
eingefrorene FE-Vergleichsrechner liegen ausschließlich außerhalb des Produktpfads.
Der Solver liefert keinen Knick-, Festigkeits- oder normativen Nachweis.
Die elastische Spannungsbewertung ist getrenntes Postprocessing in
[ENGINEERING.md](ENGINEERING.md).
