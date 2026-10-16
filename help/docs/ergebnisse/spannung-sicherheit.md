# Spannung und Sicherheitsfaktor

SpanDraft zeigt eine **maximale elastische Biegespannung** und daraus einen **Sicherheitsfaktor**. Beide Zahlen beziehen sich auf eine klar begrenzte Fragestellung: Wie groß ist die berechnete reine Biegespannung im Vergleich zur hinterlegten Streckgrenze des gewählten Werkstoffs?

## Biegespannung

Bei einem Balken mit über die Länge konstantem Querschnitt hängt die größte elastische Biegespannung vom Betrag des Biegemoments `M` und vom elastischen Widerstandsmoment `W` ab.

Für einen Querschnitt mit auf beiden geometrischen Seiten gleichem Widerstandsmoment gilt:

```text
σb,max = |M|max / W
```

Bei einem **unsymmetrischen** Querschnitt können die elastischen Widerstandsmomente der beiden Randseiten unterschiedlich sein. SpanDraft berücksichtigt für den **maximalen Spannungsbetrag** die ungünstigere der beiden Seiten:

```text
Wmaßgebend = min(W+, W−)
σb,max = |M|max / Wmaßgebend
```

Die Kennwerte beziehen sich auf die **gewählte Biegeachse** und auf die idealisierte reine Biegung. `W+` und `W−` kennzeichnen geometrische Seiten des Profils, **nicht** automatisch Zug- und Druckseite. SpanDraft weist in dieser Version keinen getrennten vorzeichenbehafteten Randfaser-Spannungsverlauf aus.

## Sicherheitsfaktor gegenüber der Streckgrenze

Der angezeigte Sicherheitsfaktor ist:

```text
S = fy / σb,max
```

Dabei ist `fy` die im Projekt hinterlegte Streckgrenze, `σb,max` der **nichtnegative** maximale elastische Biegespannungsbetrag und `S` dimensionslos.

| Wert des ungerundeten `S` | Bedeutung für diese einfache Biegebewertung |
| --- | --- |
| `S > 1` | Die berechnete maximale reine Biegespannung liegt unter der hinterlegten Streckgrenze. |
| `S = 1` | Die berechnete maximale reine Biegespannung entspricht der Streckgrenze. |
| `0 < S < 1` | Die berechnete reine Biegespannung überschreitet die hinterlegte Streckgrenze. |
| `S = ∞` | Bei verschwindendem Biegespannungsbetrag wird ein unendlicher Faktor ausgegeben; extreme Zahlenverhältnisse können dies auch numerisch verursachen. |

`∞` bedeutet **nicht** „unendlich sicher“. Es erscheint insbesondere, wenn das Modell keine biegende Last enthält. Andere beanspruchende Effekte und Versagensarten werden dadurch nicht bewertet.

Die Anzeige kann runden: Im Standardmodus wird `S` unterhalb von 3 auf Hundertstel, zwischen 3 und 10 auf Zehntel, zwischen 10 und 100 auf ganze Zahlen und darüber mit zwei signifikanten Stellen ausgegeben. Eine sichtbare `1` muss daher **nicht** exakt `S = 1` bedeuten. Für eine Grenzentscheidung ist die gerundete Anzeige allein nicht ausreichend.

## Beispiel: Rechteckbalken aus S235JR

Für den [einfach gelagerten Beispielbalken](../erste-schritte/erste-berechnung.md) mit `b = 40 mm`, `h = 80 mm`, `Mmax = 500 N·m` und `fy = 235 MPa` gilt:

```text
W = b · h² / 6 = 42.666,67 mm³
Mmax = 500 N·m = 500.000 N·mm

σb,max = 500.000 / 42.666,67 ≈ 11,71875 N/mm²
       = 11,71875 MPa

S = 235 / 11,71875 ≈ 20,0533
```

Der Wert besagt: Unter den **gewählten Modellannahmen** ist die ermittelte reine elastische Biegespannung deutlich kleiner als die eingetragene Streckgrenze. Daraus folgt **keine automatische Freigabe des realen Balkens**.

## Was dieser Sicherheitsfaktor nicht umfasst

SpanDraft berücksichtigt in dieser Kennzahl **nicht**:

- Schubspannungen und von-Mises- beziehungsweise Vergleichsspannungen;
- kombinierte Normalspannungen aus Axialkraft und Biegung (`N/A ± M/W`);
- Knicken, Beulen, Kippen und andere Stabilitätsversagensarten;
- Kerben, Bohrungen, Schweißnähte, Kontakt- und Lagerpressungen;
- Ermüdung, Stoß-/Dynamikbeanspruchung, plastisches Tragverhalten;
- zulässige Durchbiegungen, normativ geforderte Teilsicherheitsbeiwerte oder konstruktive Randbedingungen.

Insbesondere ist **eine kleine Biegespannung kein Nachweis kleiner Durchbiegung**. Die Durchbiegung muss getrennt anhand des jeweiligen Anwendungsfalls bewertet werden. SpanDraft gibt keine allgemein gültige zulässige Durchbiegung vor.

## Wie Sie den Wert sinnvoll verwenden

1. Prüfen Sie die eingegebenen **Lasten und Lager**; ein falsches Modell kann plausible Zahlen liefern.
2. Kontrollieren Sie Werkstoffkennwerte und insbesondere die **Biegeachse** und `W+`/`W−`.
3. Sehen Sie auf die **Diagramme**, nicht nur auf den Sicherheitsfaktor.
4. Beurteilen Sie zusätzlich die für Ihr Bauteil maßgebenden Effekte, die SpanDraft nicht nachweist.

Welche Effekte fehlen, beschreibt [Berechnungsmodell und Grenzen](../hintergrund/modellannahmen.md). Die Interpretation der Diagramme steht auf [Ergebnisdiagramme und Lagerreaktionen](diagramme-reaktionen.md).
