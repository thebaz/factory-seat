import { test } from "node:test";
import assert from "node:assert/strict";
import { weekendConfiguration, launchConfiguredWeekend, startManualWeekend, weekendSetup } from "../../src/LmuCareer.App/wwwroot/js/views/weekendSetup.js";

class Element {
  children = []; listeners = {}; style = {}; disabled = false;
  constructor(tag, text = "") { this.tag = tag; this.text = text; }
  append(child) { child.parent = this; this.children.push(child); }
  setAttribute(name, value) { this[name] = ["disabled", "required", "selected"].includes(name) ? true : value; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  get firstChild() { return this.children[0]; }
  remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); }
  get textContent() { return this.text + this.children.map((c) => c.textContent).join(""); }
  set textContent(value) { this.text = value; this.children = []; }
}
globalThis.Node = Element;
globalThis.document = { createElement: (tag) => new Element(tag), createTextNode: (text) => new Element("text", text) };
const all = (node) => [node, ...node.children.flatMap(all)];
const round = { state: "Upcoming", raceMinutes: 30, fuelMultiplier: 3, tireMultiplier: 2, timeScale: 20, startTime: "11:00" };
const startButton = (form) => all(form).find((e) => e.tag === "button" && e.type === "button");
const radio = (form, mode) => all(form).find((e) => e.type === "radio" && e.value === mode);

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

for (const confirmed of [false, true]) {
  test(`retained offline preview receives explicit per-attempt permission (${confirmed})`, async () => {
    const calls = [];
    const launch = launchConfiguredWeekend(async (method, args) => {
      calls.push([method, args]);
      if (method === "previewLmuWeekend" && !args.replaceMonitoredOfflineSession)
        throw Object.assign(new Error("LMU reports a retained offline session"), { code: "lmu-offline-session-confirmation-required" });
      return { token: "prepared", verified: true, requested: true };
    }, { id: "career", roundNumber: 1, replaceMonitoredOfflineSession: confirmed });
    if (confirmed) {
      assert.equal((await launch).requested, true);
      assert.equal(calls[1][1].replaceMonitoredOfflineSession, true);
      assert.deepEqual(calls.map(([method]) => method), ["saveWeekendConfiguration", "previewLmuWeekend", "applyLmuWeekend", "startLmuWeekend"]);
    } else {
      await assert.rejects(launch);
      assert.deepEqual(calls.map(([method]) => method), ["saveWeekendConfiguration", "previewLmuWeekend"]);
    }
  });
}

test("inline replacement requires a fresh unchecked checkbox and explicit click; cancel sends no game commands", async () => {
  const permissions = [];
  const form = weekendSetup(round, null, {
    onLaunch: async (configuration, progress, options) => {
      permissions.push(options?.replaceMonitoredOfflineSession === true);
      if (!options?.replaceMonitoredOfflineSession) throw Object.assign(new Error("Retained offline session"), {
        stage: "preview", code: "lmu-offline-session-confirmation-required",
      });
    },
  });
  radio(form, "assisted").listeners.change();
  await startButton(form).listeners.click();
  const checkbox = all(form).find(e => e.type === "checkbox");
  const replace = all(form).find(e => e.tag === "button" && e.textContent === "Replace session and launch");
  assert.ok(checkbox);
  assert.equal(!!checkbox.checked, false);
  assert.equal(replace.disabled, true);
  await replace.listeners.click();
  assert.deepEqual(permissions, [false]);
  checkbox.checked = true;
  checkbox.listeners.change();
  assert.equal(replace.disabled, false);
  assert.deepEqual(permissions, [false]);
  await replace.listeners.click();
  assert.deepEqual(permissions, [false, true]);
  await startButton(form).listeners.click();
  const fresh = all(form).find(e => e.type === "checkbox");
  assert.equal(!!fresh.checked, false);
  await all(form).find(e => e.tag === "button" && e.textContent === "Cancel").listeners.click();
  assert.deepEqual(permissions, [false, true, false]);
  assert.equal(all(form).find(e => e.type === "checkbox"), undefined);
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
  radio(form, "assisted").listeners.change();
  const launch = startButton(form);
  const pending = launch.listeners.click();
  await launch.listeners.click();
  assert.equal(launches, 1);
  assert.equal(launch.disabled, true);
  finish();
  await pending;
});

