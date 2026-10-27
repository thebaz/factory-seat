import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { setLocale, t, tn, number } from "../../src/LmuCareer.App/wwwroot/js/i18n.js";

const locale = JSON.parse(readFileSync(new URL("../../src/LmuCareer.App/locales/it.json", import.meta.url), "utf8"));
const template = JSON.parse(readFileSync(new URL("../../src/LmuCareer.App/locales/_template.json", import.meta.url), "utf8"));
const flatten = (file) => Object.assign({}, ...Object.entries(file).filter(([key]) => key !== "_meta").map(([, group]) => group));
const strings = flatten(locale);
globalThis.document = { documentElement: {} };
setLocale({ language: "it", strings });

test("Italian covers the complete template with exactly matching placeholders", () => {
  const english = flatten(template);
  assert.deepEqual(Object.keys(strings).sort(), Object.keys(english).sort());
  const placeholders = (text) => [...text.matchAll(/\{\w+\}/g)].map(([match]) => match).sort();
  for (const [key, value] of Object.entries(strings)) {
    assert.ok(value.trim(), key);
    assert.deepEqual(placeholders(value), placeholders(key), key);
  }
});

test("Italian translates session controls, integration messages and substituted values", () => {
  assert.equal(document.documentElement.lang, "it");
  assert.equal(t("Launch weekend in LMU"), "Avvia weekend in LMU");
  assert.equal(t("Practice and qualifying"), "Prove libere e qualifiche");
  assert.equal(t("{mode}, {minutes} min, {start}", { mode: t("Private"), minutes: 15, start: "09:30" }), "Privata, 15 min, 09:30");
  assert.equal(t("Round {number}", { number: 3 }), "Gara 3");
  assert.equal(t("Untranslated runtime message"), "Untranslated runtime message");
});

test("Italian handles singular, plural and decimal numbers", () => {
  assert.equal(tn(1, "{n} lap", "{n} laps"), "1 giro");
  assert.equal(tn(2, "{n} lap", "{n} laps"), "2 giri");
  assert.equal(number(2.5), "2,5");
  assert.equal(t({ text: "Your best lap, {best}, was right on the fastest AI's {ai}.", args: { best: "2:25.166", ai: "2:25.200" } }), "Il tuo miglior giro, 2:25,166, era allo stesso livello del 2:25,200 della migliore IA.");
});
