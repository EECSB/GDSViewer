//Global variables///////////////////////////////

let app;
let dotNetObjRef;

/////////////////////////////////////////////////


//C# Interop Functions///////////////////////////

//Init./////////////////////

function initKonva(data) {
    const viewWrapper = document.getElementsByClassName("viewWrapper")[0];

    let settings = {
        containerSizeX: viewWrapper.clientWidth,
        containerSizeY: viewWrapper.clientHeight,
        blockSnapSize: 10,
        draggable: true
    };

    //Make an instance of the Konva app.
    app = new ItemMap(settings);
}

function setDotNetObjRef(ref) {
    dotNetObjRef = ref;
}

////////////////////////////

function mapItemsInterop(itemsMap, allItems) {
    app.Items = itemsMap;
    app.AllItems = allItems;
    app.mapItems(itemsMap);
}

function selectItemInJsInterop(selectItemID) {
    app.selectItem(selectItemID);
}

function selectItemFromJsInterop(selectItemID) {
    dotNetObjRef.invokeMethodAsync('SelectItemFromJS', selectItemID).then(callbackID => {
        app.selectItem(callbackID);
    });
}

function openCloseAllChildrenFromJsInterop() {
    dotNetObjRef.invokeMethodAsync('openCloseAllChildrenFromJsInterop');
}

function openCloseChildrenFromJsInterop(currentItemID) {
    dotNetObjRef.invokeMethodAsync('openCloseChildrenFromJsInterop', currentItemID);
}

/////////////////////////////////////////////////


//Code///////////////////////////////////////////

class ItemMap {

    //Initialize//////////////////////////////////////

    constructor(settings) {
        //Add settings.
        this.settings = settings;

        //No currently selected item.
        this.selectedItem = null;
        this.selectedItemID = null;

        //Initialize container.
        this.stage = this.makeContainer();

        //Draw background grid. (stage ref., dots or lines, block snap size)
        this.drawGrid("none");

        //Add the layer for the items.
        let itemLayer = new Konva.Layer({ id: "itemLayer" });
        //Add the new layer.
        this.stage.add(itemLayer);

        //Register toolbar controls events.
        this.registerControls();
    }

    //////////////////////////////////////////////////


    //////////////////////////////////////////////////

    makeContainer() {
        return new Konva.Stage({
            container: "canvas",
            width: this.settings.containerSizeX,
            height: this.settings.containerSizeY,
            x: 0,
            y: 0,
            scaleX: 1,
            scaleY: 1,
            draggable: this.settings.draggable
        });
    }

    drawGrid(gridType) {
        //Make a new layer for the grid.
        let gridLayer = new Konva.Layer({ id: "grid" });

        const padding = this.settings.blockSnapSize;

        //Grid size.
        const height = this.stage.attrs.height * 15;
        const width = this.stage.attrs.width * 15;

        if (gridType == "dottedLines") {
            //Draw dotted lines for background to improve performance vs drawing dots as individual objects.
            for (var i = /*-(width / padding)*/0; i < width / padding; i++) {
                gridLayer.add(new Konva.Line({
                    points: [Math.round(i * padding) + 0.5, 0, Math.round(i * padding) + 0.5, height],
                    stroke: "#000",
                    strokeWidth: 3,
                    name: "backgroundLine",
                    lineCap: "round",
                    lineJoin: "round",
                    dash: [0, this.settings.blockSnapSize, 0, this.settings.blockSnapSize],
                    perfectDrawEnabled: false
                }));
            }

            gridLayer.add(new Konva.Line({ points: [0, 0, 10, 10] }));
        }

        if (gridType == "lines") {
            //Draw lines for backgrouond.
            for (var i = 0; i < width / padding; i++) {
                gridLayer.add(new Konva.Line({
                    points: [Math.round(i * padding) + 0.5, 0, Math.round(i * padding) + 0.5, height],
                    stroke: "#000",
                    strokeWidth: 0.5,
                    name: "backgroundLine",
                    perfectDrawEnabled: false
                }));
            }

            gridLayer.add(new Konva.Line({ points: [0, 0, 10, 10] }));

            for (var j = 0; j < height / padding; j++) {
                gridLayer.add(new Konva.Line({
                    points: [0, Math.round(j * padding), width, Math.round(j * padding)],
                    stroke: "#000",
                    strokeWidth: 0.5,
                    name: "backgroundLine",
                    perfectDrawEnabled: false
                }));
            }
        }

        //Add layer to stage.
        this.stage.add(gridLayer);

        if (this.stage.find("Layer").length > 1) //Another layer must be present in stage to be able to use setZIndex().
            gridLayer.setZIndex(0); //Will be above any lower numbered layers.

    }

