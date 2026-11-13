# SpanDraft – Engineering-Auswertung

## Zweck und Schnittstelle

`SpanDraft.Engineering` ist fachliches Postprocessing einer bereits berechneten
Euler-Bernoulli-Lösung. Die Schicht referenziert Core und Solver, benötigt keine
UI, kein Reporting und keine zusätzlichen externen Pakete. Die separate
Validation-Solution enthält Engineering nicht; Engineering wird in den normalen
Produkttests geprüft.

```csharp
BeamEngineeringResult result = BeamEngineeringAnalysis.Analyze(solution);
```

Die einzige Eingabe ist `BeamSolution`; Material und Querschnitt stammen aus
`solution.Beam`. Ein inkonsistentes Modell/Lösungs-Paar ist damit ausgeschlossen.
`null` wird mit `ArgumentNullException` abgewiesen. Das Ergebnis ist immutable:

- `CriticalTransverseDisplacement`: originales `BeamExtremum<Displacement>`
  mit signed `Value`, `Position` und nullable `Side`.
- `TransverseDisplacementMagnitude`: Betrag als `Displacement`.
- `CriticalBendingMoment`: originales `BeamExtremum<Moment>` mit signed `Value`,
  `Position` und nullable `Side`.
- `BendingMomentMagnitude`: Betrag als `Moment`.
- `MaximumBendingStress`: nicht-negativer Betrag als `Pressure`.
- `BendingStressPosition` und `BendingStressSide`: Ort und Seite des kritischen Moments.
- `SafetyFactor`: dimensionsloser `double`.

`EvaluationSide.Left/Right` bezeichnet den einseitigen Grenzwert entlang der
Balkenachse, keine obere oder untere Randfaser. `Side == null` kennzeichnet ein
inneres analytisches Extremum und bleibt unverändert erhalten.

## Betragsmaximum, Vorzeichen und Auswahl

Für Durchbiegung und Moment wird jeweils zwischen dem signed Minimum und dem
signed Maximum aus `BeamSolution.Extrema` anhand des Betrags gewählt:

```text
|w|max = max(|wmin|, |wmax|)
|M|max = max(|Mmin|, |Mmax|)
```

Es werden ausschließlich `MinimumTransverseDisplacement`,
`MaximumTransverseDisplacement`, `MinimumBendingMoment` und `MaximumBendingMoment`
verwendet. Engineering führt keine zweite Lösung, keine zusätzlichen
Punktabfragen, kein Diagramm-Sampling und keine eigene Extremwertsuche aus.

Die Auswahl verwendet dieselbe Größenordnung des reinen relativen
Rundungsvergleichs wie der Solver. Für Beträge a und b mit `scale = max(a, b)`
besteht ein Gleichstand bei `scale == 0` oder
`abs(a/scale - b/scale) <= 64 * 2.2204460492503131e-16`.
Dies ist keine fachliche Toleranz; es gibt keine absolute SI-Schwelle und kein
Positionsepsilon. Bei Gleichstand gewinnt die kleinste Position, danach Left
vor Right. Bei identischen Schlüsseln bleibt der Minimum-Kandidat gewählt.
Bei einem Rundungsgleichstand kann dadurch der numerisch geringfügig kleinere
Betrag maßgebend werden.

Wert, Position und Seite werden exakt vom ausgewählten Solverextremum übernommen.
Der signed Wert bleibt verfügbar: w ist nach oben positiv; M ist positiv/sagend
beziehungsweise negativ/hoggend. Die Magnitude wird separat nicht-negativ geliefert.

## Formeln und Nulllastfall

Die Engineering-Auswertung berechnet ausschließlich reine elastische Biegung:

```text
σ_b,max [Pa] = BendingMomentMagnitude [Nm] / min(W+, W−) [m³]
S           = Festigkeitskennwert [Pa] / σ_b,max [Pa]
```

W+ und W− stammen aus `Beam.BendingAxisProperties.PositiveSectionModulus`
beziehungsweise `NegativeSectionModulus`, der Festigkeitskennwert aus
`Beam.Material.YieldStrength` (Typ Re oder Rp0,2);
alle sind im Domain Model positiv validiert. Der maximale elastische
Spannungsbetrag ist das Maximum aus |M|/W+ und |M|/W−.
Der Spannungsbetrag besitzt denselben kritischen Ort und dieselbe Seite wie
das ausgewählte Moment. S ist ein einfaches Verhältnis zum gewählten Festigkeitskennwert,
kein normativer Sicherheitsnachweis und keine Freigabe einer Konstruktion.

