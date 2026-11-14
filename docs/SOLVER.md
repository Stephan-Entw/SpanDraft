# SpanDraft – Solver

## Produkteinstieg und mechanischer Umfang

`EulerBernoulliBeamSolver.Solve(BeamModel)` ist der einzige Produkteinstieg.
Er validiert das unveränderte Core-Modell, bildet dessen freigegebene Mechanik
auf den internen `SolverKernel` ab und liefert eine unveränderliche
`BeamSolution`. Core bleibt unabhängig von Numerik, UI und Persistenz.

Berechnet wird ein einzelner gerader Euler-Bernoulli-Balken aus einem oder mehreren
Abschnitten mit je konstantem Werkstoff und Querschnitt, linear elastisch bei kleinen Verschiebungen und
Rotationen. Querschnitte bleiben eben und senkrecht zur verformten Balkenachse;
Schubverformung wird vernachlässigt. Unterstützt sind starre Einspannungen,
Fest- und Loslager, linear-elastische Federlager, innere Gelenke (Momentenfreigaben), Überhänge, transversale Punktkräfte, Punktmomente und
konstante und linear veränderliche, auch überlappende Streckenlasten. Eigengewicht wird bereits im
Fachmodell als konstante Streckenlast abgeleitet. Alle Werte verwenden SI.

Die Biegesteifigkeit ist `EI = E · I` mit E aus dem Werkstoff und I ausschließlich
aus `BendingAxisProperties.SecondMomentOfArea` des jeweiligen Abschnitts. Y/Z/U/V wählen die
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

Mechanische Stationen liegen an beiden Balkenenden, an allen Lagerpositionen
(auch an Federlagern) und an allen Gelenken.
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
| Spring / Federlager | u | w und θ elastisch, je nach Federn |

Alle vorgeschriebenen Verschiebungen und Rotationen sind exakt null.
Verschiedene Lagertypen an derselben Position vereinigen ihre Sperren.
Die Reaktion wird einmal je Station und gesperrter Richtung ausgegeben;
eine Aufteilung auf übereinanderliegende Lager ist nicht eindeutig und wird
nicht vorgenommen. Die Core-Prüfung gleicher Lagerduplikate bleibt vorgeschaltet.
Es gibt keine Strafsteifigkeiten, künstlichen Federn oder Regularisierung.

### Federlager

Ein Federlager hält axial starr (u = 0, `ReactionX` exakt +0, keine Axialfeder) und
besitzt eine Wegfeder k (N/m), eine Drehfeder k_θ (N·m/rad) oder beide. Mit Drehfeder
darf die Querverschiebung starr gehalten werden (Teileinspannung: w = 0, θ elastisch);
eine starre Translation ohne Drehfeder wäre ein Loslager und ist ungültig, ebenso eine
starre Translation mit Wegfeder. Die Federgesetze `R = −k·w⁺` und `C = −k_θ·θ⁺` (Rückstellkraft
beziehungsweise -moment auf den Balken) sind Teil der Stationsregeln und
ersetzen die Zeile des Querkraft- beziehungsweise Momentensprungs:
`V⁺ − V⁻ + k·w⁺ = P`, `M⁺ − M⁻ − k_θ·θ⁺ = −C`. w und θ bleiben freie Unbekannte,
die Zeilenzahl ändert sich nicht. Die Steifigkeiten gehen als exakte Zweierpotenz-Skalierung
`k·2^(3·e_L − e_EI)` beziehungsweise `k_θ·2^(e_L − e_EI)` ohne Rundung ein und müssen
normal darstellbar sein (sonst `NumericalFailure`, `NumericalRange`). Es gibt keinen Ersatz für starre
Lager und keine Steifigkeitsgrenze; ein starres Lager ist kein k = ∞.
Der dokumentierte Nachweisbereich ist k·L³/EI und k_θ·L/EI von 10⁻³ bis 10¹⁵; darunter
und darüber entscheidet das Ausgabe-Gate.

Für den Rang zählt eine Feder durch ihre Wirkrichtung, unabhängig vom Wert: eine Wegfeder
wie eine w-Sperre, eine Drehfeder wie eine θ-Sperre. Eine einzelne Feder lässt deshalb
einen Mechanismus (`UnstableModel`), ein Balken nur auf Federn ist dagegen stabil.

