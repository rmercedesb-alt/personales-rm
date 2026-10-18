// Joceando mis cuartos - Incentivo Lluvias Mayo 2026
// AM Comercial, S.A.S.

const pptxgen = require("pptxgenjs");

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE"; // 13.3" x 7.5"
pres.title = "Joceando mis cuartos - Incentivo Lluvias Mayo 2026";
pres.author = "AM Comercial, S.A.S.";

// Color palette
const C = {
  navy:       "1B3A6B",
  navyDark:   "0D1F3C",
  navyMid:    "132047",
  gold:       "F5A623",
  goldDark:   "D4891A",
  lightBlue:  "4A90D9",
  skyBlue:    "E8F4FD",
  white:      "FFFFFF",
  green:      "27AE60",
  gray:       "6B7280",
  grayLight:  "9CA3AF",
  cardBlue:   "1F4B8E",
  cardGold:   "7A4A00",
  cardGreen:  "1A5C36",
  rowAlt:     "0D1F3C",
  rowNorm:    "132047",
  tableHead:  "1B3A6B",
  text:       "D1D5DB",
  darkText:   "374151",
};

const LOGO_PATH = "C:\\Personales RM\\Claude\\logo_am_comercial.png";
const makeShadow = () => ({ type: "outer", color: "000000", blur: 8, offset: 2, angle: 135, opacity: 0.15 });

// ===========================================================
// SLIDE 1 — PORTADA / COVER
// ===========================================================
{
  const s = pres.addSlide();
  s.background = { color: C.navyDark };

  // Decorative vertical rain strips (left-side ambience)
  const strips = [
    { x: 0.0,  w: 0.8,  transparency: 92 },
    { x: 2.2,  w: 0.35, transparency: 90 },
    { x: 4.8,  w: 0.25, transparency: 93 },
    { x: 7.5,  w: 0.4,  transparency: 91 },
    { x: 10.2, w: 0.3,  transparency: 92 },
    { x: 12.5, w: 0.8,  transparency: 90 },
  ];
  strips.forEach(st => {
    s.addShape(pres.shapes.RECTANGLE, {
      x: st.x, y: 0, w: st.w, h: 7.5,
      fill: { color: C.lightBlue, transparency: st.transparency },
      line: { width: 0 },
    });
  });

  // Gold bottom bar
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 6.75, w: 13.3, h: 0.75,
    fill: { color: C.gold },
    line: { width: 0 },
  });

  // Top thin accent bar (light blue)
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 13.3, h: 0.12,
    fill: { color: C.lightBlue },
    line: { width: 0 },
  });

  // Company logo
  s.addImage({
    path: LOGO_PATH,
    x: 5.15, y: 0.18, w: 3.0, h: 1.1,
    sizing: { type: "contain", w: 3.0, h: 1.1 },
  });

  // Rain emojis decorative row
  s.addText("🌧️  🌧️  🌧️  🌧️  🌧️", {
    x: 0.5, y: 1.45, w: 12.3, h: 0.6,
    align: "center",
    fontSize: 30,
    color: C.lightBlue,
    transparency: 40,
    fontFace: "Calibri",
  });

  // Main title — huge
  s.addText("JOCEANDO MIS CUARTOS 💰", {
    x: 0.4, y: 1.95, w: 12.5, h: 1.7,
    align: "center",
    fontSize: 52,
    bold: true,
    color: C.white,
    fontFace: "Calibri",
    shadow: makeShadow(),
  });

  // Colored divider accent bar under title
  s.addShape(pres.shapes.RECTANGLE, {
    x: 3.5, y: 3.72, w: 6.3, h: 0.07,
    fill: { color: C.gold },
    line: { width: 0 },
  });

  // Subtitle
  s.addText("Incentivo de Lluvias | Canal Minorista", {
    x: 0.4, y: 3.85, w: 12.5, h: 0.65,
    align: "center",
    fontSize: 24,
    bold: true,
    color: C.gold,
    fontFace: "Calibri",
    charSpacing: 2,
  });

  // Date line
  s.addText("Mayo 2026 — Últimos 5 Días del Mes", {
    x: 0.4, y: 4.5, w: 12.5, h: 0.5,
    align: "center",
    fontSize: 17,
    italic: true,
    color: C.white,
    fontFace: "Calibri",
  });

  // Tagline pill box
  s.addShape(pres.shapes.RECTANGLE, {
    x: 4.15, y: 5.15, w: 5.0, h: 0.75,
    fill: { color: C.lightBlue, transparency: 25 },
    line: { color: C.lightBlue, width: 1.5 },
    shadow: makeShadow(),
  });
  s.addText("🌧️  ¡El que sale, GANA!", {
    x: 4.15, y: 5.15, w: 5.0, h: 0.75,
    align: "center",
    valign: "middle",
    fontSize: 20,
    bold: true,
    color: C.white,
    fontFace: "Calibri",
    margin: 0,
  });

  // Footer text on gold bar
  s.addText("AM Comercial, S.A.S.  ·  Incentivos Comerciales  ·  Confidencial  ·  Mayo 2026", {
    x: 0.4, y: 6.79, w: 12.5, h: 0.65,
    align: "center",
    valign: "middle",
    fontSize: 12,
    bold: true,
    color: C.navyDark,
    fontFace: "Calibri",
    margin: 0,
  });
}

