// Chat thread: keep the newest message in view on load and when the 5-second poll appends messages
// (only when the reader is already near the bottom). Enter sends, Shift+Enter adds a line.
(function () {
  "use strict";
  let follow = true;
  function list() { return document.querySelector("[data-chat-list]"); }
  function toBottom() { const l = list(); if (l) l.scrollTop = l.scrollHeight; }
  function isPoll(e) { return e.detail && e.detail.elt && e.detail.elt.id === "chat-poll"; }

  document.addEventListener("DOMContentLoaded", toBottom);
  document.addEventListener("htmx:beforeSwap", function (e) {
    const l = list();
    if (l && isPoll(e)) follow = l.scrollHeight - l.scrollTop - l.clientHeight < 120;
  });
  document.addEventListener("htmx:afterSettle", function (e) {
    const l = list();
    if (!l || !isPoll(e)) return;
    if (l.querySelector("[data-message]")) { const empty = l.querySelector("[data-chat-empty]"); if (empty) empty.remove(); }
    if (follow) toBottom();
  });
  document.addEventListener("htmx:afterRequest", function (e) {
    if (e.detail.elt && e.detail.elt.matches && e.detail.elt.matches("[data-chat-form]")) follow = true;
  });
  // Back on a visible page: fetch at once, which also marks what is on screen read (hidden-tab polls do not).
  document.addEventListener("visibilitychange", function () {
    if (document.visibilityState === "visible" && window.htmx && document.getElementById("chat-poll")) window.htmx.trigger(document.body, "chat:refresh");
  });
  document.addEventListener("keydown", function (e) {
    const t = e.target;
    if (e.key !== "Enter" || e.shiftKey || e.isComposing || !(t instanceof HTMLTextAreaElement) || !t.matches("[data-chat-body]")) return;
    e.preventDefault();
    const form = t.closest("form");
    if (form && t.value.trim()) form.requestSubmit();
  });
})();
