# Erste Berechnung: ein Balken mit mittiger Einzelkraft

An einem einfach gelagerten Balken mit mittiger Einzelkraft wird einmal der vollständige SpanDraft-Ablauf gezeigt.

## 1. Projekt definieren

Ein neues Balkenprojekt benötigt zunächst einen Berechnungstitel, seine Länge, einen Werkstoff und einen Querschnitt. „Erstellt von“, Projekt und Referenz sind freiwillige Angaben. Ein vorgeschlagener Ersteller kann geändert oder vollständig gelöscht werden. Für das Beispiel sind das:

- Berechnungstitel **Balken mit mittiger Einzelkraft**;
- Balkenlänge `L = 2 m`;
- Werkstoff **S235JR**;
- Rechteckquerschnitt **40 × 80 mm** mit Breite `b = 40 mm` und Höhe `h = 80 mm`;
- Biegeachse **y-y**.

Die Biegeachse gehört zum Modell: Sie legt fest, welche Querschnittskennwerte für Biegesteifigkeit und Biegespannung verwendet werden. Beim Rechteck ist `y-y` die zur Breite parallele Achse.

## 2. Lagerung festlegen

![Balken mit Festlager links und Loslager rechts, ohne Lasten und Reaktionspfeile](../assets/beispiel-lagerung.png)

Im nächsten Schritt wird festgelegt, wie der Balken gehalten wird. Im Beispiel liegt ein **Festlager am linken Ende** und ein **Loslager am rechten Ende**. Diese Kombination verhindert die starre Bewegung des Balkens, lässt aber die Drehung an beiden Lagern zu.

## 3. Belastung hinzufügen

![Dieselbe Lagerung mit einer nach unten gerichteten Einzelkraft F in der Balkenmitte bei L/2](../assets/beispiel-belastung.png)

Im Beispiel wirkt eine Einzelkraft von **1 kN** in der Balkenmitte nach unten. SpanDraft verwendet die positive `w`-Richtung nach oben. Wählen Sie daher **Punktkraft** und geben Sie **`F = −1 kN`** bei `x = L/2 = 1 m` ein.

## 4. Automatisch berechnen und Ergebnisse beurteilen

Sobald Lagerung und Belastung ein berechenbares Modell ergeben, aktualisiert SpanDraft die Ergebnisse nach bestätigten Änderungen automatisch.

![Durchbiegung des Beispielbalkens im SpanDraft-Ergebnisdiagramm](../assets/diagramm-durchbiegung.png)

![Querkraft des Beispielbalkens im SpanDraft-Ergebnisdiagramm](../assets/diagramm-querkraft.png)

![Biegemoment des Beispielbalkens im SpanDraft-Ergebnisdiagramm](../assets/diagramm-biegemoment.png)

Die Diagramme zeigen die Durchbiegung `w(x)`, die Querkraft `V(x)` und das Biegemoment `M(x)`. Außerdem stehen Lagerreaktionen, maximale Durchbiegung, maßgebendes Moment, maximale elastische Biegespannung und Sicherheitsfaktor zur Verfügung.

Am Beispiel lässt sich unmittelbar erkennen:

- Die Durchbiegung ist in der Mitte betragsmäßig am größten.
- Die Querkraft springt an der Einzelkraft.
- Das größte Biegemoment liegt in der Mitte.

Details erläutern [Ergebnisdiagramme und Lagerreaktionen](../ergebnisse/diagramme-reaktionen.md), [Spannung und Sicherheitsfaktor](../ergebnisse/spannung-sicherheit.md) sowie [Was SpanDraft berechnet – und was nicht](../hintergrund/modellannahmen.md).

## 5. Projekt speichern

Das fertige Modell kann als `.spandraft`-Projekt gespeichert und später wieder geöffnet werden. Die Eingaben werden gespeichert; die Ergebnisse werden beim Öffnen erneut berechnet.

## Beispiel zum Ausprobieren

Das Modell dieses Artikels gibt es als fertiges Beispiel. Es öffnet sich als neues, unverändertes Projekt ohne Dateinamen; Ihre Änderungen speichern Sie mit **Speichern unter**.

![Einfeldträger mit Festlager, Loslager und Einzelkraft in der Mitte](../assets/beispiel-belastung.png)

| Größe | Wert |
| --- | --- |
| Länge | `L = 2 m` |
| Werkstoff | S235JR, `E = 210 000 N/mm²`, Referenzwert `Re = 235 N/mm²` |
| Querschnitt | Rechteck 40 × 80 mm, Biegeachse y-y: `I = 1,707 · 10⁶ mm⁴`, `W = 42 667 mm³` |
| Lager | Festlager `A` bei `x = 0`, Loslager `B` bei `x = 2 m` |
| Last | Punktkraft `F1 = −1 kN` bei `x = 1 m` |

Erwartete Ergebnisse, nachrechenbar mit den Handformeln des Einfeldträgers:

- Lagerreaktionen `A = B = F / 2 = 500 N` nach oben;
- größtes Biegemoment `M = F · L / 4 = 500 N·m` in der Mitte;
- größte Durchbiegung `w = F · L³ / (48 · E · I) = 0,465 mm` nach unten, in der Mitte;
- Biegespannung `σ = M / W = 11,7 N/mm²`, Sicherheitsfaktor `Re / σ ≈ 20`.

[Beispieldatei herunterladen](../assets/examples/einfeldtraeger.spandraft)