test("armed weekends cannot save or restart; uncertain launch allows manual continuation but no API replay", async () => {
  let calls = 0;
  const locked = weekendSetup({ ...round, state: "Armed" }, null, { onSave: () => calls++, onLaunch: () => calls++ });
  await locked.listeners.submit({ preventDefault() {} });
  await all(locked).find((e) => e.tag === "button" && e.type === "button").listeners.click();
  assert.equal(calls, 0);
  let manual = 0;
  const form = weekendSetup(round, null, {
    onLaunch: async () => { throw Object.assign(new Error("Timeout"), { stage: "launch" }); },
    onManual: async () => { manual++; },
  });
  radio(form, "assisted").listeners.change();
  const button = startButton(form);
  await button.listeners.click();
  assert.equal(button.disabled, false);
  assert.equal(button.textContent, "Continue manually");
  assert.equal(radio(form, "assisted").disabled, true);
  assert.equal(all(form).find((e) => e.type === "submit").disabled, true);
  assert.match(all(form).find((e) => e.role === "status").textContent, /LMU may already be loading/);
  await button.listeners.click();
  assert.equal(manual, 1);
});

test("manual is the default and calls only the local manual endpoint", async () => {
  const calls = [];
  const request = async (method, args) => { calls.push([method, args]); return { view: {}, alreadyStarted: false }; };
  const form = weekendSetup(round, null, {
    onManual: (configuration) => startManualWeekend(request, { id: "career", roundNumber: 1, configuration }),
    onLaunch: () => assert.fail("Manual must not call assisted setup"),
  });
  assert.equal(radio(form, "manual").checked, true);
  assert.equal(startButton(form).textContent, "Start manual weekend");
  assert.equal(calls.length, 0);
  await startButton(form).listeners.click();
  assert.deepEqual(calls.map(([method]) => method), ["startManualWeekend"]);
  assert.equal(calls[0][1].configuration.raceMinutes, 30);
});

test("a refused LMU preview keeps Assisted selected, shows the error and sends no load or manual start", async () => {
  const calls = [];
  const message = "Return to LMU's offline Race Weekend menu first. A running or loading session will not be changed.";
  const form = weekendSetup(round, null, {
    onLaunch: configuration => launchConfiguredWeekend(async method => {
      calls.push(method);
      if (method === "previewLmuWeekend") throw new Error(message);
      return {};
    }, { configuration }),
    onManual: () => assert.fail("No implicit manual start"),
  });
  radio(form, "assisted").listeners.change();
  await startButton(form).listeners.click();
  assert.equal(radio(form, "assisted").checked, true);
  assert.equal(startButton(form).textContent, "Launch weekend in LMU");
  assert.match(all(form).find(e => e.role === "status").textContent, /Return to LMU/);
  assert.deepEqual(calls, ["saveWeekendConfiguration", "previewLmuWeekend"]);
});

for (const stage of ["apply"]) {
  test(`${stage} failure leaves the manual path available without further game API calls`, async () => {
    let launches = 0, manuals = 0;
    const form = weekendSetup(round, null, {
      onLaunch: async () => { launches++; throw Object.assign(new Error("Unavailable"), { stage }); },
      onManual: async () => { manuals++; },
    });
    radio(form, "assisted").listeners.change();
    await startButton(form).listeners.click();
    assert.equal(radio(form, "manual").checked, true);
    assert.equal(startButton(form).disabled, false);
    await startButton(form).listeners.click();
    assert.equal(launches, 1);
    assert.equal(manuals, 1);
  });
}
