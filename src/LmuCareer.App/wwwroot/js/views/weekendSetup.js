import { h, put, clear } from "../dom.js";
import { t, lmu, number } from "../i18n.js";
import { sessionChoices, sessionFields, validateSessionChoices } from "./weekendSessions.js";

export const RACE_LENGTHS = [5, 10, 15, 20, 30, 45, 60, 72, 90, 120, 144, 180, 240, 360, 480, 600, 720, 1440];

export function weekendConfiguration(round, defaults) {
  return {
    raceMinutes: round.raceMinutes, fuelUsage: round.fuelMultiplier, tyreWear: round.tireMultiplier,
    timeScale: round.timeScale, startTime: round.startTime || null,
    sessions: sessionChoices(round.weekendSessions ?? defaults),
  };
}

/** Local persistence and the existing XML matching boundary only; no game API calls. */
export const startManualWeekend = (request, args) => request("startManualWeekend", args);

/** One explicit launch action; no retry and never a load command after failed readback. */
export async function launchConfiguredWeekend(request, args, progress = () => {}) {
  let stage = "save";
  try {
    progress(stage);
    await request("saveWeekendConfiguration", args);
    stage = "preview";
    progress(stage);
    const previewArgs = { id: args.id, roundNumber: args.roundNumber };
    if (args.replaceMonitoredOfflineSession === true) previewArgs.replaceMonitoredOfflineSession = true;
    const preview = await request("previewLmuWeekend", previewArgs);
    stage = "apply";
    progress(stage);
    const applied = await request("applyLmuWeekend", { id: args.id, token: preview.token, confirmed: true });
    if (!applied.verified) throw new Error(t("Settings did not match after verification. The weekend was not launched."));
    stage = "launch";
    progress(stage);
    return await request("startLmuWeekend", { id: args.id, token: preview.token, confirmed: true });
  } catch (error) {
    error.stage = stage;
    throw error;
  }
}

