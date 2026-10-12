<template>
    <Teleport to="body">
        <Transition
            enter-active-class="transition-all duration-300 ease-in-out"
            leave-active-class="transition-all duration-300 ease-in-out"
            enter-from-class="opacity-0"
            leave-to-class="opacity-0">
            <div
                v-if="isOpen"
                class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
                @click="close">
                <div
                    class="flex max-h-[80vh] w-full max-w-lg flex-col rounded-lg border border-accent bg-primary p-4 text-primary-content shadow-lg"
                    @click.stop>
                    <div class="mb-3 flex items-start justify-between gap-3">
                        <div>
                            <h2 class="text-base font-semibold">提取内嵌字幕</h2>
                            <p v-if="title" class="text-primary-content/60 mt-1 text-sm">
                                {{ title }}
                            </p>
                            <p v-if="result?.fileName" class="text-primary-content/50 mt-1 text-xs">
                                {{ result.fileName }}
                            </p>
                        </div>
                        <ButtonComponent size="xs" variant="ghost" @click="close">
                            Close
                        </ButtonComponent>
                    </div>

                    <div class="shell-scroll min-h-32 grow overflow-auto">
                        <div v-if="loading" class="flex flex-col items-center justify-center gap-3 py-10">
                            <LoaderCircleIcon class="h-8 w-8 animate-spin text-accent" />
                            <p class="text-sm text-accent-content">正在读取字幕轨</p>
                        </div>

                        <div v-else class="space-y-4">
                            <StatusMessage
                                v-if="statusMessage"
                                :message="statusMessage"
                                :type="statusType"
                                show-icon />

                            <div v-if="tracks.length" class="space-y-2">
                                <button
                                    v-for="track in tracks"
                                    :key="track.index"
                                    type="button"
                                    class="flex w-full flex-col rounded-md border border-accent/60 bg-secondary px-3 py-2 text-left text-sm"
                                    :class="
                                        selectedIndex === track.index
                                            ? 'border-accent ring-1 ring-accent'
                                            : 'hover:border-accent'
                                    "
                                    @click="selectedIndex = track.index">
                                    <div class="flex flex-wrap items-center gap-2">
                                        <BadgeComponent>
                                            {{ track.language.toUpperCase() }}
                                            <span
                                                v-if="track.caption"
                                                class="text-primary-content/50">
                                                - {{ track.caption.toUpperCase() }}
                                            </span>
                                        </BadgeComponent>
                                        <span class="text-primary-content/70 text-xs">
                                            {{ track.codecName }}
                                            {{ track.extension }}
                                        </span>
                                        <span
                                            v-if="track.isDefault"
                                            class="text-primary-content/50 text-xs">
                                            default
                                        </span>
                                        <span
                                            v-if="track.isForced"
                                            class="text-primary-content/50 text-xs">
                                            forced
                                        </span>
                                        <span
                                            v-if="track.isHearingImpaired"
                                            class="text-primary-content/50 text-xs">
                                            sdh
                                        </span>
                                        <span
                                            v-if="track.sidecarExists"
                                            class="text-xs text-green-400">
                                            已有外挂
                                        </span>
                                    </div>
                                    <p
                                        v-if="track.title"
                                        class="text-primary-content/60 mt-1 truncate text-xs">
                                        {{ track.title }}
                                    </p>
                                </button>
                            </div>

                            <div v-if="skipped.length" class="space-y-2">
                                <h3 class="text-primary-content/50 text-xs font-semibold tracking-wide uppercase">
                                    无法提取
                                </h3>
                                <div
                                    v-for="track in skipped"
                                    :key="`skipped-${track.index}`"
                                    class="rounded-md border border-accent/30 bg-secondary/40 px-3 py-2 text-sm text-primary-content/40">
                                    <span>#{{ track.index }}</span>
                                    <span class="ml-2">{{ track.codecName || 'unknown codec' }}</span>
                                    <span class="ml-2 text-xs">{{ track.reason }}</span>
                                </div>
                            </div>

                            <div v-if="selectedTrack" class="space-y-3">
                                <SelectComponent
                                    v-if="needsSourceLanguage"
                                    label="源语言"
                                    size="sm"
                                    placeholder="选择源语言"
                                    :options="sourceLanguageOptions"
                                    :selected="sourceLanguage"
                                    @update:selected="sourceLanguage = $event" />
                                <SelectComponent
                                    label="目标语言"
                                    size="sm"
                                    placeholder="选择目标语言"
                                    :options="targetLanguageOptions"
                                    :selected="targetLanguage"
                                    @update:selected="targetLanguage = $event" />
                            </div>
                        </div>
                    </div>

                    <p class="text-primary-content/50 mt-3 text-xs">
                        提取会完整读取视频文件。文件在云盘上时可能很慢。
                    </p>

                    <div class="mt-3 flex justify-end gap-2">
                        <ButtonComponent v-if="loading" size="sm" variant="ghost" @click="cancelProbe">
                            Cancel
                        </ButtonComponent>
                        <ButtonComponent v-else size="sm" variant="ghost" @click="close">
                            Close
                        </ButtonComponent>
                        <ButtonComponent
                            size="sm"
                            variant="accent"
                            :disabled="!canTranslate"
                            :loading="submitting"
                            @click="translate">
                            翻译
                        </ButtonComponent>
                    </div>
                </div>
            </div>
        </Transition>
    </Teleport>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
    apiErrorMessage,
    IEmbeddedSubtitleTrack,
    ILanguage,
    IProbeEmbeddedResponse,
    ISelectOption,
    ISkippedEmbeddedTrack,
    MediaType,
    SETTINGS
} from '@/ts'
import services from '@/services'
import { useSettingStore } from '@/store/setting'
import useTranslationRequestStore from '@/store/translationRequest'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import StatusMessage from '@/components/common/StatusMessage.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'

