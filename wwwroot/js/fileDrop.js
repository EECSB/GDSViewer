//
//Dropping a layout file onto the view.
//
//**Through the file input rather than around it.** A drop hands over a FileList, which is exactly what the
//Open dialog hands over - so this sets that list on the hidden input Open already uses and lets the input's
//own change event carry it across. Everything after that is the upload path that was there before: the offer
//to bring the file in as a cell instead of replacing what is open, the confirm for when there is nothing to
//import, the history entry, the parse errors, the alert about a second file. A drop that read the bytes
//itself and handed them to C# by another route would be a second copy of all of that, and the copy is where
//the two would drift.
//
//**Delegated from the document**, the way sidebars.js is and for the same reason: the pane is re-rendered
//whenever the view changes, so a listener bound to whatever element was mounted at startup is a listener on
//an element that no longer exists.
//
//**A drop outside the drawing is swallowed rather than left to the browser.** The default for a file dropped on
//a page is to navigate to it, which here means the app closing on top of an unsaved layout because somebody
//missed by a centimeter and hit the toolbar. Nothing happens instead, and the highlight says where the file
//would have landed.
//

///The class the drawing wears while a file is over it, which is the whole of the highlight.
const OVER_CLASS = 'fileDropOver';

///
///Whether the drag is carrying files at all.
///
///A drag inside the page - a text selection, a link, an image - has types of its own and must go on doing
///whatever it did before. `types` is a DOMStringList in older browsers rather than an array, so this asks it
///the way both answer.
///
function carriesFiles(event) {
    const carrying = event.dataTransfer;

    if (carrying == null || carrying.types == null)
        return false;

    return Array.prototype.indexOf.call(carrying.types, 'Files') >= 0;
}

///
///The box the file would open into, or null when this drag is not one to take.
///
///Null covers three cases that all mean the same thing here: a drag with no files in it, a pointer that is
///not over the drawing, and a page that has turned editing off. That last one is the read-only embed, where
///the toolbar's Open is disabled and a drop is Open by another name - see noEditing in Viewer.razor.
///
///**The pane is the permission; the canvas is the target.** `#viewPane` is where the shell says whether this
///page opens files at all, but it is not only the drawing: the 2D view puts the cell tree in a column inside
///it, so a file held anywhere over the pane lit a box that was mostly a list of cell names. `.viewCanvas` is
///the drawing and its own furniture, which is the thing being offered. The 3D and text views have no such
///box and take the pane, which for them is the same rectangle.
///
function zoneUnder(event) {
    if (!carriesFiles(event))
        return null;

    const target = event.target;

    if (target == null || typeof target.closest !== 'function')
        return null;

    const pane = target.closest('#viewPane');

    if (pane == null || pane.dataset.fileDrop !== 'on')
        return null;

    const canvas = pane.querySelector('.viewCanvas');

    if (canvas == null)
        return pane;

    //Over the tree, or over whatever else the pane holds beside the drawing.
    if (!canvas.contains(target))
        return null;

    return canvas;
}

///Takes the highlight off whichever box is wearing it.
function clearHighlight() {
    const lit = document.querySelector('.' + OVER_CLASS);

    if (lit != null)
        lit.classList.remove(OVER_CLASS);
}

///
///Says the drop would be taken here, and lights the drawing if it would.
///
///dragover rather than dragenter is what decides: the browser reads the answer to this one event every few
///hundred milliseconds for as long as the drag lasts, so preventing the default here is what makes the drop
///possible, and it is also the only event that keeps telling us where the pointer is. Which means the
///highlight can be turned off from the same place it is turned on, rather than from a matching dragleave
///that has to be counted because it fires on every child boundary crossed.
///
function onDragOver(event) {
    if (!carriesFiles(event))
        return;

    //Even off the drawing. This is the drop the browser would otherwise answer by navigating away.
    event.preventDefault();

    const zone = zoneUnder(event);

    if (zone == null) {
        clearHighlight();

        if (event.dataTransfer != null)
            event.dataTransfer.dropEffect = 'none';

        return;
    }

    if (event.dataTransfer != null)
        event.dataTransfer.dropEffect = 'copy';

    zone.classList.add(OVER_CLASS);
}

///
///The drag leaving the window, which no dragover reports.
///
///Only that case. dragleave also fires every time the pointer crosses from one element to another inside the
///pane - and there are a great many of those, since the 2D view is one path per shape - so a bare listener
///here would put the highlight out while the file was still over the view. relatedTarget is null when the
///drag has gone out of the document, and an element when it has merely moved within it.
///
function onDragLeave(event) {
    if (event.relatedTarget == null)
        clearHighlight();
}

///
///The drop itself: the file onto the input, and the input's own event to carry it.
///
///Assigned rather than copied through a fresh DataTransfer. The FileList the drop arrives with is the one
///the input wants, and the change listener Blazor put on that input reads `files` off the element when it
///fires - so what matters is that the list is on the element before the event is dispatched, and not where
///the list came from.
///
function onDrop(event) {
    if (!carriesFiles(event))
        return;

    //Before the zone is even looked for, so a miss lands on nothing rather than on the browser's own
    //file viewer.
    event.preventDefault();

    clearHighlight();

    const zone = zoneUnder(event);

    if (zone == null)
        return;

    const input = document.getElementById('fileUpload');

    if (input == null)
        return;

    //
    //**Nothing at all until Blazor's own listener is on.**
    //
    //A change event dispatched before InputFile has attached carries no files: C# is handed FileCount 0 and
    //takes the silent return a canceled dialog takes, so the drop leaves no mark of any kind - see the note
    //on this same flag in e2e/helpers.js. The window is milliseconds wide and only reachable by dropping a
    //file onto a page that is still starting, but a drop that quietly does nothing is the worst of the
    //answers available, so it is not offered.
    //
    if (input._blazorInputFileNextFileId === undefined)
        return;

    const files = event.dataTransfer.files;

    if (files == null || files.length === 0)
        return;

    input.files = files;

    input.dispatchEvent(new Event('change', { bubbles: true }));
}

function startFileDrop() {
    document.addEventListener('dragover', onDragOver);
    document.addEventListener('dragleave', onDragLeave);
    document.addEventListener('drop', onDrop);

    //A drag abandoned with Escape, which fires neither of the two above.
    document.addEventListener('dragend', clearHighlight);
}

startFileDrop();
