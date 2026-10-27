import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { apiConsentDialog, createApiConsentGate } from "../../src/LmuCareer.App/wwwroot/js/apiConsent.js";
import { setLocale } from "../../src/LmuCareer.App/wwwroot/js/i18n.js";

class Element {
  children = []; listeners = {}; style = {}; disabled = false; checked = false;
  constructor(tag, text = "") { this.tag = tag; this.text = text; }
  append(child) { this.children.push(child); }
  setAttribute(name, value) { this[name] = value === "" ? true : value; }
  addEventListener(name, handler) { this.listeners[name] = handler; }
  get textContent() { return this.text + this.children.map(c => c.textContent).join(""); }
}
globalThis.Node = Element;
globalThis.document = { documentElement: {}, createElement: tag => new Element(tag), createTextNode: text => new Element("text", text) };
const all = node => [node, ...node.children.flatMap(all)];

test("accept requires an unchecked-by-default checkbox and a separate explicit click", () => {
  let accepted = 0, cancelled = 0;
  const form = apiConsentDialog(() => accepted++, () => cancelled++);
  const checkbox = all(form).find(e => e.type === "checkbox");
  const buttons = all(form).filter(e => e.tag === "button");
  const accept = buttons[1];
  assert.equal(checkbox.checked, false);
  assert.equal(accept.disabled, true);
  accept.listeners.click();
  assert.equal(accepted, 0);
  checkbox.checked = true;
  checkbox.listeners.change();
  assert.equal(accept.disabled, false);
  assert.equal(accepted, 0);
  checkbox.checked = false;
  checkbox.listeners.change();
  assert.equal(accept.disabled, true);
  checkbox.checked = true;
  checkbox.listeners.change();
  buttons[0].listeners.click();
  assert.equal(cancelled, 1);
  assert.equal(accepted, 0);
  accept.listeners.click();
  assert.equal(accepted, 1);
  assert.equal(all(apiConsentDialog(() => {}, () => {})).find(e => e.type === "checkbox").checked, false);
});

test("one prompt serves concurrent requests, consent is memory-only, and revocation prompts again", async () => {
  const gate = createApiConsentGate();
  let prompts = 0;
  gate.setPrompt(async () => { prompts++; return true; });
  assert.deepEqual(await Promise.all([gate.request(), gate.request()]), [true, true]);
  assert.equal(prompts, 1);
  assert.equal(await gate.request(), true);
  assert.equal(prompts, 1);
  assert.equal(gate.granted(), true);
  assert.equal(await createApiConsentGate().request(), false);
  gate.revoke();
  assert.equal(gate.granted(), false);
  assert.equal(await gate.request(), true);
  assert.equal(prompts, 2);
});

test("revocation while a dialog is open invalidates its late acceptance", async () => {
  const gate = createApiConsentGate();
  let accept;
  gate.setPrompt(() => new Promise(resolve => { accept = resolve; }));
  const pending = gate.request();
  await Promise.resolve();
  gate.revoke();
  accept(true);
  assert.equal(await pending, false);
  assert.equal(gate.granted(), false);
});

test("all consent and revocation text is translated in every shipped locale", () => {
  const source = readFileSync(new URL("../../src/LmuCareer.App/wwwroot/js/apiConsent.js", import.meta.url), "utf8");
  const keys = new Set([...source.matchAll(/\bt\("([^"]+)"/g)].map(match => match[1]));
  for (const key of ["LMU API consent", "Revoke API consent", "API consent revoked. Manual mode remains available.", "API consent is required. Manual mode remains available."]) keys.add(key);
  const folder = new URL("../../src/LmuCareer.App/locales/", import.meta.url);
  const files = readdirSync(folder).filter(name => name.endsWith(".json") && !name.startsWith("_"));
  assert.equal(files.length, 6); // Plus English, supplied by the source strings.
  for (const file of files) {
    const locale = JSON.parse(readFileSync(new URL(file, folder), "utf8"));
    const strings = Object.assign({}, ...Object.entries(locale).filter(([k]) => k !== "_meta").map(([,v]) => v));
    for (const key of keys) assert.ok(typeof strings[key] === "string" && strings[key].trim(), `${file}: ${key}`);
    setLocale({ language: locale._meta.language, strings });
    const dialog = apiConsentDialog(() => {}, () => {});
    assert.ok(dialog.textContent.includes(strings["Experimental LMU API access"]), file);
  }
  setLocale({ language: "en", strings: {} });
});
