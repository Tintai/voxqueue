# VoxQueue — Português

Síntese de voz em lote para qualquer servidor compatível com OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Início rápido

**Windows** — descarregue `publish/windows/VoxQueue.exe` e execute. Nada a instalar.

**Linux** — `publish/linux/VoxQueue`, ou a partir do código:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Configurações → Conexão** — endereço do servidor, por omissão `http://127.0.0.1:3900`
2. **Atualizar** — carrega a lista de vozes
3. Escolha um modo, cole o texto (ou arraste um `.txt` para o campo)
4. **Processar texto** — mostra quantas falas o analisador encontrou
5. **Iniciar**

Ficheiros de exemplo para cada modo em `samples/`.

---

## Três modos

O modo decide **uma coisa**: como o texto é dividido e como os ficheiros são nomeados.

| Modo | Entrada | Saída |
|---|---|---|
| **Treino** | uma linha = uma fala | `wav/0000.wav` + `metadata.csv` |
| **Dobragem** | `PERSONAGEM: diálogo` | `voices/PERSONAGEM/PERSONAGEM_0000_2.41s.wav` |
| **Lote** | blocos separados por `###` | `wav/0000.wav` + `0000.txt` |

---

## Marcadores de pronúncia

Um modelo TTS lê letras, não ortografia. `Aether` sai como «A-e-ther». `2024` é soletrado. Os números são expandidos na forma base, pelo que qualquer forma flexionada está errada. Dê ao motor uma grafia fonética mantendo a correta na transcrição:

```
{Eter|Aether} está online.
Ano {dois mil e vinte e quatro|2024}.
{Pi Em|PM} enviou o relatório.
```

- a **primeira** forma vai para o motor TTS — é a que é falada
- a **segunda** forma vai para `metadata.csv` e para a transcrição `.txt`

### Regras

| Notação | Efeito |
|---|---|
| `{falado\|escrito}` | diz o primeiro, escreve o segundo |
| `{texto}` sem barra | fica literal — não é marcador |
| lado vazio (`{\|texto}`, `{texto\|}`) | fica literal — nada é apagado |
| `{` sem `}` | fica literal |
| marcador mal formado | nunca destrói texto |

A forma escrita é **inviolável**: mesma flexão, mesma ortografia, mesma pontuação.

La lista de palabras depende del idioma — constrúyala escuchando la voz de referencia.

---

## Prompt pronto a copiar para um modelo de IA

Cole em qualquer assistente para obter um guião que o VoxQueue aceita diretamente.

```
Prepara um ficheiro `.txt` para importar no VoxQueue. Uma linha = uma fala.
Escreve em português natural. Onde uma síntese de voz lesse mal — nomes próprios
estrangeiros, números com vários dígitos, siglas, símbolos, unidades — usa o
marcador `{pronúncia|escrita}`, por exemplo `{Eter|Aether}` ou
`{dois mil e vinte e quatro|2024}`. A forma à direita deve ser exatamente o que
deve aparecer na transcrição, com a flexão original. Não marques palavras comuns,
números de um dígito ou datas que se leem bem. Não traduzas. Não corrijas a
gramática do texto original. No fim, lista todas as linhas cuja pronúncia te
deixou dúvidas.
```

---

De raiz, 5–8 horas de áudio é o mínimo; aponta para 8–10.

Outros idiomas: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
