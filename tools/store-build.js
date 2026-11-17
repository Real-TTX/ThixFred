// Builds ThixFred's store from the folder store/ of the repository:
//
//   node tools/store-build.js <output folder>          e.g. node tools/store-build.js artifacts/store
//
//   store/plugins/<id>/plugin.json (+ scripts)  ->  <id>.zip   (every file of the folder, deterministic: fixed times, sorted)
//   store/themes/<id>.json                      ->  <id>.json  (the theme as it is)
//   everything                                  ->  store.json (the catalog: id, kind, name, version, keyword, icon,
//                                                   platforms, download, sha256 …) – what ThixFred reads (StoreService)
//
// No dependencies (zip written with zlib). CI publishes the output in the release "store-channel" (.github/workflows/store.yml).
'use strict';

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');

const root = path.resolve(__dirname, '..');
const store = path.join(root, 'store');
const output = path.resolve(process.argv[2] || path.join(root, 'artifacts', 'store'));

const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit++) value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  return value >>> 0;
});

function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

/** A zip of the files (name -> Buffer), sorted by name, all dated 1980-01-01 00:00: the same input gives the same bytes. */
function zip(files) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const name of Object.keys(files).sort()) {
    const data = files[name];
    const compressed = zlib.deflateRawSync(data, { level: 9 });
    const nameBytes = Buffer.from(name, 'utf8');
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6); // UTF-8 names
    local.writeUInt16LE(8, 8); // deflate
    local.writeUInt16LE(0, 10);
    local.writeUInt16LE(0x21, 12); // 1980-01-01
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    local.writeUInt16LE(0, 28);
    locals.push(local, nameBytes, compressed);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(0x031e, 4); // made by Unix, 3.0: keeps the mode below
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(8, 10);
    central.writeUInt16LE(0, 12);
    central.writeUInt16LE(0x21, 14);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(compressed.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(((/\.(sh|zsh|py)$/.test(name) ? 0o100755 : 0o100644) << 16) >>> 0, 38);
    central.writeUInt32LE(offset, 42);
    centrals.push(central, nameBytes);
    offset += 30 + nameBytes.length + compressed.length;
  }

  const centralSize = centrals.reduce((sum, part) => sum + part.length, 0);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(centrals.length / 2, 8);
  end.writeUInt16LE(centrals.length / 2, 10);
  end.writeUInt32LE(centralSize, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, ...centrals, end]);
}

function filesOf(folder, prefix = '') {
  const files = {};
  for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
    const full = path.join(folder, entry.name);
    const name = prefix + entry.name;
    if (entry.isDirectory()) Object.assign(files, filesOf(full, name + '/'));
    else files[name] = /\.(sh|zsh|py|js|ps1|json)$/.test(entry.name)
      // LF (bash and python need it); .ps1 with a BOM (Windows PowerShell 5.1 reads UTF-8 only so)
      ? Buffer.from((entry.name.endsWith('.ps1') ? '﻿' : '') + fs.readFileSync(full, 'utf8').replace(/^﻿/, '').replace(/\r\n/g, '\n'), 'utf8')
      : fs.readFileSync(full);
  }
  return files;
}

const sha256 = (bytes) => crypto.createHash('sha256').update(bytes).digest('hex');
const lastChange = (file) => fs.statSync(file).mtime.toISOString();

fs.mkdirSync(output, { recursive: true });
const items = [];
const problems = [];

for (const id of fs.readdirSync(path.join(store, 'plugins')).sort()) {
  const folder = path.join(store, 'plugins', id);
  const manifest = JSON.parse(fs.readFileSync(path.join(folder, 'plugin.json'), 'utf8'));
  if (manifest.id !== id) problems.push(`plugins/${id}: plugin.json has the id "${manifest.id}"`);
  for (const key of ['script', 'scriptWindows', 'scriptMacOS']) {
    if (manifest[key] && !fs.existsSync(path.join(folder, manifest[key]))) problems.push(`plugins/${id}: ${key} ${manifest[key]} is missing`);
  }

  const platforms = [];
  if (manifest.script || manifest.scriptWindows) platforms.push('windows');
  if (manifest.script || manifest.scriptMacOS) platforms.push('macos');
  const bytes = zip(filesOf(folder));
  fs.writeFileSync(path.join(output, `${id}.zip`), bytes);
  items.push({
    id, kind: 'Plugin', name: manifest.name, version: manifest.version, author: manifest.author || '', description: manifest.description || '',
    keyword: manifest.keyword || '', icon: manifest.icon || 'plug', platforms, download: `${id}.zip`, sha256: sha256(bytes),
    homepage: manifest.homepage || '', updatedAt: lastChange(path.join(folder, 'plugin.json')),
  });
}

for (const file of fs.readdirSync(path.join(store, 'themes')).filter((name) => name.endsWith('.json')).sort()) {
  const id = path.basename(file, '.json');
  const text = fs.readFileSync(path.join(store, 'themes', file), 'utf8').replace(/\r\n/g, '\n');
  const theme = JSON.parse(text);
  const bytes = Buffer.from(text, 'utf8');
  fs.writeFileSync(path.join(output, `${id}.json`), bytes);
  items.push({
    id, kind: 'Theme', name: theme.name, version: theme.version || '1.0.0', author: theme.author || '', description: theme.description || '',
    keyword: '', icon: 'palette', platforms: [], download: `${id}.json`, sha256: sha256(bytes), homepage: '', updatedAt: lastChange(path.join(store, 'themes', file)),
  });
}

if (problems.length > 0) {
  console.error(problems.join('\n'));
  process.exit(1);
}

fs.writeFileSync(path.join(output, 'store.json'), JSON.stringify({ format: 1, generatedAt: new Date().toISOString(), items }, null, 2));
console.log(`${items.length} packages written to ${output}`);
