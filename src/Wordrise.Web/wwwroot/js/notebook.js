// Speaker buttons in the Notebook of peaks.
(function () {
  "use strict";
  var list = document.querySelector(".notebook-full");
  if (!list) { return; }
  // The Profile audio setting controls auto-play after a round; manual playback is always offered when supported.
  var enabled = window.WordriseSpeech.isSupported();
  list.querySelectorAll("[data-speak]").forEach(function (btn) {
    if (!enabled) { btn.hidden = true; return; }
    btn.addEventListener("click", function () { window.WordriseSpeech.speak(btn.dataset.speak); });
  });
})();
