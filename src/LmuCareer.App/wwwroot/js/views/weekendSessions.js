import { h, put, clear } from "../dom.js";
import { t, lmu } from "../i18n.js";

export function sessionChoices(saved) {
  return {
    practice: { mode: "keep", minutes: 60, startTime: null, ...saved?.practice },
    qualifying: { mode: "keep", minutes: 20, startTime: null, ...saved?.qualifying },
  };
}

export function validateSessionChoices(choices) {
  for (const session of [choices.practice, choices.qualifying]) {
    if (!session || !["keep", "skip", "open", "private"].includes(session.mode)) return t("Choose a supported session mode.");
    if (!["open", "private"].includes(session.mode)) continue;
    if (!Number.isInteger(session.minutes) || session.minutes < 1 || session.minutes > 1440)
      return t("Session duration must be a whole number between 1 and 1440 minutes.");
    if (session.startTime && !/^([01]\d|2[0-3]):[0-5]\d$/.test(session.startTime)) return t("Use HH:mm for the session start time, or leave it blank for the circuit default.");
  }
  return null;
}

export function sessionDescription(session) {
  return session.mode === "keep" ? t("Keep LMU settings") : session.mode === "skip" ? t("Skip session")
    : t("{mode}, {minutes} min, {start}", {
      mode: session.mode === "private" ? t("Private") : t("Open"), minutes: session.minutes, start: session.startTime || t("Circuit default"),
    });
}

export function sessionSummary(saved) {
  const choices = sessionChoices(saved);
  return `${lmu("Practice")}: ${sessionDescription(choices.practice)} · ${lmu("Qualifying")}: ${sessionDescription(choices.qualifying)}`;
}

/** Shared compact fields for the per-race setup and the default-session preferences. */
export function sessionFields(choices, controls = []) {
  const field = (key, label) => {
    const session = choices[key];
    const active = () => session.mode === "open" || session.mode === "private";
    const duration = h("input", { class: "input", type: "number", min: "1", max: "1440", step: "1", required: true,
      value: session.minutes, disabled: !active(), oninput: (e) => { session.minutes = Number(e.target.value); } });
    const time = h("input", { class: "input", type: "time", step: "60", value: session.startTime ?? "", disabled: !active(),
      oninput: (e) => { session.startTime = e.target.value || null; } });
    const mode = h("select", { class: "input", onchange: (e) => {
      session.mode = e.target.value;
      duration.disabled = time.disabled = !active();
    } }, [
      ["keep", t("Keep LMU settings")], ["skip", t("Skip session")], ["open", t("Open")], ["private", t("Private")],
    ].map(([value, text]) => h("option", { value, selected: session.mode === value }, text)));
    controls.push(mode, duration, time);
    return h("fieldset", { class: "session-card" }, h("legend", {}, label),
      h("label", { class: "field" }, t("Session mode"), mode),
      h("div", { class: "session-timing" },
        h("label", { class: "field" }, t("Duration (minutes)"), duration),
        h("label", { class: "field" }, t("Session start time"), time)));
  };
  return h("div", { class: "session-grid" }, field("practice", lmu("Practice")), field("qualifying", lmu("Qualifying")));
}

/** Offline editor: saving preferences never contacts LMU or applies game settings. */
export function weekendSessionsEditor(saved, onSave, onCancel) {
  const choices = sessionChoices(saved);
  const status = h("div", { role: "status", "aria-live": "polite" });
  const controls = [];
  const save = h("button", { class: "btn", type: "submit" }, t("Save session choices"));
  const cancel = h("button", { class: "btn ghost", type: "button", onclick: onCancel }, t("Cancel"));
  return h("form", { class: "session-options", onsubmit: async (event) => {
    event.preventDefault();
    const error = validateSessionChoices(choices);
    if (error) { put(clear(status), error); return; }
    const wasDisabled = controls.map((control) => control.disabled);
    for (const control of controls) control.disabled = true;
    save.disabled = cancel.disabled = true;
    try { await onSave(structuredClone(choices)); }
    catch (error) { put(clear(status), error.phrase ? t(error.phrase) : error.message); }
    finally {
      controls.forEach((control, i) => { control.disabled = wasDisabled[i]; });
      save.disabled = cancel.disabled = false;
    }
  } }, h("h2", {}, t("Practice and qualifying")),
  h("p", { class: "muted" }, t("Saved for future weekends. Nothing changes in LMU until you preview and apply.")),
  sessionFields(choices, controls),
  h("div", { class: "session-help muted" },
    h("p", {}, t("Blank time: circuit default. Open/private: one session. Skip practice: all practice sessions disabled.")),
    h("p", {}, t("Additional formats, weather and grip are not configured here."))),
  status, h("div", { class: "modal-actions" }, cancel, save));
}
