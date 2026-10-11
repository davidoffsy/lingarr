<template>
    <CardComponent title="Subtitle">
        <template #description>
            Configure how subtitles are scanned, cleaned, and timed. Embedded MKV text tracks can
            be extracted as sidecars next to the video for Plex / Emby.
        </template>
        <template #content>
            <div class="flex flex-col space-y-4">
                <SaveNotification ref="saveNotification" />
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Scan embedded MKV subtitles:</span>
                    When enabled, automatic and bulk translation will probe this media's MKV for text
                    subtitle tracks (SRT / ASS / SSA). Image tracks such as PGS are skipped. Movie
                    and show lists are not probed. Default is off so existing libraries stay
                    unchanged.
                </div>
                <ToggleButton v-model="scanEmbeddedSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{ scanEmbeddedSubtitles == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Extract embedded source:</span>
                    Write the selected source track as a sidecar next to the video so Plex / Emby
                    can switch languages. Translation of embedded tracks always extracts this file
                    first. Results use
                    <code class="text-sm">{{ '{video}.{language}[.caption].srt|ass' }}</code>
                    in the same folder as the video.
                </div>
                <ToggleButton v-model="extractEmbeddedSource">
                    <span class="text-primary-content text-sm font-medium">
                        {{ extractEmbeddedSource == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        Skip translation if subtitles with caption tags exist:
                    </span>
                    Skip translation if a target subtitle with captions such as Hearing Impaired or
                    Forced already exists.
                </div>
                <ToggleButton v-model="ignoreCaptions">
                    <span class="text-primary-content text-sm font-medium">
                        {{ ignoreCaptions == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Fix overlapping subtitles:</span>
                    Automatically resolves overlapping subtitles by trimming the end time of the
                    earlier subtitle. This won't work on subtitles that are out of sync.
                    And in most cases it also caused subtitles to go out of sync.
                </div>
                <ToggleButton v-model="fixOverlappingSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{ fixOverlappingSubtitles == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Strip subtitle formatting:</span>
                    Enable this option to remove all formatting tags (e.g., italics, bold, color,
                    position) from (SRT) subtitles, resulting in plain text subtitles.
                </div>
                <ToggleButton v-model="stripSubtitleFormatting">
                    <span class="text-primary-content text-sm font-medium">
                        {{ stripSubtitleFormatting == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Preserve line breaks:</span>
                    Keep multi-line subtitles split across lines after translation instead of
                    merging them into a single line. AI services translate the whole subtitle in one
                    call; classic services (DeepL, Google, Libre, etc.) translate each line
                    separately.
                </div>
                <ToggleButton v-model="preserveLineBreaks">
                    <span class="text-primary-content text-sm font-medium">
                        {{ preserveLineBreaks == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Add translator info</span>
                    Add translator info at the beginning of subtitles.
                </div>
                <ToggleButton v-model="addTranslatorInfo">
                    <span class="text-primary-content text-sm font-medium">
                        {{ addTranslatorInfo == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">Remove language tag</span>
                    Remove language tags (e.g., ".en.") from file names.
                </div>
                <ToggleButton v-model="removeLanguageTag">
                    <span class="text-primary-content text-sm font-medium">
                        {{ removeLanguageTag == 'true' ? 'Enabled' : 'Disabled' }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-y-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">Use subtitle tagging</span>
                        Appends a custom segment to the subtitle filename however. This is not a
                        recognised subtitle standard and may cause issues with other apps.
                    </div>
                    <ToggleButton v-model="useSubtitleTagging">
                        <span class="text-primary-content text-sm font-medium">
                            {{ useSubtitleTagging == 'true' ? 'Enabled' : 'Disabled' }}
                        </span>
                    </ToggleButton>
                    <InputComponent
                        v-if="useSubtitleTagging == 'true'"
                        v-model="subtitleTag"
                        :validation-type="INPUT_VALIDATION_TYPE.STRING"
                        label="Subtitle tag"
                        @update:validation="(val) => (isValid.subtitleTag = val)" />
                </div>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { INPUT_VALIDATION_TYPE, SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'

import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isValid = reactive({
    subtitleTag: true
})

const scanEmbeddedSubtitles = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SCAN_EMBEDDED_SUBTITLES) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SCAN_EMBEDDED_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

const extractEmbeddedSource = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.EXTRACT_EMBEDDED_SOURCE) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.EXTRACT_EMBEDDED_SOURCE, newValue, true)
        saveNotification.value?.show()
    }
})

const ignoreCaptions = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.IGNORE_CAPTIONS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.IGNORE_CAPTIONS, newValue, true)
        saveNotification.value?.show()
    }
})

const fixOverlappingSubtitles = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

const stripSubtitleFormatting = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING, newValue, true)
        saveNotification.value?.show()
    }
})

const preserveLineBreaks = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.PRESERVE_LINE_BREAKS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.PRESERVE_LINE_BREAKS, newValue, true)
        saveNotification.value?.show()
    }
})

const addTranslatorInfo = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.ADD_TRANSLATOR_INFO) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.ADD_TRANSLATOR_INFO, newValue, true)
        saveNotification.value?.show()
    }
})

const removeLanguageTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.REMOVE_LANGUAGE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.REMOVE_LANGUAGE_TAG, newValue, true)
        saveNotification.value?.show()
    }
})

const useSubtitleTagging = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_SUBTITLE_TAGGING, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG, newValue, isValid.subtitleTag)
        saveNotification.value?.show()
    }
})
</script>
