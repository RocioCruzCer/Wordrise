// Round screen: renders server state, collects guesses and talks to /api/rounds.
// The server is the source of truth: it validates every guess and only sends the answer when the round ends.
(function () {
  "use strict";

  var container = document.querySelector(".round");
  if (!container) { return; }

  var roundId = container.dataset.roundId;
  var autoAudio = container.dataset.audio === "on";
  var csrf = container.querySelector('input[name="__RequestVerificationToken"]').value;
  var state = JSON.parse(document.getElementById("round-state").textContent);

  var board = document.getElementById("board");
  var keyboardEl = document.getElementById("keyboard");
  var hintsEl = document.getElementById("hints");
  var hintsCount = document.getElementById("hints-count");
  var hintBtn = document.getElementById("use-hint");
  var emojiBtn = document.getElementById("emoji-hint");
  var emojiBox = document.getElementById("emoji-box");
  var progressEl = document.getElementById("result-progress");
  var fx = window.WordriseFx || { sound: function () {}, confetti: function () {}, fetchProgress: function () { return Promise.resolve(null); } };
  var messageEl = document.getElementById("message");
  var attemptBadge = document.getElementById("attempt-badge");
  var resultEl = document.getElementById("result");
  var giveUpBtn = document.getElementById("give-up");
  var listenPanel = document.getElementById("listen-panel");

  var STATE_CLASS = { Correct: "correct", Present: "present", Absent: "absent" };
  var STATE_ICON = { Correct: "✓", Present: "•", Absent: "×" };
  var STATE_LABEL = { Correct: "right spot", Present: "in the word", Absent: "not in word" };
  var CHEERS = ["Nice climb!", "One step higher!", "You reached the peak!", "Great rise!"];
  var ROWS = [["q", "w", "e", "r", "t", "y", "u", "i", "o", "p"], ["a", "s", "d", "f", "g", "h", "j", "k", "l"], ["enter", "z", "x", "c", "v", "b", "n", "m", "del"]];

  var current = "";
  var busy = false;
  var justFinished = false;
  var revealRow = -1; // row that animates after a successful guess

  // ---------- Rendering ----------

  function render() {
    renderTraining();
    renderBadge();
    renderBoard();
    renderHints();
    renderKeyboard();
    renderResult();
    revealRow = -1;
  }

  function renderTraining() {
    var playing = state.status === "InProgress";
    if (giveUpBtn) { giveUpBtn.hidden = !playing; giveUpBtn.disabled = busy; }
    if (listenPanel) { listenPanel.hidden = !playing; }
  }

  function renderBadge() {
    var shown = state.status === "InProgress" ? Math.min(state.attemptsUsed + 1, state.maxAttempts) : state.attemptsUsed;
    attemptBadge.textContent = "Attempt " + shown + "/" + state.maxAttempts;
  }

  function renderBoard() {
    board.style.setProperty("--cols", state.wordLength);
    board.innerHTML = "";
    for (var r = 0; r < state.maxAttempts; r++) {
      var row = document.createElement("div");
      row.className = "board-row";
      row.setAttribute("role", "row");
      var attempt = state.attempts[r];
      var isCurrent = !attempt && r === state.attemptsUsed && state.status === "InProgress";
      var rowLabel = [];

      for (var c = 0; c < state.wordLength; c++) {
        var tile = document.createElement("div");
        tile.className = "tile";
        tile.setAttribute("role", "gridcell");
        var letter = "";
        if (attempt) {
          letter = attempt.guess[c];
          var s = attempt.states[c];
          tile.classList.add(STATE_CLASS[s]);
          tile.dataset.icon = STATE_ICON[s];
          tile.style.setProperty("--delay", (c * 70) + "ms");
          if (r === revealRow) { tile.classList.add("reveal"); }
          tile.setAttribute("aria-label", letter.toUpperCase() + ", " + STATE_LABEL[s]);
          rowLabel.push(letter.toUpperCase() + " " + STATE_LABEL[s]);
        } else if (isCurrent && current[c]) {
          letter = current[c];
          tile.classList.add("filled");
          tile.setAttribute("aria-label", letter.toUpperCase());
        } else {
          tile.setAttribute("aria-label", "empty");
        }
        tile.textContent = letter ? letter.toUpperCase() : "";
        row.appendChild(tile);
      }
      if (rowLabel.length) { row.setAttribute("aria-label", "Attempt " + (r + 1) + ": " + rowLabel.join(", ")); }
      board.appendChild(row);
    }
  }

  function renderHints() {
    hintsCount.textContent = state.hintsUnlocked + " of " + state.totalHints + " unlocked";
    hintsEl.innerHTML = "";
    state.hints.forEach(function (h) {
      var li = document.createElement("li");
      li.className = h.value === null ? "locked" : "unlocked";
      var label = document.createElement("strong");
      label.textContent = h.label + (h.value === null ? "" : ": ");
      li.appendChild(label);
      if (h.value === null) {
        var lock = document.createElement("span");
        lock.className = "muted";
        lock.textContent = " (attempt " + h.unlocksAtAttempt + ")";
        li.appendChild(lock);
      } else {
        li.appendChild(document.createTextNode(h.value));
      }
      hintsEl.appendChild(li);
    });
    var finished = state.status !== "InProgress";
    hintBtn.hidden = finished;
    hintBtn.disabled = busy || state.hintsUnlocked >= state.totalHints;

    if (emojiBox) {
      emojiBox.hidden = !state.emoji;
      emojiBox.textContent = state.emoji || "";
    }
    if (emojiBtn) {
      emojiBtn.hidden = finished || !state.emojiAvailable || state.emojiHintUsed;
      emojiBtn.disabled = busy;
    }
  }

  function renderKeyboard() {
    var best = {};
    var rank = { Absent: 0, Present: 1, Correct: 2 };
    state.attempts.forEach(function (a) {
      for (var i = 0; i < a.guess.length; i++) {
        var l = a.guess[i], s = a.states[i];
        if (best[l] === undefined || rank[s] > rank[best[l]]) { best[l] = s; }
      }
    });

    keyboardEl.innerHTML = "";
    ROWS.forEach(function (keys) {
      var row = document.createElement("div");
      row.className = "kb-row";
      keys.forEach(function (k) {
        var b = document.createElement("button");
        b.type = "button";
        b.className = "key";
        b.dataset.key = k;
        if (k === "enter") { b.textContent = "Enter"; b.classList.add("wide"); b.setAttribute("aria-label", "Submit guess"); }
        else if (k === "del") { b.textContent = "Del"; b.classList.add("wide"); b.setAttribute("aria-label", "Delete letter"); }
        else {
          b.textContent = k.toUpperCase();
          if (best[k]) {
            b.classList.add(STATE_CLASS[best[k]]);
            b.setAttribute("aria-label", k.toUpperCase() + ", " + STATE_LABEL[best[k]]);
          }
        }
        b.disabled = state.status !== "InProgress";
        row.appendChild(b);
      });
      keyboardEl.appendChild(row);
    });
  }

  function renderResult() {
    if (!state.result) { resultEl.hidden = true; return; }
    var r = state.result;
    resultEl.hidden = false;
    document.getElementById("result-kicker").textContent = r.won
      ? CHEERS[state.attemptsUsed % CHEERS.length]
      : "Out of attempts. This word will come back for review.";
    document.getElementById("result-title").textContent = r.word;
    document.getElementById("result-pos").textContent = r.partOfSpeech + " · " + r.level;
    document.getElementById("result-definition").textContent = r.definition;
    document.getElementById("result-example").textContent = r.example;
    document.getElementById("result-translation").textContent = r.translation;
    document.getElementById("result-score").textContent = r.score + " pts";

    var speakBtn = document.getElementById("speak");
    var unavailable = document.getElementById("speech-unavailable");
    if (window.WordriseSpeech.isSupported()) {
      speakBtn.hidden = false;
      unavailable.hidden = true;
      speakBtn.onclick = function () { window.WordriseSpeech.speak(r.word + ". " + r.example); };
    } else {
      speakBtn.hidden = true;           // AC-02.3
      unavailable.hidden = false;
    }

    setupShare();
    startCountdown();

    if (justFinished) {
      fx.sound(r.won ? "win" : "lose");
      if (r.won) { celebrate(); fx.confetti(); }
      loadProgress();
      if (autoAudio) { window.WordriseSpeech.speak(r.word); }
      resultEl.focus();
      justFinished = false;
    }
  }

  // Goal ring, XP, combo and achievements after the round ends.
  function loadProgress() {
    fx.fetchProgress(csrf).then(function (p) {
      if (!p || !progressEl) { return; }
      progressEl.innerHTML = "";
      function chip(text, cls) {
        var span = document.createElement("span");
        span.className = "chip" + (cls ? " " + cls : "");
        span.textContent = text;
        progressEl.appendChild(span);
      }
      if (state.xp > 0) { chip("+" + state.xp + " XP", "chip-xp"); }
      if (p.combo >= 2 && (state.mode === "Practice" || state.mode === "Listening")) {
        chip("Combo x" + p.combo, "chip-combo");
        fx.sound("combo");
      }
      chip(p.goal.met ? "Daily goal reached ✓" : "Daily goal " + p.goal.done + "/" + p.goal.target, p.goal.met ? "chip-goal" : "");
      chip(p.weeklyXp + " XP this week");
      progressEl.hidden = false;
    });
  }

  function celebrate() {
    document.body.classList.remove("sunrise");
    void document.body.offsetWidth; // restart the animation
    document.body.classList.add("sunrise");
  }

  function showMessage(text, isError) {
    messageEl.textContent = text || "";
    messageEl.classList.toggle("error", !!isError);
    if (isError) {
      fx.sound("wrong");
      if (navigator.vibrate) { navigator.vibrate(80); }
      var row = board.children[state.attemptsUsed];
      if (row) { row.classList.remove("shake"); void row.offsetWidth; row.classList.add("shake"); }
    }
  }

  // ---------- Share & countdown ----------

  var SHARE_EMOJI = { Correct: "🟧", Present: "🟨", Absent: "⬛" };

  // Spoiler-free summary: only the colors, never the letters.
  function shareText() {
    var score = state.status === "Won" ? state.attemptsUsed : "X";
    var lines = state.attempts.map(function (a) {
      return a.states.map(function (st) { return SHARE_EMOJI[st]; }).join("");
    });
    return "Wordrise " + state.playedOn + " · " + state.level + " · " + score + "/" + state.maxAttempts + "\n" +
      lines.join("\n") + "\n" + window.location.origin;
  }

  function setupShare() {
    var btn = document.getElementById("share");
    if (!btn || btn.dataset.ready) { return; }
    btn.dataset.ready = "1";
    btn.addEventListener("click", function () {
      var text = shareText();
      if (navigator.share) {
        navigator.share({ title: "Wordrise", text: text }).catch(function () { /* user cancelled */ });
      } else if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(
          function () { showMessage("Result copied. Paste it anywhere!"); },
          function () { showMessage("Could not copy the result.", true); });
      } else {
        showMessage("Sharing is not available in this browser.", true);
      }
    });
  }

  var countdownTimer = null;
  function startCountdown() {
    var el = document.getElementById("countdown-time");
    if (!el || countdownTimer) { return; }
    var deadline = Date.now() + Number(container.dataset.nextWordSeconds || 0) * 1000;
    function pad(n) { return (n < 10 ? "0" : "") + n; }
    function tick() {
      var left = Math.max(0, Math.round((deadline - Date.now()) / 1000));
      el.textContent = pad(Math.floor(left / 3600)) + ":" + pad(Math.floor(left % 3600 / 60)) + ":" + pad(left % 60);
      if (left === 0) {
        clearInterval(countdownTimer);
        document.getElementById("countdown").textContent = "A new word is ready. Go back to Home to play it.";
      }
    }
    tick();
    countdownTimer = setInterval(tick, 1000);
  }

  // ---------- Input ----------

  function press(key) {
    if (busy || state.status !== "InProgress") { return; }
    if (key === "enter") { submit(); return; }
    if (key === "del" || key === "backspace") { current = current.slice(0, -1); renderBoard(); return; }
    if (/^[a-z]$/.test(key) && current.length < state.wordLength) {
      fx.sound("key");
      current += key;
      showMessage("");
      renderBoard();
    }
  }

  keyboardEl.addEventListener("click", function (e) {
    var btn = e.target.closest("button[data-key]");
    if (btn) { press(btn.dataset.key); }
  });

  document.addEventListener("keydown", function (e) {
    if (e.ctrlKey || e.metaKey || e.altKey) { return; }
    var tag = (e.target.tagName || "").toLowerCase();
    if (tag === "input" || tag === "textarea") { return; }
    var k = e.key.toLowerCase();
    if (k === "enter") {
      // Let Enter activate focused buttons (hint, speaker, links) instead of submitting the guess.
      if (tag === "button" || tag === "a") { return; }
      e.preventDefault();
      var nextBtn = document.getElementById("next-word");
      if (state.status !== "InProgress" && nextBtn) { nextBtn.click(); return; }
      press("enter");
    } else if (k === "backspace") { e.preventDefault(); press("del"); }
    else if (/^[a-z]$/.test(k)) { press(k); }
  });

  if (emojiBtn) {
    emojiBtn.addEventListener("click", function () {
      if (busy || state.status !== "InProgress") { return; }
      call("/api/rounds/" + roundId + "/emoji-hint", null).then(function (ok) {
        if (ok) { showMessage("Here is a picture to help you."); }
      });
    });
  }

    hintBtn.addEventListener("click", function () {
    if (busy || state.status !== "InProgress") { return; }
    call("/api/rounds/" + roundId + "/hints", null).then(function (ok) {
      if (ok) { showMessage("A rope to help you climb!"); }
    });
  });

  // ---------- Training: listening & skip ----------

  if (listenPanel) {
    if (!window.WordriseSpeech.isSupported()) {
      listenPanel.innerHTML = "";
      var note = document.createElement("p");
      note.className = "message error";
      note.textContent = "Your browser cannot play audio. Use the hints, or skip this word.";
      listenPanel.appendChild(note);
    } else {
      document.getElementById("listen").addEventListener("click", function () {
        window.WordriseSpeech.speak(state.spokenWord, 0.85);
      });
      document.getElementById("listen-slow").addEventListener("click", function () {
        window.WordriseSpeech.speak(state.spokenWord, 0.5);
      });
    }
  }

  if (giveUpBtn) {
    giveUpBtn.addEventListener("click", function () {
      if (busy || state.status !== "InProgress") { return; }
      if (!window.confirm("Skip this word? It will be revealed and added to your review list.")) { return; }
      call("/api/rounds/" + roundId + "/give-up", null).then(function (ok) {
        if (!ok) { return; }
        current = "";
        showMessage("Word revealed. It will come back in your review list.");
        resultEl.focus();
      });
    });
  }

  // ---------- Server ----------

  function submit() {
    // Client-side checks mirror the server (length and letters); the server re-validates everything.
    if (current.length !== state.wordLength) {
      showMessage("Type " + state.wordLength + " letters.", true);
      return;
    }
    var before = state.status;
    call("/api/rounds/" + roundId + "/guesses", { guess: current }).then(function (ok) {
      if (!ok) { return; }
      current = "";
      revealRow = state.attempts.length - 1;
      playRowSounds(state.attempts[revealRow]);
      if (before === "InProgress" && state.status !== "InProgress") {
        justFinished = true;
        showMessage(state.status === "Won" ? "Nice climb!" : "");
      } else {
        showMessage("");
      }
      render();
    });
  }

  // One soft note per letter, in sync with the flip animation.
  function playRowSounds(attempt) {
    if (!attempt) { return; }
    attempt.states.forEach(function (st, i) {
      setTimeout(function () { fx.sound(st === "Correct" ? "correct" : st === "Present" ? "present" : "absent"); }, i * 70);
    });
  }

  function call(url, body) {
    busy = true;
    hintBtn.disabled = true;
    return fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json", "Accept": "application/json", "X-CSRF-TOKEN": csrf },
      body: body ? JSON.stringify(body) : null
    }).then(function (res) {
      if (res.status === 401) {
        showMessage("Your session expired. Log in again to keep playing.", true);
        setTimeout(function () { window.location.href = "/account/login?returnUrl=" + encodeURIComponent(window.location.pathname); }, 1500);
        return false;
      }
      if (res.status === 429) { showMessage("Too many tries in a short time. Wait a moment.", true); return false; }
      return res.json().then(function (data) {
        if (!res.ok) {
          showMessage((data && data.detail) || "Something went wrong. Try again.", true);
          if (res.status === 409) { return reload().then(function () { return false; }); }
          return false;
        }
        state = data;
        if (!body) { render(); }
        return true;
      }, function () {
        showMessage("Unexpected response from the server. Try again.", true);
        return false;
      });
    }).catch(function () {
      showMessage("Connection problem. Check your internet and try again.", true);
      return false;
    }).finally(function () {
      busy = false;
      renderHints();
      renderTraining();
    });
  }

  function reload() {
    return fetch("/api/rounds/" + roundId, { credentials: "same-origin", headers: { "Accept": "application/json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) { if (data) { state = data; current = ""; render(); } })
      .catch(function () { /* keep current state; message already shown */ });
  }

  render();
})();
