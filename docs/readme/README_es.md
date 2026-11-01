# VoxQueue — Español

Síntesis de voz por lotes para cualquier servidor compatible con OpenAI `/v1/audio` (Piper, VoiceStudio, Aether Voice Hub).

---

## Inicio rápido

**Windows** — descarga `publish/windows/VoxQueue.exe` y ejecútalo. Nada que instalar.

**Linux** — `publish/linux/VoxQueue`, o desde el código:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **Ajustes → Conexión** — dirección del servidor, por defecto `http://127.0.0.1:3900`
2. **Actualizar** — carga la lista de voces
3. Elige un modo, pega el texto (o arrastra un `.txt` al campo)
4. **Procesar texto** — muestra cuántas frases encontró el analizador
5. **Iniciar**

Hay archivos de ejemplo para cada modo en `samples/`.

---

## Tres modos

El modo decide **una cosa**: cómo se divide el texto en frases y cómo se nombran los archivos.

| Modo | Entrada | Salida |
|---|---|---|
| **Entrenamiento** | una línea = una frase | `wav/0000.wav` + `metadata.csv` |
| **Doblaje** | `PERSONAJE: diálogo` | `voices/PERSONAJE/PERSONAJE_0000_2.41s.wav` |
| **Lote** | bloques separados por `###` | `wav/0000.wav` + `0000.txt` |

---

## Marcadores de pronunciación

Un modelo TTS lee letras, no ortografía. `Aether` sale como «A-e-ther». `2024` se deletrea. Los números se expanden en su forma base, así que cualquier forma flexionada es incorrecta. Da al motor una grafía fonética conservando la correcta en la transcripción:

```
{Eter|Aether} está en línea.
Año {dos mil veinticuatro|2024}.
{Pi Em|PM} envió el informe.
```

- la **primera** forma va al motor TTS — es lo que se pronuncia
- la **segunda** forma va a `metadata.csv` y a la transcripción `.txt`

### Reglas

| Notación | Efecto |
|---|---|
| `{hablado\|escrito}` | dice el primero, escribe el segundo |
| `{texto}` sin barra | queda literal — no es marcador |
| lado vacío (`{\|texto}`, `{texto\|}`) | queda literal — no se borra nada |
| `{` sin `}` | queda literal |
| marcador mal formado | nunca destruye texto |

La forma escrita es **inviolable**: misma flexión, misma ortografía, misma puntuación.

La lista de palabras depende del idioma — constrúyala escuchando la voz de referencia.

---

## Prompt listo para copiar a un modelo de IA

Pégalo en cualquier asistente para obtener un guion que VoxQueue acepte directamente.

```
Prepara un archivo `.txt` para importar en VoxQueue. Una línea = una frase.
Escribe en español natural. Donde un sintetizador leería algo incorrectamente —
nombres propios extranjeros, números de varias cifras, siglas, símbolos,
unidades — usa el marcador `{pronunciación|escritura}`, por ejemplo
`{Eter|Aether}` o `{dos mil veinticuatro|2024}`. La forma de la derecha debe ser
exactamente lo que debe aparecer en la transcripción, con su flexión original. No
marques palabras comunes, números de una cifra ni fechas que se lean bien. No
traduzcas. No corrijas la gramática del texto original. Al final, enumera todas
las líneas cuya pronunciación te generó dudas.
```

---

Desde cero, 5–8 horas de audio es el mínimo; apunta a 8–10.

Otros idiomas: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
