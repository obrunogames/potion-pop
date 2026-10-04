// Store drafts only. No command in this file submits a release for review or publishes it.
// Adapted from car-racing/Tools/release/stores.mjs (same commands, same APIs); everything specific to the app lives
// in Docs/loja/ficha.json. `check` runs offline and is also run before every command that writes to a store.
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join, basename } from 'node:path';
import { homedir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { createPrivateKey, createSign, sign, createHash } from 'node:crypto';

const root = join(dirname(fileURLToPath(import.meta.url)), '../..');
// Git tracks the folder as "Docs"; on macOS (case-insensitive) "docs/loja" is the same folder.
const loja = join(root, 'Docs/loja');
const credentials = process.env.POTIONPOP_STORE_CREDENTIALS_DIR || join(homedir(), 'Desktop/games/CarrosRebaixados/Keystore');
const catalog = JSON.parse(readFileSync(join(loja, 'ficha.json'), 'utf8'));
const relation = (type, id) => ({ data: { type, id } });
// ficha.json is keyed by Google Play locale; App Store Connect uses es-MX for Latin American Spanish.
const appleLocales = { 'pt-BR': 'pt-BR', 'en-US': 'en-US', 'es-419': 'es-MX' };
const APPLE_ID_PLACEHOLDER = 'APPLE_APP_ID';
let appleToken, playToken, appleIdCache;

async function request(url, method, token, body, media = false, contentType = 'application/octet-stream') {
  const headers = { Authorization: `Bearer ${token}`, 'Content-Type': media ? contentType : 'application/json' };
  const result = await fetch(url, { method, headers, body: body === undefined ? undefined : media ? body : JSON.stringify(body) });
  const text = await result.text();
  const data = text ? JSON.parse(text) : null;
  if (!result.ok) throw new Error(`${method} ${new URL(url).pathname}: ${result.status} ${data?.error?.message || data?.errors?.map(x => x.detail).join('; ') || 'request failed'}`);
  return data;
}

function appleAuth() {
  if (appleToken) return appleToken;
  const cfg = JSON.parse(readFileSync(join(credentials, 'appstoreconnect.json'), 'utf8'));
  const key = createPrivateKey(readFileSync(join(credentials, cfg.keyFile)));
  const now = Math.floor(Date.now() / 1000), b64 = x => Buffer.from(JSON.stringify(x)).toString('base64url');
  const input = `${b64({ alg: 'ES256', kid: cfg.keyId, typ: 'JWT' })}.${b64({ iss: cfg.issuerId, iat: now, exp: now + 1100, aud: 'appstoreconnect-v1' })}`;
  return appleToken = `${input}.${sign('sha256', Buffer.from(input), { key, dsaEncoding: 'ieee-p1363' }).toString('base64url')}`;
}
const apple = (method, path, body) => request(`https://api.appstoreconnect.apple.com${path}`, method, appleAuth(), body);

async function playAuth() {
  if (playToken) return playToken;
  const cfg = JSON.parse(readFileSync(join(credentials, 'play-publisher.json'), 'utf8'));
  const now = Math.floor(Date.now() / 1000), b64 = x => Buffer.from(JSON.stringify(x)).toString('base64url');
  const input = `${b64({ alg: 'RS256', typ: 'JWT' })}.${b64({ iss: cfg.client_email, scope: 'https://www.googleapis.com/auth/androidpublisher', aud: cfg.token_uri, iat: now, exp: now + 3600 })}`;
  const signature = createSign('RSA-SHA256').update(input).sign(cfg.private_key).toString('base64url');
  const result = await fetch(cfg.token_uri, { method: 'POST', body: new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion: `${input}.${signature}` }) });
  const data = await result.json();
  if (!data.access_token) throw new Error(`Google authentication failed (${result.status})`);
  return playToken = data.access_token;
}
const play = async (method, path, body, media = false, contentType) => request(`https://androidpublisher.googleapis.com/${media ? 'upload/' : ''}androidpublisher/v3/applications/${catalog.bundleId}${path}`, method, await playAuth(), body, media, contentType);