    mapItems(itemsMap) {
        let itemGroups = this.stage.find(".itemGroup");
        if (itemGroups.length > 0)
            itemGroups.forEach((shape) => shape.destroy());

        //Set starting point.
        let x = this.settings.blockSnapSize;
        let y = this.settings.blockSnapSize;

        //Item dims.
        const itemWidth = 9;
        const itemHeight = 5;
        //Items spacing.
        const verticalPaddingBetweenItems = this.settings.blockSnapSize * itemHeight * 2;
        const horizontalPaddingBetweenItems = this.settings.blockSnapSize * 2;

        //Iterate all items by level.
        let level = 0;
        for (const itemsRowKey of Object.keys(itemsMap)) {
            const levelComponents = itemsMap[itemsRowKey];

            //Center items row.
            //const numberOfItems = Object.keys(levelComponents).length;
            //const itemsRowWidth = ((numberOfItems - 1) * horizontalPaddingBetweenItems) + (numberOfItems * itemWidth * this.settings.blockSnapSize);
            let numberOfItems = 0;
            for (let rowItem of Object.keys(levelComponents)) {
                let component = levelComponents[rowItem];

                if (component.id != "Root") { //Skip for root as it should always be visible.
                    let parentItem = this.AllItems[component.parentID];

                    //Skip component if parent has 'show' disabled.
                    if (parentItem.show)
                        numberOfItems++;
                }
            }

            const itemsRowWidth = ((numberOfItems - 1) * horizontalPaddingBetweenItems) + (numberOfItems * itemWidth * this.settings.blockSnapSize);
            x = this.settings.containerSizeX / 2 - itemsRowWidth / 2;

            for (let rowItem of Object.keys(levelComponents)) {
                let component = levelComponents[rowItem];

                if (!component.show)
                    continue;

                //Add item.
                this.addItem(x, y, itemWidth, itemHeight, component);
                //Set x for next item.
                x += (itemWidth * this.settings.blockSnapSize) + horizontalPaddingBetweenItems;
            }

            //Set y for next row.
            y += (itemHeight * this.settings.blockSnapSize) + verticalPaddingBetweenItems;
            //Set/go down to the next item level.
            level++;
        }

        //Reset the selected item parent.
        let selectedItemParents = this.getItemParentsFromID(this.selectItemID);
        if (this.selectedItem != null)
            if (selectedItemParents.length > 0)
                this.selectedItem.parent = selectedItemParents[0].parent;

        //Redraw stage.
        this.stage.draw();
    }

    openCloseChildren(currentItemID) {
        openCloseChildrenFromJsInterop(currentItemID);
    }

    openCloseAllChildren() {
        openCloseAllChildrenFromJsInterop();
    }

    /////////////////////////////////////////////////


    //Utils functions////////////////////////////////

    getItemParentsFromID(parentID) {
        let itemRectangles = this.stage.find(".itemRectangle");

        let parents = [];

        if (itemRectangles.length == 0)
            return parents;

        for (let itemRectangle of itemRectangles) {
            if (itemRectangle.attrs.id == parentID)
                parents.push(itemRectangle);
        }

        return parents;
    }

    getItemChildrenFromID(id) {
        let itemRectangles = this.stage.find(".itemRectangle");

        let children = [];

        if (itemRectangles.length == 0)
            return children;

        let component;
        //Not very efficeint. Refactor later(maybe).
        for (const itemsRowKey of Object.keys(this.Items)) {
            const levelComponents = this.Items[itemsRowKey];
            for (let rowItem of Object.keys(levelComponents)) {
                if (levelComponents[rowItem].id == id)
                {
                    component = levelComponents[rowItem];
                    break; //this needs to be changed to break out of booth loops.
                }
            }
        }

        if (component == undefined || component == null)
            return children;

        if (component.childrenIDs == null)
            return children;
        
        for (let itemRectangle of itemRectangles) {
            for (let childID of component.childrenIDs) {
                if (itemRectangle.attrs.id == childID)
                    children.push(itemRectangle);
            }
        }

        return children;
    }

