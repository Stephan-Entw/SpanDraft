(() => {
  function initializeUnitTables() {
    document.querySelectorAll("[data-collapsible-units]").forEach((component) => {
      const toggle = component.querySelector(".spandraft-unit-toggle");
      const label = toggle?.querySelector(".spandraft-unit-toggle-label");
      const icon = toggle?.querySelector(".spandraft-unit-toggle-icon");
      const rows = component.querySelectorAll("tr.spandraft-unit-extra");
      if (!(toggle instanceof HTMLButtonElement) || !label || !icon || !rows.length) return;

      function setExpanded(expanded) {
        rows.forEach((row) => { row.hidden = !expanded; });
        toggle.setAttribute("aria-expanded", String(expanded));
        label.textContent = expanded ? "Weniger anzeigen" : "Weitere Einheiten anzeigen";
        icon.textContent = expanded ? "⌃" : "⌄";
      }

      toggle.addEventListener("click", () => {
        setExpanded(toggle.getAttribute("aria-expanded") !== "true");
      });
      setExpanded(false);
      toggle.hidden = false;
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeUnitTables, { once: true });
  } else {
    initializeUnitTables();
  }
})();
