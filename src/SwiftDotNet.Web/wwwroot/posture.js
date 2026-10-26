// Window-posture reporter for the Blazor backend. Builds the same `$posture` wire the native shims emit —
// "state;orientation;x;y;w;h;angle;windowW;windowH" in CSS px — and hands it to .NET whenever it changes.
//
// Sources (Chromium only today; elsewhere this reports window size and "none"):
//   • window.viewport.segments — one DOMRect per screen segment; two segments mean a fold divides the
//     viewport, and the gap between them is the fold (zero-width on a seamless fold).
//   • navigator.devicePosture — its change event re-checks the segments. No hinge angle exists on the web.
// The root area is the viewport: the Blazor host renders into the page body.

function segments() {
  const s = window.viewport && window.viewport.segments;
  return Array.isArray(s) ? s : (s ? Array.from(s) : null);
}

function compute() {
  const w = window.innerWidth, h = window.innerHeight;
  const segs = segments();
  let state = "none", orientation = "none", x = 0, y = 0, fw = 0, fh = 0;
  if (segs && segs.length > 1) {
    const a = segs[0], b = segs[1];
    state = "halfOpen";
    if (b.left >= a.right - 0.5) {            // side by side → vertical (book) fold
      orientation = "vertical";
      x = a.right; y = 0; fw = Math.max(0, b.left - a.right); fh = h;
    } else {                                   // stacked → horizontal (tabletop) fold
      orientation = "horizontal";
      x = 0; y = a.bottom; fw = w; fh = Math.max(0, b.top - a.bottom);
    }
  }
  // No segments → "none", even when devicePosture exists: Chromium reports "continuous" on every ordinary
  // laptop and phone, so it can't tell a flat foldable from a slab. "folded" without segments has no
  // geometry to lay out around either. The posture still triggers a re-check through its change event.
  return [state, orientation, x, y, fw, fh, "", w, h].join(";");
}

export function start(dotnet) {
  let last = "";
  const report = () => {
    const payload = compute();
    if (payload === last) return;
    last = payload;
    dotnet.invokeMethodAsync("OnPosture", payload);
  };
  window.addEventListener("resize", report);
  if (window.viewport && window.viewport.addEventListener) window.viewport.addEventListener("segmentschange", report);
  if (navigator.devicePosture) navigator.devicePosture.addEventListener("change", report);
  report();
  return {
    stop() {
      window.removeEventListener("resize", report);
      if (window.viewport && window.viewport.removeEventListener) window.viewport.removeEventListener("segmentschange", report);
      if (navigator.devicePosture) navigator.devicePosture.removeEventListener("change", report);
    },
  };
}
