# SpanDraft – Engineering-Auswertung V1

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

V1 berechnet ausschließlich reine elastische Biegung:

```text
σ_b,max [Pa] = BendingMomentMagnitude [Nm] / W [m³]
S           = Re [Pa] / σ_b,max [Pa]
```

W stammt aus `Beam.Section.SectionModulus`, Re aus
`Beam.Material.YieldStrength`; beide sind im Domain Model positiv validiert.
Der Spannungsbetrag besitzt denselben kritischen Ort und dieselbe Seite wie
das ausgewählte Moment. S ist ein einfaches Verhältnis zur Streckgrenze,
kein normativer Sicherheitsnachweis und keine Freigabe einer Konstruktion.

Im unbelasteten beziehungsweise biegespannungsfreien Fall sind Momentbetrag und
Spannung exakt null. Bei `σ_b,max == 0` wird ausdrücklich
`double.PositiveInfinity` geliefert, ohne Division durch null. Die Auswertung
liefert niemals NaN. Verbraucher müssen den unendlichen Faktor berücksichtigen,
etwa bei späterer Darstellung oder Serialisierung.

Es wird keine kleine Spannung künstlich auf null gesetzt. Es gelten die Grenzen
der double-Arithmetik: Ein Überlauf der positiven Safety-Factor-Division ergibt
ebenfalls positive Unendlichkeit; ein Unterlauf der Spannungsdivision kann null
ergeben. Eine nicht endlich darstellbare Spannung wird von der bestehenden
`Pressure`-Validierung mit `ArgumentOutOfRangeException` abgewiesen, nicht als
unendliche Spannung veröffentlicht.

## Ein einziges W und fachliche Grenzen

Das Domain Model besitzt ein einziges skalares elastisches Widerstandsmoment W
für die betrachtete Biegeachse. Für die vorhandenen symmetrischen
Standardquerschnitte ist das aktuelle Modell eindeutig.

Ein `CustomSection` kann derzeit keine getrennten W-Werte für die beiden
Randfasern darstellen. Bei asymmetrischen benutzerdefinierten Querschnitten
darf SpanDraft deshalb keinen seitenspezifischen Spannungsnachweis behaupten.
Ein bewusst als maßgebend gewähltes W kann verwendet werden; SpanDraft kennt
jedoch dessen geometrische Zuordnung zu den Randfasern nicht. Aus dem
Momentvorzeichen wird keine Aussage über Zug oder Druck an der oberen oder
unteren Randfaser abgeleitet.

Ausdrücklich ausgeschlossen sind kombinierte Normalspannung `N/A ± M/W`,
von-Mises-Spannung, Schubspannung, Spannungsinteraktionen, Normbeiwerte,
zulässige Durchbiegung, Knicken/Beulen, Ermüdung sowie Kerb- und
Schweißnahtbewertungen. Die axiale Solverfähigkeit erweitert diesen V1-Scope
nicht. Diese Auswertung ist **kein normativer Festigkeits- oder Sicherheitsnachweis**.
