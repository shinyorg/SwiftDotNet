package com.swiftdotnet.rider

import com.swiftdotnet.rider.devtools.DevToolsProtocol
import com.swiftdotnet.rider.msbuild.HeadDiscovery
import com.swiftdotnet.rider.run.SwiftDotNetRunConfiguration
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.nio.charset.StandardCharsets

/**
 * The wire formats that cross a language boundary. Both are places where "it looked right" is not good
 * enough, because the other side is written in C# and cannot be checked by the same compiler.
 */
class DevToolsProtocolTest {

    // ---- framing, against the exact bytes C# writes ---------------------------------------------

    @Test
    fun `a frame is written exactly as DevToolsProtocol cs writes it`() {
        // Byte-for-byte, because this is the contract with src/SwiftDotNet.DevTools/DevToolsProtocol.cs.
        // Asserting a round trip through our own reader would pass even if both ends drifted together.
        val out = ByteArrayOutputStream()

        DevToolsProtocol.write(out, DevToolsProtocol.PATCH, "{}")

        assertEquals("SDN1 patch 2\n{}", out.toString(StandardCharsets.UTF_8))
    }

    @Test
    fun `the length is in bytes, not characters`() {
        // "é" is two bytes in UTF-8. A character count here would desynchronise the stream on the first
        // non-ASCII string in anyone's UI — which is to say, immediately.
        val out = ByteArrayOutputStream()

        DevToolsProtocol.write(out, DevToolsProtocol.LOG, "é")

        assertEquals("SDN1 log 2\n", out.toString(StandardCharsets.UTF_8).substringBefore('é'))
    }

    @Test
    fun `reads a frame written in the C# format`() {
        val greeting = "backend=skia;protocol=1"
        val bytes = "SDN1 hello ${greeting.length}\n$greeting".toByteArray(StandardCharsets.UTF_8)

        val frame = DevToolsProtocol.read(ByteArrayInputStream(bytes))!!

        assertEquals("hello", frame.type)
        assertEquals(greeting, frame.text)
    }

    @Test
    fun `reads binary payloads containing newlines`() {
        val png = byteArrayOf(0x89.toByte(), 'P'.code.toByte(), 'N'.code.toByte(), 'G'.code.toByte(), 0x0A, 0x0A)
        val stream = ByteArrayOutputStream()
        stream.write("SDN1 frame ${png.size}\n".toByteArray(StandardCharsets.US_ASCII))
        stream.write(png)

        val frame = DevToolsProtocol.read(ByteArrayInputStream(stream.toByteArray()))!!

        assertEquals("frame", frame.type)
        assertTrue(png.contentEquals(frame.payload))
    }

    @Test
    fun `reads several frames back to back`() {
        val out = ByteArrayOutputStream()
        DevToolsProtocol.write(out, "log", "one")
        DevToolsProtocol.write(out, "log", "two")
        val input = ByteArrayInputStream(out.toByteArray())

        assertEquals("one", DevToolsProtocol.read(input)!!.text)
        assertEquals("two", DevToolsProtocol.read(input)!!.text)
        assertNull(DevToolsProtocol.read(input))
    }

    @Test(expected = IllegalArgumentException::class)
    fun `rejects a foreign header`() {
        DevToolsProtocol.read(ByteArrayInputStream("GET / HTTP/1.1\n".toByteArray()))
    }

    // ---- the MSBuild -getProperty document ------------------------------------------------------

    @Test
    fun `reads the property document dotnet msbuild prints`() {
        val output = """
            {
              "Properties": {
                "SwiftDotNetPlatform": "skia",
                "SwiftDotNetIsAppHead": "true",
                "TargetFramework": "net10.0",
                "TargetFrameworks": "",
                "OutputType": "Exe"
              }
            }
        """.trimIndent()

        val properties = HeadDiscovery.parseProperties(output)

        assertEquals("skia", properties["SwiftDotNetPlatform"])
        assertEquals("true", properties["SwiftDotNetIsAppHead"])
        assertEquals("net10.0", properties["TargetFramework"])
        assertEquals("", properties["TargetFrameworks"])
    }

    @Test
    fun `unescapes the backslashes MSBuild puts in Windows paths`() {
        // The one thing a naive reader gets wrong, and only on Windows, which is where nobody testing
        // this on a Mac would notice.
        val output = """{"Properties": {"TargetPath": "C:\\src\\app\\bin\\app.dll"}}"""

        assertEquals("C:\\src\\app\\bin\\app.dll", HeadDiscovery.parseProperties(output)["TargetPath"])
    }

    @Test
    fun `survives msbuild printing a warning before the json`() {
        val output = "warning: something happened\n{\"Properties\": {\"OutputType\": \"Library\"}}"

        assertEquals("Library", HeadDiscovery.parseProperties(output)["OutputType"])
    }

    @Test
    fun `returns nothing rather than throwing on unparseable output`() {
        assertTrue(HeadDiscovery.parseProperties("MSBUILD : error MSB1009").isEmpty())
        assertTrue(HeadDiscovery.parseProperties("").isEmpty())
    }

    // ---- the MSBuild property list --------------------------------------------------------------

    @Test
    fun `msbuild properties parse from the editor's semicolon syntax`() {
        val properties = SwiftDotNetRunConfiguration.parseProperties("Foo=Bar; Baz = Qux ;;Empty")

        assertEquals(mapOf("Foo" to "Bar", "Baz" to "Qux"), properties)
    }

    @Test
    fun `a property value may itself contain an equals sign`() {
        val properties = SwiftDotNetRunConfiguration.parseProperties("DefineConstants=A=1")

        assertEquals("A=1", properties["DefineConstants"])
    }

    @Test
    fun `a free port is a real one`() {
        val port = SwiftDotNetRunConfiguration.freePort()

        assertTrue("expected a bindable port, got $port", port in 1..65535)
    }
}
