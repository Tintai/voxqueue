#!/usr/bin/env python3
"""Frozen-build entry point.

PyInstaller runs its entry script as `__main__` with no package context, so a
module inside the package that uses relative imports (`from . import ...`)
fails with "attempted relative import with no known parent package". This file
sits outside the package and imports it absolutely, which fixes that and keeps
`voxqueue/main.py` importable the normal way.
"""

from __future__ import annotations

import multiprocessing
import os
import sys


def selftest() -> int:
    """Verify a built bundle without opening a window.

    Checks that every module imports, the assets were bundled, and the Qt
    platform plugin loads. A packaged binary that cannot do this is broken,
    and this finds out before a user does.
    """
    import os
    import traceback

    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    failures = 0

    def step(name: str, fn) -> None:
        nonlocal failures
        try:
            detail = fn()
            print(f"  OK   {name}" + (f"  ({detail})" if detail else ""))
        except Exception as e:  # noqa: BLE001
            failures += 1
            print(f"  FAIL {name}: {type(e).__name__}: {e}")
            traceback.print_exc(limit=2)

    print("VoxQueue selftest")

    def check_assets():
        from pathlib import Path
        from voxqueue.main import resource_path
        found = []
        for name in ("icon.png", "icon-128.png"):
            p = Path(resource_path(f"assets/{name}"))
            if not p.exists():
                raise FileNotFoundError(f"missing asset: {name}")
            found.append(f"{name}={p.stat().st_size}B")
        return ", ".join(found)

    def check_imports():
        from voxqueue.core import audio, config, context, splitter, state, worker
        from voxqueue.i18n import LANGS, I18N
        from voxqueue.ui import main_window, settings_tab, theme, widgets, work_tab
        assert len(LANGS) == 20, f"expected 20 languages, got {len(LANGS)}"
        return f"{len(LANGS)} langs"

    def check_splitter():
        from voxqueue.core import splitter as sp
        dub = sp.split_dub("SHEPPARD: raz.\n[EDI] dwa.")
        assert len(dub) == 2 and dub[1].speaker == "EDI", dub
        lines = sp.split_lines("a\n\nb\n")
        assert len(lines) == 2, lines
        return "dub+train"

    def check_pronunciation():
        from voxqueue.core import pronunciation as pr

        # spoken|written — the engine says "Eter", the transcript keeps "Aether"
        sp, wr = pr.split("{Eter|Aether} jest online.")
        assert sp == "Eter jest online.", sp
        assert wr == "Aether jest online.", wr

        # a number read aloud, stored as digits
        sp, wr = pr.split("Mamy {dwa tysiące dwudziestego czwartego|2024} rok.")
        assert "dwa tysiące" in sp and "2024" in wr, (sp, wr)

        # no markers: both forms identical, nothing lost
        sp, wr = pr.split("Zwykłe zdanie bez znaczników.")
        assert sp == wr, (sp, wr)

        # malformed markers stay literal instead of eating the text
        for bad in ("To { nie jest znacznik.", "Puste {|} znaczniki.", "{tylko|}"):
            sp, wr = pr.split(bad)
            assert sp == bad and wr == sp, (bad, sp, wr)

        # the grid shows only the substituted words
        assert pr.summary("{Eter|Aether} i {dżoker|Joker}.") == "Eter · dżoker"
        assert pr.summary("bez znaczników") == ""
        assert pr.summary("{same|same}") == ""

        return "4 marker shapes"

    def check_workspace():
        import tempfile
        from pathlib import Path
        from voxqueue.core import workspace as ws

        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / f"t.{ws.EXTENSION}"
            proj = ws.Project(
                mode="dub", out_dir="/tmp/out", voice="v1",
                input_text="SHEPPARD: raz.",
                options={"add_extension": False, "index_start": 5},
                items=[ws.ProjectItem(index=0, text="Aether", spoken="Eter",
                                      phonetic="Eter", speaker="SHEPPARD")],
            )
            proj.voice_map["SHEPPARD"] = "v1"
            ws.save(path, proj)

            back = ws.load(path)
            assert back.mode == "dub", back.mode
            assert back.options["add_extension"] is False, back.options
            assert back.items[0].spoken == "Eter", back.items[0].spoken
            assert back.voice_map["SHEPPARD"] == "v1", back.voice_map

            # a corrupt file must fail loudly, not return an empty project
            path.write_text("{ not json", encoding="utf-8")
            try:
                ws.load(path)
            except ValueError as exc:
                assert str(exc), "empty error message"
            else:
                raise AssertionError("corrupt project was accepted")

        return "save+load+corrupt"

    def check_resume():
        import tempfile
        from pathlib import Path
        from voxqueue.core.state import Job, Item

        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            (out / "wav").mkdir(parents=True)

            # A previous run: two clips finished, one failed.
            prev = Job(mode="train", out_dir=str(out), voice="v")
            for idx, txt in enumerate(("pierwsze", "drugie", "trzecie")):
                prev.items.append(Item(index=idx, text=txt))
            prev.items[0].status = "done"
            prev.items[0].file = "wav/0000.wav"
            prev.items[0].duration = 1.5
            prev.items[1].status = "done"
            prev.items[1].file = "wav/0001.wav"
            prev.items[1].duration = 2.0
            prev.items[2].status = "failed"
            prev.items[2].error = "boom"
            (out / "wav" / "0000.wav").write_bytes(b"RIFF")
            (out / "wav" / "0001.wav").write_bytes(b"RIFF")
            prev.save()

            # A new run over the same folder, different numbering.
            nxt = Job(mode="train", out_dir=str(out), voice="v",
                      options={"index_start": 100})
            for idx, txt in enumerate(("pierwsze", "drugie", "trzecie", "czwarte")):
                nxt.items.append(Item(index=idx, text=txt))

            restored = nxt.restore_progress()
            assert restored == 2, f"restored {restored}, expected 2"
            assert nxt.items[0].status == "done", "item 0 not restored"
            assert nxt.items[1].status == "done", "item 1 not restored"
            assert nxt.items[2].status == "pending", "failed item inherited as failed"
            assert nxt.items[3].status == "pending", "unknown line marked done"

            # A row pointing at a deleted file must stay pending.
            (out / "wav" / "0000.wav").unlink()
            again = Job(mode="train", out_dir=str(out))
            again.items.append(Item(index=0, text="pierwsze"))
            again.items.append(Item(index=1, text="drugie"))
            second = again.restore_progress()
            assert second == 1, f"missing file restored anyway ({second})"
            assert again.items[0].status == "pending", "deleted clip restored as done"

        return "2 of 4, failed stays pending, missing file skipped"

    def check_sort_rank():
        """Failed rows first, then flagged, original index order inside a bucket."""
        from voxqueue.core.state import Item

        items = [
            Item(index=0, text="ok-1", status="done"),
            Item(index=1, text="failed-1", status="failed"),
            Item(index=2, text="flagged-1", status="done", defects=["click_end"]),
            Item(index=3, text="ok-2", status="done"),
            Item(index=4, text="failed-2", status="failed"),
            Item(index=5, text="flagged-2", status="done", defects=["beep"]),
        ]

        order = [i.index for i in sorted(items, key=lambda i: (i.sort_rank, i.index))]
        assert order == [1, 4, 2, 5, 0, 3], f"errors-first order = {order}"

        restored = [i.index for i in sorted(items, key=lambda i: i.index)]
        assert restored == [0, 1, 2, 3, 4, 5], "original order not recoverable"

        # A pending row must not be lifted as if it were a problem.
        assert Item(index=9, text="x").sort_rank == 3, "pending row ranked as an error"

        return "failed, flagged, rest, then index order"

    def check_session():
        """The session slot round-trips, and an empty one is cleared."""
        import tempfile
        from pathlib import Path
        from voxqueue.core import workspace as ws

        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "session.vqproj"
            proj = ws.Project(
                mode="dub", out_dir="/tmp/out", voice="v1", input_text="A: one",
                items=[ws.ProjectItem(index=0, text="one", speaker="A")])
            ws.save(path, proj)

            back = ws.load(path)
            assert back.mode == "dub", back.mode
            assert len(back.items) == 1, len(back.items)
            assert back.input_text == "A: one", back.input_text

        # All three tabs must survive: saving only the active one lost the other
        # two every time the app was closed from a different tab.
        import voxqueue.core.workspace as W
        orig = W.session_path
        with tempfile.TemporaryDirectory() as tmp:
            W.session_path = lambda: Path(tmp) / "session.vqproj"
            try:
                W.save_session({
                    "train": ws.Project(mode="train",
                                        items=[ws.ProjectItem(index=0, text="t")]),
                    "dub": ws.Project(mode="dub",
                                      items=[ws.ProjectItem(index=0, text="d")]),
                }, active=1)

                loaded = W.load_session()
                assert loaded is not None, "session did not load"
                projects, active = loaded
                assert set(projects) == {"train", "dub"}, f"tabs = {set(projects)}"
                assert active == 1, f"active = {active}"

                # Empty everything clears the slot.
                W.save_session({}, 0)
                assert W.load_session() is None, "empty session was kept"
            finally:
                W.session_path = orig

        return "round-trip + all tabs + empty clears"

    def check_pinned():
        """The pin flag must survive a workspace save."""
        import tempfile
        from pathlib import Path
        from voxqueue.core import workspace as ws

        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "p.vqproj"
            proj = ws.Project(mode="train", items=[
                ws.ProjectItem(index=0, text="a", pinned=True),
                ws.ProjectItem(index=1, text="b", pinned=False),
            ])
            ws.save(path, proj)

            back = ws.load(path)
            assert back.items[0].pinned is True, "pin lost on save"
            assert back.items[1].pinned is False, "unpinned row became pinned"

        return "pin survives save"

    def check_source_line():
        """The raw source line must survive a project save.

        Editing a pronunciation finds the line by matching it; a restored session
        without it made every edit fail silently.
        """
        import tempfile
        from pathlib import Path
        from voxqueue.core import workspace as ws

        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "p.vqproj"
            proj = ws.Project(mode="train", items=[
                ws.ProjectItem(index=0, text="Aether is online",
                               spoken="Eter is online",
                               source_line="{Eter|Aether} is online"),
            ])
            ws.save(path, proj)

            back = ws.load(path)
            got = back.items[0].source_line
            assert got == "{Eter|Aether} is online", f"source line lost: '{got}'"

        return "raw line round-trips"

    def check_pron_edit():
        """Editing a pronunciation must reach the text — including on a line that
        already carries a marker, which is where the matching used to fail."""
        from voxqueue.core import pronunciation, splitter

        def rewrite(text, source_line, item_text, replacement):
            raw = source_line or item_text
            target = splitter.clean(pronunciation.to_written(raw))
            lines = text.replace("\r\n", "\n").split("\n")
            for i, line in enumerate(lines):
                if splitter.clean(pronunciation.to_written(line)) != target:
                    continue
                lines[i] = replacement
                return "\n".join(lines)
            return None

        # Plain line.
        got = rewrite("Aether is online.\nSecond.", "Aether is online.",
                      "Aether is online.", "{Aether is online.|Ether is online.}")
        assert got == "{Aether is online.|Ether is online.}\nSecond.", got

        # Line that already has a marker: this is the case that used to fail.
        got = rewrite("{Eter|Aether} is online.\nSecond.", "{Eter|Aether} is online.",
                      "Aether is online.", "{Eter|Aether is on-line.}")
        assert got == "{Eter|Aether is on-line.}\nSecond.", got

        # And the rewritten line must still parse to the new spelling.
        spoken, written = pronunciation.split("{Eter|Aether is on-line.}")
        assert written == "Aether is on-line.", written
        assert spoken == "Eter", spoken

        return "plain, marked and re-parse"

    def check_phonetic_diff():
        """The column shows the WORDS that changed, not the sentence."""
        from voxqueue.core import pronunciation as p

        got = p.summary("{Aether is online today|Ether is online today}")
        assert got == "Aether", f"summary = {got!r}"

        got = p.summary("{Eter|Aether} met {Dżoker|Joker}")
        assert got == "Eter · Dżoker", f"multi = {got!r}"

        assert p.summary("plain text") == "", "plain text produced a summary"
        assert p.summary("{Eter.|Eter}") == "", "punctuation counted as a change"

        uneven = p.summary("{dwa słowa|one}")
        assert uneven == "dwa słowa", f"uneven = {uneven!r}"

        return "single, multi, plain, punctuation, uneven"

    def check_search():
        """Search covers text, pronunciation, speaker and index."""
        from voxqueue.core.state import Item

        items = [
            Item(index=0, text="Aether is online", spoken="Eter is online", phonetic="Eter"),
            Item(index=1, text="Nothing here", spoken="Nothing here"),
            Item(index=2, text="Third line"),
        ]

        assert sum(1 for i in items if i.matches("Aether")) == 1, "text search"
        assert sum(1 for i in items if i.matches("Eter")) == 1, "spoken search"

        hit = [i for i in items if i.matches("aether")]
        assert len(hit) == 1 and hit[0].index == 0, "case-insensitive search"

        assert sum(1 for i in items if i.matches("")) == 3, "empty query filtered rows"
        assert sum(1 for i in items if i.matches("   ")) == 3, "whitespace query filtered"

        by_index = [i for i in items if i.matches("2")]
        assert len(by_index) == 1 and by_index[0].index == 2, "index search"

        return "text, spoken, case, empty, index"

    def check_bulk_replace():
        """Markers are skipped, whole-word and case rules hold."""
        from voxqueue.core import pronunciation as p

        got, n = p.replace_all("Kup ID i drugi ID.", "ID", "aj-di", "ID", True, True)
        assert n == 2, n
        assert "{aj-di|ID}" in got, got

        # A marker already in the line is REWRITTEN: fixing a pronunciation that
        # is already marked is the main use of the dialog. {i-de|ID} -> {ajdi|ID},
        # with the written side untouched.
        # Empty "Written as" keeps each marker's own recorded form.
        guarded, n2 = p.replace_all("{i-de|ID} lewy {i-de|SS} oraz ID",
                                    "i-de", "ajdi", "", True, True)
        assert n2 == 2, n2
        assert guarded == "{ajdi|ID} lewy {ajdi|SS} oraz ID", guarded

        untouched, n2b = p.replace_all("{Eter|Aether} x", "i-de", "ajdi", "", True, True)
        assert n2b == 0 and untouched == "{Eter|Aether} x", untouched

        assert p.replace_all("IDEA ID", "ID", "a", "ID", False, True)[1] == 2
        assert p.replace_all("IDEA ID", "ID", "a", "ID", True, True)[1] == 1
        assert p.replace_all("id ID", "ID", "a", "ID", True, False)[1] == 2
        assert p.replace_all("id ID", "ID", "a", "ID", True, True)[1] == 1

        same, n7 = p.replace_all("nothing", "zzz", "a", "a", True, False)
        assert n7 == 0 and same == "nothing", "no-match altered the text"

        return "marker rewrite, whole word, case, no match"

    def check_stale():
        """An edited-but-not-regenerated clip is marked and sorts first."""
        from voxqueue.core.state import Item

        items = [
            Item(index=0, text="a", status="done"),
            Item(index=1, text="b", status="stale"),
            Item(index=2, text="c", status="failed"),
            Item(index=3, text="d"),
        ]

        assert items[1].is_stale, "stale flag missing"
        assert items[1].sort_rank == 1, items[1].sort_rank
        assert items[2].sort_rank == 0, items[2].sort_rank
        assert items[3].sort_rank == 3, items[3].sort_rank

        order = [i.index for i in sorted(items, key=lambda i: (i.sort_rank, i.index))]
        assert order == [2, 1, 0, 3], order

        return "rank and order"

    def check_i18n_complete():
        """Every key must resolve in every language.

        This is the check that would have caught the C# table: a key that resolves
        to the WRONG string still resolves, so nothing complained while the Log tab
        was labelled "Replaced" and the colouring switch "Replace".
        """
        from voxqueue.i18n import I18N, LANGS, _EN

        problems = []
        for code, _name in LANGS:
            t = I18N(code)
            for key in _EN:
                text = t.t(key)
                if not text:
                    problems.append(f"{code}:{key}=empty")
                elif text == key and code != "en":
                    problems.append(f"{code}:{key}=raw")

        assert not problems, f"{len(problems)} problem(s): {problems[:8]}"

        # Spot checks for the labels that were reported broken.
        assert I18N("en").t("tab_log") == "Log"
        assert I18N("pl").t("tab_log") == "Log"
        assert I18N("en").t("colour_text") == "Colour the text"
        assert I18N("pl").t("colour_text") == "Koloruj tekst"
        assert I18N("en").t("bulk_pron") == "Bulk pronunciation"
        assert I18N("pl").t("log_refresh") == "Odśwież"
        assert I18N("en").t("stale_only") == "Needs regeneration only"

        return f"{len(_EN)} keys x {len(LANGS)} languages"

    def check_language_switch():
        """Every panel must survive a language change.

        This is the check that was missing entirely: WorkTab.retranslate called
        self.table.set_headers(), and that method did not exist. Switching
        language raised AttributeError and took the tab with it, while the
        translation table itself was perfectly fine — so every test that only
        looked at the table passed.
        """
        from PySide6.QtWidgets import QApplication
        app = QApplication.instance() or QApplication([])

        from voxqueue.core.context import Context
        from voxqueue.ui.log_tab import LogTab
        from voxqueue.ui.settings_tab import SettingsTab
        from voxqueue.ui.work_tab import WorkTab

        ctx = Context()
        languages = ("en", "pl", "de", "ja")
        built = 0

        try:
            for mode in ("train", "dub", "bulk"):
                tab = WorkTab(ctx, mode)
                for code in languages:
                    ctx.tr.set_language(code)
                    tab.retranslate()
                built += 1

            settings = SettingsTab(ctx)
            log = LogTab(ctx)
            for code in languages:
                ctx.tr.set_language(code)
                settings.retranslate()
                log.retranslate()
            built += 2

        finally:
            ctx.tr.set_language("en")

        return f"{built} panels x {len(languages)} languages"

    def check_retry_covers():
        """"Retry" must re-queue failed AND stale rows, not only failed ones."""
        from voxqueue.core.state import Item, Job

        job = Job(mode="train", out_dir="/tmp/vq-retry")
        job.items = [
            Item(index=0, text="a", status="done", file="wav/a.wav"),
            Item(index=1, text="b", status="failed", error="boom"),
            Item(index=2, text="c", status="stale", file="wav/c.wav"),
            Item(index=3, text="d", status="stale", file="wav/d.wav"),
            Item(index=4, text="e"),
        ]

        count = job.reset_failed()
        assert count == 3, f"re-queued {count}, expected 3"

        assert job.items[0].status == "done", "a finished row was re-queued"
        assert job.items[1].status == "pending", "failed row not re-queued"
        assert job.items[1].error == "", "failed row kept its error"
        assert job.items[2].status == "pending", "stale row not re-queued"
        assert job.items[3].status == "pending", "second stale row not re-queued"

        # The clip on disk is kept so it can still be played if the re-render fails.
        assert job.items[2].file == "wav/c.wav", "stale row lost its file"

        # A second call must find nothing.
        assert job.reset_failed() == 0, "second reset found work"

        return "3 re-queued, done row untouched, file kept"

    def check_polish_hints():
        """A hint that silently falls back to English is a missing translation.

        The three field notes were added to the English table and, by a bad
        replace, landed in the Polish block as English text — which resolves, so
        every completeness check passed while the dialog was half-translated.
        """
        from voxqueue.i18n import I18N

        en = I18N("en")
        pl = I18N("pl")
        keys = ("bulk_find_note", "bulk_spoken_note", "bulk_written_note",
                "bulk_pron_hint", "bulk_spoken_hint")

        same = [k for k in keys if pl.hint(k) == en.hint(k)]

        assert not same, f"Polish hint equals English for: {same}"

        return f"{len(keys)} hints differ from English"

    def check_written_form():
        """Leaving "Written as" alone must keep what the text records."""
        from voxqueue.core import pronunciation as p

        src = "Kup {i-de|ID} oraz {i-de|SS} i wolny i-de tekst"

        assert p.written_form_for(src, "i-de") == "ID", p.written_form_for(src, "i-de")

        got, n = p.replace_all(src, "i-de", "ajdi", "ID")
        assert n == 3, n
        assert got == "Kup {ajdi|ID} oraz {ajdi|ID} i wolny {ajdi|ID} tekst", got

        got, n = p.replace_all(src, "i-de", "ajdi", "")
        assert n == 3, n
        assert got == "Kup {ajdi|ID} oraz {ajdi|SS} i wolny {ajdi|i-de} tekst", got

        got, n = p.replace_all(src, "i-de", "ajdi", "EjdI")
        assert got == "Kup {ajdi|EjdI} oraz {ajdi|EjdI} i wolny {ajdi|EjdI} tekst", got

        assert p.written_form_for("zwykly tekst", "i-de") is None

        return "suggest ID, empty keeps per-marker, explicit wins"

    def check_stale_lifetime():
        """A row needing regeneration stays listed until its render succeeds."""
        from voxqueue.core.state import Item

        stale = Item(index=1, text="b", status="stale", needs_regen=True)
        queued = Item(index=2, text="c", status="pending", needs_regen=True)
        running = Item(index=3, text="d", status="running", needs_regen=True)
        failed = Item(index=4, text="e", status="failed", needs_regen=True)
        legacy = Item(index=5, text="f", status="stale")
        fresh = Item(index=0, text="a", status="done")

        for row in (stale, queued, running, failed, legacy):
            assert row.awaits_regen, f"{row.index} vanished from the list"

        assert not fresh.awaits_regen, "a finished row is in the regeneration list"

        # Only success removes it.
        running.needs_regen = False
        running.status = "done"
        assert not running.awaits_regen, "row still listed after a successful render"

        return "stale, queued, running, failed stay; done leaves"

    def check_settings_visible():
        """Every settings control must actually be in the widget tree.

        A widget that is created, configured and connected to its handler is still
        invisible if nobody adds it to a layout. That is exactly what happened to
        the language selector: `row` was reassigned to the colouring row before the
        language row was added, so the combo existed and worked but never reached
        the card. On screen it looked as though the application had no language
        setting at all.
        """
        from PySide6.QtWidgets import QApplication, QComboBox, QCheckBox

        app = QApplication.instance() or QApplication([])

        from voxqueue.core.context import Context
        from voxqueue.ui.settings_tab import SettingsTab

        tab = SettingsTab(Context())

        # Walk up the parent chain: an orphan has no parent widget at all.
        def attached(w) -> bool:
            seen, node = 0, w.parent()
            while node is not None and seen < 50:
                if node is tab:
                    return True
                node = node.parent()
                seen += 1
            return False

        required = {
            "theme combo": tab.cmb_theme,
            "language combo": tab.cmb_lang,
            "audio format combo": tab.cmb_fmt,
            "sample rate combo": tab.cmb_rate,
            "colouring checkbox": tab.chk_colour,
        }

        orphans = [name for name, w in required.items() if not attached(w)]
        assert not orphans, f"created but never added to the window: {orphans}"

        # And they must hold real entries, not be empty shells.
        assert tab.cmb_lang.count() >= 5, f"language combo has {tab.cmb_lang.count()} entries"
        assert tab.cmb_theme.count() == 3, f"theme combo has {tab.cmb_theme.count()} entries"

        # "System" must be a real, first, selected-by-default entry. It used to be
        # appended lazily inside _load and only when the saved value was already
        # "system", so a fresh install had no such option in the list at all.
        assert tab.cmb_lang.itemData(0) == "system", \
            f"first language entry is {tab.cmb_lang.itemData(0)!r}, expected 'system'"
        assert tab.cmb_lang.itemText(0).strip(), "the System entry has no label"

        # It must also translate: it is a normal string, unlike language names.
        seen = set()
        for code in ("pl", "de", "ja"):
            tab.ctx.tr.set_language(code)
            tab.retranslate()
            seen.add(tab.cmb_lang.itemText(0))
        tab.ctx.tr.set_language("en")
        tab.retranslate()
        assert len(seen) == 3, f"System label is not translated: {seen}"

        return f"{len(required)} controls attached, {tab.cmb_lang.count()} languages"

    def check_labels_translate():
        """Every registered label must actually change when the language changes.

        The registry guarantees a label is reachable; this proves the reachable
        ones are re-read. Together they close the class of bug where half a page
        stays in the previous language after switching: a label created inline
        could not be updated, and nothing noticed.
        """
        SAME_IN_BOTH = {"start", "system", "status", "log", "search"}

        from PySide6.QtWidgets import QApplication, QCheckBox, QLabel, QPushButton

        app = QApplication.instance() or QApplication([])

        from voxqueue.core.context import Context
        from voxqueue.ui.settings_tab import SettingsTab
        from voxqueue.ui.work_tab import WorkTab

        ctx = Context()

        panels = [SettingsTab(ctx), WorkTab(ctx, "train"), WorkTab(ctx, "bulk")]

        def snapshot(panel):
            # Every widget that carries a caption, not just QLabel. The first
            # version of this check looked at QLabel alone, so a checkbox left in
            # the previous language slipped through: exactly the reported case.
            out = []
            for cls in (QLabel, QCheckBox, QPushButton):
                out += [w.text() for w in panel.findChildren(cls)]
            return out

        try:
            ctx.tr.set_language("pl")
            for p in panels:
                p.retranslate()
            before = [snapshot(p) for p in panels]

            ctx.tr.set_language("de")
            for p in panels:
                p.retranslate()
            after = [snapshot(p) for p in panels]

            stuck = []
            for idx, (b, a) in enumerate(zip(before, after)):
                for x, y in zip(b, a):
                    # Empty strings, pure symbols and template syntax such as
                    # "{index} → 0000" are the same in every language. Requiring a
                    # letter is what separates text from a dash or an arrow.
                    if not any(c.isalpha() for c in x) or "{" in x:
                        continue
                    # A word that is spelled the same in both languages is not a
                    # bug: "Start" is "Start" in Polish and German alike. Only a
                    # caption that differs somewhere else in the table and did not
                    # change here counts as stuck.
                    if x == y and x.lower() not in SAME_IN_BOTH:
                        stuck.append(f"panel {idx}: {x!r}")

            assert not stuck, f"{len(stuck)} label(s) did not switch: {stuck[:4]}"

            return f"{sum(len(b) for b in before)} labels switched pl -> de"
        finally:
            ctx.tr.set_language("en")

    def check_headers():
        from voxqueue.i18n import I18N, defect_name, LANGS

        keys = ("col_index", "col_text", "col_phonetic",
                "col_duration", "col_status", "col_notes")
        for code in ("en", "pl", "de"):
            tr = I18N(code)
            for k in keys:
                v = tr.t(k)
                assert v != k and v.strip(), f"{code}: {k} -> {v!r}"

        # "silent" equals the English word, so English cannot be tested by
        # comparing against the code — Polish can.
        for code in ("pl", "de", "ru", "ja", "zh"):
            assert defect_name("click_end", code) != "click_end", code

        return f"{len(LANGS)} langs, {len(keys)} columns"

    def check_qt():
        from PySide6.QtWidgets import QApplication
        app = QApplication.instance() or QApplication([])
        from PySide6.QtGui import QIcon
        from voxqueue.main import resource_path
        icon = QIcon(resource_path("assets/icon.png"))
        assert not icon.isNull(), "icon did not load"
        return f"qt {app.applicationName() or 'ok'}"

    def check_window():
        from PySide6.QtWidgets import QApplication
        app = QApplication.instance() or QApplication([])
        from voxqueue.core.context import Context
        from voxqueue.ui.main_window import MainWindow
        ctx = Context()
        win = MainWindow(ctx)
        win.resize(1200, 760)
        win.show()
        for _ in range(10):
            app.processEvents()
        n = win.tabs.count()
        assert n == 5, f"expected 5 tabs, got {n}"
        win.close()
        return f"{n} tabs"

    def check_flow():
        """FlowLayout must wrap, not clip.

        QHBoxLayout kept every button on one line and squeezed them, so on a
        narrow window the trailing ones were unusable. The layout is verified
        directly: at a narrow width the required height must grow, and no button
        may extend past the right edge.
        """
        from PySide6.QtWidgets import QApplication, QPushButton, QWidget
        app = QApplication.instance() or QApplication([])

        from voxqueue.ui.widgets import FlowLayout

        host = QWidget()
        flow = FlowLayout(host)
        buttons = []
        for label in ("Start", "Pause", "Cancel", "Generate selected",
                      "Retry failed", "Regenerate selected",
                      "Open folder", "Clear project"):
            b = QPushButton(label)
            flow.addWidget(b)
            buttons.append(b)
        host.show()

        wide = 1400
        narrow = 420
        h_wide = flow.heightForWidth(wide)
        h_narrow = flow.heightForWidth(narrow)

        assert h_narrow > h_wide, (
            f"no wrapping: {h_wide}px at {wide}px, {h_narrow}px at {narrow}px")

        # Lay out at the narrow width and check nothing overflows.
        from PySide6.QtCore import QRect
        flow.setGeometry(QRect(0, 0, narrow, h_narrow))
        for b in buttons:
            right = b.x() + b.width()
            assert right <= narrow + 2, (
                f"'{b.text()}' ends at {right}, width is {narrow}")

        host.close()
        return f"{h_wide}px wide -> {h_narrow}px narrow, {len(buttons)} buttons"

    def check_audio():
        import numpy as np
        from voxqueue.core import audio
        sr = 22050
        t = np.linspace(0, 1.0, sr, endpoint=False)
        tone = (0.3 * np.sin(2 * np.pi * 200 * t)).astype(np.float32)
        import tempfile
        from pathlib import Path
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "t.wav"
            audio.write_wav(p, tone, sr)
            a = audio.analyze(p)
            assert a.sample_rate == sr, a.sample_rate
            assert a.duration > 0.9, a.duration
        return f"sr={a.sample_rate} dur={a.duration:.2f}s"

    step("assets bundled", check_assets)
    step("module imports", check_imports)
    step("text splitters", check_splitter)
    step("phonetic markers", check_pronunciation)
    step("workspace save/load", check_workspace)
    step("resume from output folder", check_resume)
    step("errors-first ordering", check_sort_rank)
    step("session round-trip", check_session)
    step("pinned round-trip", check_pinned)
    step("source line round-trip", check_source_line)
    step("pronunciation edit reaches text", check_pron_edit)
    step("phonetic column is a word diff", check_phonetic_diff)
    step("search matches text and pronunciation", check_search)
    step("bulk replace rewrites markers", check_bulk_replace)
    step("stale rows sort first", check_stale)
    step("retry picks up failed and stale", check_retry_covers)
    step("every key resolves in every language", check_i18n_complete)
    step("Polish hints are really Polish", check_polish_hints)
    step("written form reflects the text", check_written_form)
    step("settings widgets are on screen", check_settings_visible)
    step("language switch rewrites every label", check_labels_translate)
    step("stale rows stay visible until OK", check_stale_lifetime)
    step("every tab survives a language switch", check_language_switch)
    step("translations", check_headers)
    step("audio pipeline", check_audio)
    step("qt + icon", check_qt)
    step("main window", check_window)
    step("buttons wrap at any width", check_flow)

    print()
    if failures:
        print(f"FAILED: {failures} check(s)")
        return 1
    print("ALL OK")
    return 0



