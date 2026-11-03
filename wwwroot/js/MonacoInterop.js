//Self-hosted Monaco editor interop (no npm/bundler). The AMD loader under wwwroot/lib/monaco is
//injected lazily on first use — after the other classic scripts have already attached to window —
//so Monaco's global `define`/`require` can't hijack them.

let monacoEditor = null;
let monacoLoadPromise = null;
let monacoLanguageId = null;

//Absolute base URL of the app, honoring <base href> in dev and under a deployed subpath.
function monacoBaseUrl() {
    let href = document.baseURI;

    if (!href.endsWith('/'))
        href += '/';

    return href;
}

function monacoVsUrl() {
    return monacoBaseUrl() + 'lib/monaco/vs';
}

//Injects a <script> and resolves once it has loaded.
function loadMonacoScript(src) {
    return new Promise(function (resolve, reject) {
        const script = document.createElement('script');
        script.src = src;
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    });
}

//Loads the AMD loader + Monaco editor core exactly once.
function ensureMonaco() {
    if (monacoLoadPromise)
        return monacoLoadPromise;

    const vs = monacoVsUrl();

    //The editor web worker is loaded same-origin via a tiny bootstrap blob that, in turn,
    //importScripts the real workerMain with the right base URL.
    window.MonacoEnvironment = {
        getWorkerUrl: function () {
            const js = "self.MonacoEnvironment={baseUrl:'" + monacoBaseUrl() + "lib/monaco/'};" +
                "importScripts('" + vs + "/base/worker/workerMain.js');";

            return URL.createObjectURL(new Blob([js], { type: 'text/javascript' }));
        }
    };

    monacoLoadPromise = loadMonacoScript(vs + '/loader.js').then(function () {
        return new Promise(function (resolve) {
            window.require.config({ paths: { vs: vs } });
            window.require(['vs/editor/editor.main'], resolve);
        });
    });

    return monacoLoadPromise;
}

//
//The two themes every editor here draws in - light, and dark for the app's dark mode - with the GDS text's own token
//colors in them. Once: a theme is the page's, not an editor's, and defining one again replaces it for every editor.
//Out of registerGdsLanguage since a PDK's rules are edited too, possibly before the text view has ever opened.
//
let editorThemesDefined = false;

function defineEditorThemes() {
    if (editorThemesDefined)
        return;

    editorThemesDefined = true;

    //Define a new theme that contains only rules that match this language.
    monaco.editor.defineTheme("defaultGDS", {
        base: "vs",
        inherit: true,
        rules: [
            { token: "colon", foreground: "9400ff" },
            { token: "typeKeywords", foreground: "03b1b9" },
            { token: "number.float", foreground: "ed7e00" },
            //{ token: "number", foreground: "ffbc70" }, //inherit default from 'number'

            //A layermap's: its pair, its name, a color and a header row - see registerLayermapLanguage.
            { token: "type.pair", foreground: "267f99", fontStyle: "bold" },
            { token: "layername", foreground: "0451a5" },
            { token: "color", foreground: "af00db" },
            { token: "keyword.header", foreground: "6c757d", fontStyle: "italic" },
            { token: "keyword.fill", foreground: "795e26" },
        ],
        colors: {
            "editor.foreground": "#000000",
        },
    });

    //The same three token colors lifted for a dark ground, for the app's dark mode. See editorThemeName.
    monaco.editor.defineTheme("defaultGDSDark", {
        base: "vs-dark",
        inherit: true,
        rules: [
            { token: "colon", foreground: "c792ff" },
            { token: "typeKeywords", foreground: "4dd6de" },
            { token: "number.float", foreground: "ffa040" },
            { token: "type.pair", foreground: "4ec9b0", fontStyle: "bold" },
            { token: "layername", foreground: "9cdcfe" },
            { token: "color", foreground: "d38cf0" },
            { token: "keyword.header", foreground: "9aa0a6", fontStyle: "italic" },
            { token: "keyword.fill", foreground: "dcdcaa" },
        ],
        colors: {
            "editor.foreground": "#e4e6e9",
        },
    });
}