const props = defineProps<{
    isOpen: boolean
    mediaId: number | null
    mediaType: MediaType
    title?: string
}>()

const emit = defineEmits<{
    (e: 'close'): void
    (e: 'queued', jobId: number): void
}>()

const router = useRouter()
const settingsStore = useSettingStore()
const translationRequestStore = useTranslationRequestStore()

const loading = ref(false)
const submitting = ref(false)
const result = ref<IProbeEmbeddedResponse | null>(null)
const errorMessage = ref<string | null>(null)
const selectedIndex = ref<number | null>(null)
const sourceLanguage = ref('')
const targetLanguage = ref('')
let abortController: AbortController | null = null

const tracks = computed<IEmbeddedSubtitleTrack[]>(() => result.value?.tracks ?? [])
const skipped = computed<ISkippedEmbeddedTrack[]>(() => result.value?.skipped ?? [])
const selectedTrack = computed(
    () => tracks.value.find((track) => track.index === selectedIndex.value) ?? null
)

const needsSourceLanguage = computed(() => {
    const language = selectedTrack.value?.language
    return language === 'und' || language === 'unknown'
})

const targetLanguages = computed(
    () => (settingsStore.getSetting('target_languages') as ILanguage[]) ?? []
)
const sourceLanguages = computed(
    () => (settingsStore.getSetting(SETTINGS.SOURCE_LANGUAGES) as ILanguage[]) ?? []
)

const targetLanguageOptions = computed<ISelectOption[]>(() =>
    targetLanguages.value.map((language) => ({
        value: language.code,
        label: language.name
    }))
)

const sourceLanguageOptions = computed<ISelectOption[]>(() =>
    sourceLanguages.value.map((language) => ({
        value: language.code,
        label: language.name
    }))
)

const navigateToDetails = computed(
    () => settingsStore.getSetting(SETTINGS.NAVIGATE_TO_DETAILS_ON_REQUEST) === 'true'
)

