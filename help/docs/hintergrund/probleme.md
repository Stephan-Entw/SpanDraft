# Häufige Probleme und Hinweise

Die meisten Probleme lassen sich eingrenzen, indem Sie zuerst die angezeigte Meldung lesen und dann **Lagerung, Positionen und Eingabewerte** prüfen. SpanDraft führt Berechnungen nur für bestätigte, gültige Modelle durch.

## Keine Ergebnisse sichtbar

| Meldung oder Situation | Bedeutung | Was Sie prüfen können |
| --- | --- | --- |
| **Berechnung nicht verfügbar · Lagerung fehlt** | Es gibt noch keine Lager. | Zuerst die erforderlichen Lager hinzufügen. |
| **Berechnung nicht verfügbar · Lagerung nicht ausreichend** | Mindestens eine Starrkörperbewegung ist nicht verhindert. | Gibt es eine axiale Festhaltung? Sind Rotationen beziehungsweise die beiden vertikalen Starrkörperbewegungen ausreichend gesperrt? |
| **Berechnung nicht verfügbar · Modell unvollständig** | Mindestens eine Modellbedingung ist ungültig. | Positionen, Lastbereiche und andere beanstandete Eingaben kontrollieren. |
| **Berechnung nicht möglich** | Beispielsweise numerischer Fehler oder zu schlechte Konditionierung. | Sehr kurze Abschnitte, ungewöhnliche Maßstäbe und extreme Steifigkeitsverhältnisse prüfen. |
| Eine Zahl wurde geändert, das Ergebnis aber noch nicht | Die neue Eingabe ist möglicherweise noch ein offener Bearbeitungsentwurf. | Bearbeitung bestätigen. Erst dann wird neu gerechnet. |

### Typischer Fall: zwei Loslager

Zwei Loslager an verschiedenen Positionen verhindern zwar die vertikale Translation und die Starrkörperrotation, aber **nicht** die axiale Verschiebung des gesamten Balkens. SpanDraft verlangt deshalb auch dann ein Lager, das die axiale Verschiebung sperrt, wenn Sie keine äußere Horizontallast eingegeben haben. Tauschen Sie beispielsweise eines der Loslager gegen ein **Festlager**, sofern diese Idealisierung zum realen System passt.

Ein einzelnes Festlager verhindert die Rotation um dieses Lager nicht; es genügt deshalb ebenfalls nicht als alleinige Lagerung. Eine **Einspannung** kann dagegen einen Kragarm allein halten.

## Eingaben lassen sich nicht übernehmen

- **Position außerhalb des Balkens:** Lager und Punktlasten müssen `0 ≤ x ≤ L` erfüllen. Bei einer Streckenlast gilt zusätzlich `xStart < xEnd`.
- **Zwei Lager am gleichen Ort:** Die aktuelle Bearbeitung lässt dort nur ein Lager zu. Ein vorhandenes Lager stattdessen bearbeiten.
- **Balkenlänge zu kurz:** Beim Verkürzen dürfen bereits platzierte Lasten oder Lager nicht außerhalb des neuen Balkens landen. Diese Objekte zuerst versetzen oder entfernen.
- **Ungültiger Querschnitt:** Abmessungen müssen eine geometrisch gültige Form ergeben; beispielsweise müssen Wanddicke, freier Innenraum und Radien zueinander passen. `r = 0` ist bei geeigneten Formen zulässig.
- **Ungültiger Werkstoffwert:** `E` und `fy` müssen positiv sein; optional eingegebene Dichte muss ebenfalls positiv sein und `ν` muss zwischen `−1` und `0,5` liegen (Grenzen ausgeschlossen).
- **Name doppelt:** Objektnamen innerhalb eines Projekts beziehungsweise Eintragsnamen innerhalb derselben Benutzerbibliothek müssen eindeutig sein (ohne Unterscheidung der Groß-/Kleinschreibung).

Ungültige oder noch nicht bestätigte Texte werden nicht stillschweigend zu anderen mechanischen Werten umgedeutet. Korrigieren Sie sie im betreffenden Eingabefeld.

## Diagramme und Kennwerte wirken widersprüchlich

**Negative Durchbiegung, aber positive „Maximale Durchbiegung“?** Die Kurve zeigt `w` mit Vorzeichen (nach unten negativ). Die Kennzahl zeigt den **Betrag**.

**Biegemoment positiv, Kraft nach unten negativ?** Last- und Schnittgrößenvorzeichen sind verschieden definiert. Beim einfach gelagerten Balken mit mittiger Last ist das positive/sagende Moment richtig.

**Sprung in der Querkraft oder im Moment?** Eine konzentrierte Punktkraft beziehungsweise ein Punktmoment kann einen **echten Sprung** im jeweiligen Verlauf verursachen. Es ist kein Grafikfehler.

**Sicherheitsfaktor `∞`?** Das tritt insbesondere bei verschwindender Biegespannung auf und kann bei extremen Zahlenverhältnissen auch numerische Gründe haben. Es sagt nichts über andere Belastungsarten oder die allgemeine Bauteilsicherheit aus.

**Anzeige `≈ 0`?** Der Wert ist nicht exakt null, wird aber durch die gewählte Darstellungsauflösung zu null gerundet. Ändern Sie gegebenenfalls unter **Einstellungen → Numerische Ergebnisdarstellung** den Modus; die Rechnung selbst bleibt unverändert.

## Gespeichertes Projekt und Bibliothek

Die Datei `.spandraft` speichert Werkstoff- und Querschnittswerte **im Projekt selbst**. Wird ein Eintrag später aus der eigenen Bibliothek gelöscht oder dort geändert, bleiben bestehende Projekte unverändert.

Lässt sich eine **eigene Material- oder Querschnittsbibliothek** wegen einer beschädigten Datei nicht lesen, ersetzt SpanDraft deren Inhalt nicht automatisch durch eine leere Bibliothek und überschreibt die beschädigte Datei nicht beim nächsten Speichern. Die zugehörige Bibliotheksbearbeitung kann eingeschränkt sein. Einmalige Definitionen und bereits gespeicherte Projekte bleiben grundsätzlich möglich.

Bei beschädigten oder unbekannten Projektdateien werden Werte nicht stillschweigend repariert. Verwenden Sie eine intakte Sicherung oder prüfen Sie die Datei auf einem anderen Weg, statt manuell wahllos JSON-Felder zu ändern.

## Wiederherstellung nach einem Absturz

SpanDraft kann nach einem unerwarteten Abbruch einen lokalen, ungespeicherten Bearbeitungsstand zur Wiederherstellung anbieten. Diese Wiederherstellung ist **kein automatisches Überschreiben der ursprünglichen Projektdatei**. Prüfen Sie die wiederhergestellten Eingaben und speichern Sie das Projekt anschließend bewusst.

## Eine kurze fachliche Prüfroutine

Wenn das Programm rechnet, das Ergebnis aber unplausibel wirkt, überprüfen Sie nacheinander:

1. Stimmt die **Lastwirkung** (Vorzeichen, Betrag, Position, Lastbereich)?
2. Stimmen die **Lagerbedingungen** mit der realen Situation überein?
3. Stimmen **Länge, Querschnittsmaße, Biegeachse und Werkstoffwerte**?
4. Entsprechen die **Einheiten** Ihrer Erwartung?
5. Ist die Fragestellung innerhalb der [Modellgrenzen](modellannahmen.md)?

Anschließend können Sie den Lastfall über Gleichgewicht und ein einfaches Referenzbeispiel kontrollieren. Die [erste Berechnung](../erste-schritte/erste-berechnung.md) liefert dafür konkrete Vergleichswerte.
