# VoxQueue — Svenska

Batch-text-till-tal för vilken OpenAI-kompatibel `/v1/audio`-server som helst (Piper, VoiceStudio, Aether Voice Hub).

---

## Snabbstart

**Windows** — ladda ner `publish/windows/VoxQueue.exe` och kör. Inget att installera.

**Linux** — `publish/linux/VoxQueue`, eller från källkoden:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Inställningar → Anslutning** — serveradress, standard `http://127.0.0.1:3900`
2. **Uppdatera** — läser in röstlistan
3. Välj läge, klistra in text (eller dra en `.txt` till fältet)
4. **Bearbeta text** — visar hur många yttranden tolken hittade
5. **Starta**

Exempelfiler för varje läge finns i `samples/`.

---

## Tre lägen

Läget avgör **en sak**: hur text delas i yttranden och hur filer namnges.

| Läge | Indata | Utdata |
|---|---|---|
| **Träning** | en rad = ett yttrande | `wav/0000.wav` + `metadata.csv` |
| **Dubbning** | `FIGUR: dialog` | `voices/FIGUR/FIGUR_0000_2.41s.wav` |
| **Batch** | block åtskilda av `###` | `wav/0000.wav` + `0000.txt` |

---

## Uttalsmarkeringar

En TTS-modell läser bokstäver, inte stavning. `Aether` blir "A-e-ther". `2024` stavas. Tal skrivs ut i grundform, så varje böjd form är fel. Ge motorn en fonetisk stavning och behåll den korrekta i transkriptet:

```
{Eter|Aether} är online.
År {tjugohundratjugofyra|2024}.
{Pi Em|PM} skickade rapporten.
```

- **första** formen går till TTS-motorn — det är den som sägs
- **andra** formen går till `metadata.csv` och `.txt`-transkriptet

### Regler

| Notation | Effekt |
|---|---|
| `{talat\|skrivet}` | säger det första, skriver det andra |
| `{text}` utan streck | förblir bokstavligt — ingen markering |
| tom sida (`{\|text}`, `{text\|}`) | förblir bokstavligt — inget raderas |
| `{` utan `}` | förblir bokstavligt |
| felaktig markering | förstör aldrig text |

Den skrivna formen är **orubblig**: samma böjning, samma stavning, samma interpunktion.

A confirmed word list is language-specific — build it by listening to the reference voice.

---

## Färdig prompt för en AI-modell

Klistra in i valfri assistent för att få ett manus som VoxQueue tar emot direkt.

```
Förbered en `.txt`-fil för import i VoxQueue. En rad = ett yttrande. Skriv på
naturlig svenska. Där en talsyntes skulle läsa fel — utländska egennamn,
flersiffriga tal, akronymer, symboler, enheter — använd markeringen
`{uttal|stavning}`, till exempel `{Eter|Aether}` eller
`{tjugohundratjugofyra|2024}`. Den högra formen måste vara exakt det som ska stå
i transkriptet, med sin ursprungliga böjning. Markera inte vanliga ord,
ensiffriga tal eller datum som låter naturliga. Översätt inte. Rätta inte
grammatik i källtexten. Lista på slutet alla rader där du var osäker på uttalet.
```

---

From scratch, 5–8 hours of audio is the minimum; aim for 8–10.

Andra språk: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
