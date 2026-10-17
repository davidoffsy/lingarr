<template>
    <CardComponent title="Webhook">
        <template #description>
            Configure download webhooks in Radarr and Sonarr to sync new media. Enable automatic
            translation below to translate its subtitles immediately. The URL includes a Lingarr-generated secret — do not use Lingarr's API
            key or the Radarr/Sonarr API keys. Radarr and Sonarr share one secret; regenerating it
            invalidates both URLs.
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />
            <div class="flex flex-col space-y-2">
                <p v-if="loadError" class="text-sm text-red-500">
                    {{ loadError }}
                </p>
                <span v-else-if="loading">Loading…</span>
                <template v-else-if="!secret">
                    <p class="text-secondary-content/80 text-sm">
                        No webhook secret yet. Existing Radarr/Sonarr connections without a token
                        keep working until you generate one.
                    </p>
                    <ButtonComponent variant="primary" :loading="regenerating" @click="generateSecret">
                        Generate secret
                    </ButtonComponent>
                </template>
                <template v-else>
                    <span>
                        Go to
                        <b>Settings → Connect → Connections → +</b>
                        and paste this URL:
                    </span>
                    <span class="font-semibold">Radarr</span>
                    <code class="block break-all rounded bg-black/20 p-3 font-mono text-sm">
                        {{ radarrWebhookUrl }}
                    </code>
                    <span class="font-semibold">Sonarr</span>
                    <code class="block break-all rounded bg-black/20 p-3 font-mono text-sm">
                        {{ sonarrWebhookUrl }}
                    </code>
                    <div class="flex flex-wrap gap-2">
                        <ButtonComponent variant="secondary" @click="copyUrl(radarrWebhookUrl)">
                            {{ copied === 'radarr' ? 'Copied' : 'Copy Radarr' }}
                        </ButtonComponent>
                        <ButtonComponent variant="secondary" @click="copyUrl(sonarrWebhookUrl)">
                            {{ copied === 'sonarr' ? 'Copied' : 'Copy Sonarr' }}
                        </ButtonComponent>
                        <ButtonComponent variant="ghost" :loading="regenerating" @click="regenerateSecret">
                            Regenerate secret
                        </ButtonComponent>
                    </div>
                </template>
            </div>
            <div class="mt-4 flex flex-col space-y-3">
                <ToggleButton v-model="radarrWebhookAutoTranslate">
                    <span class="text-sm font-medium text-primary-content">
                        Automatically translate Radarr movie imports
                    </span>
                </ToggleButton>
                <ToggleButton v-model="sonarrWebhookAutoTranslate">
                    <span class="text-sm font-medium text-primary-content">
                        Automatically translate Sonarr episode imports
                    </span>
                </ToggleButton>
                <p class="text-sm text-secondary-content/80">
                    When off, that app's download webhooks only sync new media. Scheduled translation tasks are unaffected.
                </p>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import ButtonComponent from '@/components/common/ButtonComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import services from '@/services'
import { resolveUrl } from '@/utils/baseUrl'

const secret = ref('')
const loading = ref(true)
const regenerating = ref(false)
const loadError = ref('')
const copied = ref<'radarr' | 'sonarr' | ''>('')
const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const webhookAutoTranslate = (key: typeof SETTINGS.RADARR_WEBHOOK_AUTO_TRANSLATE | typeof SETTINGS.SONARR_WEBHOOK_AUTO_TRANSLATE) =>
    computed({
        get: (): string => (settingsStore.getSetting(key) as string) ?? 'true',
        set: (newValue: string): void => {
            settingsStore.updateSetting(key, newValue, true)
            saveNotification.value?.show()
        }
    })

const radarrWebhookAutoTranslate = webhookAutoTranslate(SETTINGS.RADARR_WEBHOOK_AUTO_TRANSLATE)
const sonarrWebhookAutoTranslate = webhookAutoTranslate(SETTINGS.SONARR_WEBHOOK_AUTO_TRANSLATE)

const radarrWebhookUrl = computed(() => webhookUrl('/api/webhook/radarr'))
const sonarrWebhookUrl = computed(() => webhookUrl('/api/webhook/sonarr'))

const webhookUrl = (path: string) => {
    if (!secret.value) {
        return ''
    }
    return `${resolveUrl(path)}?token=${secret.value}`
}

const loadSecret = async () => {
    loading.value = true
    loadError.value = ''
    try {
        const response = await services.webhook.getArrSecret()
        secret.value = response.secret ?? ''
    } catch {
        loadError.value = 'Failed to load webhook secret'
    } finally {
        loading.value = false
    }
}

const generateSecret = async () => {
    regenerating.value = true
    loadError.value = ''
    try {
        const response = await services.webhook.regenerateArrSecret()
        secret.value = response.secret
    } catch {
        loadError.value = 'Failed to generate webhook secret'
    } finally {
        regenerating.value = false
    }
}

const copyUrl = async (url: string) => {
    if (!url) {
        return
    }
    await navigator.clipboard.writeText(url)
    copied.value = url.includes('/radarr') ? 'radarr' : 'sonarr'
    window.setTimeout(() => {
        copied.value = ''
    }, 2000)
}

const regenerateSecret = async () => {
    if (
        !window.confirm(
            'Regenerating this secret invalidates existing Radarr and Sonarr webhook URLs. Continue?'
        )
    ) {
        return
    }

    await generateSecret()
}

onMounted(async () => {
    await loadSecret()
})
</script>
