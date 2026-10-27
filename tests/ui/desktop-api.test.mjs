import { test } from "node:test";
import assert from "node:assert/strict";

let receive;
const sent = [];
globalThis.window = { chrome: { webview: {
  addEventListener(name, handler) { assert.equal(name, "message"); receive = handler; },
  postMessage(message) { sent.push(message); },
} } };
const { call, onEvent } = await import("../../src/LmuCareer.App/wwwroot/js/api.js");

test("Windows requests and asynchronous replies retain the WebView2 message contract", async () => {
  const pending = call("saveWeekendConfiguration", { id: "career", roundNumber: 1 });
  const request = sent.at(-1);
  assert.equal(request.method, "saveWeekendConfiguration");
  assert.deepEqual(request.args, { id: "career", roundNumber: 1 });
  receive({ data: { id: request.id, ok: true, result: { saved: true } } });
  assert.deepEqual(await pending, { saved: true });
});

test("Windows backend errors preserve their translatable phrase", async () => {
  const pending = call("detectOwnedContent");
  const phrase = { text: "Start LMU", args: {} };
  receive({ data: { id: sent.at(-1).id, ok: false, error: "Start LMU", phrase } });
  await assert.rejects(pending, (error) => error.message === "Start LMU" && error.phrase === phrase);
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