// ===========================================================
// SLIDE 2 — MECÁNICA DEL INCENTIVO
// ===========================================================
{
  const s = pres.addSlide();
  s.background = { color: C.skyBlue };

  // Top navy header bar
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 13.3, h: 1.25,
    fill: { color: C.navy },
    line: { width: 0 },
  });

  // Title in header
  s.addText("¿Cómo funciona el Incentivo? 💡", {
    x: 0.4, y: 0.05, w: 12.5, h: 0.72,
    align: "center",
    fontSize: 30,
    bold: true,
    color: C.white,
    fontFace: "Calibri",
    margin: 0,
  });

  // Subtitle in header
  s.addText("Simple, justo y emocionante — ¡Cada día cuenta!", {
    x: 0.4, y: 0.76, w: 12.5, h: 0.42,
    align: "center",
    fontSize: 14,
    italic: true,
    color: C.gold,
    fontFace: "Calibri",
    margin: 0,
  });

  const colW  = 5.85;
  const cardH = 5.95;
  const cardY = 1.35;

  // ── LEFT CARD: VENDEDOR ──────────────────────────────────
  const lx = 0.4;

  s.addShape(pres.shapes.RECTANGLE, {
    x: lx, y: cardY, w: colW, h: cardH,
    fill: { color: C.white },
    line: { color: "BDD9F5", width: 1 },
    shadow: makeShadow(),
  });

  // Card header (blue)
  s.addShape(pres.shapes.RECTANGLE, {
    x: lx, y: cardY, w: colW, h: 0.58,
    fill: { color: C.lightBlue },
    line: { width: 0 },
  });
  s.addText("👤  VENDEDORES MINORISTAS", {
    x: lx, y: cardY, w: colW, h: 0.58,
    align: "center", valign: "middle",
    fontSize: 16, bold: true,
    color: C.white, fontFace: "Calibri", margin: 0,
  });

  // Bullet rows — vendedor
  const vendRows = [
    { e: "📅", bold: "Período:", txt: "27 al 31 de Mayo, 2026 (5 días hábiles)" },
    { e: "🎯", bold: "Meta:", txt: "Lograr el presupuesto diario asignado" },
    { e: "💵", bold: "Premio por día cumplido:", txt: "RD$ 500.00" },
    { e: "💰", bold: "Premio máximo acumulado:", txt: "RD$ 2,500.00" },
    { e: "✅", bold: "¿Cómo funciona?", txt: "¡Es ACUMULATIVO! Cada día que cumplas tu meta, sumas RD$ 500 más." },
  ];
  vendRows.forEach((row, i) => {
    const yy = cardY + 0.72 + i * 0.54;
    s.addText([
      { text: row.e + "  ", options: { fontSize: 14 } },
      { text: row.bold + " ", options: { bold: true, fontSize: 12.5, color: C.navy } },
      { text: row.txt, options: { fontSize: 12.5, color: C.darkText } },
    ], { x: lx + 0.18, y: yy, w: colW - 0.36, h: 0.5, valign: "middle", fontFace: "Calibri" });
  });

  // Vendedor example label
  s.addText("📊  Ejemplo — ¿Cuánto Ganas?", {
    x: lx + 0.18, y: cardY + 3.55, w: colW - 0.36, h: 0.36,
    fontSize: 12.5, bold: true, color: C.navy, fontFace: "Calibri",
  });

  // Vendedor table
  const mkHdr = (txt) => ({ text: txt, options: { bold: true, color: C.white, fill: { color: C.navy }, align: "center", fontSize: 11, valign: "middle" } });
  const mkCell = (txt, isWinner) => ({
    text: txt,
    options: {
      align: "center", fontSize: 11,
      color: isWinner ? C.green : C.darkText,
      bold: isWinner,
      fill: { color: isWinner ? "EAF7EF" : C.white },
      valign: "middle",
    },
  });

  s.addTable([
    [mkHdr("Días Cumplidos"), mkHdr("Premio Ganado")],
    [mkCell("1 día", false), mkCell("RD$ 500", false)],
    [mkCell("2 días", false), mkCell("RD$ 1,000", false)],
    [mkCell("3 días", false), mkCell("RD$ 1,500", false)],
    [mkCell("4 días", false), mkCell("RD$ 2,000", false)],
    [mkCell("5 días ⭐", true), mkCell("RD$ 2,500", true)],
  ], {
    x: lx + 0.8, y: cardY + 3.95, w: colW - 1.6, h: 1.82,
    border: { pt: 0.5, color: "D1D5DB" },
    rowH: 0.3,
    colW: [(colW - 1.6) / 2, (colW - 1.6) / 2],
  });

  // ── RIGHT CARD: SUPERVISOR ───────────────────────────────
  const rx = 7.05;

  s.addShape(pres.shapes.RECTANGLE, {
    x: rx, y: cardY, w: colW, h: cardH,
    fill: { color: C.white },
    line: { color: "F5E0A0", width: 1 },
    shadow: makeShadow(),
  });

  // Card header (gold)
  s.addShape(pres.shapes.RECTANGLE, {
    x: rx, y: cardY, w: colW, h: 0.58,
    fill: { color: C.gold },
    line: { width: 0 },
  });
  s.addText("🏆  SUPERVISORES", {
    x: rx, y: cardY, w: colW, h: 0.58,
    align: "center", valign: "middle",
    fontSize: 16, bold: true,
    color: C.navyDark, fontFace: "Calibri", margin: 0,
  });

  // Bullet rows — supervisor
  const supRows = [
    { e: "📅", bold: "Período:", txt: "27 al 31 de Mayo, 2026 (5 días hábiles)" },
    { e: "🎯", bold: "Meta:", txt: "Apoyar a su equipo a lograr las metas diarias" },
    { e: "💵", bold: "Premio por día cumplido:", txt: "RD$ 1,000.00" },
    { e: "💰", bold: "Premio máximo acumulado:", txt: "RD$ 5,000.00" },
    { e: "✅", bold: "¿Cómo funciona?", txt: "¡También ACUMULATIVO! Gana RD$ 1,000 por cada día que su equipo cumpla." },
  ];
  supRows.forEach((row, i) => {
    const yy = cardY + 0.72 + i * 0.54;
    s.addText([
      { text: row.e + "  ", options: { fontSize: 14 } },
      { text: row.bold + " ", options: { bold: true, fontSize: 12.5, color: C.navyDark } },
      { text: row.txt, options: { fontSize: 12.5, color: C.darkText } },
    ], { x: rx + 0.18, y: yy, w: colW - 0.36, h: 0.5, valign: "middle", fontFace: "Calibri" });
  });

  // Supervisor example label
  s.addText("📊  Ejemplo — ¿Cuánto Ganas?", {
    x: rx + 0.18, y: cardY + 3.55, w: colW - 0.36, h: 0.36,
    fontSize: 12.5, bold: true, color: C.navyDark, fontFace: "Calibri",
  });

  const mkGoldHdr = (txt) => ({ text: txt, options: { bold: true, color: C.white, fill: { color: C.goldDark }, align: "center", fontSize: 11, valign: "middle" } });

  s.addTable([
    [mkGoldHdr("Días Cumplidos"), mkGoldHdr("Premio Ganado")],
    [mkCell("1 día", false), mkCell("RD$ 1,000", false)],
    [mkCell("3 días", false), mkCell("RD$ 3,000", false)],
    [mkCell("5 días ⭐", true), mkCell("RD$ 5,000", true)],
  ], {
    x: rx + 0.8, y: cardY + 3.95, w: colW - 1.6, h: 1.2,
    border: { pt: 0.5, color: "D1D5DB" },
    rowH: 0.3,
    colW: [(colW - 1.6) / 2, (colW - 1.6) / 2],
  });

  // Bottom footnote
  s.addText("* El logro se evalúa diariamente con base en el presupuesto calculado del mes.", {
    x: 0.4, y: 7.18, w: 12.5, h: 0.28,
    align: "center",
    fontSize: 9.5,
    italic: true,
    color: C.gray,
    fontFace: "Calibri",
  });
}

