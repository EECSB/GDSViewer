//
//The command palette's key, and the one thing a command needs from the page that C# cannot do itself.
//
//**Ctrl+K, or ⌘K on a Mac, from anywhere in the app** - the key the other apps with a palette use, and one the
//app's own shortcuts leave free: those are Ctrl+Z, Y, C, X, V and A, Delete, Escape and single letters, and they
//belong to the 2D view. This is the page's, so it opens the palette over the 3D and text views as well.
//
//**Except while something is being typed into**, where the key is the box's. The text view's editor uses Ctrl+K
//to begin chords of its own - and has a palette of its own, on F1 - so a palette that took the key from it would
//take away something it already had.
//
//**On the window, once.** The palette is part of the shell, which is never re-rendered away, so one listener
//put on at its first render lasts the life of the page.
//

///The shell's handle, which the key opens the palette through. Null until the shell has said it is listening.
let paletteHandle = null;

///Whether the focus is somewhere the key is being typed into, rather than somewhere it is free to mean the palette.
function paletteTyping(element) {
    if (element == null)
        return false;

    if (element.isContentEditable)
        return true;

    return ['INPUT', 'TEXTAREA', 'SELECT'].includes(element.tagName);
}

function onPaletteKey(event) {
    if (paletteHandle == null)
        return;

    const held = event.ctrlKey === true || event.metaKey === true;

    if (!held || event.altKey || event.shiftKey || event.key.toLowerCase() !== 'k')
        return;

    //The palette's own search box is a box too, and the key there closes the palette it opened - a toggle,
    //as a key that opens something usually is. Any other box keeps the key.
    const inPalette = document.activeElement != null && document.activeElement.closest('#commandPalette') != null;

    if (paletteTyping(document.activeElement) && !inPalette)
        return;

    event.preventDefault();

    paletteHandle.invokeMethodAsync('ToggleCommandPalette');
}

window.commandPalette = {
    ///Starts listening for the key, for the shell that owns the palette.
    watch: function (handle) {
        if (paletteHandle == null)
            window.addEventListener('keydown', onPaletteKey);

        paletteHandle = handle;
    },

    ///
    ///Presses a control by its selector, for a command that does what a button on the page already does - and
    ///false when there is no such control on the page, so the palette can say so.
    ///
    ///Pressed rather than imitated, so a command and its button can never come to do different things. A click
    ///dispatched here reaches the control's own handler exactly as a press does, Blazor's included.
    ///
    press: function (selector) {
        const control = document.querySelector(selector);

        if (control == null)
            return false;

        control.click();

        return true;
    },

    ///Keeps the highlighted command in view as the arrows move through a list longer than the box.
    reveal: function (selector) {
        const row = document.querySelector(selector);

        if (row != null)
            row.scrollIntoView({ block: 'nearest' });
    }
};