Im unbelasteten beziehungsweise biegespannungsfreien Fall sind Momentbetrag und
Spannung exakt null. Bei `σ_b,max == 0` wird ausdrücklich
`double.PositiveInfinity` geliefert, ohne Division durch null. Die Auswertung
liefert niemals NaN. Verbraucher müssen den unendlichen Faktor bei Darstellung
oder Serialisierung berücksichtigen.

Es wird keine kleine Spannung künstlich auf null gesetzt. Es gelten die Grenzen
der double-Arithmetik: Ein Überlauf der positiven Safety-Factor-Division ergibt
ebenfalls positive Unendlichkeit; ein Unterlauf der Spannungsdivision kann null
ergeben. Eine nicht endlich darstellbare Spannung wird von der bestehenden
`Pressure`-Validierung mit `ArgumentOutOfRangeException` abgewiesen, nicht als
unendliche Spannung veröffentlicht.

## Randfasern und fachliche Grenzen

Die ausgewählte Biegeachse stellt getrennte positive und negative geometrische
Widerstandsmomente bereit. Da der Querschnitt entlang des Balkens konstant ist,
bleibt der Ort des maximalen |M| auch der maßgebende Ort für den maximalen
Spannungsbetrag. Dieser Betrag ist unabhängig davon, welche Seite bei einem
gegebenen Momentenvorzeichen unter Zug beziehungsweise Druck steht.
Es werden keine Zug-/Druck-Randfaser und keine signed fiber stresses ausgewiesen.

Alte `Section`-Typen einschließlich `CustomSection` sowie manuelle Definitionen
mit einem W pro Achse liefern W+ = W− = W. Ihre Spannungs- und
Safety-Factor-Auswertung entspricht deshalb unverändert dem bisherigen Verhalten.

Ausdrücklich ausgeschlossen sind kombinierte Normalspannung `N/A ± M/W`,
von-Mises-Spannung, Schubspannung, Spannungsinteraktionen, Normbeiwerte,
zulässige Durchbiegung, Knicken/Beulen, Ermüdung sowie Kerb- und
Schweißnahtbewertungen. Die axiale Solverfähigkeit erweitert diesen Umfang
nicht. Diese Auswertung ist **kein normativer Festigkeits- oder Sicherheitsnachweis**.

## Numerische Darstellung in App und Bericht

Die Darstellung liegt außerhalb der fachlichen Auswertung. Modell, Solver,
Engineering und Projektdateien behalten ihre ungerundeten SI-Werte. Jeder
angezeigte technische Einzelwert gehört zu genau einer von zwei Zahlenklassen;
die Zuordnung liegt an einer Stelle, den beiden Formatierungswegen des
`QuantityFormatter` (`FormatInput…` / `FormatResult…`). Beide runden nach
derselben Regel; sie unterscheiden sich nur in der Kennzeichnung.

**Vorgabewerte** sind direkt vorgegebene oder aus Bibliotheken übernommene
Werte: Längen, Positionen, Lasten, Querschnittsabmessungen sowie gespeicherte
Material- und Profilkennwerte. **Berechnete Werte** sind alle aus anderen
Werten ermittelten Größen: Reaktionen, Schnittgrößen, Spannungen,
Verformungen, Sicherheitsfaktoren, aus Geometrie abgeleitete
Querschnittswerte, das Eigengewicht und berechnete Positionen wie die Lage
eines Maximums.

Die Reihenfolge ist **SI-Wert → Anzeigeeinheit (exakte Umrechnung) →
einmalige Rundung → Schreibweise**. Für jeden angezeigten Wert gilt einzeln:

- **R1** `n` signifikante Stellen für alle Werte. Eine lokale,
  projektunabhängige Einstellung, 1–5, Standard 3.
- **R2** In Dezimalschreibweise nie gröber als eine ganze Einheit: Die
  Rundestelle ist `10^min(0, e − n + 1)` mit dem Dezimalexponenten `e` des
  Werts in der Anzeigeeinheit. Ziffern vor dem Komma gehen nie verloren
  (`1234,5 N` → `1 235 N`).