Ein Federlager darf an keiner Position mit einem anderen Lager oder einer weiteren Feder
zusammenfallen (`SpringSupportCoincidence`, `InvalidModel`); der Kernel prüft das zusätzlich.
Lasten genau auf der Feder sind zulässig.

### Innere Gelenke

Ein inneres Gelenk (`MomentRelease`) ist eine mechanische Station ohne Sperre. Auf ihr gilt
M⁺ = 0 als vorgeschriebener Wert; die Spalte entfällt, die Momentensprungzeile bleibt und erzwingt
M⁻ = 0. Die Zeile der Rotationsstetigkeit entfällt, weil θ springt; w, V und N bleiben stetig, die
Querkraft springt um eine Punktkraft und eine Reaktion. Das System bleibt quadratisch (ein Gelenk
entfernt eine Spalte und eine Zeile), die Matrixeinträge und das Bandmuster bleiben unverändert. An
der Station liefern `EvaluateAt` und die Knotenstation beide Rotationen: `RotationRadians` ist der
rechtsseitige Grenzwert und `BeamNodeResult.RotationLeftRadians` der linksseitige, der nur an einem
Gelenk vorhanden ist (sonst `null`). Das Moment wird auf beiden Seiten als exakt 0 geliefert; seine
Schranke ist die fortgepflanzte Schranke des linken Stücks, und ein berechneter Wert außerhalb dieser
Schranke ist ein `InternalInconsistency`. Es gibt keine Strafsteifigkeit, kein Snapping und kein
Zusammenlegen von Positionen; Gate und τ = 10⁻⁸ bleiben unverändert.

Eine Last genau auf einem Gelenk ist zulässig, ein Punktmoment dagegen nicht: ohne Seitenzuordnung
hätte das Gelenk kein Gleichgewicht (`InvalidModel`). Ebenso lehnt der Kernel ein Gelenk ab, das nicht
im Balkeninneren liegt, ein zweites an gleicher Position und eines an einer Drehbehinderung
(Einspannung, Drehfeder, Teileinspannung). Ein Gelenk auf einem Fest-, Los- oder Wegfederlager trennt
den Balken in zwei Felder mit gemeinsamer Reaktion; auf einer Abschnittsgrenze behält jedes Intervall
das EI seines eigenen Abschnitts.

Die Mechanismusprüfung bildet die Teilkörper zwischen den Gelenken: je Teilkörper `w = a + b·x`, je
Gelenk eine Stetigkeitszeile für w, je gesperrter oder federnder Translation eine Zeile `a + b·x = 0`
für jeden Teilkörper, der die Position berührt, je Drehsperre oder Drehfeder `b = 0`. Der exakte Rang
muss alle `2·(Gelenke + 1)` Moden binden; Federn zählen durch ihre Wirkrichtung, Abschnittsgrenzen
erzeugen keine Zeilen. Ein Kragarm mit Gelenk, ein Gelenk zwischen zwei Festlagern, ein Teilstück ohne
Halt und zu viele Gelenke sind Mechanismen (`UnstableModel`; die Meldung nennt bei vorhandenen
Gelenken diese). Die Prüfung entscheidet exakt, nicht durch Zählen: Pinned–Roller–Roller ist mit einem
Gelenk bestimmt und mit zweien ein Mechanismus.

## Vorzeichen und Lastabbildung

Die Vorzeichen aus [DOMAIN.md](DOMAIN.md) gelten: x und u nach rechts,
w und aufgebrachte Kräfte/Streckenlasten nach oben, θ und aufgebrachte Momente
gegen den Uhrzeigersinn. `θ = dw/dx`, N > 0 bedeutet Zug, M > 0 positives/sagendes
Biegemoment; `dM/dx = V` und `dV/dx = q` auf offenen Laststücken.
Für die Biegung gilt `EI · w'' = M` und `EI · w'''' = q`.

