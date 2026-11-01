# VoxQueue — 中文

面向任何兼容 OpenAI `/v1/audio` 服务器（Piper、VoiceStudio、Aether Voice Hub）的批量语音合成。

---

## 快速开始

**Windows** — 下载 `publish/windows/VoxQueue.exe` 并运行。无需安装。

**Linux** — `publish/linux/VoxQueue`，或从源码构建:

```bash
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
python3 -m voxqueue
```

1. **设置 → 连接** — 服务器地址，默认 `http://127.0.0.1:3900`
2. **刷新** — 载入音色列表
3. 选择模式，粘贴文本（或将 `.txt` 拖到输入框）
4. **解析文本** — 显示解析器找到多少条台词
5. **开始**

每种模式的示例文件位于 `samples/`。

---

## 三种模式

模式只决定**一件事**：文本如何切分为台词，以及文件如何命名。

| 模式 | 输入 | 输出 |
|---|---|---|
| **训练** | 一行 = 一条台词 | `wav/0000.wav` + `metadata.csv` |
| **配音** | `角色: 台词` | `voices/角色/角色_0000_2.41s.wav` |
| **批处理** | 以 `###` 分隔的块 | `wav/0000.wav` + `0000.txt` |

---

## 发音标记

TTS 模型读的是字母，不是拼写。`Aether` 会变成 “A-e-ther”。`2024` 会被逐字母读出。数字只按原形展开，因此任何变格形式都是错误的。给引擎提供发音写法，同时在转录中保留正确拼写:

```
{Eter|Aether} 已上线。
{二零二四年|2024}。
{披埃姆|PM} 发送了报告。
```

- **第一种**形式交给 TTS 引擎——它才是被读出的内容
- **第二种**形式写入 `metadata.csv` 和 `.txt` 转录

### 规则

| 写法 | 效果 |
|---|---|
| `{发音\|拼写}` | 读第一种，写第二种 |
| 无竖线的 `{文本}` | 原样保留——不是标记 |
| 空的一侧（`{\|文本}`、`{文本\|}`） | 原样保留——不会删除任何内容 |
| 缺少 `}` 的 `{` | 原样保留 |
| 格式错误的标记 | 绝不破坏文本 |

拼写形式**不可更改**：相同的变格、相同的拼写、相同的标点。

确认的词表因语言而异 — 请听参考语音自行整理。

---

## 可直接复制的 AI 提示词

粘贴到任意助手，即可得到 VoxQueue 可直接使用的脚本。

```
请准备一个用于 VoxQueue 导入的 `.txt` 文件。一行 = 一条台词。请用自然的汉语书
写。凡是语音合成会读错的地方——外来专有名词、多位数、缩写、符号、单位——使用
`{发音|写法}` 标记，例如 `{Eter|Aether}` 或 `{二零二四年|2024}`。右侧的写法
必须与要写入转录的内容完全一致，并保留原有的词形。不要标记普通词语、一位数以
及读起来自然的日期。不要翻译。不要修正原文的语法。最后请列出所有你不确定读
音的行。
```

---

从头开始至少需要 5–8 小时音频，目标 8–10 小时。

其他语言: [English](../../README.md) · [Polski](README_pl.md) · [Deutsch](README_de.md) · [Español](README_es.md) · [Français](README_fr.md) · [Italiano](README_it.md) · [Português](README_pt.md) · [Русский](README_ru.md) · [Українська](README_uk.md) · [Čeština](README_cs.md) · [Slovenčina](README_sk.md) · [Nederlands](README_nl.md) · [Svenska](README_sv.md) · [Dansk](README_da.md) · [Norsk bokmål](README_nb.md) · [Suomi](README_fi.md) · [Türkçe](README_tr.md) · [日本語](README_ja.md) · [中文](README_zh.md) · [한국어](README_ko.md)
