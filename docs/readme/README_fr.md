# VoxQueue — Français

Synthèse vocale par lots pour tout serveur compatible OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Démarrage rapide

**Windows** — téléchargez `publish/windows/VoxQueue.exe` et lancez-le. Rien à installer.

**Linux** — `publish/linux/VoxQueue`, ou depuis les sources :

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Réglages → Connexion** — adresse du serveur, par défaut `http://127.0.0.1:3900`
2. **Actualiser** — charge la liste des voix
3. Choisissez un mode, collez le texte (ou glissez un `.txt` sur le champ)
4. **Analyser le texte** — indique combien de répliques ont été trouvées
5. **Démarrer**

Des fichiers d'exemple pour chaque mode se trouvent dans `samples/`.

---

## Trois modes

Le mode décide d'**une seule chose** : comment le texte est découpé et comment les fichiers sont nommés.

| Mode | Entrée | Sortie |
|---|---|---|
| **Entraînement** | une ligne = une réplique | `wav/0000.wav` + `metadata.csv` |
| **Doublage** | `PERSONNAGE : dialogue` | `voices/PERSONNAGE/PERSONNAGE_0000_2.41s.wav` |
| **Lot** | blocs séparés par `###` | `wav/0000.wav` + `0000.txt` |

---

## Marqueurs de prononciation

Un modèle TTS lit des lettres, pas l'orthographe. `Aether` devient « A-e-ther ». `2024` est épelé. Les nombres sont développés à leur forme de base, donc toute forme fléchie est fausse. Donnez au moteur une graphie phonétique en gardant l'orthographe correcte dans la transcription :

```
{Eter|Aether} est en ligne.
Année {deux mille vingt-quatre|2024}.
{Pi Em|PM} a envoyé le rapport.
```

- la **première** forme va au moteur TTS — c'est elle qui est prononcée
- la **seconde** forme va dans `metadata.csv` et dans la transcription `.txt`

### Règles

| Notation | Effet |
|---|---|
| `{prononcé\|écrit}` | dit le premier, écrit le second |
| `{texte}` sans barre | reste littéral — pas un marqueur |
| côté vide (`{\|texte}`, `{texte\|}`) | reste littéral — rien n'est supprimé |
| `{` sans `}` | reste littéral |
| marqueur mal formé | ne détruit jamais le texte |

La forme écrite est **inviolable** : même flexion, même orthographe, même ponctuation.

La liste de mots dépend de la langue — construisez-la en écoutant la voix de référence.

---

## Prompt prêt à copier pour un modèle d'IA

Collez-le dans n'importe quel assistant pour obtenir un script directement exploitable.

```
Prépare un fichier `.txt` pour l'import dans VoxQueue. Une ligne = une réplique.
Écris en français naturel. Partout où une synthèse vocale lirait mal — noms
propres étrangers, nombres à plusieurs chiffres, sigles, symboles, unités —
utilise le marqueur `{prononciation|orthographe}`, par exemple `{Eter|Aether}` ou
`{deux mille vingt-quatre|2024}`. La forme de droite doit être exactement ce qui
doit figurer dans la transcription, avec sa flexion d'origine. Ne marque pas les
mots ordinaires, les nombres à un chiffre ni les dates qui se lisent bien. Ne
traduis pas. Ne corrige pas la grammaire du texte source. À la fin, liste toutes
les lignes dont la prononciation t'a laissé un doute.
```

---

Depuis zéro, 5–8 heures d'audio est le minimum ; visez 8–10.

Autres langues : [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
