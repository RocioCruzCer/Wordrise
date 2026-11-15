// Group page: copy the join code and confirm before leaving.
(function () {
  "use strict";
  document.querySelectorAll("[data-copy]").forEach(function (btn) {
    btn.addEventListener("click", function () {
      var code = btn.dataset.copy;
      var text = "Join my Wordrise group with the code " + code + " at " + window.location.origin + "/league";
      if (navigator.share) {
        navigator.share({ title: "Wordrise group", text: text }).catch(function () {});
      } else if (navigator.clipboard) {
        navigator.clipboard.writeText(code).then(function () { btn.textContent = "Copied: " + code; });
      }
    });
  });
  document.querySelectorAll("form[data-confirm]").forEach(function (form) {
    form.addEventListener("submit", function (e) {
      if (!window.confirm(form.dataset.confirm)) { e.preventDefault(); }
    });
  });
})();
