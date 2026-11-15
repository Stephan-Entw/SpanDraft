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

## Federlager-Draft

Ein neu gesetztes Federlager startet mit einer Wegfeder ohne Wert: Der Anwender muss
die Steifigkeit selbst eingeben, weil jeder Vorgabewert eine falsche Lagernachgiebigkeit
unbemerkt in die Berechnung tragen würde. Die Kombinationsregeln (mindestens eine Feder,
„starr in w“ nur mit Drehfeder und ohne Wegfeder) sind im Draft erzwungen, damit nie ein
nicht bestehendes Core-Objekt entsteht. Das Werkzeug der Federlager ist in der Werkzeugleiste
bewusst ohne Beschriftung: Bei der schmalsten Fensterbreite wäre sonst die deutsche Leiste
überlaufen.

## Gelenk-Draft und Werkzeugleiste

Ein Gelenk hat nur Name und Position; sein Draft ist deshalb ein eigener, schmaler Draft und kein
Typ des Lager-Drafts. Die Positionsregeln gegenüber Einspannung, Drehfeder, Punktmoment und
Balkenenden prüft der Editor schon bei der Eingabe mit denselben exakten Vergleichen wie der
Validator: Das Projektformat lehnt beim Laden jede Core-Verletzung ab, ein im Editor festgeschriebener
Verstoß ließe sich also später nicht mehr öffnen. Der Ring liegt auf der Balkenachse und damit oft auf
einem Lagersymbol; ein Treffer auf den Ring selbst gehört deshalb dem Gelenk allein, sonst wäre ein Gelenk
über einem Lager nie ohne Auswahlmenü greifbar. Ein Gelenk rastet nicht an den Balkenenden ein, die es
nicht tragen dürfen.

Die deutsche Werkzeugleiste hat bei der Mindestbreite des Fensters kaum Reserve. Das beschriftungslose
Gelenkwerkzeug passte erst, nachdem Innenabstand der Werkzeugschaltflächen und Leistenrand knapper wurden;
die Mindestbreite blieb bewusst unverändert, weil sie jedes Layout des Editors betrifft. Ein Test hält fest,
dass die Leiste in beiden Sprachen bei der Mindestbreite nichts abschneidet, nicht nur, dass ihre zugeteilte
Fläche in der Zeile liegt.

## Abschnitte in der Projekteinrichtung

Der Balken wird als Kette von Karten eingerichtet, eine je Abschnitt mit Länge, Querschnitt und
Werkstoff, beim Anlegen wie beim Bearbeiten. Die Gesamtlänge ist die Summe und nur eine Anzeige. Es
gibt kein Teilen und keine Grenzeingabe: Beide ließen offen, welche Länge sich dabei ändert und
was mit den Lasten dahinter geschieht, während eine Karte mit eigener Länge das Ergebnis jeder
Eingabe eindeutig macht. Ein neuer oder eingefügter Abschnitt ist leer und sperrt die Übernahme, bis
er vollständig ist; ein Duplikat ist vorausgefüllt, weil es bewusst eine Kopie ist, einschließlich
eines benutzerdefinierten Querschnittsnamens. Eingefügt wird vor oder nach einer Karte, weil es kein
Umsortieren gibt: sonst wäre eine Position vor dem ersten Abschnitt nicht erreichbar. Das Menü der
Karte wird beim Öffnen gebaut, weil ein Flyout nicht im visuellen Baum der Karte steht und seine
Einträge den Datenkontext nicht erben. Der Begriff bleibt „Abschnitt“, wie in Übersicht und Bericht.

Jede Änderung wird sofort gegen die Elemente des Projekts geprüft. Eine Ablehnung nennt die betroffenen
Elemente und ändert nichts; der Nutzer löst sie im Editor selbst auf, denn jede stille Korrektur
wäre eine Annahme über seine Absicht. Wo nur Positionen vom Balkenanfang betroffen sind, bleibt die
bekannte Meldung über Lager und Lasten außerhalb des Balkens. Die Längenänderung im Editor gibt es nur
bei einem Abschnitt, wo sie eindeutig ist; mit mehreren ist die Länge dort eine Anzeige mit Hinweis, weil
unklar wäre, welcher Abschnitt sich ändert.