    /////////////////////////////////////////////////


    //Feature specific functions/////////////////////

    addItem(x, y, width, height, component) {
        const ID = component.id;

        //Make item group.
        let itemGroup = new Konva.Group({ name: "itemGroup" });

        //Make the width/height fit(match) the grid.
        width = this.settings.blockSnapSize * width;
        height = this.settings.blockSnapSize * height;

        let opacity = 0.2;
        if (ID == this.selectedItemID) {
            opacity = 1;
        }

        //Create a new rectangle.
        let rectangle = this.newRectangle(x, y, width, height, ID, opacity);
        //Create a snap location indicator rectangle for the new rectangle.
        //let snapLocationRectangle = this.newSnapLocationRectangle(x, y, width, height); //Not needed if rectangle is not draggable.
        //Create rectangle text.
        let text = this.newRectangleText(x + 25, y, ID, component.displayID, 1); //Opacity always 1 from now on.
        let openChildrenText = this.newOpenChildernText(x + 25, y + 70, ID, component);
        //let text2 = this.newRectangleText(x + 10, y + 25, component.displayID, opacity);

        const url = component.url;
        let image = this.newImage(x+5, y+50, 40, 40, ID, url);

        //////////////////////////

        //Add items to the item group.
        itemGroup.add(rectangle);
        //itemGroup.add(snapLocationRectangle); //Not needed if rectangle is not draggable.
        itemGroup.add(text);
        itemGroup.add(openChildrenText);
        itemGroup.add(image);
        //itemGroup.add(text2);

        let konvaObjectParents = this.getItemParentsFromID(component.parentID);
        for (let konvaObjectParent of konvaObjectParents) {
            let points = [(konvaObjectParent.attrs.x + width / 2), (konvaObjectParent.attrs.y + height), (x + width / 2), y];
            let relationLine = this.newRelationLine(points, opacity);
            itemGroup.add(relationLine);
        }

        //Add itemGroup a layer.
        this.stage.find("#itemLayer")[0].add(itemGroup);
    }

    newImage(x, y, height, width, ID, url) {
        let imageObject = new Image();
        imageObject.src = url;

        let image = new Konva.Image({
            x: x,
            y: y,
            image: imageObject,
            width: width,
            height: height,
            draggable: false
        });

        image.on("click", (event) => {
            selectItemFromJsInterop(ID);
        });

        return image;
    }

    newOpenChildernText(x, y, ID, component) {
        let opacity = 0.2;
        if (component.childrenIDs.length > 0)
            opacity = 1;

        let text = new Konva.Text({
            name: "openChildrenText",
            text: "+/-",
            x: x,
            y: y,
            fontSize: 16,
            fontStyle: 'bold',
            fontFamily: 'Calibri',
            fill: '#000',
            width: 130,
            padding: 5,
            align: 'center',
            opacity: opacity
        });

        text.on("click", (event) => {
            this.openCloseChildren(ID);
            selectItemFromJsInterop(ID);
        });

        return text;
    }

    newRectangleText(x, y, ID, displayID, opacity) {
        let text = new Konva.Text({
            name: "itemText",
            text: displayID,
            x: x,
            y: y,
            fontSize: 16,
            fontFamily: 'Calibri',
            fill: '#000',
            width: 130,
            padding: 5,
            align: 'center',
            opacity: opacity
        });

        text.on("click", (event) => {
            selectItemFromJsInterop(ID);
        });

        return text;
    }

    newSnapLocationRectangle(x, y, width, height) {
        let snapRect = new Konva.Rect({
            name: "snapLocationRectangle",
            x: x,
            y: y,
            width: width,
            height: height,
            fill: "#26dd02",
            opacity: 0.7,
            stroke: "#168201",
            strokeWidth: 4,
            perfectDrawEnabled: false
        });

        //Keep the snap location rectangle hidden by default.
        snapRect.hide();

        return snapRect;
    }

