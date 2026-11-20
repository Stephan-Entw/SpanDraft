# Versionshinweise

## 1.2.0

SpanDraft 1.2.0 erweitert das Balkenmodell um mehrere Balkenabschnitte, Federlager, innere Gelenke und linear veränderliche Streckenlasten. Außerdem bietet die neue Version mehr Kontrolle über die Zahlendarstellung sowie mehrere Verbesserungen an Berechnungsbericht und Bearbeitung.

### Neu

- **Balken abschnittsweise definieren.** Ein Balken kann jetzt aus mehreren Abschnitten mit jeweils eigenem Werkstoff, Querschnitt und eigener Biegeachse bestehen. Ist das Eigengewicht aktiviert, berücksichtigt SpanDraft jeden Abschnitt einzeln und kennzeichnet in Projektübersicht und Berechnungsbericht den Abschnitt mit dem niedrigsten Sicherheitsfaktor.
- **Lager, Lasten und Gelenke folgen der Balkengeometrie.** Ihre Position kann relativ zum Anfang eines Abschnitts angegeben oder direkt an dessen Ende gebunden werden. Ändert sich die Balkengeometrie, folgt die Position automatisch ihrem Bezug.
- **Federlager mit Weg- und Drehfedern hinzufügen.** Weg- und Drehfedern können unabhängig voneinander oder gemeinsam an derselben Position angeordnet werden. Eine Drehfeder lässt sich außerdem mit einer starren Querverschiebung kombinieren und bildet damit eine Teileinspannung.
- **Innere Gelenke modellieren.** Damit lassen sich Gerberträger und andere Systeme mit Momentenfreigaben innerhalb eines durchgehenden Balkens abbilden. Projektübersicht und Berechnungsbericht zeigen die Verdrehungen auf beiden Seiten sowie die Querkraft am Gelenk.
- **Dreieck- und Trapezlasten aufbringen.** Für eine Streckenlast können am Anfang und Ende unterschiedliche Lastintensitäten vorgegeben werden – auch mit einem Vorzeichenwechsel entlang des Balkens. Beide Werte werden im Editor, in der Projektübersicht und im Berechnungsbericht angezeigt.
- **Versionshinweise direkt in SpanDraft lesen.** Der Menüpunkt „Was ist neu?“ öffnet die Versionshinweise auf Deutsch oder Englisch, passend zur Oberflächensprache.

### Verbessert

- **Zahlendarstellung anpassen.** Es stehen fünf Rundungsstufen mit direkter Vorschau in den Einstellungen zur Verfügung. Gerundete Vorgabewerte werden mit `≈` gekennzeichnet, während Ergebnisse ohne Näherungszeichen gerundet dargestellt werden. Der Berechnungsbericht dokumentiert die gewählte Rundung. Sehr kleine und große Werte lassen sich optional mit Zehnerpotenzen darstellen.
- **Berichte und Diagramme klarer lesen.** Die Berichtstabellen für Balkenabschnitte und innere Gelenke wurden neu strukturiert, und die Achsen der Ergebnisdiagramme verwenden jetzt einheitliche Zahlenformate. Verständlichere Berechnungshinweise helfen außerdem dabei, problematische Lagerabstände oder Steifigkeiten zu erkennen, wenn kein verlässliches Ergebnis ermittelt werden kann.
- **Mit weniger Zwischenschritten bearbeiten.** Änderungen am Querschnitt lassen sich speichern, ohne zuvor die letzte Eingabe mit Enter bestätigen zu müssen. Die Werkzeugleiste des Editors passt sich besser an begrenzten Platz an, und ausgewählte vordefinierte Werkstoffe enthalten jetzt klarere Hinweise zum Gültigkeitsbereich ihrer Referenzwerte.

**Kompatibilität:** Projekte aus früheren Versionen lassen sich wie gewohnt öffnen. Nach dem Speichern mit SpanDraft 1.2.0 verwenden sie das neue V5-Projektformat und benötigen anschließend Version 1.2.0 oder neuer.

## 1.1.0

- Optionales Eigengewicht des Balkens in Modell, Editor und Berechnungsbericht ergänzt.
- Eine fixierbare Probe hinzugefügt, mit der sich Durchbiegung, Verdrehung, Querkraft und Biegemoment an einer beliebigen Position entlang des Balkens ablesen lassen.
- Die Bearbeitung von Lasten kompakter gestaltet und Schaltflächen zum Umkehren der Lastvorzeichen ergänzt.
- Die Desktop-Oberfläche überarbeitet, die Typografie in Anwendung und Berichten vereinheitlicht und Ergebnisdiagramme um eine Schraffur ergänzt.
- Eine englische Offline-Hilfe hinzugefügt. Beim Wechsel der Sprache bleibt das aktuell geöffnete Hilfethema erhalten.
- Deutsche und englische Datenschutzhinweise direkt in der Anwendung ergänzt.
- Die Angabe „Erstellt von“ ist jetzt optional. Der Berechnungstitel bleibt bei neuen Projekten weiterhin erforderlich.

**Kompatibilität:** Projekte aus früheren Versionen lassen sich wie gewohnt öffnen. Nach dem Speichern mit SpanDraft 1.1.0 verwenden sie das V4-Projektformat und können nicht mehr mit SpanDraft 1.0.0 geöffnet werden.

## 1.0.0

SpanDraft 1.0.0 ist die erste stabile Version.

- Einen vollständigen Berechnungsbericht mit integrierter Vorschau, PDF-Export und Windows-Druckfunktion hinzugefügt.
- Die Werkstoffverwaltung mit klareren Werkstoffkategorien, eindeutiger Unterscheidung zwischen Re und Rp0,2 sowie einer einheitlichen Darstellung in Anwendung und Berichten verbessert.
- Querschnittseingabe, technische Schreibweise, Zahlenformatierung sowie konfigurierbare Einheiten- und Regionseinstellungen überarbeitet.
- Das Arbeiten mit Projekten durch Projektangaben, einheitlichere Bedienelemente und einen besseren Umgang mit zuletzt verwendeten und wiederhergestellten Projekten verbessert.
- Die Darstellung von Balken, Lasten, Diagrammen und Berichten für klarere technische Grafiken und konsistentere Layouts überarbeitet.
- Die Windows-Integration mit eigenem `.spandraft`-Projektsymbol, Dateizuordnung und robusterer Prüfung des Installationspakets verbessert.

## 0.1.0

Erste öffentlich verfügbare Version von SpanDraft für Windows x64.

- Einen geraden Balken definieren und Lager, Einzelkräfte, Einzelmomente sowie konstante Streckenlasten direkt im grafischen Editor platzieren.
- Werkstoffe sowie parametrische oder manuell definierte Querschnitte auswählen und eigene lokale Werkstoff- und Querschnittsbibliotheken wiederverwenden.
- Berechnungsergebnisse und Lagerreaktionen automatisch anzeigen sowie Durchbiegung, Querkraft und Biegemoment in Diagrammen verfolgen. Zusätzlich werden elastische Biegespannung und Sicherheitsfaktor gegenüber der Streckgrenze berechnet.
- Projekte speichern und erneut öffnen, Änderungen rückgängig machen oder wiederherstellen und den Bearbeitungsstand nach einem Absturz wiederherstellen.
- Die Benutzeroberfläche auf Deutsch oder Englisch verwenden, Einheiten konfigurieren und die enthaltene deutsche Offline-Hilfe nutzen.
