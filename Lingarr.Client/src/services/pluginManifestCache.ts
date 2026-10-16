import services from '@/services'
import { IPluginManifest } from '@/ts'

const pendingManifests = new Map<string, Promise<IPluginManifest | null>>()

/**
 * Loads a provider manifest once per page session and shares the promise with every caller, so
 * several translation chain rows of the same provider do not fetch it again. Failures resolve to
 * `null` and are not cached, letting a later attempt retry.
 */
export function getCachedPluginManifest(provider: string): Promise<IPluginManifest | null> {
    const key = provider.trim().toLowerCase()
    const cached = pendingManifests.get(key)
    if (cached) {
        return cached
    }

    const pending = services.plugin
        .getManifest(provider)
        .catch((error: unknown): IPluginManifest | null => {
            console.error(`Failed to load the ${provider} plugin manifest`, error)
            pendingManifests.delete(key)
            return null
        })

    pendingManifests.set(key, pending)
    return pending
}
