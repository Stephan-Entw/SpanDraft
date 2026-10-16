# Berechnungsmodell und Grenzen

SpanDraft verwendet für die Balkenberechnung ein **linear-elastisches Euler-Bernoulli-Modell** in einer zweidimensionalen Biegeebene. Das ist für viele einfache technische Fragestellungen ein sinnvoller Ansatz. Wie bei jedem Modell gilt aber: Die Ergebnisse sind nur so aussagekräftig wie die zugehörigen Annahmen.

## Was die Berechnung abbildet

| Eigenschaft | Aktueller Ansatz |
| --- | --- |
| Tragwerk | Ein einzelner gerader Balken |
| Werkstoff | Über die Balkenlänge konstant, linear-elastisch |
| Querschnitt | Über die Balkenlänge konstant; eine ausgewählte Biegeachse |
| Verformungen | Kleine Verschiebungen und kleine Rotationen |
| Biegung | Euler-Bernoulli: Querschnitte bleiben eben und normal zur verformten Balkenachse |
| Schubverformung | Nicht berücksichtigt |
| Lager | Ideale Einspannung, ideales Festlager oder ideales Loslager, ohne Nachgiebigkeit |
| Äußere Lasten | Vertikale Punktkräfte, Punktmomente und konstante Streckenlasten |
| Berechnungsergebnisse | Verformung, Rotation, Schnittgrößen und Lagerreaktionen; anschließend reine elastische Biegespannung und einfacher Sicherheitsfaktor |

Der Rechenkern ermittelt die Verformung und die inneren Schnittgrößen als **stückweise analytische Verläufe** entlang des Balkens. Lager- und Lastpositionen definieren dabei relevante Abschnitte. Dadurch können auch Sprünge der Querkraft oder des Moments an konzentrierten Einwirkungen berücksichtigt werden. Die Ermittlung von Extremwerten ist nicht auf einzelne Diagrammpixel beschränkt.

## Wann ist das Euler-Bernoulli-Modell passend?

Der Ansatz ist insbesondere für **schlanke Balken** geeignet, bei denen Biegeverformungen dominieren und Schubverformungen gegenüber der Biegung klein bleiben. Für kurze, tiefe Balken oder andere Fälle mit ausgeprägter Schubverformung kann das Modell die tatsächliche Durchbiegung unterschätzen. Eine allgemeine, allein aus dem Verhältnis `L/h` ableitbare Garantie gibt es nicht; die Beurteilung hängt auch von Lagerung, Querschnitt und Lastfall ab.

Die Berechnung setzt kleine Verformungen voraus. Ändert sich durch eine große Verformung die Lastwirkung oder die Geometrie wesentlich, genügt der lineare Ansatz nicht.

## Was ausdrücklich nicht berechnet wird

### Andere Lasten und Verformungen

- **Eigengewicht** wird trotz optionaler Werkstoffdichte nicht automatisch angesetzt.
- Äußere **axiale Lasten** sind noch kein unterstützter Lasttyp. Der Rechenkern besitzt zwar axiale Freiheitsgrade (`u`), aus denen bei den derzeit verfügbaren äußeren Lasten normalerweise keine axiale Beanspruchung entsteht.
- Schräg wirkende Kräfte, freie 3D-Kraftvektoren, Torsion und gleichzeitige Biegung um zwei Achsen werden nicht modelliert.
- Veränderliche, beispielsweise dreieckförmige Streckenlasten sind nicht als eigener Lasttyp enthalten.
- Auflagerbewegungen, elastische Lagerfedern und innere Gelenkfreigaben sind nicht implementiert.

### Weitere physikalische Effekte

- Schubverformung nach Timoshenko, große Verformungen, geometrische Nichtlinearität, plastische Umverteilung und zeitabhängiges Werkstoffverhalten;
- Eigenfrequenzen, Schwingungen, Stoß, dynamische Lasten und Temperatureinflüsse;
- Knicken, Beulen, Kippen, Ermüdung, Kerbwirkung und Kontakt- beziehungsweise Verbindungsnachweise;
- Schub-, Vergleichs- oder kombinierte Normalspannungen als Festigkeitsauswertung.

Die Anzeige eines **Sicherheitsfaktors** ersetzt deshalb weder eine konstruktive Bewertung noch einen normbasierten Festigkeitsnachweis. Sie bewertet ausschließlich die reine elastische Biegung gegenüber der im Material gespeicherten Streckgrenze. Siehe [Spannung und Sicherheitsfaktor](../ergebnisse/spannung-sicherheit.md).

## Die Biegeachse bedeutet keine räumliche Ausrichtung

Der Querschnitt liefert geometrische Eigenschaften um `y-y`/`z-z` oder `u-u`/`v-v`. Die ausgewählte Achse bestimmt, welches Flächenträgheits- und Widerstandsmoment in der **einen** planaren Balkenrechnung verwendet wird.

SpanDraft fragt nicht nach einer räumlichen Einbaulage und zerlegt Kräfte nicht automatisch in Profilhauptachsen. Wer beispielsweise einen schräg belasteten Winkel oder gleichzeitig in zwei Richtungen belasteten Träger untersucht, kann das nicht mit einer einzigen Achsenauswahl als vollständige räumliche Analyse behandeln.

## Idealisierungen bewusst prüfen

Ein reales Lager ist selten vollkommen starr und reibungsfrei. Eine Punktkraft greift tatsächlich oft auf einer kleinen Fläche an. Ein nominell konstantes Profil kann Fertigungstoleranzen, Bohrungen oder lokale Verstärkungen besitzen. Außerdem sind Materialkennwerte von Sorte, Zustand, Temperatur und Produktausführung abhängig.

Gerade deshalb sollte die Eingabe eine begründete **Idealisierung des konkreten technischen Falls** sein. Die mechanische Gleichgewichts- und Verformungsrechnung kann mathematisch korrekt sein, obwohl ihre Annahmen für das reale Bauteil ungeeignet sind.

## Grenzen numerischer Ergebnisse

Bei extrem kurzen Abschnitten, stark unterschiedlichen Längenmaßstäben oder ungewöhnlichen Steifigkeitsverhältnissen kann die numerische Lösung zu schlecht konditioniert sein. SpanDraft bricht die Berechnung dann ab, statt scheinbar verlässliche Ergebnisse auszugeben. Sehr kleine numerische Restwerte können außerdem aus Gleitkommaarithmetik entstehen; die Standarddarstellung kennzeichnet nahezu null gerundete, aber nicht exakt null große Werte mit `≈ 0`.

Die unabhängige Validierung des Solvers prüft den implementierten Modellumfang. Sie hebt keine der genannten physikalischen Modellgrenzen auf.

Bei unklaren Resultaten helfen die Prüfschritte unter [Häufige Probleme](probleme.md).
