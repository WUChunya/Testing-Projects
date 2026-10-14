// Program Diagram — Illustrator runtime (ExtendScript, ES3).
// PD.run(drawings, {outDir, png, pngScale}) creates or updates one .ai per drawing (and a PNG per artboard when png):
//  - global process swatches (updated in place, so recolouring a swatch recolours the drawing),
//  - paragraph styles, artboards, named layers,
//  - items named by the plugin are replaced; unnamed items and the _MANUAL / OVERLAY layers are never touched.
// Drawing coordinates: points from the artboard's top-left, y down.
var PD = PD || {};
PD.version = "0.8.7";

PD.run = function (drawings, opts) {
    var log = [];
    var oldLevel = app.userInteractionLevel;
    app.userInteractionLevel = UserInteractionLevel.DONTDISPLAYALERTS;
    var oldCoords = app.coordinateSystem;
    app.coordinateSystem = CoordinateSystem.DOCUMENTCOORDINATESYSTEM;
    for (var i = 0; i < drawings.length; i++) {
        try {
            var warnings = [];
            PD.drawFile(drawings[i], opts, warnings);
            log.push("OK " + drawings[i].fileName + (warnings.length ? " (" + warnings.length + " item warnings)" : ""));
            for (var w = 0; w < warnings.length && w < 20; w++) log.push("  " + warnings[w]);
        } catch (e) {
            log.push("ERROR " + drawings[i].fileName + ": " + e + (e.line ? " (line " + e.line + ")" : ""));
        }
    }
    app.coordinateSystem = oldCoords;
    app.userInteractionLevel = oldLevel;
    PD.writeLog(opts.outDir + "/illustrator_log.txt", log);
    return log.join("\n");
};

PD.writeLog = function (path, lines) {
    var f = new File(path);
    f.encoding = "UTF-8";
    if (f.open("w")) {
        for (var i = 0; i < lines.length; i++) f.writeln(lines[i]);
        f.close();
    }
};

PD.drawFile = function (dw, opts, warnings) {
    var file = new File(opts.outDir + "/" + dw.fileName);
    var doc = file.exists ? app.open(file)
        : app.documents.add(DocumentColorSpace.RGB, dw.artboards[0].w, dw.artboards[0].h);
    PD.setupArtboards(doc, dw.artboards);
    // A swatch or style that cannot be made must not stop the file: fall back to a plain colour and log it.
    var sw = {};
    for (var s = 0; s < dw.swatches.length; s++) {
        var sn = dw.swatches[s].name;
        try { sw[sn] = PD.swatch(doc, sn, dw.swatches[s].hex); } catch (es) {
            sw[sn] = PD.hexToRgb(dw.swatches[s].hex);
            warnings.push("WARN " + dw.fileName + " swatch " + sn + ": " + es + " (plain colour used)");
        }
    }
    var ps = {};
    PD.specs = {};
    for (var p = 0; p < dw.paraStyles.length; p++) {
        PD.specs[dw.paraStyles[p].name] = dw.paraStyles[p];
        try { ps[dw.paraStyles[p].name] = PD.paraStyle(doc, dw.paraStyles[p], sw); } catch (ep) {
            warnings.push("WARN " + dw.fileName + " paragraph style " + dw.paraStyles[p].name + ": " + ep);
        }
    }
    PD.syms = PD.symbols(doc, dw.symbols, sw);

    var rects = [];
    for (var a = 0; a < doc.artboards.length; a++) rects.push(doc.artboards[a].artboardRect);

    for (var l = 0; l < dw.layers.length; l++) {
        var spec = dw.layers[l];
        var layer = PD.layer(doc, spec.name);
        var wasLocked = layer.locked, wasVisible = layer.visible;
        layer.locked = false;
        layer.visible = true;
        PD.clearNamed(layer);
        for (var k = 0; k < spec.items.length; k++) {
            var it = spec.items[k];
            try {
                PD.drawItem(layer, it, rects[it.artboard || 0], sw, ps);
            } catch (e) {
                warnings.push("WARN " + dw.fileName + " item " + it.name + ": " + e + (e.line ? " (line " + e.line + ")" : ""));
            }
        }
        layer.zOrder(ZOrderMethod.BRINGTOFRONT);
        layer.locked = wasLocked;
        layer.visible = wasVisible;
    }
    // Protected layers: created empty if missing, kept on top, never cleared.
    for (var q = 0; q < dw.protectedLayers.length; q++) PD.layer(doc, dw.protectedLayers[q]).zOrder(ZOrderMethod.BRINGTOFRONT);
    PD.purgeStaleLayers(doc, dw);
    PD.removeDefaultLayer(doc, dw);

    doc.saveAs(file, new IllustratorSaveOptions());
    if (opts.png) PD.exportPng(doc, opts.outDir + "/" + dw.fileName.replace(/\.ai$/i, ""), opts.pngScale || 300);
    doc.close(SaveOptions.DONOTSAVECHANGES);
};