/** Per-round editor. The caller owns persistence; neither defaults nor other rounds are changed. */
export function weekendSetup(round, defaults, { onSave, onManual, onLaunch }) {
  const draft = weekendConfiguration(round, defaults);
  const locked = round.state !== "Upcoming";
  const status = h("div", { class: "weekend-setup-status", role: "status", "aria-live": "polite" });
  const replacementPanel = h("section", { class: "api-consent", hidden: true });
  function clearReplacement() { clear(replacementPanel); replacementPanel.hidden = true; }
  function offerReplacement() {
    clearReplacement();
    const replace = h("button", { class: "btn danger", type: "button", disabled: true, onclick: () => {
      if (checkbox.checked) return submit("assisted", true);
    } }, t("Replace session and launch"));
    const checkbox = h("input", { type: "checkbox", onchange: () => { replace.disabled = !checkbox.checked; } });
    put(replacementPanel,
      h("h3", {}, t("Replace the offline session reported by LMU?")),
      h("p", {}, t("LMU may report a previous session even on its home screen. Continuing will replace any loaded offline session with this career weekend. Unsaved session progress may be lost. Only continue if you intend to start a new weekend.")),
      h("label", { class: "api-consent-check" }, checkbox,
        h("span", {}, t("I understand that unsaved session progress may be lost."))),
      h("div", { class: "modal-actions" },
        h("button", { class: "btn ghost", type: "button", onclick: () => {
          clearReplacement();
          put(clear(status), t("Launch cancelled. No LMU settings were changed."));
        } }, t("Cancel")), replace));
    replacementPanel.hidden = false;
    checkbox.focus?.();
  }
  const fields = h("fieldset", { class: "weekend-event-fields", disabled: locked });
  const choose = (key, label, values, format) => h("label", { class: "field" }, label,
    h("select", { class: "input", onchange: (e) => { draft[key] = Number(e.target.value); } },
      values.map((value) => h("option", { value, selected: draft[key] === value }, format(value)))));
  put(fields,
    h("legend", {}, lmu("Event settings")),
    h("div", { class: "race-settings-grid" },
      choose("raceMinutes", lmu("Race length"), RACE_LENGTHS, (n) => t("{minutes} min", { minutes: n })),
      h("label", { class: "field" }, lmu("Race start time"), h("input", { class: "input", type: "time", value: draft.startTime ?? "",
        oninput: (e) => { draft.startTime = e.target.value || null; } })),
      choose("fuelUsage", lmu("Fuel Usage"), [1, 2, 3], (n) => n === 1 ? lmu("Real") : `×${n}`),
      choose("tyreWear", lmu("Tyre Wear"), [1, 2, 3], (n) => n === 1 ? lmu("Real") : `×${n}`),
      choose("timeScale", lmu("Time Scale"), Array.from({ length: 60 }, (_, i) => i + 1), (n) => n === 1 ? lmu("Normal") : `×${number(n)}`)),
    sessionFields(draft.sessions),
    h("p", { class: "muted session-help" }, t("Blank time: circuit default. Open/private: one session. Skip practice: all practice sessions disabled.")));
  const save = h("button", { class: "btn ghost", type: "submit", disabled: locked }, t("Save configuration"));
  const start = h("button", { class: "btn", type: "button", disabled: locked, onclick: () => submit(mode) });
  const modeHint = h("p", { class: "muted" });
  let mode = "manual", assistedBlocked = false, uncertainLoad = false;
  const modeInput = (value, label) => h("label", { class: "weekend-mode" },
    h("input", { type: "radio", name: "weekend-start-mode", value, disabled: locked, onchange: () => {
      if (busy || locked || (value === "assisted" && assistedBlocked)) return;
      mode = value;
      updateMode();
    } }), label);
  const manualChoice = modeInput("manual", t("Manual"));
  const assistedChoice = modeInput("assisted", t("Assisted (LMU API)"));
  const manualRadio = manualChoice.firstChild, assistedRadio = assistedChoice.firstChild;
  function updateMode() {
    clearReplacement();
    manualRadio.checked = mode === "manual";
    assistedRadio.checked = mode === "assisted";
    start.textContent = mode === "assisted" ? t("Launch weekend in LMU")
      : uncertainLoad ? t("Continue manually") : t("Start manual weekend");
    put(clear(modeHint), mode === "manual"
      ? t("Manual is always available: save these choices, start the weekend here, then configure and drive in LMU yourself. No game API calls.")
      : t("Optional automation uses LMU's internal API. By launching, you authorize applying these settings and loading the weekend. LMU must be at its offline Race Weekend menu."));
  }
  let busy = false;
  async function submit(action, replaceMonitoredOfflineSession = false) {
    if (busy || locked || (action === "assisted" && assistedBlocked) || (action === "save" && uncertainLoad)) return;
    const error = validateSessionChoices(draft.sessions);
    if (error) { put(clear(status), error); return; }
    clearReplacement();
    busy = true;
    fields.disabled = save.disabled = start.disabled = manualRadio.disabled = assistedRadio.disabled = true;
    try {
      if (action === "assisted") await onLaunch(structuredClone(draft), (stage) => {
        put(clear(status), { save: t("Saving configuration…"), preview: t("Checking LMU availability…"),
          apply: t("Applying settings and reading them back…"), launch: t("Loading the weekend in LMU…") }[stage]);
      }, { replaceMonitoredOfflineSession: replaceMonitoredOfflineSession === true });
      else if (action === "manual") await onManual(structuredClone(draft));
      else { await onSave(structuredClone(draft)); put(clear(status), t("Configuration saved for this race.")); }
    } catch (error) {
      assistedBlocked ||= error.stage === "launch" || error.stage === "apply";
      uncertainLoad ||= error.stage === "launch";
      put(clear(status), error.phrase ? t(error.phrase) : error.message);
      if (action === "assisted" && error.stage === "preview" && error.code === "lmu-offline-session-confirmation-required") {
        clear(status);
        offerReplacement();
      } else if (action === "assisted" && error.stage !== "save") {
        // A preview failure sent no configuration/load command. Preserve the
        // user's assisted choice; manual remains an explicit alternative.
        if (assistedBlocked) {
          mode = "manual";
          updateMode();
        }
        put(status, h("p", {}, uncertainLoad
          ? t("LMU may already be loading. Check the game, then continue manually. An existing weekend start time is preserved and no load command is repeated.")
          : error.stage === "apply"
            ? t("Some LMU settings may have changed, but no load command was sent. Check them against this configuration and continue manually.")
            : t("Assisted setup is unavailable. You can start manually without the game API.")));
      }
    } finally {
      busy = false;
      fields.disabled = save.disabled = uncertainLoad;
      start.disabled = manualRadio.disabled = false;
      assistedRadio.disabled = assistedBlocked;
    }
  }
  updateMode();
  return h("form", { class: "weekend-setup", onsubmit: (event) => { event.preventDefault(); return submit("save"); } },
    fields,
    locked ? h("p", { class: "muted" }, t("Weekend in progress. Cancel it before editing or launching again."))
      : [h("div", { class: "weekend-modes", role: "group", "aria-label": t("How to start this weekend") }, manualChoice, assistedChoice), modeHint],
    h("p", { class: "session-help muted" }, t("Weather, grip, warmup, difficulty and AI grid remain controlled by LMU.")),
    status, replacementPanel, h("div", { class: "weekend-setup-actions" }, save, start));
}
