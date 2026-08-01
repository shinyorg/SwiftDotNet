package com.swiftdotnet.rider

import com.swiftdotnet.rider.devtools.Json
import com.swiftdotnet.rider.devtools.JsonValue
import com.swiftdotnet.rider.devtools.PatchModel
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Reconstructing the view tree from the patch stream — the thing that makes one inspector work for every
 * backend.
 *
 * The JSON here is the real wire format produced by `TreeDiffer.ToJson()` and `NodeJson.AppendNode`, not
 * an approximation of it: `{"ops":[…]}` with `replace`, `updateProps` and `setChildren`, and nodes of
 * `{id, type, props, modifiers, children}`.
 */
class PatchModelTest {

    private fun node(id: String, type: String, text: String? = null, children: String = "") = buildString {
        append("""{"id":"$id","type":"$type","props":{""")
        if (text != null) append(""""text":"$text"""")
        append("""},"modifiers":[],"children":[$children]}""")
    }

    @Test
    fun `replace installs a whole tree`() {
        val model = PatchModel()

        val changed = model.apply(
            """{"ops":[{"op":"replace","node":${node("0", "VStack", children = node("0.0", "Text", "Hi"))}}]}"""
        )

        assertTrue(changed)
        assertEquals("VStack", model.root?.type)
        assertEquals(1, model.root?.children?.size)
        assertEquals("Hi", model.root?.children?.first()?.props?.get("text")?.display())
        assertEquals(2, model.root?.count())
    }

    @Test
    fun `updateProps rewrites one node and leaves its siblings alone`() {
        val model = PatchModel()
        model.apply(
            """{"ops":[{"op":"replace","node":${
                node("0", "VStack", children = node("0.0", "Text", "before") + "," + node("0.1", "Text", "sibling"))
            }}]}"""
        )

        model.apply("""{"ops":[{"op":"updateProps","id":"0.0","props":{"text":"after"},"modifiers":[]}]}""")

        assertEquals("after", model.root?.find("0.0")?.props?.get("text")?.display())
        assertEquals("sibling", model.root?.find("0.1")?.props?.get("text")?.display())
    }

    @Test
    fun `setChildren replaces a child list`() {
        val model = PatchModel()
        model.apply("""{"ops":[{"op":"replace","node":${node("0", "VStack", children = node("0.0", "Text", "one"))}}]}""")

        model.apply(
            """{"ops":[{"op":"setChildren","id":"0","children":[${node("0.0", "Text", "a")},${node("0.1", "Text", "b")}]}]}"""
        )

        assertEquals(2, model.root?.children?.size)
        assertEquals("b", model.root?.find("0.1")?.props?.get("text")?.display())
    }

    @Test
    fun `a hot reload arrives as a full replace and is applied as one`() {
        // Invalidate() drops the diff baseline on purpose, so every reload is a replace of the root
        // rather than a diff against a tree the *old* code built. The inspector must follow that
        // rather than trying to merge.
        val model = PatchModel()
        model.apply("""{"ops":[{"op":"replace","node":${node("0", "VStack", children = node("0.0", "Text", "old"))}}]}""")

        model.apply("""{"ops":[{"op":"replace","node":${node("0", "ScrollView", children = node("0.0", "Text", "new"))}}]}""")

        assertEquals("ScrollView", model.root?.type)
        assertEquals("new", model.root?.find("0.0")?.props?.get("text")?.display())
        assertEquals(2, model.patchCount)
    }

    @Test
    fun `several ops in one patch all apply`() {
        val model = PatchModel()
        model.apply("""{"ops":[{"op":"replace","node":${node("0", "VStack", children = node("0.0", "Text", "x"))}}]}""")

        model.apply(
            """{"ops":[
                {"op":"updateProps","id":"0.0","props":{"text":"y"},"modifiers":[]},
                {"op":"setChildren","id":"0","children":[${node("0.0", "Text", "z")}]}
            ]}"""
        )

        assertEquals(2, model.lastOps.size)
        assertEquals("z", model.root?.find("0.0")?.props?.get("text")?.display())
    }

    @Test
    fun `modifiers survive the round trip and are readable`() {
        val model = PatchModel()
        model.apply(
            """{"ops":[{"op":"replace","node":{"id":"0","type":"Text","props":{"text":"Hi"},
               "modifiers":[{"type":"padding","value":20},{"type":"foregroundColor","value":"#FF0000"}],
               "children":[]}}]}"""
        )

        val modifiers = model.root?.modifiers.orEmpty()
        assertEquals(2, modifiers.size)
        assertEquals("padding", modifiers[0]["type"]?.display())
        assertEquals("20", modifiers[0]["value"]?.display())
    }

    @Test
    fun `an update for an unknown id leaves the tree intact`() {
        // Ordinary during startup: the inspector can attach after the first patch has gone out, so the
        // first thing it sees may reference a node it has never had.
        val model = PatchModel()
        model.apply("""{"ops":[{"op":"replace","node":${node("0", "VStack")}}]}""")

        model.apply("""{"ops":[{"op":"updateProps","id":"9.9","props":{"text":"nope"},"modifiers":[]}]}""")

        assertEquals("VStack", model.root?.type)
        assertNull(model.root?.find("9.9"))
    }

    @Test
    fun `malformed json is ignored rather than thrown`() {
        val model = PatchModel()

        assertEquals(false, model.apply("""{"ops":[{"op":"replace","node":{"id":"""))
        assertNull(model.root)
    }

    @Test
    fun `an empty patch changes nothing`() {
        val model = PatchModel()

        assertEquals(false, model.apply("""{"ops":[]}"""))
    }

    // ---- the JSON reader itself ----------------------------------------------------------------

    @Test
    fun `json reader handles escapes, unicode and nesting`() {
        val value = Json.parse("""{"a":"line\nbreak","b":"é","c":[1,-2.5,true,null],"d":{"e":{}}}""")

        val obj = value as JsonValue.JsonObject
        assertEquals("line\nbreak", obj.string("a"))
        assertEquals("é", obj.string("b"))
        assertEquals(4, obj.array("c").size)
        assertNotNull(obj.obj("d")?.obj("e"))
    }

    @Test
    fun `json reader keeps property order`() {
        // The inspector shows props in the order the DSL wrote them; sorting would lose that.
        val obj = Json.parse("""{"z":1,"a":2,"m":3}""") as JsonValue.JsonObject

        assertEquals(listOf("z", "a", "m"), obj.members.keys.toList())
    }

    @Test
    fun `json reader survives an escaped quote inside a string`() {
        val obj = Json.parse("""{"text":"she said \"hi\""}""") as JsonValue.JsonObject

        assertEquals("she said \"hi\"", obj.string("text"))
    }

    @Test
    fun `parseOrNull returns null instead of throwing`() {
        assertNull(Json.parseOrNull("{not json"))
    }
}
