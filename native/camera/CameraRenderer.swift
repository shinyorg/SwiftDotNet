// SwiftDotNet.Controls.Camera — Apple (AVFoundation + Vision) renderer.
//
// This is the native half of the `CameraView` control on iOS (and Mac Catalyst). It is NOT part of the C#
// NuGet package (custom renderers on Apple are Swift, registered via `swiftDotNetRegisterRenderer`). Ship it
// by adding this file to the app's Swift bridge sources (or a companion Swift package linked into the app)
// and calling `registerSwiftDotNetCameraRenderer()` at startup, alongside the SwiftDotNetBridge — exactly
// like native/maps/MapRenderer.swift.
//
// It reads the props the C# `CameraView` writes ("facing", "flash", "analyzers", "captureToken"), hosts a
// live AVCaptureVideoPreviewLayer, runs Vision requests (barcode/face/text) on the video frames, and emits
// results back over the single event channel with the `kind:body` grammar CameraView defines
// ("photo:<b64>", "barcode:<fmt>:<b64>", "faces:<n>", "text:<b64>", "focus:<x>,<y>", "error:<b64>").
//
// STATUS: authored for review — not compiled in this environment (needs the Apple SDK + the bridge build).
// The AVFoundation/Vision usage is type-checked standalone against the iOS SDK via a bridge-symbol stub.

#if canImport(UIKit) && canImport(AVFoundation)
import SwiftUI
import AVFoundation
import Vision
import SwiftDotNetBridge   // companion module: swiftDotNetRegisterRenderer + SwiftDotNetProps + emitEvent

// MARK: - The capture controller

@MainActor
final class CameraController: NSObject, ObservableObject,
    AVCaptureVideoDataOutputSampleBufferDelegate, AVCapturePhotoCaptureDelegate {

    // AVCaptureSession isn't Sendable; the unsafe opt-out lets the capture queue start/stop it (the standard
    // AVFoundation pattern — the session is only mutated on the main actor or the dedicated queue).
    nonisolated(unsafe) let session = AVCaptureSession()
    private let photoOutput = AVCapturePhotoOutput()
    private let videoOutput = AVCaptureVideoDataOutput()
    private let queue = DispatchQueue(label: "swiftdotnet.camera")
    private var deviceInput: AVCaptureDeviceInput?

    var nodeId: String = ""
    var analyzers: Set<String> = []
    private var lastFaceCount = -1
    private var lastText = ""
    private var busy = false

    func configure(facing: String, analyzers: Set<String>) {
        self.analyzers = analyzers
        session.beginConfiguration()
        session.sessionPreset = .high

        // (Re)bind the camera input for the requested facing.
        if let existing = deviceInput { session.removeInput(existing) }
        let position: AVCaptureDevice.Position = facing == "front" ? .front : .back
        if let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: position),
           let input = try? AVCaptureDeviceInput(device: device),
           session.canAddInput(input) {
            session.addInput(input)
            deviceInput = input
        } else {
            emitEvent(nodeId, "error:" + b64("Camera unavailable"))
        }

        if session.canAddOutput(photoOutput) { session.addOutput(photoOutput) }
        if !analyzers.isEmpty, session.canAddOutput(videoOutput) {
            videoOutput.setSampleBufferDelegate(self, queue: queue)
            session.addOutput(videoOutput)
        }
        session.commitConfiguration()
        if !session.isRunning { queue.async { self.session.startRunning() } }
    }

    func capture() {
        let settings = AVCapturePhotoSettings()
        photoOutput.capturePhoto(with: settings, delegate: self)
    }

    // Photo captured → emit the JPEG bytes.
    nonisolated func photoOutput(_ output: AVCapturePhotoOutput,
                                 didFinishProcessingPhoto photo: AVCapturePhoto, error: Error?) {
        guard let data = photo.fileDataRepresentation() else { return }
        Task { @MainActor in emitEvent(self.nodeId, "photo:" + data.base64EncodedString()) }
    }

    // Each video frame → run the enabled Vision requests.
    nonisolated func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                                   from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        Task { @MainActor in self.analyze(pixelBuffer) }
    }

    private func analyze(_ pixelBuffer: CVPixelBuffer) {
        if busy { return }
        busy = true
        let handler = VNImageRequestHandler(cvPixelBuffer: pixelBuffer, options: [:])
        var requests: [VNRequest] = []

        if analyzers.contains("barcode") {
            requests.append(VNDetectBarcodesRequest { [weak self] req, _ in
                guard let self, let r = (req.results as? [VNBarcodeObservation])?.first,
                      let payload = r.payloadStringValue else { return }
                let fmt = r.symbology.rawValue
                Task { @MainActor in emitEvent(self.nodeId, "barcode:\(fmt):" + self.b64(payload)) }
            })
        }
        if analyzers.contains("face") {
            requests.append(VNDetectFaceRectanglesRequest { [weak self] req, _ in
                guard let self else { return }
                let count = (req.results as? [VNFaceObservation])?.count ?? 0
                if count != self.lastFaceCount {
                    self.lastFaceCount = count
                    Task { @MainActor in emitEvent(self.nodeId, "faces:\(count)") }
                }
            })
        }
        if analyzers.contains("text") {
            let textReq = VNRecognizeTextRequest { [weak self] req, _ in
                guard let self else { return }
                let text = (req.results as? [VNRecognizedTextObservation])?
                    .compactMap { $0.topCandidates(1).first?.string }
                    .joined(separator: "\n") ?? ""
                if !text.isEmpty, text != self.lastText {
                    self.lastText = text
                    Task { @MainActor in emitEvent(self.nodeId, "text:" + self.b64(text)) }
                }
            }
            textReq.recognitionLevel = .fast
            requests.append(textReq)
        }

        try? handler.perform(requests)
        busy = false
    }

    private nonisolated func b64(_ s: String) -> String {
        Data(s.utf8).base64EncodedString()
    }
}