const canTranslate = computed(() => {
    if (loading.value || submitting.value || !selectedTrack.value || !targetLanguage.value) {
        return false
    }
    if (needsSourceLanguage.value && !sourceLanguage.value) {
        return false
    }
    return true
})

const statusMessage = computed(() => {
    if (errorMessage.value) {
        return errorMessage.value
    }
    if (!result.value) {
        return null
    }
    if (!result.value.found) {
        return reasonMessage(result.value.reason)
    }
    if (!tracks.value.length && !skipped.value.length) {
        return '没有可提取的文本字幕轨。'
    }
    if (!tracks.value.length) {
        return '没有可提取的文本字幕轨。图像或其它不支持的轨列在下方。'
    }
    return null
})

const statusType = computed(() => (errorMessage.value ? 'error' : 'warning'))

const reasonMessage = (reason?: string) => {
    switch (reason) {
        case 'no_mkv':
            return '没有找到对应的 MKV 文件。'
        case 'no_ffprobe':
            return '服务器上没有 ffprobe，无法读取内嵌字幕轨。'
        case 'timeout':
            return '读取字幕轨超时。文件在云盘上时可能需要更长时间。'
        case 'probe_failed':
            return '无法读取字幕轨。'
        default:
            return '无法读取字幕轨。'
    }
}

const close = () => {
    cancelInFlight()
    emit('close')
}

const cancelProbe = () => {
    close()
}

const cancelInFlight = () => {
    abortController?.abort()
    abortController = null
    loading.value = false
}

const startProbe = async () => {
    if (props.mediaId == null) {
        return
    }

    cancelInFlight()
    abortController = new AbortController()
    loading.value = true
    submitting.value = false
    result.value = null
    errorMessage.value = null
    selectedIndex.value = null
    sourceLanguage.value = ''
    targetLanguage.value = ''

    try {
        result.value = await services.subtitle.probe(
            props.mediaId,
            props.mediaType,
            abortController.signal
        )
        const first = result.value.tracks?.[0]
        selectedIndex.value = first?.index ?? null
        targetLanguage.value = targetLanguages.value[0]?.code ?? ''
        sourceLanguage.value = sourceLanguages.value[0]?.code ?? ''
    } catch (error: unknown) {
        if (isCanceled(error)) {
            return
        }
        errorMessage.value = apiErrorMessage(error, '无法读取字幕轨。')
    } finally {
        loading.value = false
        abortController = null
    }
}

const translate = async () => {
    if (props.mediaId == null || !selectedTrack.value || !canTranslate.value) {
        return
    }

    submitting.value = true
    errorMessage.value = null

    try {
        const response = await services.subtitle.extractAndTranslate({
            mediaId: props.mediaId,
            mediaType: props.mediaType === 'Episode' ? 'Episode' : 'Movie',
            trackIndex: selectedTrack.value.index,
            sourceLanguage: needsSourceLanguage.value
                ? sourceLanguage.value
                : selectedTrack.value.language,
            targetLanguage: targetLanguage.value
        })
        await translationRequestStore.fetchActiveTranslations()
        emit('queued', response.jobId)
        if (navigateToDetails.value) {
            router.push({ name: 'translation-detail', params: { id: response.jobId } })
        }
        close()
    } catch (error: unknown) {
        errorMessage.value = apiErrorMessage(error, '无法创建翻译任务。')
    } finally {
        submitting.value = false
    }
}

const isCanceled = (error: unknown) => {
    if (!error || typeof error !== 'object') {
        return false
    }
    const candidate = error as { code?: string; name?: string }
    return candidate.code === 'ERR_CANCELED' || candidate.name === 'CanceledError'
}

watch(
    () => props.isOpen,
    (open) => {
        if (open) {
            startProbe()
        } else {
            cancelInFlight()
            result.value = null
            errorMessage.value = null
            selectedIndex.value = null
            sourceLanguage.value = ''
            targetLanguage.value = ''
            submitting.value = false
        }
    }
)
</script>
