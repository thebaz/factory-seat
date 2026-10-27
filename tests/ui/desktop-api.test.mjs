import { test } from "node:test";
import assert from "node:assert/strict";

let receive;
const sent = [];
let autoReply = null;
globalThis.window = { chrome: { webview: {
  addEventListener(name, handler) { assert.equal(name, "message"); receive = handler; },
  postMessage(message) {
    sent.push(message);
    if (autoReply) queueMicrotask(() => receive({ data: { id: message.id, ok: true, result: autoReply(message) } }));
  },
} } };
const { call, onEvent, configureApiConsent, revokeApiConsent } = await import("../../src/LmuCareer.App/wwwroot/js/api.js");

test("Windows requests and asynchronous replies retain the WebView2 message contract", async () => {
  const pending = call("saveWeekendConfiguration", { id: "career", roundNumber: 1 });
  const request = sent.at(-1);
  assert.equal(request.method, "saveWeekendConfiguration");
  assert.deepEqual(request.args, { id: "career", roundNumber: 1 });
  receive({ data: { id: request.id, ok: true, result: { saved: true } } });
  assert.deepEqual(await pending, { saved: true });
});

test("Windows backend errors preserve their translatable phrase", async () => {
  const pending = call("getCareer");
  const phrase = { text: "Start LMU", args: {} };
  receive({ data: { id: sent.at(-1).id, ok: false, error: "Start LMU", phrase, code: "lmu-offline-session-confirmation-required" } });
  await assert.rejects(pending, (error) => error.message === "Start LMU" && error.phrase === phrase && error.code === "lmu-offline-session-confirmation-required");
});

test("XML watcher events are delivered through WebView2 subscribers", () => {
  const seen = [];
  const unsubscribe = onEvent("roundUpdate", (event) => seen.push(event.careerId));
  receive({ data: { event: "roundUpdate", careerId: "career" } });
  unsubscribe();
  receive({ data: { event: "roundUpdate", careerId: "another" } });
  assert.deepEqual(seen, ["career"]);
});

test("outside WebView2 requests reject instead of opening a browser HTTP transport", async () => {
  const chrome = window.chrome;
  delete window.chrome;
  try { await assert.rejects(call("init"), /Not running inside the app/); }
  finally { window.chrome = chrome; }
});

test("declining consent prevents every game API request but leaves local requests available", async () => {
  let prompts = 0;
  configureApiConsent(async () => { prompts++; return false; });
  const before = sent.length;
  for (const method of ["detectOwnedContent", "previewLmuWeekend", "applyLmuWeekend", "startLmuWeekend"]) {
    await assert.rejects(call(method), /API consent is required/);
  }
  assert.equal(sent.length, before);
  assert.equal(prompts, 4);
  autoReply = () => ({ view: {} });
  await call("startManualWeekend", { id: "career" });
  assert.equal(sent.at(-1).method, "startManualWeekend");
  assert.equal(sent.at(-1).args.apiConsent, undefined);
  autoReply = null;
});

test("accepted consent attaches a version to each API call, is reused for the session, and can be revoked", async () => {
  let prompts = 0;
  configureApiConsent(async () => { prompts++; return true; });
  autoReply = () => ({});
  for (const method of ["detectOwnedContent", "previewLmuWeekend", "applyLmuWeekend", "startLmuWeekend"]) {
    await call(method, { id: "career" });
    assert.equal(sent.at(-1).args.apiConsent, true);
    assert.equal(sent.at(-1).args.apiConsentVersion, "lmu-api-v1");
  }
  assert.equal(prompts, 1);
  revokeApiConsent();
  await call("detectOwnedContent");
  assert.equal(prompts, 2);
  autoReply = null;
});
