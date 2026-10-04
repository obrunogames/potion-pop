// Store screenshots: a caption in the game font, the game's own product art as decoration and a real, unchanged
// capture of the game (Screenshots/qa/store_<lang>_<phone|tablet>_<scene>.png, taken by QaTour in the editor).
// Writes Docs/loja/capturas/<locale>/<iphone|ipad|android|android-tablet>/NN-scene.png plus the Play icon and
// feature graphic in Docs/loja/imagens/. Usage: node Tools/release/promo_screenshots.mjs [--preview]
import { readFileSync, mkdirSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { homedir } from 'node:os';

const root = join(dirname(fileURLToPath(import.meta.url)), '../..');
const require = createRequire(import.meta.url);
const firstExisting = list => list.filter(Boolean).find(existsSync);
const sharpPath = firstExisting([process.env.POTIONPOP_SHARP_MODULE, join(root, 'node_modules/sharp'), join(homedir(), 'Desktop/projetos/brunogames/node_modules/sharp')]);
const opentypePath = firstExisting([process.env.POTIONPOP_OPENTYPE_MODULE, join(root, 'node_modules/opentype.js'), join(homedir(), 'Desktop/games/car-racing/Builds/store-design-runtime/node_modules/opentype.js')]);
if (!sharpPath || !opentypePath) throw new Error('Set POTIONPOP_SHARP_MODULE and POTIONPOP_OPENTYPE_MODULE to installed sharp / opentype.js modules.');
const sharp = require(sharpPath);
const opentype = require(opentypePath);

const shots = join(root, 'Screenshots/qa');
const artDir = join(root, 'Assets/_Game/Resources/Art');
const loja = join(root, 'Docs/loja');
const fontBuffer = readFileSync(join(root, 'Assets/_Game/Fonts/LilitaOne-Regular.ttf'));
const font = opentype.parse(fontBuffer.buffer.slice(fontBuffer.byteOffset, fontBuffer.byteOffset + fontBuffer.byteLength));
const locales = { 'pt-BR': 'pt', 'en-US': 'en', 'es-419': 'es' };
const n = Math.round;

// Store slots: canvas size and which capture goes inside (phone = 1080x2340, tablet = 1536x2048).
const kinds = {
  iphone: { W: 1320, H: 2868, capture: 'phone' },          // App Store 6.9"
  ipad: { W: 2064, H: 2752, capture: 'tablet' },           // App Store 13"
  android: { W: 1080, H: 1920, capture: 'phone' },          // Play phone: the Console now asks for 16:9 / 9:16
  'android-tablet': { W: 1080, H: 1920, capture: 'tablet' }, // Play 7" and 10" tablets
};

// Store order. palette: gradient top, gradient bottom, glow. props: sprites (Resources/Art) around the caption.
const cards = [
  { scene: '01_game', palette: ['#A35CFF', '#4B1FA8', '#E6CCFF'], props: ['card_rainbow_potion', 'card_starlight_potion', 'card_moon_potion', 'card_mermaid_potion'],
    copy: { pt: ['SEPARE AS CORES', 'toque e despeje as poções'], en: ['SORT THE COLORS', 'tap and pour the potions'], es: ['ORDENA LOS COLORES', 'toca y vierte las pociones'] } },
  { scene: '03_home', palette: ['#FF7EB8', '#C2347A', '#FFD3E8'], props: ['card_star_witch_hat', 'card_flying_broom', 'card_broom_kitten', 'card_cloud_kitten'],
    copy: { pt: ['MAGIA COM A LUNA', 'a gatinha bruxa te espera'], en: ['MAGIC WITH LUNA', 'the little witch cat awaits'], es: ['MAGIA CON LUNA', 'la gatita bruja te espera'] } },
  { scene: '02_hard', palette: ['#35C9D6', '#0B5E8F', '#B8F4FF'], props: ['card_crystal_ball', 'card_geode', 'card_amethyst_cluster', 'card_gem_crown'],
    copy: { pt: ['FASES DESAFIADORAS', 'cores escondidas e garrafas de pedra'], en: ['TRICKY LEVELS', 'hidden colors and stone bottles'], es: ['NIVELES DESAFIANTES', 'colores ocultos y botellas de piedra'] } },
  { scene: '04_boosters', palette: ['#FFB43A', '#D9541A', '#FFE3A8'], props: ['booster_wand', 'booster_bottle', 'booster_rainbow', 'booster_crystal'],
    copy: { pt: ['6 REFORÇOS MÁGICOS', 'varinha, garrafa extra e mais'], en: ['6 MAGIC BOOSTERS', 'magic wand, extra bottle and more'], es: ['6 POTENCIADORES MÁGICOS', 'varita, botella extra y más'] } },
  { scene: '05_win', palette: ['#5FD86A', '#1B7F50', '#D2FFD0'], props: ['icon_star', 'card_lucky_clover', 'icon_coin', 'card_honey_drops'],
    copy: { pt: ['FECHE COM ROLHA', 'e ganhe até 3 estrelas'], en: ['CORK THEM ALL', 'and earn up to 3 stars'], es: ['¡PONLES CORCHO!', 'y gana hasta 3 estrellas'] } },
  { scene: '06_collection', palette: ['#4FA8FF', '#2347C4', '#CFE6FF'], props: ['card_baby_dragon', 'card_sugar_unicorn', 'card_seahorse', 'card_wise_owl'],
    copy: { pt: ['COLECIONE 54 CARTAS', 'e complete os 6 álbuns'], en: ['COLLECT 54 CARDS', 'and complete all 6 albums'], es: ['COLECCIONA 54 CARTAS', 'y completa los 6 álbumes'] } },
  { scene: '07_worlds', palette: ['#B58BFF', '#5A2FC2', '#E8DCFF'], props: ['card_glow_mushroom', 'card_cupcake_castle', 'card_hot_air_balloon', 'card_happy_pumpkin'],
    copy: { pt: ['6 MUNDOS MÁGICOS', 'floresta, cristais, doces e mais'], en: ['6 MAGICAL WORLDS', 'forest, crystals, candy and more'], es: ['6 MUNDOS MÁGICOS', 'bosque, cristales, dulces y más'] } },
  { scene: '08_ranking', palette: ['#8B6CFF', '#33239E', '#DCD3FF'], props: ['icon_trophy', 'icon_crown', 'icon_medal_gold', 'card_treasure_chest'],
    copy: { pt: ['SUBA NO RANKING', 'torneio semanal e global'], en: ['CLIMB THE RANKING', 'weekly contest and global board'], es: ['SUBE EN EL RANKING', 'torneo semanal y global'] } },
];

const svg = (w, h, body) => Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${n(w)}" height="${n(h)}" viewBox="0 0 ${w} ${h}">${body}</svg>`);

// Text as outlined paths (no system font lookup), centered on cx, shrunk until it fits maxWidth.
function textPath(text, cx, baseline, size, maxWidth) {
  let s = size;
  while (font.getAdvanceWidth(text, s) > maxWidth && s > 8) s *= 0.96;
  const w = font.getAdvanceWidth(text, s);
  return { d: font.getPath(text, cx - w / 2, baseline, s).toPathData(2), size: s };
}

function caption(W, top, title, subtitle, palette) {
  const titleSize = W * 0.105, subSize = W * 0.064;
  const t = textPath(title, W / 2, top + titleSize, titleSize, W * 0.88);
  const s = textPath(subtitle, W / 2, top + t.size + subSize * 1.45, subSize, W * 0.84);
  const stroke = t.size * 0.16;
  return `
    <path d="${t.d}" transform="translate(0 ${stroke * 0.55})" fill="#000" fill-opacity="0.22" stroke="#000" stroke-opacity="0.22" stroke-width="${stroke}" stroke-linejoin="round"/>
    <path d="${t.d}" fill="#fff" stroke="${palette[1]}" stroke-width="${stroke}" stroke-linejoin="round" paint-order="stroke"/>
    <path d="${s.d}" fill="#fff" stroke="${palette[1]}" stroke-width="${s.size * 0.12}" stroke-linejoin="round" paint-order="stroke"/>`;
}

async function compose(card, lang, kind, file) {
  const { W, H, capture } = kinds[kind];
  const source = join(shots, `store_${lang}_${capture}_${card.scene}.png`);
  if (!existsSync(source)) throw new Error(`Missing capture ${source} (run the QaTour store script in the editor).`);
  const [p0, p1, glow] = card.palette;
  const captionH = H * (capture === 'phone' ? 0.18 : 0.21);
  const meta = await sharp(source).metadata();
  // Capture box: as big as the space under the caption allows, keeping the capture's own ratio.
  const maxW = W * (capture === 'phone' ? 0.8 : 0.88), maxH = H - captionH - H * 0.035;
  const scale = Math.min(maxW / meta.width, maxH / meta.height);
  const cw = n(meta.width * scale), ch = n(meta.height * scale);
  const cx = n((W - cw) / 2), cy = n(captionH + (maxH - ch) / 2 + H * 0.012);
  const radius = n(cw * (capture === 'phone' ? 0.07 : 0.04)), border = n(W * 0.012);

  const background = svg(W, H, `
    <defs>
      <linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${p0}"/><stop offset="1" stop-color="${p1}"/></linearGradient>
      <radialGradient id="r" cx="0.5" cy="0.12" r="0.7"><stop offset="0" stop-color="${glow}" stop-opacity="0.75"/><stop offset="1" stop-color="${glow}" stop-opacity="0"/></radialGradient>
    </defs>
    <rect width="${W}" height="${H}" fill="url(#g)"/>
    <rect width="${W}" height="${H}" fill="url(#r)"/>
    ${Array.from({ length: 14 }, (_, i) => `<circle cx="${n(((i * 397) % 1000) / 1000 * W)}" cy="${n(((i * 613) % 1000) / 1000 * H)}" r="${n(W * (0.01 + (i % 4) * 0.006))}" fill="#fff" fill-opacity="${0.08 + (i % 3) * 0.05}"/>`).join('')}`);

  // Product art: two above the capture's top corners (never over the game's HUD), two behind its sides.
  const propSize = n(W * (capture === 'phone' ? 0.17 : 0.13));
  const spots = [[cx - propSize * 0.55, cy - propSize * 0.98, -14], [cx + cw - propSize * 0.45, cy - propSize * 0.98, 12],
                 [-propSize * 0.25, cy + ch * 0.36, 10], [W - propSize * 0.75, cy + ch * 0.5, -10]];
  const props = await Promise.all(card.props.map(async (name, i) => {
    const [x, y, angle] = spots[i];
    const img = await sharp(join(artDir, `${name}.png`)).resize(propSize, propSize, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .rotate(angle, { background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toBuffer();
    return { input: img, left: Math.max(0, n(x)), top: n(y) };
  }));

  const shadow = await sharp(svg(W, H, `<rect x="${cx - border}" y="${cy - border + n(H * 0.012)}" width="${cw + border * 2}" height="${ch + border * 2}" rx="${radius + border}" fill="#1a0b3d" fill-opacity="0.45"/>`))
    .blur(Math.max(1, W * 0.015)).png().toBuffer();
  const frame = svg(W, H, `<rect x="${cx - border}" y="${cy - border}" width="${cw + border * 2}" height="${ch + border * 2}" rx="${radius + border}" fill="#fff"/>`);
  const shot = await sharp(source).resize(cw, ch).composite([{ input: svg(cw, ch, `<rect width="${cw}" height="${ch}" rx="${radius}" fill="#fff"/>`), blend: 'dest-in' }]).png().toBuffer();
  const [title, subtitle] = card.copy[lang];

  mkdirSync(dirname(file), { recursive: true });
  await sharp(background)
    .composite([...props.slice(2), { input: shadow }, { input: frame }, { input: shot, left: cx, top: cy }, ...props.slice(0, 2),
      { input: svg(W, H, caption(W, H * 0.035, title, subtitle, card.palette)) }])
    .flatten({ background: p1 }).png({ compressionLevel: 9 }).toFile(file);
}

async function playImages() {
  const out = join(loja, 'imagens');
  mkdirSync(out, { recursive: true });
  await sharp(join(root, 'Assets/_Game/Art/AppIcon/app_icon.png')).resize(512, 512).flatten({ background: '#ffffff' }).png().toFile(join(out, 'icone_512.png'));
  // Feature graphic 1024x500: logo + Luna + potions, no small text (Play shows it cropped on some surfaces).
  const W = 1024, H = 500;
  const bg = svg(W, H, `
    <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#7B3BFF"/><stop offset="1" stop-color="#FF6FC8"/></linearGradient>
    <radialGradient id="r" cx="0.38" cy="0.45" r="0.6"><stop offset="0" stop-color="#fff" stop-opacity="0.45"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient></defs>
    <rect width="${W}" height="${H}" fill="url(#g)"/><rect width="${W}" height="${H}" fill="url(#r)"/>`);
  const logo = await sharp(join(artDir, 'logo.png')).resize({ width: 560, height: 300, fit: 'inside' }).png().toBuffer();
  const logoMeta = await sharp(logo).metadata();
  const mascot = await sharp(join(artDir, 'mascot_wave.png')).resize({ height: 440, fit: 'inside' }).png().toBuffer();
  const mascotMeta = await sharp(mascot).metadata();
  const items = [['card_rainbow_potion', 40, 330, 120, -12], ['card_starlight_potion', 70, 30, 110, 10], ['card_moon_potion', 560, 360, 110, 8], ['card_crystal_ball', 600, 20, 95, -8], ['card_mermaid_potion', 920, 330, 120, 10]];
  const extras = await Promise.all(items.map(async ([name, x, y, size, angle]) => ({
    input: await sharp(join(artDir, `${name}.png`)).resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .rotate(angle, { background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toBuffer(), left: x, top: y })));
  await sharp(bg).composite([...extras,
    { input: logo, left: n(380 - logoMeta.width / 2), top: n(250 - logoMeta.height / 2) },
    { input: mascot, left: n(W - mascotMeta.width - 40), top: H - mascotMeta.height }])
    .flatten({ background: '#7B3BFF' }).png().toFile(join(out, 'grafico_destaque_1024x500.png'));
}

const preview = process.argv.includes('--preview');
for (const [locale, lang] of Object.entries(locales)) {
  if (preview && lang !== 'pt') continue;
  for (const kind of Object.keys(kinds)) {
    if (preview && kind !== 'iphone') continue;
    for (const [i, card] of cards.entries()) {
      const name = `${String(i + 1).padStart(2, '0')}-${card.scene.slice(3)}.png`;
      await compose(card, lang, kind, join(loja, 'capturas', locale, kind, name));
    }
    console.log(`${locale}/${kind}: ${cards.length} screenshots`);
  }
}
if (!preview) { await playImages(); console.log('imagens: icone_512.png, grafico_destaque_1024x500.png'); }
