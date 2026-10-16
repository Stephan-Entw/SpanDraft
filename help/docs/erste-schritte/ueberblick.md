# SpanDraft kennenlernen

SpanDraft ist ein visuelles Berechnungsprogramm für einzelne gerade Balken. Ein Modell entsteht schrittweise aus Balkendefinition, Lagerung und Belastung. Sobald daraus ein berechenbares System entsteht, aktualisiert SpanDraft die Ergebnisse automatisch.

<ol class="spandraft-workflow">
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Projekt definieren</span>
      <span class="spandraft-workflow-description">Länge, Werkstoff und Querschnitt festlegen.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Lagerung festlegen</span>
      <span class="spandraft-workflow-description">Festlegen, wie der Balken gehalten wird.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Lasten hinzufügen</span>
      <span class="spandraft-workflow-description">Punktkräfte, Punktmomente und Streckenlasten anordnen.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Automatisch berechnen</span>
      <span class="spandraft-workflow-description">SpanDraft aktualisiert das Modell nach bestätigten Änderungen automatisch, sobald ein lösbares System vorliegt.</span>
    </div>
  </li>
  <li class="spandraft-workflow-step">
    <div class="spandraft-workflow-content">
      <span class="spandraft-workflow-title">Ergebnisse beurteilen</span>
      <span class="spandraft-workflow-description">Verformung, Schnittgrößen, Lagerreaktionen und abgeleitete Kennwerte betrachten.</span>
    </div>
  </li>
</ol>

Das Modell kann jederzeit als `.spandraft`-Projekt gespeichert und später weiterbearbeitet werden.

## Was an SpanDraft wichtig ist

Die Balkenskizze ist eine technische, schematische Darstellung und kann für bessere Lesbarkeit optisch entzerrt sein. Lager und Lasten werden grafisch angeordnet; ihre physikalischen Positionen und Werte bleiben jedoch exakt die eingegebenen Zahlen. Maßgeblich sind deshalb die numerischen Angaben. Die Ergebnisse beziehen sich auf das bestätigte Modell und aktualisieren sich nach bestätigten Änderungen automatisch.

**Rückgängig und Wiederholen** betreffen im Balkeneditor bestätigte Projektänderungen, im Querschnittseditor bestätigte Änderungen des aktuellen Querschnittsentwurfs. Noch nicht bestätigte Texteingaben verwenden das normale Textfeld-Undo.

Weiter: [Erste Berechnung](erste-berechnung.md)