In den Flyouts erscheint die Wahl des Bezugs nur bei mehreren Abschnitten, weil sie bei einem nichts
unterscheidet; die Bindung an ein Abschnittsende bleibt dagegen auch bei einem Abschnitt sichtbar und
nennt bei mehreren den Abschnitt („Am Ende von Abschnitt 2 halten“), denn auf einer Grenze ist die
Position zugleich das Ende des einen und der Beginn des nächsten. Das Positionsfeld zeigt den Abstand
vom gewählten Bezug, die globale Position steht daneben. Ein Bezug, der nichts an dem zeigt, was der
Nutzer sieht, wirkte wirkungslos und war in einer ersten Fassung genau das: Das Feld blieb die globale
Koordinate, und die Wahl änderte nur, wie gespeichert wird. Ein Wechsel des Bezugs lässt den Punkt
deshalb, wo er ist, und rechnet den Abstand um. Angeboten werden nur Bezüge, die den Punkt beschreiben
können: der Balkenanfang und die Abschnitte, die ihn enthalten (auf einer Grenze beide Nachbarn); ein
Abschnitt ohne den Punkt wäre ein Bezug, der ihn verschieben müsste. Ein getippter Abstand gilt vom gewählten Bezug und ändert ihn nie,
ein Abstand über den Abschnitt hinaus ist ein Fehler. Das Feld ist jederzeit editierbar; Bezug und
Bindung sind getrennte, ausdrückliche Eingaben. Die
Auswahlliste ersetzt ihre Einträge nur, wenn sich deren Inhalt ändert, weil ein Kombinationsfeld beim
Ersetzen der Liste seine Auswahl verwirft und neu anbietet, was als Eingabe des Nutzers missverstanden
würde und die Bindung zurücknähme. Grenzen lassen sich nicht im Balkenbild ziehen; die Abbildung liefert
nur die dünnen Querstriche.

## Streckenlast-Draft

Eine Streckenlast hat zwei Randintensitäten, meistens aber ist sie konstant. Der
Draft hält deshalb beide Texte getrennt und lässt die Endintensität dem Anfangswert
folgen, solange sie nicht selbst bearbeitet wurde. Das erspart bei der häufigsten
Eingabe einen zweiten Wert, ohne eine Betriebsart "konstant/linear" einzuführen.
Ein Fehler des Anfangswerts wird dabei nur einmal gemeldet, nicht an beiden Feldern.

## Querschnitts-Drafts

Jede Querschnittsgrundform besitzt einen eigenen transienten Draft. Eingaben,
Preview- und Achszustand sowie der optionale Anzeigename sind form-lokal.
Benutzerdefinierte und aus einer Bibliothek übernommene Namen werden bei einem
Grundformwechsel nicht übertragen. Die Rückkehr zu einer Form stellt ihren
eigenen Draft wieder her. Erst Übernehmen erzeugt aus dem aktiven Draft einen
Projektsnapshot; die anderen Form-Drafts werden nicht im Projekt gespeichert.

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

## Zahlendarstellung in den Einstellungen

Die Zehnerpotenzen sind ein Kontrollkästchen und kein Umschalter: Der Dialog
übernimmt Änderungen erst mit OK, ein Umschalter signalisiert dagegen sofortige
Wirkung, und Abbrechen wirkte dann widersprüchlich. Die Vorschau steht rechts in
der Spalte der rechten Einheitenliste, damit Ursache und Wirkung nebeneinander
liegen, ohne den Dialog zu verlängern. Sie zeigt einen gerundeten Vorgabewert,
weil der Dialog die Stelle ist, an der `≈` erklärt wird; der Bericht erklärt es
nicht.

## Berichte als eingefrorene Ausgabe

Berichte frieren den bestätigten Projektzustand samt Darstellung beim Öffnen ein,
damit Vorschau, PDF und Windows-Druck auch nach Einstellungsänderungen
übereinstimmen. Offene Eingaben bleiben beim Editor und werden durch die
Ausgabe weder übernommen noch verworfen.

## Savepoint und Recovery

Der Savepoint bezeichnet eine gespeicherte Revision. Undo kann genau zu ihr
zurückkehren; ein neuer Bearbeitungszweig mit zufällig gleichem Inhalt bleibt
dagegen ungespeichert. Dafür verfolgt die Session Revisionsidentität statt nur
Inhaltsgleichheit. Speichern setzt den Savepoint, ohne die History zu löschen.

Recovery sichert bestätigte, ungespeicherte Arbeit separat. Sie überschreibt
keine Benutzerdatei, weil sonst eine Wiederherstellung nach einem Absturz zugleich
eine ungewollte Speicherung wäre. Der Dateivertrag steht in
[PROJECT_FORMAT.md](PROJECT_FORMAT.md).
