import { onUnmounted, reactive } from 'vue'
import { MediaType } from '@/ts'
import useTranslationRequestStore from '@/store/translationRequest'

const FAST_REFRESH_MS = 2000

export function useExtractBadgeRefresh(mediaType: MediaType, onComplete: () => void) {
    const translationRequestStore = useTranslationRequestStore()
    const pending: Record<number, boolean> = reactive({})
    const timers = new Map<number, ReturnType<typeof setTimeout>>()

    const isActive = (mediaId: number) =>
        translationRequestStore.activeTranslations.some(
            (item) => item.mediaId === mediaId && item.mediaType === mediaType
        )

    const finish = (mediaId: number) => {
        const timer = timers.get(mediaId)
        if (timer) {
            clearTimeout(timer)
            timers.delete(mediaId)
        }
        if (!(mediaId in pending)) {
            return
        }
        delete pending[mediaId]
        onComplete()
    }

    const onQueued = (mediaId: number) => {
        pending[mediaId] = isActive(mediaId)
        const existing = timers.get(mediaId)
        if (existing) {
            clearTimeout(existing)
        }
        timers.set(
            mediaId,
            setTimeout(() => {
                timers.delete(mediaId)
                if (!(mediaId in pending)) {
                    return
                }
                if (!isActive(mediaId)) {
                    finish(mediaId)
                }
            }, FAST_REFRESH_MS)
        )
    }

    const stop = translationRequestStore.$subscribe(() => {
        for (const mediaId of Object.keys(pending).map(Number)) {
            if (isActive(mediaId)) {
                pending[mediaId] = true
                continue
            }
            if (pending[mediaId]) {
                finish(mediaId)
            }
        }
    })

    onUnmounted(() => {
        stop()
        for (const timer of timers.values()) {
            clearTimeout(timer)
        }
        timers.clear()
    })

    return { onQueued }
}

export default useExtractBadgeRefresh
