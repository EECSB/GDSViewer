//The 2D SVG view: what it draws for a known file, and the two controls that redraw it.
const { test, expect } = require('@playwright/test');
const {
    gotoExample,
    svgCounts,
    layerPairs,
    hideLayer,
    showLayer,
    MOSFET,
    MOSFET_POLYGONS,
    MOSFET_LABELS,
    MOSFET_LAYER_PAIRS
} = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoExample(page, MOSFET);
});

test('draws every element of the file, with its labels', async ({ page }) => {
    //Polled: the file is fetched and flattened after the view is on screen, so reading once races it.
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
    expect((await svgCounts(page)).labels).toBe(MOSFET_LABELS);
});

test('the layer sidebar lists one row per layer/datatype pair, in ascending order', async ({ page }) => {
    //Nine rows across six layer numbers: 66, 67 and 68 each carry two purposes in this file, and keying on
    //the number alone merged each of those pairs into a single row sharing one checkbox and one color.
    expect(await layerPairs(page)).toEqual(MOSFET_LAYER_PAIRS);
});

test('coordinates and opacity are written so a browser can read them', async ({ page }) => {
    const svg = await svgCounts(page);

    //A decimal point and an ASCII minus, whatever locale the browser is in. Mosfet.gds is the useful
    //case because its coordinates go negative.
    expect(svg.opacity).toBe('0.5');
    expect(svg.points).toContain('-');
    expect(svg.points).not.toContain(',,');
});

test('hiding a layer stops its geometry being drawn, and showing it brings it back', async ({ page }) => {
    //Waited for rather than read once: reading the count while the file is still being drawn gives a
    //smaller number to compare against, and then "fewer than before" can never be true.
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);

    await hideLayer(page);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBeLessThan(MOSFET_POLYGONS);

    await showLayer(page);
    await expect.poll(async () => (await svgCounts(page)).polygons).toBe(MOSFET_POLYGONS);
});

///
///The slider is with the layers, not in the toolbar.
///
///Opacity is a property of the layer stack - it exists so you can see through the sheets on top of one
///another - and it used to sit in the bar over the view, a row away from the list of the very things it
///makes see-through. Under the list is where it reads, so this pins it to the sidebar rather than to
///whatever is in the bar that week.
///
test('the opacity slider sits under the layer list, in the layer sidebar', async ({ page }) => {
    const slider = page.locator('#layerOpacity');

    await expect(slider).toBeVisible();

    //In the sidebar, and the thing directly after the list of layers rather than merely somewhere near it.
    await expect(page.locator('#layerSidebar #layerOpacity')).toHaveCount(1);

    const follows = await page.evaluate(() => {
        const control = document.querySelector('.layerOpacity');

        return control.previousElementSibling === document.querySelector('.layerList');
    });

    expect(follows).toBe(true);

    //And not in the bar over the view, which is where it was.
    await expect(page.locator('.viewToolbar #layerOpacity')).toHaveCount(0);
});

///
///The label, the slider and the strip they are in all share one middle.
///
///Two different things get called "not centered" here and only one of them was true. The label and the
///slider were already dead level with each other - `align-items` does that. What sat low was the pair of
///them together, because the strip's padding was 6px over 2.4px and put their middle 1.8 under its own.
///So this measures all four, or a fix to the wrong one of them would pass.
///
test('the opacity label and slider are centered on each other, and in their strip', async ({ page }) => {
    const middles = await page.evaluate(() => {
        const strip = document.querySelector('.layerOpacity');
        const style = getComputedStyle(strip);
        const box = strip.getBoundingClientRect();
        const middle = rect => (rect.top + rect.bottom) / 2;

        return {
            strip: middle(box),
            content: (box.top + parseFloat(style.paddingTop) + box.bottom - parseFloat(style.paddingBottom)) / 2,
            label: middle(strip.querySelector('label').getBoundingClientRect()),
            slider: middle(strip.querySelector('input').getBoundingClientRect())
        };
    });

    //Half a pixel of slack for sub-pixel layout; the bug this catches was 1.8 out.
    expect(Math.abs(middles.label - middles.slider)).toBeLessThanOrEqual(0.5);
    expect(Math.abs(middles.content - middles.strip)).toBeLessThanOrEqual(0.5);
    expect(Math.abs(middles.label - middles.strip)).toBeLessThanOrEqual(0.5);
});

test('the opacity slider changes what the polygons are filled at', async ({ page }) => {
    const slider = page.locator('#layerOpacity');

    await slider.fill('0.9');

    //Read back through the SVG rather than the slider, so this covers the whole round trip: the value
    //is parsed from the DOM event and written back into the markup.
    await expect.poll(async () => (await svgCounts(page)).opacity).toBe('0.9');
});