    newRectangle(x, y, width, height, ID, opacity) {
        let fillColor = "#fff";
        if (ID == this.selectedItemID) {
            fillColor = "#D5E8D4";
        }

        //Make new object.
        let rectangle = new Konva.Rect({
            name: "itemRectangle",
            id: ID,
            x: x,
            y: y,
            width: width,
            height: height,
            fill: fillColor,
            stroke: "#ddd",
            strokeWidth: 1,
            shadowColor: "black",
            shadowBlur: 2,
            shadowOffset: { x: 1, y: 1 },
            shadowOpacity: 0.4,
            draggable: false,
            opacity: opacity,
            perfectDrawEnabled: false
        });

        //Events////////////////////////////////////////////

        //When the object dragging starts show snap location, then move the object on top.
        rectangle.on("dragstart", (event) => {
            event.currentTarget.parent.find(".snapLocationRectangle").forEach((shape) => shape.show());
            event.currentTarget.moveToTop();

            this.stage.batchDraw();
        });

        //When moving stops snap item to grid and hide snap location item. 
        rectangle.on("dragend", (event) => {

            event.currentTarget.position({
                x: Math.round(rectangle.x() / this.settings.blockSnapSize) * this.settings.blockSnapSize,
                y: Math.round(rectangle.y() / this.settings.blockSnapSize) * this.settings.blockSnapSize
            });

            this.stage.batchDraw();
            event.currentTarget.parent.find(".snapLocationRectangle").forEach((shape) => shape.hide());
        });

        //On move snap location indication rectangle.
        rectangle.on("dragmove", (event) => {
            event.currentTarget.parent.find(".snapLocationRectangle").forEach((shape) => shape.position({
                x: Math.round(rectangle.x() / this.settings.blockSnapSize) * this.settings.blockSnapSize,
                y: Math.round(rectangle.y() / this.settings.blockSnapSize) * this.settings.blockSnapSize
                })
            );

            let itemText = event.currentTarget.parent.find(".itemText")[0];

            itemText.position({
                x: Math.round(rectangle.x()),
                y: Math.round(rectangle.y())
            });

            itemText.moveToTop();

            this.stage.batchDraw();
        });

        //On item click set it as the selected item.
        rectangle.on("click", (event) => {
            selectItemFromJsInterop(rectangle.attrs.id);
        });

        ///////////////////////////////////////////////////////

        return rectangle;
    }

    newRelationLine(p, opacity) {
        //Make drawn line.
        let drawnLine = new Konva.Line({
            points: p,
            tension: 0,
            fill: 'red',
            stroke: 'black',
            strokeWidth: 2,
            draggable: false,
            name: "drawLine",
            opacity: opacity,
            perfectDrawEnabled: false
        });

        return drawnLine;
    }

    scale(scaleBy, directionCmd) {
        var oldScale = this.stage.scaleX();

        let newScale;
        if (directionCmd == "in")
            newScale = oldScale * scaleBy;
        else if (directionCmd == "out")
            newScale = oldScale / scaleBy;

        this.stage.scale({ x: newScale, y: newScale });

        /*
        var mousePointTo = {
            x: this.stage.x() / oldScale,
            y: this.stage.y() / oldScale,
        };

        var newPos = {
            x: mousePointTo.x * newScale,
            y: mousePointTo.y * newScale,
        };

        this.stage.position(newPos);
        */

        this.stage.draw();
    }

    /////////////////////////////////////////////////


    //Events/////////////////////////////////////////

