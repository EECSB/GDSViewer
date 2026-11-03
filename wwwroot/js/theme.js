//
//Light or dark: the look of the whole app, and the one place it is remembered.
//
//**Loaded in the head and run at once, rather than from the app's own interop**, because it has to land
//before anything is drawn. Blazor is a second or two from starting, and a dark page that opened white and
//then flicked over would be worse than having no dark mode at all - so the attribute the stylesheet keys on
//is set while the head is still being read.
//
//**Its own key in localStorage, not the session.** The session is written compressed and only Blazor reads
//it; this has to be read by a few lines of script before Blazor exists. It is a preference about the app
//rather than about a file, too, so it has no business in a history entry.
//
//Light unless dark was chosen, which is how the app has always looked: an unreadable or missing value is
//light, and so is a browser that will not hand localStorage over at all.
//

const THEME_KEY = 'gdsviewer.theme';

///Whichever was chosen last, as 'dark' or 'light'.
function storedTheme() {
    try {
        if (localStorage.getItem(THEME_KEY) === 'dark')
            return 'dark';
    }
    catch (e) { }

    return 'light';
}

///The theme the page is showing now, for the switch that has to say so.
function currentTheme() {
    if (document.documentElement.dataset.theme === 'dark')
        return 'dark';

    return 'light';
}

///
///Shows a theme and remembers it.
///
///Light is the absence of the attribute rather than a value of its own, so every rule written for the page
///before there was a dark mode goes on meaning exactly what it did.
///
///The text editor keeps a theme of its own inside its canvas, where no stylesheet reaches, so it is told
///separately - see applyEditorTheme in MonacoInterop.js, which is only there once the editor has loaded.
///
function setTheme(theme) {
    if (theme === 'dark')
        document.documentElement.dataset.theme = 'dark';
    else
        delete document.documentElement.dataset.theme;

    try {
        localStorage.setItem(THEME_KEY, theme);
    }
    catch (e) { }

    if (typeof window.applyEditorTheme === 'function')
        window.applyEditorTheme();
}

if (storedTheme() === 'dark')
    document.documentElement.dataset.theme = 'dark';