// A new app must exist in the Play Console and the service account needs access to it (Users and permissions).
async function playEdit() {
  try { return await play('POST', '/edits', {}); }
  catch (error) {
    if (/: (403|404) /.test(error.message)) throw new Error(`${error.message}\nCreate ${catalog.bundleId} in the Play Console and invite the service account of play-publisher.json in Users and permissions (app access: releases, store listing, app content).`);
    throw error;
  }
}

// ---------------------------------------------------------------------------------------------- offline checks

const LIMITS = { name: 30, subtitle: 30, promotionalText: 170, keywords: 100, description: 4000, shortDescription: 80, playDescription: 4000, releaseNotes: 500, reviewNotes: 4000 };
const EMOJI = /\p{Extended_Pictographic}/u;
// Other games' names (store policies and trademark risk). Generic words like "sort" or "match" are fine.
const COMPETITORS = ['magic sort', 'water sort puzzle', 'ball sort', 'soda sort', 'get color', 'sortpuz', 'color sort 3d', 'liquid sort puzzle', 'candy crush', 'royal match'];
// The stricter of code points and UTF-16 units (emoji count as 2 in some counters).
const size = text => Math.max([...text].length, text.length);

function validate({ quiet = false } = {}) {
  const errors = [], warnings = [], rows = [];
  const limit = (where, field, text, max) => {
    if (typeof text !== 'string' || !text.trim()) { errors.push(`${where} ${field}: empty`); return; }
    const n = size(text);
    rows.push([where, field, n, max]);
    if (n > max) errors.push(`${where} ${field}: ${n} > ${max}`);
  };
  for (const [locale, t] of Object.entries(catalog.locales)) {
    for (const field of ['name', 'subtitle', 'promotionalText', 'description', 'shortDescription', 'playDescription', 'releaseNotes']) limit(locale, field, t[field], LIMITS[field]);
    limit(locale, 'keywords', t.keywords, LIMITS.keywords);
    const bytes = Buffer.byteLength(t.keywords || '', 'utf8');
    if (bytes > LIMITS.keywords) errors.push(`${locale} keywords: ${bytes} UTF-8 bytes > 100 (App Store Connect may count bytes)`);
    const words = (t.keywords || '').split(',');
    if (words.some(w => w !== w.trim() || !w)) errors.push(`${locale} keywords: no spaces around commas and no empty items`);
    if (new Set(words).size !== words.length) errors.push(`${locale} keywords: duplicated keyword`);
    const indexed = `${t.name} ${t.subtitle}`.toLowerCase();
    for (const w of words) if (indexed.split(/[^\p{L}\p{N}]+/u).includes(w.toLowerCase())) warnings.push(`${locale} keywords: "${w}" is already in the name/subtitle (wasted characters)`);
    for (const field of ['name', 'subtitle', 'keywords', 'shortDescription'])
      if (EMOJI.test(t[field] || '')) errors.push(`${locale} ${field}: no emoji allowed here`);
    for (const field of ['name', 'subtitle', 'keywords', 'promotionalText', 'description', 'shortDescription', 'playDescription'])
      for (const c of COMPETITORS) if ((t[field] || '').toLowerCase().includes(c)) errors.push(`${locale} ${field}: mentions "${c}"`);
    for (const field of ['privacyUrl', 'supportUrl']) if (!/^https:\/\//.test(t[field] || '')) errors.push(`${locale} ${field}: must be an https URL`);
    if (!(t.name || '').includes('Potion Pop!')) errors.push(`${locale} name: must contain "Potion Pop!"`);
    if (!appleLocales[locale]) errors.push(`${locale}: no App Store locale mapped`);
  }
  limit('app', 'reviewNotes', catalog.reviewNotes, LIMITS.reviewNotes);
  for (const field of ['marketingUrl', 'accountDeletionUrl']) if (!/^https:\/\//.test(catalog[field] || '')) errors.push(`${field}: must be an https URL`);
  if (!catalog.apple?.ageRating || !catalog.apple?.primaryCategory) errors.push('apple: ageRating and categories are required');
  if (!quiet) {
    for (const [where, field, n, max] of rows) console.log(`${n <= max ? 'ok ' : 'BAD'} ${where.padEnd(6)} ${field.padEnd(16)} ${String(n).padStart(4)} / ${max}`);
    for (const w of warnings) console.log(`warning: ${w}`);
  }
  if (errors.length) throw new Error(`Docs/loja/ficha.json is not valid:\n- ${errors.join('\n- ')}`);
  if (!quiet) console.log(`Docs/loja/ficha.json: ${rows.length} texts within the store limits.`);
}

// ---------------------------------------------------------------------------------------------- App Store Connect

// The Apple ID is filled in ficha.json once the app exists; until then it is looked up (read-only) by bundle id.
async function appleAppId() {
  if (appleIdCache) return appleIdCache;
  const configured = process.env.POTIONPOP_APPLE_APP_ID || (catalog.appleId !== APPLE_ID_PLACEHOLDER ? catalog.appleId : '');
  if (configured) return appleIdCache = configured;
  const app = (await apple('GET', `/v1/apps?filter[bundleId]=${encodeURIComponent(catalog.bundleId)}`)).data.find(x => x.attributes.bundleId === catalog.bundleId);
  if (!app) throw new Error(`No App Store Connect app with bundle ${catalog.bundleId}. Create it in App Store Connect (Apps > + > New App, SKU ${catalog.sku}) first.`);
  console.log(`Apple: app ${app.id} found for ${catalog.bundleId}. Write it to Docs/loja/ficha.json ("appleId").`);
  return appleIdCache = app.id;
}

// Registers the bundle id in Certificates, Identifiers & Profiles (App Store Connect can only create the app record for
// an existing id) and enables Sign in with Apple on it. Safe to re-run.
async function appleBundle() {
  let bundle = (await apple('GET', `/v1/bundleIds?filter[identifier]=${encodeURIComponent(catalog.bundleId)}&include=bundleIdCapabilities`)).data
    .find(x => x.attributes.identifier === catalog.bundleId);
  if (!bundle) {
    bundle = (await apple('POST', '/v1/bundleIds', { data: { type: 'bundleIds', attributes: { identifier: catalog.bundleId, name: 'Potion Pop', platform: 'IOS' } } })).data;
    console.log(`Apple: bundle id ${catalog.bundleId} registered (${bundle.id}).`);
  } else console.log(`Apple: bundle id ${catalog.bundleId} already registered (${bundle.id}).`);
  const capabilities = (await apple('GET', `/v1/bundleIds/${bundle.id}/bundleIdCapabilities`)).data.map(x => x.attributes.capabilityType);
  if (capabilities.includes('APPLE_ID_AUTH')) return console.log('Apple: Sign in with Apple already enabled.');
  await apple('POST', '/v1/bundleIdCapabilities', { data: { type: 'bundleIdCapabilities',
    attributes: { capabilityType: 'APPLE_ID_AUTH', settings: [{ key: 'APPLE_ID_AUTH_APP_CONSENT', options: [{ key: 'PRIMARY_APP_CONSENT' }] }] },
    relationships: { bundleId: relation('bundleIds', bundle.id) } } });
  console.log('Apple: Sign in with Apple enabled.');
}

async function appleContext() {
  const app = (await apple('GET', `/v1/apps/${await appleAppId()}`)).data;
  if (app.attributes.bundleId !== catalog.bundleId) throw new Error('The Apple ID belongs to a different bundle; stopping.');
  const info = (await apple('GET', `/v1/apps/${app.id}/appInfos`)).data.find(x => x.attributes.state === 'PREPARE_FOR_SUBMISSION');
  const version = (await apple('GET', `/v1/apps/${app.id}/appStoreVersions?filter[platform]=IOS`)).data.find(x => x.attributes.appStoreState === 'PREPARE_FOR_SUBMISSION');
  if (!info || !version) throw new Error('No editable Apple draft exists. This script does not modify submitted versions.');
  return { app, info, version };
}

async function appleDraft() {
  validate({ quiet: true });
  const { info, version } = await appleContext();
  const a = catalog.apple;
  await apple('PATCH', `/v1/appInfos/${info.id}`, { data: { type: 'appInfos', id: info.id, relationships: {
    primaryCategory: relation('appCategories', a.primaryCategory), primarySubcategoryOne: relation('appCategories', a.primarySubcategoryOne),
    primarySubcategoryTwo: relation('appCategories', a.primarySubcategoryTwo),
  } } });
  // Age rating answers (and the reasons for each one) are in ficha.json and Docs/loja/lojas.md.
  await apple('PATCH', `/v1/ageRatingDeclarations/${info.id}`, { data: { type: 'ageRatingDeclarations', id: info.id, attributes: a.ageRating } });
  await apple('PATCH', `/v1/appStoreVersions/${version.id}`, { data: { type: 'appStoreVersions', id: version.id,
    attributes: { versionString: catalog.version, copyright: catalog.copyright, releaseType: a.releaseType } } });
  const infoLocs = (await apple('GET', `/v1/appInfos/${info.id}/appInfoLocalizations`)).data;
  const versionLocs = (await apple('GET', `/v1/appStoreVersions/${version.id}/appStoreVersionLocalizations`)).data;
  for (const [local, t] of Object.entries(catalog.locales)) {
    const locale = appleLocales[local];
    // "What's New" is not editable on an app's first version, so releaseNotes only go to Google Play here.
    for (const [items, type, relatedType, relatedId, relatedName, attributes] of [
      [infoLocs, 'appInfoLocalizations', 'appInfos', info.id, 'appInfo', { name: t.name, subtitle: t.subtitle, privacyPolicyUrl: t.privacyUrl }],
      [versionLocs, 'appStoreVersionLocalizations', 'appStoreVersions', version.id, 'appStoreVersion', {
        description: t.description, keywords: t.keywords, promotionalText: t.promotionalText, supportUrl: t.supportUrl,
        marketingUrl: catalog.marketingUrl,
      }],
    ]) {
      let current = items.find(x => x.attributes.locale === locale);
      // Apple can create the version localization automatically when its app-info localization is added.
      if (!current && type === 'appStoreVersionLocalizations')
        current = (await apple('GET', `/v1/appStoreVersions/${version.id}/appStoreVersionLocalizations`)).data.find(x => x.attributes.locale === locale);
      if (current) await apple('PATCH', `/v1/${type}/${current.id}`, { data: { type, id: current.id, attributes } });
      else await apple('POST', `/v1/${type}`, { data: { type, attributes: { locale, ...attributes }, relationships: { [relatedName]: relation(relatedType, relatedId) } } });
    }
    console.log(`Apple: ${locale} listing saved in draft.`);
  }
  const after = (await apple('GET', `/v1/appInfos/${info.id}`)).data.attributes;
  console.log(`Apple age rating: ${after.appStoreAgeRating}; Brazil: ${after.brazilAgeRatingV2 || after.brazilAgeRating}.`);
}

const appleAll = async path => {
  let out = [], next = path;
  while (next) { const page = await apple('GET', next.replace('https://api.appstoreconnect.apple.com', '')); out = out.concat(page.data); next = page.links?.next ?? null; }
  return out;
};

// Price (free), every territory, content rights and the App Review contact (copied from the studio's
// other app when REVIEW_CONTACT_FROM_APP is set, so the phone number never lives in this repo).
async function appleExtra() {
  validate({ quiet: true });
  const { app, version } = await appleContext();
  const rights = catalog.apple.contentRights;
  if (app.attributes.contentRightsDeclaration !== rights) {
    await apple('PATCH', `/v1/apps/${app.id}`, { data: { type: 'apps', id: app.id, attributes: { contentRightsDeclaration: rights } } });
    console.log(`Apple: content rights = ${rights}.`);
  }
  let schedule = null;
  try { schedule = await apple('GET', `/v1/apps/${app.id}/appPriceSchedule?include=manualPrices`); } catch (e) { if (!/404/.test(e.message)) throw e; }
  if (!schedule?.included?.length) {
    const free = (await appleAll(`/v1/apps/${app.id}/appPricePoints?filter[territory]=BRA&limit=200`)).find(p => Number(p.attributes.customerPrice) === 0);
    await apple('POST', '/v1/appPriceSchedules', {
      data: { type: 'appPriceSchedules', relationships: { app: relation('apps', app.id), baseTerritory: relation('territories', 'BRA'), manualPrices: { data: [{ type: 'appPrices', id: '${p0}' }] } } },
      included: [{ type: 'appPrices', id: '${p0}', attributes: { startDate: null }, relationships: { appPricePoint: relation('appPricePoints', free.id) } }],
    });
    console.log('Apple: price = free.');
  }
  let availability = null;
  try { availability = await apple('GET', `/v1/apps/${app.id}/appAvailabilityV2`); } catch (e) { if (!/404/.test(e.message)) throw e; }
  if (!availability?.data) {
    const territories = (await appleAll('/v1/territories?limit=200')).map(t => t.id);
    await apple('POST', '/v2/appAvailabilities', {
      data: { type: 'appAvailabilities', attributes: { availableInNewTerritories: true },
        relationships: { app: relation('apps', app.id), territoryAvailabilities: { data: territories.map((t, i) => ({ type: 'territoryAvailabilities', id: `\${t${i}}` })) } } },
      included: territories.map((t, i) => ({ type: 'territoryAvailabilities', id: `\${t${i}}`, attributes: { available: true }, relationships: { territory: relation('territories', t) } })),
    });
    console.log(`Apple: available in ${territories.length} territories (and new ones).`);
  }
  const notes = catalog.reviewNotes;
  let contact = { contactFirstName: catalog.reviewContact?.firstName, contactLastName: catalog.reviewContact?.lastName, contactEmail: catalog.reviewContact?.email };
  const source = process.env.REVIEW_CONTACT_FROM_APP;
  if (source) {
    const versions = (await apple('GET', `/v1/apps/${source}/appStoreVersions?limit=5`)).data;
    for (const v of versions) {
      const detail = (await apple('GET', `/v1/appStoreVersions/${v.id}/appStoreReviewDetail`).catch(() => null))?.data?.attributes;
      if (detail?.contactPhone) { contact = { contactFirstName: detail.contactFirstName, contactLastName: detail.contactLastName, contactEmail: detail.contactEmail, contactPhone: detail.contactPhone }; break; }
    }
  }
  // Sign-in is optional (Sign in with Apple / Google with the reviewer's own account): no demo account.
  const attributes = { ...contact, demoAccountRequired: false, notes };
  let detail = null;
  try { detail = (await apple('GET', `/v1/appStoreVersions/${version.id}/appStoreReviewDetail`)).data; } catch (e) { if (!/404/.test(e.message)) throw e; }
  if (detail) await apple('PATCH', `/v1/appStoreReviewDetails/${detail.id}`, { data: { type: 'appStoreReviewDetails', id: detail.id, attributes } });
  else await apple('POST', '/v1/appStoreReviewDetails', { data: { type: 'appStoreReviewDetails', attributes, relationships: { appStoreVersion: relation('appStoreVersions', version.id) } } });
  console.log(`Apple: App Review contact ${attributes.contactFirstName} ${attributes.contactLastName} (${attributes.contactPhone ? 'with phone' : 'NO PHONE'}) and notes saved.`);
}

// Attaches the newest processed build of this version (pass a build number to pick another one).
async function appleBuild(number) {
  const { app, version } = await appleContext();
  const builds = (await apple('GET', `/v1/builds?filter[app]=${app.id}&filter[preReleaseVersion.version]=${encodeURIComponent(catalog.version)}&filter[processingState]=VALID&sort=-uploadedDate&limit=20`)).data;
  const build = number ? builds.find(b => b.attributes.version === String(number)) : builds[0];
  if (!build) throw new Error(`No processed build ${number ?? ''} for ${catalog.version} yet.`);
  await apple('PATCH', `/v1/appStoreVersions/${version.id}/relationships/build`, { data: { type: 'builds', id: build.id } });
  console.log(`Apple: build ${catalog.version} (${build.attributes.version}) attached to the version (encryption exempt: ${build.attributes.usesNonExemptEncryption === false}).`);
}

// Docs/loja/capturas/<pt-BR|en-US|es-419>/<folder>/*.png, sorted by name; the App Store takes up to 10 per set.
function captures(locale, folder, max) {
  const path = join(loja, 'capturas', locale, folder);
  const files = existsSync(path) ? readdirSync(path).filter(x => x.endsWith('.png')).sort() : [];
  if (files.length > max) console.log(`warning: ${locale}/${folder} has ${files.length} captures; only the first ${max} are used.`);
  return { path, files: files.slice(0, max) };
}

async function appleScreenshots() {
  const { version } = await appleContext();
  const locales = (await apple('GET', `/v1/appStoreVersions/${version.id}/appStoreVersionLocalizations`)).data;
  for (const [local, appleLocale] of Object.entries(appleLocales)) {
    const localization = locales.find(x => x.attributes.locale === appleLocale);
    if (!localization) throw new Error(`Missing Apple localization ${appleLocale}; run apple-draft first.`);
    // iPhone 6.9" (1320x2868 portrait) and iPad 13" (2064x2752 portrait)
    for (const [folder, display] of [['iphone', 'APP_IPHONE_67'], ['ipad', 'APP_IPAD_PRO_3GEN_129']]) {
      const { path, files } = captures(local, folder, 10);
      if (!files.length) throw new Error(`No captures in ${path}.`);
      let sets = (await apple('GET', `/v1/appStoreVersionLocalizations/${localization.id}/appScreenshotSets`)).data;
      let set = sets.find(x => x.attributes.screenshotDisplayType === display);
      if (!set) set = (await apple('POST', '/v1/appScreenshotSets', { data: { type: 'appScreenshotSets',
        attributes: { screenshotDisplayType: display }, relationships: { appStoreVersionLocalization: relation('appStoreVersionLocalizations', localization.id) } } })).data;
      const current = (await apple('GET', `/v1/appScreenshotSets/${set.id}/appScreenshots`)).data;
      for (const file of files) {
        // a reservation interrupted mid-upload stays AWAITING_UPLOAD forever and blocks the review: replace it
        const stuck = current.filter(x => x.attributes.fileName === file && ['AWAITING_UPLOAD', 'FAILED'].includes(x.attributes.assetDeliveryState?.state));
        for (const x of stuck) await apple('DELETE', `/v1/appScreenshots/${x.id}`);
        if (current.some(x => x.attributes.fileName === file && !stuck.includes(x))) continue;
        const buffer = readFileSync(join(path, file));
        const asset = (await apple('POST', '/v1/appScreenshots', { data: { type: 'appScreenshots', attributes: { fileName: file, fileSize: buffer.length }, relationships: { appScreenshotSet: relation('appScreenshotSets', set.id) } } })).data;
        for (const op of asset.attributes.uploadOperations) {
          const response = await fetch(op.url, { method: op.method, headers: Object.fromEntries(op.requestHeaders.map(x => [x.name, x.value])), body: buffer.subarray(op.offset, op.offset + op.length) });
          if (!response.ok) throw new Error(`Screenshot upload failed: ${response.status}`);
        }
        await apple('PATCH', `/v1/appScreenshots/${asset.id}`, { data: { type: 'appScreenshots', id: asset.id, attributes: { uploaded: true, sourceFileChecksum: createHash('md5').update(buffer).digest('hex') } } });
        console.log(`Apple: ${appleLocale}/${folder}/${file} uploaded to draft.`);
      }
    }
  }
}

// ---------------------------------------------------------------------------------------------- Google Play

// Apps never published on Google Play reject changesNotSentForReview: their edits only save drafts
// (the release stays a draft until someone rolls it out in the Play Console).
async function commitPlayEdit(id) {
  try { await play('POST', `/edits/${id}:commit?changesNotSentForReview=true`); return 'changes NOT sent for review'; }
  catch (error) {
    if (!/changesNotSentForReview must not be set/.test(error.message)) throw error;
    await play('POST', `/edits/${id}:commit`);
    return 'app never published: saved as draft, nothing goes live until it is rolled out in the Play Console';
  }
}

async function playDraft(bundle) {
  validate({ quiet: true });
  if (bundle && !existsSync(bundle)) throw new Error(`Missing AAB: ${bundle}`);
  const { id } = await playEdit();
  try {
    for (const [locale, t] of Object.entries(catalog.locales)) {
      await play('PUT', `/edits/${id}/listings/${locale}`, { language: locale, title: t.name, shortDescription: t.shortDescription, fullDescription: t.playDescription || t.description });
      console.log(`Google Play: ${locale} listing saved in draft edit.`);
    }
    // The API has no field for the app category: set Game > Puzzle (catalog.play.category) in the Play Console.
    const p = catalog.play;
    await play('PUT', `/edits/${id}/details`, { defaultLanguage: p.defaultLanguage, contactEmail: p.contactEmail, contactWebsite: p.contactWebsite, ...(p.contactPhone ? { contactPhone: p.contactPhone } : {}) });
    if (bundle) {
      const { versionCode } = await play('POST', `/edits/${id}/bundles?uploadType=media`, readFileSync(bundle), true);
      await play('PUT', `/edits/${id}/tracks/production`, { track: 'production', releases: [{ name: `${catalog.version} (${versionCode})`, status: 'draft', versionCodes: [String(versionCode)], releaseNotes: Object.entries(catalog.locales).map(([language, t]) => ({ language, text: t.releaseNotes })) }] });
      console.log(`Google Play: ${basename(bundle)}, version ${versionCode}, production DRAFT.`);
    }
    console.log(`Google Play: edit committed; ${await commitPlayEdit(id)}.`);
  } catch (error) {
    await play('DELETE', `/edits/${id}`).catch(() => {});
    throw error;
  }
}

// AAB only, as a production DRAFT, with release notes in the default language: needs only the release permissions
// (no listing or contact details, which need "Manage store presence"). Nothing is sent for review.
async function playBundle(bundle) {
  validate({ quiet: true });
  if (!bundle || !existsSync(bundle)) throw new Error(`Missing AAB: ${bundle}`);
  const { id } = await playEdit();
  try {
    const { versionCode } = await play('POST', `/edits/${id}/bundles?uploadType=media`, readFileSync(bundle), true);
    const language = catalog.play.defaultLanguage;
    await play('PUT', `/edits/${id}/tracks/production`, { track: 'production', releases: [{ name: `${catalog.version} (${versionCode})`, status: 'draft', versionCodes: [String(versionCode)], releaseNotes: [{ language, text: catalog.locales[language].releaseNotes }] }] });
    console.log(`Google Play: ${basename(bundle)}, version ${versionCode}, production DRAFT.`);
    console.log(`Google Play: edit committed; ${await commitPlayEdit(id)}.`);
  } catch (error) {
    await play('DELETE', `/edits/${id}`).catch(() => {});
    throw error;
  }
}

async function playImages() {
  const icon = join(loja, 'imagens/icone_512.png'), feature = join(loja, 'imagens/grafico_destaque_1024x500.png');
  for (const file of [icon, feature]) if (!existsSync(file)) throw new Error(`Missing ${file}.`);
  const { id } = await playEdit();
  try {
    for (const locale of Object.keys(catalog.locales)) {
      // Phone: 9:16 portrait (1080x1920). A 1080x2340 capture is refused (longest side > 2x the shortest).
      // Tablets: android-tablet/ when present, otherwise the phone captures (9:16, short side >= 1080 px also
      // meets the 7" and 10" rules, as in Car Racing).
      const phone = captures(locale, 'android', 8);
      if (!phone.files.length) throw new Error(`No captures in ${phone.path}.`);
      const tablet = captures(locale, 'android-tablet', 8);
      const tabletFiles = (tablet.files.length ? tablet : phone).files.map(file => join((tablet.files.length ? tablet : phone).path, file));
      for (const [type, files] of [
        ['icon', [icon]],
        ['featureGraphic', [feature]],
        ['phoneScreenshots', phone.files.map(file => join(phone.path, file))],
        ['sevenInchScreenshots', tabletFiles],
        ['tenInchScreenshots', tabletFiles],
      ]) {
        const current = (await play('GET', `/edits/${id}/listings/${locale}/${type}`)).images || [];
        for (const file of files) {
          const buffer = readFileSync(file);
          if (current.some(image => image.sha1 === createHash('sha1').update(buffer).digest('hex') || image.sha256 === createHash('sha256').update(buffer).digest('hex'))) continue;
          await play('POST', `/edits/${id}/listings/${locale}/${type}?uploadType=media`, buffer, true, 'image/png');
          console.log(`Google Play: ${locale}/${type}/${basename(file)} uploaded to draft edit.`);
        }
      }
    }
    console.log(`Google Play: image edit committed; ${await commitPlayEdit(id)}.`);
  } catch (error) { await play('DELETE', `/edits/${id}`).catch(() => {}); throw error; }
}

// Data safety form, from Docs/loja/seguranca-dos-dados.csv (generated by Tools/release/data_safety.py).
async function playDataSafety() {
  const csv = readFileSync(join(loja, 'seguranca-dos-dados.csv'), 'utf8');
  await play('POST', '/dataSafety', { safetyLabels: csv });
  console.log('Google Play: Data safety form saved from Docs/loja/seguranca-dos-dados.csv.');
}

async function status() {
  const { app, info, version } = await appleContext();
  const builds = (await apple('GET', `/v1/apps/${app.id}/builds?limit=5`)).data;
  console.log(JSON.stringify({ apple: { id: app.id, name: app.attributes.name, bundleId: app.attributes.bundleId,
    version: version.attributes.versionString, state: version.attributes.appStoreState, releaseType: version.attributes.releaseType,
    rating: info.attributes.appStoreAgeRating, builds: builds.map(x => ({ version: x.attributes.version, state: x.attributes.processingState })) } }, null, 2));
  const edit = await playEdit();
  try {
    for (const path of ['listings', 'tracks']) console.log(JSON.stringify({ play: path, data: await play('GET', `/edits/${edit.id}/${path}`) }, null, 2));
  } finally { await play('DELETE', `/edits/${edit.id}`); }
}

const actions = { 'apple-bundle': appleBundle, 'apple-draft': appleDraft, 'apple-extra': appleExtra, 'apple-build': () => appleBuild(process.argv[3]), 'apple-screenshots': appleScreenshots, 'play-draft': () => playDraft(process.argv[3]), 'play-bundle': () => playBundle(process.argv[3]), 'play-images': playImages, 'play-data-safety': playDataSafety, status, check: () => validate() };
const command = actions[process.argv[2]];
if (!command) throw new Error('Use: node Tools/release/stores.mjs check|apple-bundle|apple-draft|apple-extra|apple-build [n]|apple-screenshots|play-draft [file.aab]|play-bundle <file.aab>|play-images|play-data-safety|status');
await command();
