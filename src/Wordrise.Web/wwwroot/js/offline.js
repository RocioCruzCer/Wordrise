// Offline fallback page: retry manually or automatically when the connection returns.
(function () {
  "use strict";
  function retry() { window.location.reload(); }
  document.getElementById("retry").addEventListener("click", retry);
  window.addEventListener("online", retry);
})();
