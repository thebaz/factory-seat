import { test } from "node:test";
import assert from "node:assert/strict";
import { weekendConfiguration, launchConfiguredWeekend, weekendSetup } from "../../src/LmuCareer.App/wwwroot/js/views/weekendSetup.js";

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
const round = { state: "Upcoming", raceMinutes: 30, fuelMultiplier: 3, tireMultiplier: 2, timeScale: 20, startTime: "11:00" };

test("each race owns a draft; saved choices override defaults without mutating either", () => {
  const defaults = { practice: { mode: "open", minutes: 60 } };
  const first = weekendConfiguration(round, defaults);
  first.sessions.practice.minutes = 10;
  assert.equal(defaults.practice.minutes, 60);
  const saved = { ...round, weekendSessions: { practice: { mode: "skip", minutes: 45 } } };
  const draft = weekendConfiguration(saved, defaults);
  assert.equal(draft.sessions.practice.mode, "skip");
  draft.sessions.practice.minutes = 15;
  assert.equal(saved.weekendSessions.practice.minutes, 45);
  assert.equal(round.raceMinutes, 30);
});

test("one launch action saves then previews, applies, verifies and launches using the same token", async () => {
  const calls = [], stages = [];
  const args = { id: "career", roundNumber: 2, configuration: weekendConfiguration(round) };
  const result = await launchConfiguredWeekend(async (method, input) => {
    calls.push([method, input]);
    return { token: "prepared", verified: true, requested: true };
  }, args, (stage) => stages.push(stage));
  assert.equal(result.requested, true);
  assert.deepEqual(stages, ["save", "preview", "apply", "launch"]);
  assert.deepEqual(calls.map(([method]) => method), ["saveWeekendConfiguration", "previewLmuWeekend", "applyLmuWeekend", "startLmuWeekend"]);
  assert.deepEqual(calls[1][1], { id: "career", roundNumber: 2 });
  assert.deepEqual(calls[2][1], calls[3][1]);
  assert.equal(calls[3][1].confirmed, true);
});

test("readback mismatch prevents launch", async () => {
  const calls = [];
  await assert.rejects(launchConfiguredWeekend(async (method) => {
    calls.push(method);
    return { token: "prepared", verified: false };
  }, {}), (error) => error.stage === "apply" && /not launched/.test(error.message));
  assert.equal(calls.includes("startLmuWeekend"), false);
});

for (const [index, stage] of ["save", "preview", "apply", "launch"].entries()) {
  test(`failure at ${stage} stops without retries or later commands`, async () => {
    let calls = 0;
    await assert.rejects(launchConfiguredWeekend(async () => {
      if (calls++ === index) throw new Error("failure");
      return { token: "prepared", verified: true };
    }, {}), (error) => error.stage === stage);
    assert.equal(calls, index + 1);
  });
}

test("event editor contains both session groups, saves offline, and blocks duplicate launch clicks", async () => {
  let saves = 0, launches = 0, finish;
  const form = weekendSetup(round, null, {
    onSave: async () => { saves++; },
    onLaunch: () => { launches++; return new Promise((resolve) => { finish = resolve; }); },
  });
  assert.equal(all(form).filter((e) => e.tag === "legend").length, 3);
  await form.listeners.submit({ preventDefault() {} });
  assert.equal(saves, 1);
  const launch = all(form).find((e) => e.tag === "button" && e.type === "button");
  const pending = launch.listeners.click();
  await launch.listeners.click();
  assert.equal(launches, 1);
  assert.equal(launch.disabled, true);
  finish();
  await pending;
});

test("armed weekends cannot save or relaunch; uncertain launch leaves launch disabled", async () => {
  let calls = 0;
  const locked = weekendSetup({ ...round, state: "Armed" }, null, { onSave: () => calls++, onLaunch: () => calls++ });
  await locked.listeners.submit({ preventDefault() {} });
  await all(locked).find((e) => e.tag === "button" && e.type === "button").listeners.click();
  assert.equal(calls, 0);
  const form = weekendSetup(round, null, { onLaunch: async () => { throw Object.assign(new Error("Timeout"), { stage: "launch" }); } });
  const button = all(form).find((e) => e.tag === "button" && e.type === "button");
  await button.listeners.click();
  assert.equal(button.disabled, true);
  assert.match(all(form).find((e) => e.role === "status").textContent, /Check LMU/);
});
