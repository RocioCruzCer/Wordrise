// PWA glue: service worker registration, install prompt and update notice.
(function () {
  "use strict";

  var deferredPrompt = null;
  var isStandalone = window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
  var isIos = /iphone|ipad|ipod/i.test(window.navigator.userAgent);

  function installButtons() { return document.querySelectorAll("[data-install]"); }

  function setInstallVisible(visible) {
    installButtons().forEach(function (btn) { btn.hidden = !visible; });
  }

  function showToast(message, actionLabel, onAction) {
    var toast = document.getElementById("toast");
    if (!toast) { return; }
    toast.innerHTML = "";
    var text = document.createElement("span");
    text.textContent = message;
    toast.appendChild(text);
    if (actionLabel) {
      var btn = document.createElement("button");
      btn.type = "button";
      btn.className = "btn btn-accent btn-sm";
      btn.textContent = actionLabel;
      btn.addEventListener("click", onAction);
      toast.appendChild(btn);
    }
    toast.hidden = false;
  }

  // ---------- Install ----------
  window.addEventListener("beforeinstallprompt", function (e) {
    e.preventDefault(); // show our own button instead of the mini-infobar
    deferredPrompt = e;
    setInstallVisible(true);
  });

  window.addEventListener("appinstalled", function () {
    deferredPrompt = null;
    setInstallVisible(false);
    showToast("Wordrise was installed. Find it on your home screen.");
  });

  document.addEventListener("click", function (e) {
    var btn = e.target.closest("[data-install]");
    if (!btn || !deferredPrompt) { return; }
    deferredPrompt.prompt();
    deferredPrompt.userChoice.finally(function () {
      deferredPrompt = null;
      setInstallVisible(false);
    });
  });

  document.addEventListener("DOMContentLoaded", function () {
    setInstallVisible(false);
    // iOS Safari has no install prompt: explain the manual steps.
    var iosHint = document.getElementById("ios-install-hint");
    if (iosHint) { iosHint.hidden = !(isIos && !isStandalone); }
    var installed = document.getElementById("installed-hint");
    if (installed) { installed.hidden = !isStandalone; }
  });

  // ---------- Service worker ----------
  if (!("serviceWorker" in navigator)) { return; }

  var reloading = false;
  navigator.serviceWorker.addEventListener("controllerchange", function () {
    if (reloading) { return; }
    reloading = true;
    window.location.reload();
  });

  function offerUpdate(worker) {
    showToast("A new version of Wordrise is ready.", "Update", function () {
      worker.postMessage({ type: "SKIP_WAITING" });
    });
  }

  window.addEventListener("load", function () {
    navigator.serviceWorker.register("/sw.js", { scope: "/" }).then(function (reg) {
      if (reg.waiting && navigator.serviceWorker.controller) { offerUpdate(reg.waiting); }
      reg.addEventListener("updatefound", function () {
        var worker = reg.installing;
        if (!worker) { return; }
        worker.addEventListener("statechange", function () {
          // "installed" with an existing controller means this is an update, not the first install.
          if (worker.state === "installed" && navigator.serviceWorker.controller) { offerUpdate(worker); }
        });
      });
    }).catch(function () { /* PWA features unavailable; the site keeps working online */ });
  });
})();
