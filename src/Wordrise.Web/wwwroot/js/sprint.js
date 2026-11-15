// 60-second anagram sprint. The server owns the word, the clock and the score; this script only
// shows scrambled letters, counts down and sends answers with the challenge number (idempotent).
(function () {
  "use strict";

  var root = document.querySelector(".sprint");
  if (!root) { return; }

  var id = root.dataset.sprintId;
  var csrf = root.querySelector('input[name="__RequestVerificationToken"]').value;
  var state = JSON.parse(document.getElementById("sprint-state").textContent);
  var fx = window.WordriseFx;

  var el = {
    play: document.getElementById("sprint-play"),
    result: document.getElementById("sprint-result"),
    score: document.getElementById("score"),
    time: document.getElementById("time"),
    fill: document.getElementById("timer-fill"),
    pos: document.getElementById("pos"),
    definition: document.getElementById("definition"),
    letters: document.getElementById("letters"),
    form: document.getElementById("answer-form"),
    answer: document.getElementById("answer"),
    skip: document.getElementById("skip"),
    message: document.getElementById("sprint-message"),
    finalScore: document.getElementById("final-score"),
    words: document.getElementById("sprint-words"),
    progress: document.getElementById("sprint-progress"),
    best: document.getElementById("best")
  };

  var DURATION = 60;
  var deadline = Date.now() + state.secondsLeft * 1000;
  var busy = false;
  var timer = null;
  var lastTick = null;

  function message(text, isError) {
    el.message.textContent = text || "";
    el.message.classList.toggle("error", !!isError);
  }

  function renderChallenge() {
    var c = state.challenge;
    el.score.textContent = state.score;
    if (!c) { return; }
    el.pos.textContent = c.partOfSpeech + " · " + c.length + " letters";
    el.definition.textContent = c.definition;
    el.letters.innerHTML = "";
    c.letters.split("").forEach(function (ch) {
      var tile = document.createElement("span");
      tile.className = "tile sprint-tile";
      tile.textContent = ch;
      el.letters.appendChild(tile);
    });
    el.answer.maxLength = c.length;
  }

  function renderResult() {
    clearInterval(timer);
    el.play.hidden = true;
    el.result.hidden = false;
    el.finalScore.textContent = state.score;
    el.best.textContent = state.bestScore;
    el.words.innerHTML = "";
    (state.results || []).forEach(function (w) {
      var li = document.createElement("li");
      li.className = w.solved ? "solved" : "missed";
      var head = document.createElement("strong");
      head.textContent = (w.solved ? "✓ " : "✗ ") + w.word;
      var def = document.createElement("small");
      def.textContent = w.definition + " · ES: " + w.translation;
      li.appendChild(head);
      li.appendChild(def);
      el.words.appendChild(li);
    });
    fx.sound(state.score > 0 ? "win" : "lose");
    if (state.score >= 5) { fx.confetti(); }
    el.result.focus();

    fx.fetchProgress(csrf).then(function (p) {
      if (!p) { return; }
      el.progress.innerHTML = "";
      [["+" + state.xp + " XP", "chip-xp"],
       [p.goal.met ? "Daily goal reached ✓" : "Daily goal " + p.goal.done + "/" + p.goal.target, p.goal.met ? "chip-goal" : ""],
       [p.weeklyXp + " XP this week", ""]].forEach(function (c) {
        var span = document.createElement("span");
        span.className = "chip " + c[1];
        span.textContent = c[0];
        el.progress.appendChild(span);
      });
    });
  }

  function tick() {
    var left = Math.max(0, (deadline - Date.now()) / 1000);
    var whole = Math.ceil(left);
    el.time.textContent = whole;
    el.fill.style.width = (left / DURATION * 100) + "%";
    root.classList.toggle("hurry", left <= 10);
    if (left <= 10 && whole !== lastTick && whole > 0) { fx.sound("tick"); }
    lastTick = whole;
    if (left <= 0) {
      clearInterval(timer);
      send("/finish", null);
    }
  }

  function send(path, body) {
    busy = true;
    return fetch("/api/sprints/" + id + path, {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json", "Accept": "application/json", "X-CSRF-TOKEN": csrf },
      body: body ? JSON.stringify(body) : null
    }).then(function (res) {
      if (res.status === 401) {
        message("Your session expired. Log in again.", true);
        return null;
      }
      return res.ok ? res.json() : null;
    }).then(function (data) {
      if (!data) { return null; }
      var previousChallenge = state.challenge ? state.challenge.number : 0;
      state = data;
      deadline = Date.now() + state.secondsLeft * 1000;
      if (state.finished) {
        renderResult();
      } else if (path === "/finish") {
        // Server clock still has time left (clock skew): keep counting with its value.
        clearInterval(timer);
        timer = setInterval(tick, 200);
      } else if (state.challenge && state.challenge.number !== previousChallenge) {
        el.answer.value = "";
        renderChallenge();
      }
      return data;
    }).catch(function () {
      message("Connection problem. Keep trying.", true);
      return null;
    }).finally(function () {
      busy = false;
      if (!state.finished) { el.answer.focus(); }
    });
  }

  el.form.addEventListener("submit", function (e) {
    e.preventDefault();
    if (busy || state.finished || !state.challenge) { return; }
    var guess = el.answer.value.trim().toLowerCase();
    if (!guess) { return; }
    send("/answer", { challenge: state.challenge.number, guess: guess }).then(function (data) {
      if (!data || data.finished) { return; }
      if (data.lastAnswerCorrect) {
        fx.sound("correct");
        message("Nice!");
        el.score.classList.remove("bump"); void el.score.offsetWidth; el.score.classList.add("bump");
      } else if (data.lastAnswerCorrect === false) {
        fx.sound("wrong");
        message("Not quite. Try again or skip.", true);
        el.letters.classList.remove("shake"); void el.letters.offsetWidth; el.letters.classList.add("shake");
        el.answer.select();
      }
    });
  });

  el.skip.addEventListener("click", function () {
    if (busy || state.finished || !state.challenge) { return; }
    send("/skip", { challenge: state.challenge.number }).then(function () { message(""); });
  });

  // Typing on letters: tap a tile to append it (handy on phones).
  el.letters.addEventListener("click", function (e) {
    var tile = e.target.closest(".sprint-tile");
    if (!tile || state.finished) { return; }
    el.answer.value += tile.textContent.toLowerCase();
    fx.sound("key");
    el.answer.focus();
  });

  if (state.finished) {
    renderResult();
  } else {
    renderChallenge();
    tick();
    timer = setInterval(tick, 200);
    el.answer.focus();
  }
})();
