using System;
using SkiaSharp;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

namespace SwiftDotNet.Unity
{
    /// <summary>
    /// Hosts a SwiftDotNet UI inside Unity.
    /// </summary>
    /// <remarks>
    /// <para>The engine is unchanged here — layout, hit-testing, gestures and the paint pass all run in
    /// <c>SwiftDotNet.Graphics</c> exactly as they do on every other backend. This component only supplies
    /// what a host owes the engine: a surface to draw into, a pointer stream, and a repaint signal.</para>
    ///
    /// <para>Drawing goes straight into a <see cref="Texture2D"/>'s own memory. Skia writes to the texture's
    /// raw bytes in place, so a frame costs one <c>Apply</c> upload and no intermediate copy or encode.</para>
    ///
    /// <para>Attach to a GameObject, assign <see cref="Target"/> (a UGUI <c>RawImage</c>) or leave it null
    /// to draw full-screen through <c>OnGUI</c>, and set <see cref="Root"/> from your own code before
    /// <c>Start</c> runs — usually from a subclass.</para>
    /// </remarks>
    [AddComponentMenu("SwiftDotNet/SwiftDotNet View")]
    public class SwiftDotNetView : MonoBehaviour
    {
        [Tooltip("Where the rendered UI is displayed. Leave empty to draw full-screen via OnGUI.")]
        public RawImage Target;

        [Tooltip("Render at the display's pixel density. Turn off to render at 1x and upscale.")]
        public bool UseDisplayScale = true;

        [Tooltip("Match the OS dark appearance.")]
        public bool Dark;

        SkiaBridge _bridge;
        SkiaPointerRouter _router;
        Texture2D _texture;
        SKSurface _surface;
        int _width, _height;
        bool _dirty = true;
        bool _pointerDown;

        /// <summary>
        /// The view to render. Set before <c>Start</c> — typically by overriding <see cref="BuildRoot"/>.
        /// </summary>
        public View Root { get; set; }

        /// <summary>Override to supply the root view. Called once, during <c>Start</c>.</summary>
        protected virtual View BuildRoot() => Root;

        void Start()
        {
            _bridge = new SkiaBridge();
            _router = new SkiaPointerRouter(_bridge);

            // The engine raises this whenever a patch lands, which is the only thing that can change the
            // pixels — so repaint is demand-driven rather than every frame.
            _bridge.Invalidate += () => _dirty = true;

            var root = BuildRoot();
            if (root == null)
            {
                Debug.LogError("SwiftDotNetView: no Root view was set. Assign Root or override BuildRoot().");
                enabled = false;
                return;
            }

            SwiftApp.Run(root, _bridge);
            EnsureSurface();
        }

        void OnDestroy()
        {
            _surface?.Dispose();
            if (_texture != null) Destroy(_texture);
        }

        // ---- surface ---------------------------------------------------------

        void EnsureSurface()
        {
            var scale = UseDisplayScale ? Mathf.Max(1f, Screen.dpi / 96f) : 1f;
            var width = Mathf.Max(1, Mathf.RoundToInt(ViewportWidth * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(ViewportHeight * scale));
            if (_texture != null && width == _width && height == _height) return;

            _surface?.Dispose();
            if (_texture != null) Destroy(_texture);

            _width = width;
            _height = height;

            // Mipmaps off and linear filtering: this is a 1:1 screen-space blit, so anything else only
            // costs memory and softens text.
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            _surface = CreateSurfaceOver(_texture);
            if (Target != null) Target.texture = _texture;
            _dirty = true;
        }

        /// <summary>
        /// Wraps a texture's own storage in an <see cref="SKSurface"/> so Skia composites directly into the
        /// bytes Unity will upload — no staging bitmap, no per-frame copy.
        /// </summary>
        static unsafe SKSurface CreateSurfaceOver(Texture2D texture)
        {
            var raw = texture.GetRawTextureData<byte>();
            var address = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(raw);
            var info = new SKImageInfo(texture.width, texture.height, SKColorType.Rgba8888, SKAlphaType.Premul);
            return SKSurface.Create(info, address, texture.width * 4);
        }

        float ViewportWidth => Target != null ? Target.rectTransform.rect.width : Screen.width;
        float ViewportHeight => Target != null ? Target.rectTransform.rect.height : Screen.height;

        // ---- frame -----------------------------------------------------------

        void Update()
        {
            EnsureSurface();
            PumpInput();

            // Implicit animations advance on the engine's clock and keep the surface dirty while running.
            if (_bridge.Tick(Time.deltaTime)) _dirty = true;

            if (_dirty) Repaint();
        }

        void Repaint()
        {
            _dirty = false;

            var scale = _width / Mathf.Max(1f, ViewportWidth);
            var canvas = _surface.Canvas;

            var restore = canvas.Save();
            canvas.Scale(scale, scale);
            _bridge.Paint(canvas, new SKSize(ViewportWidth, ViewportHeight), Dark);
            canvas.RestoreToCount(restore);

            _surface.Flush();

            // updateMipmaps: false, makeNoLongerReadable: false — the CPU keeps write access, which is the
            // whole point of drawing into the texture's own memory.
            _texture.Apply(false, false);
        }

        void OnGUI()
        {
            if (Target != null || _texture == null) return;
            GUI.DrawTexture(new UnityEngine.Rect(0, 0, Screen.width, Screen.height), _texture);
        }

        // ---- input -----------------------------------------------------------

        /// <summary>
        /// Feeds Unity's pointer state into the engine's gesture recognizer. The router turns a raw
        /// down/move/up stream into taps, long-presses, swipes, drags and scrolls, so every backend agrees
        /// on what a gesture is — a Unity host does not re-derive any of that.
        /// </summary>
        void PumpInput()
        {
            var time = Time.realtimeSinceStartupAsDouble;
            var position = ToCanvas(Input.mousePosition);

            if (Input.GetMouseButtonDown(0))
            {
                _pointerDown = true;
                _router.Down(position, time);
            }
            else if (Input.GetMouseButtonUp(0) && _pointerDown)
            {
                _pointerDown = false;
                _router.Up(position, time);
            }
            else if (_pointerDown)
            {
                _router.Move(position, time);
            }

            // The long-press timer needs a clock tick even when nothing moved.
            _router.Poll(time);

            var wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && _bridge.Scroll(position, -wheel * 40f)) _dirty = true;
        }

        /// <summary>
        /// Converts a Unity screen point to engine coordinates: Unity's origin is bottom-left and the
        /// engine's is top-left, and a RawImage host needs the point mapped into the image's own rect.
        /// </summary>
        SKPoint ToCanvas(Vector3 screenPoint)
        {
            if (Target == null)
                return new SKPoint(screenPoint.x, Screen.height - screenPoint.y);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Target.rectTransform, screenPoint, null, out var local);

            var rect = Target.rectTransform.rect;
            return new SKPoint(local.x - rect.xMin, rect.yMax - local.y);
        }
    }
}