Punktkräfte und Punktmomente wirken mit ihrer tatsächlichen Position über
analytische Lastintegrale und Stationssprünge. Streckenlasten mit zwei Randintensitäten q₁ und q₂
(linear veränderlich, gleiche Werte ergeben eine konstante Last) werden über ihre
tatsächlichen Grenzen integriert, nicht durch Punktkräfte ersetzt. Das Lastintegral
ist die Summe eines Terms mit der Anfangsintensität und eines Rampenterms mit dem
Anstieg q₂ − q₁. Beide verwenden globale positive Abstände, sodass auch eine sehr
kurze Last weit vom Ursprung Resultierende und erstes Moment behält. An einer
mechanischen Station zugeschnittene Lasten interpolieren ihre Randintensitäten aus
den globalen Abständen; der Interpolationsfehler gehört zur verifizierten Schranke.
Für die Anregungsnorm gilt das Trapezmittel (|q₁| + |q₂|)/2 · l, auch bei
Vorzeichenwechsel und bei verschwindender Resultierenden. Ein zugeschnittenes
Laststück muss normiert mindestens 2⁻⁹⁶⁹ breit sein, sonst lehnt der Solver mit
`NumericalFailure` (`NumericalRange`) ab.
Überlappungen und Lasten an Lagern werden additiv berücksichtigt. Innerhalb
eines mechanischen Abschnitts bleiben sämtliche Last-Ereignisse erhalten,
ohne den Abschnitt in Steifigkeitselemente zu zerlegen.

### Abschnitte mit unterschiedlicher Steifigkeit

Der Adapter bildet jeden `BeamSegment` auf einen `StiffnessSection(Start, End, EI)` ab, ohne
gleiche Nachbarn zusammenzulegen. Jede Abschnittsgrenze ist im Kernel eine mechanische Station
ohne Sperren: Sie liefert wie jede Station vier Unbekannte und vier Gleichungen im bekannten
Muster und ändert die Bandbreite nicht. Grenze und Lager, Einspannung, Federlager oder Last auf
derselben Position verschmelzen zu einer Station; die Koinzidenzregel der Federlager gilt nur für
Lager. Die Grenze ist selbst keine mechanische Unbekannte außer dem Sprung von EI: M, V, w und
θ sind dort stetig (außer an einer Punktlast oder einem Lager auf der Grenze), w'' = M/EI springt
um das EI-Verhältnis. Es gibt keine Penalty, kein Snapping und keine Zusammenlegung von
Positionen.

Den Abschnitt eines Intervalls zwischen zwei mechanischen Stationen bestimmt der exakte
Vergleich der Station, nie eine Mittelpunktklassifikation. Die Abschnitte müssen den Balken
bitgenau überdecken, sonst lehnt der Kernel mit `InvalidModel` ab. Die Normierung folgt dem
steifsten Abschnitt (`e_EI = ilogb(max EI)`); jedes normierte EI muss normal sein, ein extremes
Verhältnis (hier ab etwa 10³⁰⁰) wird mit `NumericalFailure` (`NumericalRange`) abgelehnt.
Gewichte der Verifikation und Referenzskalen der Ausgabeschranken nutzen kleinstes und größtes
EI aller Intervalle, die Stückschranke das EI ihres eigenen Abschnitts. Das Gate und τ = 10⁻⁸
bleiben unverändert; ein EI-Kontrast von 10³ bis 10⁶ wurde ohne Ablehnung geliefert
([VALIDATION.md](VALIDATION.md)).

`BeamSolution.SegmentExtrema` liefert je Abschnitt ein `BeamExtrema`. Es entsteht aus denselben
Polynomstücken wie das globale Extremum, eingeschränkt auf die Stücke des Abschnitts; an einer
Grenze zählt die innere Seite (rechts am Beginn, links am Ende). Das ist kein Sampling und keine
Zweitlösung. Eine Grenze auf einer Punktlast oder einem Lager gehört beiden Nachbarn mit dem
Wert ihrer Seite.

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
Positionen; mit Gelenken gilt die Teilkörperprüfung von „Innere Gelenke“. Die Mechanismusprüfung hat Vorrang vor numerischen Bereichsfehlern,
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
`RotationRadians` (an einem Gelenk der rechtsseitige Grenzwert; der linksseitige steht in
`RotationLeftRadians`, sonst `null`) und nullable Reaktionen in Core-`Force` bzw. Core-`Moment`.
`null` bezeichnet eine freie Richtung. Eine nullwertige Reaktion an einer
gesperrten Richtung bleibt ein vorhandener Wert.

