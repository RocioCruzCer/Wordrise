// Game feedback: synthesized sounds (Web Audio, no audio files), confetti and achievement notices.
// Sounds are off when the page says data-sound="off" (Profile setting); motion respects prefers-reduced-motion.
window.WordriseFx = (function () {
  "use strict";

  var AudioCtx = window.AudioContext || window.webkitAudioContext;
  var ctx = null;
  var reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function soundOn() {
    var el = document.querySelector("[data-sound]");
    return !!AudioCtx && (!el || el.getAttribute("data-sound") !== "off");
  }

  function context() {
    if (!ctx) { ctx = new AudioCtx(); }
    if (ctx.state === "suspended") { ctx.resume(); }
    return ctx;
  }

  // One short tone with a soft attack/decay envelope.
  function tone(freq, start, duration, type, volume) {
    var c = context();
    var osc = c.createOscillator();
    var gain = c.createGain();
    var t0 = c.currentTime + start;
    osc.type = type || "sine";
    osc.frequency.setValueAtTime(freq, t0);
    gain.gain.setValueAtTime(0.0001, t0);
    gain.gain.exponentialRampToValueAtTime(volume || 0.12, t0 + 0.015);
    gain.gain.exponentialRampToValueAtTime(0.0001, t0 + duration);
    osc.connect(gain).connect(c.destination);
    osc.start(t0);
    osc.stop(t0 + duration + 0.02);
  }

  var SOUNDS = {
    key: function () { tone(660, 0, 0.05, "triangle", 0.04); },
    correct: function () { tone(880, 0, 0.12, "sine", 0.1); },
    present: function () { tone(587, 0, 0.12, "sine", 0.08); },
    absent: function () { tone(220, 0, 0.1, "triangle", 0.06); },
    wrong: function () { tone(180, 0, 0.12, "square", 0.05); tone(150, 0.1, 0.14, "square", 0.05); },
    win: function () { [523, 659, 784, 1047].forEach(function (f, i) { tone(f, i * 0.09, 0.25, "sine", 0.12); }); },
    lose: function () { [392, 330, 262].forEach(function (f, i) { tone(f, i * 0.12, 0.3, "triangle", 0.08); }); },
    achievement: function () { [784, 988, 1175, 1568].forEach(function (f, i) { tone(f, i * 0.07, 0.22, "sine", 0.1); }); },
    tick: function () { tone(1200, 0, 0.03, "square", 0.03); },
    combo: function () { tone(988, 0, 0.08, "sine", 0.08); tone(1319, 0.07, 0.12, "sine", 0.08); }
  };

  function sound(name) {
    if (!soundOn() || !SOUNDS[name]) { return; }
    try { SOUNDS[name](); } catch (e) { /* audio blocked until a user gesture: ignore */ }
  }

  // An open modal <dialog> lives in the top layer and would cover anything appended to <body>.
  function layer() {
    return document.querySelector("dialog[open]") || document.body;
  }

  function confetti(amount) {
    if (reducedMotion) { return; }
    var canvas = document.createElement("canvas");
    canvas.className = "confetti";
    canvas.setAttribute("aria-hidden", "true");
    layer().appendChild(canvas);
    var g = canvas.getContext("2d");
    var w = canvas.width = window.innerWidth;
    var h = canvas.height = window.innerHeight;
    var colors = ["#F5A524", "#F26B5B", "#14213D", "#FFF8EE", "#7CC6A4"];
    var pieces = [];
    for (var i = 0; i < (amount || 120); i++) {
      pieces.push({
        x: w / 2 + (Math.random() - 0.5) * w * 0.3, y: h * 0.35,
        vx: (Math.random() - 0.5) * 12, vy: -Math.random() * 12 - 4,
        size: 5 + Math.random() * 6, rot: Math.random() * Math.PI, vr: (Math.random() - 0.5) * 0.3,
        color: colors[i % colors.length]
      });
    }
    var start = performance.now();
    function frame(now) {
      var elapsed = now - start;
      g.clearRect(0, 0, w, h);
      pieces.forEach(function (p) {
        p.vy += 0.35; p.x += p.vx; p.y += p.vy; p.rot += p.vr;
        g.save(); g.translate(p.x, p.y); g.rotate(p.rot);
        g.fillStyle = p.color; g.globalAlpha = Math.max(0, 1 - elapsed / 2200);
        g.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.6);
        g.restore();
      });
      if (elapsed < 2200) { requestAnimationFrame(frame); } else { canvas.remove(); }
    }
    requestAnimationFrame(frame);
  }

  // Stacked notice for each unlocked achievement.
  function achievement(a) {
    var host = document.getElementById("achievement-host");
    if (!host) {
      host = document.createElement("div");
      host.id = "achievement-host";
      host.className = "achievement-host";
      host.setAttribute("role", "status");
      host.setAttribute("aria-live", "polite");
    }
    if (host.parentNode !== layer()) { layer().appendChild(host); }
    var card = document.createElement("div");
    card.className = "achievement-toast";
    var icon = document.createElement("span");
    icon.className = "achievement-icon";
    icon.textContent = a.icon;
    icon.setAttribute("aria-hidden", "true");
    var text = document.createElement("div");
    var title = document.createElement("strong");
    title.textContent = "Achievement unlocked: " + a.title;
    var desc = document.createElement("small");
    desc.textContent = a.description;
    text.appendChild(title);
    text.appendChild(desc);
    card.appendChild(icon);
    card.appendChild(text);
    host.appendChild(card);
    sound("achievement");
    setTimeout(function () { card.classList.add("leaving"); }, 4200);
    setTimeout(function () { card.remove(); }, 4800);
  }

  // Shared "after a round/sprint" update: goal ring, XP, combo and achievements.
  function fetchProgress(csrf) {
    return fetch("/api/me/progress", {
      method: "POST",
      credentials: "same-origin",
      headers: { "Accept": "application/json", "X-CSRF-TOKEN": csrf }
    }).then(function (r) { return r.ok ? r.json() : null; }).then(function (p) {
      if (!p) { return null; }
      (p.newAchievements || []).forEach(function (a, i) {
        setTimeout(function () { achievement(a); }, 600 + i * 900);
      });
      if (p.newAchievements && p.newAchievements.length) { confetti(80); }
      return p;
    }).catch(function () { return null; });
  }

  return { sound: sound, confetti: confetti, achievement: achievement, fetchProgress: fetchProgress };
})();