// One PNG per artboard, clipped to the artboard (drawings are at true scale; the PNG resolution is pngScale %).
PD.exportPng = function (doc, base, scale) {
    for (var i = 0; i < doc.artboards.length; i++) {
        doc.artboards.setActiveArtboardIndex(i);
        var o = new ExportOptionsPNG24();
        o.artBoardClipping = true;
        o.antiAliasing = true;
        o.transparency = false;
        o.horizontalScale = scale;
        o.verticalScale = scale;
        var name = doc.artboards.length > 1 ? base + "_" + (i + 1) + ".png" : base + ".png";
        doc.exportFile(new File(name), ExportType.PNG24, o);
    }
};

// Artboards keep the plugin's row layout (x, y from the drawing), centred on where the first artboard started so
// many-artboard files (plans) stay inside Illustrator's canvas.
PD.setupArtboards = function (doc, abs) {
    var r0 = doc.artboards[0].artboardRect;
    var cx = (r0[0] + r0[2]) / 2, cy = (r0[1] + r0[3]) / 2;
    var lw = 0, lh = 0;
    for (var k = 0; k < abs.length; k++) {
        lw = Math.max(lw, (abs[k].x || 0) + abs[k].w);
        lh = Math.max(lh, (abs[k].y || 0) + abs[k].h);
    }
    if (lw > 16000 || lh > 16000) throw new Error("artboards need " + Math.round(lw) + " x " + Math.round(lh) + " pt: larger than the Illustrator canvas; use a smaller scale");
    var left = cx - lw / 2, top = cy + lh / 2;
    for (var i = 0; i < abs.length; i++) {
        var x = left + (abs[i].x || 0), y = top - (abs[i].y || 0);
        var rect = [x, y, x + abs[i].w, y - abs[i].h];
        var ab = i < doc.artboards.length ? doc.artboards[i] : doc.artboards.add(rect);
        ab.artboardRect = rect;
        ab.name = abs[i].name;
    }
};

PD.layer = function (doc, name) {
    try { return doc.layers.getByName(name); } catch (e) { }
    var l = doc.layers.add();
    l.name = name;
    return l;
};

// Layers an earlier plugin version generated but this drawing no longer uses (e.g. PD_CONTEXT from v0.2.1):
// drop their named items, and the layer when it is then empty. Unnamed user items keep the layer alive.
PD.purgeStaleLayers = function (doc, dw) {
    var keep = {};
    for (var i = 0; i < dw.layers.length; i++) keep[dw.layers[i].name] = true;
    for (var j = 0; j < dw.protectedLayers.length; j++) keep[dw.protectedLayers[j]] = true;
    for (var k = doc.layers.length - 1; k >= 0; k--) {
        var l = doc.layers[k];
        if (keep[l.name] || !/^(PD_|VIEW_|BG_|AXON_|FACADE$)/.test(l.name)) continue;
        l.locked = false;
        l.visible = true;
        PD.clearNamed(l);
        if (l.pageItems.length === 0 && l.layers.length === 0) l.remove();
    }
};

// A fresh document starts with "Layer 1"; drop it when it is empty and not one of ours.
PD.removeDefaultLayer = function (doc, dw) {
    for (var i = doc.layers.length - 1; i >= 0; i--) {
        var l = doc.layers[i];
        if (l.name === "Layer 1" && l.pageItems.length === 0 && doc.layers.length > 1) l.remove();
    }
};

// Removes items the plugin drew last time (they all carry a name); unnamed user items stay.
PD.clearNamed = function (layer) {
    for (var i = layer.pageItems.length - 1; i >= 0; i--) {
        var it = layer.pageItems[i];
        if (it.name && it.name.length > 0) {
            it.locked = false;
            it.remove();
        }
    }
};

PD.hexToRgb = function (hex) {
    var c = new RGBColor();
    c.red = parseInt(hex.substr(1, 2), 16);
    c.green = parseInt(hex.substr(3, 2), 16);
    c.blue = parseInt(hex.substr(5, 2), 16);
    return c;
};

// Global process swatch: an existing one with the same name is recoloured in place.
PD.swatch = function (doc, name, hex) {
    var spot = null;
    try { spot = doc.spots.getByName(name); } catch (e) { spot = null; }
    if (spot === null) {
        spot = doc.spots.add();
        spot.name = name;
        spot.colorType = ColorModel.PROCESS;
    }
    spot.color = PD.hexToRgb(hex);
    var sc = new SpotColor();
    sc.spot = spot;
    sc.tint = 100;
    return sc;
};

