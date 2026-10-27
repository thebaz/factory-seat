import { test } from "node:test";
import assert from "node:assert/strict";
import { sessionChoices, sessionDescription, validateSessionChoices, weekendSessionsEditor } from "../../src/LmuCareer.App/wwwroot/js/views/weekendSessions.js";

class Element {
  children = []; listeners = {}; style = {}; disabled = false;
  constructor(tag, text = "") { this.tag = tag; this.text = text; }
  append(child) { child.parent = this; this.children.push(child); }
  setAttribute(name, value) { this[name] = ["disabled", "required", "selected"].includes(name) ? true : value; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  get firstChild() { return this.children[0]; }
  remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); }
  get textContent() { return this.text + this.children.map((c) => c.textContent).join(""); }
}
globalThis.Node = Element;
globalThis.document = { createElement: (tag) => new Element(tag), createTextNode: (text) => new Element("text", text) };
const all = (node) => [node, ...node.children.flatMap(all)];

test("defaults keep both game sessions and do not mutate saved choices", () => {
  const defaults = sessionChoices();
  assert.equal(defaults.practice.mode, "keep");
  assert.equal(defaults.qualifying.mode, "keep");
  const saved = { practice: { mode: "private", minutes: 45, startTime: "08:00" } };
  const draft = sessionChoices(saved);
  draft.practice.minutes = 10;
  assert.equal(saved.practice.minutes, 45);
  assert.equal(draft.qualifying.minutes, 20);
});

test("briefing describes actual session choices instead of always showing Optional", () => {
  assert.equal(sessionDescription({ mode: "keep" }), "Keep LMU settings");
  assert.equal(sessionDescription({ mode: "skip" }), "Skip session");
  assert.equal(sessionDescription({ mode: "private", minutes: 15, startTime: "09:30" }), "Private, 15 min, 09:30");
  assert.equal(sessionDescription({ mode: "open", minutes: 45 }), "Open, 45 min, Circuit default");
});

test("session editor groups both sessions together with paired timing controls and one help section", () => {
  const form = weekendSessionsEditor(null, () => {}, () => {});
  const controls = all(form);
  const grid = controls.find((e) => e.className === "session-grid");
  assert.equal(grid.children.length, 2);
  assert.equal(controls.filter((e) => e.className === "session-timing").length, 2);
  assert.equal(controls.filter((e) => e.className === "session-help muted").length, 1);
});

test("active durations and times are validated; disabled values do not prevent skipping", () => {
  const draft = sessionChoices();
  draft.practice = { mode: "private", minutes: 0, startTime: "08:00" };
  assert.match(validateSessionChoices(draft), /duration/);
  draft.practice.minutes = 15.5;
  assert.match(validateSessionChoices(draft), /whole number/);
  draft.practice.minutes = 1440;
  draft.practice.startTime = "24:00";
  assert.match(validateSessionChoices(draft), /HH:mm/);
  draft.practice.startTime = "23:59";
  assert.equal(validateSessionChoices(draft), null);
  draft.practice = { mode: "skip", minutes: 0, startTime: "invalid" };
  assert.equal(validateSessionChoices(draft), null);
});

test("switching session modes enables only applicable controls and saves offline choices", async () => {
  let saved;
  const form = weekendSessionsEditor(null, async (choices) => { saved = choices; }, () => {});
  const controls = all(form);
  const selects = controls.filter((e) => e.tag === "select");
  const durations = controls.filter((e) => e.type === "number");
  const times = controls.filter((e) => e.type === "time");
  assert.equal(durations[0].disabled, true);
  selects[0].listeners.change({ target: { value: "private" } });
  assert.equal(durations[0].disabled, false);
  assert.equal(times[0].disabled, false);
  durations[0].listeners.input({ target: { value: "45" } });
  times[0].listeners.input({ target: { value: "08:30" } });
  selects[1].listeners.change({ target: { value: "skip" } });
  assert.equal(durations[1].disabled, true);
  await form.listeners.submit({ preventDefault() {} });
  assert.deepEqual(saved.practice, { mode: "private", minutes: 45, startTime: "08:30" });
  assert.equal(saved.qualifying.mode, "skip");
});

test("invalid active input prevents saving and displays an error", async () => {
  let saves = 0;
  const form = weekendSessionsEditor({ practice: { mode: "open", minutes: 0 } }, () => { saves++; }, () => {});
  await form.listeners.submit({ preventDefault() {} });
  assert.equal(saves, 0);
  assert.match(all(form).find((e) => e.role === "status").textContent, /duration/);
});

test("save failures remain visible and allow retry without contacting the game", async () => {
  const form = weekendSessionsEditor(null, async () => { throw new Error("Save failed"); }, () => {});
  await form.listeners.submit({ preventDefault() {} });
  assert.match(all(form).find((e) => e.role === "status").textContent, /Save failed/);
  assert.equal(all(form).find((e) => e.type === "submit").disabled, false);
});
