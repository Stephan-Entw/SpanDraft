# SpanDraft – Hintergründe zum Editorzustand

Dieses Dokument erläutert Entscheidungen, deren Zweck beim Lesen einzelner
UI-Komponenten schwer erkennbar ist. Es wird nur ergänzt, wenn Code und Tests
zum Verständnis dieser Absicht nicht ausreichen. Änderungen an Controls,
Menüs, Dialogen, Texten oder Layout benötigen keine entsprechende Doku-Änderung.
Produktidee und Roadmap stehen in [KONZEPT.md](KONZEPT.md).

## Projektzustand, Eingabe und Berechnung

Der Editor trennt bestätigte Projektdaten, offene Eingaben und berechnete
Ergebnisse. Diese Trennung ermöglicht, zusammengehörige Änderungen gemeinsam
zu bestätigen oder zu verwerfen. Auch unvollständiger Zahlentext darf bestehen,
ohne vorzeitig ein fachliches Objekt erzeugen zu müssen.

Positionsbuffer und sichtbare Vorschauposition sind deshalb getrennt: Das
Eintippen einer Koordinate soll das bearbeitete Objekt und seinen Eingabeanker
nicht bei jeder Ziffer verschieben. Eine Drag-Vorschau darf sich dagegen bewegen,
bleibt aber bis zur Bestätigung Teil derselben ungespeicherten Bearbeitung.

Eine Projektänderung kann ausschließlich Metadaten betreffen. Namen und
Beschriftungspositionen gehören zum speicherbaren Projekt und zu Undo/Redo,
beeinflussen jedoch nicht die Mechanik. Die gemeinsame Änderungsgrenze entscheidet
über eine Neuberechnung, damit einzelne Controls diese Unterscheidung nicht
unterschiedlich treffen. Ein Ergebnis beschreibt immer ein bestätigtes Modell.

## Physikalische Koordinaten und Darstellung

Die schematische Entzerrung verbessert die Lesbarkeit dichter Modelle. Sie führt
keine neuen physikalischen Positionen ein. Deshalb verwenden Zeichnung, Achse
und Pointer-Auswertung dieselbe reversible Abbildung: Andernfalls könnte eine
sichtbare Position beim Anklicken oder Ziehen eine andere Modellkoordinate ergeben.

Während einer Pointer-Geste bleibt die Abbildung eingefroren. Würde die bewegte
Vorschau selbst ein neues Layout auslösen, verschöbe sich unter dem Pointer das
Koordinatensystem, aus dem ihre nächste Position ermittelt wird.

Gemeinsame Lastsymbole verdichten nur die Darstellung. Die beteiligten Lasten
bleiben einzeln bearbeitbar und werden nicht durch eine Resultierende ersetzt.
Ein mehrdeutiger Treffer darf deshalb keine beliebige Last auswählen.

Manuelle Beschriftungspositionen sind Offsets zum automatisch berechneten Anker.
Absolute Bildschirmkoordinaten würden die Beschriftung bei Resize oder Reflow
vom zugehörigen Objekt lösen. Der Offset verändert ausschließlich die Darstellung.

## Savepoint und Recovery

Der Savepoint bezeichnet eine gespeicherte Revision. Undo kann genau zu ihr
zurückkehren; ein neuer Bearbeitungszweig mit zufällig gleichem Inhalt bleibt
dagegen ungespeichert. Dafür verfolgt die Session Revisionsidentität statt nur
Inhaltsgleichheit. Speichern setzt den Savepoint, ohne die History zu löschen.

Recovery sichert bestätigte, ungespeicherte Arbeit separat. Sie überschreibt
keine Benutzerdatei, weil sonst eine Wiederherstellung nach einem Absturz zugleich
eine ungewollte Speicherung wäre. Der Dateivertrag steht in
[PROJECT_FORMAT.md](PROJECT_FORMAT.md).
