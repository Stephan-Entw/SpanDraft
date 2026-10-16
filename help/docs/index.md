# SpanDraft – Benutzerhandbuch

Mit SpanDraft modellieren und berechnen Sie einen geraden Balken: Querschnitt und Werkstoff festlegen, Lager und Lasten in einer Skizze platzieren und die Verformung, Schnittgrößen und ausgewählten Festigkeitskennwerte betrachten.

Dieses Handbuch erklärt die Bedienung und die Bedeutung der Ergebnisse. Es richtet sich an Anwender, die einen Balken überschlägig oder detaillierter untersuchen möchten, ohne dafür ein allgemeines FEM-Modell aufzubauen. Grundbegriffe wie Kraft und Moment werden kurz erläutert; Vorkenntnisse in der Finite-Elemente-Methode sind nicht erforderlich.

## Womit möchten Sie beginnen?

| Ihr Ziel | Die passende Seite |
| --- | --- |
| Einen ersten Balken berechnen | [Erste Berechnung](erste-schritte/erste-berechnung.md) |
| Die Bedienoberfläche kennenlernen | [SpanDraft kennenlernen](erste-schritte/ueberblick.md) |
| Querschnitt, Werkstoff oder Biegeachse auswählen | [Balken, Werkstoff und Querschnitt](modell/werkstoff-querschnitt.md) |
| Lager richtig setzen | [Lager und Randbedingungen](modell/lager.md) |
| Kräfte, Momente oder Streckenlasten eingeben | [Kräfte, Momente und Streckenlasten](modell/lasten.md) |
| Die Vorzeichen und Einheiten verstehen | [Koordinatensystem und Einheiten](modell/koordinaten-einheiten.md) |
| Diagramme und Auflagerkräfte lesen | [Ergebnisdiagramme und Lagerreaktionen](ergebnisse/diagramme-reaktionen.md) |
| Spannungswert und Sicherheitsfaktor beurteilen | [Spannung und Sicherheitsfaktor](ergebnisse/spannung-sicherheit.md) |
| Wissen, was das Modell berücksichtigt | [Was SpanDraft berechnet – und was nicht](hintergrund/modellannahmen.md) |
| Einen Fehler oder eine fehlende Berechnung verstehen | [Probleme erkennen und beheben](hintergrund/probleme.md) |

## Das Wichtigste vorab

SpanDraft betrachtet einen einzelnen, geraden Balken mit über die Länge konstantem Querschnitt und Werkstoff. Die Berechnung ist linear-elastisch und berücksichtigt Biegung in **einer gewählten Ebene**. Die Eingaben sind idealisierte mechanische Größen, keine automatisch erkannten realen Einbaubedingungen.

Die Anwendung stellt unter anderem die Durchbiegung, Querkraft, das Biegemoment und Lagerreaktionen dar. Die zusätzliche Biegespannung und der Sicherheitsfaktor sind **keine vollständige Bauteilfreigabe**. Beispielsweise werden Knicken, Beulen, Schweißnähte und Ermüdung nicht nachgewiesen.

Die wichtigsten Grenzen stehen auf [Was SpanDraft berechnet – und was nicht](hintergrund/modellannahmen.md); für den Sicherheitsfaktor siehe [Spannung und Sicherheitsfaktor](ergebnisse/spannung-sicherheit.md).

## Typischer Arbeitsablauf

1. Ein neues Projekt anlegen und Balkenlänge, Werkstoff, Querschnitt und Biegeachse bestimmen.
2. Die tatsächlich vorgesehenen Lagerbedingungen abbilden.
3. Punktkräfte, Punktmomente und/oder konstante Streckenlasten platzieren.
4. Ergebnisse und Fehlermeldungen prüfen; bei Bedarf die Eingaben korrigieren.
5. Die Projektdatei im Format `.spandraft` speichern.

Ergebnisse werden nach bestätigten Änderungen neu berechnet; ein noch offenes Eingabefeld oder eine Vorschau verändert das berechnete Projekt nicht.

> Dieses Handbuch beschreibt den Funktionsumfang der ersten SpanDraft-Hilfeversion. Die Formulierungen der Menüs beziehen sich auf die deutsche Benutzeroberfläche; bei englischer Systemsprache können Bezeichnungen abweichen.
