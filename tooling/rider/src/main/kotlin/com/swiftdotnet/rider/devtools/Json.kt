package com.swiftdotnet.rider.devtools

/**
 * A minimal JSON reader for the patch stream.
 *
 * Hand-written rather than pulling in a library, for the same reason `NodeJson.cs` is hand-written on the
 * other end: the shape is small and fixed, and the plugin's dependencies are a support burden — anything
 * bundled with the IDE can move between releases, and anything shaded makes the plugin bigger than the
 * feature deserves.
 */
sealed interface JsonValue {
    data class JsonObject(val members: Map<String, JsonValue>) : JsonValue {
        operator fun get(key: String): JsonValue? = members[key]
        fun string(key: String): String? = (members[key] as? JsonString)?.value
        fun array(key: String): List<JsonValue> = (members[key] as? JsonArray)?.items.orEmpty()
        fun obj(key: String): JsonObject? = members[key] as? JsonObject
    }

    data class JsonArray(val items: List<JsonValue>) : JsonValue
    data class JsonString(val value: String) : JsonValue
    data class JsonNumber(val value: Double) : JsonValue
    data class JsonBool(val value: Boolean) : JsonValue
    data object JsonNull : JsonValue

    /** Rendering a value back to a short display string for the inspector's property table. */
    fun display(): String = when (this) {
        is JsonString -> value
        is JsonNumber -> if (value == value.toLong().toDouble()) value.toLong().toString() else value.toString()
        is JsonBool -> value.toString()
        JsonNull -> "null"
        is JsonArray -> items.joinToString(", ", "[", "]") { it.display() }
        is JsonObject -> members.entries.joinToString(", ", "{", "}") { "${it.key}: ${it.value.display()}" }
    }
}

object Json {

    fun parse(text: String): JsonValue = Parser(text).run {
        val value = parseValue()
        skipWhitespace()
        value
    }

    /** Returns null instead of throwing — the inspector must never take the IDE down over a bad frame. */
    fun parseOrNull(text: String): JsonValue? = try {
        parse(text)
    } catch (_: Exception) {
        null
    }

    private class Parser(private val text: String) {
        private var i = 0

        fun parseValue(): JsonValue {
            skipWhitespace()
            if (i >= text.length) error("Unexpected end of JSON")
            return when (text[i]) {
                '{' -> parseObject()
                '[' -> parseArray()
                '"' -> JsonValue.JsonString(parseString())
                't' -> literal("true", JsonValue.JsonBool(true))
                'f' -> literal("false", JsonValue.JsonBool(false))
                'n' -> literal("null", JsonValue.JsonNull)
                else -> parseNumber()
            }
        }

        private fun parseObject(): JsonValue.JsonObject {
            expect('{')
            // LinkedHashMap: the inspector shows props in the order the framework emitted them, which is
            // the order they were written in the DSL. Sorting them would lose that.
            val members = LinkedHashMap<String, JsonValue>()
            skipWhitespace()
            if (peek() == '}') { i++; return JsonValue.JsonObject(members) }

            while (true) {
                skipWhitespace()
                val key = parseString()
                skipWhitespace()
                expect(':')
                members[key] = parseValue()
                skipWhitespace()
                when (val c = next()) {
                    ',' -> continue
                    '}' -> return JsonValue.JsonObject(members)
                    else -> error("Expected ',' or '}' in object, got '$c'")
                }
            }
        }

        private fun parseArray(): JsonValue.JsonArray {
            expect('[')
            val items = mutableListOf<JsonValue>()
            skipWhitespace()
            if (peek() == ']') { i++; return JsonValue.JsonArray(items) }

            while (true) {
                items += parseValue()
                skipWhitespace()
                when (val c = next()) {
                    ',' -> continue
                    ']' -> return JsonValue.JsonArray(items)
                    else -> error("Expected ',' or ']' in array, got '$c'")
                }
            }
        }

        private fun parseString(): String {
            expect('"')
            val sb = StringBuilder()
            while (i < text.length) {
                when (val c = text[i++]) {
                    '"' -> return sb.toString()
                    '\\' -> {
                        when (val escaped = text[i++]) {
                            '"', '\\', '/' -> sb.append(escaped)
                            'b' -> sb.append('\b')
                            // Kotlin has no '\f' escape, so the form feed is written by code point.
                            'f' -> sb.append('\u000C')
                            'n' -> sb.append('\n')
                            'r' -> sb.append('\r')
                            't' -> sb.append('\t')
                            'u' -> { sb.append(text.substring(i, i + 4).toInt(16).toChar()); i += 4 }
                            else -> error("Bad escape '\\$escaped'")
                        }
                    }
                    else -> sb.append(c)
                }
            }
            error("Unterminated string")
        }

        private fun parseNumber(): JsonValue.JsonNumber {
            val start = i
            if (peek() == '-') i++
            while (i < text.length && (text[i].isDigit() || text[i] in ".eE+-")) i++
            val slice = text.substring(start, i)
            return JsonValue.JsonNumber(slice.toDoubleOrNull() ?: error("Bad number '$slice'"))
        }

        private fun literal(word: String, value: JsonValue): JsonValue {
            require(text.startsWith(word, i)) { "Expected '$word'" }
            i += word.length
            return value
        }

        fun skipWhitespace() {
            while (i < text.length && text[i].isWhitespace()) i++
        }

        private fun peek(): Char? = text.getOrNull(i)
        private fun next(): Char = text.getOrElse(i++) { error("Unexpected end of JSON") }
        private fun expect(c: Char) {
            if (next() != c) error("Expected '$c'")
        }
    }
}
