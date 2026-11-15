// Pronunciation through the browser's Web Speech API (SpeechSynthesis). No external service.
window.WordriseSpeech = (function () {
  "use strict";
  var synth = window.speechSynthesis;
  var supported = !!(synth && window.SpeechSynthesisUtterance);
  var voice = null;

  function pickVoice() {
    if (!supported) { return; }
    var voices = synth.getVoices();
    voice = voices.find(function (v) { return /^en(-|_)US/i.test(v.lang); })
      || voices.find(function (v) { return /^en/i.test(v.lang); })
      || null;
  }

  if (supported) {
    pickVoice();
    if (typeof synth.addEventListener === "function") {
      synth.addEventListener("voiceschanged", pickVoice);
    }
  }

  function speak(text, rate) {
    if (!supported || !text) { return false; }
    synth.cancel(); // allows repeating as many times as the user wants (AC-02.2)
    var utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = voice ? voice.lang : "en-US";
    if (voice) { utterance.voice = voice; }
    utterance.rate = rate || 0.9;
    synth.speak(utterance);
    return true;
  }

  return { isSupported: function () { return supported; }, speak: speak };
})();