Biegereaktionen folgen aus den Zustands- und Lastsprüngen an der jeweiligen
mechanischen Station; ihre Fehler werden mit eigenen Reaktionsfunktionalen
verifiziert. Lasten unmittelbar am Lager sind eingeschlossen. Freie Richtungen
veröffentlichen kein numerisches Residuum als Reaktion.

Eine Federreaktion hat zwei exakte Auswertungen, den Sprung und das Federgesetz. Beide
erhalten ihre eigene verifizierte Schranke, ihre Differenz ist das Residuum der Federzeile
und darf die Summe der Schranken nicht übersteigen (sonst `NumericalFailure`,
`InternalInconsistency`). Geliefert wird der Wert mit der kleineren Schranke, bei Gleichheit
der Sprung. `ReactionY` eines Wegfederlagers ist die Federkraft, `ReactionMoment` eines
Drehfederlagers das Federmoment; beide gehen mit ihrer Schranke in das Gate ein.

`BeamSolution.EvaluateAt(Length position, EvaluationSide side)` liefert einen
unveränderlichen `BeamSectionResult` mit Position, `AxialDisplacement`,
`TransverseDisplacement`, `RotationRadians`, `AxialForce`, `ShearForce` und
`BendingMoment`, jeweils in SI. Die Seite ist verpflichtend; es gibt keinen
Default und keine Überladung ohne Seite.

An inneren Ereignissen wählt `Left` den linksseitigen und `Right` den
rechtsseitigen Grenzwert. Innerhalb eines offenen Stücks liefern beide Seiten
dieselben Werte. An x = 0 liefern beide den vorhandenen inneren Grenzwert 0+,
an x = L beide L−. An exakten Stationen stimmen w und θ auf beiden Seiten
bitgleich mit dem gemeinsamen Knotenergebnis überein, mit Ausnahme von θ an einem Gelenk, wo
die Seiten verschieden sind; gesperrte Komponenten
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
höchstens quartisch, θ kubisch, M quadratisch und V linear; bei linear
veränderlicher Last sind es w quintisch, θ quartisch, M kubisch und V quadratisch. Es gibt keine
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
reellen Nullstellen von θ für w, von V für M und der linearen Intensität q für V.
V benötigt bei konstanter Streckenlast nur die Randwerte; bei linear veränderlicher
Last liegt das Querkraftextremum an der Nullstelle von q. `PolynomialRoots` bestimmt die Kandidaten ohne
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
Auch eng benachbarte und sehr steife oder weiche Federn werden ohne Gruppierung und
ohne Modelländerung gerechnet; was das Gate nicht trägt, wird abgelehnt.
Gespeicherte Eingabepositionen werden nicht nachträglich vereinigt.

Die Implementierung verwendet dichte Matrizen mit quadratischem Speicherbedarf
und kubischer Lösungs-/Verifikationsarbeit. Viele Lasten in einem Abschnitt
verteuern die wiederholte analytische Auswertung. Eine allgemeine Modellgrößen-
oder Latenzzusage wird daraus nicht abgeleitet. Sehr kurze Stücke weit vom
Ursprung können die Darstellung innerer globaler Extrempositionen begrenzen.
Die Rechnung verwendet `double`; kleine Rundungsreste werden nicht pauschal
auf null gesetzt oder geglättet.

Nicht unterstützt sind belastete Axialmechanik, Axialfedern, Querkraft- und Axialgelenke
(nur Momentenfreigaben), Federgelenke mit `M = k·Δθ`, veränderliche Steifigkeiten, nichtlineare Streckenlastverläufe, Lagerbewegungen,
Schubverformung und geometrische Nichtlinearität. Historische Prototypen und
eingefrorene FE-Vergleichsrechner liegen ausschließlich außerhalb des Produktpfads.
Der Solver liefert keinen Knick-, Festigkeits- oder normativen Nachweis.
Die elastische Spannungsbewertung ist getrenntes Postprocessing in
[ENGINEERING.md](ENGINEERING.md).
