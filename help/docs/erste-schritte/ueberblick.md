# SpanDraft kennenlernen

SpanDraft ist ein visuelles Balkenberechnungsprogramm für einzelne gerade Balken. Sie arbeiten hauptsächlich mit einer technischen Skizze: Lager und Lasten werden am Balken positioniert, ihre Werte können anschließend exakt eingegeben werden.

## Die wichtigsten Bereiche

Die Anwendung besteht im Wesentlichen aus folgenden Arbeitsbereichen:

| Bereich | Wozu er dient |
| --- | --- |
| Projektdefinition | Balkenlänge, Werkstoff, Querschnitt und Biegeachse festlegen oder ändern |
| Werkzeugleiste | Punktkraft, Punktmoment, Streckenlast, Einspannung, Festlager oder Loslager wählen |
| Balkenskizze | Lasten und Lager platzieren, auswählen, verschieben und bearbeiten |
| Ergebnisdiagramme | Durchbiegung, Querkraft und Biegemoment entlang der Balkenlänge ansehen |
| Projektübersicht | Balkendaten, Lager, Lasten, Lagerreaktionen und maßgebende Kennwerte nachlesen |

Die Balkenskizze ist **schematisch**. Damit dicht nebeneinanderliegende Symbole lesbar bleiben, darf die Zeichnung optisch entzerrt werden. Die physikalischen Positionen bleiben dabei exakt die eingegebenen Werte; beurteilen Sie Abstände und Maße anhand der Zahlen, nicht allein anhand der Pixel.

## Ein Projekt erstellen oder öffnen

- Über **Datei → Neues Projekt** gelangen Sie zur Projektdefinition. Legen Sie die Balkenlänge fest und wählen Sie Werkstoff und Querschnitt. Die Biegeachse wird beim Querschnitt festgelegt.
- Über **Datei → Öffnen…** öffnen Sie eine vorhandene lokale `.spandraft`-Datei.
- In einem geöffneten Projekt können Sie die Balkendaten über **Ändern** im Bereich **Balkenmodell** bearbeiten.
- Mit **Datei → Speichern** bzw. **Speichern unter…** sichern Sie Ihre Arbeit. Ein Sternchen im Fenstertitel kennzeichnet ungespeicherte Änderungen.

Das Programm speichert Eingaben, Lager, Lasten und bestimmte Darstellungsanpassungen. Die numerischen Ergebnisse werden beim Öffnen erneut berechnet und müssen deshalb nicht in der Projektdatei stehen.

## Ein Objekt bearbeiten

1. Wählen Sie ein Werkzeug aus der Werkzeugleiste, etwa **Punktkraft** oder **Festlager**.
2. Bewegen Sie den Mauszeiger auf den gewünschten Ort der Balkenskizze. Die Position wird als Vorschau angezeigt.
3. Platzieren Sie das Objekt mit einem Klick; bei einer Streckenlast legen Sie Start und Ende fest.
4. Tragen Sie die gewünschte exakte Position, den Lastwert beziehungsweise die Lagerart im eingeblendeten Bearbeitungsbereich ein und bestätigen Sie.

Bereits vorhandene Objekte können Sie auswählen und bearbeiten. Sie können Positionen auch durch Ziehen verändern. Ein verändertes Objekt ist erst nach der bestätigten Bearbeitung Teil des Projekts. Die Schaltfläche **Abbrechen** verwirft die betreffende Bearbeitung.

Über die Namen in der Projektübersicht können Lager und Lasten ebenfalls zur Bearbeitung aufgerufen werden. Beschriftungen lassen sich bei Bedarf für eine bessere Lesbarkeit verschieben; das verändert keine Lastposition und kein Rechenergebnis.

## Rückgängig, Einstellungen und Speicherung

- **Bearbeiten → Rückgängig / Wiederholen** nimmt bestätigte Projektänderungen zurück beziehungsweise stellt sie wieder her.
- Unter **Bearbeiten → Einstellungen…** legen Sie Einheiten und die Darstellung numerischer Ergebnisse fest. Das betrifft die Anzeige, nicht die Berechnung.
- Beim Verlassen eines ungespeicherten Projekts fragt SpanDraft, ob die Änderungen gespeichert werden sollen.
- Nach einem unerwarteten Abbruch kann SpanDraft eine lokale Wiederherstellung anbieten. Sie ersetzt nicht das normale Speichern Ihrer Projektdatei.

Die Grundlagen zu Einheiten und gerundeten Anzeigen stehen auf [Koordinaten, Vorzeichen und Einheiten](../modell/koordinaten-einheiten.md).

## Wann zeigt SpanDraft Ergebnisse?

Sobald der bestätigte Balken ausreichend gelagert und das Modell berechenbar ist, erscheinen die Berechnungsergebnisse. Ein neu angelegtes Projekt ohne Lager kann dagegen zunächst **„Berechnung nicht verfügbar · Lagerung fehlt“** anzeigen. Das ist normal: Ein frei beweglicher Balken besitzt noch keine ausreichenden Randbedingungen.

Lesen Sie anschließend [Ergebnisdiagramme und Lagerreaktionen](../ergebnisse/diagramme-reaktionen.md) oder starten Sie direkt das [vollständige Rechenbeispiel](erste-berechnung.md).
