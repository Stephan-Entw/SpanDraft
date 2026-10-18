# Probleme erkennen und beheben

Wenn SpanDraft nicht rechnet oder ein Ergebnis unerwartet aussieht, lassen sich die meisten Ursachen über Lagerung, Lasten, Geometrie, Einheiten und Modellgrenzen eingrenzen.

## Keine Ergebnisse

| Situation | Prüfen |
| --- | --- |
| **Berechnung nicht verfügbar · Lagerung fehlt** | Erforderliche Lager hinzufügen. |
| **Berechnung nicht verfügbar · Lagerung nicht ausreichend** | Sind alle Starrkörperbewegungen verhindert? |
| **Berechnung nicht verfügbar · Modell unvollständig** | Beanstandete Eingaben prüfen. |
| **Berechnung nicht möglich** | Geometrie und ungewöhnliche Größenverhältnisse prüfen. Numerisch problematische Modelle können zu einem Berechnungsabbruch führen. |
| Änderung noch nicht übernommen | Eingabe bestätigen. |

Häufiger Fall: Zwei Loslager an verschiedenen Positionen verhindern zwar die vertikale Starrkörperbewegung, lassen aber die gesamte axiale Verschiebung frei. Eine typische einfache Lagerung verwendet deshalb Festlager + Loslager. Siehe [Lager und Randbedingungen](../modell/lager.md).

## Eingabe wird nicht übernommen

SpanDraft übernimmt nur Eingaben, aus denen ein gültiges Modell entstehen kann. Die konkrete Fehlermeldung zeigt normalerweise, welcher Wert korrigiert werden muss.

<details class="spandraft-details">
  <summary>Typische ungültige Eingaben</summary>
  <ul>
    <li>Position außerhalb <code>0 ≤ x ≤ L</code>.</li>
    <li>Streckenlast ohne gültigen Bereich <code>xStart &lt; xEnd</code>.</li>
    <li>Zwei Lager an derselben Position.</li>
    <li>Balkenverkürzung würde Lasten oder Lager außerhalb des Balkens lassen.</li>
    <li>Geometrisch ungültiger Querschnitt.</li>
    <li>Ungültige Werkstoffwerte: Die Kennwerte müssen innerhalb der von SpanDraft akzeptierten physikalischen Bereiche liegen.</li>
    <li>Doppelter Name.</li>
  </ul>
</details>

## Ergebnis wirkt unplausibel

Prüfen Sie das Modell in dieser Reihenfolge:

1. **Lasten:** Stimmen Betrag, Richtung, Position und belasteter Bereich?
2. **Lagerung:** Entspricht die idealisierte Lagerung der realen Situation?
3. **Balken und Querschnitt:** Stimmen Länge, Querschnittsabmessungen und Biegeachse?
4. **Werkstoff und Einheiten:** Sind Werkstoffwerte und verwendete Anzeigeeinheiten plausibel?
5. **Modellgrenzen:** Passt die Fragestellung zum [zweidimensionalen Balkenmodell von SpanDraft](modellannahmen.md)?

Bei Bedarf kann ein einfacher Referenzlastfall oder eine unabhängige Gleichgewichtsprüfung helfen, das Modell zusätzlich zu plausibilisieren. Ein Beispiel zeigt die [Erste Berechnung](../erste-schritte/erste-berechnung.md).

Die Bedeutung der Ergebnisse erläutern [Ergebnisdiagramme und Lagerreaktionen](../ergebnisse/diagramme-reaktionen.md) sowie [Spannung und Sicherheitsfaktor](../ergebnisse/spannung-sicherheit.md).

`≈ 0` bedeutet, dass der ungerundete Wert sehr klein, aber nicht exakt null ist. Die Berechnung selbst verwendet weiterhin den ungerundeten Wert.

<details class="spandraft-details">
  <summary>Probleme mit Projekt- oder Bibliotheksdateien</summary>
  <ul>
    <li>Projekte enthalten ihre verwendeten Werkstoff- und Querschnittsdaten unabhängig von den Bibliotheken.</li>
    <li>Beschädigte Dateien werden nicht automatisch repariert.</li>
  </ul>
  <p>Verwenden Sie bei einer beschädigten Projektdatei nach Möglichkeit eine intakte Sicherung.</p>
</details>

## Wiederherstellung nach einem Absturz

Nach einem unerwarteten Abbruch kann SpanDraft einen lokalen, ungespeicherten Bearbeitungsstand zur Wiederherstellung anbieten. Die ursprüngliche Projektdatei wird dabei nicht automatisch überschrieben. Prüfen Sie den wiederhergestellten Stand und speichern Sie ihn anschließend bewusst.