def check_display_stack() -> str | None:
    """Return a human-readable problem when the window cannot open, else None.

    Qt dies with a bare abort and a core dump when its xcb platform plugin cannot
    load, which tells the user nothing: the terminal shows "Aborted" and the window
    never appears. The two causes are always the same — a missing system library, or
    no display to draw on — so both are checked before Qt is started and reported in
    plain words with the exact command that fixes them.

    Only Linux needs this. Windows and macOS bundle their platform plugin with the
    framework and cannot hit it.
    """
    import os
    import platform

    if platform.system() != "Linux":
        return None

    # A non-interactive run (tests, CI) sets its own platform and needs no screen.
    chosen = os.environ.get("QT_QPA_PLATFORM", "")
    if chosen in ("offscreen", "minimal", "linuxfb", "eglfs"):
        return None

    if not os.environ.get("DISPLAY") and not os.environ.get("WAYLAND_DISPLAY"):
        return (
            "No display found: DISPLAY and WAYLAND_DISPLAY are both unset.\n"
            "Run this from a desktop session, or over SSH with 'ssh -X'."
        )

    # A frozen build carries these inside the bundle, so the system copy is
    # irrelevant: PyInstaller puts the bundle directory on LD_LIBRARY_PATH and Qt
    # loads them from there. Checking the system anyway would refuse to start a
    # package that works perfectly.
    if getattr(sys, "frozen", False):
        return None

    # Running from source: then the system really does have to provide them.
    # Qt 6.5+ links these into the xcb plugin; any one missing makes it refuse to load.
    needed = {
        "xcb-cursor": "libxcb-cursor0",
        "xcb-icccm": "libxcb-icccm4",
        "xcb-image": "libxcb-image0",
        "xcb-keysyms": "libxcb-keysyms1",
        "xcb-render-util": "libxcb-render-util0",
        "xkbcommon-x11": "libxkbcommon-x11-0",
    }

    import ctypes.util

    missing = [
        package for soname, package in needed.items()
        if ctypes.util.find_library(soname) is None
    ]

    if not missing:
        return None

    return (
        "Missing system libraries, so the window cannot open:\n"
        f"    {' '.join(missing)}\n\n"
        "Install them with:\n"
        f"    sudo apt install {' '.join(missing)}\n\n"
        "Fedora:  sudo dnf install " + " ".join(_fedora_name(p) for p in missing) + "\n"
        "Arch:    sudo pacman -S " + " ".join(_arch_name(p) for p in missing) + "\n\n"
        "Nothing else is required — the bundle carries Python and Qt itself."
    )