- **R3** Halbwerte werden von null weg gerundet.
- **R4** Keine Endnullen, weder dezimal noch in der Mantisse
  (`12,0 mm` → `12 mm`, `999 999,6 N` → `1 · 10⁶ N`).
- **R5** Mit der Einstellung „Zehnerpotenzen bei sehr kleinen und großen
  Werten“ (Default: ein) gilt: Ist der auf `n` Stellen gerundete Betrag kleiner
  als `10⁻³` oder mindestens `10⁶`, wird Engineering-Notation mit Exponenten als
  Vielfachen von drei und einer Mantisse mit `n` signifikanten Stellen
  verwendet; R2 gilt dort nicht. Ausgeschaltet gilt unabhängig von der
  Größenordnung Dezimalschreibweise.
- **R6** Vorgabewerte: Weicht die Anzeige vom exakten Wert in der
  Anzeigeeinheit ab, wird `≈` vorangestellt (`800,000004 mm` → `≈ 800 mm`). In
  Labels ersetzt es das Gleichheitszeichen (`F1 ≈ 800 mm`).
- **R7** Exakte Werte und exakte Umrechnungen erhalten kein `≈`
  (`25,4 mm` → `1 in`).
- **R8** Berechnete Werte erhalten nie `≈`.
- **R9** Der Sicherheitsfaktor wird auf `n` signifikante Stellen abgerundet,
  nie aufgerundet (`0,9995` → `0,999`, `1,0049` → `1`).
- **R10** Exakt null (auch −0) erscheint als `0`, der semantisch unendliche
  Sicherheitsfaktor als `∞`. Ein endlicher Nichtnullwert bleibt sichtbar; es
  gibt keine modellabhängigen Anzeigeauflösungen oder automatische Nullschwelle.

Der Bereich 1–5 begrenzt die Anzeige auf technisch sinnvolle Stellen; er ist
keine Aussage über Messgenauigkeit. Gespeicherte Einstellungen mit mehr Stellen
werden beim Laden auf 5 gesetzt. Der Bericht nennt die eingestellte Stellenzahl
im Absatz zum Berechnungsmodell; `≈` wird dort nicht erklärt.

Querschnittswerte (Fläche, Flächen- und Widerstandsmomente) folgen ihrer
Herkunft: Bei manuell definierten Profilen sind sie gespeicherte Vorgabewerte,
bei geometrisch definierten Formen berechnete Werte.

Ganzzahlige Ziffern werden in Dreiergruppen mit schmalem geschütztem Leerzeichen
(U+202F) gegliedert; nur das Dezimalzeichen folgt dem regionalen Zahlenformat,
unabhängig von der Oberflächensprache. Engineering-Ausdrücke verwenden geschützte
Abstände um `·`. Die PDF-Ausgabe bewahrt diese Zahlengruppen beim Umbruch.
E-Modul, Festigkeitskennwert und Dichte besitzen eigene semantische Einheiten;
metrische Defaults sind GPa, MPa und kg/m³, im US-Profil für beide Druckgrößen ksi.

Bearbeiten verwendet stets den vollständigen gespeicherten Wert in der gewählten
Einheit, immer in Dezimalschreibweise ohne Exponent und ohne Zifferngruppen
(`0,0000042`); das Parsen dieses Texts ergibt exakt den gespeicherten SI-Wert,
auch an den Grenzen von `double`. Unveränderte Eingabebuffer behalten den
originalen SI-Wert auch bei Fokuswechsel, Rebinding und Bestätigung. Eingaben verwenden das regionale
Dezimalzeichen und erlauben E/e-Exponenten sowie optional die ausgegebenen
Dreiergruppen mit U+202F im Ganzzahlteil. Andere Gruppentrennzeichen,
fehlerhafte Gruppen und Gruppierungen in Nachkommateil oder Exponent werden
abgelehnt; Einheitensymbole und die ausgegebene `· 10`-Schreibweise sind kein
Zahleneingabeformat.

Darstellungswechsel verändern weder Berechnung, Projektdatei, Undo/Redo noch
Dirty-State. Historische Standard-/Detailed-Einstellungen werden unter
Beibehaltung ihrer Einheiten auf die oben beschriebenen Standardwerte migriert;
das Projektformat bleibt unverändert.

Diagramm-Achsenticks behalten ihre eigenständige Nice-Tick-Logik. Zoom, Seitenzahlen
und reine UI-Zähler unterliegen ebenfalls nicht der Einstellung der signifikanten Stellen.