PD.paraStyle = function (doc, st, sw) {
    var ps = null;
    try { ps = doc.paragraphStyles.getByName(st.name); } catch (e) { ps = doc.paragraphStyles.add(st.name); }
    var ca = ps.characterAttributes;
    ca.size = st.sizePt;
    try { ca.textFont = app.textFonts.getByName(st.font); } catch (e1) {
        try { ca.textFont = app.textFonts.getByName(st.fallbackFont); } catch (e2) { }
    }
    ca.capitalization = st.allCaps ? FontCapsOption.ALLCAPS : FontCapsOption.NORMALCAPS;
    ca.tracking = st.tracking || 0;
    if (st.leadingPt > 0) { ca.autoLeading = false; ca.leading = st.leadingPt; } else ca.autoLeading = true;
    if (sw[st.color]) ca.fillColor = sw[st.color];
    ps.paragraphAttributes.justification = st.align === "center" ? Justification.CENTER
        : st.align === "right" ? Justification.RIGHT : Justification.LEFT;
    return ps;
};

PD.pt = function (rect, x, y) { return [rect[0] + x, rect[1] - y]; };

PD.style = function (item, it, sw) {
    item.filled = !!(it.fill && sw[it.fill]);
    if (item.filled) item.fillColor = sw[it.fill];
    item.stroked = !!(it.stroke && sw[it.stroke] && it.strokePt > 0);
    if (item.stroked) {
        item.strokeColor = sw[it.stroke];
        item.strokeWidth = it.strokePt;
        item.strokeDashes = it.dash ? it.dash : [];
    }
};

PD.drawItem = function (layer, it, rect, sw, ps) {
    var item;
    if (it.kind === "Path") {
        if (!it.paths || it.paths.length === 0) return;
        if (it.paths.length === 1) {
            item = PD.pathItem(layer.pathItems, it.paths[0], it.closed, rect);
            PD.style(item, it, sw);
        } else {
            item = layer.compoundPathItems.add();
            for (var i = 0; i < it.paths.length; i++) PD.style(PD.pathItem(item.pathItems, it.paths[i], it.closed, rect), it, sw);
            // A compound path takes its look from its paths; style them all again once complete, so no sub-path keeps
            // Illustrator's default black stroke (green / water areas read as fill + hatch, never fill + black line).
            for (var j = 0; j < item.pathItems.length; j++) PD.style(item.pathItems[j], it, sw);
        }
    } else if (it.kind === "RoundRect") {
        var tl = PD.pt(rect, it.x, it.y);
        item = layer.pathItems.roundedRectangle(tl[1], tl[0], it.w, it.h, it.r, it.r);
        PD.style(item, it, sw);
    } else if (it.kind === "Text") {
        if (it.matrix) {
            item = PD.planeText(layer, it, rect, ps);
        } else {
            item = layer.textFrames.pointText(PD.pt(rect, it.x, it.y));
            item.contents = it.text.replace(/\n/g, "\r");
            if (ps[it.paraStyle]) ps[it.paraStyle].applyTo(item.textRange, true);
            if (it.sizePt) item.textRange.characterAttributes.size = it.sizePt;
            if (it.rotation) item.rotate(it.rotation); // VERIFY-WIN: rotates about the frame centre
            if (it.glyphAnchor) PD.anchorOnGlyphs(item, PD.pt(rect, it.x, it.y), it.glyphAnchor);
            else if (it.center) PD.centerOnGlyphs(item, PD.pt(rect, it.x, it.y));
        }
        var spec = PD.specs[it.paraStyle];
        if (spec && spec.opacity !== undefined && spec.opacity < 1 && !(it.opacity < 1)) item.opacity = spec.opacity * 100;
    } else if (it.kind === "Symbol") {
        item = PD.symbolItem(layer, it, rect, sw);
    } else {
        return;
    }
    if (it.opacity !== undefined && it.opacity < 1) item.opacity = it.opacity * 100;
    item.name = it.name;
};

// Text laid on a plane (axon labels): built at the document origin, centred on its glyphs there, carried onto the
// page by the drawing's matrix (y-down page matrix [a,b,c,d] → Illustrator y-up matrix), then moved to its anchor.
// The frame stays live text. VERIFY-WIN: transform about DOCUMENTORIGIN with the document coordinate system.
PD.planeText = function (layer, it, rect, ps) {
    var tf = layer.textFrames.pointText([0, 0]);
    tf.contents = it.text.replace(/\n/g, "\r");
    if (ps[it.paraStyle]) ps[it.paraStyle].applyTo(tf.textRange, true);
    if (it.sizePt) tf.textRange.characterAttributes.size = it.sizePt;
    // Front-corner labels: left / right aligned on the anchor (the paragraph style is centred).
    tf.textRange.paragraphAttributes.justification = it.align === "right" ? Justification.RIGHT
        : it.align === "left" ? Justification.LEFT : Justification.CENTER;
    if (it.center) PD.centerOnGlyphs(tf, [0, 0]);
    var m = app.getIdentityMatrix();
    m.mValueA = it.matrix[0];
    m.mValueB = -it.matrix[1];
    m.mValueC = -it.matrix[2];
    m.mValueD = it.matrix[3];
    m.mValueTX = 0;
    m.mValueTY = 0;
    tf.transform(m, true, true, true, true, 1, Transformation.DOCUMENTORIGIN);
    var target = PD.pt(rect, it.x, it.y);
    tf.translate(target[0], target[1]);
    return tf;
};

