// Small DOM/bridge double: verifies the checklist's async behavior without Windows/WebView2.
// Run: node --test tests/ui/packs.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";

class Element {
  children = [];
  listeners = {};
  style = {};
  dataset = {};
  isConnected = true;
  constructor(tag, text = "") { this.tag = tag; this.text = text; }
  append(child) { child.parent = this; this.children.push(child); }
  setAttribute(name, value) { this[name] = value; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  get firstChild() { return this.children[0]; }
  remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); }
  get textContent() { return this.text + this.children.map((c) => c.textContent).join(""); }
  set textContent(value) { this.text = value; this.children = []; }
}
globalThis.Node = Element;
globalThis.document = { createElement: (tag) => new Element(tag), createTextNode: (text) => new Element("text", text) };
let receive;
const requests = [];
globalThis.window = { chrome: { webview: {
  addEventListener: (_, callback) => { receive = callback; },
  postMessage: (request) => requests.push(request),
} } };
const { packChecklist, PACK_GROUPS } = await import("../../src/LmuCareer.App/wwwroot/js/views/packs.js");
const { configureApiConsent } = await import("../../src/LmuCareer.App/wwwroot/js/api.js");
configureApiConsent(async () => true);
const flushConsent = () => new Promise(resolve => setImmediate(resolve));
const cat = { packs: PACK_GROUPS.flatMap((g) => g.ids).map((id) => ({ id, name: id })), cars: [], tracks: [] };
const reply = (body) => receive({ data: { id: requests.at(-1).id, ...body } });
const button = (panel) => panel.children.find((c) => c.tag === "button");
const status = (panel) => panel.children.find((c) => c.role === "status");

test("detects only on click, disables the button and preserves unresolved manual selections", async () => {
  requests.length = 0;
  let updated;
  const [panel] = packChecklist(cat, ["2024-1", "us-3"], (...args) => { updated = args; });
  assert.equal(requests.length, 0);
  const pending = button(panel).listeners.click();
  assert.equal(button(panel).disabled, true);
  await flushConsent();
  assert.equal(requests[0].method, "detectOwnedContent");
  const result = { checkedPacks: ["2024-1", "2024-2"], ownedPacks: ["2024-2"], unresolvedPacks: ["us-3"] };
  reply({ ok: true, result });
  await pending;
  assert.deepEqual(updated, [["us-3", "2024-2"], result]);
});

test("unavailable API leaves the selection alone and allows Retry", async () => {
  let changes = 0;
  const [panel] = packChecklist(cat, ["2024-1"], () => { changes++; });
  const pending = button(panel).listeners.click();
  await flushConsent();
  reply({ ok: false, error: "Start LMU and reach the main menu." });
  await pending;
  assert.equal(changes, 0);
  assert.match(status(panel).textContent, /Start LMU/);
  assert.equal(button(panel).disabled, false);
  assert.equal(button(panel).textContent, "Retry");
  const retry = button(panel).listeners.click();
  await flushConsent();
  reply({ ok: true, result: { checkedPacks: [], ownedPacks: [], unresolvedPacks: [] } });
  await retry;
  assert.equal(changes, 1);
});

test("late response does not modify a closed checklist", async () => {
  let changes = 0;
  const [panel] = packChecklist(cat, [], () => { changes++; });
  const pending = button(panel).listeners.click();
  panel.isConnected = false;
  await flushConsent();
  reply({ ok: true, result: { checkedPacks: ["2024-1"], ownedPacks: ["2024-1"], unresolvedPacks: [] } });
  await pending;
  assert.equal(changes, 0);
});

test("successful detection shows a review notice and names unresolved packs", () => {
  const [panel] = packChecklist(cat, [], () => {}, { unresolvedPacks: ["us-3"] });
  assert.match(status(panel).textContent, /Review the selection/);
  assert.match(status(panel).textContent, /us-3/);
});
