# VoxQueue — Italiano

Sintesi vocale in batch per qualsiasi server compatibile OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Avvio rapido

**Windows** — scarica `publish/windows/VoxQueue.exe` e avvialo. Niente da installare.

**Linux** — `publish/linux/VoxQueue`, oppure dai sorgenti:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Impostazioni → Connessione** — indirizzo del server, predefinito `http://127.0.0.1:3900`
2. **Aggiorna** — carica l'elenco delle voci
3. Scegli un modo, incolla il testo (o trascina un `.txt` sul campo)
4. **Analizza testo** — mostra quante battute ha trovato il parser
5. **Avvia**

File di esempio per ogni modo in `samples/`.

---

## Tre modi

Il modo decide **una cosa**: come il testo viene diviso e come vengono nominati i file.

| Modo | Ingresso | Uscita |
|---|---|---|
| **Addestramento** | una riga = una battuta | `wav/0000.wav` + `metadata.csv` |
| **Doppiaggio** | `PERSONAGGIO: dialogo` | `voices/PERSONAGGIO/PERSONAGGIO_0000_2.41s.wav` |
| **Batch** | blocchi separati da `###` | `wav/0000.wav` + `0000.txt` |

---

## Marcatori di pronuncia

Un modello TTS legge lettere, non ortografia. `Aether` diventa «A-e-ther». `2024` viene compitato. I numeri sono espansi nella forma base, quindi ogni forma flessa è sbagliata. Fornisci al motore una grafia fonetica mantenendo quella corretta nella trascrizione:

```
{Eter|Aether} è online.
Anno {duemilaventiquattro|2024}.
{Pi Em|PM} ha inviato il rapporto.
```

- la **prima** forma va al motore TTS — è quella pronunciata
- la **seconda** forma va in `metadata.csv` e nella trascrizione `.txt`

### Regole

| Notazione | Effetto |
|---|---|
| `{parlato\|scritto}` | dice il primo, scrive il secondo |
| `{testo}` senza barra | resta letterale — non è un marcatore |
| lato vuoto (`{\|testo}`, `{testo\|}`) | resta letterale — nulla viene cancellato |
| `{` senza `}` | resta letterale |
| marcatore malformato | non distrugge mai il testo |

La forma scritta è **inviolabile**: stessa flessione, stessa ortografia, stessa punteggiatura.

L'elenco di parole dipende dalla lingua — costruiscilo ascoltando la voce di riferimento.

---

## Prompt pronto per un modello di IA

Incollalo in qualsiasi assistente per ottenere uno script utilizzabile subito.

```
Prepara un file `.txt` per l'importazione in VoxQueue. Una riga = una battuta.
Scrivi in italiano naturale. Dove una sintesi vocale leggerebbe male — nomi
propri stranieri, numeri a più cifre, sigle, simboli, unità — usa il marcatore
`{pronuncia|scrittura}`, per esempio `{Eter|Aether}` o
`{duemilaventiquattro|2024}`. La forma a destra deve essere esattamente ciò che
deve comparire nella trascrizione, con la flessione originale. Non marcare parole
comuni, numeri a una cifra o date che si leggono bene. Non tradurre. Non
correggere la grammatica del testo di partenza. Alla fine elenca tutte le righe di
cui non eri sicuro della pronuncia.
```

---

Da zero servono almeno 5–8 ore di audio; punta a 8–10.

Altre lingue: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
