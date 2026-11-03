//
//The AI windows - the chat and its settings, the MCP server among them - and the PDK manager, as windows rather than
//popups: opened in the middle of the browser window, dragged by their header and resized from their corner.
//
//**Ported from GraphDBViewer's FloatingDialogInterop.js**, which is the same windows over the same chat. What
//changed is what had to: the names, the store's prefix, and how high a window stacks - see raise.
//
//Where a window is, is written straight onto its inline style and never goes through Blazor. A drag fires
//pointermove at the screen's rate, and a render a frame would stutter and fight the pointer; Blazor owns what is
//in the window, this owns where the window is.
//
window.floatingWindow = (function () {
    const storePrefix = 'gdsviewer.window.';

    //Keep at least this much of a window on screen, always.
    const margin = 8;

    //
    //Two heights, and the window last touched takes the upper one.
    //
    //Under the app's popups - About, a layer's settings and the rest sit at 990 - so one of those opened while a
    //window is up comes out in front of it, the way GraphDBViewer's windows sat under its own popups. GraphDBViewer
    //raised a window one higher on every press, which in a page that keeps its popups at a fixed height would climb
    //past them after a few dozen clicks.
    //
    const below = 980;
    const above = 981;
    const attached = [];

    function clamp(value, least, most) {
        if (most < least)
            return least;

        return Math.max(least, Math.min(most, value));
    }

    //Never left where it cannot be grabbed: not off the right or the bottom, and never with its header above the
    //top of the window. Coming back onto a smaller screen than it was left on is the usual way that happens.
    function place(panel, left, top) {
        const width = panel.offsetWidth;

        panel.style.left = clamp(left, margin - width + 120, window.innerWidth - margin - 120) + 'px';
        panel.style.top = clamp(top, margin, window.innerHeight - margin - 40) + 'px';
    }

    //Where a window should sit when it opens: wholly on screen, which is stricter than dragging allows. One
    //dragged half off the bottom is somebody's choice; one that opens there has a resize grip nobody can reach.
    function fit(panel) {
        const room = window.innerHeight - margin - panel.offsetHeight;

        place(panel, panel.offsetLeft, Math.max(margin, Math.min(panel.offsetTop, room)));
    }

    //Collapses the box, reads what its words want, then takes that up to a ceiling - past which it scrolls, so a
    //long paste cannot grow until there is no conversation left to see.
    function grow(box) {
        if (!box)
            return;

        //Nothing to measure: the window is hidden, and sizing to nothing would stick.
        if (box.scrollHeight === 0 && box.clientHeight === 0)
            return;

        const most = box.gdsMostHeight || 200;

        box.style.height = 'auto';

        const needed = box.scrollHeight;

        box.style.height = Math.min(needed, most) + 'px';

        if (needed > most)
            box.style.overflowY = 'auto';
        else
            box.style.overflowY = 'hidden';
    }

    function save(key, panel) {
        if (!key)
            return;

        //A hidden window measures nothing, and the observer below fires when one is hidden. Recorded, that was a
        //window that came back at its smallest - GraphDBViewer's "they get shrunk a lot when they first open".
        if (!panel || !panel.offsetWidth || !panel.offsetHeight)
            return;

        try {
            //Its size alone: where it was is not put back - every window opens in the middle, see center.
            const geometry = {
                width: panel.offsetWidth,
                height: panel.offsetHeight,

                //Whether the size is somebody's, set with the grip - see restore.
                sized: panel.dataset.sized === 'yes'
            };

            localStorage.setItem(storePrefix + key, JSON.stringify(geometry));
        }
        catch (e) { }//private browsing, a full quota: a forgotten position is not worth failing over
    }

    function restore(key) {
        if (!key)
            return null;

        try {
            const stored = localStorage.getItem(storePrefix + key);

            if (!stored)
                return null;

            const geometry = JSON.parse(stored);

            //
            //**Only a size somebody chose is put back** - one set with the grip. The size a window merely had, its
            //default as it opened or was moved, was recorded too, and kept a default from ever changing for anybody
            //who had opened the window once: the chat's was made wide enough for the row under its box to be one line
            //on 2026-10-17, and stayed narrow in every browser that had seen it. A size missing, or too small to be
            //one somebody chose, is no size either, and the window opens at its default.
            //
            if (geometry.sized !== true || !(geometry.width > 200) || !(geometry.height > 150))
                return null;

            return geometry;
        }
        catch (e) {
            return null;
        }
    }

    //
    //Where a window opens, each time it appears: across, the middle of the browser window; down, the middle of the room
    //under the bar named - the toolbar, so the spark that put it up, and Undo, are never under it - or of the whole
    //browser window when none is. Shortened first if it is taller than that room.
    //
    //**Every window, since 2026-10-21** (asked for). Only the PDK manager opened in the middle; the AI windows and the
    //MCP server's opened at the right of the browser window, under the bar, and then wherever they had been left -
    //which on a window narrower than the screen, or with the page not full screen, was off to one side of the app,
    //hanging past its edge. Where one was left is not kept either: that was the same place for anybody who never moved
    //it. Down the middle of the whole browser window, a tall one covered the bar and the spark that closes it.
    //
    function center(panel, below) {
        let top = margin;

        if (below) {
            const bar = document.querySelector(below);

            if (bar)
                top = Math.round(bar.getBoundingClientRect().bottom) + margin;
        }

        const room = window.innerHeight - top - margin;

        if (panel.offsetHeight > room && room >= 260)
            panel.style.height = room + 'px';

        place(panel, Math.round((window.innerWidth - panel.offsetWidth) / 2), top + Math.max(0, Math.round((room - panel.offsetHeight) / 2)));
        fit(panel);
    }

    function raise(panel) {
        for (const other of attached)
            other.style.zIndex = below;

        panel.style.zIndex = above;
    }

    //One drag, from a press to its release: moves or sizes the window as the pointer goes, and records where it
    //ended up.
    function drag(ev, key, panel, moved) {
        const startX = ev.clientX;
        const startY = ev.clientY;

        const onMove = (move) => {
            moved(move.clientX - startX, move.clientY - startY);
            move.preventDefault();
        };

        const onUp = () => {
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
            window.removeEventListener('pointercancel', onUp);
            document.body.style.userSelect = '';
            save(key, panel);
        };

        document.body.style.userSelect = 'none';
        window.addEventListener('pointermove', onMove);
        window.addEventListener('pointerup', onUp);
        window.addEventListener('pointercancel', onUp);
        ev.preventDefault();
    }

    return {
        //Called each time a window appears: in the middle and in front, every time - see center, and `below` there. The
        //first call sets it up as well, at a size somebody set with the grip if there is one.
        attach: function (panel, handle, grip, key, below) {
            if (!panel || !handle)
                return;

            if (panel.dataset.floating === 'on') {
                center(panel, below);
                raise(panel);

                return;
            }

            panel.dataset.floating = 'on';
            attached.push(panel);

            const saved = restore(key);

            if (saved) {
                panel.style.width = saved.width + 'px';
                panel.style.height = saved.height + 'px';
                panel.dataset.sized = 'yes';
            }

            center(panel, below);
            raise(panel);

            //Any press inside brings it forward, so the two windows can be swapped between.
            panel.addEventListener('pointerdown', () => raise(panel));

            handle.addEventListener('pointerdown', (ev) => {
                //Only a drag of the bar itself: the close button lives in the header too, and a drag from it should
                //press it rather than move the window.
                if (ev.button !== 0 || ev.target.closest('button, a, input, select, textarea'))
                    return;

                const left = panel.offsetLeft;
                const top = panel.offsetTop;

                drag(ev, key, panel, (dx, dy) => place(panel, left + dx, top + dy));
            });

            if (grip) {
                grip.addEventListener('pointerdown', (ev) => {
                    if (ev.button !== 0)
                        return;

                    const width = panel.offsetWidth;
                    const height = panel.offsetHeight;

                    ev.stopPropagation();

                    //From here the size is somebody's, and is put back - see restore.
                    panel.dataset.sized = 'yes';

                    drag(ev, key, panel, (dx, dy) => {
                        //The smallest a window goes comes from the stylesheet, so one place decides it.
                        const style = getComputedStyle(panel);
                        const narrowest = parseInt(style.minWidth, 10) || 320;
                        const shortest = parseInt(style.minHeight, 10) || 200;

                        panel.style.width = Math.max(narrowest, width + dx) + 'px';
                        panel.style.height = Math.max(shortest, height + dy) + 'px';
                    });
                });
            }

            //A window can change size without the grip - a zoom, a phone turned on its side - so what it settles at
            //is watched rather than only recorded at the end of a drag.
            if (window.ResizeObserver) {
                let pending = 0;

                const observer = new ResizeObserver(() => {
                    clearTimeout(pending);
                    pending = setTimeout(() => save(key, panel), 250);
                });

                observer.observe(panel);
            }
        },

        //
        //Makes a textarea the chat's box: one line until there is more than one line in it, and Enter to send
        //rather than to start a new line.
        //
        //The newline is refused here rather than in Blazor, because a Blazor handler's preventDefault is fixed when
        //the component renders, not when the key is pressed - by the time C# could refuse it, the browser has typed
        //it. The keydown still reaches Blazor afterwards, which is what sends.
        //
        attachComposer: function (box, most) {
            if (!box || box.gdsComposer)
                return;

            box.addEventListener('keydown', function (e) {
                //isComposing: an input method is partway through a word, where Enter picks a candidate.
                if (e.isComposing)
                    return;

                //While the slash commands' list is up its keys move through it, rather than the caret - see AskAi.razor.
                if (box.dataset.slashMenu === 'open' && ['ArrowUp', 'ArrowDown', 'Tab', 'Escape'].indexOf(e.key) >= 0) {
                    e.preventDefault();

                    return;
                }

                if (e.key !== 'Enter' || e.shiftKey)
                    return;

                e.preventDefault();
            });

            box.gdsComposer = true;
            box.gdsMostHeight = most || 200;

            box.addEventListener('input', function () {
                grow(box);
            });

            grow(box);
        },

        //
        //Files pasted into the chat's box go with the next question, through the paperclip's own input - the way a
        //drop on the conversation does (see fileDrop.js) - so the window has one way in. Ported from
        //ConfigurationRuleEditor's initComposeBox, 2026-10-29.
        //
        //**Words before a picture**: a spreadsheet copies a picture of the cells beside their text, and the text is
        //what was meant, so a paste with text in it is left to the box. **A screenshot is named for when it was
        //pasted**: every one arrives as image.png, and two of them on chips, or a question about the second one, need
        //telling apart.
        //
        attachFiles: function (box, inputId) {
            if (!box || box.gdsFilesAttached)
                return;

            box.gdsFilesAttached = true;

            box.addEventListener('paste', function (e) {
                const data = e.clipboardData;

                if (!data || !data.files || data.files.length === 0)
                    return;

                if (data.getData('text/plain'))
                    return;

                const input = document.getElementById(inputId);

                //Not before Blazor's own listener is on, or the change would carry nothing - see the same flag in fileDrop.js.
                if (!input || input.disabled || input._blazorInputFileNextFileId === undefined)
                    return;

                const stamp = new Date().toTimeString().slice(0, 8).replace(/:/g, '');
                const transfer = new DataTransfer();

                Array.from(data.files).forEach(function (file, index) {
                    let named = file;

                    if (/^image\.\w+$/.test(file.name)) {
                        let suffix = '';

                        if (index > 0)
                            suffix = '-' + (index + 1);

                        named = new File([file], 'pasted-' + stamp + suffix + '.' + file.name.split('.').pop(), { type: file.type });
                    }

                    transfer.items.add(named);
                });

                e.preventDefault();

                input.files = transfer.files;
                input.dispatchEvent(new Event('change', { bubbles: true }));
            });
        },

        //Measures again when the words changed without anybody typing: a box first sized while its window was
        //hidden, or one C# has just emptied.
        growComposer: function (box) {
            grow(box);
        },

        //Empties it and shrinks it, in that order, so it is measured against what is left.
        resetComposer: function (box) {
            if (!box)
                return;

            box.value = '';
            grow(box);
        },

        //Keeps a scrolling box on its last line, for a conversation that grows downward.
        scrollToEnd: function (box) {
            if (!box)
                return;

            box.scrollTop = box.scrollHeight;
        },

        //
        //The dividers between the AI assistant's chats, its conversation and its files (2026-10-31): dragging one sizes the panel on
        //its side, kept on this computer for the next visit. The widths are custom properties on the row rather than
        //styles on the panels, which Blazor takes away and puts back as they collapse and open - the row stays, and so does
        //what is set on it. A divider is there only while its panel is; the arrows are buttons of their own.
        //
        attachSplits: function (row) {
            if (!row || row.gdsSplits)
                return;

            row.gdsSplits = true;

            const panels = { chats: '.aiChats', files: '.aiFiles' };

            Object.keys(panels).forEach(function (name) {
                let kept = null;

                try {
                    kept = window.localStorage.getItem('gdsviewer.aiPanel.' + name);
                }
                catch (e) { }

                if (kept && /^\d+$/.test(kept))
                    row.style.setProperty('--ai-' + name + '-width', kept + 'px');
            });

            row.addEventListener('pointerdown', function (e) {
                const split = e.target.closest('.aiSplit');

                if (!split || !row.contains(split) || e.button !== 0)
                    return;

                const name = split.dataset.split;
                const panel = row.querySelector(panels[name]);

                if (!panel)
                    return;

                e.preventDefault();

                //The chats are left of their divider and grow as it goes right; the files are right of theirs.
                let sign = 1;

                if (name === 'files')
                    sign = -1;

                const startX = e.clientX;
                const startWidth = panel.getBoundingClientRect().width;
                const most = Math.max(160, row.getBoundingClientRect().width * 0.6);
                let width = startWidth;

                split.setPointerCapture(e.pointerId);
                split.classList.add('aiSplitDragging');

                function move(m) {
                    width = Math.round(Math.max(120, Math.min(most, startWidth + (sign * (m.clientX - startX)))));
                    row.style.setProperty('--ai-' + name + '-width', width + 'px');
                }

                function done() {
                    split.removeEventListener('pointermove', move);
                    split.removeEventListener('pointerup', done);
                    split.removeEventListener('pointercancel', done);
                    split.classList.remove('aiSplitDragging');

                    try {
                        window.localStorage.setItem('gdsviewer.aiPanel.' + name, String(width));
                    }
                    catch (e) { }
                }

                split.addEventListener('pointermove', move);
                split.addEventListener('pointerup', done);
                split.addEventListener('pointercancel', done);
            });
        }
    };
})();
