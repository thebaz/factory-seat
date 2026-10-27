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

/** One explicit launch action; no retry and never a load command after failed readback. */
export async function launchConfiguredWeekend(request, args, progress = () => {}) {
  let stage = "save";
  try {
    progress(stage);
    await request("saveWeekendConfiguration", args);
    stage = "preview";
    progress(stage);
    const preview = await request("previewLmuWeekend", { id: args.id, roundNumber: args.roundNumber });
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
export function weekendSetup(round, defaults, { onSave, onLaunch }) {
  const draft = weekendConfiguration(round, defaults);
  const locked = round.state !== "Upcoming";
  const status = h("div", { class: "weekend-setup-status", role: "status", "aria-live": "polite" });
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
  const launch = h("button", { class: "btn", type: "button", disabled: locked, onclick: () => submit(true) }, t("Launch weekend in LMU"));
  let busy = false;
  async function submit(start) {
    if (busy || locked) return;
    const error = validateSessionChoices(draft.sessions);
    if (error) { put(clear(status), error); return; }
    busy = true;
    fields.disabled = save.disabled = launch.disabled = true;
    let uncertain = false;
    try {
      if (start) await onLaunch(structuredClone(draft), (stage) => {
        put(clear(status), { save: t("Saving configuration…"), preview: t("Checking LMU availability…"),
          apply: t("Applying settings and reading them back…"), launch: t("Loading the weekend in LMU…") }[stage]);
      });
      else { await onSave(structuredClone(draft)); put(clear(status), t("Configuration saved for this race.")); }
    } catch (error) {
      uncertain = error.stage === "launch" || error.stage === "apply";
      put(clear(status), error.phrase ? t(error.phrase) : error.message);
      if (uncertain) put(status, h("p", {}, t("Check LMU before trying again. Reopen this race configuration to make a new request.")));
    } finally {
      busy = false;
      fields.disabled = save.disabled = false;
      launch.disabled = uncertain;
    }
  }
  return h("form", { class: "weekend-setup", onsubmit: (event) => { event.preventDefault(); return submit(false); } },
    fields,
    locked ? h("p", { class: "muted" }, t("Weekend in progress. Cancel it before editing or launching again."))
      : h("p", { class: "muted" }, t("Choices apply only to this race. Launch saves, applies and verifies them, then loads the weekend. Open LMU at its offline Race Weekend menu first.")),
    h("p", { class: "session-help muted" }, t("Weather, grip, warmup, difficulty and AI grid remain controlled by LMU.")),
    status, h("div", { class: "weekend-setup-actions" }, save, launch));
}
