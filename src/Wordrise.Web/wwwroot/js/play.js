// Play hub: disable the listening challenge when the browser cannot speak.
(function () {
  "use strict";
  if (window.WordriseSpeech && window.WordriseSpeech.isSupported()) { return; }
  document.querySelectorAll("[data-requires-speech]").forEach(function (form) {
    form.querySelectorAll("button, input").forEach(function (el) { el.disabled = true; });
    var note = form.querySelector("[data-speech-unavailable]");
    if (note) { note.hidden = false; }
  });
})();
