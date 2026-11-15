// Applies saved theme and text size before first paint; wires the Profile toggles.
(function () {
  "use strict";
  var root = document.documentElement;

  function read(key) {
    try { return localStorage.getItem(key); } catch (e) { return null; }
  }
  function write(key, value) {
    try { localStorage.setItem(key, value); } catch (e) { /* storage unavailable: setting lasts for this page only */ }
  }

  var theme = read("wordrise.theme");
  if (theme === "dark" || theme === "light") { root.dataset.theme = theme; }

  var scale = parseFloat(read("wordrise.fontScale") || "1");
  if (!(scale >= 0.85 && scale <= 1.4)) { scale = 1; }
  root.style.setProperty("--font-scale", String(scale));

  document.addEventListener("DOMContentLoaded", function () {
    var toggle = document.querySelector("[data-theme-toggle]");
    if (toggle) {
      var isDark = root.dataset.theme
        ? root.dataset.theme === "dark"
        : window.matchMedia("(prefers-color-scheme: dark)").matches;
      toggle.checked = isDark;
      toggle.addEventListener("change", function () {
        root.dataset.theme = toggle.checked ? "dark" : "light";
        write("wordrise.theme", root.dataset.theme);
      });
    }

    document.querySelectorAll("[data-font-step]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        scale = Math.min(1.4, Math.max(0.85, Math.round((scale + 0.1 * Number(btn.dataset.fontStep)) * 100) / 100));
        root.style.setProperty("--font-scale", String(scale));
        write("wordrise.fontScale", String(scale));
      });
    });
  });
})();
