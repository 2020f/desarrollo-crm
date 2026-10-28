// Run after `npm run build`: compare actual lazy-module imports with the MVC
// shell's effective entry URL. Query strings create distinct ESM identities.
const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const web = path.resolve(__dirname, '../../src/Desarrollo.Web');

test('Built lazy modules and MVC shell resolve the shared entry to one canonical module URL', () => {
  const shell = fs.readFileSync(path.join(web, 'Views/Home/Index.cshtml'), 'utf8');
  const tag = shell.match(/<script\b[^>]*\btype="module"[^>]*>/i)?.[0];
  assert(tag, 'MVC shell has one module entry');
  const source = tag.match(/\bsrc="([^"]+)"/)?.[1];
  assert(source, 'module entry has a source URL');
  const origin = 'http://localhost:5007';
  const entry = new URL(source, origin);
  if (/\basp-append-version="true"/i.test(tag)) entry.searchParams.set('v', 'mvc-file-version');
  const assets = path.join(web, 'wwwroot/dist/assets');
  assert(fs.existsSync(assets), 'build the frontend before verifying generated module identity');
  let sharedImports = 0;
  for (const filename of fs.readdirSync(assets).filter((file) => file.endsWith('.js'))) {
    const content = fs.readFileSync(path.join(assets, filename), 'utf8');
    const parent = new URL('/dist/assets/' + filename, origin);
    for (const match of content.matchAll(/(?:\bfrom\s*|\bimport\s*)["'](\.[^"']+)["']/g)) {
      const imported = new URL(match[1], parent);
      if (imported.pathname !== entry.pathname) continue;
      sharedImports++;
      assert.equal(imported.href, entry.href, `${filename} must share the exact entry URL; otherwise React and createRoot execute twice`);
    }
  }
  assert.equal(entry.search, '', 'fixed-name Vite entry is canonical and cache policy handles freshness');
  assert(sharedImports > 0, 'this build exercises a real shared-entry import from a lazy module');
});
