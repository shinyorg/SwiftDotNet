package com.swiftdotnet.rider.devtools

import java.io.BufferedInputStream
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.charset.StandardCharsets
import java.util.concurrent.atomic.AtomicBoolean

/**
 * The IDE half of the dev-tools protocol implemented in `src/SwiftDotNet.DevTools/DevToolsProtocol.cs`.
 *
 * Frame: an ASCII header line `SDN1 <type> <length>\n`, then exactly `length` bytes. Length-prefixed
 * because the same socket carries patch JSON and PNG frames, and PNG bytes are full of newlines.
 */
data class DevToolsFrame(val type: String, val payload: ByteArray) {
    val text: String get() = String(payload, StandardCharsets.UTF_8)

    // ByteArray in a data class needs these spelled out, or equality is by reference and the tests lie.
    override fun equals(other: Any?): Boolean =
        this === other || (other is DevToolsFrame && type == other.type && payload.contentEquals(other.payload))

    override fun hashCode(): Int = 31 * type.hashCode() + payload.contentHashCode()

    override fun toString(): String = "$type[${payload.size}]"
}

object DevToolsProtocol {
    const val MAGIC = "SDN1"

    // Server → IDE.
    const val HELLO = "hello"
    const val PATCH = "patch"
    const val EVENT = "event"
    const val FRAME = "frame"
    const val LOG = "log"

    // IDE → app.
    const val PING = "ping"
    const val INPUT = "input"
    const val RESIZE = "resize"
    const val THEME = "theme"
    const val RELOAD = "reload"

    fun write(stream: OutputStream, type: String, payload: String = "") {
        val bytes = payload.toByteArray(StandardCharsets.UTF_8)
        stream.write("$MAGIC $type ${bytes.size}\n".toByteArray(StandardCharsets.US_ASCII))
        stream.write(bytes)
        stream.flush()
    }

    /** Reads one frame, or null at clean end of stream. */
    fun read(stream: InputStream): DevToolsFrame? {
        val header = readHeaderLine(stream) ?: return null
        val parts = header.split(' ')
        require(parts.size == 3 && parts[0] == MAGIC) { "Bad dev-tools frame header: '$header'" }

        val length = parts[2].toIntOrNull()
        require(length != null && length >= 0) { "Bad dev-tools frame length: '$header'" }

        val payload = ByteArray(length)
        var read = 0
        while (read < length) {
            // readNBytes/read returns what has arrived, not what was asked for. A 50 KB PNG frame
            // reliably arrives in pieces.
            val n = stream.read(payload, read, length - read)
            if (n <= 0) return null
            read += n
        }
        return DevToolsFrame(parts[1], payload)
    }

    private fun readHeaderLine(stream: InputStream): String? {
        val sb = StringBuilder(48)
        while (true) {
            val b = stream.read()
            if (b < 0) return if (sb.isEmpty()) null else error("Truncated dev-tools header")
            if (b == '\n'.code) return sb.toString()
            sb.append(b.toChar())
            check(sb.length <= 256) { "Dev-tools header line too long" }
        }
    }
}

/**
 * Connects to a running app's dev-tools port and pumps frames to a listener on a background thread.
 *
 * Retries the connect, because the IDE always wins the race: the tool window is ready before the app
 * it is trying to attach to has finished starting.
 */
class DevToolsClient(
    private val port: Int,
    private val host: String = "127.0.0.1",
    private val onFrame: (DevToolsFrame) -> Unit,
    private val onDisconnect: (Throwable?) -> Unit = {},
) : AutoCloseable {

    private val stopped = AtomicBoolean(false)
    private var socket: Socket? = null
    private var thread: Thread? = null

    fun start(connectTimeoutMillis: Int = 15_000) {
        thread = Thread({ run(connectTimeoutMillis) }, "SwiftDotNet dev-tools client").apply {
            isDaemon = true
            start()
        }
    }

    /** Send a command to the app. Silently dropped when not connected — commands are advisory. */
    fun send(type: String, payload: String = "") {
        val output = socket?.takeIf { it.isConnected && !it.isClosed }?.getOutputStream() ?: return
        try {
            synchronized(this) { DevToolsProtocol.write(output, type, payload) }
        } catch (_: Exception) {
            // The app went away between the check and the write. The read loop will notice.
        }
    }

    private fun run(connectTimeoutMillis: Int) {
        val deadline = System.currentTimeMillis() + connectTimeoutMillis
        var connected: Socket? = null

        while (!stopped.get() && System.currentTimeMillis() < deadline) {
            try {
                connected = Socket().apply {
                    tcpNoDelay = true
                    connect(InetSocketAddress(host, port), 1_000)
                }
                break
            } catch (_: Exception) {
                Thread.sleep(200)
            }
        }

        if (connected == null) {
            if (!stopped.get()) onDisconnect(IllegalStateException("no app listening on $host:$port"))
            return
        }

        socket = connected
        try {
            val input = BufferedInputStream(connected.getInputStream())
            while (!stopped.get()) {
                val frame = DevToolsProtocol.read(input) ?: break
                onFrame(frame)
            }
            if (!stopped.get()) onDisconnect(null)
        } catch (ex: Exception) {
            if (!stopped.get()) onDisconnect(ex)
        }
    }

    override fun close() {
        stopped.set(true)
        try { socket?.close() } catch (_: Exception) { }
        thread?.interrupt()
    }
}