    selectItem(itemID) {
        if (this.selectedItem != undefined && this.selectedItem != null) {
            //Previous item reset color.
            this.selectedItem.attrs.fill = "#fff";
            this.selectedItem.opacity(0.2);

            if (this.selectedItem.parent != null) {
                let parentLines = this.selectedItem.parent.find(".drawLine");
                for (let parentLine of parentLines)
                    parentLine.opacity(0.2);

                //this.selectedItem.parent.find(".itemText")[0].opacity(0.2); //Always 1 from now on.
            }

            let childrenItems = this.getItemChildrenFromID(this.selectedItem.attrs.id);
            for (let childItem of childrenItems) {
                let childLines = childItem.parent.find(".drawLine");

                for (let childLine of childLines)
                    childLine.opacity(0.2);
            }
        }

        //Get selected item.
        let item = this.stage.find("#" + itemID)[0];
        //Make sure it exists else return.
        if (item == undefined || item == null)
            return;

        //Set new selected item.
        this.selectedItem = item;
        this.selectItemID = this.selectedItem.attrs.id;

        //Highlight and set the "selected" color to the selectedItem.
        this.selectedItem.attrs.fill = "#D5E8D4";
        this.selectedItem.opacity(1);


        if (this.selectedItem.parent != null) {
            //Highlight the text of the selected item.
            //this.selectedItem.parent.find(".itemText")[0].opacity(1); //Always 1 from now on.
            //Highlight the parent lines of the selected item.
            let parentLines = this.selectedItem.parent.find(".drawLine");
            for (let parentLine of parentLines)
                parentLine.opacity(1);
        }

        //Highlight the child lines of the selected item.
        let childrenItems = this.getItemChildrenFromID(itemID);
        for (let childItem of childrenItems) {
            let childLines = childItem.parent.find(".drawLine");

            for (let childLine of childLines)
                childLine.opacity(1);
        }

        //Redraw stage.
        app.stage.draw();
    }

    centerCanvas() {
        //Reset position.
        this.stage.x(0);
        this.stage.y(0);

        //Reset scale.
        this.stage.scale({ x: 1, y: 1 });

        //Redraw stage.
        this.stage.draw();
    }

    scrollScale() {
        const scaleBy = 1.2;
        this.stage.on('wheel', (e) => {
            // stop default scrolling
            e.evt.preventDefault();

            var oldScale = this.stage.scaleX();
            var pointer = this.stage.getPointerPosition();

            var mousePointTo = {
                x: (pointer.x - this.stage.x()) / oldScale,
                y: (pointer.y - this.stage.y()) / oldScale,
            };

            // how to scale? Zoom in? Or zoom out?
            let direction = e.evt.deltaY > 0 ? -1 : 1;

            // when we zoom on trackpad, e.evt.ctrlKey is true
            // in that case lets revert direction
            if (e.evt.ctrlKey) {
                direction = -direction;
            }

            var newScale = direction > 0 ? oldScale * scaleBy : oldScale / scaleBy;

            this.stage.scale({ x: newScale, y: newScale });

            var newPos = {
                x: pointer.x - mousePointTo.x * newScale,
                y: pointer.y - mousePointTo.y * newScale,
            };

            this.stage.position(newPos);

            this.stage.draw();
        });
    }
    
    registerControls() {
        //Grid selection.
        document.getElementById("backgroundSelection").addEventListener("change", (event) => {
            //Clear current grid.
            this.stage.find("#grid")[0].destroy();
            //Draw grid with lines.
            this.drawGrid(event.target.value);
            //Redraw stage.
            this.stage.draw();
        });

        //Delete selected item.
        document.addEventListener('keydown', (event) => {
            if (event.keyCode == 46) {
                this.selectedItem.destroy();
                this.selectedItem = null;
                this.stage.batchDraw();
            }
        });

        //Center map. // moved to centerCanvas()
        document.getElementById("center").addEventListener("click", (event) => {
            this.centerCanvas();
        });

        //Zoom in/out with the scroll wheel.
        this.scrollScale();

        
        //Zoom in.
        document.getElementById("zoomIn").addEventListener("click", (event) => {
            this.scale(1.2, "in");
        });

        //Zoom out.
        document.getElementById("zoomOut").addEventListener("click", (event) => {
            this.scale(1.2, "out");
        });
    }

    /////////////////////////////////////////////////
}


class Queue {
    constructor() {
        this.elements = {};
        this.head = 0;
        this.tail = 0;
    }
    enqueue(element) {
        this.elements[this.tail] = element;
        this.tail++;
    }
    dequeue() {
        const item = this.elements[this.head];
        delete this.elements[this.head];
        this.head++;
        return item;
    }
    peek() {
        return this.elements[this.head];
    }
    get length() {
        return this.tail - this.head;
    }
    get isEmpty() {
        return this.length === 0;
    }
}