//Registers the custom GDS language, its theme and its completion provider. Switching away from the
//text editor view and back re-creates the component, so this has to stay idempotent — re-registering
//would stack up a second completion provider and duplicate every suggestion.
function registerGdsLanguage(language) {
    if (monacoLanguageId === language)
        return;

    monacoLanguageId = language;

    monaco.languages.register({ id: language });

    //Register a tokens provider for the language.
    monaco.languages.setMonarchTokensProvider(language, {
        typeKeywords: getKeyWords(),

        tokenizer: {
            root: [
                //Match all the record keywords.
                [/[A-Z_$][\w$]*/, { cases: { '@typeKeywords': 'typeKeywords' } }],

                //Match decimal numbers.
                [/\d*\.\d+([eE][\-+]?\d+)?/, 'number.float'],
                //Match numbers that aren't next to a special or regular character.
                [/\b\d+\b(?![a-zA-Z_0-9])/, 'number'],

                //Delimiter: after number because of .\d floats. //Not needed.
                //[/[;,.]/, 'delimiter'],

                //Match colon.
                [/:/, 'colon'],
            ],
        },
    });

    defineEditorThemes();

    //Add completitions/suggestions.
    monaco.languages.registerCompletionItemProvider(language, {
        provideCompletionItems: function (model, position) {
            var word = model.getWordUntilPosition(position);
            var range = {
                startLineNumber: position.lineNumber,
                endLineNumber: position.lineNumber,
                startColumn: word.startColumn,
                endColumn: word.endColumn,
            };

            return {
                suggestions: createDependencyProposals(range)
            };
        }
    });
}

window.InitializeMonaco = async function (text, language) {
    await ensureMonaco();

    registerGdsLanguage(language);

    //The host div is new every time the text editor view is re-entered, so drop the old instance
    //instead of leaking it and its models.
    if (monacoEditor != null)
        monacoEditor.dispose();

    //And the marks on it, which belonged to that instance.
    cellMarks = null;
    cellNames = [];
    faultMark = null;
    placementMarks = null;

    //Create the editor instance.
    monacoEditor = monaco.editor.create(document.getElementById('gdsTextEditor'), {
        theme: editorThemeName(),
        value: text,
        language: language,
        automaticLayout: true,
        wordWrap: "on"
    });
}

//
//A rule deck's language, for the PDK manager's editor - the format GdsII's DrcDeck reads: `layer`, `derive` and `rule`
//lines, the checks a rule names and the operators a derived layer is made with, numbers and layer pairs, quoted
//descriptions and # comments. Coloring only: the deck's own reader is what says whether a line means anything. Once.
//
let deckLanguageRegistered = false;