// ===========================================================
// SLIDE 3 — ANÁLISIS FINANCIERO
// ===========================================================
{
  const s = pres.addSlide();
  s.background = { color: C.navyDark };

  // Top gold accent strip
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 13.3, h: 0.1,
    fill: { color: C.gold },
    line: { width: 0 },
  });

  // Title
  s.addText("El Negocio del Incentivo 📊", {
    x: 0.4, y: 0.12, w: 12.5, h: 0.7,
    align: "center",
    fontSize: 30,
    bold: true,
    color: C.white,
    fontFace: "Calibri",
    margin: 0,
  });

  // Subtitle
  s.addText("Inversión vs. Retorno — Canal Minorista | 5 Días | Mayo 2026", {
    x: 0.4, y: 0.8, w: 12.5, h: 0.38,
    align: "center",
    fontSize: 14,
    italic: true,
    color: C.gold,
    fontFace: "Calibri",
    margin: 0,
  });

  // ── 3 METRIC CARDS ────────────────────────────────────────
  const cardY = 1.28;
  const cardH = 1.42;

  const metricCards = [
    {
      x: 0.3, w: 3.95,
      bg: C.cardBlue, accent: C.lightBlue,
      emoji: "📈",
      label: "Meta de Ventas (5 Días)",
      value: "RD$ 12,843,542",
      sub: "53 vendedores · 5 días",
    },
    {
      x: 4.68, w: 3.95,
      bg: C.cardGold, accent: C.gold,
      emoji: "💰",
      label: "Inversión Total en Incentivos",
      value: "RD$ 167,500",
      sub: "Reps: RD$132,500  |  Sups: RD$35,000",
    },
    {
      x: 9.05, w: 4.0,
      bg: C.cardGreen, accent: C.green,
      emoji: "🎯",
      label: "Incidencia de la Inversión",
      value: "1.30%",
      sub: "RD$1.30 invertidos por cada RD$100 vendidos",
    },
  ];

  metricCards.forEach(card => {
    // Card body
    s.addShape(pres.shapes.RECTANGLE, {
      x: card.x, y: cardY, w: card.w, h: cardH,
      fill: { color: card.bg },
      line: { color: card.accent, width: 2 },
      shadow: makeShadow(),
    });
    // Top accent strip on card
    s.addShape(pres.shapes.RECTANGLE, {
      x: card.x, y: cardY, w: card.w, h: 0.07,
      fill: { color: card.accent },
      line: { width: 0 },
    });
    // Emoji
    s.addText(card.emoji, {
      x: card.x + 0.12, y: cardY + 0.08, w: 0.6, h: 0.42,
      fontSize: 22, align: "center", margin: 0,
    });
    // Label
    s.addText(card.label, {
      x: card.x + 0.1, y: cardY + 0.48, w: card.w - 0.2, h: 0.36,
      fontSize: 11, bold: true, color: card.accent,
      align: "center", fontFace: "Calibri", margin: 0,
    });
    // Big value
    s.addText(card.value, {
      x: card.x + 0.1, y: cardY + 0.82, w: card.w - 0.2, h: 0.42,
      fontSize: card.value.length > 8 ? 20 : 28,
      bold: true, color: C.white,
      align: "center", fontFace: "Calibri", margin: 0,
    });
    // Sub text
    s.addText(card.sub, {
      x: card.x + 0.1, y: cardY + 1.22, w: card.w - 0.2, h: 0.28,
      fontSize: 9, color: "94A3B8",
      align: "center", italic: true, fontFace: "Calibri", margin: 0,
    });
  });

  // ── TABLE SECTION ─────────────────────────────────────────
  // Section label
  s.addText("▸  Detalle por Supervisor", {
    x: 0.3, y: 2.82, w: 8, h: 0.34,
    fontSize: 13, bold: true, color: C.gold, fontFace: "Calibri", margin: 0,
  });

  const mkTH = (txt, align = "center") => ({
    text: txt,
    options: {
      bold: true, color: C.white,
      fill: { color: C.tableHead },
      align, fontSize: 10, valign: "middle", fontFace: "Calibri",
    },
  });
  const mkTD = (txt, align = "center", bold = false, highlight = false) => ({
    text: txt,
    options: {
      align, fontSize: 9.5, bold,
      color: bold ? C.gold : C.text,
      fill: { color: highlight ? C.navy : undefined },
      valign: "middle", fontFace: "Calibri",
    },
  });

  const tableData = [
    [mkTH("Supervisor", "left"), mkTH("Vend."), mkTH("Meta 5 Días"), mkTH("Incent. Reps"), mkTH("Incent. Sup"), mkTH("Total Incent."), mkTH("Incidencia")],
    ...[
      ["Disponible Sur Corto (SC)",      "12", "RD$ 2,881,732", "RD$ 30,000", "RD$ 5,000", "RD$ 35,000", "1.21%"],
      ["Fernando Martinez (DN)",          "5",  "RD$ 1,033,256", "RD$ 12,500", "RD$ 5,000", "RD$ 17,500", "1.69%"],
      ["Fernando Martinez (SDO)",         "5",  "RD$ 1,172,896", "RD$ 12,500", "RD$ 5,000", "RD$ 17,500", "1.49%"],
      ["Narciso Pina Bello (Sur Largo)",  "14", "RD$ 4,109,700", "RD$ 35,000", "RD$ 5,000", "RD$ 40,000", "0.97%"],
      ["Ramon E. Pimentel (BC)",           "2",  "RD$ 543,953",  "RD$ 5,000",  "RD$ 5,000", "RD$ 10,000", "1.84%"],
      ["Ramon E. Pimentel (SDE)",          "8",  "RD$ 1,696,750", "RD$ 20,000", "RD$ 5,000", "RD$ 25,000", "1.47%"],
      ["Samuel Valenzuela (SDN)",          "7",  "RD$ 1,405,256", "RD$ 17,500", "RD$ 5,000", "RD$ 22,500", "1.60%"],
    ].map((row, i) => [
      { text: row[0], options: { align: "left", fontSize: 9.5, color: C.text, fill: { color: i % 2 === 0 ? C.rowNorm : C.rowAlt }, valign: "middle", fontFace: "Calibri" } },
      ...row.slice(1).map(cell => ({
        text: cell,
        options: { align: "center", fontSize: 9.5, color: C.text, fill: { color: i % 2 === 0 ? C.rowNorm : C.rowAlt }, valign: "middle", fontFace: "Calibri" },
      })),
    ]),
    // Total row
    [
      mkTD("TOTAL COMPAÑÍA", "left", true, true),
      mkTD("53", "center", true, true),
      mkTD("RD$ 12,843,542", "center", true, true),
      mkTD("RD$ 132,500", "center", true, true),
      mkTD("RD$ 35,000", "center", true, true),
      mkTD("RD$ 167,500", "center", true, true),
      mkTD("1.30%", "center", true, true),
    ],
  ];

  s.addTable(tableData, {
    x: 0.3, y: 3.18, w: 12.7, h: 2.55,
    border: { pt: 0.3, color: "1E3A5F" },
    colW: [3.15, 0.6, 1.95, 1.75, 1.75, 1.9, 1.6],
    rowH: 0.28,
  });

  // ── CONCLUSION BOX ────────────────────────────────────────
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0.3, y: 5.9, w: 12.7, h: 1.02,
    fill: { color: C.cardBlue },
    line: { color: C.gold, width: 1.5 },
    shadow: makeShadow(),
  });

  // Left gold accent bar on conclusion
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0.3, y: 5.9, w: 0.08, h: 1.02,
    fill: { color: C.gold },
    line: { width: 0 },
  });

  s.addText([
    { text: "Conclusión Ejecutiva:  ", options: { bold: true, color: C.gold, fontSize: 12, fontFace: "Calibri" } },
    { text: "Por una inversión de RD$ 167,500 activamos a 53 vendedores y 7 supervisores del canal Minorista durante los últimos 5 días de Mayo 2026. La incidencia total del incentivo es del 1.30% sobre la meta de ventas — una inversión de alto impacto con bajo riesgo financiero.", options: { color: C.white, fontSize: 11.5, fontFace: "Calibri" } },
  ], {
    x: 0.55, y: 5.93, w: 12.25, h: 0.96,
    valign: "middle",
    wrap: true,
  });

  // Footer bar
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 7.22, w: 13.3, h: 0.28,
    fill: { color: C.navy },
    line: { width: 0 },
  });
  s.addText("AM Comercial, S.A.S.  ·  Incentivos Comerciales  ·  Mayo 2026", {
    x: 0.4, y: 7.22, w: 12.5, h: 0.28,
    align: "center", valign: "middle",
    fontSize: 9, color: "94A3B8", fontFace: "Calibri", margin: 0,
  });
}

// ===========================================================
// WRITE FILE
// ===========================================================
pres.writeFile({
  fileName: "C:\\Personales RM\\Claude\\Joceando mis cuartos - Incentivo Lluvias Mayo 2026.pptx",
})
.then(() => console.log("✅ Presentación creada: Joceando mis cuartos - Incentivo Lluvias Mayo 2026.pptx"))
.catch(err => { console.error("❌ Error:", err); process.exit(1); });
