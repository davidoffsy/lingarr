<template>
    <div v-if="entries.length" class="mt-4">
        <div class="mb-4">
            <h3 class="text-primary-content text-sm font-medium">{{ title }}</h3>
            <p v-if="caption" class="text-primary-content/70 mt-1 text-xs">{{ caption }}</p>
        </div>
        <div class="grid grid-cols-2 gap-2 xl:grid-cols-3">
            <div v-for="[source, count] in entries" :key="source" class="bg-primary rounded-sm p-2">
                <h4 class="text-primary-content/70 truncate text-xs font-medium" :title="source">
                    {{ formatSource(source) }}
                </h4>
                <p class="text-primary-content text-lg font-bold">{{ formatNumber(count) }}</p>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { splitSourceDisplayName } from '@/ts'

const props = withDefaults(
    defineProps<{
        /** Line or file counts per source, keyed by backend `TranslationSource.DisplayName`. */
        counts?: Record<string, number>
        title?: string
        caption?: string
    }>(),
    {
        title: 'Lines by Source'
    }
)

const entries = computed(() => {
    if (!props.counts) {
        return []
    }

    return Object.entries(props.counts)
})

const formatSource = (source: string): string => {
    const { provider, model } = splitSourceDisplayName(source)
    const label = provider ? provider.charAt(0).toUpperCase() + provider.slice(1) : provider
    return model ? `${label} (${model})` : label
}

const formatNumber = (num: number): string => {
    return num ? new Intl.NumberFormat().format(num) : '0'
}
</script>
