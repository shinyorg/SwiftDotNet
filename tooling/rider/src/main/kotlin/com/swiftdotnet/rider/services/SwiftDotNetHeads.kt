package com.swiftdotnet.rider.services

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.diagnostic.logger
import com.intellij.openapi.project.Project
import com.swiftdotnet.rider.model.Head
import com.swiftdotnet.rider.model.HostOs
import com.swiftdotnet.rider.model.OsGate
import com.swiftdotnet.rider.model.Supported
import com.swiftdotnet.rider.msbuild.HeadDiscovery
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Caches the heads discovered in a solution.
 *
 * Discovery shells out to MSBuild once per project per target framework, which costs a second or two on
 * a cold SDK. That is fine once and intolerable every time the run-configuration dropdown opens, so the
 * answer is cached and refreshed explicitly.
 */
@Service(Service.Level.PROJECT)
class SwiftDotNetHeads(private val project: Project) {

    private val log = logger<SwiftDotNetHeads>()
    private val refreshing = AtomicBoolean(false)

    @Volatile
    private var cached: List<Head> = emptyList()

    @Volatile
    var lastRefreshFailed: Boolean = false
        private set

    val heads: List<Head> get() = cached

    /** Heads this operating system can actually build, in the order they should be offered. */
    fun runnableHeads(host: HostOs = HostOs.current()): List<Head> =
        cached.filter { OsGate.supports(host, it) is Supported.Yes }

    /** The rest, with the reason — shown greyed rather than hidden. */
    fun unavailableHeads(host: HostOs = HostOs.current()): List<Pair<Head, String>> =
        cached.mapNotNull { head ->
            (OsGate.supports(host, head) as? Supported.No)?.let { head to it.reason }
        }

    fun findById(id: String): Head? = cached.firstOrNull { it.id == id }

    /**
     * Rediscover in the background. [onDone] runs on a pooled thread, not the EDT — callers that touch
     * UI must marshal themselves.
     */
    fun refresh(onDone: (List<Head>) -> Unit = {}) {
        if (!refreshing.compareAndSet(false, true)) return

        ApplicationManager.getApplication().executeOnPooledThread {
            try {
                val root = project.basePath?.let(::File)
                cached = if (root == null || !root.isDirectory) emptyList()
                else HeadDiscovery().discover(root)
                lastRefreshFailed = false
                log.info("SwiftDotNet: discovered ${cached.size} head(s)")
            } catch (ex: Exception) {
                lastRefreshFailed = true
                log.warn("SwiftDotNet: head discovery failed", ex)
            } finally {
                refreshing.set(false)
                onDone(cached)
            }
        }
    }

    /**
     * Replace the cache without shelling out to MSBuild.
     *
     * Exists so a test can drive the run configuration with known heads. Discovery costs a second per
     * project per TFM and needs a real solution on disk; neither belongs in a test of the launch path.
     */
    @org.jetbrains.annotations.TestOnly
    fun seed(heads: List<Head>) {
        cached = heads
        lastRefreshFailed = false
    }

    companion object {
        fun getInstance(project: Project): SwiftDotNetHeads = project.service()
    }
}
