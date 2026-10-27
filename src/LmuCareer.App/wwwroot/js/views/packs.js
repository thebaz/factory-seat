import { h, put, clear } from "../dom.js";
import { call } from "../api.js";
import { t, mark } from "../i18n.js";

// The DLC checklist: which packs the player owns. Used when starting a career and to change a
// career's packs later (bought one since, or ticked one by mistake).

export const PACK_GROUPS = [
  { title: mark("2024 season packs"), ids: ["2024-1", "2024-2", "2024-3", "2024-4", "2024-5"] },
  { title: mark("European Le Mans packs"), ids: ["elms-1", "elms-2", "elms-3"] },
  { title: mark("US track packs"), ids: ["us-1", "us-2", "us-3"] },
];

const ALL = PACK_GROUPS.flatMap((g) => g.ids);

/**
 * The checklist. owned: pack ids ticked. onChange(newOwned) is called with the whole new list on
 * every change; the caller re-renders.
 */
export function packChecklist(cat, owned, onChange, report = null) {
  const allOwned = ALL.every((id) => owned.includes(id));
  const toggle = (id) => onChange(owned.includes(id) ? owned.filter((p) => p !== id) : [...owned, id]);

  const packRow = (id) => {
    const pack = cat.packs.find((p) => p.id === id);
    const contents = [...cat.tracks.filter((track) => track.pack === id).map((track) => track.name), ...cat.cars.filter((c) => c.pack === id).map((c) => c.name)];
    return h("div", { class: "setting clickable", onclick: () => toggle(id) },
      h("div", { class: "label" }, pack.name, h("small", {}, contents.join(" · ") || t("Not released yet"))),
      h("div", { class: "value" }, h("button", { class: "toggle" + (owned.includes(id) ? " on" : ""), "data-sound": "none", tabindex: "-1" })));
  };

  const detection = h("div", { class: "notice info", style: { marginBottom: "18px" } });
  const status = h("div", { role: "status", "aria-live": "polite" });
  if (report) {
    put(status, t("Content detected from LMU. Review the selection before continuing."));
    if (report.unresolvedPacks.length) put(status, h("div", {}, t("Some packs could not be fully verified or are only partially available. Review these manually: {packs}", {
      packs: report.unresolvedPacks.map((id) => cat.packs.find((p) => p.id === id)?.name ?? id).join(", "),
    })));
  }
  const detect = h("button", { class: "btn ghost small", onclick: async () => {
    detect.disabled = true;
    detect.textContent = t("Detecting content…");
    clear(status);
    try {
      const result = await call("detectOwnedContent");
      // Don't update a checklist the player has since closed or replaced.
      if (!detection.isConnected) return;
      const next = [...new Set([...owned.filter((id) => !result.checkedPacks.includes(id)), ...result.ownedPacks])];
      onChange(next, result);
    } catch (error) {
      if (detection.isConnected) put(status, error.phrase ? t(error.phrase) : error.message);
    } finally {
      detect.disabled = false;
      detect.textContent = t("Retry");
    }
  } }, t("Detect owned content"));
  put(detection, h("div", {}, t("Start Le Mans Ultimate and reach the main menu, then detect your cars and tracks. You can also select packs manually.")), detect, status);

  return [
    detection,
    h("div", { class: "row" },
      h("button", { class: "btn ghost small", onclick: () => onChange(allOwned ? [] : [...ALL]) }, allOwned ? t("Clear all") : t("I own everything")),
      h("span", { class: "faint" }, t("A Race Control Pro+ subscription unlocks every pack."))),
    h("div", { class: "rows-title" }, t("Base game"), h("span", { class: "faint", style: { fontSize: "16px" } }, t("Always included"))),
    h("div", { class: "rows" },
      h("div", { class: "setting" },
        h("div", { class: "label" }, "Le Mans Ultimate",
          h("small", {}, cat.tracks.filter((track) => track.pack === "base").map((track) => track.name).join(" · "))),
        h("div", { class: "value" }, h("button", { class: "toggle on", disabled: true })))),
    PACK_GROUPS.map((g) => [h("div", { class: "rows-title" }, t(g.title)), h("div", { class: "rows" }, g.ids.map(packRow))]),
  ];
}
