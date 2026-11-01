# VoxQueue — Deutsch

Stapel-Sprachsynthese für jeden OpenAI-kompatiblen `/v1/audio`-Server (Piper, VoiceStudio, Aether Voice Hub).

---

## Schnellstart

**Windows** — `publish/windows/VoxQueue.exe` herunterladen und starten. Nichts zu installieren.

**Linux** — `publish/linux/VoxQueue`, oder aus dem Quelltext:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Einstellungen → Verbindung** — Serveradresse, Standard `http://127.0.0.1:3900`
2. **Aktualisieren** — lädt die Stimmenliste
3. Modus wählen, Text einfügen (oder `.txt` auf das Feld ziehen)
4. **Text zerlegen** — zeigt, wie viele Äußerungen erkannt wurden
5. **Start**

Beispieldateien für jeden Modus liegen in `samples/`.

---

## Drei Modi

Ein Modus entscheidet über **eine Sache**: wie Text in Äußerungen zerlegt und wie Dateien benannt werden.

| Modus | Eingabe | Ausgabe |
|---|---|---|
| **Training** | eine Zeile = eine Äußerung | `wav/0000.wav` + `metadata.csv` |
| **Synchron** | `NAME: Dialog` | `voices/NAME/NAME_0000_2.41s.wav` |
| **Stapel** | Blöcke getrennt durch `###` | `wav/0000.wav` + `0000.txt` |

---

## Aussprache-Marker

Ein TTS-Modell liest Buchstaben, keine Schreibweise. `Aether` wird zu „A-e-ther“. `2024` wird buchstabiert. Zahlen werden in ihrer Grundform ausgeschrieben, sodass jede gebeugte Form falsch ist. Gib der Engine eine phonetische Schreibweise und behalte die korrekte im Transkript:

```
{Eter|Aether} ist online.
Im Jahr {zwei-tausend-vier-und-zwanzig|2024}.
{Pi Em|PM} hat den Bericht geschickt.
```

- **erste** Form geht an die TTS-Engine — sie wird gesprochen
- **zweite** Form geht in `metadata.csv` und ins `.txt`-Transkript

### Regeln

| Schreibweise | Wirkung |
|---|---|
| `{gesprochen\|geschrieben}` | spricht das erste, schreibt das zweite |
| `{text}` ohne Strich | bleibt wörtlich — kein Marker |
| leere Seite (`{\|text}`, `{text\|}`) | bleibt wörtlich — nichts wird gelöscht |
| `{` ohne `}` | bleibt wörtlich |
| fehlerhafter Marker | zerstört nie Text |

Die geschriebene Form ist **unantastbar**: gleiche Beugung, gleiche Schreibweise, gleiche Zeichensetzung.

Eine bestätigte Wortliste ist sprachabhängig — erstellen Sie sie durch Zuhören.

---

## Fertiger Prompt für ein KI-Modell

In einen beliebigen Assistenten einfügen, um ein direkt nutzbares Skript zu erhalten.

```
Erstelle eine `.txt`-Datei für den Import in VoxQueue. Eine Zeile = eine
Äußerung. Schreibe in natürlichem Deutsch. Wo immer eine Sprachsynthese etwas
falsch lesen würde — fremde Eigennamen, mehrstellige Zahlen, Akronyme, Symbole,
Einheiten — verwende den Marker `{Aussprache|Schreibweise}`, zum Beispiel
`{Eter|Aether}` oder `{zwei-tausend-vier-und-zwanzig|2024}`. Die rechte Form muss
genau das sein, was im Transkript stehen soll, mit ursprünglicher Beugung.
Markiere keine gewöhnlichen Wörter, einstelligen Zahlen oder natürlich klingende
Daten. Übersetze nicht. Korrigiere keine Grammatik im Quelltext. Liste am Ende
alle Zeilen auf, bei deren Aussprache du unsicher warst.
```

---

Von Grund auf sind 5–8 Stunden Audio das Minimum; streben Sie 8–10 an.

Andere Sprachen: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
