# VoxQueue — Nederlands

Batch-tekst-na-spraak voor elke OpenAI-compatibele `/v1/audio`-server (Piper, VoiceStudio, Aether Voice Hub).

---

## Snelstart

**Windows** — download `publish/windows/VoxQueue.exe` en start. Niets te installeren.

**Linux** — `publish/linux/VoxQueue`, of vanuit de bron:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Instellingen → Verbinding** — serveradres, standaard `http://127.0.0.1:3900`
2. **Vernieuwen** — laadt de stemmenlijst
3. Kies een modus, plak tekst (of sleep een `.txt` op het veld)
4. **Tekst verwerken** — toont hoeveel uitingen de parser vond
5. **Start**

Voorbeeldbestanden per modus staan in `samples/`.

---

## Drie modi

Een modus bepaalt **één ding**: hoe tekst in uitingen wordt verdeeld en hoe bestanden heten.

| Modus | Invoer | Uitvoer |
|---|---|---|
| **Training** | één regel = één uiting | `wav/0000.wav` + `metadata.csv` |
| **Nasynchronisatie** | `PERSONAGE: dialoog` | `voices/PERSONAGE/PERSONAGE_0000_2.41s.wav` |
| **Batch** | blokken gescheiden door `###` | `wav/0000.wav` + `0000.txt` |

---

## Uitspraakmarkeringen

Een TTS-model leest letters, geen spelling. `Aether` wordt „A-e-ther”. `2024` wordt gespeld. Getallen worden in hun basisvorm uitgeschreven, dus elke verbogen vorm is fout. Geef de engine een fonetische spelling en houd de juiste in het transcript:

```
{Eter|Aether} is online.
Jaar {twee-duizend-vier-en-twintig|2024}.
{Pi Em|PM} stuurde het rapport.
```

- de **eerste** vorm gaat naar de TTS-engine — die wordt uitgesproken
- de **tweede** vorm gaat naar `metadata.csv` en het `.txt`-transcript

### Regels

| Notatie | Effect |
|---|---|
| `{gesproken\|geschreven}` | spreekt het eerste uit, schrijft het tweede |
| `{tekst}` zonder streep | blijft letterlijk — geen markering |
| lege kant (`{\|tekst}`, `{tekst\|}`) | blijft letterlijk — niets wordt verwijderd |
| `{` zonder `}` | blijft letterlijk |
| onjuiste markering | vernietigt nooit tekst |

De geschreven vorm is **onschendbaar**: zelfde verbuiging, zelfde spelling, zelfde leestekens.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Kant-en-klare prompt voor een AI-model

Plak dit in een assistent om een script te krijgen dat VoxQueue direct accepteert.

```
Maak een `.txt`-bestand voor import in VoxQueue. Eén regel = één uiting. Schrijf
in natuurlijk Nederlands. Waar een spraaksynthese iets verkeerd zou lezen —
buitenlandse eigennamen, getallen van meerdere cijfers, afkortingen, symbolen,
eenheden — gebruik de markering `{uitspraak|schrijfwijze}`, bijvoorbeeld
`{Eter|Aether}` of `{twee-duizend-vier-en-twintig|2024}`. De rechtervorm moet
precies zijn wat in het transcript moet staan, met de oorspronkelijke verbuiging.
Markeer geen gewone woorden, getallen van één cijfer of datums die natuurlijk
klinken. Vertaal niet. Corrigeer geen grammatica in de brontekst. Noem aan het
eind alle regels waarvan je de uitspraak niet zeker wist.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Andere talen: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