// Places a text frame by its outlined glyph box: right (or left) edge on target x, vertical centre on target y.
PD.anchorOnGlyphs = function (tf, target, mode) {
    var dup = tf.duplicate();
    var outlined = dup.createOutline();
    var b = outlined.geometricBounds; // [left, top, right, bottom]
    outlined.remove();
    var x = mode === "right" ? b[2] : mode === "left" ? b[0] : (b[0] + b[2]) / 2;
    tf.translate(target[0] - x, target[1] - (b[1] + b[3]) / 2);
};

// Moves a text frame so the centre of its outlined glyphs lands on target (exact centring in a colour block).
PD.centerOnGlyphs = function (tf, target) {
    var dup = tf.duplicate();
    var outlined = dup.createOutline();
    var b = outlined.geometricBounds; // [left, top, right, bottom]
    outlined.remove();
    tf.translate(target[0] - (b[0] + b[2]) / 2, target[1] - (b[1] + b[3]) / 2);
};

// Entourage symbols: defined once per document from the drawing's art. A same-named symbol already in the file is
// kept, so art the office replaces in Illustrator survives every re-export. VERIFY-WIN: symbols.add / symbolItems.add.
PD.symbols = function (doc, defs, sw) {
    var out = {};
    if (!defs) return out;
    for (var i = 0; i < defs.length; i++) {
        var def = defs[i], sym = null;
        try { sym = doc.symbols.getByName(def.name); } catch (e) { sym = null; }
        if (!sym) {
            var tmp = null;
            try {
                tmp = doc.layers.add();
                var g = PD.symbolArt(tmp, def, [0, 0], [1, 0, 0, 1], [0, 0], sw);
                sym = doc.symbols.add(g);
                sym.name = def.name;
            } catch (e2) {
                sym = null;
            }
            if (tmp) tmp.remove();
        }
        out[def.name] = { sym: sym, def: def };
    }
    return out;
};

// Symbol art as a group of compound paths: symbol point p → (x, y) + M·p on the artboard.
PD.symbolArt = function (layer, def, at, m, rect, sw) {
    var g = layer.groupItems.add();
    for (var p = 0; p < def.parts.length; p++) {
        var cp = g.compoundPathItems.add();
        for (var r = 0; r < def.parts[p].length; r++) {
            var ring = def.parts[p][r], pts = [];
            for (var k = 0; k < ring.length; k++)
                pts.push([at[0] + m[0] * ring[k][0] + m[2] * ring[k][1], at[1] + m[1] * ring[k][0] + m[3] * ring[k][1]]);
            PD.style(PD.pathItem(cp.pathItems, pts, true, rect), { fill: def.fill }, sw);
        }
    }
    return g;
};

// Symbol instance: scaled / mirrored / rotated by the item's matrix, then moved so its centre matches the plugin's.
// Falls back to plain grouped paths when the document has no symbol for it.
PD.symbolItem = function (layer, it, rect, sw) {
    var s = PD.syms[it.symbol];
    if (!s) throw "symbol " + it.symbol + " missing";
    var m = it.matrix, b = s.def.bounds;
    if (!s.sym) return PD.symbolArt(layer, s.def, [it.x, it.y], m, rect, sw);
    // Centre of the symbol's bounds on the page (exact under scale, mirror and rotation of centred art).
    var bx = (b[0] + b[2]) / 2, by = (b[1] + b[3]) / 2;
    var target = PD.pt(rect, it.x + m[0] * bx + m[2] * by, it.y + m[1] * bx + m[3] * by);
    var item = layer.symbolItems.add(s.sym);
    var t = app.getIdentityMatrix();
    t.mValueA = m[0];
    t.mValueB = -m[1];
    t.mValueC = -m[2];
    t.mValueD = m[3];
    item.transform(t, true, true, true, true, 1, Transformation.CENTER);
    var g = item.geometricBounds;
    item.translate(target[0] - (g[0] + g[2]) / 2, target[1] - (g[1] + g[3]) / 2);
    return item;
};

PD.pathItem = function (collection, pts, closed, rect) {
    var p = collection.add();
    var arr = [];
    for (var i = 0; i < pts.length; i++) arr.push(PD.pt(rect, pts[i][0], pts[i][1]));
    p.setEntirePath(arr);
    p.closed = closed;
    return p;
};
