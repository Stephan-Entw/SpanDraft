# Koordinatensystem und Einheiten

## Balkenkoordinaten

![Balkenlängsachse und positive Bewegungsrichtungen](../assets/koordinaten.png)

| Größe | Positive Richtung |
| --- | --- |
| `x`, `u` | nach rechts |
| `w` | nach oben |
| `θ` | gegen den Uhrzeigersinn |

`x = 0` liegt links, `x = L` rechts. Positionen sind echte Modellkoordinaten und bleiben von einer optischen Entzerrung der Skizze unabhängig.

Die [Lastvorzeichen](lasten.md) und [Reaktionsvorzeichen](../ergebnisse/diagramme-reaktionen.md#lagerreaktionen-in-der-projektubersicht) stehen bei den jeweiligen Größen. Balkenkoordinaten `x/u/w/θ` sind nicht mit den lokalen Querschnittsachsen `y/z` beziehungsweise `u/v` zu verwechseln; siehe [Balken, Werkstoff und Querschnitt](werkstoff-querschnitt.md).

## Einheiten

SpanDraft bietet die Profile **Maschinenbau** (Standard), **Bauwesen** und **US** sowie benutzerdefinierte Kombinationen.

<div class="spandraft-unit-table" data-collapsible-units>
  <table>
    <thead><tr><th scope="col">Größe</th><th scope="col">Maschinenbau</th><th scope="col">Bauwesen</th><th scope="col">US</th></tr></thead>
    <tbody>
      <tr><th scope="row">Länge / Position</th><td>mm</td><td>m</td><td>ft</td></tr>
      <tr><th scope="row">Kraft</th><td>N</td><td>kN</td><td>kip</td></tr>
      <tr><th scope="row">Moment</th><td>N·m</td><td>kN·m</td><td>kip·ft</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Querschnittsmaße</th><td>mm</td><td>mm</td><td>in</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Verschiebung</th><td>mm</td><td>mm</td><td>in</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Fläche <code>A</code></th><td>mm²</td><td>cm²</td><td>in²</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Flächenträgheitsmoment <code>I</code></th><td>mm⁴</td><td>cm⁴</td><td>in⁴</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Widerstandsmoment <code>W</code></th><td>mm³</td><td>cm³</td><td>in³</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Streckenlast</th><td>N/m</td><td>kN/m</td><td>kip/ft</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Spannung</th><td>MPa</td><td>MPa</td><td>ksi</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">E-Modul</th><td>GPa</td><td>GPa</td><td>ksi</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Festigkeitskennwert</th><td>MPa</td><td>MPa</td><td>ksi</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Dichte</th><td>kg/m³</td><td>kg/m³</td><td>kg/m³</td></tr>
      <tr class="spandraft-unit-extra"><th scope="row">Rotation</th><td>rad</td><td>rad</td><td>rad</td></tr>
    </tbody>
  </table>
  <button type="button" class="spandraft-unit-toggle" aria-expanded="false" hidden>
    <span class="spandraft-unit-toggle-label">Weitere Einheiten anzeigen</span>
    <span class="spandraft-unit-toggle-icon" aria-hidden="true">⌄</span>
  </button>
</div>

Einheiten ändern nur die Darstellung: Intern rechnet SpanDraft mit SI-Werten; das mechanische Modell und die physikalischen Ergebnisse bleiben gleich. Eine gerade bearbeitete Zahl muss gegebenenfalls zuerst bestätigt oder verworfen werden.

### Zahlendarstellung

**Signifikante Stellen** begrenzt die angezeigte Genauigkeit auf 1 bis 15 Stellen
(Standard: 3). Zum Beispiel wird `12,356 mm` als `12,4 mm` angezeigt; beim
Bearbeiten ist weiterhin der vollständige Wert verfügbar. Nachgestellte Nullen
werden nicht ergänzt.

**Technische Schreibweise: Automatisch** zeigt gerundete Beträge unter `0,001`
oder ab `1 000 000` mit Zehnerpotenzen in Dreierschritten, beispielsweise
`123 · 10⁻⁹`. Mit **Aus** bleiben diese Werte in Dezimalschreibweise.
Zifferngruppen werden durch schmale geschützte Leerzeichen getrennt; das
Dezimalzeichen folgt dem regionalen Zahlenformat.

Diese Einstellungen gelten für technische Werte in App und Bericht. Die
Berechnung und gespeicherte Eingaben bleiben unverändert. Diagramm-Achsenticks
behalten ihre eigene Skalierung.
