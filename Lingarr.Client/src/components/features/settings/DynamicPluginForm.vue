<template>
    <div class="flex flex-col space-y-3">
        <div
            v-if="manifest.description"
            class="text-secondary-content/80 text-xs"
            v-html="manifest.description" />
        <div
            v-if="manifest.settings.length === 0 && !manifest.description"
            class="text-sm opacity-60">
            This provider has no configurable settings.
        </div>

        <PluginField
            v-for="field in connectionFields"
            :key="field.key"
            :field="field"
            @save="emit('save')" />

        <div v-if="canTestConnection" class="flex flex-col gap-2 pt-1">
            <ButtonComponent
                variant="accent"
                size="sm"
                :loading="testing"
                @click="testConnection">
                Test connection
            </ButtonComponent>
            <p class="text-secondary-content/60 text-xs">
                Loads the model list. This does not send a translation.
            </p>
            <p
                v-if="testMessage"
                class="text-sm"
                :class="testSuccess ? 'text-green-600' : 'text-red-500'">
                {{ testMessage }}
            </p>
        </div>

        <PluginField
            v-for="field in modelFields"
            :key="field.key"
            :field="field"
            @save="emit('save')" />
    </div>
</template>

<script setup lang="ts">
import { computed, nextTick, provide, ref } from 'vue'
import { IPluginManifest, LabelValue, PLUGIN_SETTING_TYPE } from '@/ts'
import services from '@/services'
import { useSettingStore } from '@/store/setting'
import PluginField from '@/components/features/settings/PluginField.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'

const props = defineProps<{
    manifest: IPluginManifest
}>()

const emit = defineEmits(['save'])

const settingsStore = useSettingStore()
const testing = ref(false)
const testSuccess = ref(false)
const testMessage = ref('')
const testedModelOptions = ref<LabelValue[]>([])

const canTestConnection = computed(() => Boolean(props.manifest.supportsConnectionTest))

provide('testedModelOptions', testedModelOptions)
provide('saveImmediately', canTestConnection.value)

const connectionFields = computed(() =>
    props.manifest.settings.filter((field) => field.type !== PLUGIN_SETTING_TYPE.REMOTE_DROPDOWN)
)
const modelFields = computed(() =>
    props.manifest.settings.filter((field) => field.type === PLUGIN_SETTING_TYPE.REMOTE_DROPDOWN)
)

async function testConnection(): Promise<void> {
    testing.value = true
    testMessage.value = ''
    try {
        await nextTick()
        await settingsStore.waitForPendingSaves()
        const result = await services.plugin.testConnection(props.manifest.provider)
        testSuccess.value = result.success
        testMessage.value = result.message
        testedModelOptions.value = result.options ?? []
    } catch (error) {
        console.error('Failed to test connection', error)
        testSuccess.value = false
        testMessage.value = 'Error testing connection. Please try again.'
        testedModelOptions.value = []
    } finally {
        testing.value = false
    }
}
</script>