def _fedora_name(deb_package: str) -> str:
    """Debian package name to the RPM one, for the handful this app needs."""
    return {
        "libxcb-cursor0": "xcb-util-cursor",
        "libxcb-icccm4": "xcb-util-wm",
        "libxcb-image0": "xcb-util-image",
        "libxcb-keysyms1": "xcb-util-keysyms",
        "libxcb-render-util0": "xcb-util-renderutil",
        "libxkbcommon-x11-0": "libxkbcommon-x11",
    }.get(deb_package, deb_package)


def _arch_name(deb_package: str) -> str:
    return {
        "libxcb-cursor0": "xcb-util-cursor",
        "libxcb-icccm4": "xcb-util-wm",
        "libxcb-image0": "xcb-util-image",
        "libxcb-keysyms1": "xcb-util-keysyms",
        "libxcb-render-util0": "xcb-util-renderutil",
        "libxkbcommon-x11-0": "libxkbcommon-x11",
    }.get(deb_package, deb_package)


def main() -> int:
    # Required for frozen builds: without it a frozen process that spawns
    # another (multiprocessing on Windows) re-runs the whole app.
    multiprocessing.freeze_support()

    if "--selftest" in sys.argv:
        # Exit without unwinding Qt.
        #
        # The self-test builds a QApplication to check that the window constructs,
        # and CPython then runs the Qt destructors during interpreter shutdown.
        # With QtMultimedia loaded but no PipeWire backend those destructors
        # segfault about one run in three — after "ALL OK" has already printed, so
        # the result is correct and the process still dies with SIGSEGV and a core
        # dump. os._exit skips that teardown: the checks have run, their result is
        # flushed, and the exit status is the one they produced.
        code = selftest()
        sys.stdout.flush()
        sys.stderr.flush()
        os._exit(code)

    # Before Qt is imported: a missing xcb library aborts the process with no
    # message at all, and the user is left staring at a terminal that says "Aborted".
    problem = check_display_stack()
    if problem:
        print("VoxQueue cannot start.\n", file=sys.stderr)
        print(problem, file=sys.stderr)
        print("\nTo verify the bundle itself:  VoxQueue --selftest", file=sys.stderr)
        return 1

    from voxqueue.main import main as app_main
    return app_main()


if __name__ == "__main__":
    sys.exit(main())
