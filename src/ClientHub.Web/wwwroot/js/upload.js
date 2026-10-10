// Presigned upload: start (server checks access, builds the key) → PUT straight to storage with progress → complete.
// File bytes never pass through the app (R-061). Used by every [data-upload] block (_FileUpload partial).
(function () {
  "use strict";

  function show(el, text) { if (!el) return; if (text !== undefined) el.textContent = text; el.classList.remove("hidden"); }
  function hide(el) { if (el) el.classList.add("hidden"); }

  async function post(url, fields) {
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams(fields),
      credentials: "same-origin"
    });
    let data = null;
    try { data = await res.json(); } catch (_) { /* 404 page or empty body */ }
    if (!res.ok) throw new Error((data && data.error) || "");
    return data;
  }

  function put(url, file, contentType, onProgress) {
    return new Promise(function (resolve, reject) {
      const xhr = new XMLHttpRequest();
      xhr.open("PUT", url);
      xhr.setRequestHeader("Content-Type", contentType);
      xhr.upload.onprogress = function (e) { if (e.lengthComputable) onProgress(e.loaded / e.total); };
      xhr.onload = function () { xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error("")); };
      xhr.onerror = function () { reject(new Error("")); };
      xhr.send(file);
    });
  }

  async function handle(box, input) {
    const file = input.files && input.files[0];
    if (!file) return;
    const d = box.dataset;
    const err = box.querySelector("[data-upload-error]");
    const status = box.querySelector("[data-upload-status]");
    const bar = box.querySelector("[data-upload-bar]");
    const progress = box.querySelector("[data-upload-progress]");
    hide(err);

    const ext = (file.name.lastIndexOf(".") >= 0 ? file.name.slice(file.name.lastIndexOf(".")) : "").toLowerCase();
    if (!(d.uploadExt || "").split(",").includes(ext)) { show(err, d.msgType); input.value = ""; return; }
    if (file.size > Number(d.uploadMax)) { show(err, d.msgSize); input.value = ""; return; }

    const fields = { fileName: file.name, size: String(file.size) };
    box.querySelectorAll("[data-upload-field]").forEach(function (f) { fields[f.name] = f.value; });
    const token = box.querySelector("input[name=__RequestVerificationToken]");
    if (token) fields.__RequestVerificationToken = token.value;

    input.disabled = true;
    show(status, d.msgUploading + " " + file.name);
    show(progress);
    bar.style.width = "0%";
    try {
      const start = await post(d.uploadStart, fields);
      await put(start.url, file, start.contentType, function (p) { bar.style.width = Math.round(p * 100) + "%"; });
      const captionBox = d.uploadCaption ? document.querySelector(d.uploadCaption) : null;
      const complete = { ticket: start.ticket, __RequestVerificationToken: fields.__RequestVerificationToken };
      if (captionBox) complete.caption = captionBox.value;
      await post(d.uploadComplete, complete);
      if (captionBox) captionBox.value = "";
      if (d.uploadEvent && window.htmx) {
        // Chat: the new file message arrives through the poller instead of a page reload.
        window.htmx.trigger(document.body, d.uploadEvent);
        hide(progress); hide(status);
        input.disabled = false;
        input.value = "";
        return;
      }
      window.location.reload();
    } catch (e) {
      hide(progress); hide(status);
      show(err, (e && e.message) || d.msgFailed);
      input.disabled = false;
      input.value = "";
    }
  }

  document.addEventListener("change", function (e) {
    const input = e.target;
    if (!(input instanceof HTMLInputElement) || input.type !== "file") return;
    const box = input.closest("[data-upload]");
    if (box) handle(box, input);
  });
})();