function registerDeckLanguage() {
    if (deckLanguageRegistered)
        return;

    deckLanguageRegistered = true;

    monaco.languages.register({ id: 'gdsdeck' });

    monaco.languages.setMonarchTokensProvider('gdsdeck', {
        ignoreCase: true,
        statements: ['layer', 'derive', 'rule'],
        checks: ['width', 'space', 'notch', 'enclosure', 'area', 'holearea', 'density', 'antenna', 'offgrid'],
        words: ['and', 'or', 'not', 'xor', 'except', 'window', 'step', 'euclidean', 'square', 'projection'],

        tokenizer: {
            root: [
                //A comment is a line of its own, as the deck is read (DrcDeck.Parse); a # after a rule is not one, and is
                //marked as what the parser will refuse rather than colored as though it were skipped (the 2026-10-21 audit).
                [/^\s*#.*$/, 'comment'],
                [/#.*$/, 'invalid'],
                [/"[^"]*"/, 'string'],
                [/\d+\/\d+/, 'number'],
                [/-?\d+(\.\d+)?/, 'number'],
                [/[a-zA-Z_][\w.]*/, { cases: { '@statements': 'keyword', '@checks': 'type', '@words': 'keyword', '@default': 'identifier' } }],
                [/=/, 'delimiter'],
            ],
        },
    });
}

//
//A layermap's language, for the PDK manager's layers as text (asked for 2026-11-01) - the CSV GdsII's LayerNames reads:
//# comments on lines of their own, a header row of words, and rows of layer,datatype,name then the optional columns.
//The pair and the name are matched together at the start of a row, so a layer named "via" is a name and not the role.
//A color is colored, and gets Monaco's own swatch beside it, whose picker writes the color back. Coloring only: the
//layermap's own reader is what says whether a row means anything. Once.
//
let layermapLanguageRegistered = false;

const layermapColor = /#[0-9a-fA-F]{6}\b/g;

function registerLayermapLanguage() {
    if (layermapLanguageRegistered)
        return;

    layermapLanguageRegistered = true;

    monaco.languages.register({ id: 'gdslayermap' });

    monaco.languages.setMonarchTokensProvider('gdslayermap', {
        ignoreCase: true,
        roles: ['conductor', 'via', 'none'],
        fills: ['dots', 'squares', 'grid', 'dashes', 'diagonal', 'backdiagonal', 'crosshatch'],

        tokenizer: {
            root: [
                [/^\s*#.*$/, 'comment'],

                //A header row: its first two fields words, which is how the reader knows one (isHeader).
                [/^\s*[a-zA-Z][^,]*,\s*[a-zA-Z].*$/, 'keyword.header'],

                //The pair and the name, at the start of a row.
                [/^(\s*)(\d+)(\s*,\s*)(\d+)(\s*,\s*)("[^"]*"|[^,]*)/, ['white', 'type.pair', 'delimiter', 'type.pair', 'delimiter', 'layername']],

                [/#[0-9a-fA-F]{6}\b/, 'color'],
                [/[\w.\-\/]+\.(png|jpe?g|gif|webp|svg|bmp)\b/, 'string'],
                [/-?\d+/, 'number'],
                [/[a-zA-Z]+/, { cases: { '@roles': 'keyword', '@fills': 'keyword.fill', '@default': 'invalid' } }],
                [/,/, 'delimiter'],
            ],
        },
    });

    //The swatch beside each color, and what its picker writes: the same #rrggbb a row carries.
    monaco.languages.registerColorProvider('gdslayermap', {
        provideDocumentColors: function (model) {
            const colors = [];

            for (let line = 1; line <= model.getLineCount(); line++) {
                const text = model.getLineContent(line);

                if (/^\s*#/.test(text))
                    continue;

                for (const match of text.matchAll(layermapColor)) {
                    const hex = match[0];

                    colors.push({
                        color: {
                            red: parseInt(hex.substr(1, 2), 16) / 255,
                            green: parseInt(hex.substr(3, 2), 16) / 255,
                            blue: parseInt(hex.substr(5, 2), 16) / 255,
                            alpha: 1
                        },
                        range: new monaco.Range(line, match.index + 1, line, match.index + 8)
                    });
                }
            }

            return colors;
        },

        provideColorPresentations: function (model, info) {
            const part = value => Math.round(value * 255).toString(16).padStart(2, '0');

            return [{ label: '#' + part(info.color.red) + part(info.color.green) + part(info.color.blue) }];
        }
    });
}

//
//Editors of a text other than the file's, as many as are open, each under an id of its own - apart from the text view's
//one editor, which InitializeMonaco replaces whole. For a PDK's rules in the PDK manager, which can be open beside the
//text view. The host is an element of the caller's; automaticLayout, since the window it sits in starts out hidden.
//
let textEditors = {};
let nextTextEditor = 1;

window.createTextEditor = async function (host, text, language) {
    await ensureMonaco();

    defineEditorThemes();

    if (language === 'gdsdeck')
        registerDeckLanguage();
    else if (language === 'gdslayermap')
        registerLayermapLanguage();

    const id = nextTextEditor++;

    textEditors[id] = monaco.editor.create(host, {
        theme: editorThemeName(),
        value: text,
        language: language,
        automaticLayout: true,
        minimap: { enabled: false },
        wordWrap: "off",
        scrollBeyondLastLine: false,
        fontSize: 12
    });

    return id;
}

window.getTextEditorValue = function (id) {
    const editor = textEditors[id];

    if (editor == null)
        return null;

    return editor.getValue();
}

//Puts a text in, unless it is the one there already - which would throw away where the reader was in it, and the undo.
window.setTextEditorValue = function (id, text) {
    const editor = textEditors[id];

    if (editor == null || editor.getValue() === text)
        return;

    editor.setValue(text);
}

window.disposeTextEditor = function (id) {
    const editor = textEditors[id];

    if (editor == null)
        return;

    editor.dispose();

    delete textEditors[id];
}

//
//What one of the assistant's changes did to the file's text, as a diff on its card - asked for 2026-10-21: whichever
//view a change was asked for from, the record dump is what changed. The lines taken away over the lines put in, one
//text rather than two side by side, which a card is too narrow for; numbered as the text view numbers them, though only
//the lines round the change are handed in (see TextChange in the app). Read-only. Under the id of the element it is in,
//made once, and let go all together when the conversation is cleared.
//
//**As tall as what it shows**, up to TEXT_DIFF_MOST_LINES and then it scrolls: first as tall as both texts, the most it
//could need, and cut down to what it does show once the editor has measured that - so a conversation scrolled to its
//end when the card came in is still at its end after.
//
const TEXT_DIFF_MOST_LINES = 14;
const TEXT_DIFF_LINE_PIXELS = 18;

let textDiffs = {};

window.showTextDiff = async function (hostId, before, after, firstLine) {
    if (textDiffs[hostId] != null)
        return;

    //Held while Monaco loads, so a second render does not make a second one.
    textDiffs[hostId] = {};

    await ensureMonaco();

    defineEditorThemes();
    registerGdsLanguage('GDS');

    const host = document.getElementById(hostId);

    //Cleared, or the card gone, while Monaco was loading.
    if (host == null || textDiffs[hostId] == null) {
        delete textDiffs[hostId];

        return;
    }

    fitTextDiff(host, (lineCount(before) + lineCount(after)) * TEXT_DIFF_LINE_PIXELS);

    //Room for the widest number and one more, since the lines kept are numbered twice - before and after - side by side.
    const widest = String(firstLine + Math.max(lineCount(before), lineCount(after))).length;

    const editor = monaco.editor.createDiffEditor(host, {
        theme: editorThemeName(),
        readOnly: true,
        originalEditable: false,
        renderSideBySide: false,
        enableSplitViewResizing: false,
        automaticLayout: true,
        minimap: { enabled: false },
        scrollBeyondLastLine: false,
        renderOverviewRuler: false,
        overviewRulerLanes: 0,
        lineNumbers: function (number) {
            return String(number + firstLine - 1);
        },
        lineNumbersMinChars: widest + 1,
        folding: false,
        glyphMargin: false,
        contextmenu: false,
        renderLineHighlight: 'none',
        fontSize: 12,
        lineHeight: TEXT_DIFF_LINE_PIXELS,
        wordWrap: 'off',
        //A wheel over a diff that has nowhere to scroll goes on to the conversation it is in.
        scrollbar: { alwaysConsumeMouseWheel: false, verticalScrollbarSize: 8, horizontalScrollbarSize: 8 }
    });

    const original = monaco.editor.createModel(before, 'GDS');
    const modified = monaco.editor.createModel(after, 'GDS');

    editor.setModel({ original: original, modified: modified });

    //What it shows, as the editor measures it: the text after, the lines taken away shown over the lines that took
    //their place, and the scrollbar under a line too long for the card.
    editor.getModifiedEditor().onDidContentSizeChange(function (size) {
        fitTextDiff(host, size.contentHeight);
    });

    textDiffs[hostId] = { editor: editor, original: original, modified: modified };
}

function lineCount(text) {
    return text.split('\n').length;
}

function fitTextDiff(host, pixels) {
    const most = TEXT_DIFF_MOST_LINES * TEXT_DIFF_LINE_PIXELS;

    host.style.height = Math.max(TEXT_DIFF_LINE_PIXELS, Math.min(most, pixels)) + 'px';
}

window.disposeTextDiffs = function () {
    for (const id in textDiffs) {
        const diff = textDiffs[id];

        if (diff.editor == null)
            continue;

        diff.editor.dispose();
        diff.original.dispose();
        diff.modified.dispose();
    }

    textDiffs = {};
}

///
///The editor's theme for the page's: the dark one under a dark page.
///
///Asked of the page rather than of theme.js's storage, so the editor matches what is on screen even if the
///two were ever to disagree. The editor draws inside a canvas of its own, which no stylesheet reaches.
///
function editorThemeName() {
    if (document.documentElement.dataset.theme === 'dark')
        return "defaultGDSDark";

    return "defaultGDS";
}

///Called by theme.js when the page changes theme, so an editor already open follows it at once.
window.applyEditorTheme = function () {
    if (window.monaco != null && monaco.editor != null)
        monaco.editor.setTheme(editorThemeName());
}

window.SetMonacoContent = function (text) {
    if (monacoEditor == null)
        return;

    monacoEditor.setValue(text);

    //A fault's lines were lines of the text this replaced.
    if (faultMark != null)
        faultMark.clear();
}

window.GetMonacoContent = function () {
    if (monacoEditor == null)
        return "";

    return monacoEditor.getValue();
}

///
///Where each cell begins in the text, marked on its line - the list beside the editor goes to these.
///
///**Marks rather than a search.** The list comes from the file's cells, and each cell's first line is handed over
///once, as the text goes in - see linesOf in TextEditor.razor. The editor moves a mark with every edit above it, so
///going to a cell stays a lookup however much has been typed since. An outline built from the text would read every
///line of it again after each pause in typing: a hundred and fifty thousand of them for the RGB mixer.
///
///A thin rule across the editor at each one, and a tick in the margin of the scrollbar, so the cells can be seen
///going past as well as gone to. See .textCellStart.
///
let cellMarks = null;
let cellNames = [];

window.MarkMonacoCells = function (names, lines) {
    if (monacoEditor == null)
        return;

    const marks = lines.map(function (line) {
        return {
            range: new monaco.Range(line, 1, line, 1),
            options: {
                isWholeLine: true,
                className: 'textCellStart',
                stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges,
                overviewRuler: { color: '#1b6ec2', position: monaco.editor.OverviewRulerLane.Left }
            }
        };
    });

    if (cellMarks == null)
        cellMarks = monacoEditor.createDecorationsCollection(marks);
    else
        cellMarks.set(marks);

    cellNames = names;
}

///
///Goes to where a cell begins, which is wherever its mark has been moved to: the line at the top of the editor with
///the cursor at its start, so typing goes on there. False for a cell with no mark.
///
window.GoToMonacoCell = function (name) {
    if (monacoEditor == null || cellMarks == null)
        return false;

    const at = cellNames.indexOf(name);

    if (at < 0)
        return false;

    const range = cellMarks.getRange(at);

    if (range == null)
        return false;

    const line = range.startLineNumber;

    monacoEditor.setPosition({ lineNumber: line, column: 1 });
    monacoEditor.revealLineNearTop(line);
    monacoEditor.focus();

    return true;
}

///
///**The lines of the one thing gone to**, lit: a shape a fault is on, or a placement pressed in the cell list. One at a
///time, the last one gone to, the way the 2D view frames one fault at a time - so this is one mark, moved.
///
let faultMark = null;

function lightLines(first, end, className, color) {
    const mark = [{
        range: new monaco.Range(first, 1, end, 1),
        options: {
            isWholeLine: true,
            className: className,
            overviewRuler: { color: color, position: monaco.editor.OverviewRulerLane.Right }
        }
    }];

    if (faultMark == null)
        faultMark = monacoEditor.createDecorationsCollection(mark);
    else
        faultMark.set(mark);
}

///
///**A shape's lines, lit**, for a fault the rules found on it: `from` to `to` lines on from where cell `name` begins -
///its mark, wherever typing has moved it. See TextEditor.GoToShape, which counts them from the records.
///
///The editor is not given the focus, so the rules panel's arrows go on stepping; the cursor goes to the first line,
///for when it is.
///
window.GoToMonacoCellLines = function (name, from, to) {
    if (monacoEditor == null || cellMarks == null)
        return false;

    const at = cellNames.indexOf(name);

    if (at < 0)
        return false;

    const range = cellMarks.getRange(at);

    if (range == null)
        return false;

    const last = monacoEditor.getModel().getLineCount();
    const first = Math.min(range.startLineNumber + from, last);
    const end = Math.min(range.startLineNumber + Math.max(from, to), last);

    lightLines(first, end, 'textFaultLines', '#e53935');

    monacoEditor.setPosition({ lineNumber: first, column: 1 });
    monacoEditor.revealLinesInCenter(first, end);

    return true;
}

///
///Where each placement is - its SREF or AREF to its ENDEL - marked, so the cell list can go to one: the lines worked
///out from the records as the text went in (see linesOf in TextEditor.razor), and moved by the editor with whatever is
///typed. Nothing drawn: there can be thousands, and the list is how they are reached.
///
let placementMarks = null;

window.MarkMonacoPlacements = function (from, to) {
    if (monacoEditor == null)
        return;

    const marks = from.map(function (line, at) {
        return {
            range: new monaco.Range(line, 1, to[at], 1),
            options: { stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges }
        };
    });

    if (placementMarks == null)
        placementMarks = monacoEditor.createDecorationsCollection(marks);
    else
        placementMarks.set(marks);
}

///
///Goes to a placement, by its place among the marks: its lines lit and the cursor on its first, with the editor in
///focus - a press in the list is a way to get there and go on typing, as a cell's is. False for one with no mark.
///
window.GoToMonacoPlacement = function (at) {
    if (monacoEditor == null || placementMarks == null)
        return false;

    const range = placementMarks.getRange(at);

    if (range == null)
        return false;

    lightLines(range.startLineNumber, range.endLineNumber, 'textPlacedLines', '#1b6ec2');

    monacoEditor.setPosition({ lineNumber: range.startLineNumber, column: 1 });
    monacoEditor.revealLinesInCenter(range.startLineNumber, range.endLineNumber);
    monacoEditor.focus();

    return true;
}

window.ToggleMonacoWordwrap = function (value) {
    if (monacoEditor == null)
        return;

    let wrap = "off";

    if (value)
        wrap = "on";

    monacoEditor.updateOptions({ wordWrap: wrap });
}

function getKeyWords()
{
    //This list could also be dynamically generated from the GDS model at GDS.cs. Let's just hard code it for now.
    const headersList = [
        "HEADER",
        "BGNLIB",
        "LIBNAME",
        "UNITS",
        "ENDLIB",
        "BGNSTR",
        "STRNAME",
        "ENDSTR",
        "BOUNDARY",
        "PATH",
        "SREF",
        "AREF",
        "TEXT",
        "LAYER",
        "DATATYPE",
        "WIDTH",
        "XY",
        "ENDEL",
        "SNAME",
        "COLROW",
        "NODE",
        "TEXTTYPE",
        "PRESENTATION",
        "STRING",
        "STRANS",
        "MAG",
        "ANGLE",
        "REFLIBS",
        "FONTS",
        "PATHTYPE",
        "GENERATIONS",
        "ATTRTABLE",
        "ELFLAGS",
        "NODETYPE",
        "PROPATTR",
        "PROPVALUE",
        "BOX",
        "BOXTYPE",
        "PLEX",
        "TAPENUM",
        "TAPECODE",
        "FORMAT",
        "MASK",
        "ENDMASKS"
    ]

    return headersList;
}

function createDependencyProposals(range) {
    //Returning a static list of proposals, not even looking at the prefix (filtering is done by the Monaco editor).
    //This json could also be dynamically generated from the GDS model at GDS.cs. Let's just hard code it for now.
    return [
        {
            label: 'HEADER',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer version number.",
            insertText: 'HEADER: ',
            range: range,
        },
        {
            label: 'BGNLIB',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer begin of library, last modification date and time.",
            insertText: 'BGNLIB: ',
            range: range,
        },
        {
            label: 'LIBNAME',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer name of library.",
            insertText: 'LIBNAME: ',
            range: range,
        },
        {
            label: 'UNITS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Eight-Byte Real user and database units.",
            insertText: 'UNITS: ',
            range: range,
        },
        {
            label: 'ENDLIB',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data end of library.",
            insertText: 'ENDLIB: ',
            range: range,
        },
        {
            label: 'BGNSTR',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer begin of structure + creation and modification time.",
            insertText: 'BGNSTR: ',
            range: range,
        },
        {
            label: 'STRNAME',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string name of structure",
            insertText: 'STRNAME: ',
            range: range,
        },
        {
            label: 'ENDSTR',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data end of structure.",
            insertText: 'ENDSTR: ',
            range: range,
        },
        {
            label: 'BOUNDARY',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of boundary element.",
            insertText: 'BOUNDARY: ',
            range: range,
        },
        {
            label: 'PATH',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of path element.",
            insertText: 'PATH: ',
            range: range,
        },
        {
            label: 'SREF',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of structure reference element.",
            insertText: 'SREF: ',
            range: range,
        },
        {
            label: 'AREF',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of array reference element.",
            insertText: 'AREF: ',
            range: range,
        },
        {
            label: 'TEXT',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of text element.",
            insertText: 'TEXT: ',
            range: range,
        },
        {
            label: 'LAYER',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer layer number of element.",
            insertText: 'LAYER: ',
            range: range,
        },
        {
            label: 'DATATYPE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer Datatype number of element.",
            insertText: 'DATATYPE: ',
            range: range,
        },
        {
            label: 'WIDTH',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Four-Byte Signed Integer width of element in db units.",
            insertText: 'WIDTH: ',
            range: range,
        },
        {
            label: 'XY',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Four-Byte Signed Integer list of xy coordinates in db units.",
            insertText: 'XY: ',
            range: range,
        },
        {
            label: 'ENDEL',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data end of element.",
            insertText: 'ENDEL: ',
            range: range,
        },
        {
            label: 'SNAME',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string name of structure reference.",
            insertText: 'SNAME: ',
            range: range,
        },
        {
            label: 'COLROW',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer number of columns and rows in array reference.",
            insertText: 'COLROW: ',
            range: range,
        },
        {
            label: 'NODE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of node element.",
            insertText: 'NODE: ',
            range: range,
        },
        {
            label: 'TEXTTYPE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer texttype number.",
            insertText: 'TEXTTYPE: ',
            range: range,
        },
        {
            label: 'PRESENTATION',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Bit Array text presentation, font.",
            insertText: 'PRESENTATION: ',
            range: range,
        },
        {
            label: 'STRING',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string character string for text element.",
            insertText: 'STRING: ',
            range: range,
        },
        {
            label: 'STRANS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Bit Array array reference, structure reference and text transform flags.",
            insertText: 'STRANS: ',
            range: range,
        },
        {
            label: 'MAG',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Eight Byte Real magnification factor for text and references.",
            insertText: 'MAG: ',
            range: range,
        },
        {
            label: 'ANGLE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Eight Byte Real rotation angle for text and references.",
            insertText: 'ANGLE: ',
            range: range,
        },
        {
            label: 'REFLIBS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string name of referenced libraries.",
            insertText: 'REFLIBS: ',
            range: range,
        },
        {
            label: 'FONTS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string name of text fonts definition files.",
            insertText: 'FONTS: ',
            range: range,
        },
        {
            label: 'PATHTYPE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer type of PATH element end (rounded, square).",
            insertText: 'PATHTYPE: ',
            range: range,
        },
        {
            label: 'GENERATIONS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer number of deleted structure.",
            insertText: 'GENERATIONS: ',
            range: range,
        },
        {
            label: 'ATTRTABLE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string attribute table, used in combination with element properties.",
            insertText: 'ATTRTABLE: ',
            range: range,
        },
        {
            label: 'ELFLAGS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer template data.",
            insertText: 'ELFLAGS: ',
            range: range,
        },
        {
            label: 'NODETYPE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer node type number for NODE element.",
            insertText: 'NODETYPE: ',
            range: range,
        },
        {
            label: 'PROPATTR',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer attribute number.",
            insertText: 'PROPATTR: ',
            range: range,
        },
        {
            label: 'PROPVALUE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string attribute name.",
            insertText: 'PROPVALUE: ',
            range: range,
        },
        {
            label: 'BOX',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data begin of box element.",
            insertText: 'BOX: ',
            range: range,
        },
        {
            label: 'BOXTYPE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer boxtype for box element.",
            insertText: 'BOXTYPE: ',
            range: range,
        },
        {
            label: 'PLEX',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Four-Byte Signed Integer plex number.",
            insertText: 'PLEX: ',
            range: range,
        },
        {
            label: 'TAPENUM',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer Tape Number.",
            insertText: 'TAPENUM: ',
            range: range,
        },
        {
            label: 'TAPECODE',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer Tape code.",
            insertText: 'TAPECODE: ',
            range: range,
        },
        {
            label: 'FORMAT',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "Two-Byte Signed Integer format type.",
            insertText: 'FORMAT: ',
            range: range,
        },
        {
            label: 'MASK',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "ASCII string list of layers.",
            insertText: 'MASK: ',
            range: range,
        },
        {
            label: 'ENDMASKS',
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: "No Data end of MASK.",
            insertText: 'ENDMASKS: ',
            range: range,
        },
    ];
}
