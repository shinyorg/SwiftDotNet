// SwiftDotNet.Controls.Camera — Android (CameraX + ML Kit) renderer.
//
// The native half of the `CameraView` control on Android. Custom renderers on Android are Kotlin,
// registered via `registerRenderer`, so this is NOT part of the C# NuGet package. Ship it by adding this
// file to the SwiftDotNetComposeBridge sources (rebuilding the AAR) or a companion module the app links,
// and calling `registerSwiftDotNetCameraRenderer()` at startup — exactly like native/maps/MapRenderer.kt.
//
// It reads the props the C# `CameraView` writes ("facing", "flash", "analyzers", "captureToken"), hosts a
// CameraX PreviewView in an AndroidView, runs ML Kit frame analysis, and emits results back over the single
// event channel with the `kind:body` grammar CameraView defines ("photo:<b64>", "barcode:<fmt>:<b64>",
// "faces:<n>", "text:<b64>", "focus:<x>,<y>", "error:<b64>"). Mirrors native/camera/CameraRenderer.swift.
//
// Requires CameraX + ML Kit:
//   implementation("androidx.camera:camera-camera2:1.3.+")
//   implementation("androidx.camera:camera-lifecycle:1.3.+")
//   implementation("androidx.camera:camera-view:1.3.+")
//   implementation("com.google.mlkit:barcode-scanning:17.+")
//   implementation("com.google.mlkit:face-detection:16.+")
//   implementation("com.google.mlkit:text-recognition:16.+")
//
// STATUS: authored for review — not compiled in this environment (needs the CameraX/ML Kit deps + AAR rebuild).

package com.swiftdotnet.bridge

import android.util.Base64
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.face.FaceDetection
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import java.io.ByteArrayOutputStream
import java.util.concurrent.Executors

private fun b64(s: String) = Base64.encodeToString(s.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)

@Composable
fun SwiftDotNetCamera(props: SwiftDotNetProps) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember { PreviewView(context) }
    val executor = remember { Executors.newSingleThreadExecutor() }
    val imageCapture = remember { ImageCapture.Builder().build() }

    val facing = props.string("facing") ?: "back"
    val analyzers = (props.string("analyzers") ?: "").split(",").filter { it.isNotEmpty() }.toSet()
    val captureToken = props.string("captureToken") ?: ""

    // ML Kit detectors (created once for the enabled analyzers).
    val barcodeScanner = remember { BarcodeScanning.getClient() }
    val faceDetector = remember { FaceDetection.getClient() }
    val textRecognizer = remember { TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS) }
    var lastFaceCount = remember { -1 }
    var lastText = remember { "" }

    // Bind the camera + analysis to the lifecycle whenever the facing changes.
    LaunchedEffect(facing) {
        val provider = ProcessCameraProvider.getInstance(context).get()
        val selector = if (facing == "front") CameraSelector.DEFAULT_FRONT_CAMERA else CameraSelector.DEFAULT_BACK_CAMERA
        val preview = Preview.Builder().build().also { it.setSurfaceProvider(previewView.surfaceProvider) }

        val analysis = ImageAnalysis.Builder()
            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
            .build()
        if (analyzers.isNotEmpty()) {
            analysis.setAnalyzer(executor) { proxy: ImageProxy ->
                val media = proxy.image
                if (media == null) { proxy.close(); return@setAnalyzer }
                val input = InputImage.fromMediaImage(media, proxy.imageInfo.rotationDegrees)
                var pending = 0
                fun done() { if (--pending <= 0) proxy.close() }

                if (analyzers.contains("barcode")) {
                    pending++
                    barcodeScanner.process(input).addOnSuccessListener { list ->
                        list.firstOrNull()?.rawValue?.let { v ->
                            props.emit("barcode:${list[0].format}:${b64(v)}")
                        }
                    }.addOnCompleteListener { done() }
                }
                if (analyzers.contains("face")) {
                    pending++
                    faceDetector.process(input).addOnSuccessListener { faces ->
                        if (faces.size != lastFaceCount) { lastFaceCount = faces.size; props.emit("faces:${faces.size}") }
                    }.addOnCompleteListener { done() }
                }
                if (analyzers.contains("text")) {
                    pending++
                    textRecognizer.process(input).addOnSuccessListener { result ->
                        val t = result.text
                        if (t.isNotEmpty() && t != lastText) { lastText = t; props.emit("text:${b64(t)}") }
                    }.addOnCompleteListener { done() }
                }
                if (pending == 0) proxy.close()
            }
        }

        try {
            provider.unbindAll()
            provider.bindToLifecycle(lifecycleOwner, selector, preview, imageCapture, analysis)
        } catch (e: Exception) {
            props.emit("error:${b64(e.message ?: "Camera unavailable")}")
        }
    }

    // Capture a still whenever the C# captureToken changes.
    LaunchedEffect(captureToken) {
        if (captureToken.isEmpty() || captureToken == "0") return@LaunchedEffect
        imageCapture.takePicture(executor, object : ImageCapture.OnImageCapturedCallback() {
            override fun onCaptureSuccess(image: ImageProxy) {
                val buffer = image.planes[0].buffer
                val bytes = ByteArray(buffer.remaining()).also { buffer.get(it) }
                props.emit("photo:${Base64.encodeToString(bytes, Base64.NO_WRAP)}")
                image.close()
            }
            override fun onError(exc: ImageCaptureException) {
                props.emit("error:${b64(exc.message ?: "Capture failed")}")
            }
        })
    }

    AndroidView(
        factory = {
            previewView.setOnTouchListener { v, ev ->
                if (ev.action == android.view.MotionEvent.ACTION_UP) {
                    props.emit("focus:${ev.x / v.width.coerceAtLeast(1)},${ev.y / v.height.coerceAtLeast(1)}")
                }
                v.performClick(); true
            }
            previewView
        },
        modifier = Modifier.fillMaxSize(),
    )
}

/** Call once at app startup (after the bridge is initialized) to render `CameraView` nodes via CameraX. */
fun registerSwiftDotNetCameraRenderer() {
    registerRenderer("CameraView") { props -> SwiftDotNetCamera(props) }
}
