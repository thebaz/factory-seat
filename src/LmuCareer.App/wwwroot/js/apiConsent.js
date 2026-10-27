import { h } from "./dom.js";
import { t } from "./i18n.js";

export const API_CONSENT_VERSION = "lmu-api-v1";

/** Consent is memory-only, shared by API actions and invalidated by revoke/reload. */
export function createApiConsentGate() {
  let prompt = null, granted = false, pending = null, revision = 0;
  const revoke = () => { granted = false; pending = null; revision++; };
  return {
    setPrompt(callback) { revoke(); prompt = callback; },
    revoke,
    granted: () => granted,
    request() {
      if (granted) return Promise.resolve(true);
      if (!pending) {
        const at = revision;
        const current = Promise.resolve().then(() => prompt?.() ?? false).then((accepted) => {
          const allowed = at === revision && accepted === true;
          if (allowed) granted = true;
          return allowed;
        }).finally(() => { if (pending === current) pending = null; });
        pending = current;
      }
      return pending;
    },
  };
}

/** Neither checkbox changes nor cancellation grant consent; only explicit acceptance does. */
export function apiConsentDialog(onAccept, onCancel) {
  const accept = h("button", { class: "btn", type: "button", disabled: true, onclick: () => {
    if (checkbox.checked) onAccept();
  } }, t("Enable API access for this session"));
  const checkbox = h("input", { type: "checkbox", onchange: () => { accept.disabled = !checkbox.checked; } });
  return h("div", { class: "api-consent", role: "dialog", "aria-modal": "true", "aria-label": t("Experimental LMU API access") },
    h("h2", {}, t("Experimental LMU API access")),
    h("p", {}, t("These internal, undocumented APIs may change with any LMU patch. Requests can fail or partially change game settings.")),
    h("p", {}, t("The original Factory Seat author is not responsible for developing, maintaining or supporting this fork's API integration. It comes without guarantees; use it at your own risk.")),
    h("p", {}, t("API access can read content ownership and, only when you request assisted launch, change settings and load a weekend. Manual mode and XML results do not require it.")),
    h("label", { class: "api-consent-check" }, checkbox,
      h("span", {}, t("I have read this notice, understand the risks and explicitly consent to using LMU's internal APIs."))),
    h("p", { class: "muted session-help" }, t("Consent lasts until the app is reloaded or closed. You can revoke it in Settings; this does not undo commands already sent.")),
    h("div", { class: "modal-actions" },
      h("button", { class: "btn ghost", type: "button", onclick: onCancel }, t("Stay in manual mode")), accept));
}
