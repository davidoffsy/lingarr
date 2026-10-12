<template>
    <div class="flex w-full items-center gap-2">
        <div class="relative grow select-none" :title="title">
            <div
                class="h-2 w-full select-none overflow-hidden rounded-full bg-secondary brightness-125">
                <div
                    class="bg-linear-to-r h-full select-none rounded-full from-blue-500 to-purple-500 transition-all duration-1000 ease-out"
                    :class="indeterminate ? 'w-1/3 animate-pulse' : ''"
                    :style="indeterminate ? undefined : { width: `${progress}%` }"></div>
            </div>
        </div>
        <span v-if="label" class="text-primary-content/60 shrink-0 text-xs">{{ label }}</span>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
    progress: number
    label?: string
    indeterminate?: boolean
}>()

const title = computed(() =>
    props.indeterminate ? props.label || 'Extracting' : `${props.progress}%`
)
</script>
