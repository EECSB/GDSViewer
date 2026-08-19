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

    //Define a new theme that contains only rules that match this language.
    monaco.editor.defineTheme("defaultGDS", {
        base: "vs",
        inherit: true,
        rules: [
            { token: "colon", foreground: "9400ff" },
            { token: "typeKeywords", foreground: "03b1b9" },
            { token: "number.float", foreground: "ed7e00" },
            //{ token: "number", foreground: "ffbc70" }, //inherit default from 'number'
        ],
        colors: {
            "editor.foreground": "#000000",
        },
    });

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

    //Create the editor instance.
    monacoEditor = monaco.editor.create(document.getElementById('gdsTextEditor'), {
        theme: "defaultGDS",
        value: text,
        language: language,
        automaticLayout: true,
        wordWrap: "on"
    });
}

window.SetMonacoContent = function (text) {
    if (monacoEditor != null)
        monacoEditor.setValue(text);
}

window.GetMonacoContent = function () {
    if (monacoEditor == null)
        return "";

    return monacoEditor.getValue();
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
