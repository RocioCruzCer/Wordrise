// Show/hide password buttons on the Log in / Sign up screen.
(function () {
  "use strict";
  document.querySelectorAll("[data-toggle-password]").forEach(function (btn) {
    var input = btn.parentElement.querySelector("input");
    btn.setAttribute("aria-pressed", "false");
    btn.addEventListener("click", function () {
      var show = input.type === "password";
      input.type = show ? "text" : "password";
      btn.textContent = show ? "Hide" : "Show";
      btn.setAttribute("aria-pressed", String(show));
    });
  });
})();