// MARK: - The preview view (UIViewRepresentable wrapping the AVCaptureVideoPreviewLayer)

final class PreviewUIView: UIView {
    override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
    var previewLayer: AVCaptureVideoPreviewLayer { layer as! AVCaptureVideoPreviewLayer }
}

struct CameraPreview: UIViewRepresentable {
    let controller: CameraController
    let onTapFocus: (CGPoint) -> Void

    func makeUIView(context: Context) -> PreviewUIView {
        let view = PreviewUIView()
        view.previewLayer.session = controller.session
        view.previewLayer.videoGravity = .resizeAspectFill
        let tap = UITapGestureRecognizer(target: context.coordinator, action: #selector(Coordinator.handleTap(_:)))
        view.addGestureRecognizer(tap)
        context.coordinator.view = view
        return view
    }

    func updateUIView(_ uiView: PreviewUIView, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onTapFocus) }

    final class Coordinator: NSObject {
        weak var view: PreviewUIView?
        let onTapFocus: (CGPoint) -> Void
        init(_ onTapFocus: @escaping (CGPoint) -> Void) { self.onTapFocus = onTapFocus }

        @objc func handleTap(_ g: UITapGestureRecognizer) {
            guard let view else { return }
            let p = g.location(in: view)
            // Normalize to 0–1 within the preview bounds.
            onTapFocus(CGPoint(x: p.x / max(view.bounds.width, 1), y: p.y / max(view.bounds.height, 1)))
        }
    }
}

// MARK: - The SwiftDotNet-facing view

struct SwiftDotNetCameraView: View {
    let props: SwiftDotNetProps
    @StateObject private var controller = CameraController()

    private var facing: String { props.string("facing") ?? "back" }
    private var analyzerSet: Set<String> {
        Set((props.string("analyzers") ?? "").split(separator: ",").map(String.init))
    }
    private var captureToken: Double { props.number("captureToken") ?? 0 }

    var body: some View {
        CameraPreview(controller: controller) { pt in
            emitEvent(props.id, "focus:\(pt.x),\(pt.y)")
        }
        .onAppear {
            controller.nodeId = props.id
            controller.configure(facing: facing, analyzers: analyzerSet)
        }
        .onChange(of: facing) { _, f in controller.configure(facing: f, analyzers: analyzerSet) }
        .onChange(of: captureToken) { _, _ in controller.capture() }
    }
}

// MARK: - Registration

/// Call once at app startup (after loading the SwiftDotNetBridge) to render `CameraView` nodes via AVFoundation.
public func registerSwiftDotNetCameraRenderer() {
    swiftDotNetRegisterRenderer("CameraView") { props in AnyView(SwiftDotNetCameraView(props: props)) }
}

/// C entry point so managed code can register the camera renderer at startup (resolved via dlsym / "__Internal").
@_cdecl("swiftdotnet_register_camera")
public func swiftdotnet_register_camera() {
    registerSwiftDotNetCameraRenderer()
}

#endif
